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


def _require_download() -> None:
    if not sources.is_downloaded():
        sys.exit(
            f"{sources.distilled_path()} not found.\n"
            "Run `python -m lexicycle_data download` first (streams ~3.2 GB, one time)."
        )


def _target_language(pair: str) -> str:
    """The target-language code of a ``en-<code>`` pair, or exit with a clear message."""
    parts = pair.split("-")
    if len(parts) != 2 or parts[0] != "en":
        sys.exit(f"Unsupported pair {pair!r}; expected 'en-<code>', e.g. en-de or en-es.")
    return parts[1]


def command_download(_args: argparse.Namespace) -> None:
    paths.ensure_dirs()
    print("Streaming the English Wiktionary extract (~3.2 GB) and distilling it ...")

    try:
        destination = sources.download()
    except Exception as error:
        if "CERTIFICATE_VERIFY_FAILED" in str(error):
            sys.exit(
                "TLS verification failed.\n\n"
                "Something on this machine is inspecting HTTPS traffic (antivirus or a\n"
                "corporate proxy) and signing it with a root certificate that Python's\n"
                "bundled CA list does not carry. Install `truststore` so verification\n"
                "uses the OS certificate store instead:\n\n"
                "    pip install truststore\n"
            )
        raise

    size = destination.stat().st_size
    print(f"Wrote {destination}  ({_human_size(size)})")


def command_build(args: argparse.Namespace) -> None:
    _require_download()
    target = _target_language(args.pair)
    db_path = paths.dictionary_db(args.pair)

    print(f"Reading the distilled extract for {args.pair} ...")
    entries = rows_to_entries(sources.iter_rows(), target)

    stats = build_database(
        entries,
        db_path,
        target_language=target,
        top_n=args.top_n,
        rank_lookup=build_rank_lookup("en"),
        target_rank_lookup=build_rank_lookup(target),
    )

    print(f"Considered {stats.considered:,} upstream rows")
    print(f"  English words  : {stats.english:,}")
    print(f"  {target} words{' ' * max(0, 5 - len(target))}: {stats.target:,}")
    print(f"  Pairs          : {stats.pairs:,}")
    print(f"  Database       : {db_path}  ({_human_size(db_path.stat().st_size)})")


def command_report(args: argparse.Namespace) -> None:
    """Build at several cut-offs and print the resulting sizes side by side."""
    _require_download()
    target = _target_language(args.pair)
    lookup = build_rank_lookup("en")
    target_lookup = build_rank_lookup(target)

    print(f"Reading the distilled extract once and caching {args.pair} entries in memory ...")
    entries = list(rows_to_entries(sources.iter_rows(), target))
    print(f"{len(entries):,} English lemmas have at least one {target} translation.\n")

    scratch = paths.DIST_DIR / "_report.db"
    header = (
        f"{'top-N':>8}  {'EN words':>10}  {target.upper() + ' words':>10}  "
        f"{'pairs':>10}  {'size':>10}"
    )
    print(header)
    print("-" * len(header))

    try:
        for size in (*REPORT_SIZES, None):
            stats = build_database(
                entries, scratch, target_language=target, top_n=size,
                rank_lookup=lookup, target_rank_lookup=target_lookup,
            )
            label = f"{size:,}" if size else "all"
            print(
                f"{label:>8}  {stats.english:>10,}  {stats.target:>10,}  "
                f"{stats.pairs:>10,}  {_human_size(scratch.stat().st_size):>10}"
            )
    finally:
        scratch.unlink(missing_ok=True)


def command_export(args: argparse.Namespace) -> None:
    target = _target_language(args.pair)
    db_path = paths.dictionary_db(args.pair)
    if not db_path.exists():
        sys.exit(f"{db_path} not found. Run `python -m lexicycle_data build --pair {args.pair}` first.")

    resolved_set_id = args.set_id or f"en-{target}-top{args.limit}"
    output = Path(args.output) if args.output else paths.APP_SETS_DIR / f"{resolved_set_id}.json"
    count = export_set(
        db_path,
        output,
        target_language=target,
        limit=args.limit,
        offset=args.offset,
        set_id=resolved_set_id,
        name=args.name,
    )
    print(f"Wrote {count} word pairs to {output}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="lexicycle-data",
        description="Turn Wiktionary dumps into Lexicycle's bundled dictionary.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    download = subparsers.add_parser(
        "download", help="stream the English Wiktionary extract and distil it (~3.2 GB)"
    )
    download.set_defaults(func=command_download)

    build = subparsers.add_parser("build", help="generate the small SQLite dictionary")
    build.add_argument(
        "--pair", default="en-de",
        help="language pair to build, e.g. en-de or en-es (default: en-de)",
    )
    build.add_argument(
        "--top-n",
        type=int,
        default=5000,
        help="keep only the N most frequent English words (default: 5000; 0 keeps all)",
    )
    build.set_defaults(func=command_build)

    report = subparsers.add_parser("report", help="print row counts and file size per top-N")
    report.add_argument("--pair", default="en-de", help="language pair to report on")
    report.set_defaults(func=command_report)

    export = subparsers.add_parser("export-json", help="write a vocabulary set for the app")
    export.add_argument("--pair", default="en-de", help="language pair to export from")
    export.add_argument("--limit", type=int, default=50, help="how many English words")
    export.add_argument("--offset", type=int, default=0)
    export.add_argument("--set-id", default=None, help="defaults to en-<code>-top<limit>")
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
