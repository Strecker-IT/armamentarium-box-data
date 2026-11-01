# Architecture

Binding target structure of this repository. Derived from the state at creation (2026-10-17) and from Armamentarium
ADR 0118 (main repository, `docs/adr/0118-box-contents-table.md`), which decided that this repository exists.

## 1 — What this repository is

A data repository. Its product is **one table**, `box-contents.json`, read by the Armamentarium app at build time (the
app bundles a fixed commit of it; it never fetches it at runtime). Everything else exists to keep that table correct.

## 2 — Units

| Unit | Path | Role |
|---|---|---|
| Table | `box-contents.json` | The data. Single file, `format` number at the top. |
| Schema | `box-contents.schema.json` | JSON Schema draft 2020-12 of the table. The table references it with `$schema`. |
| Contract prose | `README.md` | The rules the schema cannot express (key uniqueness across boxes, what goes in, NPO fallback, sources). |
| Aliases | `aliases.json`, `aliases.schema.json` | Configuration of the scraper service, read from `main` every run: a model's own name to a root entry by entry and catalogue name, never ids (ADR 0004 Addendum ARMAM-642). Not read by the app; own `format` number, independent of the table's. |
| Decisions | `docs/adr/` | This repository's decisions, index `docs/adr/INDEX.md`. |
| Scraper service | `src/Armamentarium.BoxData.Scraper/`, tests `tests/`, solution `armamentarium-box-data.slnx` | Opens pull requests with new boxes (ADR 0003); with `--auto-merge` sets ids by name and merges them itself (ADR 0004). See §4. |
| Operation | `deploy/` | systemd service, its failure unit (ADR 0003 Addendum ARMAM-600) and timer, deploy script. Runbook: Armamentarium `docs/server/betrieb.md`. `*.service` and `*.timer` are LF (`.gitattributes`, `.editorconfig`): systemd reads a trailing CR as part of a value. Every other file stays CRLF. |

The table, the schema and the README form **one contract**. A change to one that the other two do not follow is a defect.

## 3 — Format rules

- **Format number.** A change a reader of the current `format` would misread (a field removed, renamed, its meaning
  changed, a new required field) raises `format` by one, in the table and in the schema's `const` together. Adding an
  optional field a reader may ignore does not.
- **Keys** are full article numbers (`productCodes`), never the nine-digit remainder.
- **Readings** are `kt`, `40k`, `aos`, `tow`, `hh` (ADR 0002), a reading family, not an app id. A new reading is a format
  decision; ADR 0002 decided that an added reading value does not raise the number, because a reader of format 1 ignores
  a reading it does not know. A Kill Team product gets `kt` only from the service (ADR 0002, Addendum ARMAM-601).
- **No guessed data.** Catalogue ids come from the catalogues, never from a similar name; counts come from at least two
  sources, disagreement is recorded as `countCandidates`. One named exception (ADR 0004): while the service runs with
  `--auto-merge`, it sets ids where an entry's name names one root entry of its reading (ADR 0004, rules of a hit in
  Addendum ARMAM-598); the same hit picks a build sentence over a source's lines and lets two names of one entry confirm
  each other (ADR 0003 Addendum ARMAM-599); an own name listed in `aliases.json` gets the ids of its target, resolved by
  entry and catalogue name, one line per model beside its entry's others (ADR 0004 Addendum ARMAM-642); no id is ever
  written into `aliases.json`; a member of a built unit the catalogue lists under it gets the unit's ids, one line per
  model (ADR 0004 Addendum ARMAM-688).
- **Aliases format.** `aliases.json` carries its own `format` number, raised by the same rule as the table's and
  independent of it.

## 4 — Scraper service

Fixed by Armamentarium ADR 0118 §4 and its Addenda ARMAM-591 and ARMAM-593, built under ARMAM-591, decided in ADR 0003
and ADR 0004:

