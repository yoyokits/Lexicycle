"""Extraction rules: what becomes a word pair and what gets thrown away."""

from __future__ import annotations

import pytest

from lexicycle_data.model import (
    clean_term,
    extract_gender,
    extract_translations,
    is_drillable_prompt,
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


class TestExtractTranslations:
    def test_returns_terms_with_their_gender(self):
        result = extract_translations(
            [
                translation("Auto", "automobile", ["neuter"]),
                translation("Wagen", "automobile", ["masculine"]),
            ],
            "de",
        )

        assert result == (("Auto", "neuter"), ("Wagen", "masculine"))

    def test_deduplicates_while_preserving_order(self):
        result = extract_translations(
            [
                translation("Katze", "animal", ["feminine"]),
                translation("Katze", "animal", ["feminine"]),
            ],
            "de",
        )

        assert result == (("Katze", "feminine"),)

    def test_skips_obsolete_and_archaic_translations(self):
        result = extract_translations(
            [
                translation("Schwert", "weapon", ["neuter"]),
                translation("Degen", "weapon", ["masculine", "obsolete"]),
            ],
            "de",
        )

        assert result == (("Schwert", "neuter"),)

    def test_handles_missing_translations(self):
        assert extract_translations(None, "de") == ()
        assert extract_translations([], "de") == ()

    def test_keeps_every_sense_not_just_the_first(self):
        """One English word can mean several things, and each meaning is a valid answer.

        "run" is both `laufen`/`rennen` and `fließen`; a learner typing any of them has
        translated the prompt correctly. Which of them actually ship is decided later,
        by frequency, in database.build_database.
        """
        result = extract_translations(
            [
                translation("laufen", "to move quickly on two feet"),
                translation("rennen", "to move quickly on two feet"),
                translation("fließen", "to flow"),
            ],
            "de",
        )

        assert result == (("laufen", None), ("rennen", None), ("fließen", None))

    def test_a_word_repeated_across_senses_is_one_answer(self):
        """Wiktionary lists "laufen" under several senses of "run"; it is one answer."""
        result = extract_translations(
            [
                translation("laufen", "to move quickly on two feet"),
                translation("laufen", "to move quickly"),
            ],
            "de",
        )

        assert result == (("laufen", None),)

    def test_a_gender_found_on_a_later_sense_is_kept(self):
        result = extract_translations(
            [
                translation("Bank", "financial institution"),
                translation("Bank", "bench", ["feminine"]),
            ],
            "de",
        )

        assert result == (("Bank", "feminine"),)

    def test_picks_out_one_language_from_a_shared_sense(self):
        """A distilled row can carry several target languages' translations of the same
        sense together; only the language asked for should come back."""
        translations = [
            translation("Liebe", "affection", ["feminine"], code="de"),
            translation("amor", "affection", ["masculine"], code="es"),
        ]

        assert extract_translations(translations, "de") == (("Liebe", "feminine"),)
        assert extract_translations(translations, "es") == (("amor", "masculine"),)

    def test_untagged_translations_are_never_filtered_out(self):
        """A translation with no `code` at all (older fixtures, hand-built data) is kept
        for whatever language is asked — there is nothing to tell it apart by."""
        result = extract_translations([{"word": "rennen", "sense": "move"}], "de")

        assert result == (("rennen", None),)


class TestRowToEntry:
    def test_maps_a_complete_row(self):
        entry = row_to_entry(
            {
                "word": "house",
                "pos": "noun",
                "lang_code": "en",
                "translations": [translation("Haus", "building", ["neuter"])],
            },
            "de",
        )

        assert entry is not None
        assert entry.word == "house"
        assert entry.pos == "noun"
        assert entry.translations == (("Haus", "neuter"),)

    def test_drops_a_row_without_a_translation_in_the_target_language(self):
        row = {"word": "nonesuch", "pos": "noun", "lang_code": "en", "translations": []}

        assert row_to_entry(row, "de") is None

    @pytest.mark.parametrize("pos", ["article", "prep", "conj", "particle", "det"])
    def test_drops_function_words(self, pos):
        """`the -> der | die | das` is not vocabulary worth drilling."""
        row = {
            "word": "the",
            "pos": pos,
            "lang_code": "en",
            "translations": [translation("der")],
        }

        assert row_to_entry(row, "de") is None

    @pytest.mark.parametrize("pos", ["noun", "verb", "adj", "adv"])
    def test_keeps_content_words(self, pos):
        row = {
            "word": "x",
            "pos": pos,
            "lang_code": "en",
            "translations": [translation("Ypsilon")],
        }

        assert row_to_entry(row, "de") is not None

    def test_drops_a_row_from_another_language_edition(self):
        row = {
            "word": "Haus",
            "pos": "noun",
            "lang_code": "de",
            "translations": [translation("house")],
        }

        assert row_to_entry(row, "de") is None

    def test_a_pair_absent_from_the_row_yields_nothing(self):
        """"love" carries German and Spanish; asking for French finds neither."""
        row = {
            "word": "love",
            "pos": "noun",
            "lang_code": "en",
            "translations": [translation("Liebe", code="de"), translation("amor", code="es")],
        }

        assert row_to_entry(row, "fr") is None


def test_rows_to_entries_filters_the_stream(rows):
    entries = list(rows_to_entries(rows, "de"))
    words = [entry.word for entry in entries]

    assert "house" in words
    assert "nonesuch" not in words  # no German translation
    assert "the" not in words  # function word
    assert "Haus" not in words  # wrong language edition

    # Every sense reaches the Entry, including the rarer "guild" one. Whether Zunft
    # survives into the built dictionary is a frequency question, tested in
    # test_database.py, not something extraction decides.
    house = next(entry for entry in entries if entry.word == "house")
    assert house.translations == (("Haus", "neuter"), ("Gebäude", "neuter"), ("Zunft", "feminine"))

    sword = next(entry for entry in entries if entry.word == "sword")
    assert sword.translations == (("Schwert", "neuter"), ("Säbel", "masculine"))

    love = next(entry for entry in entries if entry.word == "love")
    assert love.translations == (("Liebe", "feminine"),)


def test_rows_to_entries_builds_a_second_pair_from_the_same_stream(rows):
    """Building en-es from the identical row stream needs no re-download and no change
    to extraction rules — only which language `rows_to_entries` is asked for."""
    entries = list(rows_to_entries(rows, "es"))
    words = {entry.word: entry for entry in entries}

    assert "house" not in words  # this fixture gives "house" no Spanish translation
    assert words["love"].translations == (("amor", "masculine"),)


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
        result = extract_translations(
            [
                translation("Zeit", "time", ["feminine"]),
                translation("Ziit", "time", [tag, "feminine"]),
            ],
            "de",
        )

        assert result == (("Zeit", "feminine"),)

    def test_rejects_morphological_fragments(self):
        """"most" offers "-ste" and "am ...-sten", which are endings, not words."""
        assert clean_term("-ste") is None
        assert clean_term("am ...-sten") is None
        assert clean_term("sten-") is None
