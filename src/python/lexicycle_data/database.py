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
    pos       TEXT,
    freq_rank INTEGER
);

CREATE TABLE words_de (
    id     INTEGER PRIMARY KEY,
    text   TEXT    NOT NULL UNIQUE,
    gender TEXT
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


#: Answers beyond this many make a prompt ambiguous rather than rich.
MAX_ANSWERS = 4

#: A German answer this much rarer than the entry's best one is a dialect or archaic
#: variant riding along with the standard word, and is dropped.
_RANK_GAP = 1_500


def _best_answers(
    german: tuple[tuple[str, str | None], ...],
    rank_lookup: RankLookup,
) -> list[tuple[str, str | None]]:
    """Order an entry's German answers by real-world frequency and keep the best few.

    Wiktionary lists dialect forms beside the standard word and not always after it —
    "love" offers Liab before Liebe — so source order cannot pick the primary answer.
    Frequency can: Liebe is common German, Liab is not.
    """
    if len(german) <= 1:
        return list(german)

    ranked = sorted(german, key=lambda pair: (rank_lookup(pair[0]), pair[0]))
    best = rank_lookup(ranked[0][0])

    return [
        pair for pair in ranked if rank_lookup(pair[0]) - best <= _RANK_GAP
    ][:MAX_ANSWERS]


def build_database(
    entries: Iterable[Entry],
    db_path: Path,
    top_n: int | None = 5000,
    rank_lookup: RankLookup | None = None,
    german_rank_lookup: RankLookup | None = None,
) -> BuildStats:
    """Write ``entries`` to a fresh SQLite file at ``db_path``.

    ``top_n`` keeps only the most frequent English words (and the German words paired
    with them); pass None to keep everything. ``german_rank_lookup`` orders each entry's
    answers so the standard German word, not a dialect variant, is the one displayed.
    """
    rank_lookup = rank_lookup or null_rank_lookup()
    german_rank_lookup = german_rank_lookup or null_rank_lookup()

    # Collect first so English words can be ranked before the top-N cut is applied.
    pairs: set[tuple[str, str]] = set()
    english: dict[str, str | None] = {}
    german: dict[str, str | None] = {}
    considered = 0

    for entry in entries:
        considered += 1

        # First entry for a lemma wins; later ones are other parts of speech.
        english.setdefault(entry.word, entry.pos)

        for german_term, gender in _best_answers(entry.german, german_rank_lookup):
            pairs.add((entry.word, german_term))

            # Fill in a gender discovered on any occurrence of the term.
            if german.get(german_term) is None:
                german[german_term] = gender

    english_terms = {term for term, _ in pairs}
    ranks = {term: rank_lookup(term) for term in english_terms}

    if top_n is not None:
        keep = sorted(english_terms, key=lambda term: (ranks[term], term))[:top_n]
        english_terms = set(keep)
        pairs = {pair for pair in pairs if pair[0] in english_terms}

    # Drop German words left with no surviving pair.
    kept_german = {german_word for _, german_word in pairs}
    german = {word: gender for word, gender in german.items() if word in kept_german}

    db_path.parent.mkdir(parents=True, exist_ok=True)
    db_path.unlink(missing_ok=True)

    connection = sqlite3.connect(db_path)
    try:
        connection.executescript(SCHEMA)

        english_pos = {term: english.get(term) for term in english_terms}
        english_ids = _insert_english(connection, english_terms, ranks, english_pos)
        german_ids = _insert_german(connection, german)

        connection.executemany(
            "INSERT OR IGNORE INTO translations (en_id, de_id) VALUES (?, ?)",
            [
                (english_ids[english_term], german_ids[german_word])
                for english_term, german_word in sorted(pairs)
            ],
        )

        connection.executemany(
            "INSERT INTO meta (key, value) VALUES (?, ?)",
            [
                ("schema_version", "2"),
                ("pair", "en-de"),
                ("source", "English Wiktionary via kaikki.org (wiktextract)"),
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
    parts_of_speech: dict[str, str | None],
) -> dict[str, int]:
    ordered = sorted(terms, key=lambda term: (ranks.get(term, UNKNOWN_RANK), term))
    rows = [
        (
            index,
            term,
            parts_of_speech.get(term),
            None if ranks.get(term, UNKNOWN_RANK) >= UNKNOWN_RANK else ranks[term],
        )
        for index, term in enumerate(ordered, start=1)
    ]
    connection.executemany(
        "INSERT INTO words_en (id, text, pos, freq_rank) VALUES (?, ?, ?, ?)", rows
    )
    return {term: index for index, term, _, _ in rows}


def _insert_german(
    connection: sqlite3.Connection,
    genders: dict[str, str | None],
) -> dict[str, int]:
    rows = [
        (index, word, genders[word])
        for index, word in enumerate(sorted(genders), start=1)
    ]
    connection.executemany("INSERT INTO words_de (id, text, gender) VALUES (?, ?, ?)", rows)
    return {word: index for index, word, _ in rows}
