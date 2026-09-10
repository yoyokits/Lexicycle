"""The narrow record the pipeline cares about, and the rules for extracting it.

Upstream rows carry 30+ fields (senses, IPA, inflected forms, etymology, semantic
relations). Lexicycle v1 needs four things: the German lemma, its part of speech, its
grammatical gender, and its English translations. Everything else is dropped here, which
is what keeps the generated database small.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any, Iterable, Iterator

#: Gender tags wiktextract puts on the entry, in the order we prefer them.
GENDERS = ("masculine", "feminine", "neuter")

#: Parquet columns the pipeline reads. Everything else stays on disk.
REQUIRED_COLUMNS = ("word", "pos", "lang_code", "tags", "translations")

#: Trailing round-bracket qualifier, e.g. "schadenfreude (loanword)". Safe to drop.
#: Square brackets are deliberately NOT stripped — in this dataset they wrap glosses
#: ("malicious joy [at another's misfortune]"), and stripping them would leave a
#: plausible-looking but wrong term behind. Those are rejected outright below.
_PARENTHETICAL = re.compile(r"\s*\([^)]*\)\s*$")

#: Anything that signals a gloss or fragment rather than a plain term.
_REJECTED_CHARACTERS = frozenset("[]{}<>|/;…\"")


@dataclass(frozen=True)
class Entry:
    """One German lemma with the English words it translates to."""

    word: str
    pos: str | None = None
    gender: str | None = None
    english: tuple[str, ...] = field(default=())


def clean_term(raw: Any) -> str | None:
    """Normalise one translation term, or return None when it is not usable as a pair.

    Rejects glosses, fragments, and anything longer than three words — Lexicycle drills
    single terms, not explanations.
    """
    if not isinstance(raw, str):
        return None

    term = _PARENTHETICAL.sub("", raw).strip()
    term = " ".join(term.split())

    if not term:
        return None

    if any(character in _REJECTED_CHARACTERS for character in term):
        return None

    # A term that is mostly punctuation, or an explanation rather than a word.
    if len(term.split()) > 3:
        return None

    if not any(character.isalpha() for character in term):
        return None

    return term


def extract_gender(tags: Any) -> str | None:
    """Pick the grammatical gender out of the entry-level tag list."""
    if not tags:
        return None

    tag_set = {str(tag).lower() for tag in tags}
    for gender in GENDERS:
        if gender in tag_set:
            return gender

    return None


def extract_english(translations: Any) -> tuple[str, ...]:
    """English terms from the translation list, de-duplicated, order preserved."""
    if not translations:
        return ()

    found: dict[str, None] = {}
    for translation in translations:
        if not isinstance(translation, dict):
            continue

        if translation.get("lang_code") != "en":
            continue

        # Wiktionary flags translations it is unsure about; they make poor quiz answers.
        if translation.get("uncertain"):
            continue

        term = clean_term(translation.get("word"))
        if term:
            found.setdefault(term, None)

    return tuple(found)


def row_to_entry(row: dict[str, Any]) -> Entry | None:
    """Convert one upstream row, or None when it yields no usable pair."""
    word = clean_term(row.get("word"))
    if not word:
        return None

    # The dataset is the German edition, but guard anyway so a mixed dump cannot leak in.
    lang_code = row.get("lang_code")
    if lang_code and lang_code != "de":
        return None

    english = extract_english(row.get("translations"))
    if not english:
        return None

    return Entry(
        word=word,
        pos=row.get("pos") or None,
        gender=extract_gender(row.get("tags")),
        english=english,
    )


def rows_to_entries(rows: Iterable[dict[str, Any]]) -> Iterator[Entry]:
    """Filter and convert a stream of upstream rows."""
    for row in rows:
        entry = row_to_entry(row)
        if entry is not None:
            yield entry
