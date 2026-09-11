"""Builds the small three-table SQLite database the app bundles.

Schema is deliberately minimal — a word table per language plus a join table. One
database holds exactly one language pair; a second pair (English-Spanish alongside
English-German) is a second file built with a different ``target_language``, not a
wider schema. Enrichment (examples, IPA) arrives later as nullable columns or side
tables, so nothing here needs a breaking migration.
"""

from __future__ import annotations

import re
import sqlite3
from pathlib import Path
from typing import Iterable

from .frequency import UNKNOWN_RANK, RankLookup, null_rank_lookup
from .model import Entry

#: Target-language codes are only ever one of the values in sources.TARGET_LANGUAGES,
#: never user input, but this still guards against a typo landing in raw SQL.
_VALID_LANGUAGE_CODE = re.compile(r"^[a-z]{2,3}$")

_BASE_SCHEMA = """
CREATE TABLE words_en (
    id        INTEGER PRIMARY KEY,
    text      TEXT    NOT NULL UNIQUE,
    pos       TEXT,
    freq_rank INTEGER
);

CREATE INDEX idx_words_en_rank ON words_en(freq_rank);

CREATE TABLE meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
"""


def schema_for(target_language: str) -> str:
    """The full schema for one language pair's database.

    The target-language table and its half of the join table are named after the
    language code (``words_de`` / ``de_id``, ``words_es`` / ``es_id``) so a bundled
    database is self-describing without a side channel — the app reads the pair from
    ``meta`` once, then knows which table to join against.
    """
    if not _VALID_LANGUAGE_CODE.match(target_language):
        raise ValueError(f"Not a language code: {target_language!r}")

    lang = target_language
    return f"""
{_BASE_SCHEMA}

CREATE TABLE words_{lang} (
    id     INTEGER PRIMARY KEY,
    text   TEXT    NOT NULL UNIQUE,
    gender TEXT
);

CREATE TABLE translations (
    en_id   INTEGER NOT NULL REFERENCES words_en(id),
    {lang}_id INTEGER NOT NULL REFERENCES words_{lang}(id),
    PRIMARY KEY (en_id, {lang}_id)
) WITHOUT ROWID;

CREATE INDEX idx_translations_{lang} ON translations({lang}_id);
"""


class BuildStats:
    """Row counts from one build, for the report step and the CLI output."""

    def __init__(self, english: int, target: int, pairs: int, considered: int) -> None:
        self.english = english
        self.target = target
        self.pairs = pairs
        self.considered = considered

    def __repr__(self) -> str:  # pragma: no cover - debugging aid
        return (
            f"BuildStats(english={self.english}, target={self.target}, "
            f"pairs={self.pairs}, considered={self.considered})"
        )


#: Answers beyond this many make a prompt ambiguous rather than rich.
MAX_ANSWERS = 4

#: A target-language answer this much rarer than the entry's best one is a dialect or
#: archaic variant riding along with the standard word, and is dropped.
_RANK_GAP = 1_500


def _best_answers(
    translations: tuple[tuple[str, str | None], ...],
    rank_lookup: RankLookup,
) -> list[tuple[str, str | None]]:
    """Order an entry's answers by real-world frequency and keep the best few.

    Wiktionary lists dialect forms beside the standard word and not always after it —
    "love" offers Liab before Liebe — so source order cannot pick the primary answer.
    Frequency can: Liebe is common German, Liab is not.
    """
    if len(translations) <= 1:
        return list(translations)

    ranked = sorted(translations, key=lambda pair: (rank_lookup(pair[0]), pair[0]))
    best = rank_lookup(ranked[0][0])

    return [
        pair for pair in ranked if rank_lookup(pair[0]) - best <= _RANK_GAP
    ][:MAX_ANSWERS]


def build_database(
    entries: Iterable[Entry],
    db_path: Path,
    target_language: str = "de",
    top_n: int | None = 5000,
    rank_lookup: RankLookup | None = None,
    target_rank_lookup: RankLookup | None = None,
) -> BuildStats:
    """Write ``entries`` to a fresh SQLite file at ``db_path``.

    ``target_language`` names the pair being built (``"de"``, ``"es"``, ...) and decides
    the schema's table names. ``top_n`` keeps only the most frequent English words (and
    the target-language words paired with them); pass None to keep everything.
    ``target_rank_lookup`` orders each entry's answers so the standard word, not a
    dialect variant, is the one displayed.
    """
    rank_lookup = rank_lookup or null_rank_lookup()
    target_rank_lookup = target_rank_lookup or null_rank_lookup()

    # Collect first so English words can be ranked before the top-N cut is applied.
    pairs: set[tuple[str, str]] = set()
    english: dict[str, str | None] = {}
    target: dict[str, str | None] = {}
    considered = 0

    for entry in entries:
        considered += 1

        # First entry for a lemma wins; later ones are other parts of speech.
        english.setdefault(entry.word, entry.pos)

        for term, gender in _best_answers(entry.translations, target_rank_lookup):
            pairs.add((entry.word, term))

            # Fill in a gender discovered on any occurrence of the term.
            if target.get(term) is None:
                target[term] = gender

    english_terms = {term for term, _ in pairs}
    ranks = {term: rank_lookup(term) for term in english_terms}

    if top_n is not None:
        keep = sorted(english_terms, key=lambda term: (ranks[term], term))[:top_n]
        english_terms = set(keep)
        pairs = {pair for pair in pairs if pair[0] in english_terms}

    # Drop target-language words left with no surviving pair.
    kept_target = {target_word for _, target_word in pairs}
    target = {word: gender for word, gender in target.items() if word in kept_target}

    db_path.parent.mkdir(parents=True, exist_ok=True)
    db_path.unlink(missing_ok=True)

    connection = sqlite3.connect(db_path)
    try:
        connection.executescript(schema_for(target_language))

        english_pos = {term: english.get(term) for term in english_terms}
        english_ids = _insert_english(connection, english_terms, ranks, english_pos)
        target_ids = _insert_target(connection, target, target_language)

        connection.executemany(
            f"INSERT OR IGNORE INTO translations (en_id, {target_language}_id) "
            "VALUES (?, ?)",
            [
                (english_ids[english_term], target_ids[target_word])
                for english_term, target_word in sorted(pairs)
            ],
        )

        connection.executemany(
            "INSERT INTO meta (key, value) VALUES (?, ?)",
            [
                ("schema_version", "2"),
                ("pair", f"en-{target_language}"),
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
        target=len(target),
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


def _insert_target(
    connection: sqlite3.Connection,
    genders: dict[str, str | None],
    target_language: str,
) -> dict[str, int]:
    rows = [
        (index, word, genders[word])
        for index, word in enumerate(sorted(genders), start=1)
    ]
    connection.executemany(
        f"INSERT INTO words_{target_language} (id, text, gender) VALUES (?, ?, ?)", rows
    )
    return {word: index for index, word, _ in rows}
