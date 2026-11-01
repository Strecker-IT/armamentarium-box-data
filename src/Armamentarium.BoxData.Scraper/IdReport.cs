using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// What the catalogues of a run say about the table's ids (ADR 0004 Addendum ARMAM-598), for <c>--id-report</c>: per reading
/// the entries with and without ids, every gap's name with the reason it has none (no root entry, or several with their
/// catalogues and whether each is the entry's home), the gaps a run would now fill, and the names it would repair.
/// Reads only; one line per normalised name, the most frequent first.
/// </summary>
public static class IdReport
{
    private sealed record GapName(string Key, string Name, int Count, int InServiceBoxes, RootEntry? Hit, IReadOnlyList<Candidate> Candidates);

    public static void Write(JsonNode table, CatalogueIndex catalogues, IReadOnlySet<string> hosts, Action<string> log)
    {
        var entries = (
            from box in table["boxes"]!.AsArray().OfType<JsonObject>()
            from block in (box["readings"]?.AsArray() ?? []).OfType<JsonObject>()
            from entry in block["entries"]!.AsArray().OfType<JsonObject>()
            select (Box: box, Reading: (string?)entry["reading"] ?? (string)block["reading"]!, Entry: entry)).ToList();

        foreach (var reading in entries.Select(e => e.Reading).Distinct().Order(StringComparer.Ordinal))
        {
            var inReading = entries.Where(e => e.Reading == reading).ToList();
            var gaps = inReading.Where(e => IdBackfill.IsGap(e.Entry)).ToList();
            log($"== {reading}: {inReading.Count} entries, {inReading.Count - gaps.Count} with ids, {gaps.Count} gaps" +
                (catalogues.Unavailable.Contains(reading) ? " (reading unreadable this run)" : ""));

            var names = gaps
                .GroupBy(g => ContentLines.Normalize((string)g.Entry["name"]!), StringComparer.Ordinal)
                .Select(g =>
                {
                    var name = (string)g.First().Entry["name"]!;
                    return new GapName(g.Key, name, g.Count(), g.Count(e => IdBackfill.IsServiceBox(e.Box, hosts)),
                        catalogues.Find(reading, name), catalogues.Candidates(reading, name));
                })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            Section("would get ids now", names.Where(g => g.Hit is not null).ToList(),
                g => $"{g.Hit!.CatalogueId} {g.Hit.EntryId}" +
                     (g.InServiceBoxes < g.Count ? $", {g.Count - g.InServiceBoxes} in hand boxes stay gaps" : ""));
            Section("ambiguous", names.Where(g => g.Hit is null && g.Candidates.Count > 0).ToList(),
                g => string.Join("; ", g.Candidates.OrderBy(c => c.CatalogueName, StringComparer.Ordinal)
                    .Select(c => $"{c.CatalogueName} {c.Entry.CatalogueId} {c.Entry.EntryId} {(c.Home ? "home" : "linked")}")));
            Section("no hit", names.Where(g => g.Hit is null && g.Candidates.Count == 0).ToList(), g => g.Key);
        }

        var repairs = NameRepair.Apply(table.DeepClone(), hosts, catalogues);
        log($"== name repair: {repairs.Renamed.Count} renamed, {repairs.Removed.Count} removed, {repairs.BoxesRemoved.Count} box(es) removed");
        foreach (var one in repairs.Renamed.Concat(repairs.Removed))
        {
            log($"  {Text.Clean(one.Box)} | {one.Reading} | {Text.Clean(one.Name)} -> {(one.NewName is { } name ? Text.Clean(name) : "removed")}");
        }

        foreach (var box in repairs.BoxesRemoved)
        {
            log($"  box removed: {Text.Clean(box)}");
        }

        void Section(string title, IReadOnlyList<GapName> group, Func<GapName, string> detail)
        {
            log($"-- {title}: {group.Count} name(s), {group.Sum(g => g.Count)} entries");
            foreach (var one in group)
            {
                log($"  {one.Count} x {Text.Clean(one.Name)} | {Text.Clean(detail(one))}");
            }
        }
    }
}
