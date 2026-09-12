# Lexicycle

A .NET MAUI (Android-first) vocabulary trainer. Show a word, type the translation;
missed words loop back into later rounds until every word has been answered correctly in
one clean round.

## Where things are

```
src/Lexicycle/LexicycleApp/     MAUI head (net10.0-android) — Views, ViewModels, DI
src/Lexicycle/LexicycleCore/    plain net10.0 library — models, engine, stores. No MAUI.
src/Lexicycle/LexicycleCore.Tests/
src/python/                     offline data tooling; NOT shipped in the app
data/raw/                       downloaded Wiktionary data (gitignored)
data/dist/                      generated dictionary
```

| Doc | Read it when |
| --- | --- |
| `docs/BUILD.md` | building, testing, deploying, or running the data pipeline |
| `docs/ARCHITECTURE.md` | changing the engine, scheduling, storage or navigation |
| `docs/DATA-SOURCES.md` | touching the dictionary or `src/python` |
| `docs/ROADMAP.md` | wondering what phase something belongs to |
| `REQUIREMENTS.md` | picking up work — open items, `R-NNN` |
| `REQUIREMENTS-DONE.md` | checking whether something is already built |

## Key constraints

- **A prompt accepts every common answer, across all its senses** — "run" takes `laufen`,
  `rennen` *and* `fließen`, because each is a correct translation of the bare word
  (R-511). What it is *not* is disambiguation: the app never says which meaning it wants,
  shows no per-sense hint or example, and grades only whether the answer is *a* correct
  one. Answers are stored most-common-first; the first is what a miss reveals. Breadth is
  bounded by target-language frequency (`MAX_ANSWERS`, `_RANK_GAP` in `database.py`),
  never by sense labels — see `docs/DATA-SOURCES.md`.
- **No word is ever asked twice.** Every session is ≥80% material never seen before; the
  rest is failure-weighted revision, and nothing from the previous session. A pool with
  nothing new left yields an *empty* session, never a replay. Read the selection rules in
  `docs/ARCHITECTURE.md` before touching `SessionComposer` — this has been re-reported as
  a bug three times.
- **`words_en.freq_rank` is not a rank.** It is `round((8 − zipf) × 1000)`: ~1,590 to
  6,990, heavily tied. `ORDER BY` it; never filter on its value. Positional slicing needs
  `ORDER BY freq_rank, id` with `LIMIT`/`OFFSET`.
- **English-German and English-Spanish ship as bundled dictionaries**; each can be
  practised in either direction (a home-screen toggle, R-305) without a second database —
  reversed reads the same curated `translations` rows backwards. The pipeline,
  `LanguagePair`, `FrequencyBand` and progress scoping are all pair-*and-direction*
  parameterised, so a third pair is configuration (one code in `sources.TARGET_LANGUAGES`)
  rather than redesign. A build missing a pair's `.db` behaves as if that pair does not
  exist; the language switcher only appears once more than one pair is bundled.
- **Dictionary data comes from the *English* Wiktionary edition**, not the German one.
  The German edition fragments multi-word translations and is unusable — read
  `docs/DATA-SOURCES.md` before changing the source.
- **Fully offline.** No backend, no runtime network calls.
- **`src/python` is build-time tooling.** Nothing in it ships inside the app.
- **All round, answer and scheduling logic lives in `LexicycleCore`**, stays free of MAUI
  types, and is unit tested. `LexicycleApp` only binds to it.

## Conventions

- MVVM via `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- Compiled bindings everywhere: every XAML page and `DataTemplate` sets `x:DataType`.
- Bind `IsVisible` to a real `bool` property (`HasHint`, `HasError`) — a string does not
  convert.
- Hints must never contain their own answer — `das … (neuter)`, never `das Haus`.
  `PracticeSessionFactoryTests` and a Python exporter test enforce it.
- New extraction or scheduling rules need a test that would fail without them. Row counts
  and green builds have both looked healthy while the behaviour was wrong.
- **Make test fixtures resemble the real data.** A fixture with dense `freq_rank` values
  1..N passed while the shipped dictionary produced an empty band; one that played every
  session perfectly never reached the relearning path that was broken.

## Working agreements

- Requirements are the source of truth for scope. Completed items **move** from
  `REQUIREMENTS.md` to `REQUIREMENTS-DONE.md` with a date, so the open file only ever
  shows outstanding work.
- Verify on the emulator before calling UI work done, and say plainly what was and was
  not checked.
- Keep this file short. Detail belongs in `docs/`.
