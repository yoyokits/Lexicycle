"""Repository-relative locations used by every pipeline step."""

from __future__ import annotations

from pathlib import Path

# .../Lexicycle/src/python/lexicycle_data/paths.py -> .../Lexicycle
REPO_ROOT = Path(__file__).resolve().parents[3]

DATA_DIR = REPO_ROOT / "data"

#: Downloaded upstream dumps. Gitignored — large and re-downloadable.
RAW_DIR = DATA_DIR / "raw"

#: Generated artifacts small enough to commit and bundle into the app.
DIST_DIR = DATA_DIR / "dist"

#: Where the app picks up bundled JSON vocabulary sets.
APP_SETS_DIR = REPO_ROOT / "src" / "Lexicycle" / "LexicycleApp" / "Resources" / "Raw" / "sets"


def dictionary_db(pair: str) -> Path:
    """Output path of the generated dictionary for a language pair, e.g. 'en-de'."""
    return DIST_DIR / f"lexicycle-dict-{pair}.db"


def ensure_dirs() -> None:
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    DIST_DIR.mkdir(parents=True, exist_ok=True)
