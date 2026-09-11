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
   Dictionary/    IDictionaryStore, FrequencyBand, the two session factories
   Progress/      IProgressStore, SessionComposer, ReviewSchedule, Milestones
   Services/      IVocabularySetRepository, IAssetProvider, bundled-JSON implementation
```

Nothing ships as JSON any more, but the repository seam stays: OCR (R-404) and imported
sets (R-505) both produce word lists from outside the dictionary.

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

Two factories build sessions, and **both** consult progress:

| Factory | Source | Session size |
| --- | --- | --- |
| `PracticeSessionFactory` | the generated dictionary, whole or one band | `DefaultSize` (10) |
| `FixedSetSessionFactory` | an OCR'd page (R-404) | `SizeFor(count)` |

### Bands, not starter sets

The home screen offers **Practice** over the whole dictionary, plus `FrequencyBand.All`:
Basics (the 1,000 most common), Common words (the next 1,000), Wider vocabulary (the
rest).

These replaced three hand-written JSON sets of **twelve words each**. Those were written
in Phase 1, before the dictionary existed, and were never revisited once it did — a
learner exhausted one in two sittings, which is what made the repetition complaints so
acute. They are deleted; nothing ships as JSON now.

A band is a **positional window** over the frequency ordering, not a range of `freq_rank`
values — that column holds a scaled Zipf score, not an ordinal, and slicing on its value
put every word in the last band. See `docs/DATA-SOURCES.md`.

Bands share the single `dictionary` progress scope, so they are views over one body of
vocabulary rather than separate courses: a word learned under Basics is never offered as
*new* under Practice.

`FixedSetSessionFactory` remains for externally supplied word lists. Its `SizeFor` takes
**half the set**, capped at `DefaultSize`, so a set always yields at least two disjoint
sessions before it is exhausted.

### Scoping

Word ids are unique only within a pool: the dictionary numbers words from `words_en`, a
fixed set numbers its own words by position. `ProgressScope` keeps them apart —
`"dictionary"` or `"set:<id>"` — and every progress query is scoped. All three bands share
the `dictionary` scope, because they are slices of one pool.

**Session numbering is per-scope too.** A global counter would let dictionary practice
advance a fixed set's rotation, so its no-repeat rule would be satisfied by sessions the
learner never played there.

The milestone bar counts the dictionary scope only, so finishing an OCR'd page raises no
milestone — it is not progress through the dictionary.

**Word order depends on whether frequency is known.**

- A *dictionary* session is presented **most common first**, which is the order worth
  learning in. Its membership changes every session, so a deterministic order never feels
  repetitive.
- A *fixed* set has no frequency data, so `SessionViewModel` calls
  `VocabularySet.Shuffled()` on it.

Ordering is a property of a session, not of a set, so it is applied where the session
starts rather than inside `SessionEngine`, whose job is round mechanics. `Shuffled`
returns a new set, so the repository's cached instances are never mutated.

### Running out

Because nothing is ever replayed, every pool eventually empties. The dictionary is ~350
sessions away from that; a small fixed set reaches it in a couple of visits. When a fixed
set empties, `SessionViewModel` says so and offers **Start this set again**, which calls
`IProgressStore.ResetScopeAsync` for that scope alone. An emptied band points the learner
at the next band instead.

### How a session is chosen

`SessionComposer` applies four rules, in priority order:

1. **At least 80% new** (`MinNewShare`), measured on the session *delivered*, not the size
   requested. A ten-word session is eight words never asked before, taken
   most-common-first. If there is not enough new material the session **shrinks**; with
   none at all it is **empty**, and the caller reports the pool finished.
2. **The remainder is revision, weighted by failures.** A word's chance of being drawn is
   proportional to how often it has been answered *wrong*, plus one.
3. **Sampled, not sorted.** Revision is drawn by weighted random sampling rather than by
   taking the worst few. Taking the worst outright would serve the same handful of hard
   words every session until they were finally learned — which is exactly the "it keeps
   asking me the same things" complaint. Weighting makes them likely, not certain.
4. **Never twice running.** Nothing asked in the immediately preceding session is offered,
   whatever its failure count. This one is absolute and overrides the weighting.

The `+1` weight floor matters: a word that has never been missed still has a small chance
of returning, so revision does not degenerate into drilling only the failures.

Revision is therefore capped by how much new material exists, not by the requested size:
`review ≤ fresh / MinNewShare − fresh`, which is a quarter of the new words at 80%. Zero
new words allow zero revision.

That cap is the whole answer to "why does it keep asking me the same words". An earlier
version padded a session back up to full size out of the already-answered pool whenever
new material ran short, which turned a twelve-word set into an endless loop of the same
twelve questions. Nothing is replayed now: a pool with nothing unasked is *finished*.

> The floating-point tolerance in `ReviewBudget` is load-bearing. `8 / 0.8` is
> `10.000000000000002` and `4 / 0.8` is `4.999999999999999`, so a bare `floor` would allow
> two review words in one case and none in the other under the same rule.

Consequences worth knowing:

- **Small sessions never revise.** `ceil(size × 0.8)` leaves no room below size 5, so a
  four-word session is entirely new. `PracticeSessionFactory.DefaultSize` is 10, which
  gives 8 new + 2 review.
- **A finished pool yields an empty session.** For the dictionary that is ~350 sessions
  away. For a bundled set it arrives quickly by design — twelve words, six per visit, done
  in two — and `SessionViewModel` then offers **Start this set again**, which calls
  `ResetScopeAsync` on that set alone.

### Mastery

`ReviewSchedule` tracks how well a word is known as a Leitner box: a correct answer
promotes one box, a miss drops it to 0, and past the last box it counts as mastered. This
is a *statistic* — it records progress but does not decide what a session asks. An earlier
version did drive selection through fixed per-box intervals; that was removed rather than
left in place, because two scheduling models with only one of them live is a trap for the
next reader.

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
