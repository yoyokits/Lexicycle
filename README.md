# Lexicycle

A .NET MAUI vocabulary trainer — practice word pairs in any language, with missed words
looping back until you get them right.

Show a word, type the translation. Get it right and it's done; get it wrong and it comes
back in the next round. The session ends only when a full round passes with no mistakes,
so every word has been answered correctly at least once.

## Status

Android, offline, no backend.

- **Working now:** the trainer, a bundled English–German dictionary generated from
  Wiktionary — 3,545 prompts against 5,154 German words — and sessions that remember what
  you have seen. **No word is ever asked twice.** Each session is at least 80% material
  you have never met, most common first, with a small slice of revision weighted towards
  what you get wrong.
- **Pick your level:** Basics (the 1,000 most common words), Common words (the next
  1,000), or Wider vocabulary — or Practice straight through the whole dictionary.
- **More than one language:** the dictionary, progress tracking, and every screen are
  built around language pairs rather than English-German specifically, so a bundled
  English-Spanish dictionary appears as a second option on the home screen with no other
  change. English-Spanish itself has not been generated yet — see `docs/DATA-SOURCES.md`.
- **Next:** OCR — photograph a book page and practise the words on it.

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for the plan and
[`REQUIREMENTS.md`](REQUIREMENTS.md) for what's outstanding.

## Building

```bash
dotnet build Lexicycle.slnx
dotnet test
dotnet build src/Lexicycle/LexicycleApp/LexicycleApp.csproj -f net10.0-android -t:Run
```

Requires the .NET 10 SDK with the `maui` and `android` workloads. Full setup, emulator
instructions and the data pipeline are in [`docs/BUILD.md`](docs/BUILD.md).

## Layout

| Path | What it is |
| --- | --- |
| `src/Lexicycle/LexicycleApp` | The MAUI Android app — views and view models |
| `src/Lexicycle/LexicycleCore` | Models, session engine, answer checking. No MAUI dependency |
| `src/Lexicycle/LexicycleCore.Tests` | xUnit tests for the above |
| `src/python` | Offline data tooling. Not shipped in the app — see its [README](src/python/README.md) |
| `docs/` | Roadmap, architecture, data sources |

## Data and licensing

Dictionary data derives from the [English Wiktionary](https://en.wiktionary.org) via the
[kaikki.org](https://kaikki.org/dictionary/English/) machine-readable export, licensed
**CC-BY-SA 4.0**. Any generated dictionary carries the same licence. Details in
[`docs/DATA-SOURCES.md`](docs/DATA-SOURCES.md).

The German Wiktionary edition was tried first and rejected: `wiktextract` shatters
multi-word English translations there into separate word-level entries, so "piece of
furniture" becomes `item`, `piece`, `of`, `furniture`. That is a correctness decision, not
a convenience one — the reasoning is in `docs/DATA-SOURCES.md` and should be read before
changing the source.

The application code is licensed under the [MIT License](LICENSE).
