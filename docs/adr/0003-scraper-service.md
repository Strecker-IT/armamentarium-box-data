# ADR 0003 — Scraper service: draft pull requests from retailer listings

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-591). Founded on Armamentarium ADR 0118 §4 and its Addendum ARMAM-591.

**Overridden in part by ADR 0004** (ARMAM-593): §2 "once a day" (now the 1st and 15th), the token's "at most 90 days"
(now without expiry); while `--auto-merge` runs also §10 "draft … no ids; the maintainer adds ids … before merging" and
§12 "until five scraper pull requests are open"; and Rejected "Ids by exact name match against the catalogues".

**Narrowed by Addendum ARMAM-601:** §10 box name only from a listing that is a source of the box.

**Changed by Addendum ARMAM-596:** `www.fantasywelt.de` leaves §5; a 403 counts as "does not answer" (§12), no longer
as no finding (Context, last point).

**Changed by Addendum ARMAM-595:** §6 "at most one request per five seconds and host" is stretched up to ten seconds for a
host that throttles, and a 429's `Retry-After` is honoured; §12 "the product comes back in three days" holds, and it is
asked once more at the end of the same run first.

**Changed by Addendum ARMAM-599:** §8 reads a single kit's count from its build sentence as well as lines; §9 matches
two names of one root entry and drops a source with two counts for one entry, and a source sharing no entry with another
no longer holds the box; Context's "single kits are described in prose" no longer makes them wait.

**Changed by Addendum ARMAM-602:** §5 gains `shop.afk.games`, `obsidiangames.ca`, `www.goblingaming.co.uk` and
`mrmuffinstrains.com` (Shopify) and loses `taschengelddieb.de` and `miniset.net` with the JTL and miniset adapters; §6
"per minute for `miniset.net`" and its slow-source rule have no host left. A text and data mining reservation no longer
keeps a shop out (Context, `games-island.eu` stays out for its captcha).

**Changed by ADR 0004 Addendum ARMAM-597:** besides one pull request per box (§10), a run may open one draft
`scraper/stale-ids` that removes orphaned ids; it is never merged by the service and counts among the five of §12. Addendum
ARMAM-599's "no catalogues are loaded" without `--auto-merge` no longer holds (still no hits).

**Changed by Addendum ARMAM-600:** §13 "Failures go to the journal only" and Rejected "Mail or an issue on failure" no
longer hold for mail: a failed run and the notices of a run are mailed to the maintainer; an issue stays rejected.

**Changed by Addendum ARMAM-603:** §6 shops are asked over HTTP/2 (HTTP/1.1 only for a host without it). §7 and Rejected
"A browser user agent" keep their decision; their "measured worse" is now only a guess, since the trigger measured is
HTTP/1.1, not the user agent.

**Changed by Addendum ARMAM-610:** §4 a product named as an upgrade set is no candidate; §8 drops parts, hand weapons,
filler names and names starting with "of", and cuts prose before and after a name, in lines and build sentences alike.

**Changed by Addendum ARMAM-655:** Shopify's product address (`/products/<handle>.js`) is asked through curl, no longer
through the .NET client; every other request stays with it (§6, Addendum ARMAM-603). Addendum ARMAM-603's "the trigger is
HTTP/1.1 on `.js`, not the rate", "no limit per address across shops was found" and "every request goes over HTTP/2" (as
.NET's) no longer hold for that address. §5's "never circumvented", §7's own name and Addendum ARMAM-596's rejected
"working around the guard (browser fingerprint, challenge answers)" stay, bounded as the addendum says.

**Changed by Addendum ARMAM-642:** §8 also reads a line "1 <name> miniature" without bullet or "x", and a build sentence
that names models without a count ("builds Ciaphas Cain and Jurgen"), one each, when every name names a root entry.

**Changed by Addendum ARMAM-688:** §8 reads a build by article only before a name with a capital; a build of a unit by
article ("builds a … Squad") states no model count, the unit's members do ("consisting of three … and one …"), and a
name carried on over "on" counts when it names a root entry. Addendum ARMAM-599 "a number word or an article right before
the name" holds for a model, no longer for a unit.

## Context

The table grows only by hand so far. Measured on 2026-10-17:

- No source names catalogue ids. The product catalogue `WarHub/warhub-catalog` links `productCode` and `ean`, names game
  system, category and status, but carries no contents (`quantity` is always 1) and no release date per product.
- Retailer shops list a box's models as lines `N x Name` in their product description. Shopify shops find a product by EAN
  (`/search/suggest.json`) and confirm it in `/products/<handle>.js` (`variants[].barcode`); their bulk lists carry no
  barcode. JTL shops find it by EAN (`/?qs=`) and confirm it in microdata (`itemprop="gtin13"`). Element Games finds it
  by article number only and names it on the page.
- German shops translate model names (`Phantomwächter` for `Wraithblades`); the order of the lines follows the publisher's
  text.
- `warhammer.com` (AWS WAF captcha), `pk-pro.de` (Cloudflare challenge) and `games-island.eu` (captcha; its catalogue API
  carries no EAN, no article number and no model count, and the shop declares a text and data mining reservation) block or
  give nothing. `warhammer-community.com` has no product search and states counts only in prose.
- Added on the maintainer's list (2026-10-17): `miniset.net` keeps one page per article number (`/sets/gw-<code>`, the
  canonical address confirms it) with the box's list, and restricts a client after about a dozen requests at five
  seconds ("temporarily restricted due to suspicious automated activity"). `www.retrofixgames.com` is a Shopify shop
  whose terms forbid scraping ("spider, crawl, or scrape"). `wh40k.lexicanum.com` and `whfb.lexicanum.com` sit behind a
  Cloudflare challenge; `www.fauxhammer.com` is a blog without box lists.
- Single kits are described in prose by every shop measured ("six Wolf Scouts"); lists `N x Name` appear for boxes of
  several kits. `www.fantasywelt.de` answers the .NET client with 403 behind Cloudflare while curl gets 200; it stays
  listed, a 403 is no finding.

## Decision

1. **Project** `src/Armamentarium.BoxData.Scraper` (console, .NET 10), tests in `tests/`, solution
   `armamentarium-box-data.slnx`. No package reference in the service; MSTest only in the test project.
2. **Runs** once a day on the project's server: systemd timer, `oneshot` service, `DynamicUser`, state in its
   `StateDirectory`, the token through `LoadCredential`. Unit, timer and deploy script live in `deploy/`.
