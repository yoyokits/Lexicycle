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
./.venv/Scripts/python.exe -m lexicycle_data report        # row counts + size per top-N
./.venv/Scripts/python.exe -m lexicycle_data build --top-n 0
./.venv/Scripts/python.exe -m lexicycle_data export-json --limit 50
```

The tests run against a small in-code fixture shaped like the real dataset, so the whole
transform is verifiable without the download. If `download` fails with
`CERTIFICATE_VERIFY_FAILED`, see the `truststore` note in `src/python/README.md`.

After regenerating the dictionary, copy it into the app and bump `DictionaryVersion` in
`AppDatabases.cs` so installed copies refresh:

```bash
cp data/dist/lexicycle-dict-en-de.db src/Lexicycle/LexicycleApp/Resources/Raw/
```

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
