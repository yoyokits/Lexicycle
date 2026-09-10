"""Fixtures shaped exactly like the upstream rows, so no 287 MB download is needed."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))


def translation(word: str, lang_code: str = "en", **extra) -> dict:
    """One entry of the upstream ``translations`` list."""
    return {"lang": "Englisch", "lang_code": lang_code, "word": word, **extra}


@pytest.fixture
def rows() -> list[dict]:
    """A miniature stand-in for the German Wiktionary export.

    Field names and nesting match the real dataset: word / pos / lang_code / tags /
    translations[{lang_code, word, uncertain, ...}].
    """
    return [
        {
            "word": "Haus",
            "pos": "noun",
            "lang_code": "de",
            "tags": ["neuter"],
            "translations": [
                translation("house"),
                translation("maison", lang_code="fr"),
            ],
        },
        {
            "word": "Auto",
            "pos": "noun",
            "lang_code": "de",
            "tags": ["neuter"],
            "translations": [translation("car")],
        },
        {
            # A second German word for the same English term.
            "word": "Wagen",
            "pos": "noun",
            "lang_code": "de",
            "tags": ["masculine"],
            "translations": [translation("car")],
        },
        {
            "word": "Katze",
            "pos": "noun",
            "lang_code": "de",
            "tags": ["feminine"],
            "translations": [translation("cat"), translation("cat")],  # duplicate
        },
        {
            "word": "Subfamilia",
            "pos": "noun",
            "lang_code": "de",
            "tags": ["feminine"],
            "translations": [translation("subfamily")],  # rare: dropped by a small top-N
        },
        {
            # No English translation at all — must not reach the database.
            "word": "Ohnegleichen",
            "pos": "adj",
            "lang_code": "de",
            "tags": [],
            "translations": [translation("sans pareil", lang_code="fr")],
        },
        {
            # Uncertain and gloss-shaped translations must be rejected.
            "word": "Schadenfreude",
            "pos": "noun",
            "lang_code": "de",
            "tags": ["feminine"],
            "translations": [
                translation("gloating", uncertain=True),
                translation("malicious joy [at another's misfortune]"),
                translation("joy at the misfortune of other people"),  # too long
                translation("schadenfreude (loanword)"),  # parenthetical is stripped
            ],
        },
        {
            # A row from another language edition must be ignored.
            "word": "house",
            "pos": "noun",
            "lang_code": "en",
            "tags": [],
            "translations": [translation("Haus", lang_code="de")],
        },
    ]