- Its own .NET 10 project **in this repository**, not part of `Armamentarium.slnx`, never shipped with the app.
- Runs on the 1st and the 15th of every month on the project's own server (systemd timer).
- Changes the table **only through a pull request**. With `--auto-merge` (ADR 0004) it merges each one itself, except the
  drafts of orphaned ids and of name repairs (ADR 0004 Addenda ARMAM-597, ARMAM-598), until the maintainer removes the switch; without it, it never merges and the merge is the hand check.
- Write access through a fine-grained token scoped to this repository only (contents and pull requests), without expiry
  (ADR 0004), stored outside every repository, never logged.
- Catalogue ids from the catalogues of a compiled allowlist of repositories (ADR 0004 §3), downloaded every run, with or
  without `--auto-merge`; every run first gives the gaps of its own boxes their ids and then checks the ids the table
  holds against them (ADR 0004 Addenda ARMAM-597, ARMAM-598). Own names of models that belong to a root entry of
  another name go to it through `aliases.json`, read from `main` every run, resolved by entry and catalogue name, never
  ids by hand (ADR 0004 Addendum ARMAM-642).
- Candidates from warhub-catalog `products.json`, one request per run to a compiled address (ADR 0003 §4).
- Sources from a compiled allowlist, no open search, measured with the `--host` probe before they enter (ADR 0003
  Addendum ARMAM-602); at most one request per five seconds and host (ADR 0003 §6; no host needs the per-minute interval
  since ARMAM-602), stretched up to ten seconds for a host that answers 429 three times and
  never before a 429's `Retry-After`; a product such a shop did not answer is asked once more at the end of the run (ADR
  0003 Addendum ARMAM-595); asked over HTTP/2, HTTP/1.1 only for a host without it (ADR 0003 Addendum ARMAM-603), Shopify's product address through curl
  (ADR 0003 Addendum ARMAM-655); own user agent
  without a contact address (ADR 0003 §7). Captchas and challenges are never circumvented.
- Counts from a source's "N x Name" lines or from its build sentences ("builds a squad of 10 …"; ADR 0003 Addendum
  ARMAM-599), a line "1 <name> miniature", a build of names ("builds Ciaphas Cain and Jurgen") when every name hits (ADR
  0003 Addendum ARMAM-642), the members of a unit built by article ("consisting of three … and one …"), never the unit
  by article itself, a name carried over "on" when it hits (ADR 0003 Addendum ARMAM-688), never from part counts. A read name passes one rule: prose around it is cut, parts, hand weapons and filler
  words are no model; upgrade sets are no candidates (ADR 0003 Addendum ARMAM-610).
- Proposes one pull request per box, every entry confirmed by two sources: a **draft** without ids, or with `--auto-merge`
  a ready one with the ids of the one root entry a name names (ADR 0004 Addendum ARMAM-598), merged at once (a merge refused with 405 is tried again, ADR 0004
  Addendum ARMAM-603).
- Besides, at most one draft `scraper/stale-ids` that removes orphaned ids (an id naming no root entry of its reading any
  more); never merged by the service, also with `--auto-merge`, and counted among the five open (ADR 0004 Addendum ARMAM-597).
- At the start of a run, before the id check, one pull request `scraper/backfill-ids` that gives the gaps of boxes it wrote the ids their names now name,
  merged like a box; after it, one draft `scraper/fix-names` that repairs names read before the reader's fixes and removes
  upgrade sets whole, never merged (ADR 0004 Addendum ARMAM-598, ADR 0003 Addendum ARMAM-610); it also removes a gap beside the line of its own root entry with the same count (ADR 0004 Addendum ARMAM-707). `--id-report` prints the gaps per reading with their reason and changes nothing.
- Mails the maintainer (ADR 0003 Addendum ARMAM-600): a failed run through `OnFailure=` and the failure unit
  (`--notify-failure`), a run's notices at its end; through the mailbox and the password credential of the Armamentarium
  web services, nothing from the journal.
- Every further dependency of the service is decided with a reason (ADR in `docs/adr/`).

## 5 — Not in this repository

- Code of the Armamentarium app, its reader of the table, its conversion script — they live in the main repository.
- Texts, images, prices or descriptions from any source. Facts only (which entry, how many models).
