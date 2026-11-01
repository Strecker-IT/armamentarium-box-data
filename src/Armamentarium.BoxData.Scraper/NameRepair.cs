using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Armamentarium.BoxData.Scraper;

/// <summary>One entry the repair changes: renamed to <see cref="NewName"/>, or removed when it is null.</summary>
public sealed record NameRepairing(string Box, string Reading, string Name, string? NewName);

/// <summary>What <see cref="NameRepair.Apply"/> changed.</summary>
public sealed record NameRepairs(IReadOnlyList<NameRepairing> Renamed, IReadOnlyList<NameRepairing> Removed, IReadOnlyList<string> BoxesRemoved);

/// <summary>
/// Applies the reader's fixes of ADR 0004 Addendum ARMAM-598 and ADR 0003 Addendum ARMAM-610 to names the service wrote
/// before them, through <see cref="ContentLines.Tidy"/>: a name that is no model (excluded, a part, a weapon, a filler)
/// leaves its box, prose read into the name ("… accompanied by", "accompanying …", "… model", "sinister …") is cut, or the
/// entry leaves when the cut name is already in the reading. An upgrade set leaves whole. A reading left without entries goes, a box left without readings goes whole (the schema asks
/// for one) and comes back with the next run. Only gaps of service boxes: an entry with ids names a root entry, a box
/// written by hand is a decision (<see cref="IdBackfill.IsServiceBox"/>). With the catalogues, a gap whose name now names
/// the entry another line of its reading carries under its own name with the same count leaves too: two sources named one
/// model in two ways before a name rule joined them (ADR 0004 Addendum ARMAM-707). Builds the content of a draft the
/// maintainer merges.
/// </summary>
public static partial class NameRepair
{
    public static NameRepairs Apply(JsonNode table, IReadOnlySet<string> hosts, CatalogueIndex? catalogues = null)
    {
        var renamed = new List<NameRepairing>();
        var removed = new List<NameRepairing>();
        var boxesRemoved = new List<string>();
        var boxes = table["boxes"]!.AsArray();
        foreach (var box in boxes.OfType<JsonObject>().Where(b => IdBackfill.IsServiceBox(b, hosts)).ToList())
        {
            var boxName = (string)box["name"]!;

            // An upgrade set holds no model; it leaves whole and is not proposed again (ADR 0003 Addendum ARMAM-610).
            if (ProductCatalogue.IsUpgradeSet(boxName))
            {
                boxes.Remove(box);
                boxesRemoved.Add(boxName);
                continue;
            }

            if (box["readings"] is not JsonArray blocks)
            {
                continue;
            }

            foreach (var block in blocks.OfType<JsonObject>().ToList())
            {
                var reading = (string)block["reading"]!;
                var entries = block["entries"]!.AsArray();
                foreach (var entry in entries.OfType<JsonObject>().Where(IdBackfill.IsGap).ToList())
                {
                    var name = (string)entry["name"]!;
                    var cut = ContentLines.Tidy(name);
                    var twice = cut is not null && cut != name && entries.OfType<JsonObject>().Any(e => e != entry
                        && ContentLines.Normalize((string)e["name"]!) == ContentLines.Normalize(cut));
                    if (cut is null || twice || (catalogues is not null && IsTwin(entries, entry, reading, cut, catalogues)))
                    {
                        entries.Remove(entry);
                        removed.Add(new NameRepairing(boxName, reading, name, null));
                    }
                    else if (cut != name)
                    {
                        entry["name"] = cut;
                        renamed.Add(new NameRepairing(boxName, reading, name, cut));
                    }
                }

                if (entries.Count == 0)
                {
                    blocks.Remove(block);
                }
            }

            if (blocks.Count == 0)
            {
                boxes.Remove(box);
                boxesRemoved.Add(boxName);
            }
        }

        return new NameRepairs(renamed, removed, boxesRemoved);
    }

    // The gap names the root entry another line carries as that entry itself, not as an alias or a member, which carry their
    // target's ids under their own name (ADR 0004 Addenda ARMAM-642, ARMAM-688). Reading, role and count must match; which
    // role or count is right is not known otherwise, and candidates never match.
    private static bool IsTwin(JsonArray entries, JsonObject gap, string block, string name, CatalogueIndex catalogues)
    {
        string ReadingOf(JsonObject e) => (string?)e["reading"] ?? block;
        var reading = ReadingOf(gap);
        return gap["count"] is JsonValue count
            && !catalogues.IsAlias(reading, name)
            && catalogues.Find(reading, name) is { } ids
            && entries.OfType<JsonObject>().Any(e => e != gap
                && ReadingOf(e) == reading
                && (string?)e["role"] == (string?)gap["role"]
                && (string?)e["catalogueId"] == ids.CatalogueId
                && (string?)e["entryId"] == ids.EntryId
                && e["count"] is JsonValue other && (int)other == (int)count
                && (string?)e["name"] is { } own
                && !catalogues.IsAlias(reading, own)
                && catalogues.Find(reading, own) == ids);
    }
}
