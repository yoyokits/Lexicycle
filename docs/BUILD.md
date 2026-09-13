# Building, testing and deploying

## Prerequisites

.NET 10 SDK with the `maui` and `android` workloads. Verified working set on the
developer machine:

| Tool | Version / path |
| --- | --- |
| .NET SDK | 10.0.300 |
| Android workload | 36.1.43 (platforms 35 and 36 installed) |
| JDK | 21.0.8 — `C:\Program Files\Android\openjdk\jdk-21.0.8` |
| Android SDK | `C:\Program Files (x86)\Android\android-sdk` |
| Emulator AVD | `pixel_6a_-_api_36_0` |
| Python | 3.13 (miniconda) |

Neither the JDK nor the Android SDK is on `PATH`. Visual Studio and `dotnet build`
usually discover both; if not, copy `Directory.Build.props.user.sample` to
`Directory.Build.props.user` (gitignored) and set the paths there, so no machine-specific
path is ever committed.

## .NET

```bash
dotnet build Lexicycle.slnx
dotnet test                                   # LexicycleCore.Tests

dotnet build src/Lexicycle/LexicycleApp/LexicycleApp.csproj -f net10.0-android -t:Install
dotnet build src/Lexicycle/LexicycleApp/LexicycleApp.csproj -f net10.0-android -t:Run
```

Start the emulator with:

```bash
"C:/Program Files (x86)/Android/android-sdk/emulator/emulator.exe" -avd pixel_6a_-_api_36_0
```

## Data pipeline

```bash
cd src/python
python -m venv .venv && ./.venv/Scripts/python.exe -m pip install -e ".[dev]"
./.venv/Scripts/python.exe -m pytest                       # fixture-based, no download

./.venv/Scripts/python.exe -m lexicycle_data download      # streams ~3.2 GB, one time
./.venv/Scripts/python.exe -m lexicycle_data report         # row counts + size per top-N
./.venv/Scripts/python.exe -m lexicycle_data build --top-n 0                # en-de
./.venv/Scripts/python.exe -m lexicycle_data build --pair en-es --top-n 0   # a second pair
./.venv/Scripts/python.exe -m lexicycle_data export-json --limit 50   # inspection only
```

`download` captures every language in `sources.TARGET_LANGUAGES` (German and Spanish, by
default) in one pass, so building a second pair from an already-downloaded extract needs
no re-download — only `build --pair en-<code>`. Re-run `download` only after adding a new
code to `TARGET_LANGUAGES` itself.

`export-json` no longer feeds the app — nothing ships as JSON since the starter sets were
deleted. It stays because dumping fifty pairs and reading them is the fastest way to judge
whether an extraction change improved or wrecked the data, which row counts cannot tell
you.

The tests run against a small in-code fixture shaped like the real dataset, so the whole
transform is verifiable without the download. If `download` fails with
`CERTIFICATE_VERIFY_FAILED`, see the `truststore` note in `src/python/README.md`.

After regenerating a dictionary, copy it into the app and bump `DictionaryVersion` in
`AppDatabases.cs` so installed copies refresh:

```bash
cp data/dist/lexicycle-dict-en-de.db src/Lexicycle/LexicycleApp/Resources/Raw/
```

Adding a **second** pair for the first time is the same copy step, plus one line: add the
pair to `LanguagePair.All` in `LexicycleCore/Dictionary/LanguagePair.cs`. Nothing else
needs to change — `AppDatabases` picks up whichever `.db` files it finds bundled, and the
home screen's language switcher appears on its own once more than one pair is present.
See "Adding a language pair" in `docs/DATA-SOURCES.md`.

Check the bands still make sense afterwards. They are positional windows, so they resize
themselves, but a much smaller dictionary could leave one empty — the home screen hides
any band with no words rather than showing a dead row.

## App icon and splash

`Resources/AppIcon/appicon.svg` (a plain white background), `appiconfg.svg` (the mark)
and `Resources/Splash/splash.svg` are all **generated**, and all checked in — an
ordinary build never regenerates them. Redraw them only when the mark itself changes:

```bash
pip install fonttools          # once; not part of src/python's dependencies
python tools/make_icon.py
```

That rewrites all three SVGs and, if Inkscape is on `PATH`, the 512×512 Play Store
listing icon at `docs/images/play-store-icon-512.png`. The two letters are baked in as
outlines rather than `<text>`, because neither the build-time rasteriser nor whatever
opens the SVG later can be assumed to have a CJK font.

The icon's margin is not decoration. Android masks an adaptive icon down to a circle 66%
of the layer and silently discards everything outside it, so `make_icon.py` computes the
furthest point of the artwork and refuses to write anything past that radius. Keep the
padding in the SVG rather than reaching for `ForegroundScale`, so the store tile, the
adaptive icon and the pre-API-26 legacy icon all agree.

The splash is the same artwork with that margin cropped away — nothing masks a splash,
so there the padding would only make the mark smaller. It is cropped about the *centre*,
not about the artwork's bounding box, which is what keeps it optically centred.

## Verification traps

Every one of these produced a convincing false result. They cost real debugging time, so
check them before concluding the app is broken.

**A build reporting "Build succeeded" does not always repackage the APK.** After changing
XAML, check the APK timestamp under `bin/Debug/net10.0-android/`. When a change appears
not to take effect, `adb uninstall com.yoyokits.lexicycle` then build with `-t:Install`.

**`adb exec-out screencap` can return a stale or blank frame.** A blank screenshot is not
evidence of a blank screen. Dump the view tree instead — it gives text *and* bounds, and
bounds of `[0,0][0,0]` mean the view really was laid out to nothing:

```bash
adb shell "uiautomator dump /sdcard/ui.xml >/dev/null; cat /sdcard/ui.xml"
```

**`adb shell input text` is ASCII-only.** Typing `Rücken` silently does nothing, so a
scripted session stalls on the first umlaut and looks exactly like a stuck app. Send the
ASCII fallback (`Ruecken`) — lenient matching accepts it.

**`adb shell cat` corrupts binary files** through CRLF translation, so a pulled database
reads as "disk image is malformed". Use `exec-out`:

```bash
adb exec-out "run-as com.yoyokits.lexicycle cat /data/data/com.yoyokits.lexicycle/files/progress.db" > progress.db
```

**`adb shell pm clear` breaks a Debug install.** It deletes
`files/.__override__/<abi>/`, where Fast Deployment keeps the managed assemblies, and the
app then aborts on launch with *"No assemblies found … Assuming this is part of Fast
Deployment. Exiting…"*. The message points at packaging and reads like a build problem;
it is not. A plain `-t:Install` will not repair it either, because the APK is unchanged
and the assembly push is skipped — `adb uninstall` first, then build with `-t:Install`.

To start a run from zero progress, delete just the one file instead:

```bash
adb shell "run-as com.yoyokits.lexicycle rm -f files/progress.db"
```

**Tapping during app startup can trigger an ANR** on the emulator that does not reproduce
once the app is up. Wait for the home screen before driving the UI.

**Scripted taps need real coordinates.** Read them from the `uiautomator` dump rather than
guessing; the home screen layout shifts as sets are added.
