# ADR 0004 — Auto-merge and catalogue ids by name, until the table is filled

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-593). Founded on Armamentarium ADR 0118 Addendum ARMAM-593 and the
named exception to "Datenintegrität vor Bequemlichkeit" in Armamentarium's `CLAUDE.md`. Overrides parts of ADR 0003
(§2 daily, §10 drafts without ids, §12 five open while `--auto-merge` runs, Rejected "Ids by exact name match").

**Widened by ADR 0003 Addendum ARMAM-599:** the hit of §2 also chooses a build sentence over a source's lines and lets two
names of one root entry confirm each other.

**Changed by Addendum ARMAM-597:** §2 "at the start of a run … downloads the catalogues" holds for every run, with or
without the switch, and the ids the table already holds are checked against them; §1 "each pull request is opened ready
… merged at once" does not hold for the draft that removes orphaned ids, which the service never merges.

**Changed by Addendum ARMAM-603:** §1 "merged at once": a merge GitHub refuses with 405 is tried again, three times in
all, five seconds apart.

**Changed by Addendum ARMAM-598:** §2 "exactly one" pair: a name names a root entry also through a unit word, an army
before it or a longer root name, and of several pairs the preferred source's or the entry's home wins; Addendum ARMAM-597
"No widening of the named exception" and "Moved ids are not set again": gaps of service boxes get the ids their names name
in every run (`scraper/backfill-ids`), a moved id among them, and names read before the reader's fixes are repaired in a
never-merged draft (`scraper/fix-names`).

**Changed by ADR 0003 Addendum ARMAM-610:** the names repaired in `scraper/fix-names` follow the reader's rule of that
addendum (prose cut, parts, weapons and filler removed), and a service box named as an upgrade set goes whole, also with
ids, and is not proposed again, unlike a box left without entries.

**Changed by Addendum ARMAM-642:** §2 and Addendum ARMAM-598 "Rules of a hit": a name of `aliases.json` goes to its
target before any rule; Addendum ARMAM-598 "no root entry is listed twice in a reading" does not hold for an alias, which
gets the ids beside the other models of its entry.

**Changed by Addendum ARMAM-688:** §2 "exactly one" pair also reaches a member of a built unit: a model the catalogue
lists under the unit a build sentence hits gets that unit's ids, one line per model beside the others; the id check
counts a member's pair as valid. A name starting with "Primaris" also hits by the rest of its name.

**Changed by Addendum ARMAM-707:** Addendum ARMAM-598 "Gaps get ids": a gap whose hit its reading already lists stays a
gap there; it leaves its box in `scraper/fix-names` when the line of that entry states the same count.

**Changed by Addendum ARMAM-708:** §3 `tow` comes from `BSData/warhammer-the-old-world`, latest release, not from
`vflam/Warhammer-The-Old-World`.

**Changed by Addendum ARMAM-738:** the own repositories moved to the organisation `Strecker-IT`; §3 `aos` reads
`Strecker-IT/armamentarium-spearhead-data`, and the service opens its pull requests in `Strecker-IT/armamentarium-box-data`.

## Context

The service runs on the server since 2026-10-17 (ARMAM-593). Its drafts wait for the maintainer to add catalogue ids,
readings and roles before merging. The maintainer does not know catalogue ids and cannot add them by hand; with five
open drafts every run stops at once, so the table would not grow. The maintainer wants the table filled first and to
merge by hand again afterwards.

Measured on 2026-10-17: the catalogues of every reading are public on GitHub (archives of 0.2 to 4 MB). Exact name hits are
few, but where a shop line names a catalogue root entry exactly, the ids equal those checked by hand for the first data
(ARMAM-588): Murderwing's Champion and Shrieker, Celestian Insidiants' Denuncia and Cremators, the Ripper Swarm of Terror
on Devlan. A name found in more than one catalogue (`Superior`) gets none.

## Decision

