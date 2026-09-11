# Requirements — done

Completed and verified requirements, moved here from `REQUIREMENTS.md`.

---

## Phase 0 — Skeleton and tracking documents · 2026-09-10

- [x] **R-001** `src/` layout with `LexicycleApp`, `LexicycleCore`, `LexicycleCore.Tests` and `python/`, wired into `Lexicycle.sln`.
- [x] **R-002** Tracking documents: `CLAUDE.md`, `REQUIREMENTS.md`, `REQUIREMENTS-DONE.md`, `docs/ROADMAP.md`, `docs/DATA-SOURCES.md`, `docs/ARCHITECTURE.md`.
- [x] **R-003** `.gitignore` covers `data/raw/`, the Python venv, `__pycache__`, and `Directory.Build.props.user`.
- [x] **R-004** `Directory.Build.props` with an optional gitignored user override, so no machine path is committed.

## Phase 1 — MAUI trainer v1 · 2026-09-10

- [x] **R-101** `WordPair` (source, acceptable answers, optional hint) and `VocabularySet` (name, languages, words).
- [x] **R-102** `AnswerComparer`: trims, collapses internal whitespace, ignores case.
- [x] **R-103** Multiple acceptable answers per word ("Auto" or "Wagen" for "car").
- [x] **R-120** A leading article is optional in both directions: "das Haus" and "Haus" are both correct, whichever the set stores. German, Spanish and English articles; a bare article stays a word in its own right. Verified on-device.
- [x] **R-104** Configurable diacritics leniency, default lenient; also accepts the German ASCII fallback (`Maedchen`, `Strasse`). Verified on-device.
- [x] **R-105** `SessionEngine`: correct answers retire a word; misses defer to the next round and are never re-asked immediately.
- [x] **R-106** A round asks each of its words exactly once; the next round contains only the missed ones.
- [x] **R-107** The session ends only when a full round completes with zero misses.
- [x] **R-108** Session summary: rounds taken, per-word miss counts, hardest words first.
- [x] **R-109** `IVocabularySetRepository` with a bundled-JSON implementation and an `IAssetProvider` seam, keeping `LexicycleCore` free of MAUI types.
- [x] **R-110** Unit tests for the four spec-mandated cases plus repository and bundled-set validation (50 tests).
- [x] **R-111** Two bundled starter sets (English–German, English–Spanish, 12 words each).
- [x] **R-112** Home / set-picker screen listing the available sets.
- [x] **R-113** Session screen: prompt, hint, answer entry, "Round k", "Word n of m", progress bar, correct/incorrect feedback.
- [x] **R-114** Enter-key submission as well as the button. Verified on-device.
- [x] **R-115** Summary screen showing rounds taken and the missed words.
- [x] **R-116** MVVM throughout with `CommunityToolkit.Mvvm`, DI registered in `MauiProgram`, Shell routes.
- [x] **R-117** Diacritics-leniency toggle on the home screen, persisted via `Preferences`.
- [x] **R-118** Builds and runs on Android; a full session including a missed word looping into round two was completed on the `pixel_6a_-_api_36_0` emulator.
- [x] **R-119** Hints never reveal their own answer (`das … (neuter)`, not `das Haus`). Enforced by `BundledSetsTests` and by the Python exporter's tests.

## Phase 2 — Python data pipeline · 2026-09-10

- [x] **R-201** `src/python` package with a `pyproject.toml`, venv instructions and a step-based CLI (`python -m lexicycle_data <step>`).
- [x] **R-202** Upstream source selected, tested against real data, and documented. The German edition (`cstr/de-wiktionary-extracted`) was tried first and **rejected**: wiktextract fragments multi-word English translations there ("piece of furniture" → `item`, `piece`, `of`, `furniture`), which no filter can undo. The source is now the **English** Wiktionary edition via kaikki.org. See `docs/DATA-SOURCES.md`.
- [x] **R-203** `download` step streaming the ~3.2 GB extract and distilling it to a small gzipped file, so extraction can be re-tuned offline. Verifies the transferred byte count against `Content-Length` and resumes with HTTP Range on a dropped connection — a truncated stream is otherwise indistinguishable from a complete one, since the JSON stays valid and there is simply less of it.
- [x] **R-210** TLS verification via `truststore` (the OS certificate store), needed wherever antivirus or a proxy inspects HTTPS — Avast on this machine. Includes a clear CLI error pointing at the fix.
- [x] **R-204** Extraction rules: English lemma, content parts of speech only, primary-sense translations only, gender from each translation's `tags`; rejects obsolete/archaic tags, glosses and multi-word explanations.
- [x] **R-205** Three-table schema (`words_en` with `pos`, `words_de` with `gender`, `translations`) plus a `meta` table recording source, licence and cut-off. `schema_version` 2.
- [x] **R-206** `wordfreq`-based English frequency ranking, driving the top-N cut.
- [x] **R-207** `report` step printing row counts and on-disk size for N = 1k / 5k / 10k / 50k / all.
- [x] **R-208** `export-json` step emitting a `VocabularySet`-shaped file the app can consume before any on-device SQLite work.
- [x] **R-209** Fixture-based pytest suite (57 tests) covering extraction, primary-sense selection, schema, de-duplication, the top-N cut and the exporter — no download required.

## Phase 3 — Dictionary in the app · 2026-09-11

