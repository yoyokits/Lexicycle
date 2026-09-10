"""The generated SQLite: schema, joins, de-duplication and the top-N cut."""

from __future__ import annotations

import json
import sqlite3

import pytest

from lexicycle_data.database import build_database
from lexicycle_data.export import export_set
from lexicycle_data.model import rows_to_entries


@pytest.fixture
def fake_ranks():
    """Deterministic frequencies: 'car' commonest, 'subfamily' rarest."""
    ranks = {"house": 10, "car": 5, "cat": 20, "subfamily": 9_000, "schadenfreude": 8_000}
    return lambda term: ranks.get(term, 10**9)


@pytest.fixture
def db(tmp_path, rows, fake_ranks):
    path = tmp_path / "dict.db"
    stats = build_database(rows_to_entries(rows), path, top_n=None, rank_lookup=fake_ranks)
    return path, stats


def query(path, sql, *params):
    connection = sqlite3.connect(path)
    connection.row_factory = sqlite3.Row
    try:
        return connection.execute(sql, params).fetchall()
    finally:
        connection.close()


class TestSchema:
    def test_creates_the_three_core_tables(self, db):
        path, _ = db
        names = {
            row["name"]
            for row in query(path, "SELECT name FROM sqlite_master WHERE type='table'")
        }

        assert {"words_en", "words_de", "translations"} <= names

    def test_records_provenance_in_meta(self, db):
        path, _ = db
        meta = {row["key"]: row["value"] for row in query(path, "SELECT key, value FROM meta")}

        assert meta["pair"] == "en-de"
        assert meta["license"] == "CC-BY-SA 4.0"
        assert meta["schema_version"] == "1"


class TestContent:
    def test_stores_german_gender_and_pos(self, db):
        path, _ = db
        row = query(path, "SELECT gender, pos FROM words_de WHERE text = 'Haus'")[0]

        assert row["gender"] == "neuter"
        assert row["pos"] == "noun"

    def test_one_english_word_can_have_several_german_answers(self, db):
        path, _ = db
        answers = {
            row["text"]
            for row in query(
                path,
                """
                SELECT de.text FROM words_en en
                JOIN translations t ON t.en_id = en.id
                JOIN words_de de ON de.id = t.de_id
                WHERE en.text = 'car'
                """,
            )
        }

        assert answers == {"Auto", "Wagen"}

    def test_duplicate_translations_collapse_to_one_pair(self, db):
        path, _ = db
        count = query(
            path,
            """
            SELECT COUNT(*) AS n FROM translations t
            JOIN words_en en ON en.id = t.en_id
            WHERE en.text = 'cat'
            """,
        )[0]["n"]

        assert count == 1

    def test_every_pair_resolves_on_both_sides(self, db):
        path, stats = db
        joined = query(
            path,
            """
            SELECT COUNT(*) AS n FROM translations t
            JOIN words_en en ON en.id = t.en_id
            JOIN words_de de ON de.id = t.de_id
            """,
        )[0]["n"]

        assert joined == stats.pairs

    def test_excluded_rows_never_reach_the_database(self, db):
        path, _ = db
        german = {row["text"] for row in query(path, "SELECT text FROM words_de")}

        assert "Ohnegleichen" not in german


class TestTopN:
    def test_keeps_only_the_most_frequent_english_words(self, tmp_path, rows, fake_ranks):
        path = tmp_path / "top2.db"
        stats = build_database(rows_to_entries(rows), path, top_n=2, rank_lookup=fake_ranks)

        english = {row["text"] for row in query(path, "SELECT text FROM words_en")}

        assert english == {"car", "house"}  # ranks 5 and 10
        assert stats.english == 2

    def test_drops_german_words_left_without_a_pair(self, tmp_path, rows, fake_ranks):
        path = tmp_path / "top2.db"
        build_database(rows_to_entries(rows), path, top_n=2, rank_lookup=fake_ranks)

        german = {row["text"] for row in query(path, "SELECT text FROM words_de")}

        assert german == {"Haus", "Auto", "Wagen"}
        assert "Katze" not in german
        assert "Subfamilia" not in german

    def test_none_keeps_everything(self, db):
        path, stats = db
        english = {row["text"] for row in query(path, "SELECT text FROM words_en")}

        assert "subfamily" in english
        assert stats.english == len(english)

    def test_ranks_are_written_and_unknown_ones_are_null(self, tmp_path, rows):
        path = tmp_path / "noranks.db"
        build_database(rows_to_entries(rows), path, top_n=None)

        ranks = [row["freq_rank"] for row in query(path, "SELECT freq_rank FROM words_en")]

        assert all(rank is None for rank in ranks)

    def test_rebuilding_replaces_the_previous_file(self, tmp_path, rows, fake_ranks):
        path = tmp_path / "dict.db"
        build_database(rows_to_entries(rows), path, top_n=None, rank_lookup=fake_ranks)
        build_database(rows_to_entries(rows), path, top_n=1, rank_lookup=fake_ranks)

        assert len(query(path, "SELECT id FROM words_en")) == 1


class TestExport:
    def test_writes_a_vocabulary_set_the_app_can_read(self, db, tmp_path):
        path, _ = db
        output = tmp_path / "set.json"

        count = export_set(path, output, limit=3, set_id="en-de-top3", name="Top 3")
        payload = json.loads(output.read_text(encoding="utf-8"))

        assert count == len(payload["words"]) > 0
        assert payload["id"] == "en-de-top3"
        assert payload["sourceLanguage"] == "en"
        assert payload["targetLanguage"] == "de"

        for word in payload["words"]:
            assert word["source"]
            assert word["answers"]

    def test_groups_multiple_answers_under_one_prompt(self, db, tmp_path):
        path, _ = db
        output = tmp_path / "set.json"

        export_set(path, output, limit=5)
        payload = json.loads(output.read_text(encoding="utf-8"))

        car = next(word for word in payload["words"] if word["source"] == "car")
        assert set(car["answers"]) == {"Auto", "Wagen"}

    def test_hint_gives_the_article_without_revealing_the_answer(self, db, tmp_path):
        path, _ = db
        output = tmp_path / "set.json"

        export_set(path, output, limit=5)
        payload = json.loads(output.read_text(encoding="utf-8"))

        house = next(word for word in payload["words"] if word["source"] == "house")
        assert house["hint"] == "das … (neuter)"
        assert "Haus" not in house["hint"]

    def test_no_hint_ever_contains_its_own_answer(self, db, tmp_path):
        path, _ = db
        output = tmp_path / "set.json"

        export_set(path, output, limit=10)
        payload = json.loads(output.read_text(encoding="utf-8"))

        for word in payload["words"]:
            hint = word.get("hint")
            if not hint:
                continue
            for answer in word["answers"]:
                assert answer not in hint, f"hint for {word['source']!r} leaks {answer!r}"
