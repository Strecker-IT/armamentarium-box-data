using System.IO.Compression;
using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

// The catalogues below are written for these tests in the shapes BattleScribe uses; none is copied from BSData.
[TestClass]
public sealed class CatalogueIndexTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <catalogue id="cat-iron" name="Iron Brothers" library="false" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <selectionEntries>
            <selectionEntry type="unit" id="se-captain" name="Captain">
              <selectionEntries>
                <selectionEntry type="model" id="se-inner" name="Bodyguard"/>
              </selectionEntries>
            </selectionEntry>
            <selectionEntry type="upgrade" id="se-relic" name="Relic Blade"/>
          </selectionEntries>
          <entryLinks>
            <entryLink id="el-troopers" name="Line Troopers" targetId="lib-troopers" type="selectionEntry"/>
          </entryLinks>
          <sharedSelectionEntries>
            <selectionEntry type="unit" id="se-shared" name="Shared Hero"/>
          </sharedSelectionEntries>
        </catalogue>
        """;

    private const string Json = """
        {"catalogue": {"id": "cat-iron", "name": "Iron Brothers", "library": false,
          "selectionEntries": [{"type": "unit", "id": "se-captain", "name": "Captain",
            "selectionEntries": [{"type": "model", "id": "se-inner", "name": "Bodyguard"}]}],
          "entryLinks": [{"id": "el-troopers", "name": "Line Troopers", "targetId": "lib-troopers", "type": "selectionEntry"}]}}
        """;

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [TestMethod]
    public void Xml_RootUnitsAndLinks_AreFoundNestedAndUpgradesAreNot()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("40k", "Line Trooper"));
        Assert.IsNull(index.Find("40k", "Bodyguard"));
        Assert.IsNull(index.Find("40k", "Relic Blade"));
        Assert.IsNull(index.Find("40k", "Shared Hero"));
        Assert.IsNull(index.Find("kt", "Captain"));
    }

    [TestMethod]
    public void Json_ReadsLikeXml()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.json", Bytes(Json));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("40k", "Line Troopers"));
        Assert.IsNull(index.Find("40k", "Bodyguard"));
    }

    [TestMethod]
    public void SamePairFromTwoEditions_IsOneHit()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));
        index.AddCatalogue("40k", "Iron Brothers.json", Bytes(Json));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void NameInTwoCatalogues_IsNoHit()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));
        index.AddCatalogue("40k", "Greenskins.cat", Bytes(Xml.Replace("cat-iron", "cat-green").Replace("se-captain", "se-boss")));

        Assert.IsNull(index.Find("40k", "Captain"));
    }

    // Brood Brothers: Genestealer Cults link the unit of another army at their root (ADR 0004 Addendum ARMAM-598).
    private const string Linking = """
        <catalogue id="cat-cult" name="Cult" library="false" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <entryLinks>
            <entryLink id="el-captain" name="Captain" targetId="se-captain" type="selectionEntry"/>
            <entryLink id="el-guard" name="Guard Squad" targetId="se-guard" type="selectionEntry"/>
          </entryLinks>
        </catalogue>
        """;

    // As BSData keeps most units: the army links the entry at its root, its library defines it.
    private const string Army = """
        <catalogue id="cat-army" name="Imperium - Army" library="false" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <entryLinks>
            <entryLink id="el-guard" name="Guard Squad" targetId="se-guard" type="selectionEntry"/>
          </entryLinks>
        </catalogue>
        """;

    private const string ArmyLibrary = """
        <catalogue id="cat-army-lib" name="Imperium - Army - Library" library="true" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <sharedSelectionEntries>
            <selectionEntry type="unit" id="se-guard" name="Guard Squad"/>
          </sharedSelectionEntries>
        </catalogue>
        """;

    private static CatalogueIndex Index(params (string File, string Xml)[] catalogues)
    {
        var index = new CatalogueIndex();
        foreach (var (file, xml) in catalogues)
        {
            index.AddCatalogue("40k", file, Bytes(xml));
        }

        return index;
    }

    [TestMethod]
    public void SameEntryInItsHomeAndLinkedElsewhere_TheHomeWins()
    {
        var index = Index(("Iron Brothers.cat", Xml), ("Cult.cat", Linking));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void SameEntryDefinedInALibrary_TheCatalogueOfThatLibraryWins()
    {
        var index = Index(("Army.cat", Army), ("Army Library.cat", ArmyLibrary), ("Cult.cat", Linking));

        Assert.AreEqual(new RootEntry("cat-army", "se-guard"), index.Find("40k", "Guard Squad"));
    }

    [TestMethod]
    public void SameEntryWithoutAHome_IsNoHit()
    {
        var index = Index(("Cult.cat", Linking), ("Other Cult.cat", Linking.Replace("cat-cult", "cat-other")));

        Assert.IsNull(index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void PreferredSource_WinsOverTheOthers()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("aos", "Iron Brothers.cat", Bytes(Xml));
        index.AddCatalogue("aos", "Spear.cat", Bytes(Xml.Replace("cat-iron", "sph-catalogue::iron").Replace("se-captain", "sph::box::01-captain")), preferred: true);

        Assert.AreEqual(new RootEntry("sph-catalogue::iron", "sph::box::01-captain"), index.Find("aos", "Captain"));
    }

    [TestMethod]
    public void NameWithoutTheUnitWord_FindsTheSquad()
    {
        var index = Index(("Army.cat", Army), ("Army Library.cat", ArmyLibrary));

        Assert.AreEqual(new RootEntry("cat-army", "se-guard"), index.Find("40k", "Guards"));
    }

    [TestMethod]
    public void NameWithTheArmyBeforeIt_FindsItInThatArmy()
    {
        var index = Index(("Army.cat", Army), ("Army Library.cat", ArmyLibrary), ("Iron Brothers.cat", Xml));

        Assert.AreEqual(new RootEntry("cat-army", "se-guard"), index.Find("40k", "Army Guard Squad"));
        Assert.IsNull(index.Find("40k", "Cult Captain"));
    }

    [TestMethod]
    public void CatalogueNameStartingWithTheName_CountsFromTwoWords()
    {
        var index = Index(("Iron Brothers.cat", Xml.Replace("name=\"Line Troopers\"", "name=\"Line Troopers with Banner\"")));

        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("40k", "Line Troopers"));
        Assert.IsNull(index.Find("40k", "Line"));
    }

    private const string Chaplains = """
        <?xml version="1.0" encoding="utf-8"?>
        <catalogue id="cat-iron" name="Iron Brothers" library="false" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <selectionEntries>
            <selectionEntry type="model" id="se-chap" name="Chaplain"/>
            <selectionEntry type="model" id="se-chapbike" name="Chaplain on Bike"/>
            <selectionEntry type="model" id="se-psyker" name="Primaris Psyker"/>
            <selectionEntry type="unit" id="se-intercessors" name="Intercessor Squad"/>
          </selectionEntries>
        </catalogue>
        """;

    [TestMethod]
    public void NameWithPrimaris_FindsTheEntryTheCataloguesNameWithoutIt()
    {
        var index = Index(("Iron Brothers.cat", Chaplains));

        Assert.AreEqual(new RootEntry("cat-iron", "se-chapbike"), index.Find("40k", "Primaris Chaplain on Bike"));
        Assert.AreEqual(new RootEntry("cat-iron", "se-chap"), index.Find("40k", "Primaris Chaplain"));
        Assert.AreEqual(new RootEntry("cat-iron", "se-psyker"), index.Find("40k", "Primaris Psyker"));
        Assert.AreEqual(new RootEntry("cat-iron", "se-intercessors"), index.Find("40k", "Primaris Intercessors"));
    }

    [TestMethod]
    public void NameWithPrimaris_CountsOnlyTheWholeRestOfTheName()
    {
        var index = Index(("Iron Brothers.cat", Chaplains.Replace("<selectionEntry type=\"model\" id=\"se-chap\" name=\"Chaplain\"/>", "")));

        Assert.IsNull(index.Find("40k", "Primaris Chaplain"));
    }

    [TestMethod]
    public void Check_IdsSetByARule_AreValid()
    {
        var index = Index(("Army.cat", Army), ("Army Library.cat", ArmyLibrary));

        Assert.AreEqual(IdState.Valid, index.Check("40k", new RootEntry("cat-army", "se-guard"), "Army Guard Squad"));
    }

    [TestMethod]
    public void Candidates_NameEveryRootEntryWithCatalogueAndWhetherItIsTheHome()
    {
        var index = Index(("Iron Brothers.cat", Xml), ("Cult.cat", Linking));

        var candidates = index.Candidates("40k", "Captains").OrderBy(c => c.Entry.CatalogueId, StringComparer.Ordinal).ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                new Candidate(new RootEntry("cat-cult", "se-captain"), "Cult", false),
                new Candidate(new RootEntry("cat-iron", "se-captain"), "Iron Brothers", true),
            },
            candidates);
        Assert.AreEqual(0, index.Candidates("40k", "Nobody").Count);
    }

    [TestMethod]
    public void Candidates_FollowTheStepsOfFind()
    {
        var index = Index(("Army.cat", Army), ("Army Library.cat", ArmyLibrary));

        Assert.AreEqual(new RootEntry("cat-army", "se-guard"), index.Candidates("40k", "Guards").Single().Entry);
    }

    [TestMethod]
    public void OperativeWordsAndPlural_AreIgnored()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("kt", "Iron Brothers.cat", Bytes(Xml));

        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("kt", "Line Troopers Kill Team operatives"));
    }

    [TestMethod]
    public void LibraryAndGameSystem_AreNoCatalogues()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Library.cat", Bytes(Xml.Replace("library=\"false\"", "library=\"true\"")));
        index.AddCatalogue("40k", "System.json", Bytes("""{"gameSystem": {"id": "gst", "name": "System"}}"""));

        Assert.IsNull(index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void UnavailableReading_FindsNothing()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("aos", "Iron Brothers.cat", Bytes(Xml));
        index.MarkUnavailable("aos");

        Assert.IsNull(index.Find("aos", "Captain"));
    }

    [TestMethod]
    public void Check_PairAndName_AreHeldAgainstTheRootEntries()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));

        Assert.AreEqual(IdState.Valid, index.Check("40k", new RootEntry("cat-iron", "se-captain"), "Captains"));
        Assert.AreEqual(IdState.Valid, index.Check("40k", new RootEntry("cat-iron", "lib-troopers"), "Line Trooper"));
        Assert.AreEqual(IdState.Renamed, index.Check("40k", new RootEntry("cat-iron", "se-captain"), "Lieutenant"));
        Assert.AreEqual(IdState.Orphaned, index.Check("40k", new RootEntry("cat-iron", "se-inner"), "Bodyguard"));
        Assert.AreEqual(IdState.Orphaned, index.Check("40k", new RootEntry("cat-iron", "se-shared"), "Shared Hero"));
        Assert.AreEqual(IdState.Orphaned, index.Check("40k", new RootEntry("cat-other", "se-captain"), "Captain"));
    }

    [TestMethod]
    public void Check_UnreadableOrUnreadReading_IsUnchecked()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));
        index.MarkUnavailable("40k");

        Assert.AreEqual(IdState.Unchecked, index.Check("40k", new RootEntry("cat-iron", "se-captain"), "Captain"));
        Assert.AreEqual(IdState.Unchecked, index.Check("aos", new RootEntry("cat-iron", "se-captain"), "Captain"));
    }

    [TestMethod]
    public void DocumentTypeDeclaration_IsUnreadable()
    {
        var index = new CatalogueIndex();
        var withDtd = Xml.Replace(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE catalogue [<!ENTITY a \"aaaa\">]>");

        Assert.IsFalse(index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(withDtd)));
        Assert.IsNull(index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void SpearheadIds_AreRead()
    {
        var index = new CatalogueIndex();

        Assert.IsTrue(index.AddCatalogue("aos", "Skaven.cat", Bytes(
            Xml.Replace("cat-iron", "sph-catalogue::skaven").Replace("se-captain", "sph::clawpack::01-grey-seer"))));
        Assert.AreEqual(new RootEntry("sph-catalogue::skaven", "sph::clawpack::01-grey-seer"), index.Find("aos", "Captain"));
    }

    [TestMethod]
    public void BrokenCatalogueOrStrangeId_IsUnreadable()
    {
        var index = new CatalogueIndex();

        Assert.IsFalse(index.AddCatalogue("40k", "Broken.cat", Bytes("<catalogue id=\"x\"")));
        Assert.IsFalse(index.AddCatalogue("40k", "Broken.json", Bytes("{\"catalogue\": ")));
        Assert.IsFalse(index.AddCatalogue("40k", "Odd.cat", Bytes(Xml.Replace("se-captain", "<b>x</b>".Replace("<", "&lt;")))));
        Assert.IsTrue(index.AddCatalogue("40k", "renovate.json", Bytes("{\"extends\": []}")));
    }

    [TestMethod]
    public void Archive_WithAnUnreadableCatalogue_LeavesTheReadingWithoutIds()
    {
        using var zip = Zip(("repo-main/Iron Brothers.cat", Xml), ("repo-main/Broken.cat", "<catalogue"));
        var index = new CatalogueIndex();
        var log = new List<string>();

        index.AddArchive("40k", zip, maxEntryBytes: 1 << 20, log.Add);

        Assert.IsNull(index.Find("40k", "Captain"));
        Assert.IsTrue(log.Any(l => l.Contains("Broken.cat", StringComparison.Ordinal)));
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    [TestMethod]
    public async Task Load_ReadsGoodSourcesAndLeavesFailingOnesWithoutIds()
    {
        using var zip = Zip(("r-main/Iron Brothers.cat", Xml));
        var bytes = zip.ToArray();
        var http = new HttpClient(new Stub(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://codeload.github.com/o/good/zip/refs/heads/main" => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
            "https://api.github.com/repos/o/badtag/releases/latest" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":\"..\"}") },
            _ => new(System.Net.HttpStatusCode.NotFound),
        }));
        var log = new List<string>();

        var index = await CatalogueIndex.LoadAsync(
            http, log.Add, default,
            [new("40k", "o", "good", "main"), new("kt", "o", "badtag", null), new("aos", "o", "missing", "main"), new("aos", "o", "good", "main")]);

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.IsNull(index.Find("aos", "Captain"));
        Assert.IsTrue(log.Any(l => l.StartsWith("kt:", StringComparison.Ordinal) && l.Contains("unreadable", StringComparison.Ordinal)));
        CollectionAssert.AreEquivalent(new[] { "kt", "aos" }, index.Unavailable.ToArray());
    }

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        zip.Position = 0;
        return zip;
    }

    [TestMethod]
    public void Archive_AnEntryOverTheLimit_LeavesTheReadingWithoutIds()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[]
            {
                ("repo-main/Iron Brothers.cat", Xml),
                ("repo-main/Big.cat", Xml.Replace("cat-iron", "cat-big").Replace("Captain", "Warlord") + new string(' ', 2048)),
                ("repo-main/renovate.json", "{\"extends\": []}"),
            })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        var index = new CatalogueIndex();
        var log = new List<string>();
        zip.Position = 0;
        index.AddArchive("40k", zip, maxEntryBytes: 2048, log.Add);

        Assert.IsNull(index.Find("40k", "Captain"));
        Assert.IsNull(index.Find("40k", "Warlord"));
        Assert.IsTrue(log.Any(l => l.Contains("Big.cat", StringComparison.Ordinal)));
    }

    private const string UnitXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <catalogue id="cat-rust" name="Rust Army - Library" library="true" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <sharedSelectionEntries>
            <selectionEntry type="unit" id="lib-guns" name="Rust Gun Squad">
              <selectionEntryGroups>
                <selectionEntryGroup id="g1" name="3 Rust Gunners">
                  <selectionEntries>
                    <selectionEntry type="model" id="m1" name="Rust Gunners w/ Lascannon"/>
                    <selectionEntry type="model" id="m2" name="Rust Gunners w/ Flamer"/>
                  </selectionEntries>
                </selectionEntryGroup>
                <selectionEntryGroup id="g2" name="Fire Warden">
                  <selectionEntries>
                    <selectionEntry type="model" id="m3" name="Fire Warden"/>
                  </selectionEntries>
                </selectionEntryGroup>
              </selectionEntryGroups>
            </selectionEntry>
          </sharedSelectionEntries>
        </catalogue>
        """;

    private const string UnitLinkXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <catalogue id="cat-rust-army" name="Rust Army" library="false" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <entryLinks>
            <entryLink id="el-guns" name="Rust Gun Squad" targetId="lib-guns" type="selectionEntry"/>
          </entryLinks>
        </catalogue>
        """;

    private const string UnitJson = """
        {"catalogue": {"id": "cat-rust", "name": "Rust Army - Library", "library": true,
          "sharedSelectionEntries": [{"type": "unit", "id": "lib-guns", "name": "Rust Gun Squad",
            "selectionEntryGroups": [{"id": "g1", "name": "3 Rust Gunners",
              "selectionEntries": [{"type": "model", "id": "m1", "name": "Rust Gunners w/ Lascannon"}]},
              {"id": "g2", "name": "Fire Warden", "selectionEntries": [{"type": "model", "id": "m3", "name": "Fire Warden"}]}]}]}}
        """;

    // ARMAM-688: a model of a unit, named as the catalogue names it or without its "w/ …" gear, is a member of that unit.
    [TestMethod]
    public void IsMember_ModelsOfTheUnit_AreMembersInXmlAndJson()
    {
        foreach (var (file, library) in new[] { ("Rust Army - Library.cat", UnitXml), ("Rust Army - Library.json", UnitJson) })
        {
            var index = new CatalogueIndex();
            index.AddCatalogue("40k", file, Bytes(library));
            index.AddCatalogue("40k", "Rust Army.cat", Bytes(UnitLinkXml));
            var unit = index.Find("40k", "Rust Gun Squad")!;

            Assert.IsTrue(index.IsMember("40k", unit, "Fire Warden"), file);
            Assert.IsTrue(index.IsMember("40k", unit, "Rust Gunners"), file);
            Assert.IsFalse(index.IsMember("40k", unit, "Rust"), file);
            Assert.IsFalse(index.IsMember("40k", unit, "Iron Brothers"), file);
            Assert.IsFalse(index.IsMember("kt", unit, "Fire Warden"), file);
            Assert.AreEqual(IdState.Valid, index.Check("40k", unit, "Fire Warden"), file);
            Assert.AreEqual(IdState.Renamed, index.Check("40k", unit, "Iron Brothers"), file);
        }
    }

    [TestMethod]
    public void IsUnitName_NamesWithAUnitWord()
    {
        Assert.IsTrue(CatalogueIndex.IsUnitName("Rust Gun Squad"));
        Assert.IsTrue(CatalogueIndex.IsUnitName("Rust Kill Team"));
        Assert.IsFalse(CatalogueIndex.IsUnitName("Rust Chieftain"));
        Assert.IsFalse(CatalogueIndex.IsUnitName("Squadron Leader"));
    }
}
