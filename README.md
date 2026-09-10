# Lexicycle

A .NET MAUI vocabulary trainer — practice word pairs in any language, with missed words
looping back until you get them right.

Show a word, type the translation. Get it right and it's done; get it wrong and it comes
back in the next round. The session ends only when a full round passes with no mistakes,
so every word has been answered correctly at least once.

## Status

Android, offline, no backend.

- **Working now:** the trainer, running on two bundled starter sets (English–German and
  English–Spanish), plus an offline pipeline that turns German Wiktionary into a small
  English↔German dictionary.
- **Next:** wiring that generated dictionary into the app, then OCR — photograph a book
  page and practise the words on it.

See [`docs/ROADMAP.md`](docs/ROADMAP.md) for the plan and
[`REQUIREMENTS.md`](REQUIREMENTS.md) for what's outstanding.

## Building

```bash
dotnet build Lexicycle.slnx
dotnet test
dotnet build src/Lexicycle/LexicycleApp/LexicycleApp.csproj -f net10.0-android -t:Run
```

Requires the .NET 10 SDK with the `maui` and `android` workloads. If the JDK or Android
SDK aren't found, copy `Directory.Build.props.user.sample` to
`Directory.Build.props.user` and set the paths.

## Layout

| Path | What it is |
| --- | --- |
| `src/Lexicycle/LexicycleApp` | The MAUI Android app — views and view models |
| `src/Lexicycle/LexicycleCore` | Models, session engine, answer checking. No MAUI dependency |
| `src/Lexicycle/LexicycleCore.Tests` | xUnit tests for the above |
| `src/python` | Offline data tooling. Not shipped in the app — see its [README](src/python/README.md) |
| `docs/` | Roadmap, architecture, data sources |

## Data and licensing

Dictionary data derives from [German Wiktionary](https://de.wiktionary.org) via the
[`cstr/de-wiktionary-extracted`](https://huggingface.co/datasets/cstr/de-wiktionary-extracted)
export, licensed **CC-BY-SA 4.0**. Any generated dictionary carries the same licence.
Details in [`docs/DATA-SOURCES.md`](docs/DATA-SOURCES.md).

The application code is licensed under the [MIT License](LICENSE).
