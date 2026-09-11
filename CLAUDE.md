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

- **v1 is one-to-one translations.** `house` → `Haus`. Not full dictionary entries, not
  multi-sense disambiguation.
- **English↔German is the only generated pair.** The pipeline is pair-parameterised, so
  adding one is configuration rather than redesign.
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
- Hints must never contain their own answer; `BundledSetsTests` enforces it.
- New extraction or scheduling rules need a test that would fail without them. Row counts
  and green builds have both looked healthy while the behaviour was wrong.

## Working agreements

- Requirements are the source of truth for scope. Completed items **move** from
  `REQUIREMENTS.md` to `REQUIREMENTS-DONE.md` with a date, so the open file only ever
  shows outstanding work.
- Verify on the emulator before calling UI work done, and say plainly what was and was
  not checked.
- Keep this file short. Detail belongs in `docs/`.
