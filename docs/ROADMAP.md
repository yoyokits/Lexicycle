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
picker, session, summary — wired via MVVM.

Verified on the Android emulator: a full session including a missed word looping into a
second round and ending on the summary.

The two twelve-word starter sets written here were **deleted in Phase 3**. They existed
only because the dictionary did not yet, and once it did they were worse than useless: a
learner exhausted one in two sittings. Frequency bands replaced them.

## Phase 2 — Python data pipeline ✅

Offline tooling under `src/python`, shipped with nothing. Streams the English Wiktionary
extract and distils it into the three-table dictionary described in `DATA-SOURCES.md`:
`download` → `build` (with `wordfreq` ranking) → `report` → `export-json`.

The German edition was tried first and rejected on the data; see `DATA-SOURCES.md`.
Fixture-tested, so the transform is verifiable without the 3.2 GB download.

## Phase 3 — Dictionary in the app ✅ (mostly)

The generated dictionary ships as a bundled `MauiAsset` and every session draws from it.
Per-word progress persists in a separate database, scoped so each pool rotates
independently.

Bundled SQLite won over bundled JSON on query needs, not size: the whole dictionary is
536 KB either way, but session generation needs frequency-ordered queries with exclusion
sets.

**Selection took three attempts.** It started as a Leitner schedule with per-box
intervals, which repeated missed words in the very next session; then gained a hard
no-consecutive-repeat rule; and finally became the current policy — at least 80% words
never asked before, the remainder drawn by failure-weighted random sampling, and *nothing
ever asked twice*. A pool with nothing new left yields an empty session rather than a
replay. The interval scheduling was removed; boxes survive only as a mastery statistic.
`ARCHITECTURE.md` has the rules.

The home screen offers frequency bands — Basics, Common words, Wider vocabulary — in
place of the deleted starter sets, all sharing one progress record.

A milestone bar at the foot of the home screen counts words answered correctly against
the next rung — 10, 50, 100, 500, 1000, then every further thousand — and passing one is
congratulated on the summary screen. The rungs are close together early, where a beginner
needs to see movement, and widen once progress is steady.

Still open: reverse-direction practice (R-305), the attribution screen (R-306), and a
global reset (R-323 — per-set restart landed with R-310).

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
further language pairs, and user-created sets.

Session history, spaced repetition and frequency-banded practice all landed early, in
Phase 3 — session generation needed the first two to avoid repeating words, and the
bands replaced the starter sets that were too small to be useful.

**There is no Spanish content.** `en-es-basics.json` went with the other starter sets, and
the dictionary is English→German only. Restoring it means R-504 rather than a new JSON
file: the English Wiktionary carries translations for every language, so another pair is a
filter change in the pipeline.

The 4.45 GB `de-wiktionary-sqlite-full` dataset becomes worth revisiting here, for
examples and IPA.

*Requirements: R-501 … R-507.*

---

## Out of scope

Cloud or backend services, iOS/Windows heads, full dictionary entries, multi-sense
disambiguation, account sync.
