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
- [x] **R-202** Upstream source selected and documented: `cstr/de-wiktionary-extracted` parquet (287 MB) rather than `de-wiktionary-sqlite-full` (4.45 GB) — same content, ~15× smaller. See `docs/DATA-SOURCES.md`.
- [x] **R-203** `download` step fetching the parquet files into `data/raw/`.
- [x] **R-204** Extraction rules: German lemma, part of speech, gender from `tags`, English terms from `translations` where `lang_code == "en"`; rejects uncertain translations, glosses and multi-word explanations.
- [x] **R-205** Three-table schema (`words_en`, `words_de`, `translations`) plus a `meta` table recording source, licence and cut-off.
- [x] **R-206** `wordfreq`-based English frequency ranking, driving the top-N cut.
- [x] **R-207** `report` step printing row counts and on-disk size for N = 1k / 5k / 10k / 50k / all.
- [x] **R-208** `export-json` step emitting a `VocabularySet`-shaped file the app can consume before any on-device SQLite work.
- [x] **R-209** Fixture-based pytest suite (44 tests) covering extraction, schema, de-duplication, the top-N cut and the exporter — no download required.
