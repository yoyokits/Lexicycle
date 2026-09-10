"""Exports vocabulary sets from the generated dictionary in the app's JSON format.

Lets the MAUI app practise real dictionary data before any on-device SQLite work exists.
"""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path

#: Article for each gender, used to build the hint shown under the prompt.
ARTICLES = {"masculine": "der", "feminine": "die", "neuter": "das"}


def export_set(
    db_path: Path,
    output_path: Path,
    limit: int = 50,
    offset: int = 0,
    set_id: str | None = None,
    name: str | None = None,
) -> int:
    """Write the ``limit`` most frequent English words as one vocabulary set.

    Returns the number of word pairs written.
    """
    connection = sqlite3.connect(db_path)
    connection.row_factory = sqlite3.Row

    try:
        rows = connection.execute(
            """
            SELECT en.text AS english, de.text AS german, de.gender AS gender
            FROM words_en AS en
            JOIN translations AS t ON t.en_id = en.id
            JOIN words_de   AS de ON de.id = t.de_id
            WHERE en.id IN (
                SELECT id FROM words_en
                WHERE freq_rank IS NOT NULL
                ORDER BY freq_rank
                LIMIT ? OFFSET ?
            )
            ORDER BY en.freq_rank, de.text
            """,
            (limit, offset),
        ).fetchall()
    finally:
        connection.close()

    # Several German words can translate one English word; they all count as correct.
    words: dict[str, dict] = {}
    for row in rows:
        entry = words.setdefault(row["english"], {"source": row["english"], "answers": []})
        entry["answers"].append(row["german"])

        # The hint shows the article and gender but never the word itself — spelling out
        # "das Haus" under the prompt "house" would hand the user the answer.
        if len(entry["answers"]) == 1 and row["gender"] in ARTICLES:
            entry["hint"] = f"{ARTICLES[row['gender']]} … ({row['gender']})"

    payload = {
        "id": set_id or f"en-de-top{limit}",
        "name": name or f"German top {limit}",
        "sourceLanguage": "en",
        "targetLanguage": "de",
        "words": list(words.values()),
    }

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    return len(payload["words"])
