"""Reading upstream Wiktionary data.

The input is ``cstr/de-wiktionary-extracted`` — the same wiktextract content as the
4.45 GB ``de-wiktionary-sqlite-full`` dataset, but as 5 parquet files totalling ~287 MB.
Parquet is columnar, so reading only the five columns the pipeline needs moves far less
than that off disk.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any, Iterator

from .model import REQUIRED_COLUMNS
from .paths import RAW_DIR

DATASET_ID = "cstr/de-wiktionary-extracted"

#: Rows per batch when streaming parquet. Large enough to be quick, small enough that
#: memory stays flat over a million rows.
BATCH_SIZE = 20_000


def download(repo_id: str = DATASET_ID, destination: Path | None = None) -> Path:
    """Fetch the parquet files into ``data/raw``. Roughly a 287 MB download, once."""
    from huggingface_hub import snapshot_download

    destination = destination or RAW_DIR / repo_id.replace("/", "__")
    destination.mkdir(parents=True, exist_ok=True)

    path = snapshot_download(
        repo_id=repo_id,
        repo_type="dataset",
        local_dir=destination,
        allow_patterns=["*.parquet"],
    )
    return Path(path)


def find_parquet_files(root: Path | None = None) -> list[Path]:
    """Locate downloaded parquet files, newest download layout or flat directory."""
    root = root or RAW_DIR
    if not root.exists():
        return []

    return sorted(root.rglob("*.parquet"))


def iter_rows(files: list[Path]) -> Iterator[dict[str, Any]]:
    """Stream rows as plain dicts, reading only the columns the pipeline uses."""
    import pyarrow.parquet as pq

    for file in files:
        parquet = pq.ParquetFile(file)
        available = set(parquet.schema_arrow.names)
        columns = [column for column in REQUIRED_COLUMNS if column in available]

        for batch in parquet.iter_batches(batch_size=BATCH_SIZE, columns=columns):
            yield from batch.to_pylist()
