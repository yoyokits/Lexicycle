# Roadmap

Deliberately phased. Each phase leaves the app working; nothing is built ahead of need.
Open items live in `REQUIREMENTS.md`, completed ones in `REQUIREMENTS-DONE.md`.

---

## Phase 0 — Skeleton and tracking documents ✅

`src/` layout, solution, the tracking documents, `.gitignore` and the build-config seam.

## Phase 1 — MAUI trainer v1 ✅

The trainer from the original spec, on bundled JSON sets.

`LexicycleCore` first (pure C#, fast feedback): `WordPair`, `VocabularySet`,
`AnswerComparer`, `SessionEngine`, the repository seam. Then the three screens — set
picker, session, summary — wired via MVVM. Two starter sets, English–German and
English–Spanish.

Verified on the Android emulator: a full session including a missed word looping into a
second round and ending on the summary.

## Phase 2 — Python data pipeline ✅

Offline tooling under `src/python`, shipped with nothing. Turns the German Wiktionary
parquet export into the small three-table dictionary described in `DATA-SOURCES.md`:
`download` → `build` (with the `wordfreq` top-N cut) → `report` → `export-json`.

Fixture-tested, so the transform is verifiable without the 287 MB download.

## Phase 3 — Dictionary in the app

Bring the generated dictionary into the app behind the existing repository interface.
The bundled-SQLite vs bundled-JSON choice is made from the `report` numbers rather than
guessed. Adds dictionary-generated sessions, reverse-direction practice, and the
CC-BY-SA attribution screen.

*Requirements: R-301 … R-306.*

## Phase 4 — OCR photo input

Photograph a book page, recognise the text on-device with ML Kit (no cloud — the offline
constraint holds), extract candidate words, match them against the dictionary, and build
an ad-hoc set. A review screen stands between OCR and practice, because OCR of printed
text is good but not perfect and a junk token would make an unanswerable question.

*Requirements: R-401 … R-405.*

## Phase 5 — Enrichment

The word properties the dictionary already carries, plus the ones the fuller upstream
dataset can supply: gender and part of speech shown during practice, example sentences,
frequency-banded practice ("the 500 most common words"), further language pairs,
user-created sets, session history and eventually spaced repetition.

The 4.45 GB `de-wiktionary-sqlite-full` dataset becomes worth revisiting here, for
examples and IPA.

*Requirements: R-501 … R-507.*

---

## Out of scope

Cloud or backend services, iOS/Windows heads, full dictionary entries, multi-sense
disambiguation, account sync.
