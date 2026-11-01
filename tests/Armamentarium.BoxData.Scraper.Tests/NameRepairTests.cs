using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper.Tests;

// The table below is written for these tests in the shapes the service wrote before ARMAM-598; none is copied from the table.
[TestClass]
public sealed class NameRepairTests
{
    private static readonly HashSet<string> Hosts = new(["shop.test"], StringComparer.OrdinalIgnoreCase);

    private static JsonNode Table(string host, params string[] names) =>
        Scraper.Table.Read($$"""
            {"format": 1, "boxes": [{
              "name": "Box",
              "productCodes": ["60010199001"],
              "sources": ["https://{{host}}/a", "https://shop.test/b"],
              "readings": [{"reading": "kt", "entries": [{{string.Join(",", names.Select(n => $$"""{"role": "team", "name": "{{n}}", "count": 2}"""))}}]}]
            }]}
            """);

    private static string[] Names(JsonNode table) =>
        table["boxes"]!.AsArray().SelectMany(b => b!["readings"]!.AsArray()).SelectMany(r => r!["entries"]!.AsArray())
            .Select(e => (string)e!["name"]!).ToArray();

    [TestMethod]
    public void Apply_ClauseWordsReadBeforeARMAM598_AreCut()
    {
        var table = Table("shop.test", "Rust Wasps accompanied by", "accompanying Rust Drones", "Rust Spells usable by Rust wizards", "Iron Strider");

        var repairs = NameRepair.Apply(table, Hosts);

        CollectionAssert.AreEqual(new[] { "Rust Wasps", "Rust Drones", "Rust Spells", "Iron Strider" }, Names(table));
        Assert.AreEqual(3, repairs.Renamed.Count);
        Assert.AreEqual(new NameRepairing("Box", "kt", "Rust Wasps accompanied by", "Rust Wasps"), repairs.Renamed[0]);
    }

    [TestMethod]
    public void Apply_ExcludedName_IsRemoved()
    {
        var table = Table("shop.test", "double-sided gaming boards", "Iron Strider");

        var repairs = NameRepair.Apply(table, Hosts);

        CollectionAssert.AreEqual(new[] { "Iron Strider" }, Names(table));
        Assert.AreEqual(new NameRepairing("Box", "kt", "double-sided gaming boards", null), repairs.Removed.Single());
    }

    [TestMethod]
    public void Apply_BoxLeftWithoutAnEntry_IsRemovedWhole()
    {
        var table = Table("shop.test", "double-sided gaming boards");

        var repairs = NameRepair.Apply(table, Hosts);

        Assert.AreEqual(0, table["boxes"]!.AsArray().Count);
        CollectionAssert.AreEqual(new[] { "Box" }, repairs.BoxesRemoved.ToArray());
    }

    [TestMethod]
    public void Apply_HandBox_IsLeftAlone()
    {
        var table = Table("review.test", "double-sided gaming boards", "Rust Wasps accompanied by");

        var repairs = NameRepair.Apply(table, Hosts);

        Assert.AreEqual(0, repairs.Removed.Count + repairs.Renamed.Count);
        Assert.AreEqual(2, Names(table).Length);
    }

    [TestMethod]
    public void Apply_CutNameAlreadyInTheReading_IsRemoved()
    {
        var table = Table("shop.test", "Rust Wasps", "Rust Wasps accompanied by");

        var repairs = NameRepair.Apply(table, Hosts);

        CollectionAssert.AreEqual(new[] { "Rust Wasps" }, Names(table));
        Assert.AreEqual(new NameRepairing("Box", "kt", "Rust Wasps accompanied by", null), repairs.Removed.Single());
    }

