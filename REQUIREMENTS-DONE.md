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