3. **GitHub** through its REST API with `HttpClient` (git data API, pulls). No clone, no git on the server.
4. **Candidates:** every Games Workshop product of category `miniatures` in the systems of ADR 0002 that no box holds yet,
   read once per run from the compiled address `https://warhub.github.io/warhub-catalog/products.json`. That request is not a
   shop: it sits outside the allowlist and the throttle, and no hash or manifest is checked, because a wrong catalogue entry
   only costs a search; nothing from it enters the table but article numbers, which every shop must confirm.
   Order: Kill Team first, then `current` before discontinued, then article number.
5. **Sources** are a compiled allowlist with four adapters:
   - Shopify: `www.wellplayed.ch`, `www.irresistibleforce.com.au`, `tistaminis.com`, `abyssgamestore.ca`, `radaddel.de`,
     `www.retrofixgames.com` (its terms forbid scraping; the maintainer carries that risk, ADR 0118 Addendum ARMAM-591).
   - JTL shop: `www.fantasywelt.de`, `taschengelddieb.de`.
   - Element Games: `elementgames.co.uk`.
   - miniset.net: `miniset.net`, by article number.

   A listing counts only when the shop confirms the EAN (Element Games and miniset.net: the article number). Used goods are skipped. A
   captcha or challenge answer is no finding; it is never circumvented.
6. **Throttle:** at most one request per five seconds and host, per minute for `miniset.net`; hosts run in parallel. A
   page that says the host restricts this client, whatever its status, rests the host for the rest of the run. A slow
   source (miniset.net) is asked only when a fast one found the product and the fast findings alone would not make a
   proposal (one listing, or an entry with a single source), since alone it never makes two sources; so the five-second
   hosts set the pace and the first pass over the backlog takes about five hours.
7. **User agent:** the service's own name, `ABD/1.0`, without a contact address (maintainer decision; it
   overrides "with contact address" of ADR 0118 §4, see its Addendum ARMAM-591). A desktop browser string was tried and
   measured worse: three Shopify shops answered `/products/<handle>.js` with 429, which they answer with 200 to the own name
   (2026-10-17).
