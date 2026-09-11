"""Reading upstream Wiktionary data.

The input is the **English** Wiktionary edition, as extracted by wiktextract and
published by kaikki.org. English lemmas are the headwords and German words are the
translations, which is the direction Lexicycle asks questions in.

The German edition was tried first and abandoned: wiktextract shatters multi-word
English translations there into separate word-level entries ("piece of furniture"
becomes "item", "piece", "of", "furniture"), and the fragments are indistinguishable
from genuine one-word translations. See docs/DATA-SOURCES.md.

The full extract is ~3.2 GB of JSONL. `download` streams it and writes a distilled file
holding only entries that have German translations, and only the fields the pipeline
uses — a couple of percent of the size. Extraction rules can then be re-tuned offline
without fetching 3.2 GB again.
"""

from __future__ import annotations

import gzip
import json
from pathlib import Path
from typing import Any, Iterator

from .paths import RAW_DIR

DATASET_URL = "https://kaikki.org/dictionary/English/kaikki.org-dictionary-English.jsonl"

#: Distilled output of the download step. Gzipped JSONL, a few tens of MB.
DISTILLED_NAME = "en-wiktionary-de-translations.jsonl.gz"

#: Fields kept when distilling. Everything else (etymology, sounds, forms, derived,
#: descendants, categories, wikipedia links) is dropped on the way past.
KEPT_FIELDS = ("word", "pos", "lang_code")

TARGET_LANGUAGE = "de"

USER_AGENT = "lexicycle-data/0.1 (+https://github.com/yoyokits/Lexicycle)"


def distilled_path(root: Path | None = None) -> Path:
    return (root or RAW_DIR) / DISTILLED_NAME


def use_os_certificate_store() -> bool:
    """Verify TLS against the OS certificate store instead of certifi's bundle.

    Locally-installed TLS-inspecting software (antivirus, corporate proxies) issues its
    own root CA and puts it in the OS store, where certifi cannot see it. Some of those
    generated roots are also not strictly RFC 5280 compliant — Avast's, for one, leaves
    Basic Constraints unmarked — which OpenSSL rejects outright. Delegating to the OS
    store trusts exactly what the machine already trusts, without weakening verification.

    Returns True when the delegation was installed.
    """
    try:
        import truststore
    except ImportError:
        return False

    truststore.inject_into_ssl()
    return True


def german_translations(row: dict[str, Any]) -> list[dict[str, Any]]:
    """The German entries of a row's translation list."""
    translations = row.get("translations") or []

    return [
        translation
        for translation in translations
        if isinstance(translation, dict)
        and TARGET_LANGUAGE in (translation.get("code"), translation.get("lang_code"))
    ]


def distill_row(row: dict[str, Any]) -> dict[str, Any] | None:
    """Reduce one upstream row to the fields the pipeline needs, or drop it."""
    if row.get("lang_code") != "en":
        return None

    translations = german_translations(row)
    if not translations:
        return None

    distilled: dict[str, Any] = {field: row.get(field) for field in KEPT_FIELDS}
    distilled["translations"] = [
        {
            key: translation.get(key)
            for key in ("word", "sense", "tags", "english", "roman")
            if translation.get(key)
        }
        for translation in translations
    ]

    return distilled


class TruncatedDownload(RuntimeError):
    """The server closed the connection before the whole extract arrived."""


def _content_length(url: str) -> int | None:
    import urllib.request

    request = urllib.request.Request(
        url, method="HEAD", headers={"User-Agent": USER_AGENT}
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            length = response.headers.get("Content-Length")
            return int(length) if length else None
    except Exception:
        return None


def download(
    url: str = DATASET_URL,
    destination: Path | None = None,
    max_attempts: int = 6,
) -> Path:
    """Stream the extract and write the distilled file. ~3.2 GB transferred, once.

    Nothing large is kept: rows are filtered as they arrive and only the distilled
    result reaches disk.

    kaikki.org drops long connections partway through, and a truncated stream otherwise
    looks exactly like a successful one — the JSON stays valid, there is just less of it.
    So the byte count is checked against Content-Length and the transfer resumes with a
    Range request until the whole file has been seen.
    """
    import urllib.error
    import urllib.request

    use_os_certificate_store()

    destination = destination or distilled_path()
    destination.parent.mkdir(parents=True, exist_ok=True)

    total = _content_length(url)
    if total:
        print(f"  extract is {total / 1e9:.2f} GB", flush=True)

    offset = read = kept = 0
    remainder = b""

    with gzip.open(destination, "wt", encoding="utf-8") as out:
        for attempt in range(1, max_attempts + 1):
            headers = {"User-Agent": USER_AGENT}
            if offset:
                headers["Range"] = f"bytes={offset}-"
                print(f"  resuming at {offset / 1e9:.2f} GB (attempt {attempt})", flush=True)

            request = urllib.request.Request(url, headers=headers)

            try:
                with urllib.request.urlopen(request, timeout=120) as response:
                    if offset and response.status != 206:
                        raise TruncatedDownload(
                            "Server ignored the Range request, so the download cannot be "
                            "resumed. Delete the output and start again."
                        )

                    while chunk := response.read(1 << 20):
                        offset += len(chunk)
                        remainder += chunk

                        *lines, remainder = remainder.split(b"\n")
                        for line in lines:
                            read += 1
                            row = _parse(line)
                            if row is None:
                                continue

                            distilled = distill_row(row)
                            if distilled is None:
                                continue

                            out.write(json.dumps(distilled, ensure_ascii=False) + "\n")
                            kept += 1

                        if read and read % 500_000 < 2_000 and total:
                            print(
                                f"  ... {offset / 1e9:.2f}/{total / 1e9:.2f} GB, "
                                f"{read:,} rows, {kept:,} kept",
                                flush=True,
                            )
            except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
                if attempt == max_attempts:
                    raise TruncatedDownload(
                        f"Gave up after {max_attempts} attempts at {offset:,} bytes: {error}"
                    ) from error
                print(f"  connection dropped at {offset:,} bytes: {error}", flush=True)
                continue

            if total is None or offset >= total:
                break

            if attempt == max_attempts:
                raise TruncatedDownload(
                    f"Only {offset:,} of {total:,} bytes arrived after {max_attempts} "
                    "attempts. The distilled file would be incomplete."
                )

        # Whatever is left with no trailing newline is the final record.
        if remainder.strip():
            read += 1
            row = _parse(remainder)
            if row is not None and (distilled := distill_row(row)) is not None:
                out.write(json.dumps(distilled, ensure_ascii=False) + "\n")
                kept += 1

    if total and offset < total:
        destination.unlink(missing_ok=True)
        raise TruncatedDownload(f"Only {offset:,} of {total:,} bytes arrived.")

    print(f"  {read:,} rows read, {kept:,} kept with German translations")
    return destination


def _parse(line: bytes) -> dict[str, Any] | None:
    if not line.strip():
        return None
    try:
        return json.loads(line)
    except json.JSONDecodeError:
        return None


def iter_rows(path: Path | None = None) -> Iterator[dict[str, Any]]:
    """Stream the distilled rows."""
    path = path or distilled_path()

    with gzip.open(path, "rt", encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                yield json.loads(line)


def is_downloaded(path: Path | None = None) -> bool:
    return (path or distilled_path()).exists()
