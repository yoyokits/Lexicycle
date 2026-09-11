# Lexicycle

A .NET MAUI (Android-first) vocabulary trainer. The user practises word pairs between
two languages; missed words loop back into later rounds until every word has been
answered correctly in one clean round.

## Repository layout

```
Lexicycle.slnx
REQUIREMENTS.md            open requirements (R-NNN, checkboxes)
REQUIREMENTS-DONE.md       completed requirements, moved here when verified
docs/
  ROADMAP.md               phased plan
  DATA-SOURCES.md          upstream datasets, licences, schema mapping
  ARCHITECTURE.md          layering and the session engine
src/
  Lexicycle/
    LexicycleApp/          MAUI head (net10.0-android) — Views, ViewModels, DI
    LexicycleCore/         plain net10.0 library — models, SessionEngine, services
    LexicycleCore.Tests/   xUnit
  python/                  offline data tooling; NOT shipped in the app
data/
  raw/                     downloaded Wiktionary dumps (gitignored)
  dist/                    generated dictionary artifacts
```

## Key constraints

- **v1 is one-to-one translations only.** `house` → `Haus`. Not full dictionary
  entries, not multi-sense disambiguation. See `docs/ROADMAP.md` for what comes later.
- **English↔German is the only generated pair in v1.** The pipeline is
  pair-parameterised so adding one is config, not redesign.
- **Dictionary data comes from the *English* Wiktionary edition**, not the German one.
  The German edition fragments multi-word English translations ("piece of furniture"
  becomes four entries) and cannot be used. See `docs/DATA-SOURCES.md` before changing
  the source.
- **Fully offline.** No backend, no network calls at runtime.
- **`src/python` is build-time tooling.** Nothing in it ships inside the app.
- All round/answer logic lives in `LexicycleCore` and must stay UI-free and unit
  tested. `LexicycleApp` only binds to it.

## Build and test

```bash
dotnet build Lexicycle.slnx          # everything
dotnet test                         # LexicycleCore.Tests
```

Android build and deploy:

```bash
dotnet build src/Lexicycle/LexicycleApp/LexicycleApp.csproj -f net10.0-android -t:Install
dotnet build src/Lexicycle/LexicycleApp/LexicycleApp.csproj -f net10.0-android -t:Run
```

If the SDKs are not found, copy `Directory.Build.props.user.sample` to
`Directory.Build.props.user` (gitignored) and adjust. On this machine:

| Tool | Path |
| --- | --- |
| JDK | `C:\Program Files\Android\openjdk\jdk-21.0.8` |
| Android SDK | `C:\Program Files (x86)\Android\android-sdk` |
| Emulator AVD | `pixel_6a_-_api_36_0` |

Start the emulator with:

```bash
"C:/Program Files (x86)/Android/android-sdk/emulator/emulator.exe" -avd pixel_6a_-_api_36_0
```

**A build that reports success does not always repackage the APK.** After changing
XAML, check the APK timestamp under `bin/Debug/net10.0-android/`, or use `-t:Install`
after `adb uninstall com.yoyokits.lexicycle` when a change appears not to take effect.

**`adb exec-out screencap` can return a stale or blank frame.** To check what is
actually on screen, dump the view tree instead — it reports text *and* bounds, and
zero bounds (`[0,0][0,0]`) mean the view was laid out to nothing:

```bash
adb shell "uiautomator dump /sdcard/ui.xml >/dev/null; cat /sdcard/ui.xml"
```

## Data pipeline

```bash
cd src/python
python -m venv .venv && ./.venv/Scripts/python.exe -m pip install -e ".[dev]"
./.venv/Scripts/python.exe -m pytest                       # fixture-based, no download

./.venv/Scripts/python.exe -m lexicycle_data download      # streams ~3.2 GB, one time
./.venv/Scripts/python.exe -m lexicycle_data report        # row counts + size per top-N
./.venv/Scripts/python.exe -m lexicycle_data build --top-n 5000
./.venv/Scripts/python.exe -m lexicycle_data export-json --limit 50
```

The pipeline tests run against a small in-code fixture shaped like the real dataset,
so the whole transform is testable without the download.

## Generated sessions

"Practice" on the home screen draws from the bundled dictionary and never repeats a word
until `ReviewSchedule` says it is due. Fixed sets (bundled JSON, later OCR) bypass
rotation — see `docs/ARCHITECTURE.md`.

Two SQLite files: the dictionary is a read-only `MauiAsset` copied to app data on first
run; `progress.db` is separate so a dictionary update never wipes progress.

## Platform gotchas worth remembering

- **Never navigate while the soft keyboard is open.** Android lays the incoming page
  out against the still-open IME insets and gives it zero height, so the page arrives
  blank. `SessionViewModel.HideKeyboardAsync` (supplied by `SessionPage`) closes it
  first. This cost real debugging time — keep it.
- **A `CollectionView` must sit directly in a `*` Grid row**, never nested inside a
  `VerticalStackLayout`. A stack layout offers infinite height and the measure pass
  collapses.
- Bind `IsVisible` to a real `bool` property (`HasHint`, `HasError`). A string bound
  to `IsVisible` does not convert.
- `System.Text.Json` needs the source-generated `VocabularySetJsonContext`; the
  trimmer strips reflection-based serialisation in Release Android builds.
- **`adb shell input text` is ASCII-only.** Typing "Rücken" silently does nothing, so a
  scripted session stalls forever on the first umlaut and looks like an app bug. Send the
  ASCII fallback ("Ruecken") — lenient matching accepts it.
- **`adb shell cat` corrupts binary files** with CRLF translation. Use
  `adb exec-out "run-as <pkg> cat <path>"` to pull a database off the device.

## Conventions

- MVVM via `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- Compiled bindings everywhere: every XAML page and `DataTemplate` sets `x:DataType`.
- Hints must never contain their own answer — `BundledSetsTests` enforces this.