8. **Only facts:** a number and a model name per line. Terrain, datacards, dice, markers, tokens, mats, rule books,
   dossiers, rulers, transfers, bases, part counts, paints, brushes, clippers, tools, construction guides and handbooks are
   dropped, and so is every line under a heading that names one of them: a counted heading ("15 plastic terrain pieces"),
   a line ending in a colon ("these interchangeable parts:"), or a counted line without a bullet that ends in one ("5x
   Citadel Colour paints, including:"). A counted line without a bullet whose bulleted lines below add up to its count
   ("11x Space Marines" over "1x Captain", "5x Terminators", "5x Infernus Marines") is their total and dropped; a model
   whose equipment is listed below it with the same count would be lost with it, which the maintainer's check catches. The
   name ends at a spaced dash or at a colon followed by four words or more, where the shop's description begins; a
   shorter colon belongs to a title ("Warhammer 40,000: Battle Report Book"). An excluded word or an "and 2" in that
   description drops or splits nothing, nor does an "and 2" inside a parenthesis; an excluded word in a parenthesis or
   after a comma still drops the line. "10 Termagants and 1 Ripper Swarm" (or "and 1x") is two lines, a non-player
   marker counting for both (measured on the first server run, Armamentarium ARMAM-593). An
   alternative build named on a line is dropped and the line counted as its base build, noted in the pull request.
9. **Agreement:** names are compared normalised (lower case, without `operative`, `miniature`, `non-player`, plural `s`,
   punctuation). The same count in two sources gives `count`; different counts give `countCandidates`. A German source
   confirms lines only by position, when its number of lines and its sequence of counts equal an English source's. Every
   entry needs two confirming sources; one entry short and the whole box waits. So does a box whose listing names one
   model twice ("2x Sergeant (bolt)", "1x Sergeant (plasma)"): no count would be one a source states.
10. **Output:** one **draft** pull request per box, branch `scraper/<lowest article number of the box>` (language variants:
    the lowest added one). The entries carry name and count, no ids; the maintainer adds ids, readings and catalogue names
    and checks the preset roles in the branch before merging. Where the line says non-player, `role` is `npo` in a `kt` reading and `extra` in any other
    (README, Entry); else `team`. The box name comes from an English shop title, else from the catalogue name with a note;
    a German title never. A Combat Patrol gets `generation` from the catalogue status (`current`, else `legacy`), marked for
    checking. The body names every source of the box and every note. Shop text enters the table, the commit and the pull
    request only as one cleaned line (no control or format characters, at most 120 characters), and the body shows it only
    in code spans, so it cannot render links, images or mentions. A failed proposal (also a timeout or a stop) is logged, its
    branch deleted again, the product comes back in three days, and the run goes on.
11. **Changed box** means a new language variant: a variant of the same shop product whose barcode belongs to a box already
    in the table adds its article number. Counts of existing boxes are never touched.
12. **Pace:** the run works through the backlog until five scraper pull requests are open; a box with an open pull request
    is skipped, so hand edits in its branch are never overwritten. A product without two confirming sources is checked
    again after 30 days. A shop that does not answer (network, timeout, 429, server error) is not a "not listed": while
    one failed and fewer than two confirm, the product comes back in three days. After 20 such failures in a row a shop
    rests for the rest of the run. A shop that answers with something unreadable (a changed page) has answered: logged,
    no finding, the product rests as usual, so a broken parser cannot bring the whole backlog back every day.
13. **Failures** go to the journal only.

## Rejected

- **Ids by exact name match against the catalogues:** shop names rarely equal catalogue names; catalogues on the server for
  few hits.
- **Report only, no table change:** every row by hand again.
- **One collecting pull request per run, or one running pull request:** blocks partial merges and collides with hand edits.
- **Proposing an entry from a single source:** each number from two sources is the mitigation ADR 0118 §2 relies on.
- **Octokit, git CLI:** a dependency, or git plus a token in a remote URL on the server.
- **Bulk shop lists:** carry no barcode.
- **Prose sources, slug matching on warhammer-community.com:** assigning an article to a box by name is guessing.
- **Circumventing captchas and challenges:** never. Lexicanum stays out. Its wikiteam dumps on archive.org (2026-06-04)
  were measured as the legitimate way and do not help: box pages carry neither article number nor EAN, hardly any box has
  a page, and the text is CC BY-NC.
- **`www.fauxhammer.com`:** reviews and news, no box lists, no article numbers.
- **A browser user agent:** drew 429 from Shopify shops (§7).
- **Mail or an issue on failure:** a new channel or a wider token for a daily job whose missing pull requests show anyway.

## Addendum ARMAM-601 — the box name comes from a source (2026-10-18)

- §10 "the box name comes from an English shop title" is narrowed: **only the title of a listing that is a source of the
  box** (a confirming listing of the agreement; for a Combat Patrol, a confirming shop). A shop that matched the barcode
  but listed no contents confirms nothing, and its title named box 60010213001 "Horus Heresy: False Gods (PB)" while its
  sources, the catalogue and the contents are the Ogor Mawtribes Army Set. Without such a title the catalogue name stands,
  with a note, as before. The box is renamed "Age of Sigmar: Ogor Mawtribes Army Set".
- Readings of a Kill Team product: ADR 0002 Addendum ARMAM-601.
- Rejected: **always the catalogue name** (warhub lists a language variant first, often in French and in capitals; the
  README asks for the English product name).

## Addendum ARMAM-596 — a refusing shop has not answered (2026-10-18)

- **Measured:** `www.fantasywelt.de` answered every request of the first auto-merge run with 403 (2785 of 2785; before
  that 183 of 183 and 366 of 366). Cloudflare refuses the .NET client; curl gets 200. As "no finding" the 403 reset the
  host's failure count, so the shop was asked for every product of the run and gave nothing.
- **`www.fantasywelt.de` leaves the allowlist** (§5). The JTL adapter stays for `taschengelddieb.de`.
- **A 403 is a shop that does not answer** (§12), like 429 and a server error: the shop's guard refuses this client, which
  says nothing about the product. It counts towards the 20 failures in a row after which the host rests for the run, and
  a product with fewer than two confirming sources comes back in three days instead of 30. §5 "a captcha or challenge
  answer is no finding" holds for what it says, a challenge confirms nothing; it is never circumvented. Other answers that
  are not 200 (404) stay no finding.
- Rejected: **only removing the shop** (the next shop that refuses this client costs the whole run again); **only resting
  it** (fantasywelt would cost 20 requests per run without a prospect); **a separate 403 counter** that keeps "no
  finding" (more code for the same rest, and the product would wait 30 days for an answer it never got); **working around
  the guard** (browser fingerprint, challenge answers): never.

## Addendum ARMAM-595 — a throttling shop is asked again in the same run (2026-10-18)

- **Measured** on the first auto-merge run (2026-10-17 20:20 to 2026-10-18 03:48, 3372 products): 3598 answers 429, all
  of them to `/products/<handle>.js` of six Shopify shops (abyssgamestore.ca 1130, radaddel.de 910,
  www.irresistibleforce.com.au 771, www.retrofixgames.com 702, www.wellplayed.ch 71, tistaminis.com 14), never to
  `search/suggest.json`; about 200 to 280 an hour per shop, steadily, no burst. Never 20 in a row, so no shop rested: a 200
  or 404 in between reset the count. Whether the shops send `Retry-After` was not measured, the journal logged no headers.
  Up to 886 products came back in three days because a shop that throttled may have held the second source.
- **`Retry-After` of a 429 is honoured** (seconds or HTTP date): until then a request to the host fails at once, without
  a request and without counting towards the 20 in a row. A wait longer than ten minutes rests the host for the run. No,
  zero, negative or unreadable value is no header.
- **Stretched interval** (§6): from a host's third 429 in a run on, its interval is doubled up to ten seconds and stays so
  for the run; a longer interval (miniset.net) is never shortened, other hosts are untouched. Every product waits for its
  slowest fast host, so ten seconds is the cap: a Shopify product (two requests) takes 20 s and a pass over the whole
  backlog about 19 hours, inside the unit's 20. The catch-up below can add up to about five hours (886 products at 20 s), so
  at worst the unit's timeout stops a run in its catch-up; the marks are saved per product, the next run goes on from them.
- **One catch-up pass** (§12): a product that a shop did not answer and that fewer than two sources confirm keeps its
  three-day mark and is asked once more, whole, after the pass over the backlog, whatever kept the shop from answering (a
  429, its `Retry-After`, a timeout, a network failure, a server error); not when the run stopped at five open
  pull requests. A failure that the same run cannot get past does not bring it back: a 403 (the guard, Addendum ARMAM-596),
  a host resting for the run, a block page. The run ends with `catch-up: N asked again, M still without an answer`.
- **Measure** at the end of every run, also a stopped one: one line per host that answered 429, with the number of 429,
  how many carried `Retry-After`, and the interval reached. The baseline is the first run above.
- `Retry-After` only for 429; a 5xx stays as it was (§12).
- Rejected: **a fixed longer interval for Shopify** (slows every run, throttled or not, and asks nothing again);
  **stretching without `Retry-After`** (wastes the wait a shop names); **waiting until `Retry-After`** (every product waits
  for every shop, the whole run would stand); **halving the interval again after successes** (more state, no gain measured
  on a steady 429 pattern); **asking only the failed shops again** (keeps listings across the run for little; the good
  shops answer again at their own pace); **several catch-up passes** (run length beyond estimate); **a 403 throttles like a
  429** (more requests to a guard that refuses this client); **`Retry-After` for 503 too** (no 5xx pattern measured);
  **an own time budget for the catch-up** (the state is saved per product, the unit's timeout already bounds the run).

## Addendum ARMAM-599 — a single kit's count from its build sentence (2026-10-18)

- **Measured** (2026-10-18, 13 single kits across the nine sources, 67 kit-and-source pairs): no source lists a single kit
  line by line; 34 pairs state the count as a number before the name behind a build verb ("builds a squad of 10 …",
  "assemble a set of 10 …", "kannst du 10 … bauen"), 9 as a number word ("builds six …"), 5 by an article for one model
  ("builds a …", "einen … bauen"), 8 only a part count, 8 nothing. Where several sources state a count, they agree. The
  most frequent number in the prose is the part count ("150 plastic components"), then bases, heads and transfers. The
  first auto-merge run left 1883 products waiting with "no source lists models line by line" (Armamentarium ARMAM-593).
- **Build sentences** (§8 widened): beside its lines, a listing reads the counts its prose ties to a build verb — English
  `build(s)`, `assemble(s)`, `make(s)`, `construct(s)`, with "a squad/set/unit/team/kit/group of N"; German "… bauen" and "Bau
  von …", with "einen Trupp/eine Einheit aus N". The count is a digit (1 to 999), an English or German number word up to
  twenty, or an article right before the name, which is one. The name ends at punctuation, at the next clause ("with",
  "which", "und", "mit" …) or a further number; a name followed by a number was an adjective and is no model. An excluded
  word (§8, now also heads, shoulder pads and every "…teile") drops it, so a part count is never a model count. "As well
  as three …", "and two …", "sowie einen …" after the build is an entry of its own. A sentence that begins an alternative
  build ("Alternatively, you can build …") is not read, and an "or" before a number ends the build's additions ("or two
  units of 10"); an "or" between two weapons does not. The same sentence twice is one finding. A line that names only the
  kind of kit ("1x multi-option, plastic Citadel Miniature") is no model either.
- **One build per source** (measured on the real texts: shops copy the publisher's prose, and that prose also "builds a
  unique boarding party", "a unit of exceptional soldiers", "10 customizable …"): a count by article counts only when its
  name names a root entry; the first remaining build sentence is the source's, a later one counts only when it names
  another root entry, and the additions belong to the build they follow. An addition by article follows the same rule
  ("… armed with a lasgun and a laspistol" adds no model); an addition by digit or number word counts without a hit. A
  German build sentence by article never counts, its German name has no hit. German "elf" (eleven) is no number word: it
  is the "Elf" of "High Elf Spearmen". A count of 0 is none.
- **A build sentence wins over the lines of its source** when its name names a root entry of the product's readings, the
  hit that sets ids (ADR 0004); without such a hit the source's lines stand, and a source without lines gives its build
  sentences. Imperial Navy Breachers (99120108152), which shops split by equipment into lines no 40k entry has, becomes
  "Imperial Navy Breachers × 10" again from "builds a squad of 10 Imperial Navy Breachers".
- **A source that shares no entry with another** (Element Games splitting the Breachers by equipment, a shop's "10 Kasrkin
  elite infantry") does not count for the box instead of making it wait; a German source shares through its confirmation
  by position, but only where no two English sources share an entry; else a German source would join an unrelated
  description to the agreeing ones. A source that shares one entry counts whole, so an addition only it names still
  makes the box wait. Sources that state counts but share no entry make the box wait with "no two sources share an
  entry".
- **Agreement** (§9 widened): two names that hit the same root entry confirm each other ("Primaris Intercessors",
  "Intercessors"); without a hit the normalised name decides as before. A German source confirms by position against its
  lines or its build sentences, whichever holds the English source's sequence of counts. Two counts for one entry in one
  source's build sentences ("a set of 10 …", "a squad of 9 …") drop that source for that entry, the others still count;
  two lines of one name still make the box wait. An entry from a build sentence needs two sources like a line; an
  additional model with one source makes the box wait.
- **No hit** (none or several): the entry goes in as name × count, visible, not addable (ADR 0004 §4).
- **Without `--auto-merge`** there are no hits (ids are set only with the switch; the catalogues are loaded for the id check
  of ADR 0004 Addendum ARMAM-597 alone): names compare normalised and build sentences only stand in for a source without
  lines.
- **Named exception widened:** the hit now also chooses a build sentence over lines and matches sources; Armamentarium's
  `CLAUDE.md` names it, Armamentarium ADR 0118 Addendum ARMAM-599 records it.
- **Roll-out:** `state.json` on the server is emptied once (backup beside it), so the run on 2026-11-01 asks every
  candidate again rather than in 30 days.
- The waiting reason without any count is now "no source states a model count".
- Rejected: **a single kit without a count** (schema and app would carry a box that states no fact beyond the kit); **not
  recording single kits** (the app's fallback by name stays a guess); **any number in the prose** (the part count is the
  most frequent one); **digits only** (loses six Wolf Scouts and every one-model kit); **lines always win** (Breachers would
  come back split, without ids); **waiting where sentence and lines disagree** (the split by equipment would have to be
  summed); **a German sentence never confirms** (the German shops would not count for single kits); **the first or the
  largest of two counts** (the order of a text or a guess decides); **ignoring additional models** (real models would be
  missing); **a box waits when its name has no hit** (the table grows only by hits, the gap stays visible anyway); **every
  build sentence counts** (the copied prose confirms its own "10 customizable …" as a second entry); **only sentences with a
  hit count** (additions such as Scarab Swarms rarely hit and would be lost); **every single mention holds the box**
  (one shop's own wording keeps a kit out that all others agree on); **dropping every entry with one source** (an addition
  could be missing unnoticed).
- **Found, not decided here:** Kasrkin and the Leman Russ Battle Tank name two root entries in `40k`: the same entry id,
  defined in Imperium - Astra Militarum (`b0ae-12a5-c84-ea45`) and linked at the root of Xenos - Genestealer Cults
  (`3bdf-a114-5035-c6ac`), in the 10th and the 11th edition alike. So they get no hit, no ids and no count by article;
  whether the defining catalogue wins over a linking one, Armamentarium ARMAM-598 decides.

## Addendum ARMAM-602 — four more shops, two gone (2026-10-18)

- **Probe:** `--dry-run --host <host> --code <a,b,…>` asks one allowlisted shop for each article number and prints the
  lines and build sentences it reads, nothing else; it needs `--dry-run` and `--code` (a run over part of the sources would
  weaken the two-source rule). The candidates were measured with it, so through the service's own client (`ABD/1.0`, its
  TLS): `www.fantasywelt.de` had answered curl and refused .NET (Addendum ARMAM-596).
- **Sample:** the same twelve products for every shop: single kits Intercessors `99120101309`, Necron Warriors
  `99120110052`, Vindictors `99120218061`, MKVI Tactical Squad `99123001003`, Empire State Troops `99122702008`; boxes
  Warhammer 40,000 Starter Set `60010199058`, Dark Angels Army Set `60010101006`, Spearhead: Stormcast Eternals
  `99120218081`, Spearhead: Skaven `99120206041`; Kill Team Hivestorm `60010199070`, Exodite `60010199084`, Kasrkin
  `99120105133`.
- **A shop qualifies** when it confirms at least five of the twelve and the service reads a model count (lines or a build
  sentence) for at least five, with no captcha, WAF or challenge; a non-commercial licence on the content keeps it out. A
  ban on scraping in the terms and a text and data mining reservation are carried by the maintainer (Armamentarium
  ARMAM-559 weighs them before release); a reservation no longer keeps a shop out. Maintainer's decision.
- **Candidates** (web search over Germany, Austria, Switzerland, the UK, the USA, Canada and Australia; confirmed / count
  read, of 12):

  | Host | System | Lang. | Confirmed | Count | Terms | Result |
  |---|---|---|---|---|---|---|
  | `shop.afk.games` | Shopify | en | 9 | 9 | no ban | **in** |
  | `obsidiangames.ca` | Shopify | en | 7 | 7 | Shopify's standard ban | **in**, ban carried |
  | `www.goblingaming.co.uk` | Shopify | en | 8 | 6 | no ban | **in** |
  | `mrmuffinstrains.com` | Shopify | en | 8 | 5 | Shopify's standard ban | **in**, ban carried |
  | `www.mightylancergames.co.uk` | Shopify | en | 8 | 4 | ban | out, count |
  | `topshelfco.ca` | Shopify | en | 4 | 3 | no ban | out |
  | `www.gapgames.com.au` | Shopify | en | 5 | 3 | ban | out, count |
  | `store.401games.ca` | Shopify | en | 6 | 0 | ban | out, count |
  | `mtechcave.com` | Shopify | en | 5 | 0 | ban | out, count |
  | `leisuregames.com` | Shopify | en | 3 | 3 | ban | out |
  | `contrariangaming.com` | Shopify | en | 2 | 1 | ban | out |
  | `cardsdice.ch` | Shopify | de | 2 | 1 | no ban | out |
  | `cardcosmos.de` | Shopify | de | 2 | 0 | no ban | out |
  | `www.battle-bear-sb.de` | Shopify | de | 3 | 0 | no ban | out |
  | `gamekastle.com` | Shopify | en | 0 | 0 | ban | out |
  | `gamesclub.at` | Shopify | de | 0 | 0 | no ban | out, no barcodes |
  | `www.nippon4u.com` | Shopify | de | 0 | 0 | no ban | out, barcode field holds other numbers |
  | `www.zwergenschmiede.ch` | Lightspeed | de | 7 | 2 | no ban | out, "Du erhältst 10 …" is no build verb |
  | `www.dungeon-wars.de` | PrestaShop | de | 5 | 3 | no ban | out, "… zusammenzubauen" is not read |
  | `fmb-tabletop.de` | Shopware 6 | de | 4 | 0 | no ban | out |
  | `www.spielwaren-kontor24.de` | Shopware 6 | de | 1 | 1 | no ban | out |
  | `www.wog.ch` | own | de/en | 0 | 0 | not found | out, search finds nothing |
  | `tabletopshop.ch` | WooCommerce | de | 0 | 0 | no ban | out, no EAN search |
  | `www.xzone.de` | own | de | 0 | 0 | no ban | out, no Games Workshop range |

  Challenge or refusal on the first request, not measured further: `duo-shop.de`, `geek-haven.de`,
  `miniaturemarket.com`, `spiele-offensive.de`, `www.firestormgames.co.uk`, `gameslore.com`, `chaoscards.co.uk`,
  `mightyape.com.au` (Cloudflare), the comparison sites `idealo.de`, `geizhals.at`, `billiger.de` and `cardmarket.com`
  (403 or a bot page); `pricerunner.com` lists prices only. None of the shops in the table answered 429, 403 or a block page across its twelve
  requests at five seconds, nor did the hand-measured ones. The non-Shopify shops were measured by hand with `ABD/1.0` and
  the service's parser run over the saved pages, since a probe needs an adapter. No shop showed a text and data mining
  reservation or a non-commercial licence; the terms were searched for the words, not read as a lawyer would. For
  comparison, two shops already listed: `www.wellplayed.ch` 3 / 3, `abyssgamestore.ca` 10 / 4 — the existing sources are
  not judged anew.
- **Gone:** `taschengelddieb.de` answers its search with a JavaScript challenge (200, 18.8 kB, "Please enable JavaScript
  to continue"), 0 of 12 in two probes on 2026-10-18, the second at 11:35, and in ARMAM-599's probe the same morning; the page is larger
  than the block-page limit (§12), so the service saw no finding rather than a refusal. `miniset.net` restricted the
  client from its first request, asked once a minute, in two probes on 2026-10-18, the second at 11:35, as in ARMAM-599. Both leave, and with them the JTL
  and miniset adapters, which no other shop uses. Neither challenge was circumvented.
- **Run time:** the shops are asked side by side and each waits five seconds per request, so a further fast shop adds
  no time to a product; the unit's 20 hours (Addendum ARMAM-595) stand. The slow-source rule of §6 stays in the runner
  without a host.
- **German shops** give prose or nothing; only English Shopify shops copy the publisher's "1x … 22x …" lists. Widening the
  build verbs for `www.zwergenschmiede.ch` ("Du erhältst") or `www.dungeon-wars.de` ("… zusammenzubauen") was weighed
  and declined (maintainer's decision): Addendum ARMAM-599's rules stand.
- Rejected: a JavaScript challenge marker for `taschengelddieb.de` (the shop leaves); keeping the JTL adapter for later
  (no shop uses it); measuring by curl (a shop may answer curl and refuse the service's client).

## Addendum ARMAM-600 — the service mails the maintainer (2026-10-18)

- **Why:** since ADR 0004 the service runs twice a month and merges by itself; a missing pull request no longer shows that a
  run failed, and nobody reads the journal between runs. §13 "Failures go to the journal only" and Rejected "Mail or an
  issue on failure" no longer hold for mail.
- **Way:** mail through the mailbox of the Armamentarium web services (`smtp.ionos.de:587`, STARTTLS, login
  `support@armamentarium.app`), sender `boxdata@armamentarium.app`, recipient `info@strecker-it.net`, all fixed in
  `Mailer`. The password is the same systemd credential the web services read (`/etc/armamentarium/mail-password`, via
  `LoadCredential`): no new secret, no sudoers line, the hardening of the unit unchanged. `System.Net.Mail` from the
  shared framework, no package. Accepted (maintainer's decision): the run, which reads shop pages, now holds the mailbox
  password beside the token; one mailbox, one rotation outweighs a second path that only the failure unit would hold.
- **A failed run:** `OnFailure=armamentarium-box-data-failure.service` (exit code other than 0, the 20 h timeout, a start
  that never ran). That unit runs the service's DLL with `--notify-failure`, which mails what systemd hands it
  (`MONITOR_UNIT`, `MONITOR_SERVICE_RESULT`, `MONITOR_EXIT_CODE`, `MONITOR_EXIT_STATUS`, `MONITOR_INVOCATION_ID`) and the
  `journalctl` command for that invocation, **nothing from the journal**. Started by hand it says so: the check of the
  mail way. systemd sets `MONITOR_*` on a start by hand too, with the scraper unit's last result (measured on the first
  check, which mailed "run failed (success)"); a result of `success` or none therefore counts as by hand, a failure never
  ends in either. It has no `OnFailure=` of its own; a mail that does not go out ends in its journal.
- **Notices of a run:** one mail at the end of the run, also when it stops or fails, and only when there is something:
  a refused proposal or merge (`not proposed`, `not merged`), the `id check` summary when it counts orphaned ids or names
  an unchecked reading, the draft `scraper/stale-ids` (proposed, not proposed, no slot), a reading whose catalogue source
  is unreadable.
  A mail that does not go out is logged (`notice mail not sent`) and changes nothing about the run. `--dry-run` mails
  nothing.
- **Rejected:** an issue in this repository (the token would need issue rights and fails exactly when it is revoked);
  `curl smtp://` in the failure unit (the password would reach curl through a shell, and the notices need code in the run
  anyway); MailKit as in the web services (the first package here); the tail of the journal in the mail (the failure unit
  would need to read the journal, more than its dynamic user may); a notice for "a run without a merge although there were
  candidates" (no measure of a candidate that would not also fire for a quiet run; a refused merge is a notice already).

## Addendum ARMAM-603 — the shops are asked over HTTP/2 (2026-10-18)

- **Why:** a full run started with an emptied state on 2026-10-18 at 13:06 was stopped after about an hour: every one of the
  ten Shopify shops answered 429 (`Retry-After 60 s`), the stretched interval of Addendum ARMAM-595 did not help, and after
  315 products two had merged. 261 answers 429, all to `/products/<handle>.js`, none to `search/suggest.json`. The guess was
  a limit per client address across all Shopify storefronts.
- **Measured** from the server with curl and the user agent `ABD/1.0`, then with the service itself (`--dry-run`):

  | Probe | Requests | 429 |
  |---|---|---|
  | `.js`, one shop, one a second, IPv4 and IPv6 | 159 / 158 | 0 |
  | `.js`, ten shops in turn, one a second together | 273 | 0 |
  | `.js`, ten at once every five seconds (two a second), IPv4 and IPv6 | 580 / 580 | 0 |
  | the service before this addendum, eight minutes | 34 products | 38 |
  | the same `.js` address that answered the service 429, curl as is / with the service's headers | | 200 / 429 |
  | one header at a time: `Accept`, `Accept-Language`, compression / `--http1.1` | | 200 / 429 |
  | `.js` over HTTP/1.1, one per shop three seconds apart | 8 | 8 |
  | `search/suggest.json` over HTTP/1.1 | 1 | 0 |
  | the service with this addendum, eight minutes (`--auto-merge`); every shop answered every product, 24 would merge | 60 products | 0 |

  The trigger is HTTP/1.1 on `.js`, not the rate: `HttpClient` asks over HTTP/1.1 unless told otherwise, curl negotiates
  HTTP/2. No limit per address across shops was found up to two requests a second. All eleven sources speak HTTP/2. This is
  the case Addendum ARMAM-602 rejected curl for ("a shop may answer curl and refuse the service's client"); the service's
  own run above is the measure.
- **Every request goes over HTTP/2** (`RequestVersionOrLower`): a host without it is asked over HTTP/1.1, and the journal
  names it once per run (`<host> answers over HTTP/1.1`), since Shopify throttles that. §6's five seconds per host,
  `Retry-After`, the stretched interval and the catch-up of Addendum ARMAM-595 stay as they are, now a net.
- Guess, not measured: the 429 of the first auto-merge run (Addendum ARMAM-595) and the browser string of §7 met the same
  HTTP/1.1 guard. `www.fantasywelt.de` stays out (Addendum ARMAM-596): it answered 403 over HTTP/2 as over HTTP/1.1 (2026-10-18).
- **Run length:** the 19 hours of Addendum ARMAM-595 stay the documented ceiling; a run without 429 should be shorter, the
  next full run measures by how much. ARMAM-603 closes after the first half hour of that run (maintainer's decision); the
  whole pass, two sources for most products, is checked under ARMAM-598.
- **Rejected:** **a common interval for all Shopify hosts**, **one Shopify shop after the other** and **asking no other shop
  after a shop's first 429** (each answers a limit per address that was not found, and each lengthens the run);
  **HTTP/2 only** (`RequestVersionExact`: a host without it would never answer, one source lost silently); **a notice mail
  when a host answers over HTTP/1.1** (it breaks nothing, the 429 count at the end of the run shows its effect).

## Addendum ARMAM-610 — upgrade sets, parts and prose are no models (2026-10-19)

- **Why:** `--id-report` on the table after the full run of 2026-10-18 (984 boxes, 1857 entries) listed among the gaps
  without a hit many names that are no model: parts of upgrade sets ("purity seals", "Left arm with lasgun", "Breacher
  combi-meltas"), weapons listed in model kits ("heavy bolters", "chainsword", "Boltguns" in Havocs, Chaos Terminators,
  Legionaries), rests of sentences ("of the following miniatures", "of the squad into", "model", "Easy", "mounted", "atop")
  and names with prose around them ("sinister Gaunt Summoner", "Blade Champion model", "Annihilators wielding meteoric
  hammers", "Lord-Veritant with 1x Gryph-crow").
- **Upgrade sets hold no box contents.** A product whose name says `upgrade(s)`, `accessories` or `sponsons` is no
  candidate (§4); in warhub-catalog's `products.json` (2026-10-19) about eighty Games Workshop "miniatures" carry such a
  name, every one a set of parts, weapons, heads or icons ("Black Templars: Upgrades and Transfers", "Cadian Upgrades",
  "Breacher Squad Upgrade Set", "Baneblade Sponsons & Accessories"). The table held 13 of them (about 140 entries, none
  with ids).
- **One rule for a read name** (`ContentLines.Tidy`), used by the lines (§8), the build sentences (Addendum ARMAM-599) and
  the repair of stored names (ADR 0004 Addendum ARMAM-598):
  - cut before the name: up to three lower-case words before the first capital ("mounted Chaos Knights", "unit of Dark
    Reapers", "varying towers of Nurglings"); a longer run is a part named for its bearer ("blade and cleaver for the
    Champion") and stays as it is;
  - cut after the name: "wielding", "wearing", "clad", "equipped", "armed", "inscribed", "accompanied", "usable" and what
    follows, "with <count>x …", "in several …", and trailing "in", "model(s)", "miniature(s)", "mounted";
  - no model: a name that starts with "of"; a name of nothing but filler (of, the, following, squad, into, model(s),
    miniature(s), easy, mounted, atop, riding, huge, sinister, unhooded, can, be, assembled, additional, crew, drones,
    sergeants, commanders, plastic, metal; plural only, a Kill Team lists a single "Sergeant" as its operative); a name
    ending in a hand weapon or piece of kit (chainsword, chain axe, power sword/axe/maul/fist/lance, thunder hammer,
    lightning claw, chainfist, meltagun, plasma gun/pistol, boltgun, bolter, bolt pistol, missile launcher, lascannon,
    autocannon, flamer, combi-…, reaper chaincannon, shotgun, backpack, helmet, rope, crate, shovel, bayonet) unless "with"
    or "w/" names its bearer ("Captain with Thunder Hammer", "Gunner w/ Meltagun"); "tiles" joins the excluded words of §8.
    Guns that are models ("Grand Cannon", "Organ Gun", "Flamers of Tzeentch") are not in the list.
- **Measured** against every root entry name of the online catalogues of all readings (2026-10-19): the new rules drop no
  model (only the excluded words of §8 did before), and cut two names that are none as written ("Combat Elixir equipped
  in current battle"; "Mist-clad Revellers" keeps its hyphen); a first list dropped fourteen Kill Team operatives named "… w/ Meltagun" or "… w/ Plasma Gun", hence "w/". On the table,
  the repair renames 80 entries, removes 77 and 28 boxes: the 13 upgrade sets, and 15 boxes whose only entry was noise
  ("Nighthaunt: Chainrasps" held only "Easy"), which the next run proposes again with the new reader.
- **The repair draft** (`scraper/fix-names`, ADR 0004 Addendum ARMAM-598) applies the same rule and removes a service box
  named as an upgrade set whole, also with ids (none had any); unlike a box left without entries, it is not proposed again.
- **Rejected:** **keeping an upgrade set as a box without models** (the schema asks for a reading, the app would need a
  box without contents); **only filtering the parts of an upgrade set** (arms, icons, ribbons and seals are too many words);
  **"no capital letter, no model"** (misses "Boltguns" and "Backpacks" of a shop's list, and "Easy"); **dropping a name with
  prose around it** (the model it names would be lost).

## Addendum ARMAM-642 — a list of names builds one model each (2026-10-20)

- **Measured** on "Astra Militarum: Ciaphas Cain" (99120105152), in the product catalogue and a candidate since its
  release: five shops confirm it, none states a count the reader takes, so the box waits ("no source states a model
  count"). `www.goblingaming.co.uk` lists "1 Ciaphas Cain miniature" and "1 Jurgen miniature" without bullet or "x";
  `www.goblingaming.co.uk` and `www.retrofixgames.com` write "This multipart plastic kit builds Ciaphas Cain and Jurgen".
- **A line of a miniature:** a counted line without bullet or "x" is read when its name ends in "miniature(s)"; the word
  goes, as before. Parts, bases and guides stay out by the rules of Addendum ARMAM-610.
- **A build of names:** a build verb followed by two or more names, each starting with a capital, joined by commas and
  "and", is one model per name. It counts only when every name names a root entry, by a hit or by an alias (ADR 0004
  Addendum ARMAM-642); one miss and the sentence counts nothing ("builds the hero and his retinue" names no model). The
  two sources of §9 stay.
- **The run that brings it:** the candidates are already the codes without a box line; a run after the deploy with
  `state.json` emptied asks them all again, the prose kits waiting since the full run of 2026-10-18 among them. No mode of
  its own.
- Decisions of the maintainer. Rejected: only the miniature line (one source, the box would still wait); each name that
  hits counting while the others drop (a box would lose a model); a run mode for named codes or for codes new in the
  product catalogue.

## Addendum ARMAM-655 — Shopify's product address goes through curl, the table over 1 MB through the blob API (2026-10-21)

- **Why:** from 2026-10-20 on, the server's service drew 429 (`Retry-After 60 s`) from Shopify on `/products/<handle>.js`
  from the first request, over HTTP/2, while the full run of 2026-10-18/19 had none. Neither the fetcher nor the server's
  packages had changed since. No full run could start; "Astra Militarum: Ciaphas Cain" (99120105152) stayed missing.
- **Measured** from the server on 2026-10-21 (service `--dry-run --host`, as `ben`, and curl with `ABD/1.0`):

  | Probe | Answer |
  |---|---|
  | the service, first `.js` after a long pause (`www.goblingaming.co.uk`) | 200 |
  | the service, every `.js` in the next minutes, to that and two other shops (`abyssgamestore.ca`, `www.retrofixgames.com`) | 429 |
  | the service, one `.js` after a rest of five, ten, fifteen and twenty minutes | 429 |
  | curl `--http2`, the same three addresses, IPv4 and IPv6 | 200 |
  | curl `--http1.1`, the same addresses | 429 |
  | the service with this addendum, four shops, three codes, twice in a row | 24 lookups, no 429 |

  curl 8.14.1 on the server uses the same OpenSSL 3.5.7 as .NET 10.0.11, so the address and the TLS library are not
  what differs; Shopify sorts the .NET client from this address into a bucket shared by every storefront that refills
  in more than twenty minutes, which is no rate a run can keep (ten Shopify sources, about 3300 products).
- **Shopify's product address goes through curl** (`CurlHandler`, a `DelegatingHandler` before the
  `SocketsHttpHandler`): `curl -sS --http2 --compressed --max-filesize 16M -D -` with the request's headers, started
  without a shell, its answer turned into a response. `search/suggest.json`, `elementgames.co.uk` and GitHub stay with
  the .NET client. The fetcher is unchanged: `Retry-After`, the stretched interval, block pages, the redirect within the
  host and the journal line for an answer below HTTP/2 apply to curl's answer as before. the client's 20 s timeout
  cancels the call and kills curl; curl's own connect timeout (exit 28) is a timeout too, any other exit an answer
  missing. curl starts with `-q` (no `.curlrc`), `-g` (no globbing of a handle) and `--proto =https`. The server needs `/usr/bin/curl` (Debian
  package, installed).
- **Bound (maintainer's decision):** curl goes as it is, under the service's own name `ABD/1.0` (§7); no browser string,
  no cipher list or other setting shaped to look like another client, no other address or machine. A 429 with
  `Retry-After` is a throttle the fetcher honours, not a guard; captchas, challenges, block pages and a 403 stay as §5
  and Addendum ARMAM-596 say. Measuring with the service's own client (Addendum ARMAM-602) stays; that client is now curl
  for this address.
- **The table over 1 MB** (found in the full run started on 2026-10-21, invocation `816e5437…`): after 61 merges, at
  00:31 on 2026-10-22, the table reached 1,048,715 bytes; above 1 MB GitHub's contents API answers with `encoding` `none` and an empty `content`, so the
  next read after a merge threw and the run failed. A file answered that way is read through the blob API
  (`git/blobs/<sha>`, base64 up to 100 MB) with the same token; the sha for the update stays the contents API's.
  Rejected: the raw media type (a second request all the same, and no sha), `raw.githubusercontent.com` (a second host,
  cached, unauthenticated).
- **Rejected:** **keeping the bucket's pace** (allowed, but refills in more than twenty minutes: weeks for one run);
  **shaping .NET's TLS settings** (closer to imitating, and HTTP/2 framing would stay); **a libcurl package** (a
  dependency for what one process does); **curl for every request** (only this address was measured to need it);
  **asking Shopify or the shops** (maintainer's decision); **a run from another machine** (would sidestep the
  classification of the server's address).

## Addendum ARMAM-688 — articles, units, members and mounts (2026-10-27)

- **Measured** on the full run of 2026-10-22 and the table at `1d208b9`, against the shops' texts and the online
  catalogues (10th and 11th edition):
  - "Krieg Heavy Weapons Squad" (99120105130) stood with count 1 and its "Fire Coordinator" without ids. The shops write
    "builds a Krieg Heavy Weapons Squad – consisting of three Heavy Weapons Gunners and one Fire Coordinator"; the article
    counted the unit as one model, only "and one Fire Coordinator" was read, and the gunners were lost. The catalogues
    hold the unit as three gunners and one Fire Coordinator, models inside the unit, no root entry of their own.
  - Four more units by article stood with count 1: Ravenwing Command Squad, Cadian Command Squad, Krieg Command Squad,
    Grey Knight Strike Squad.
  - "Darkoath Chieftain on Warsteed" (99120201181) pointed at the Chieftain on foot: "on" ends a name in a build
    sentence, so "builds a Darkoath Chieftain on Warsteed" read "Darkoath Chieftain". Nine more mounted names were cut the
    same way (Chaos Lord on Karkadrak, Vampire Lord on Nightmare Steed, Tomb King on Necrolith Bone Dragon and others).
  - "Ciaphas Cain makes a striking addition to any Imperial Guard collection" read a build "1 x striking addition". The
    agreement dropped it (an article without a hit), but it hid any build later in the same sentence.
- **An article before a lower-case name is no build:** the reader tries the sentence further on. This is no name rule:
  "no capital letter, no model" stays rejected for lines and builds by number (Addendum ARMAM-610, "Boltguns" and
  "Easy" of a shop's list); it holds only for a build whose count is an article.
- **A unit by article states no model count:** a build whose count is an article and whose name carries a unit word
  (squad, team, mob, pack, unit, band, regiment; `CatalogueIndex.IsUnitName`) counts nothing by itself. "builds a
  Farseer" still counts one.
- **Members:** right after such a build, "consisting of", "comprising" or "made up of" followed by counted names joined
  by commas and "and" names the unit's models. They count, one line each, when the catalogue lists every one of them as a
  model of the unit the build hits, by its name or without its gear ("Heavy Weapons Gunners" for "Heavy Weapons Gunners
  w/ Lascannon"); each takes the unit's ids (ADR 0004 Addendum ARMAM-688). Else the source counts its lines, or the box
  waits.
- **A mount:** the words after "on" right behind a build's name, up to the next clause, are offered as a longer name
  ("Darkoath Chieftain on Warsteed", "Primaris Chaplain on bike"); the agreement takes it instead of the shorter one when
  it names a root entry. "on" still ends a name that does not hit ("10 Kroot on Citadel bases").
- **The `--host` probe** shows a longer name ("or …") and members ("member …").
- **Boxes already in the table** keep their rows: a reader change never touches a box with ids. Of the sixteen service
  boxes measured, the `--host` probe on the server read eight better with this reader in at least two sources: Krieg
  Heavy Weapons Squad, Darkoath Chieftain on Warsteed, Vampire Lord on Nightmare Steed, General of the Empire on Imperial
  Griffon, Chaos Lord on Karkadrak, Fusil-Major on Ogor Warhulk, Primaris Chaplain on Bike, Lord on Chaos Dragon. They
  are removed in a data pull request the maintainer merges, their marks in `state.json` are deleted, and the next run
  proposes them with this reader. The other eight stay: Krieg Command Squad and Grey Knight Strike Squad name no members
  and would wait; Cadian Command Squad names "Veteran Guardsmen", the catalogue "Cadian Veteran Guardsman", no member, it
  would wait; Ravenwing Command Squad is described by most sources as its Black Knights build; the Frostlord box's sources
  name different mounts; the Tomb King box builds one of two heroes; Warrior Priests of Ulric and Cadian Heavy Weapons
  Squad read as before.
- Decisions of the maintainer. Rejected: one line "Krieg Heavy Weapons Squad" with the sum of four (the Fire Coordinator
  would lose its name); the members through `aliases.json` (one row per box by hand); an article on a unit counting the
  catalogue's minimum (the box can differ); every entry of type unit as a unit (single heroes are units of one model in
  the catalogues); "on" always carrying the name on (a base or a board after it); dropping "makes" as a build verb ("This
  kit makes 10 …"); keeping the rows and waiting for a recheck that never comes.