1. **Switch:** `--auto-merge` on the command line (the unit's `ExecStart`). With it, each pull request is opened ready,
   not as a draft, merged at once through the REST API (`PUT pulls/{n}/merge`) and its branch deleted; the next box is
   built on the merged table. Without it, the service behaves as ADR 0003 says: drafts, at most five open. The maintainer
   removes the switch when the table holds enough; no rule in the code decides that.
2. **Ids by name:** at the start of a run the service downloads the catalogues of every reading from a compiled allowlist
   and reads their root entries (a root `selectionEntry` of type unit or model, a root `entryLink` to a `selectionEntry`
   with its own name and its target; libraries and `sharedSelectionEntries` are no root offer). An entry gets
   `catalogueId` and `entryId` when its normalised name (ADR 0003 §9) names **exactly one** pair of catalogue id and entry
   id within its reading; one name in two catalogues, or none, gets no ids. The 10th and the 11th edition share their ids,
   so the same pair from both counts once.
3. **Catalogue sources** (compiled, matching the app's `BsDataEditionSources`; a change there is a change here):

   | Reading | Repository | Reference |
   |---|---|---|
   | `40k` | `BSData/wh40k-10e` (XML), `BSData/wh40k-11e` (JSON) | `main` |
   | `kt` | `BSData/wh40k-killteam` | latest release |
   | `aos` | `BSData/age-of-sigmar-4th`, `LordTyberias/armamentarium-spearhead-data` | latest release, `main` |
   | `tow` | `vflam/Warhammer-The-Old-World` | latest release |
   | `hh` | `BSData/horus-heresy-3rd-edition` (JSON) | `main` |

   Archives come from `codeload.github.com` (release tags from `api.github.com`), with the run's client: no redirect, at
   most 64 MB, each catalogue file at most 32 MB, read in memory and never unpacked. BattleScribe JSON is read with
   `System.Text.Json`, XML with `System.Xml.Linq` and no document type declaration; no package. A source that cannot be
   read, or holds a catalogue file that is too large, not well-formed, or carries an id outside the forms the sources use
   (letters, digits, `-`, `_`, `.`, `:`, at most 128 characters: BSData's hex groups, the Spearhead repository's
   `sph::box::01-name`), leaves its whole reading without ids for this run (a missing file could hide a twin of a name),
   logged; the run goes on.
4. **Gaps are merged:** a box whose entries do not all get ids is merged anyway; an entry without ids is a name and a
   count, shown, not addable (README, Entry).
5. **Roles and readings** stay as ADR 0003 §10 sets them (`team`; `npo` in `kt` and `extra` elsewhere where the line says
   non-player; readings from the product's game systems), now unchecked.
6. **Pace:** the timer runs on the 1st and the 15th of every month at 03:00. Candidates are only products without a row,
   so a run adds only what is new.
7. **Token:** without expiry (maintainer decision), replacing "at most 90 days". Contents and pull requests, this repository
   only, as before.
8. **Combat Patrols** keep `generation` only (Armamentarium ADR 0118 §5); whether they get entries with ids from the
   Combat Patrol repository is decided with Armamentarium ARMAM-594.

## Consequences

- No hand check while the switch is on. A name that is unique but means another kit (Battleclade: 10 models in the box,
  9 in the unit) gets the unit's ids unchecked; the README rule "a kit counts as a unit only when it holds exactly that
  unit's models" holds only for hand-made rows meanwhile.
- A stolen token writes to the table unchecked while the switch is on and does not expire.
- Many entries stay without ids: shop names seldom equal catalogue names.
- The app bundles the table at a fixed commit (Armamentarium ADR 0118 Addendum ARMAM-589); new rows reach it with the next
  pin.

## Rejected

- **Ids by the most similar name:** more ids, but wrong ones would be merged unchecked.
- **Merging only complete boxes:** incomplete ones would block the five open, and nobody can complete them by hand.
- **A local script on the maintainer's computer, or the app's catalogue cache:** the maintainer wants it all on the
  server, nothing from his computer or the app.
- **GraphQL `markPullRequestReadyForReview`:** a ready pull request from the start needs no second API.
- **Ids for 40k from the 10th edition only:** units new in the 11th edition would get none.

## Addendum ARMAM-597 — the ids the table holds are checked every run (2026-10-18)

BSData changes its catalogues all the time; an id once merged can vanish, move or come to name another entry, and no run
looked at it again. Measured on 2026-10-18 against the online catalogues: 64 boxes, 207 pairs of catalogue id and entry id
(130 fixed entries, the rest options of a choice), all still root entries of their name.

- **Every run, every id:** at the start of each run, with or without `--auto-merge`, the service loads the catalogues of
  §3 and holds every pair the table carries against them: fixed entries and each option of a choice, hand-made rows as
  well as merged ones, in the entry's own `reading` where it names one (a Kill Team NPO falling back to a 40k entry). Ids
  by name (§2) are still set only with the switch.
- **States:** *valid* (the pair is a root entry of the reading in the sense of §2 and the entry's name, normalised as in
  ADR 0003 §9, is one of its names); *orphaned* (the pair is no such root entry); *renamed* (the pair is one, under another
  name); *unchecked* (the reading could not be read whole this run, §3: a missing source could hold the pair).
- **Journal:** one line per orphaned or renamed pair and one summary (`id check: N pair(s), V valid, O orphaned, R renamed;
  unchecked: …`), also when five scraper pull requests are open and the run ends at once.
- **Orphaned ids go into a draft:** branch `scraper/stale-ids`, the table without the orphaned pairs; such a fixed entry,
  or a choice left with fewer than two options (the schema asks for two), keeps role, name and count and becomes a gap. The
  draft is **never merged by the service**, also with `--auto-merge`; the maintainer's merge is the hand check. It counts
  among the five open (ADR 0003 §12); while it is open, or when no slot is free, the run only reports. The branch name is
  fixed: when opening fails although no pull request of it is open, a branch is left from a draft merged or closed without
  deleting it, so the service deletes that branch once and tries again; a second failure is logged and the run goes on.
- **Renamed ids are only reported:** a rename in BSData is mostly cosmetic (a plural, `[Legends]`); removing would hit
  valid ids.
- **No widening of the named exception** in Armamentarium's `CLAUDE.md`: the service sets no id here and merges nothing;
  removing goes through the hand check. Moved ids are not set again (that would be a rule beyond "exactly one hit",
  Armamentarium ARMAM-598).

Rejected: **reporting in the journal only** (nobody reads it; the table would keep ids the app cannot resolve);
**merging the removal with `--auto-merge`** (would widen the named exception for an action the app does not need: an id
that does not resolve is already a gap there); **treating a rename as orphaned** (cosmetic renames would cost valid ids);
**checking only with the switch** (the ids stay after the switch is removed); **checking at the end of the run** (a run
stopped by the timeout would lose it).

## Addendum ARMAM-603 — a refused merge is tried again (2026-10-18)

- **Measured** in the first full run over HTTP/2 (ADR 0003 Addendum ARMAM-603, 2026-10-18 from 15:48): GitHub refused 2 of
  24 merges in the first ten minutes with 405 "Base branch was modified. Review and try the merge again.", two seconds
  after the pull request was opened. Both branches stood on the newest main (`1c45847`, `13e8b43`); GitHub was still working
  out the pull request after the merge before. The next merge changed main, the two pull requests then conflicted and
  stayed open, counting among the five of ADR 0003 §12. Since the shops answer again, merges come every ten to twenty
  seconds, and five such refusals would end a run.
- **A merge answered 405 is tried again** after five seconds, three tries in all, before the run goes on to the next
  product. Any 405, not only that message: GitHub also answers "not mergeable" while it still works a pull request out, and
  a real conflict costs ten seconds and ends as before (`not merged`, a notice, the pull request stays open). Other
  answers are not tried again. Each try again is one journal line (`pull request N: merge answered 405, try 2 of 3 in
  5 s`), so the next run shows how often it happens.
- Rejected: **waiting before every merge** (costs every merge for two in 24); **asking GitHub for `mergeable` first** (one
  more request per box and the same wait when it is not yet known); **matching the message text** (GitHub's wording is not
  a contract).

## Addendum ARMAM-598 — which root entry a name names, gaps get ids, names repaired (2026-10-19)

- **Measured** with the new switch `--id-report` (reads the table and the catalogues without a token, asks no shop, writes
  nothing; per reading the gaps with their reason, the gaps a run would fill, the names it would repair) on the table after
  the full run of 2026-10-18 (984 boxes, 1857 entries, 460 with ids: `40k` 325 of 778, `kt` 87 of 122, `hh` 16 of 177,
  `aos` 27 of 631, `tow` 5 of 149). Of the gaps, 76 in `40k`, 402 in `aos` and 64 in `tow` named several root entries.
  The kinds, counted with the catalogues: the same entry linked at the root of several armies (Brood Brothers, Chaos
  legions, Aeldari and Drukhari, The Old World's armies of infamy), the unit defined in one army's library or shared
  entries and every root a link (no root defined it: the rule first considered here, "the catalogue that defines it at its
  root wins", found none); an `aos` name in Age of Sigmar 4th and in the own Spearhead repository (236); different entries.
  Names without a hit mostly leave off a unit word ("Intercessors" for "Intercessor Squad") or put the army before the name
  ("Ork Boyz").
- **Rules of a hit** (`CatalogueIndex.Find`), each from the first step that names any root entry: the name (§2); the name
  with a unit word after it (`squad`, `mob`, `unit`, `pack`, `band`, `regiment`, `team`); the name without one or two words
  before it that the catalogue's name carries ("Ork Boyz" for Boyz of the Orks); from two words, the root names that go on
  after it ("Traitor Guardsmen" for "Traitor Guardsmen Squad"). Of the root entries found: the one; else the one from the
  preferred source, the own Spearhead repository (`CatalogueSource.Preferred`); else, all being the same entry, its one
  home: the catalogue that defines it at any depth or whose library ("<name> - Library", "<name> Library") does. Different
  entries stay a gap. The same hit picks build sentences and matches sources (ADR 0003 Addendum ARMAM-599); an id a rule
  set counts as valid in the id check. Measured gain: 488 gaps (`40k` 90, `aos` 314, `tow` 72, `hh` 12).
- **Gaps get ids:** at the start of every run with catalogues, before the id check, the gaps (fixed entries without ids) of
  boxes the service wrote get the ids their names now name, in one pull request `scraper/backfill-ids`; merged like a box
  with `--auto-merge`, a draft without it, counted among the five open. A box whose sources are not all allowlisted shops
  was written by hand; its gaps are decisions ("name without id", Armamentarium ADR 0118 Addendum ARMAM-588) and stay. This
  also gives a gap the merged `scraper/stale-ids` left the ids its name names now: a moved id is set again.
- **Names repaired:** the build sentence reader ends a name at "accompanied" and "usable" and drops a leading
  "accompanying"; "boards" is excluded like "board". Names written before stay wrong, so every run applies these fixes to
  the gaps of service boxes in one draft `scraper/fix-names`: a clause cut, an excluded or description-only name removed, a
  reading left without entries removed, a box left without readings removed whole (the schema asks for one; the next run
  proposes it again). Never merged by the service, also with `--auto-merge`; counted among the five open. Measured: 4 renamed
  ("Vespid Stingwings accompanied by", "accompanying Neuroloids", two "Endless Spells usable by …"), 15 removed (shoulder
  pads, heads, a gaming board, "Push-fit").
- **Named exception widened** in Armamentarium's `CLAUDE.md` (Armamentarium ADR 0118 Addendum ARMAM-598).
- Decisions of the maintainer. Rejected: ids by the most similar name (§ Rejected stays); a one-off backfill; the backfill
  in every box pull request; the backfill for boxes written by hand; repairs merged by the service; Age of Sigmar 4th
  before Spearhead; no rule for the same entry in several armies; "root names that go on after it" for one-word names
  ("Nob", "Archaon"). Further reader noise the report shows (parts of upgrade sets, sentence rests such as "of the following
  miniatures") goes to a follow-up ticket.

## Addendum ARMAM-642 — aliases for own names (2026-10-20)

- **Measured** on the scan test of 2026-10-20: the box "Captain Titus & The Wardens of Ultramar" (99120101459) lists seven
  models; six of them (Plenipotentiary Lucia Vestha, Astropath Dainal Kornelius, Legatus Aemelia Minervas, Commodore Gaius
  Silva, Veteran Sergeant Metaurus, Ancient Gadriel) have no root entry of their own, the catalogues hold them as one unit
  "Wardens of Ultramar". Jurgen of the box "Ciaphas Cain" (99120105152) belongs to the entry of Ciaphas Cain. No rule of
  a hit can know that.
- **`aliases.json`** beside the table, with `aliases.schema.json`: per row a reading, a model's own name, the name of the
  root entry it belongs to and the name of that entry's catalogue. No id is written there: the service reads the file from
  `main` in every run, as it reads the table, and resolves each target by the rules of a hit among the root entries of the
  named catalogue (`CatalogueIndex.UseAliases`). A change to the file takes effect with its merge, without a deploy.
- **A hit by alias:** a line whose normalised name equals an alias of its reading gets the alias's target, before the rules
  of Addendum ARMAM-598; an alias whose target is not one root entry of a reading read whole leaves the name a gap and is a
  notice of the run (mail and `--id-report`). A file that breaks `aliases.schema.json` or names one model twice in a
  reading fails the run (mailed like any failure); only a target that names no one entry is a gap. The id check counts an
  alias's pair as valid.
- **One line per model:** an alias keeps its name and count 1 and gets the ids even where the reading already lists its
  entry, in a new box and in `scraper/backfill-ids`; the matching of sources keys it by its entry and its own name, so Cain
  and Jurgen stay two lines. The app labels each model with its own name (Armamentarium ARMAM-643).
- **A long pull request text is cut** (measured 2026-10-21): the first run after the aliases filled 491 gaps, and GitHub
  refused `scraper/backfill-ids` with 422 "body is too long" (at most 65,536 characters). Every proposal's text is cut
  after the last line that leaves room for its closing line within 60,000 characters, and that line says how many lines
  were left out (`GitHubRepository.BodyOf`); the commit holds every change. Rejected: only counts in the text (no line to
  check by eye); one pull request per block of gaps (more merges, more code).
- **Named exception widened** in Armamentarium's `CLAUDE.md` (Armamentarium ADR 0118 Addendum ARMAM-642).
- Decisions of the maintainer. Rejected: the alias table compiled into the code like the other rules (a deploy per alias);
  the target as ids (ids by hand, against Addendum ARMAM-593); the target by name alone (several armies link one unit);
  an alias matched through the rules of a hit (more hits, more false ones); a broken target failing the run; the lines of
  an entry's models merged into one with their sum (one entry per model, ARMAM-643).

## Addendum ARMAM-688 — members of a built unit (2026-10-27)

- **Measured** on "Krieg Heavy Weapons Squad" (99120105130): its "Fire Coordinator" is a model of the unit, no root entry,
  so no rule of a hit gave it ids (ADR 0003 Addendum ARMAM-688).
- **A member** is a model a build sentence names after its unit ("consisting of three Heavy Weapons Gunners and one Fire
  Coordinator") and the catalogue lists as a model of the unit the build hits, by its name or without its gear. It keeps
  its own name and count and gets the unit's ids, one line per model beside the others, as an alias does; the matching of
  sources keys it by the unit and its own name. Every catalogue and library records the model names under each unit it
  defines (`CatalogueIndex.IsMember`); the id check counts a member's pair as valid.
- **No row in `aliases.json`:** the sentence and the catalogue name the member, nobody writes it down.
- **Named exception widened** in Armamentarium's `CLAUDE.md` (Armamentarium ADR 0118 Addendum ARMAM-688).
- Decisions of the maintainer. Rejected: the members as aliases (a row per box by hand); the unit as one line with the sum.
- **"Primaris" dropped** (2026-11-01): the 10th-edition catalogues name "Chaplain", "Chaplain on Bike", "Lieutenant",
  "Intercessor Squad" without the word the shops still write. A name starting with "Primaris" whose own name hits nothing
  hits by the rest of the name, as it stands or with a unit word after it; never by a longer root name, so "Primaris
  Chaplain" stays on the Chaplain on foot and "Primaris Chaplain on Bike" goes to the bike. Measured with `--id-report` on
  the table of 2026-11-01: six names get ids (Chaplain, Hellblasters, Inceptors, Intercessors, Lieutenant, Techmarine),
  no other name changes. "Primaris Chaplain on Bike" (99120101273) had been read as "Primaris Chaplain" without ids, the
  bike name dropped because it hit nothing; the box is removed before this reader runs, so no backfill gives it the foot
  model's ids, and the next run reads it anew.
- Decision of the maintainer: only "Primaris", not any first word (a broader rule would build false hits and need a
  measurement over the whole table).

## Addendum ARMAM-707 — a gap beside the line of its own entry (2026-11-01)

- **Measured** with `--id-report` on the table of 2026-11-01: five service boxes held one model twice, read before a rule
  of a hit joined its two names — "Primaris Techmarine" beside "Techmarine" (99120101278), "Aeldari Windriders" beside
  "Windriders", "Aeldari Wraithguard" beside "Wraithguard", "Tyranid Hive Tyrant" beside "Hive Tyrant", "towering
  Wraithknight" beside "Aeldari Wraithknight". Two sources each named the model their own way; nothing hit the longer
  name, so the matching of sources kept two keys and two lines. Today both names hit one root entry and give one key; a new
  box is read as one line. The backfill gives no root entry twice to one reading and so leaves the gap a gap.
- **The repair** (`scraper/fix-names`, never merged by the service) removes a gap of a service box whose name, cut as the
  reader cuts it, names a root entry another line of the reading carries with the same count, and that line's own name
  names the same entry. An alias line or a member line carries its target's ids under a name of its own (Addenda
  ARMAM-642, ARMAM-688) and never counts; an alias gap stays; a gap with `countCandidates`, or with another count, role or
  reading than the line, stays: which is right is not known.
- **No widening of the named exception:** the repair sets no id and runs only in the draft the maintainer merges.
- Decisions of the maintainer. Rejected: removing the gap in `scraper/backfill-ids` (merged without the hand check); the
  line with ids always winning, whatever the count.

## Addendum ARMAM-708 — The Old World from BSData (2026-11-01)

- **Measured** on 2026-11-01: `vflam/Warhammer-The-Old-World` answers 404 on GitHub. Since then every run left `tow`
  unread (`unchecked: tow`): no name of the reading hit, so no build by article counted (ADR 0003 Addendum ARMAM-599),
  and "General of the Empire on Imperial Griffon" (99122702001), removed for the reader of ARMAM-688, waited with "no
  source states a model count", although its shops write "builds a General of the Empire on Imperial Griffon".
- **The source moved:** the same author publishes the catalogues as `BSData/warhammer-the-old-world` (created
  2026-10-23, BattleScribe JSON, release `1.4.3` of 2026-10-30), with the same game system id and the same catalogue and
  entry ids. The app switched in Armamentarium ARMAM-624; §3 follows it ("a change there is a change here").
- **No rule changes:** in that catalogue the mount is a shared entry, an option of "General of the Empire", not a root
  entry. The longer name hits nothing and the name before "on" stays (Addendum ARMAM-688), so the box reads as "General of
  the Empire" × 1, the pair it carried before. A single kit without a hit still states no count.
- **What the next runs do:** the id check holds the `tow` pairs again, hand boxes' as well as service boxes' (Addendum
  ARMAM-597); an orphaned pair goes into the never-merged draft `scraper/stale-ids`. Gaps of service boxes in `tow` get
  their ids in `scraper/backfill-ids` (Addendum ARMAM-598).
- Decisions of the maintainer. Rejected: a rule that a single kit counts one without a hit (prose would count as models);
  a line of its own for the mount (the catalogue builds it as an option).

## Addendum ARMAM-738 — the own repositories under Strecker-IT (2026-11-04)

- **Measured** on 2026-11-04 at 20:41: a run by hand stopped before it wrote anything. The Spearhead reading answered
  404 (`aos: LordTyberias/armamentarium-spearhead-data unreadable`), then the list of open pull requests
  (`repos/LordTyberias/armamentarium-box-data/pulls` answered 404).
- **The repositories moved:** on 2026-11-04 `armamentarium-box-data`, `armamentarium-spearhead-data` and
  `armamentarium-combat-patrol-data` went to the organisation `Strecker-IT`, the old addresses answer 404 with no
  forwarding. The app switched in Armamentarium ARMAM-737; §3 follows it ("a change there is a change here"). The
  service's token names `Strecker-IT` as resource owner.
- **No rule changes:** same repositories, same branch `main`, same ids. The table row of §3 stays as it was written.