- [x] **R-301** Bundled SQLite chosen over bundled JSON: the whole dictionary is 536 KB, so size did not decide it — the session generator needs frequency-ordered queries and exclusion sets, which SQL does and a JSON blob does not.
- [x] **R-302** `sqlite-net-pcl` in `LexicycleCore`, with `SqliteDictionaryStore` (read-only) and `SqliteProgressStore` (read-write).
- [x] **R-303** The generated dictionary bundled as a `MauiAsset` and copied to app data on first run, versioned so a future dictionary refreshes the copy.
- [x] **R-304** "Practice" on the home screen generates a session from the dictionary, most frequent words first.
- [x] **R-506** Per-word progress persisted in its own database file, so shipping a new dictionary never discards it. Survives app restart.
- [x] **R-507** `ReviewSchedule`: a Leitner scheme counted in sessions rather than days. A miss returns a word to box 0 for the next session; correct answers push it out 5, 12, 30 then 90 sessions before it is mastered.
- [x] **R-309** Consecutive generated sessions ask different words. Externally chosen sets — bundled JSON, and later OCR — bypass rotation entirely and drill exactly what they were given. Verified on-device across three sessions with zero overlap.
- [x] **R-503** Frequency-banded practice, replacing the starter sets. `FrequencyBand.All` offers Basics (the 1,000 most common), Common words (the next 1,000) and Wider vocabulary (the remaining 1,545), sized from the dictionary rather than hard-coded. Bands share the single dictionary progress scope, so a word learned in one is never offered as new in another.
- [x] **R-324** The three hand-written JSON starter sets are deleted. Twelve words each was enough to demo the trainer in Phase 1 and useless once the dictionary existed — a learner exhausted one in two sittings. Nothing ships as JSON now; the repository seam stays for OCR (R-404).
- [x] **R-325** Bands slice by **position** in the frequency ordering, not by `freq_rank` value. That column holds `round((8 − zipf) × 1000)` — roughly 1,590 to 6,990 with ~480 distinct values — so a value range of 1–1,000 matched nothing and the first build shipped an empty Basics band. Documented in `docs/DATA-SOURCES.md`.
- [x] **R-321** No word is ever asked twice, in any pool. The 80% floor was being applied to the *requested* session size, so when new material ran short the session was padded back to full length out of the already-answered pool — a twelve-word set became an endless loop of the same twelve questions. Revision is now capped at `fresh / MinNewShare − fresh`, so no new words means no session at all.
- [x] **R-310** A reset action in the UI. An exhausted set says so and offers "Start this set again", which calls `IProgressStore.ResetScopeAsync` for that set only — dictionary progress and other sets are untouched. Verified on-device: the set reported finished, restarted to a fresh six-word session, and the database showed only that scope cleared.
- [x] **R-322** The session screen hides the answer box and Check button when there is no question on it, and the quit button reads "Back to sets" rather than "End session" for a session that never started.
- [x] **R-320** Bundled sets rotate like the dictionary does. Finishing "German basics" and opening it again asked the identical twelve questions, every time, because fixed sets recorded no progress at all — they were drilled whole on the reasoning that their words were deliberately chosen. `FixedSetSessionFactory` now draws half the set (capped at 10) through the same `SessionComposer`, scoped by set id with its own session counter. Verified on-device by tapping a set six times in succession, and in tests down to a three-word set.
- [x] **R-317** Generated sessions are presented most-common-first, using `words_en.freq_rank` carried through on `DictionaryWord`. Fixed sets have no frequency data, so they are shuffled instead (R-316).
- [x] **R-318** Every word asked is recorded in `word_progress`, keyed by the dictionary word id, with times seen, times answered right, times answered wrong and times right first try. `WordProgress.TimesAnsweredRight/Wrong/Accuracy` expose the statistics; right answers equal sessions seen, because a session only ends once every word has been answered correctly.
- [x] **R-319** Session selection is at least 80% words never asked before (`SessionComposer.MinNewShare`). The remainder is revision drawn by weighted random sampling, with a word's weight being its wrong-answer count plus one — so failures are prioritised without the hardest word reappearing in every session. Rule R-315 still overrides: nothing from the immediately previous session, whatever its weight. The old per-box interval scheduling was removed rather than left alongside it.
- [x] **R-316** Word order is randomised per session, on fixed sets as well as generated ones. Sets were asked in their stored order, so every visit to "German basics" opened `house dog cat car` — identical questions in an identical sequence, forever. Ordering now belongs to the session (`VocabularySet.Shuffled`, called by `SessionViewModel`) rather than to the set. Verified on-device across all three bundled sets and the generated path.
- [x] **R-315** No word is ever repeated from the immediately preceding session, *including words that were missed*. The original R-309 verification only played perfect sessions, which never exercised the relearning box and so missed the bug entirely: box 0 was due after one session, so a learner who missed a few words saw those same words in every following session. Box 0 now waits two sessions (`ReviewSchedule.RelearnDelay`), and `SessionComposer` enforces the no-consecutive-repeat rule as a hard floor independent of the delay table.
- [x] **R-313** A milestone progress bar at the foot of the home screen, counting words answered correctly at least once against the next rung of the ladder — 10, 50, 100, 500, 1000, then every further thousand. The target is always strictly above the count, so the bar never reads as finished. Verified on-device: 0 / 10 on a clean install, 10 / 50 and 20 / 50 after one and two sessions.
- [x] **R-314** Passing a milestone is congratulated on the summary screen with an animated banner — the card springs in, the trophy overshoots and wobbles, the text follows. The count is measured either side of the progress write, so a milestone is celebrated exactly once and a session that crosses several rungs reports only the highest. Verified on-device: the banner appears on the session that reaches 10 and is absent on the next one.
