using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Armamentarium.BoxData.Scraper;

/// <summary>A root entry of a catalogue: the catalogue's id and the selectionEntry id (a link's target).</summary>
public sealed record RootEntry(string CatalogueId, string EntryId);

/// <summary>
/// A root entry a normalised name names, with its catalogue's name and whether that catalogue is the entry's home: it
/// defines the entry, at its root, in its shared entries or in its library (ADR 0004 Addendum ARMAM-598).
/// </summary>
public sealed record Candidate(RootEntry Entry, string CatalogueName, bool Home);

/// <summary>
/// A catalogue repository the service reads ids from (ADR 0004): owner, repository and branch, or the latest release
/// when <see cref="Branch"/> is null. The rows follow the app's BsDataEditionSources; a change there is a change here. A
/// <see cref="Preferred"/> source wins a name that it and another source of the reading name (ADR 0004 Addendum ARMAM-598).
/// </summary>
public sealed record CatalogueSource(string Reading, string Owner, string Repository, string? Branch, bool Preferred = false);

/// <summary>
/// What the catalogues of a run say about ids the table already holds (ADR 0004 Addendum ARMAM-597): still a root entry
/// of that name, a root entry no longer, a root entry under another name, or not checked (the reading was not read).
/// </summary>
public enum IdState
{
    Valid,
    Orphaned,
    Renamed,
    Unchecked,
}

/// <summary>
/// The root entries of the catalogues of every reading, by normalised name (ADR 0004). A name gets ids only when it
/// names one root entry of its reading, by the rules of <see cref="Find"/> (ADR 0004 Addendum ARMAM-598); otherwise none.
/// </summary>
public sealed partial class CatalogueIndex
{
    /// <summary>The compiled allowlist of catalogue repositories (ADR 0004). Nothing else is downloaded.</summary>
    public static readonly CatalogueSource[] Sources =
    [
        new("40k", "BSData", "wh40k-10e", "main"),
        new("40k", "BSData", "wh40k-11e", "main"),
        new("kt", "BSData", "wh40k-killteam", null),
        new("aos", "BSData", "age-of-sigmar-4th", null),
        new("aos", "LordTyberias", "armamentarium-spearhead-data", "main", Preferred: true),
        new("tow", "BSData", "warhammer-the-old-world", null),
        new("hh", "BSData", "horus-heresy-3rd-edition", "main"),
    ];

    /// <summary>The largest catalogue file read; the largest measured (11th edition Space Marines JSON) is 6.8 MB.</summary>
    public const int MaxEntryBytes = 32 * 1024 * 1024;

    private readonly Dictionary<string, Dictionary<string, HashSet<RootEntry>>> _byReading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<RootEntry, HashSet<string>>> _namesByReading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _homesByReading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<RootEntry>> _preferredByReading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _catalogueNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, RootEntry?>> _aliasesByReading = new(StringComparer.Ordinal);

    // The normalised names of the models under each unit a catalogue or library defines, by the unit's entry id.
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _modelsByReading = new(StringComparer.Ordinal);

    // The word a shop leaves off a unit's name ("Intercessors" for "Intercessor Squad").
    private static readonly string[] UnitWords = ["squad", "mob", "unit", "pack", "band", "regiment", "team"];

    private static readonly string PrimarisPrefix = ContentLines.Normalize("Primaris") + " ";

