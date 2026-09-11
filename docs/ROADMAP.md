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

Offline tooling under `src/python`, shipped with nothing. Streams the English Wiktionary
extract and distils it into the three-table dictionary described in `DATA-SOURCES.md`:
`download` → `build` (with `wordfreq` ranking) → `report` → `export-json`.

The German edition was tried first and rejected on the data; see `DATA-SOURCES.md`.
Fixture-tested, so the transform is verifiable without the 3.2 GB download.

## Phase 3 — Dictionary in the app ✅ (mostly)

The generated dictionary ships as a bundled `MauiAsset` and "Practice" draws sessions
from it. Per-word progress persists in a separate database, and a Leitner schedule
counted in sessions keeps consecutive sessions from repeating words — while still
bringing back anything missed. Sets chosen deliberately (bundled JSON, later OCR) bypass
rotation entirely.

Bundled SQLite won over bundled JSON on query needs, not size: the whole dictionary is
536 KB either way, but session generation needs frequency-ordered queries with exclusion
sets.

A milestone bar at the foot of the home screen counts words answered correctly against
the next rung — 10, 50, 100, 500, 1000, then every further thousand — and passing one is
congratulated on the summary screen. The rungs are close together early, where a beginner
needs to see movement, and widen once progress is steady.

Still open: reverse-direction practice (R-305), the attribution screen (R-306), and a
reset-progress action (R-310).

## Phase 4 — OCR photo input

Photograph a book page, recognise the text on-device with ML Kit (no cloud — the offline
constraint holds), extract candidate words, match them against the dictionary, and build
an ad-hoc set. A review screen stands between OCR and practice, because OCR of printed
text is good but not perfect and a junk token would make an unanswerable question.

*Requirements: R-401 … R-405.*

## Data quality

The generated pairs are good but not perfect — first-sense artefacts like `go → machen`,
surviving untagged regionalisms, and phrasal verbs excluded along with phrase fragments.
Tracked as R-308, R-509 and R-510; detailed in `DATA-SOURCES.md`.

## Phase 5 — Enrichment

The word properties the dictionary already carries, plus the ones the fuller upstream
dataset can supply: gender and part of speech shown during practice, example sentences,
frequency-banded practice ("the 500 most common words"), further language pairs, and
user-created sets.

Session history and spaced repetition landed early, in Phase 3, because generated
sessions needed them to avoid repeating words.

The 4.45 GB `de-wiktionary-sqlite-full` dataset becomes worth revisiting here, for
examples and IPA.

*Requirements: R-501 … R-507.*

---

## Out of scope

Cloud or backend services, iOS/Windows heads, full dictionary entries, multi-sense
disambiguation, account sync.
