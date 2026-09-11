"""Fixtures shaped exactly like the upstream rows, so no 3.2 GB download is needed.

Field names and nesting match the English Wiktionary extract after the download step's
distillation: word / pos / lang_code / translations[{word, sense, tags, code, english}].
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def translation(
    word: str,
    sense: str | None = None,
    tags: list[str] | None = None,
    code: str = "de",
) -> dict:
    """One entry of the upstream ``translations`` list.

    Defaults to German (``code="de"``) since that is what most of this fixture data
    exercises; pass ``code="es"`` for the handful of tests covering a second language.
    """
    entry: dict = {"word": word, "code": code}
    if sense is not None:
        entry["sense"] = sense
    if tags:
        entry["tags"] = tags
    return entry


@pytest.fixture
def rows() -> list[dict]:
    """A miniature stand-in for the English Wiktionary extract."""
    return [
        {
            "word": "house",
            "pos": "noun",
            "lang_code": "en",
            "translations": [
                translation("Haus", "building", ["neuter"]),
                translation("Gebäude", "building", ["neuter"]),
                # A later sense must not leak into the answers.
                translation("Zunft", "guild", ["feminine"]),
            ],
        },
        {
            "word": "car",
            "pos": "noun",
            "lang_code": "en",
            "translations": [
                translation("Auto", "automobile", ["neuter"]),
                translation("Wagen", "automobile", ["masculine"]),
            ],
        },
        {
            "word": "cat",
            "pos": "noun",
            "lang_code": "en",
            "translations": [
                translation("Katze", "animal", ["feminine"]),
                translation("Katze", "animal", ["feminine"]),  # duplicate
            ],
        },
        {
            "word": "furniture",
            "pos": "noun",
            "lang_code": "en",
            "translations": [translation("Möbel", "large movable items", ["neuter"])],
        },
        {
            "word": "run",
            "pos": "verb",
            "lang_code": "en",
            "translations": [
                translation("rennen", "to move quickly on two feet"),
                translation("laufen", "to move quickly on two feet"),
                translation("umlaufen", "to move or spread quickly"),  # other sense
            ],
        },
        {
            "word": "subfamily",
            "pos": "noun",
            "lang_code": "en",
            "translations": [translation("Unterfamilie", "taxonomy", ["feminine"])],
        },
        {
            # Obsolete and gloss-shaped translations must be rejected.
            "word": "sword",
            "pos": "noun",
            "lang_code": "en",
            "translations": [
                translation("Schwert", "weapon", ["neuter"]),
                translation("Degen", "weapon", ["masculine", "obsolete"]),
                translation("Klinge [poetic]", "weapon", ["neuter"]),
                translation("a long bladed weapon for cutting", "weapon"),  # too long
                translation("Säbel (curved)", "weapon", ["masculine"]),  # qualifier stripped
            ],
        },
        {
            # Function words are not vocabulary; rejected by part of speech.
            "word": "the",
            "pos": "article",
            "lang_code": "en",
            "translations": [translation("der"), translation("die"), translation("das")],
        },
        {
            # No German translation at all.
            "word": "nonesuch",
            "pos": "noun",
            "lang_code": "en",
            "translations": [],
        },
        {
            # A distilled row carries every configured target language's translations
            # together, grouped by sense — German and Spanish here share one sense.
            "word": "love",
            "pos": "noun",
            "lang_code": "en",
            "translations": [
                translation("Liebe", "affection", ["feminine"], code="de"),
                translation("amor", "affection", ["masculine"], code="es"),
            ],
        },
        {
            # A row from another language edition must be ignored.
            "word": "Haus",
            "pos": "noun",
            "lang_code": "de",
            "translations": [translation("house")],
        },
    ]
