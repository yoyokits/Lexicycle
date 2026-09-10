# lexicycle-data

Offline tooling that turns Wiktionary dumps into the small English–German dictionary
Lexicycle bundles. **Nothing here ships inside the MAUI app** — it runs on a development
machine and its output is a database file.

See `../../docs/DATA-SOURCES.md` for why the parquet dataset is used rather than the
4.45 GB SQLite one, and for the extraction rules.

## Setup

```bash
cd src/python
python -m venv .venv
./.venv/Scripts/python.exe -m pip install -e ".[dev]"     # Windows
# source .venv/bin/activate && pip install -e ".[dev]"    # macOS / Linux
```

## Steps

Each step runs standalone; run them in order the first time.

```bash
python -m lexicycle_data download                 # ~287 MB into ../../data/raw, once
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
whole transform is testable **without** the download. If you change the extraction rules,
change the fixture in `tests/conftest.py` alongside them.

## Layout

| Module | Responsibility |
| --- | --- |
| `paths.py` | Repository-relative locations |
| `sources.py` | Downloading and streaming the parquet rows |
| `model.py` | What a usable word pair is, and what gets thrown away |
| `frequency.py` | `wordfreq`-based English ranking |
| `database.py` | Building the three-table SQLite |
| `export.py` | Emitting app-format vocabulary set JSON |
| `__main__.py` | The CLI |

`model.py` and `database.py` take plain iterables, not file paths, which is what keeps
them testable without any download.