    [TestMethod]
    public void Apply_NamesWithOtherStopWords_AreKept()
    {
        var table = Table("shop.test", "Lord on Rust Steed", "XV26 Rust Suits", "Guard by the Gate");

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts).Renamed.Count);
    }

    [TestMethod]
    public void Apply_UpgradeSet_IsRemovedWhole()
    {
        var table = Table("shop.test", "Rust Wardens");
        table["boxes"]![0]!["name"] = "Rust Templars: Upgrades and Transfers";

        var repairs = NameRepair.Apply(table, Hosts);

        Assert.AreEqual(0, table["boxes"]!.AsArray().Count);
        CollectionAssert.AreEqual(new[] { "Rust Templars: Upgrades and Transfers" }, repairs.BoxesRemoved.ToArray());
    }

    [TestMethod]
    public void Apply_ReaderRulesOfARMAM610_AreApplied()
    {
        var table = Table("shop.test", "Blade Warden model", "heavy bolters", "of the following miniatures", "sinister Gaunt Seer", "Iron Strider");

        var repairs = NameRepair.Apply(table, Hosts);

        CollectionAssert.AreEqual(new[] { "Blade Warden", "Gaunt Seer", "Iron Strider" }, Names(table));
        Assert.AreEqual(2, repairs.Removed.Count);
    }

    // ARMAM-707: a gap read before a name rule (here "Primaris", ADR 0004 Addendum ARMAM-688) beside the line the rule now
    // names. The catalogue and boxes are written for these tests; none is copied from BSData or the committed table.
    private const string IronCatalogue = """
        <catalogue id="cat-iron" name="Iron Brothers" library="false">
          <selectionEntries>
            <selectionEntry type="model" id="se-sergeant" name="Sergeant"/>
            <selectionEntry type="unit" id="se-squad" name="Iron Squad">
              <selectionEntries>
                <selectionEntry type="model" id="se-coordinator" name="Coordinator"/>
              </selectionEntries>
            </selectionEntry>
          </selectionEntries>
        </catalogue>
        """;

    private static CatalogueIndex Iron()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("kt", "Iron.cat", System.Text.Encoding.UTF8.GetBytes(IronCatalogue));
        return index;
    }

    private static string Gap(string name, int count) => $$"""{"role": "team", "name": "{{name}}", "count": {{count}}}""";

    private static string Listed(string name, string entryId, int count) =>
        $$"""{"role": "team", "catalogueId": "cat-iron", "entryId": "{{entryId}}", "name": "{{name}}", "count": {{count}}}""";

    private static JsonNode Box(string host, params string[] entries) =>
        Scraper.Table.Read($$"""
            {"format": 1, "boxes": [{
              "name": "Box",
              "productCodes": ["60010199001"],
              "sources": ["https://{{host}}/a", "https://shop.test/b"],
              "readings": [{"reading": "kt", "entries": [{{string.Join(",", entries)}}]}]
            }]}
            """);

    [TestMethod]
    public void Apply_GapNamingAnEntryTheReadingListsWithItsCount_IsRemoved()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 1), Listed("Sergeant", "se-sergeant", 1));

        var repairs = NameRepair.Apply(table, Hosts, Iron());

        CollectionAssert.AreEqual(new[] { "Sergeant" }, Names(table));
        Assert.AreEqual(new NameRepairing("Box", "kt", "Primaris Sergeant", null), repairs.Removed.Single());
    }

    [TestMethod]
    public void Apply_GapNamingAListedEntryWithAnotherCount_Stays()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 2), Listed("Sergeant", "se-sergeant", 1));

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
        Assert.AreEqual(2, Names(table).Length);
    }

    [TestMethod]
    public void Apply_GapThatIsAnAlias_Stays()
    {
        var table = Box("shop.test", Gap("Brother Aldo", 1), Listed("Sergeant", "se-sergeant", 1));
        var index = Iron();
        index.UseAliases([new Alias("kt", "Brother Aldo", "Sergeant", "Iron Brothers")]);

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, index).Removed.Count);
    }

    // An alias line carries its target's ids under its own name (ADR 0004 Addendum ARMAM-642): it is one model, not the entry.
    [TestMethod]
    public void Apply_GapBesideAnAliasLineOfItsEntry_Stays()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 1), Listed("Brother Aldo", "se-sergeant", 1));
        var index = Iron();
        index.UseAliases([new Alias("kt", "Brother Aldo", "Sergeant", "Iron Brothers")]);

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, index).Removed.Count);
    }

    // A member carries its unit's ids (ADR 0004 Addendum ARMAM-688); its own name names no root entry.
    [TestMethod]
    public void Apply_GapBesideAMemberLineOfItsUnit_Stays()
    {
        var table = Box("shop.test", Gap("Iron Squad", 1), Listed("Coordinator", "se-squad", 1));

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
    }

    [TestMethod]
    public void Apply_GapWithCountCandidates_Stays()
    {
        var table = Box("shop.test",
            """{"role": "team", "name": "Primaris Sergeant", "countCandidates": [1, 2]}""",
            """{"role": "team", "catalogueId": "cat-iron", "entryId": "se-sergeant", "name": "Sergeant", "countCandidates": [1, 2]}""");

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
    }

    [TestMethod]
    public void Apply_GapBesideALineWithCountCandidates_Stays()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 1),
            """{"role": "team", "catalogueId": "cat-iron", "entryId": "se-sergeant", "name": "Sergeant", "countCandidates": [1, 2]}""");

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
    }

    // Which role is right is not known; the gap stays.
    [TestMethod]
    public void Apply_GapOfAnotherRole_Stays()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 1).Replace("\"team\"", "\"extra\""), Listed("Sergeant", "se-sergeant", 1));

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
    }

    // An entry may name its own reading (schema); a line of another reading is no twin.
    [TestMethod]
    public void Apply_GapBesideALineOfAnotherReading_Stays()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 1),
            Listed("Sergeant", "se-sergeant", 1).Replace("{\"role\"", "{\"reading\": \"40k\", \"role\""));

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
    }

    [TestMethod]
    public void Apply_GapOfAHandBox_Stays()
    {
        var table = Box("review.test", Gap("Primaris Sergeant", 1), Listed("Sergeant", "se-sergeant", 1));

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts, Iron()).Removed.Count);
    }

    [TestMethod]
    public void Apply_WithoutCatalogues_KeepsTheGap()
    {
        var table = Box("shop.test", Gap("Primaris Sergeant", 1), Listed("Sergeant", "se-sergeant", 1));

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts).Removed.Count);
    }
}
