using System.Text;
using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// One run (ADR 0003 §10–§12): works through the candidates until five scraper pull requests are open; a product the
/// shops confirm becomes a draft box, or new article numbers of a box already in the table. With <c>autoMerge</c> each
/// pull request is merged at once and the next one is built on the merged table (ADR 0004); entries get the ids of the
/// one root entry their name names in <c>catalogues</c>. A product a shop did not answer is asked once more at the end of
/// the run (ADR 0003 Addendum ARMAM-595). Before all that, each run holds the ids the table already carries against
/// <c>catalogues</c> and may open one draft <see cref="StaleIdsBranch"/> that it never merges (ADR 0004 Addendum ARMAM-597);
/// before that it gives the gaps of service boxes the ids their names now name (<see cref="BackfillIdsBranch"/>, merged like a
/// box), after it cuts names read before the reader's fixes (<see cref="FixNamesBranch"/>, never merged) (ADR 0004 Addendum
/// ARMAM-598).
/// What the maintainer should hear of goes to <c>notice</c> as well as to the log: a refused proposal or merge, orphaned
/// ids and their draft, ids of a reading that could not be checked; the run mails them at its end (ADR 0003 Addendum
/// ARMAM-600).
/// </summary>
public sealed class Runner(
    IRepository repository,
    IReadOnlyList<ISource> sources,
    IFetcher fetcher,
    ScanState state,
    Action<string> log,
    Action? persist = null,
    bool autoMerge = false,
    CatalogueIndex? catalogues = null,
    Action<string>? notice = null)
{
    public const int MaxOpen = 5;

    /// <summary>The one branch of the draft that removes orphaned ids (ADR 0004 Addendum ARMAM-597).</summary>
    public const string StaleIdsBranch = GitHubRepository.BranchPrefix + "stale-ids";

    /// <summary>The one branch that gives gaps their ids (ADR 0004 Addendum ARMAM-598).</summary>
    public const string BackfillIdsBranch = GitHubRepository.BranchPrefix + "backfill-ids";

    /// <summary>The one branch of the draft that repairs names read before the reader's fixes (ADR 0004 Addendum ARMAM-598).</summary>
    public const string FixNamesBranch = GitHubRepository.BranchPrefix + "fix-names";

    private readonly IReadOnlySet<string> _hosts = sources.Select(s => s.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task RunAsync(
        IReadOnlyList<Product> products, DateTimeOffset now, CancellationToken cancellationToken, string? onlyCode = null)
    {
        var open = new HashSet<string>(await repository.OpenScraperBranchesAsync(cancellationToken), StringComparer.Ordinal);
        var (text, sha) = await repository.GetTableAsync(cancellationToken);

        // First, so a run stopped later has still checked; with five open it only reports (ADR 0004 Addendum ARMAM-597).
        // The backfill is merged before, so the check and both drafts are built on the table it leaves (Addendum ARMAM-598).
        if (catalogues is not null)
        {
            (text, sha) = await BackfillAsync(catalogues, open, text, sha, cancellationToken);
            await CheckIdsAsync(catalogues, open, text, sha, cancellationToken);
            await RepairNamesAsync(open, text, sha, cancellationToken);
        }

        if (open.Count >= MaxOpen)
        {
            log($"{open.Count} scraper pull requests are open; nothing to do");
            return;
        }

        var run = new RunState(
            open,
            products.Where(p => p.Ean is not null)
                .GroupBy(p => p.Ean!)
                .ToDictionary(g => g.Key, g => g.First().Code, StringComparer.Ordinal),
            text,
            sha);

        // A product whose shop throttled or did not answer is asked once more at the end of the run, when the shop may
        // answer again, instead of waiting three days (ADR 0003 Addendum ARMAM-595).
        var later = new List<Product>();
        foreach (var product in ProductCatalogue.Candidates(products, run.Known))
        {
            if (open.Count >= MaxOpen)
            {
                log($"{MaxOpen} scraper pull requests are open; stopping");
                return;
            }

            if ((onlyCode is not null && product.Code != onlyCode) || (onlyCode is null && !state.IsDue(product.Code, now)))
            {
                continue;
            }

            if (await ProcessAsync(product, run, now, cancellationToken) == Outcome.AskAgain)
            {
                later.Add(product);
            }
        }

        if (later.Count == 0 || open.Count >= MaxOpen)
        {
            return;
        }

        log($"asking {later.Count} product(s) again that a shop did not answer");
        var unanswered = 0;
        foreach (var product in later)
        {
            if (open.Count >= MaxOpen)
            {
                log($"{MaxOpen} scraper pull requests are open; stopping");
                return;
            }

            if (await ProcessAsync(product, run, now, cancellationToken) != Outcome.Answered)
            {
                unanswered++;
            }
        }

        log($"catch-up: {later.Count} asked again, {unanswered} still without an answer");
    }

    private async Task<Outcome> ProcessAsync(Product product, RunState run, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (listings, failed, retryable) = await FindAsync(product, cancellationToken);
        log($"{product.Code} {Text.Clean(product.Name)}: {listings.Count} shop(s) confirm it, {failed} did not answer");

        // A shop that did not answer may hold the missing evidence: the product comes back in three days instead of
        // resting 30 (ADR 0003 §12). The mark stands before the catch-up, so a run stopped in between loses nothing.
        if (failed > 0 && listings.Count < 2)
        {
            Retry(product.Code, now);
            return retryable ? Outcome.AskAgain : Outcome.NotAnswered;
        }

        var codes = listings.SelectMany(l => l.VariantEans)
            .Select(e => run.CodeByEan.GetValueOrDefault(e))
            .OfType<string>()
            .Append(product.Code)
            .Where(c => ProductCatalogue.ArticleNumber().IsMatch(c))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        var knownCode = codes.FirstOrDefault(run.Known.Contains);
        var proposal = knownCode is null
            ? NewBox(product, codes, listings, run.Text)
            : LanguageVariants(knownCode, codes.Where(c => !run.Known.Contains(c)).ToList(), listings, run.Text);
        if (proposal is null)
        {
            Mark([product.Code], now);
            return Outcome.Answered;
        }

        if (run.Open.Contains(proposal.Branch))
        {
            log($"{proposal.Branch} is already open; skipped");
            Mark([product.Code], now);
            return Outcome.Answered;
        }

        int number;
        try
        {
            number = await repository.ProposeAsync(proposal, run.Sha, cancellationToken);
        }
        catch (GitHubException ex)
        {
            Notice($"{proposal.Branch} not proposed: {ex.Message}");
            Retry(product.Code, now);
            return Outcome.Answered;
        }

        Mark(codes, now);
        log($"proposed {proposal.Branch}: {proposal.Title}");
        if (await MergeAsync(proposal, number, run.Open, cancellationToken))
        {
            (run.Text, run.Sha) = await repository.GetTableAsync(cancellationToken);
        }

        return Outcome.Answered;
    }

    // With --auto-merge: merges the pull request and deletes its branch; true when merged. A pull request left open counts
    // like a draft.
    private async Task<bool> MergeAsync(Proposal proposal, int number, HashSet<string> open, CancellationToken cancellationToken)
    {
        if (!autoMerge)
        {
            open.Add(proposal.Branch);
            return false;
        }

        try
        {
            await repository.MergeAsync(number, cancellationToken);
        }
        catch (GitHubException ex)
        {
            // The pull request stays open and counts like a draft; the maintainer sees why in the journal.
            Notice($"{proposal.Branch} not merged: {ex.Message}");
            open.Add(proposal.Branch);
            return false;
        }

        log($"merged {proposal.Branch}");
        try
        {
            await repository.DeleteBranchAsync(proposal.Branch, cancellationToken);
        }
        catch (GitHubException ex)
        {
            // The merge stands; a branch left behind only blocks a later proposal of the same name.
            log($"{proposal.Branch} merged, branch not deleted: {ex.Message}");
        }

        return true;
    }

    // Gaps of service boxes whose names now name one root entry get their ids, in one pull request merged like a box
    // (ADR 0004 Addendum ARMAM-598); without --auto-merge a draft. Returns the table the rest of the run builds on.
    private async Task<(string Text, string Sha)> BackfillAsync(
        CatalogueIndex index, HashSet<string> open, string text, string sha, CancellationToken cancellationToken)
    {
        var table = Table.Read(text);
        var filled = IdBackfill.Fill(table, index, _hosts);
        log($"backfill: {filled.Count} gap(s) of service boxes get ids");
        if (filled.Count == 0 || !HasSlot(open, BackfillIdsBranch))
        {
            return (text, sha);
        }

        var body = new StringBuilder()
            .AppendLine("From the scraper service (ADR 0004 Addendum ARMAM-598): gaps of boxes the service wrote whose name now")
            .AppendLine("names one root entry of its reading, by the same hit that sets ids in a new box. Boxes written by hand keep")
            .AppendLine($"their gaps. {(autoMerge ? "Merged by the scraper service (`--auto-merge`)." : "Draft; the merge is the hand check.")}")
            .AppendLine()
            .AppendLine("| Box | Reading | Entry | Ids |")
            .AppendLine("|---|---|---|---|");
        foreach (var one in filled)
        {
            body.AppendLine($"| {Text.Code(one.Box)} | {Text.Code(one.Reading)} | {Text.Code(one.Name)} | " +
                $"{Text.Code(one.Ids.CatalogueId)} {Text.Code(one.Ids.EntryId)} |");
        }

        var proposal = new Proposal(BackfillIdsBranch, "data: give gaps the ids their names name", body.ToString(), Table.Write(table), Draft: !autoMerge);
        if (await ProposeFixedAsync(proposal, sha, cancellationToken) is not { } number)
        {
            return (text, sha);
        }

        log($"proposed {BackfillIdsBranch}: {filled.Count} gap(s)");
        return await MergeAsync(proposal, number, open, cancellationToken)
            ? await repository.GetTableAsync(cancellationToken)
            : (text, sha);
    }

    // Names the service read before the reader's fixes, cut or removed in a draft it never merges, also with --auto-merge:
    // a removed entry is data gone (ADR 0004 Addendum ARMAM-598). An upgrade set leaves whole (ADR 0003 Addendum ARMAM-610).
    private async Task RepairNamesAsync(HashSet<string> open, string text, string sha, CancellationToken cancellationToken)
    {
        var table = Table.Read(text);
        var repairs = NameRepair.Apply(table, _hosts, catalogues);
        if (repairs.Renamed.Count + repairs.Removed.Count + repairs.BoxesRemoved.Count == 0)
        {
            return;
        }

        log($"name repair: {repairs.Renamed.Count} renamed, {repairs.Removed.Count} removed, {repairs.BoxesRemoved.Count} box(es) removed");
        if (!HasSlot(open, FixNamesBranch))
        {
            return;
        }

        var body = new StringBuilder()
            .AppendLine("From the scraper service (ADR 0004 Addendum ARMAM-598): names read before the reader's fixes. A clause read")
            .AppendLine("into a name is cut; a name that is no model leaves its box; a box left without entries goes whole and comes")
            .AppendLine("back with the next run; an upgrade set (ADR 0003 Addendum ARMAM-610) goes whole and does not come back.")
            .AppendLine("A gap whose name now names the entry another line of its reading carries with the same count leaves")
            .AppendLine("(ADR 0004 Addendum ARMAM-707).")
            .AppendLine("Never merged by the service, also with `--auto-merge`: the merge is the hand check.")
            .AppendLine()
            .AppendLine("| Box | Reading | Entry | Becomes |")
            .AppendLine("|---|---|---|---|");
        foreach (var one in repairs.Renamed.Concat(repairs.Removed))
        {
            body.AppendLine($"| {Text.Code(one.Box)} | {Text.Code(one.Reading)} | {Text.Code(one.Name)} | " +
                $"{(one.NewName is { } name ? Text.Code(name) : "removed")} |");
        }

        foreach (var box in repairs.BoxesRemoved)
        {
            body.AppendLine().Append(ProductCatalogue.IsUpgradeSet(box)
                ? $"Upgrade set removed, not proposed again: {Text.Code(box)}"
                : $"Box removed, will be proposed again: {Text.Code(box)}");
        }

        var proposal = new Proposal(FixNamesBranch, "data: repair names read before the reader's fixes", body.ToString(), Table.Write(table), Draft: true);
        if (await ProposeFixedAsync(proposal, sha, cancellationToken) is not null)
        {
            open.Add(FixNamesBranch);
            Notice($"proposed {FixNamesBranch}: {repairs.Renamed.Count} renamed, {repairs.Removed.Count} removed, {repairs.BoxesRemoved.Count} box(es) removed");
        }
    }

    // A pull request of a fixed branch: skipped while one is open or no slot is free.
    private bool HasSlot(HashSet<string> open, string branch)
    {
        if (open.Contains(branch))
        {
            log($"{branch} is already open; skipped");
            return false;
        }

        if (open.Count >= MaxOpen)
        {
            Notice($"{open.Count} scraper pull requests are open; no slot for {branch}");
            return false;
        }

        return true;
    }

    // No pull request of the fixed branch is open, so a branch of its name is left from one merged or closed without
    // deleting it; the one fixed name would otherwise fail every later run. Deleted once, then asked again.
    private async Task<int?> ProposeFixedAsync(Proposal proposal, string sha, CancellationToken cancellationToken)
    {
        try
        {
            return await repository.ProposeAsync(proposal, sha, cancellationToken);
        }
        catch (GitHubException first)
        {
            log($"{proposal.Branch} not proposed: {first.Message}; deleting a leftover branch and trying once more");
            try
            {
                await repository.DeleteBranchAsync(proposal.Branch, cancellationToken);
                return await repository.ProposeAsync(proposal, sha, cancellationToken);
            }
            catch (GitHubException ex)
            {
                Notice($"{proposal.Branch} not proposed: {ex.Message}");
                return null;
            }
        }
    }

    // The ids the table holds against this run's catalogues: each orphaned or renamed pair in the journal, a summary, and for
    // orphaned pairs a draft that leaves those entries as gaps. Never merged, with or without --auto-merge: the maintainer's
    // merge is the hand check (ADR 0004 Addendum ARMAM-597). A renamed pair is only reported.
    private async Task CheckIdsAsync(
        CatalogueIndex index, HashSet<string> open, string text, string sha, CancellationToken cancellationToken)
    {
        var table = Table.Read(text);
        var findings = IdCheck.Findings(table, index);
        var orphaned = findings.Where(f => f.State == IdState.Orphaned).ToList();
        foreach (var finding in findings.Where(f => f.State is IdState.Orphaned or IdState.Renamed))
        {
            log($"id check: {Text.Clean(finding.Box)}, {finding.Reading}, {Text.Clean(finding.Name)}: " +
                $"{Text.Clean(finding.Ids.CatalogueId)} {Text.Clean(finding.Ids.EntryId)} {(finding.State == IdState.Orphaned ? "orphaned" : "renamed")}");
        }

        var notChecked = findings.Where(f => f.State == IdState.Unchecked).Select(f => f.Reading).Distinct().Order(StringComparer.Ordinal).ToList();
        (orphaned.Count > 0 || notChecked.Count > 0 ? Notice : log)(
            $"id check: {findings.Count} pair(s), {findings.Count(f => f.State == IdState.Valid)} valid, {orphaned.Count} orphaned, " +
            $"{findings.Count(f => f.State == IdState.Renamed)} renamed; unchecked: {(notChecked.Count > 0 ? string.Join(", ", notChecked) : "none")}");
        if (orphaned.Count == 0)
        {
            return;
        }

        if (!HasSlot(open, StaleIdsBranch))
        {
            return;
        }

        IdCheck.RemoveOrphans(table, index);
        var body = new StringBuilder()
            .AppendLine("From the scraper service (ADR 0004 Addendum ARMAM-597): these ids name no root entry of their reading in")
            .AppendLine("this run's catalogues any more. The entries keep name and count and become gaps; a choice left with fewer")
            .AppendLine("than two options becomes a gap as a whole, its remaining ids go with it. Never merged by the service, also")
            .AppendLine("with `--auto-merge`: the merge is the hand check.")
            .AppendLine()
            .AppendLine("| Box | Reading | Entry | Ids |")
            .AppendLine("|---|---|---|---|");
        foreach (var finding in orphaned)
        {
            body.AppendLine($"| {Text.Code(finding.Box)} | {finding.Reading} | {Text.Code(finding.Name)} | " +
                $"{Text.Code(finding.Ids.CatalogueId)} {Text.Code(finding.Ids.EntryId)} |");
        }

        var proposal = new Proposal(StaleIdsBranch, "data: remove orphaned catalogue ids", body.ToString(), Table.Write(table), Draft: true);
        if (await ProposeFixedAsync(proposal, sha, cancellationToken) is null)
        {
            return;
        }

        open.Add(StaleIdsBranch);
        Notice($"proposed {StaleIdsBranch}: {orphaned.Count} orphaned pair(s)");
    }

    private void Notice(string line)
    {
        log(line);
        notice?.Invoke(line);
    }

    private void Retry(string code, DateTimeOffset now)
    {
        state.Retry(code, now);
        persist?.Invoke();
    }

    private void Mark(IEnumerable<string> codes, DateTimeOffset now)
    {
        foreach (var code in codes)
        {
            state.Mark(code, now);
        }

        persist?.Invoke();
    }

    // A slow source (one request a minute, as miniset.net was until ARMAM-602) never confirms a box alone, so it is asked
    // only when a fast one has found the product and the fast findings alone would not make a proposal; otherwise its
    // interval would set the pace of the whole run (ADR 0003 §6).
    private async Task<(List<Listing> Listings, int Failed, bool Retryable)> FindAsync(
        Product product, CancellationToken cancellationToken)
    {
        var fast = sources.Where(s => s.Interval <= Page.DefaultInterval).ToList();
        var (listings, failed, retryable) = await AskAsync(fast, product, cancellationToken);
        var slow = sources.Except(fast).ToList();
        if (listings.Count > 0 && slow.Count > 0 && !Suffices(product, listings))
        {
            var (more, moreFailed, moreRetryable) = await AskAsync(slow, product, cancellationToken);
            listings.AddRange(more);
            failed += moreFailed;
            retryable |= moreRetryable;
        }

        return (listings, failed, retryable);
    }

    private bool Suffices(Product product, IReadOnlyList<Listing> listings) =>
        ProductCatalogue.IsCombatPatrol(product)
            ? listings.Select(l => l.Url).Distinct().Count() >= 2
            : Agreement.Agree(listings, RootOf(product), MemberOf(product)).Entries is not null;

    // Retryable: at least one shop failed in a way the same run can still get past (a throttle, not a refusal).
    private async Task<(List<Listing> Listings, int Failed, bool Retryable)> AskAsync(
        IEnumerable<ISource> asked, Product product, CancellationToken cancellationToken)
    {
        var found = await Task.WhenAll(asked.Select(async source =>
        {
            try
            {
                return (Listing: await source.FindAsync(product, fetcher, cancellationToken), Failed: false, Retryable: false);
            }
            catch (FetchFailedException ex)
            {
                // The shop did not answer; the fetcher has logged it (a wait for Retry-After once per wait).
                return (Listing: null, Failed: true, Retryable: ex.CanRetryInRun);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The shop answered with something unreadable (a changed page, a timed-out pattern): no finding, logged.
                // Counting it as "did not answer" would bring every product back each run until the parser is fixed.
                log($"{source.Host}: {product.Code} unreadable: {ex.GetType().Name}: {Text.Clean(ex.Message)}");
                return (Listing: null, Failed: false, Retryable: false);
            }
        }));
        return (found.Select(f => f.Listing).OfType<Listing>().ToList(), found.Count(f => f.Failed), found.Any(f => f.Retryable));
    }

    private Proposal? NewBox(Product product, IReadOnlyList<string> codes, IReadOnlyList<Listing> listings, string text)
    {
        // The table names a box in English (README, Box); a German shop's title never becomes the name, nor the title of a
        // listing that is no source of the box (ADR 0003 addendum ARMAM-601).
        var agreement = ProductCatalogue.IsCombatPatrol(product) ? null : Agreement.Agree(listings, RootOf(product), MemberOf(product));
        var sourceUrls = agreement?.Sources ?? listings.Select(l => l.Url).Distinct().ToList();
        var englishTitle = listings.FirstOrDefault(l => !l.German && l.Title.Length > 0 && sourceUrls.Contains(l.Url))?.Title;
        var name = englishTitle ?? Text.Clean(product.Name);
        var notes = new List<string>();
        if (englishTitle is null)
        {
            notes.Add("The box name is the catalogue name; set the English product name.");
        }

        var body = autoMerge
            ? new StringBuilder()
                .AppendLine("From the scraper service, merged by the scraper service (`--auto-merge`, ADR 0004). Ids come from a")
                .AppendLine("catalogue entry of exactly the same name; an entry without ids had no such entry.")
            : new StringBuilder()
                .AppendLine("Draft from the scraper service (ADR 0003). Before merging, complete by hand: catalogue ids")
                .AppendLine("(`catalogueId` with `entryId` or `choice`), readings, catalogue names and roles. The merge is the hand check.");
        body.AppendLine()
            .AppendLine($"**Box name:** {Text.Code(name)}  ")
            .AppendLine($"**Article numbers:** {string.Join(", ", codes)}  ")
            .AppendLine($"**Catalogue name and status:** {Text.Code(product.Name)}, {Text.Code(product.Status)}")
            .AppendLine();

        JsonObject box;
        if (agreement is null)
        {
            if (sourceUrls.Count < 2)
            {
                log($"{product.Code} waits: {sourceUrls.Count} shop(s) confirm the Combat Patrol");
                return null;
            }

            var generation = product.Status == "current" ? "current" : "legacy";
            box = Table.CombatPatrolBox(name, codes, sourceUrls, generation);
            Sources(body, sourceUrls);
            notes.Add($"The generation is set to `{generation}` from the catalogue status; check it.");
        }
        else
        {
            if (agreement.Entries is null)
            {
                log($"{product.Code} waits: {Text.Clean(agreement.Waiting ?? "")}");
                return null;
            }

            var readings = ProductCatalogue.ReadingsOf(product);
            box = Table.Box(name, codes, agreement.Sources, readings, agreement.Entries, IdsOf);
            Sources(body, agreement.Sources);
            body.AppendLine($"**Entries** (in every reading: {string.Join(", ", readings)})")
                .AppendLine()
                .AppendLine("| Entry | Count | Confirmed by | Ids |")
                .AppendLine("|---|---|---|---|");
            foreach (var entry in agreement.Entries)
            {
                var ids = readings.Select(r => IdsOf(r, entry.MemberOf ?? entry.Name) is { } e ? $"{r}: {Text.Code(e.CatalogueId)} {Text.Code(e.EntryId)}" : null)
                    .OfType<string>()
                    .ToList();
                body.AppendLine($"| {Text.Code(entry.Name)} | {string.Join(" or ", entry.Counts)} | {string.Join(", ", entry.Hosts)} | " +
                    $"{(ids.Count > 0 ? string.Join("; ", ids) : "none")} |");
            }

            body.AppendLine();
            notes.AddRange(agreement.Entries.SelectMany(Notes));
        }

        if (Table.Validate(box) is { } error)
        {
            log($"{product.Code} not proposed: {error}");
            return null;
        }

        if (notes.Count > 0)
        {
            body.AppendLine("**Notes**");
            notes.ForEach(n => body.AppendLine($"- {n}"));
        }

        var table = Table.Read(text);
        Table.AddBox(table, box);
        return new Proposal(
            $"{GitHubRepository.BranchPrefix}{codes[0]}", $"data: add {name}", body.ToString(), Table.Write(table), Draft: !autoMerge);
    }

    // Ids by name only while the service merges by itself (ADR 0004); a draft leaves them to the maintainer.
    private RootEntry? IdsOf(string reading, string name) => autoMerge ? catalogues?.Find(reading, name) : null;

    // The root entries a name names in the product's readings, as one key, or null: the same hit that sets ids decides
    // between a build sentence and lines and lets two names of one entry confirm each other (ADR 0003 addendum ARMAM-599).
    // An alias is one model of its entry beside the others and keeps its own name in the key (ADR 0004 Addendum ARMAM-642).
    private Func<string, string?> RootOf(Product product)
    {
        var readings = ProductCatalogue.ReadingsOf(product);
        return name =>
        {
            var hits = readings.Select(r => IdsOf(r, name) is { } e ? $"{r}:{e.CatalogueId}:{e.EntryId}" : null).OfType<string>().ToList();
            return hits.Count == 0 ? null
                : readings.Any(r => catalogues!.IsAlias(r, name)) ? $"{string.Join('|', hits)}|alias:{ContentLines.Normalize(name)}"
                : string.Join('|', hits);
        };
    }

    // Whether a name is a model of the unit another name hits, in any of the product's readings: a member, which takes the
    // unit's ids (ADR 0004 Addendum ARMAM-688).
    private Func<string, string, bool> MemberOf(Product product)
    {
        var readings = ProductCatalogue.ReadingsOf(product);
        return (unit, name) => readings.Any(r => IdsOf(r, unit) is { } ids && catalogues!.IsMember(r, ids, name));
    }

    private Proposal? LanguageVariants(string knownCode, IReadOnlyList<string> added, IReadOnlyList<Listing> listings, string text)
    {
        if (added.Count == 0)
        {
            return null;
        }

        var table = Table.Read(text);
        var name = Text.Clean(Table.AddCodes(table, knownCode, added));
        var body = new StringBuilder()
            .AppendLine($"Language variants of {Text.Code(name)} (ADR 0003 §11): {string.Join(", ", added)}.")
            .AppendLine()
            .AppendLine($"Found as variants of the same shop product as {knownCode}:")
            .AppendLine();
        foreach (var url in listings.Where(l => l.VariantEans.Count > 1).Select(l => l.Url).Distinct())
        {
            body.AppendLine($"- {url}");
        }

        return new Proposal(
            $"{GitHubRepository.BranchPrefix}{added[0]}",
            $"data: add language variants to {name}",
            body.ToString(),
            Table.Write(table),
            Draft: !autoMerge);
    }

    private static void Sources(StringBuilder body, IEnumerable<string> urls)
    {
        body.AppendLine("**Sources**");
        foreach (var url in urls)
        {
            body.AppendLine($"- {url}");
        }

        body.AppendLine();
    }

    private static IEnumerable<string> Notes(ProposedEntry entry)
    {
        if (entry.Counts.Count > 1)
        {
            yield return $"{Text.Code(entry.Name)}: the sources disagree ({string.Join(", ", entry.Counts)}); entered as `countCandidates`.";
        }

        if (entry.AlternativeBuild)
        {
            yield return $"{Text.Code(entry.Name)}: a source names an alternative build; counted as the build the box is sold as.";
        }

        if (entry.NonPlayer)
        {
            yield return $"{Text.Code(entry.Name)}: named as non-player models; role `npo` in Kill Team, `extra` in any other reading.";
        }
    }

    private enum Outcome
    {
        Answered,
        NotAnswered,
        AskAgain,
    }

    // What a run carries from one product to the next: the open branches and the table as last merged.
    private sealed class RunState(HashSet<string> open, Dictionary<string, string> codeByEan, string text, string sha)
    {
        private string _text = text;

        public HashSet<string> Open { get; } = open;

        public Dictionary<string, string> CodeByEan { get; } = codeByEan;

        public IReadOnlySet<string> Known { get; private set; } = Table.Codes(Table.Read(text));

        public string Sha { get; set; } = sha;

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                Known = Table.Codes(Table.Read(value));
            }
        }
    }
}