    /// <summary>
    /// The name names a unit of several models ("Krieg Heavy Weapons Squad"): a build of it by article states no model count
    /// (ADR 0003 Addendum ARMAM-688).
    /// </summary>
    public static bool IsUnitName(string name) =>
        name.ToLowerInvariant().Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries).Any(w => UnitWords.Contains(w));

    /// <summary>
    /// The name is a model of the unit, as the catalogue names it or without its gear ("Heavy Weapons Gunners" for
    /// "Heavy Weapons Gunners w/ Lascannon"): a member, which takes the unit's ids (ADR 0004 Addendum ARMAM-688).
    /// </summary>
    public bool IsMember(string reading, RootEntry unit, string name)
    {
        var key = ContentLines.Normalize(name);
        return key.Length > 0
            && _modelsByReading.GetValueOrDefault(reading)?.GetValueOrDefault(unit.EntryId) is { } models
            && models.Any(m => m == key || m.StartsWith(key + " w ", StringComparison.Ordinal) || m.StartsWith(key + " with ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The ids for a model name (ADR 0004, Addendum ARMAM-598), from the first of these that names any root entry of the
    /// reading: the name; the name with a unit word after it; the name without one or two words before it that the
    /// catalogue's name carries ("Ork Boyz" for Boyz of the Orks); from two words, the root names that go on after it.
    /// Of the root entries found, one; or the one from a preferred source; or, all being the same entry, its one home.
    /// Null otherwise: no rule picks among different entries. An alias name goes to its target before any rule and stays a
    /// gap when its target is not one entry (ADR 0004 Addendum ARMAM-642).
    /// </summary>
    public RootEntry? Find(string reading, string name) =>
        _unavailable.Contains(reading) ? null
        : _aliasesByReading.GetValueOrDefault(reading) is { } aliases && aliases.TryGetValue(ContentLines.Normalize(name), out var target) ? target
        : Named(reading, name) is { } entries ? Resolve(reading, entries)
        : null;

    /// <summary>
    /// Takes the aliases of <c>aliases.json</c> (ADR 0004 Addendum ARMAM-642): each name goes to the root entry its target
    /// names in the catalogue it names, by the rules of <see cref="Find"/> among that catalogue's hits. Returns the aliases
    /// whose target is not one root entry of a reading read whole; their names stay gaps.
    /// </summary>
    public IReadOnlyList<Alias> UseAliases(IEnumerable<Alias> aliases)
    {
        var unresolved = new List<Alias>();
        foreach (var alias in aliases)
        {
            var hits = Named(alias.Reading, alias.Target)?.Where(e => CatalogueName(e) == alias.Catalogue).ToList();
            var target = hits is { Count: > 0 } ? Resolve(alias.Reading, hits) : null;
            if (!_aliasesByReading.TryGetValue(alias.Reading, out var names))
            {
                _aliasesByReading[alias.Reading] = names = new Dictionary<string, RootEntry?>(StringComparer.Ordinal);
            }

            names[ContentLines.Normalize(alias.Name)] = target;
            if (target is null && !_unavailable.Contains(alias.Reading))
            {
                unresolved.Add(alias);
            }
        }

        return unresolved;
    }

    /// <summary>
    /// The name is an alias with a target: one model of a root entry another line of the box may name too, so it keeps its
    /// own line and its own key (ADR 0004 Addendum ARMAM-642).
    /// </summary>
    public bool IsAlias(string reading, string name) =>
        _aliasesByReading.GetValueOrDefault(reading)?.GetValueOrDefault(ContentLines.Normalize(name)) is not null;

    /// <summary>The root entries of the step of <see cref="Find"/> that names any, for the id report; empty for none.</summary>
    public IReadOnlyList<Candidate> Candidates(string reading, string name) =>
        Named(reading, name)?.Select(e => new Candidate(e, CatalogueName(e), IsHome(reading, e))).ToList() ?? [];

    // The root entries of the first step that names any (Find), or null.
    private IReadOnlyCollection<RootEntry>? Named(string reading, string name)
    {
        if (!_byReading.TryGetValue(reading, out var names))
        {
            return null;
        }

        var key = ContentLines.Normalize(name);
        if (Whole(key) is { } entries)
        {
            return entries;
        }

        // The 10th-edition catalogues dropped "Primaris"; only the whole rest counts, never a longer name, so Chaplain and
        // Chaplain on Bike stay apart (ADR 0004 Addendum ARMAM-688).
        if (key.StartsWith(PrimarisPrefix, StringComparison.Ordinal) && Whole(key[PrimarisPrefix.Length..]) is { } unprefixed)
        {
            return unprefixed;
        }

        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var drop = 1; drop <= 2 && drop < words.Length; drop++)
        {
            var army = $" {string.Join(' ', words[..drop])} ";
            if (names.TryGetValue(string.Join(' ', words[drop..]), out var rest)
                && rest.Where(e => $" {ContentLines.Normalize(CatalogueName(e))} ".Contains(army, StringComparison.Ordinal)).ToList() is { Count: > 0 } inArmy)
            {
                return inArmy;
            }
        }

        // ponytail: scans every root name of the reading; an index by first word if a run measurably slows.
        if (words.Length >= 2)
        {
            var longer = names.Where(n => n.Key.StartsWith(key + " ", StringComparison.Ordinal)).SelectMany(n => n.Value).ToHashSet();
            if (longer.Count > 0)
            {
                return longer;
            }
        }

        return null;

        // The name itself, or the name with a unit word after it.
        IReadOnlyCollection<RootEntry>? Whole(string k)
        {
            if (names.TryGetValue(k, out var exact))
            {
                return exact;
            }

            var withUnitWord = UnitWords.Select(w => names.GetValueOrDefault($"{k} {w}")).OfType<HashSet<RootEntry>>().SelectMany(e => e).ToHashSet();
            return withUnitWord.Count > 0 ? withUnitWord : null;
        }
    }

    /// <summary>
    /// Holds ids the table carries against this run's root entries: the pair must still be a root entry of the reading and
    /// the name one of its normalised names, or a name <see cref="Find"/> gives the pair. A reading not read whole is not
    /// checked: a missing source could hold the pair.
    /// </summary>
    public IdState Check(string reading, RootEntry ids, string name) =>
        _unavailable.Contains(reading) || !_namesByReading.TryGetValue(reading, out var entries) ? IdState.Unchecked
        : !entries.TryGetValue(ids, out var names) ? IdState.Orphaned
        : names.Contains(ContentLines.Normalize(name)) || Find(reading, name) == ids || IsMember(reading, ids, name) ? IdState.Valid
        : IdState.Renamed;

    private RootEntry? Resolve(string reading, IReadOnlyCollection<RootEntry> entries)
    {
        var preferred = _preferredByReading.GetValueOrDefault(reading);
        var pool = entries.Where(e => preferred?.Contains(e) == true).ToList() is { Count: > 0 } fromPreferred ? fromPreferred : entries.ToList();
        if (pool.Count == 1)
        {
            return pool[0];
        }

        // A unit of one army that others link at their root (Brood Brothers, Chaos legions): the army it belongs to.
        var homes = pool.Where(e => IsHome(reading, e)).ToList();
        return pool.Select(e => e.EntryId).Distinct(StringComparer.Ordinal).Count() == 1 && homes.Count == 1 ? homes[0] : null;
    }

    // The catalogue defines the entry, or its library does ("Imperium - Astra Militarum - Library").
    private bool IsHome(string reading, RootEntry entry) =>
        _homesByReading.GetValueOrDefault(reading)?.GetValueOrDefault(entry.EntryId) is { } definedIn
        && CatalogueName(entry) is var name
        && (definedIn.Contains(name) || definedIn.Contains($"{name} - Library") || definedIn.Contains($"{name} Library"));

    private string CatalogueName(RootEntry entry) => _catalogueNames.GetValueOrDefault(entry.CatalogueId, entry.CatalogueId);

    /// <summary>A reading whose sources could not all be read gets no ids in this run: a missing source could hide a twin.</summary>
    public void MarkUnavailable(string reading) => _unavailable.Add(reading);

    /// <summary>The readings without ids this run, one notice each in the run's mail (ADR 0003 Addendum ARMAM-600).</summary>
    public IReadOnlySet<string> Unavailable => _unavailable;

    /// <summary>
    /// Reads one catalogue file, BattleScribe XML (.cat) or JSON; a library or anything else adds nothing. False when the
    /// file cannot be read whole: not well-formed, a document type declaration, or an id outside the BattleScribe form.
    /// </summary>
    public bool AddCatalogue(string reading, string fileName, byte[] content, bool preferred = false) =>
        fileName.EndsWith(".cat", StringComparison.OrdinalIgnoreCase) ? AddXml(reading, content, preferred)
        : !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || AddJson(reading, content, preferred);

    /// <summary>
    /// Reads every catalogue of a repository archive in memory; nothing is unpacked to disk. A file that is skipped or
    /// unreadable leaves the whole reading without ids for this run (logged): it could hold the twin of a name.
    /// </summary>
    public void AddArchive(string reading, Stream zip, int maxEntryBytes, Action<string> log, bool preferred = false)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase)
                && !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? problem = null;
            if (entry.Length > maxEntryBytes)
            {
                problem = $"{entry.Length} bytes";
            }
            else
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                // The declared length can lie; the copy stops one byte past the limit.
                CopyAtMost(stream, buffer, maxEntryBytes + 1);
                problem = buffer.Length > maxEntryBytes ? "larger than declared"
                    : !AddCatalogue(reading, entry.Name, buffer.ToArray(), preferred) ? "unreadable"
                    : null;
            }

            if (problem is not null)
            {
                MarkUnavailable(reading);
                log($"{reading}: {Text.Clean(entry.Name)} {problem}, no ids for this reading");
            }
        }
    }

    /// <summary>
    /// Downloads every source of the allowlist and reads it. A source that fails leaves its reading without ids for this
    /// run (logged); the run goes on.
    /// </summary>
    public static async Task<CatalogueIndex> LoadAsync(
        HttpClient http, Action<string> log, CancellationToken cancellationToken, IReadOnlyList<CatalogueSource>? sources = null)
    {
        var index = new CatalogueIndex();
        foreach (var source in sources ?? Sources)
        {
            try
            {
                var reference = source.Branch is { } branch ? $"heads/{branch}" : $"tags/{await LatestTagAsync(http, source, cancellationToken)}";
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"https://codeload.github.com/{source.Owner}/{source.Repository}/zip/refs/{reference}");
                request.Headers.UserAgent.ParseAdd("ArmamentariumBoxData");
                using var response = await http.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var zip = new MemoryStream(await response.Content.ReadAsByteArrayAsync(cancellationToken));
                index.AddArchive(source.Reading, zip, MaxEntryBytes, log, source.Preferred);
                log($"{source.Reading}: read {source.Owner}/{source.Repository} at {reference}");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or KeyNotFoundException
                                           || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                index.MarkUnavailable(source.Reading);
                log($"{source.Reading}: {source.Owner}/{source.Repository} unreadable, no ids for this reading: " +
                    $"{ex.GetType().Name}: {Text.Clean(ex.Message)}");
            }
        }

        return index;
    }

    private static async Task<string> LatestTagAsync(HttpClient http, CatalogueSource source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("ArmamentariumBoxData");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
        // The tag goes into a URL path: only the characters release tags use, starting with a letter or digit, no "..".
        return tag.Length > 0
            && char.IsAsciiLetterOrDigit(tag[0])
            && !tag.Contains("..", StringComparison.Ordinal)
            && tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? tag
            : throw new InvalidDataException("unexpected release tag");
    }

    private static void CopyAtMost(Stream from, Stream to, long limit)
    {
        var buffer = new byte[81920];
        int read;
        while (to.Length < limit && (read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - to.Length))) > 0)
        {
            to.Write(buffer, 0, read);
        }
    }

    private bool AddXml(string reading, byte[] content, bool preferred)
    {
        XElement root;
        try
        {
            using var stream = new MemoryStream(content);
            // No document type declaration at all, so no entity expansion; BattleScribe catalogues carry none.
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            root = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return false;
        }

        if (root.Name.LocalName != "catalogue")
        {
            return true;
        }

        // Every entry a catalogue or library defines, at any depth: where a linked unit is at home.
        foreach (var defined in root.Descendants().Where(e => e.Name.LocalName == "selectionEntry"))
        {
            AddHome(reading, Attr(defined, "id"), Attr(root, "name"));
            if (Attr(defined, "type") == "unit")
            {
                AddModels(reading, Attr(defined, "id"), defined.Descendants()
                    .Where(e => e.Name.LocalName == "selectionEntry" && Attr(e, "type") == "model")
                    .Select(e => Attr(e, "name")));
            }
        }

        if ((string?)root.Attribute("library") == "true")
        {
            return true;
        }

        var valid = true;
        // Only the root lists: sharedSelectionEntries hold the same element names but are no root offer.
        foreach (var container in root.Elements().Where(c => c.Name.LocalName is "selectionEntries" or "entryLinks"))
        {
            foreach (var child in container.Elements())
            {
                valid &= Add(reading, preferred, Attr(root, "id"), Attr(root, "name"), child.Name.LocalName, Attr(child, "type"), Attr(child, "id"), Attr(child, "name"), Attr(child, "targetId"));
            }
        }

        return valid;

        static string? Attr(XElement element, string name) => (string?)element.Attribute(name);
    }

    private bool AddJson(string reading, byte[] content, bool preferred)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("catalogue", out var catalogue)
                || catalogue.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            AddHomes(catalogue, Scalar(catalogue, "name"));
            if (Scalar(catalogue, "library") == "true")
            {
                return true;
            }

            var valid = true;
            foreach (var (container, element) in new[] { ("selectionEntries", "selectionEntry"), ("entryLinks", "entryLink") })
            {
                if (!catalogue.TryGetProperty(container, out var children) || children.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var child in children.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object))
                {
                    valid &= Add(reading, preferred, Scalar(catalogue, "id"), Scalar(catalogue, "name"), element, Scalar(child, "type"), Scalar(child, "id"), Scalar(child, "name"), Scalar(child, "targetId"));
                }
            }

            return valid;
        }

        // Every entry the catalogue defines, at any depth, as in the XML.
        void AddHomes(JsonElement element, string? catalogueName)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    AddHomes(item, catalogueName);
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "selectionEntries" or "sharedSelectionEntries" && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in property.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                        {
                            AddHome(reading, Scalar(entry, "id"), catalogueName);
                            if (Scalar(entry, "type") == "unit")
                            {
                                AddModels(reading, Scalar(entry, "id"), Models(entry));
                            }
                        }
                    }

                    AddHomes(property.Value, catalogueName);
                }
            }
        }

        // The names of the model entries at any depth under a unit, as in the XML.
        static IEnumerable<string?> Models(JsonElement unit)
        {
            foreach (var property in unit.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
            {
                foreach (var child in property.Value.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object))
                {
                    if (property.Name == "selectionEntries" && Scalar(child, "type") == "model")
                    {
                        yield return Scalar(child, "name");
                    }

                    foreach (var name in Models(child))
                    {
                        yield return name;
                    }
                }
            }
        }

        // BattleScribe JSON writes some attributes as strings and some as booleans; both read as text.
        static string? Scalar(JsonElement element, string name) =>
            !element.TryGetProperty(name, out var value) ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() ? "true" : "false"
            : null;
    }

    // A root selectionEntry of type unit or model, or a root entryLink to a selectionEntry (its own name, its target).
    // An id outside the forms the sources use (BSData's hex groups with dashes, the Spearhead repository's
    // "sph::box::01-name") makes the file unreadable: it would go into the table unchecked.
    private void AddHome(string reading, string? entryId, string? catalogueName)
    {
        if (entryId is null || catalogueName is null)
        {
            return;
        }

        if (!_homesByReading.TryGetValue(reading, out var homes))
        {
            _homesByReading[reading] = homes = new(StringComparer.Ordinal);
        }

        if (!homes.TryGetValue(entryId.Trim(), out var catalogues))
        {
            homes[entryId.Trim()] = catalogues = new(StringComparer.Ordinal);
        }

        catalogues.Add(catalogueName.Trim());
    }

    // ponytail: only model entries nested in the unit; a model the unit reaches through an entryLink is no member and the
    // box falls back to its lines or waits. Resolve linked targets if such a box shows up.
    private void AddModels(string reading, string? unitId, IEnumerable<string?> names)
    {
        if (unitId is null)
        {
            return;
        }

        if (!_modelsByReading.TryGetValue(reading, out var units))
        {
            _modelsByReading[reading] = units = new(StringComparer.Ordinal);
        }

        if (!units.TryGetValue(unitId.Trim(), out var models))
        {
            units[unitId.Trim()] = models = new(StringComparer.Ordinal);
        }

        models.UnionWith(names.OfType<string>().Select(n => ContentLines.Normalize(n.Trim())).Where(n => n.Length > 0));
    }

    private bool Add(string reading, bool preferred, string? catalogueId, string? catalogueName, string element, string? type, string? id, string? name, string? targetId)
    {
        var entryId = element switch
        {
            "selectionEntry" when type is "unit" or "model" => id,
            "entryLink" when type == "selectionEntry" => targetId,
            _ => null,
        };
        var key = name is null ? "" : ContentLines.Normalize(name.Trim());
        if (entryId is null || key.Length == 0)
        {
            return true;
        }

        catalogueId = catalogueId?.Trim();
        entryId = entryId.Trim();
        if (catalogueId is null || !BattleScribeId().IsMatch(catalogueId) || !BattleScribeId().IsMatch(entryId))
        {
            return false;
        }

        if (!_byReading.TryGetValue(reading, out var names))
        {
            _byReading[reading] = names = new(StringComparer.Ordinal);
        }

        if (!names.TryGetValue(key, out var entries))
        {
            names[key] = entries = [];
        }

        var root = new RootEntry(catalogueId, entryId);
        entries.Add(root);
        if (catalogueName is not null)
        {
            _catalogueNames.TryAdd(catalogueId, catalogueName.Trim());
        }

        if (preferred)
        {
            if (!_preferredByReading.TryGetValue(reading, out var fromPreferred))
            {
                _preferredByReading[reading] = fromPreferred = [];
            }

            fromPreferred.Add(root);
        }

        if (!_namesByReading.TryGetValue(reading, out var byEntry))
        {
            _namesByReading[reading] = byEntry = [];
        }

        if (!byEntry.TryGetValue(root, out var rootNames))
        {
            byEntry[root] = rootNames = new(StringComparer.Ordinal);
        }

        rootNames.Add(key);
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,128}\z", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex BattleScribeId();
}
