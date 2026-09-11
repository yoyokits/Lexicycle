# lexicycle-data

Offline tooling that turns Wiktionary into the small English–German dictionary Lexicycle
bundles. **Nothing here ships inside the MAUI app** — it runs on a development machine
and its output is a database file.

The source is the **English** Wiktionary edition via kaikki.org: English lemmas as
headwords, German words as translations, gender on each translation. See
`../../docs/DATA-SOURCES.md` for the extraction rules and for why the German edition was
tried and rejected.

## Setup

```bash
cd src/python
python -m venv .venv
./.venv/Scripts/python.exe -m pip install -e ".[dev]"     # Windows
# source .venv/bin/activate && pip install -e ".[dev]"    # macOS / Linux
```

### If `download` fails with `CERTIFICATE_VERIFY_FAILED`

Something on the machine is inspecting HTTPS and signing it with its own root CA that
Python's bundled `certifi` list does not carry. On this developer's machine it is **Avast
Web/Mail Shield**; corporate proxies do the same. Avast's generated root also leaves
Basic Constraints unmarked, which OpenSSL rejects outright, so the error can read
`Basic Constraints of CA cert not marked critical` rather than the more familiar
`unable to get local issuer certificate`.

`truststore` is a declared dependency and `sources.download()` activates it, so this
should already be handled — it delegates verification to the OS certificate store, which
already trusts that root. If you hit it anyway:

```bash
./.venv/Scripts/python.exe -m pip install truststore
```

Do **not** disable certificate verification instead.

## Steps

Each step runs standalone; run them in order the first time.

```bash
python -m lexicycle_data download                 # streams ~3.2 GB, distils to ../../data/raw
python -m lexicycle_data report                   # row counts + file size per top-N
python -m lexicycle_data build --top-n 5000       # ../../data/dist/lexicycle-dict-en-de.db
python -m lexicycle_data export-json --limit 50   # a vocabulary set for the app
```

- `build --top-n 0` keeps every pair instead of applying a cut-off.
- `export-json` writes into the app's `Resources/Raw/sets` by default; pass `--output`
  to put it elsewhere.

## Tests

```bash
./.venv/Scripts/python.exe -m pytest
```

The suite runs against an in-code fixture shaped exactly like the upstream rows, so the
whole transform is testable **without** the 3.2 GB download. If you change the extraction rules,
change the fixture in `tests/conftest.py` alongside them.

## Layout

| Module | Responsibility |
| --- | --- |
| `paths.py` | Repository-relative locations |
| `sources.py` | Streaming the extract, distilling it, and reading it back |
| `model.py` | What a usable word pair is, and what gets thrown away |
| `frequency.py` | `wordfreq`-based English ranking |
| `database.py` | Building the three-table SQLite |
| `export.py` | Emitting app-format vocabulary set JSON |
| `__main__.py` | The CLI |

`model.py` and `database.py` take plain iterables, not file paths, which is what keeps
them testable without any download.
