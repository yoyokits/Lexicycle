"""Extraction rules: what becomes a word pair and what gets thrown away."""

from __future__ import annotations

import pytest

from lexicycle_data.model import (
    clean_term,
    extract_english,
    extract_gender,
    row_to_entry,
    rows_to_entries,
)

from conftest import translation


class TestCleanTerm:
    @pytest.mark.parametrize(
        ("raw", "expected"),
        [
            ("house", "house"),
            ("  house  ", "house"),
            ("ice   cream", "ice cream"),
            ("schadenfreude (loanword)", "schadenfreude"),
        ],
    )
    def test_normalises_usable_terms(self, raw, expected):
        assert clean_term(raw) == expected

    @pytest.mark.parametrize(
        "raw",
        [
            None,
            "",
            "   ",
            # Square brackets wrap glosses. Stripping them would leave "malicious joy",
            # which reads like a real term but is not the translation.
            "malicious joy [at another's misfortune]",
            "run [informal]",
            "joy at the misfortune of other people",  # too many words
            "---",  # no letters
            123,  # not a string
        ],
    )
    def test_rejects_unusable_terms(self, raw):
        assert clean_term(raw) is None


class TestExtractGender:
    @pytest.mark.parametrize(
        ("tags", "expected"),
        [
            (["neuter"], "neuter"),
            (["feminine"], "feminine"),
            (["masculine"], "masculine"),
            (["Neuter"], "neuter"),
            (["plural", "masculine"], "masculine"),
            ([], None),
            (None, None),
            (["plural"], None),
        ],
    )
    def test_reads_gender_from_tags(self, tags, expected):
        assert extract_gender(tags) == expected


class TestExtractEnglish:
    def test_keeps_only_english_translations(self):
        translations = [
            translation("house"),
            translation("maison", lang_code="fr"),
            translation("casa", lang_code="es"),
        ]

        assert extract_english(translations) == ("house",)

    def test_deduplicates_while_preserving_order(self):
        translations = [translation("car"), translation("automobile"), translation("car")]

        assert extract_english(translations) == ("car", "automobile")

    def test_skips_uncertain_translations(self):
        assert extract_english([translation("gloating", uncertain=True)]) == ()

    def test_handles_missing_translations(self):
        assert extract_english(None) == ()
        assert extract_english([]) == ()


class TestRowToEntry:
    def test_maps_a_complete_row(self):
        entry = row_to_entry(
            {
                "word": "Haus",
                "pos": "noun",
                "lang_code": "de",
                "tags": ["neuter"],
                "translations": [translation("house")],
            }
        )

        assert entry is not None
        assert entry.word == "Haus"
        assert entry.pos == "noun"
        assert entry.gender == "neuter"
        assert entry.english == ("house",)

    def test_drops_a_row_without_english(self):
        assert row_to_entry({"word": "Haus", "lang_code": "de", "translations": []}) is None

    def test_drops_a_row_from_another_language_edition(self):
        row = {
            "word": "house",
            "lang_code": "en",
            "translations": [translation("Haus", lang_code="de")],
        }

        assert row_to_entry(row) is None


def test_rows_to_entries_filters_the_stream(rows):
    entries = list(rows_to_entries(rows))
    words = [entry.word for entry in entries]

    assert "Haus" in words
    assert "Ohnegleichen" not in words  # no English translation
    assert "house" not in words  # wrong language edition

    schadenfreude = next(entry for entry in entries if entry.word == "Schadenfreude")
    assert schadenfreude.english == ("schadenfreude",)
