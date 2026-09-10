"""Command line entry point: ``python -m lexicycle_data <step>``."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import paths, sources
from .database import build_database
from .export import export_set
from .frequency import build_rank_lookup
from .model import rows_to_entries

#: Sizes the report step measures, so the bundle size is chosen from real numbers.
REPORT_SIZES = (1_000, 5_000, 10_000, 50_000)


def _human_size(byte_count: int) -> str:
    size = float(byte_count)
    for unit in ("B", "KB", "MB", "GB"):
        if size < 1024 or unit == "GB":
            return f"{size:,.1f} {unit}"
        size /= 1024
    return f"{size:,.1f} GB"


def _require_parquet() -> list[Path]:
    files = sources.find_parquet_files()
    if not files:
        sys.exit(
            "No parquet files under data/raw.\n"
            "Run `python -m lexicycle_data download` first (~287 MB, one time)."
        )
    return files


def command_download(_args: argparse.Namespace) -> None:
    paths.ensure_dirs()
    print(f"Downloading {sources.DATASET_ID} (~287 MB) ...")
    destination = sources.download()
    files = sources.find_parquet_files()
    print(f"Downloaded {len(files)} parquet file(s) to {destination}")


def command_build(args: argparse.Namespace) -> None:
    files = _require_parquet()
    db_path = paths.dictionary_db(args.pair)

    print(f"Reading {len(files)} parquet file(s) ...")
    entries = rows_to_entries(sources.iter_rows(files))
    lookup = build_rank_lookup("en")

    stats = build_database(entries, db_path, top_n=args.top_n, rank_lookup=lookup)

    print(f"Considered {stats.considered:,} upstream rows")
    print(f"  English words : {stats.english:,}")
    print(f"  German words  : {stats.german:,}")
    print(f"  Pairs         : {stats.pairs:,}")
    print(f"  Database      : {db_path}  ({_human_size(db_path.stat().st_size)})")


def command_report(args: argparse.Namespace) -> None:
    """Build at several cut-offs and print the resulting sizes side by side."""
    files = _require_parquet()
    lookup = build_rank_lookup("en")

    print("Reading parquet once and caching entries in memory ...")
    entries = list(rows_to_entries(sources.iter_rows(files)))
    print(f"{len(entries):,} German lemmas have at least one English translation.\n")

    scratch = paths.DIST_DIR / "_report.db"
    header = f"{'top-N':>8}  {'EN words':>10}  {'DE words':>10}  {'pairs':>10}  {'size':>10}"
    print(header)
    print("-" * len(header))

    try:
        for size in (*REPORT_SIZES, None):
            stats = build_database(entries, scratch, top_n=size, rank_lookup=lookup)
            label = f"{size:,}" if size else "all"
            print(
                f"{label:>8}  {stats.english:>10,}  {stats.german:>10,}  "
                f"{stats.pairs:>10,}  {_human_size(scratch.stat().st_size):>10}"
            )
    finally:
        scratch.unlink(missing_ok=True)


def command_export(args: argparse.Namespace) -> None:
    db_path = paths.dictionary_db(args.pair)
    if not db_path.exists():
        sys.exit(f"{db_path} not found. Run `python -m lexicycle_data build` first.")

    output = Path(args.output) if args.output else paths.APP_SETS_DIR / f"{args.set_id}.json"
    count = export_set(
        db_path,
        output,
        limit=args.limit,
        offset=args.offset,
        set_id=args.set_id,
        name=args.name,
    )
    print(f"Wrote {count} word pairs to {output}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="lexicycle-data",
        description="Turn Wiktionary dumps into Lexicycle's bundled dictionary.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    download = subparsers.add_parser("download", help="fetch the upstream parquet files (~287 MB)")
    download.set_defaults(func=command_download)

    build = subparsers.add_parser("build", help="generate the small SQLite dictionary")
    build.add_argument("--pair", default="en-de", help="language pair (default: en-de)")
    build.add_argument(
        "--top-n",
        type=int,
        default=5000,
        help="keep only the N most frequent English words (default: 5000; 0 keeps all)",
    )
    build.set_defaults(func=command_build)

    report = subparsers.add_parser("report", help="print row counts and file size per top-N")
    report.add_argument("--pair", default="en-de")
    report.set_defaults(func=command_report)

    export = subparsers.add_parser("export-json", help="write a vocabulary set for the app")
    export.add_argument("--pair", default="en-de")
    export.add_argument("--limit", type=int, default=50, help="how many English words")
    export.add_argument("--offset", type=int, default=0)
    export.add_argument("--set-id", default="en-de-top50")
    export.add_argument("--name", default=None)
    export.add_argument("--output", default=None, help="defaults to the app's Resources/Raw/sets")
    export.set_defaults(func=command_export)

    args = parser.parse_args(argv)

    if getattr(args, "top_n", None) == 0:
        args.top_n = None

    args.func(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
