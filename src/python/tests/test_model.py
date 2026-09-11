"""Extraction rules: what becomes a word pair and what gets thrown away."""

from __future__ import annotations

import pytest

from lexicycle_data.model import (
    clean_term,
    extract_gender,
    extract_german,
    is_drillable_prompt,
    primary_sense_translations,
    row_to_entry,
    rows_to_entries,
)

from conftest import translation


class TestCleanTerm:
    @pytest.mark.parametrize(
        ("raw", "expected"),
        [
            ("Haus", "Haus"),
            ("  Haus  ", "Haus"),
            ("sich   freuen", "sich freuen"),
            ("Säbel (curved)", "Säbel"),
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
            # Square brackets wrap glosses; stripping them would leave a wrong term.
            "Klinge [poetic]",
            "a long bladed weapon for cutting",  # too many words
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
    def test_reads_gender_from_translation_tags(self, tags, expected):
        assert extract_gender(tags) == expected


class TestPrimarySense:
    def test_keeps_only_the_first_sense(self):
        translations = [
            translation("rennen", "to move quickly on two feet"),
            translation("laufen", "to move quickly on two feet"),
            translation("umlaufen", "to move or spread quickly"),
        ]

        kept = [t["word"] for t in primary_sense_translations(translations)]

        assert kept == ["rennen", "laufen"]

    def test_keeps_everything_when_no_senses_are_labelled(self):
        translations = [translation("Haus"), translation("Gebäude")]

        assert len(primary_sense_translations(translations)) == 2

    def test_handles_an_empty_list(self):
        assert primary_sense_translations([]) == []


class TestExtractGerman:
    def test_returns_terms_with_their_gender(self):
        result = extract_german(
            [
                translation("Auto", "automobile", ["neuter"]),
                translation("Wagen", "automobile", ["masculine"]),
            ]
        )

        assert result == (("Auto", "neuter"), ("Wagen", "masculine"))

    def test_deduplicates_while_preserving_order(self):
        result = extract_german(
            [
                translation("Katze", "animal", ["feminine"]),
                translation("Katze", "animal", ["feminine"]),
            ]
        )

        assert result == (("Katze", "feminine"),)

    def test_skips_obsolete_and_archaic_translations(self):
        result = extract_german(
            [
                translation("Schwert", "weapon", ["neuter"]),
                translation("Degen", "weapon", ["masculine", "obsolete"]),
            ]
        )

        assert result == (("Schwert", "neuter"),)

    def test_handles_missing_translations(self):
        assert extract_german(None) == ()
        assert extract_german([]) == ()


class TestRowToEntry:
    def test_maps_a_complete_row(self):
        entry = row_to_entry(
            {
                "word": "house",
                "pos": "noun",
                "lang_code": "en",
                "translations": [translation("Haus", "building", ["neuter"])],
            }
        )

        assert entry is not None
        assert entry.word == "house"
        assert entry.pos == "noun"
        assert entry.german == (("Haus", "neuter"),)

    def test_drops_a_row_without_german(self):
        row = {"word": "nonesuch", "pos": "noun", "lang_code": "en", "translations": []}

        assert row_to_entry(row) is None

    @pytest.mark.parametrize("pos", ["article", "prep", "conj", "particle", "det"])
    def test_drops_function_words(self, pos):
        """`the -> der | die | das` is not vocabulary worth drilling."""
        row = {
            "word": "the",
            "pos": pos,
            "lang_code": "en",
            "translations": [translation("der")],
        }

        assert row_to_entry(row) is None

    @pytest.mark.parametrize("pos", ["noun", "verb", "adj", "adv"])
    def test_keeps_content_words(self, pos):
        row = {
            "word": "x",
            "pos": pos,
            "lang_code": "en",
            "translations": [translation("Ypsilon")],
        }

        assert row_to_entry(row) is not None

    def test_drops_a_row_from_another_language_edition(self):
        row = {
            "word": "Haus",
            "pos": "noun",
            "lang_code": "de",
            "translations": [translation("house")],
        }

        assert row_to_entry(row) is None


def test_rows_to_entries_filters_the_stream(rows):
    entries = list(rows_to_entries(rows))
    words = [entry.word for entry in entries]

    assert "house" in words
    assert "nonesuch" not in words  # no German translation
    assert "the" not in words  # function word
    assert "Haus" not in words  # wrong language edition

    house = next(entry for entry in entries if entry.word == "house")
    assert house.german == (("Haus", "neuter"), ("Gebäude", "neuter"))

    sword = next(entry for entry in entries if entry.word == "sword")
    assert sword.german == (("Schwert", "neuter"), ("Säbel", "masculine"))


class TestDrillablePrompt:
    """Which English headwords are worth asking at all."""

    @pytest.mark.parametrize("word", ["house", "furniture", "quickly", "Catholicize"])
    def test_keeps_content_words(self, word):
        assert is_drillable_prompt(word)

    @pytest.mark.parametrize("word", ["the", "that", "in", "so", "when", "more", "There"])
    def test_rejects_function_words(self, word):
        """`in -> herein` and `that -> dermaßen` are grammar, not vocabulary."""
        assert not is_drillable_prompt(word)

    @pytest.mark.parametrize("word", ["as in", "what if", "get in", "make up"])
    def test_rejects_multi_word_prompts_in_v1(self, word):
        assert not is_drillable_prompt(word)


class TestDialectFiltering:
    """Wiktionary lists regional forms beside the standard word."""

    @pytest.mark.parametrize(
        "tag",
        ["Alemannic-German", "Swiss", "Bavarian", "Palatine", "Rhine-Franconian", "dialectal"],
    )
    def test_rejects_regionally_tagged_translations(self, tag):
        result = extract_german(
            [
                translation("Zeit", "time", ["feminine"]),
                translation("Ziit", "time", [tag, "feminine"]),
            ]
        )

        assert result == (("Zeit", "feminine"),)

    def test_rejects_morphological_fragments(self):
        """"most" offers "-ste" and "am ...-sten", which are endings, not words."""
        assert clean_term("-ste") is None
        assert clean_term("am ...-sten") is None
        assert clean_term("sten-") is None
