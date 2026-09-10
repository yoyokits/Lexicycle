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

`AnswerComparer` always trims, collapses internal whitespace and ignores case. With
lenient diacritics on (the default) it also accepts:

- the diacritic-stripped spelling — `Cafe` for `Café`, `Madchen` for `Mädchen`
- the German ASCII fallback — `Maedchen` for `Mädchen`, `Strasse` for `Straße`

It does this by expanding both sides into a small set of candidate spellings and testing
for any overlap, so adding another normalisation later is one more entry in that set.
In strict mode only the exact spelling (case-folded) is accepted.

A `WordPair` carries a list of acceptable answers, so "Auto" and "Wagen" both pass for
"car". The first entry is what gets shown on a miss.

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
  dataset, so extraction and schema are testable without the 287 MB download.
