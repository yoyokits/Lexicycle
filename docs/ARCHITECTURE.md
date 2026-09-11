# Architecture

## Layering

```
LexicycleApp  (net10.0-android, MAUI)
   Views/         XAML pages, compiled bindings, code-behind for view concerns only
   ViewModels/    CommunityToolkit.Mvvm; turn engine state into bindable properties
   Services/      MauiAssetProvider, AppSettings — the MAUI-specific edges
        │  depends on
        ▼
LexicycleCore  (net10.0, no MAUI reference)
   Models/        WordPair, VocabularySet
   Session/       AnswerComparer, SessionEngine, SessionSummary
   Services/      IVocabularySetRepository, IAssetProvider, bundled-JSON implementation
```

`LexicycleCore` has no MAUI dependency at all. That is what lets `LexicycleCore.Tests`
reference it directly — an xUnit project cannot cleanly reference a multi-targeted MAUI
head. Two projects is the minimum that satisfies the "testable, separate from the UI"
requirement; the library gets nothing beyond what that requires.

## The session engine

`SessionEngine` owns the entire round mechanic and knows nothing about the UI.

- Round 1 contains every word in the set.
- Each word in the current round is asked **exactly once**.
- A correct answer retires the word.
- A miss is recorded and deferred to the next round — deliberately **not** re-asked
  immediately, so the user cannot brute-force it while it is still on screen.
- When the round ends, the missed words become the next round.
- The session ends when a round completes with **zero** misses, which guarantees every
  word was answered correctly at least once.
- An empty set is complete before it starts.

`Submit` returns an `AnswerResult` carrying whether the answer was right, the correct
answer to display, and whether the round or the whole session just ended. Progress is
exposed as `RoundNumber`, `PositionInRound` and `WordsInRound`.

`BuildSummary()` returns rounds taken, total words, and per-word miss counts ordered
hardest-first.

## Answer checking

`AnswerComparer` always trims, collapses internal whitespace and ignores case. It also
treats a **leading article as optional in both directions**, so `das Haus` and `Haus` are
the same answer whichever one the set happens to store. German, Spanish and English
articles are recognised; a bare article is left alone, because "die" on its own can be
the answer rather than a prefix. Gender is taught through the hint (`das … (neuter)`),
not enforced by the grader, so a wrong article does not fail an otherwise correct noun.

With lenient diacritics on (the default) it additionally accepts:

- the diacritic-stripped spelling — `Cafe` for `Café`, `Madchen` for `Mädchen`
- the German ASCII fallback — `Maedchen` for `Mädchen`, `Strasse` for `Straße`

It does this by expanding both sides into a small set of candidate spellings and testing
for any overlap, so adding another normalisation later is one more entry in that set —
and the rules compose, so `die Straße` matches a typed `strasse`. Diacritics strictness
is independent of the article rule, which is always on.

A `WordPair` carries a list of acceptable answers, so "Auto" and "Wagen" both pass for
"car". The first entry is what gets shown on a miss.

## Generated sessions and progress

Tapping **Practice** draws its words from the bundled dictionary rather than a fixed
list, and consecutive sessions ask different things.

`PracticeSessionFactory` is the only path that consults progress. Sets chosen
deliberately — the bundled JSON, and later an OCR'd page — go straight to the engine with
exactly the words they were given, because rotating those away would be wrong.

`ReviewSchedule` is a Leitner scheme counted in **sessions, not days**, so someone
practising twice a week gets the same sequence as someone practising twice a day:

| Box | Meaning | Returns after |
| --- | --- | --- |
| 0 | being learned, or just missed | the next session |
| 1-4 | answered correctly N times | 5, 12, 30, 90 sessions |
| 5 | mastered | never |

A miss drops a word straight back to box 0 — it needs relearning, not a longer wait. The
first correct interval is deliberately long: variety is the point, so a word answered
correctly should stay away for a while.

`SessionComposer` caps revision at half a session so new material keeps arriving, then
lifts that cap once the dictionary runs out of unseen words. If nothing is new and
nothing is due, it returns an empty plan and the UI says so rather than repeating.

Two database files, deliberately separate:

- `lexicycle-dict-en-de.db` — the generated dictionary, bundled as a `MauiAsset` and
  copied to app data on first run (a `MauiAsset` cannot be opened as a file on Android).
  Read-only.
- `progress.db` — created on demand in app data. Keeping it apart means shipping an
  updated dictionary never discards a learner's history.

## The repository seam

```csharp
public interface IVocabularySetRepository
{
    Task<IReadOnlyList<VocabularySet>> GetAllAsync(CancellationToken ct = default);
    Task<VocabularySet?> GetByIdAsync(string id, CancellationToken ct = default);
}
```

v1 ships `BundledJsonVocabularySetRepository`, reading `MauiAsset` JSON through an
`IAssetProvider` (`MauiAssetProvider` in the app, in-memory streams in tests). The
generated dictionary arrives in Phase 3 as a second implementation behind the same
interface — no ViewModel changes.

JSON goes through the source-generated `VocabularySetJsonContext`: reflection-based
`System.Text.Json` is stripped by the trimmer in Release Android builds.

## Navigation

Shell, with one `ShellContent` (`home`) and two registered routes:

```
home  ──GoToAsync("session?setId=…")──▶  session  ──GoToAsync("summary", {summary})──▶  summary
  ▲                                                                                        │
  └────────────────────────────── GoToAsync("//home") ◀───────────────────────────────────┘
```

Both ViewModels implement `IQueryAttributable`; Shell forwards query attributes to a
page's `BindingContext`. The finished `SessionSummary` is passed as an object through
the dictionary overload rather than being re-derived.

`SummaryPage` overrides `OnBackButtonPressed` so hardware back returns home instead of
walking back into the finished session.

## Platform constraints that shaped the code

Two Android behaviours caused real bugs and are worth stating explicitly.

**Never navigate with the soft keyboard open.** Android measures the incoming page
against the still-open IME insets and gives it zero height; the page arrives blank and
only lays out once the keyboard happens to close. `SessionPage` supplies
`SessionViewModel.HideKeyboardAsync`, which unfocuses the entry, waits for the IME to
hide, and lets the insets settle before any `GoToAsync`.

**A `CollectionView` must sit directly in a `*` Grid row.** Nested inside a
`VerticalStackLayout` it measures against infinite height and the layout collapses.

## Testing

- `LexicycleCore.Tests` covers the engine, the comparer, the repository, and validates
  the JSON sets the app actually ships (parseable, unique prompts, every answer accepted
  by the comparer, no hint containing its own answer, a clean run finishing in one round).
- `src/python/tests` covers the pipeline against an in-code fixture shaped like the real
  dataset, so extraction and schema are testable without the 3.2 GB download.
