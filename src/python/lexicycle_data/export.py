"""Exports vocabulary sets from the generated dictionary in the app's JSON format.

Lets the MAUI app practise real dictionary data before any on-device SQLite work exists.
"""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path

#: Article for each gender, by target language. Spanish has no neuter for common nouns.
ARTICLES: dict[str, dict[str, str]] = {
    "de": {"masculine": "der", "feminine": "die", "neuter": "das"},
    "es": {"masculine": "el", "feminine": "la"},
}

#: Display name of each target language, used to name a set when the caller gives none.
LANGUAGE_NAMES = {"de": "German", "es": "Spanish"}


def export_set(
    db_path: Path,
    output_path: Path,
    target_language: str = "de",
    limit: int = 50,
    offset: int = 0,
    set_id: str | None = None,
    name: str | None = None,
) -> int:
    """Write the ``limit`` most frequent English words as one vocabulary set.

    Returns the number of word pairs written.
    """
    articles = ARTICLES.get(target_language, {})

    connection = sqlite3.connect(db_path)
    connection.row_factory = sqlite3.Row

    try:
        rows = connection.execute(
            f"""
            SELECT en.text AS english, tw.text AS answer, tw.gender AS gender
            FROM words_en AS en
            JOIN translations AS t ON t.en_id = en.id
            JOIN words_{target_language} AS tw ON tw.id = t.{target_language}_id
            WHERE en.id IN (
                SELECT id FROM words_en
                WHERE freq_rank IS NOT NULL
                ORDER BY freq_rank
                LIMIT ? OFFSET ?
            )
            ORDER BY en.freq_rank, tw.id
            """,
            (limit, offset),
        ).fetchall()
    finally:
        connection.close()

    # Several target-language words can translate one English word; all count as correct.
    words: dict[str, dict] = {}
    for row in rows:
        entry = words.setdefault(row["english"], {"source": row["english"], "answers": []})
        entry["answers"].append(row["answer"])

        # The hint shows the article and gender but never the word itself — spelling out
        # "das Haus" under the prompt "house" would hand the user the answer.
        if len(entry["answers"]) == 1 and row["gender"] in articles:
            entry["hint"] = f"{articles[row['gender']]} … ({row['gender']})"

    language_name = LANGUAGE_NAMES.get(target_language, target_language)
    payload = {
        "id": set_id or f"en-{target_language}-top{limit}",
        "name": name or f"{language_name} top {limit}",
        "sourceLanguage": "en",
        "targetLanguage": target_language,
        "words": list(words.values()),
    }

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    return len(payload["words"])
