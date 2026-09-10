"""Builds the small three-table SQLite database the app bundles.

Schema is deliberately minimal — a word table per language plus a join table. Enrichment
(examples, IPA, more languages) arrives later as nullable columns or side tables, so
nothing here needs a breaking migration.
"""

from __future__ import annotations

import sqlite3
from pathlib import Path
from typing import Iterable

from .frequency import UNKNOWN_RANK, RankLookup, null_rank_lookup
from .model import Entry

SCHEMA = """
CREATE TABLE words_en (
    id        INTEGER PRIMARY KEY,
    text      TEXT    NOT NULL UNIQUE,
    freq_rank INTEGER
);

CREATE TABLE words_de (
    id     INTEGER PRIMARY KEY,
    text   TEXT    NOT NULL UNIQUE,
    gender TEXT,
    pos    TEXT
);

CREATE TABLE translations (
    en_id INTEGER NOT NULL REFERENCES words_en(id),
    de_id INTEGER NOT NULL REFERENCES words_de(id),
    PRIMARY KEY (en_id, de_id)
) WITHOUT ROWID;

CREATE INDEX idx_words_en_rank ON words_en(freq_rank);
CREATE INDEX idx_translations_de ON translations(de_id);

CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
"""


class BuildStats:
    """Row counts from one build, for the report step and the CLI output."""

    def __init__(self, english: int, german: int, pairs: int, considered: int) -> None:
        self.english = english
        self.german = german
        self.pairs = pairs
        self.considered = considered

    def __repr__(self) -> str:  # pragma: no cover - debugging aid
        return (
            f"BuildStats(english={self.english}, german={self.german}, "
            f"pairs={self.pairs}, considered={self.considered})"
        )


def build_database(
    entries: Iterable[Entry],
    db_path: Path,
    top_n: int | None = 5000,
    rank_lookup: RankLookup | None = None,
) -> BuildStats:
    """Write ``entries`` to a fresh SQLite file at ``db_path``.

    ``top_n`` keeps only the most frequent English words (and the German words paired
    with them); pass None to keep everything.
    """
    rank_lookup = rank_lookup or null_rank_lookup()

    # Collect first so English words can be ranked before the top-N cut is applied.
    pairs: set[tuple[str, str]] = set()
    german: dict[str, Entry] = {}
    considered = 0

    for entry in entries:
        considered += 1

        # First spelling of a lemma wins; later duplicates only differ by sense.
        german.setdefault(entry.word, entry)

        for english_term in entry.english:
            pairs.add((english_term, entry.word))

    english_terms = {english for english, _ in pairs}
    ranks = {term: rank_lookup(term) for term in english_terms}

    if top_n is not None:
        keep = sorted(english_terms, key=lambda term: (ranks[term], term))[:top_n]
        english_terms = set(keep)
        pairs = {pair for pair in pairs if pair[0] in english_terms}

    # Drop German words left with no surviving pair.
    kept_german = {german_word for _, german_word in pairs}
    german = {word: entry for word, entry in german.items() if word in kept_german}

    db_path.parent.mkdir(parents=True, exist_ok=True)
    db_path.unlink(missing_ok=True)

    connection = sqlite3.connect(db_path)
    try:
        connection.executescript(SCHEMA)

        english_ids = _insert_english(connection, english_terms, ranks)
        german_ids = _insert_german(connection, german)

        connection.executemany(
            "INSERT OR IGNORE INTO translations (en_id, de_id) VALUES (?, ?)",
            [
                (english_ids[english], german_ids[german_word])
                for english, german_word in sorted(pairs)
            ],
        )

        connection.executemany(
            "INSERT INTO meta (key, value) VALUES (?, ?)",
            [
                ("schema_version", "1"),
                ("pair", "en-de"),
                ("source", "German Wiktionary via cstr/de-wiktionary-extracted"),
                ("license", "CC-BY-SA 4.0"),
                ("top_n", str(top_n) if top_n is not None else "all"),
            ],
        )

        connection.commit()
        connection.execute("VACUUM")
    finally:
        connection.close()

    return BuildStats(
        english=len(english_terms),
        german=len(german),
        pairs=len(pairs),
        considered=considered,
    )


def _insert_english(
    connection: sqlite3.Connection,
    terms: Iterable[str],
    ranks: dict[str, int],
) -> dict[str, int]:
    ordered = sorted(terms, key=lambda term: (ranks.get(term, UNKNOWN_RANK), term))
    rows = [
        (index, term, None if ranks.get(term, UNKNOWN_RANK) >= UNKNOWN_RANK else ranks[term])
        for index, term in enumerate(ordered, start=1)
    ]
    connection.executemany("INSERT INTO words_en (id, text, freq_rank) VALUES (?, ?, ?)", rows)
    return {term: index for index, term, _ in rows}


def _insert_german(
    connection: sqlite3.Connection,
    entries: dict[str, Entry],
) -> dict[str, int]:
    rows = [
        (index, word, entries[word].gender, entries[word].pos)
        for index, word in enumerate(sorted(entries), start=1)
    ]
    connection.executemany(
        "INSERT INTO words_de (id, text, gender, pos) VALUES (?, ?, ?, ?)", rows
    )
    return {word: index for index, word, _, _ in rows}
