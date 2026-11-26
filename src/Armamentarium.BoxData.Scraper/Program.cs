using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Security.Authentication;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Entry point of a run on the 1st and the 15th (ADR 0004 §6). systemd hands the token in through <c>LoadCredential</c>
/// (<c>$CREDENTIALS_DIRECTORY/github-token</c>), the mail password (<c>mail-password</c>) and the state directory through
/// <c>$STATE_DIRECTORY</c>. A run mails its notices at its end; a failed run is mailed by armamentarium-box-data-failure.service
/// (ADR 0003 Addendum ARMAM-600).
///
///   --dry-run        reads GitHub without a token, writes nothing, prints each proposal instead of opening it
///   --code &lt;number&gt;  searches this one article number only, whatever the state says (with --dry-run for a check)
///   --auto-merge     opens each pull request ready and merges it at once (ADR 0004); with --dry-run it only says so
///   --host &lt;host&gt;    with --dry-run and --code: asks this one allowlisted shop and prints what it reads; --code
///                    then takes a comma-separated list (ADR 0003 addendum ARMAM-602)
///   --notify-failure mails that a run failed, from what systemd hands an OnFailure= unit; nothing else (ADR 0003 Addendum
///                    ARMAM-600)
///   --id-report      reads the table and the catalogues without a token, asks no shop, writes nothing; prints per reading
///                    the gaps with their reason, the gaps a run would now fill and the names it would repair (ADR 0004
///                    Addendum ARMAM-598)
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var idReport = args.Contains("--id-report");
        var dryRun = args.Contains("--dry-run") || idReport;
        var autoMerge = args.Contains("--auto-merge");
        var codeAt = Array.IndexOf(args, "--code");
        var onlyCode = codeAt >= 0 && codeAt + 1 < args.Length ? args[codeAt + 1] : null;
        void Log(string line) => Console.WriteLine(line);
        if (args.Contains("--notify-failure"))
        {
            return await NotifyFailureAsync(Log);
        }

        var (probe, probeError) = Probe.Resolve(args, Sources.All);
        if (probeError is not null)
        {
            Log(probeError);
            return 2;
        }

        // systemd stops the unit with SIGTERM; the run ends between two requests, the state of every finished product is saved.
        using var stop = new CancellationTokenSource();
        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            stop.Cancel();
        }

        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

        try
        {
            // GitHub's API and the catalogue on GitHub Pages: no redirect off the requested host, at most 64 MB
            // (products.json measured 13.6 MB).
            using var github = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(100),
                MaxResponseContentBufferSize = 64 * 1024 * 1024,
            };
            IRepository repository = new GitHubRepository(github, dryRun ? null : ReadCredential("github-token"), log: Log);
            // The bundled catalogues lie in the private app repository: the service's token, a local dry run's GITHUB_TOKEN.
            var catalogueToken = dryRun ? Environment.GetEnvironmentVariable("GITHUB_TOKEN") : ReadCredential("github-token");
            if (dryRun)
            {
                repository = new DryRunRepository(repository, Log);
            }

            if (idReport)
            {
                var (text, _) = await repository.GetTableAsync(stop.Token);
                var index = await CatalogueIndex.LoadAsync(github, Log, stop.Token, token: catalogueToken);
                foreach (var notice in AliasNotices(index.UseAliases(Aliases.Read(await repository.GetAliasesAsync(stop.Token)))))
                {
                    Log(notice);
                }

                IdReport.Write(Table.Read(text), index, Sources.All.Select(s => s.Host).ToHashSet(StringComparer.OrdinalIgnoreCase), Log);
                return 0;
            }

            var statePath = dryRun ? null : Path.Combine(Required("STATE_DIRECTORY"), "state.json");
            var state = statePath is null ? new ScanState() : ScanState.Load(statePath);
            var products = ProductCatalogue.Parse(await github.GetStringAsync(ProductCatalogue.ProductsUrl, stop.Token));
            Log($"{products.Count} Games Workshop products in the catalogue");
            if (probe is not null)
            {
                await ProbeAsync(probe, onlyCode!, products, Log, stop.Token);
                return 0;
            }

            // Every run checks the ids the table holds (ADR 0004 Addendum ARMAM-597); ids by name are set only while the
            // service merges by itself (ADR 0004), drafts stay without ids for the maintainer (Runner.IdsOf).
            var catalogues = await CatalogueIndex.LoadAsync(github, Log, stop.Token, token: catalogueToken);
            var notices = catalogues.Unavailable.Order(StringComparer.Ordinal)
                .Select(reading => $"{reading}: a catalogue source is unreadable, no ids and no id check for this reading this run")
                .ToList();
            notices.AddRange(AliasNotices(catalogues.UseAliases(Aliases.Read(await repository.GetAliasesAsync(stop.Token)))));

            using var fetcher = new Fetcher(Sources.All.ToDictionary(s => s.Host, s => s.Interval, StringComparer.OrdinalIgnoreCase), Log);
            try
            {
                await new Runner(
                        repository,
                        Sources.All,
                        fetcher,
                        state,
                        Log,
                        statePath is null ? null : () => state.Save(statePath),
                        autoMerge,
                        catalogues,
                        notices.Add)
                    .RunAsync(products, DateTimeOffset.UtcNow, stop.Token, onlyCode);
            }
            finally
            {
                // What the throttling shops did this run, the measure of ADR 0003 Addendum ARMAM-595; also on a stop.
                foreach (var line in fetcher.Summary())
                {
                    Log(line);
                }

                // Also on a stop or a failure: what the run found so far still reaches the maintainer.
                await MailNoticesAsync(notices, dryRun, Log);
            }

            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Log("Run stopped; the next run continues from the saved state");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"Run failed: {ex}");
            return 1;
        }
    }

    private static async Task ProbeAsync(
        ISource source, string code, IReadOnlyList<Product> products, Action<string> log, CancellationToken cancellationToken)
    {
        using var fetcher = new Fetcher(new Dictionary<string, TimeSpan> { [source.Host] = source.Interval }, log);
        foreach (var one in code.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var product = products.FirstOrDefault(p => p.Code == one);
            if (product is null)
            {
                log($"{one} is not in the product catalogue");
                continue;
            }

            log($"{one} {Text.Clean(product.Name)}");
            try
            {
                foreach (var line in Probe.Describe(source.Host, await source.FindAsync(product, fetcher, cancellationToken)))
                {
                    log(line);
                }
            }
            catch (FetchFailedException ex)
            {
                log($"{source.Host}: did not answer: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // An unreadable page costs this code, not the rest of the list, as in a run (Runner.AskAsync).
                log($"{source.Host}: {one} unreadable: {ex.GetType().Name}: {Text.Clean(ex.Message)}");
            }
        }
    }

    // An alias whose target names no one root entry leaves its name a gap; the maintainer hears of it (ADR 0004 Addendum ARMAM-642).
    private static IEnumerable<string> AliasNotices(IReadOnlyList<Alias> unresolved) =>
        unresolved.Select(a =>
            $"{a.Reading}: alias {Text.Code(a.Name)} -> {Text.Code(a.Target)} in {Text.Code(a.Catalogue)} names no one root entry; the name stays a gap");

    private static async Task<int> NotifyFailureAsync(Action<string> log)
    {
        var message = Mailer.FailureMessage(Environment.GetEnvironmentVariable);
        try
        {
            await Mailer.SendAsync(message, ReadCredential("mail-password"), CancellationToken.None);
            log($"notification sent: {message.Subject}");
            return 0;
        }
        catch (Exception ex) when (IsMailFailure(ex))
        {
            // The failure unit fails in turn and shows in the journal; it has no OnFailure= of its own.
            log($"notification not sent: {ex.GetType().Name}: {Text.Clean(ex.Message)}");
            return 1;
        }
    }

    // A mail that does not go out is logged and changes nothing about the run (ADR 0003 Addendum ARMAM-600).
    private static async Task MailNoticesAsync(IReadOnlyList<string> notices, bool dryRun, Action<string> log)
    {
        if (notices.Count == 0)
        {
            return;
        }

        if (dryRun)
        {
            log($"--- would mail {notices.Count} notice(s)");
            return;
        }

        try
        {
            await Mailer.SendAsync(Mailer.FindingsMessage(notices), ReadCredential("mail-password"), CancellationToken.None);
            log($"notice mail sent: {notices.Count} notice(s)");
        }
        catch (Exception ex) when (IsMailFailure(ex))
        {
            log($"notice mail not sent: {ex.GetType().Name}: {Text.Clean(ex.Message)}");
        }
    }

    private static bool IsMailFailure(Exception ex) =>
        ex is SmtpException or IOException or UnauthorizedAccessException or InvalidOperationException or AuthenticationException
            or OperationCanceledException;

    private static string ReadCredential(string name)
    {
        var value = File.ReadAllText(Path.Combine(Required("CREDENTIALS_DIRECTORY"), name)).Trim();
        return value.Length > 0 ? value : throw new InvalidOperationException($"The {name} credential is empty.");
    }

    private static string Required(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{variable} is not set; the systemd unit provides it (deploy/).");

    /// <summary>Reads like the real repository, prints proposals instead of opening them.</summary>
    private sealed class DryRunRepository(IRepository inner, Action<string> log) : IRepository
    {
        public Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken) =>
            inner.GetTableAsync(cancellationToken);

        public Task<string?> GetAliasesAsync(CancellationToken cancellationToken) => inner.GetAliasesAsync(cancellationToken);

        public Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken) =>
            inner.OpenScraperBranchesAsync(cancellationToken);

        public Task<int> ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken)
        {
            log($"--- would open {(proposal.Draft ? "draft" : "pull request")} {proposal.Branch}: {proposal.Title}");
            log(proposal.Body);
            return Task.FromResult(0);
        }

        // Nothing was opened, so nothing is merged; the run reads the unchanged table again and goes on.
        public Task MergeAsync(int number, CancellationToken cancellationToken)
        {
            log("--- would merge it");
            return Task.CompletedTask;
        }

        public Task DeleteBranchAsync(string branch, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
