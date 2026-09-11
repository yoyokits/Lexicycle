# Requirements — open

Source of truth for scope. Each item has a stable id (`R-NNN`). When an item is
finished **and verified**, move its line to `REQUIREMENTS-DONE.md` with the date, so
this file only ever shows outstanding work.

Phases are described in `docs/ROADMAP.md`.

---

## Phase 3 — Dictionary in the app

- [ ] **R-305** Practise German → English as well as English → German (reverse the pair at session start).
- [ ] **R-306** About screen crediting Wiktionary and the upstream dataset under CC-BY-SA 4.0.
- [ ] **R-323** A global "reset all progress" action. Per-set restart exists (R-310), but `IProgressStore.ResetAsync`, which clears every scope including the dictionary, still has no caller.
- [ ] **R-311** Show *mastery* on the home screen. The milestone bar (R-313) counts words answered correctly at least once, which is a weaker thing than reaching `ReviewSchedule.MasteredBox`; neither the home screen nor anywhere else surfaces the box a word has climbed to.
- [ ] **R-312** Let the learner choose the session length; it is fixed at `PracticeSessionFactory.DefaultSize` (10).

## Data quality

Known limitations of the generated dictionary, detailed in `docs/DATA-SOURCES.md`.

- [ ] **R-308** Improve sense selection so `go → gehen` rather than `machen`. The primary sense is currently just Wiktionary's first, which is often not the common meaning.
- [ ] **R-509** Admit genuine phrasal verbs ("get in", "make up") as prompts while still rejecting phrase fragments ("as in", "what if").
- [ ] **R-510** Filter untagged regional forms. Frequency ordering catches `Liab` and `Ziit`, but `home → Ham | Heim | …` still leads with a regionalism because `Ham` scores as ordinary German.

## Phase 4 — OCR photo input

- [ ] **R-401** Capture or pick a page photo with `MediaPicker`, with the camera and storage permissions handled.
- [ ] **R-402** On-device text recognition with ML Kit (offline; no cloud OCR).
- [ ] **R-403** Tokenise and normalise recognised text into candidate words.
- [ ] **R-404** Match candidates against the dictionary and build an ad-hoc vocabulary set.
- [ ] **R-405** Review screen before starting: correct OCR errors, drop junk tokens.

## Phase 5 — Enrichment

- [ ] **R-501** Show grammatical gender and part of speech during practice (gender is in `words_de`, part of speech in `words_en`).
- [ ] **R-502** Example sentences, sourced from the fuller upstream dataset, as a side table.
- [ ] **R-508** German→English practice sourced from the German edition, where its English-side fragmentation does not matter.
- [ ] **R-505** User-created and imported vocabulary sets.

## Cross-cutting

- [ ] **R-601** Accessibility pass: `SemanticProperties` on the prompt, answer entry and feedback.
- [ ] **R-602** Dark theme verified on every screen.
- [ ] **R-603** CI workflow running `dotnet test` and `pytest`.
- [ ] **R-604** Release build verified with trimming enabled (the JSON source generator must hold up).
