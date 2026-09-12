"""The narrow record the pipeline cares about, and the rules for extracting it.

Upstream rows carry 20+ fields (senses, IPA, inflected forms, etymology, derived terms,
descendants). Lexicycle v1 needs four things: the English lemma, its part of speech, its
German translations, and their gender. Everything else is dropped, which is what keeps
the generated database small.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Iterable, Iterator

#: Gender tags wiktextract puts on a translation, in the order we prefer them.
GENDERS = ("masculine", "feminine", "neuter")

#: Translation tags that mean "this is not the plain dictionary form".
#: The regional ones matter: Wiktionary lists dialect forms alongside the standard word,
#: so "time" otherwise collects Zeit, Zit, Ziit and zeid as equally valid answers.
REJECTED_TAGS = frozenset(
    {
        "obsolete", "archaic", "rare", "dated", "misspelling", "nonstandard",
        "alemannic-german", "swiss", "swiss-german", "austrian", "bavarian",
        "palatine", "rhine-franconian", "low-german", "silesian", "regional",
        "dialectal", "colloquial", "slang", "vulgar",
    }
)

#: Parts of speech worth drilling. Function words make poor vocabulary questions.
CONTENT_POS = frozenset({"noun", "verb", "adj", "adv"})

#: English function words. They reach this point as adverbs or nouns, but "in → herein"
#: and "that → dermaßen" are grammar, not vocabulary.
STOPWORDS = frozenset(
    """
    a an the this that these those and or but if then than as of in on at to for with by
    from into onto up down out off over under above below is are was were be been being
    am do does did done have has had will would shall should can could may might must
    not no nor so such it its he she they them him her his hers you your yours we us our
    ours i me my mine one two there here when where why how what which who whom whose
    all any both each few more most other some only just very too also even still yet
    again once ever never always about after before during while until since though
    because else otherwise however therefore thus hence rather quite
    """.split()
)

#: Fragments that are morphology rather than words, e.g. "-ste", "am ...-sten".
_NOT_A_WORD = re.compile(r"(^-)|(-$)|(\.\.\.)")

#: Trailing round-bracket qualifier, e.g. "Haus (Gebäude)". Safe to drop.
_PARENTHETICAL = re.compile(r"\s*\([^)]*\)\s*$")

#: Anything that signals a gloss or fragment rather than a plain term.
_REJECTED_CHARACTERS = frozenset("[]{}<>|/;…\"")

#: Most vocabulary is one word; German compounds are one word by construction. Allowing
#: a little more room keeps "sich freuen" and separable verbs.
_MAX_WORDS = 3


@dataclass(frozen=True)
class Entry:
    """One English lemma with the target-language words it translates to."""

    word: str
    pos: str | None = None
    translations: tuple[tuple[str, str | None], ...] = ()
    """Pairs of (term, gender or None), in upstream order."""


def clean_term(raw: Any) -> str | None:
    """Normalise one term, or return None when it is not usable in a word pair."""
    if not isinstance(raw, str):
        return None

    term = _PARENTHETICAL.sub("", raw).strip()
    term = " ".join(term.split())

    if not term:
        return None

    if any(character in _REJECTED_CHARACTERS for character in term):
        return None

    if _NOT_A_WORD.search(term):
        return None

    if len(term.split()) > _MAX_WORDS:
        return None

    if not any(character.isalpha() for character in term):
        return None

    return term


def is_drillable_prompt(word: str) -> bool:
    """Whether an English lemma is worth asking as a question.

    Single words only in v1. Wiktionary's English headwords include plenty of phrases
    ("as in", "what if") that are not vocabulary, and separating those from genuine
    phrasal verbs ("get in", "make up") needs more than a word count — see R-509.
    """
    if " " in word:
        return False

    return word.lower() not in STOPWORDS


def extract_gender(tags: Any) -> str | None:
    """Pick the grammatical gender out of a translation's tag list."""
    if not tags:
        return None

    tag_set = {str(tag).lower() for tag in tags}
    for gender in GENDERS:
        if gender in tag_set:
            return gender

    return None


def _is_rejected(tags: Any) -> bool:
    if not tags:
        return False

    return bool({str(tag).lower() for tag in tags} & REJECTED_TAGS)


def extract_translations(
    translations: Any, language_code: str
) -> tuple[tuple[str, str | None], ...]:
    """Terms in ``language_code`` from **every** sense, de-duplicated, order preserved.

    One English word often means several different things, and each meaning has its own
    target-language word: "run" is `laufen`/`rennen` but also `fließen`, "drop" is
    `fallen` but also `abnehmen`. All of them are correct answers to the bare prompt, so
    all of them are candidates here.

    Nothing is truncated at this stage. `database.build_database` ranks the candidates by
    real-world frequency and keeps the best few, which is what stops a word with a long
    tail of obscure senses from collecting meaningless answers — frequency decides that,
    not the order Wiktionary happens to list senses in.
    """
    if not translations:
        return ()

    found: dict[str, str | None] = {}

    for translation in translations:
        if not isinstance(translation, dict):
            continue

        code = translation.get("code") or translation.get("lang_code")
        if code and code != language_code:
            continue

        tags = translation.get("tags")
        if _is_rejected(tags):
            continue

        term = clean_term(translation.get("word"))
        if not term:
            continue

        gender = extract_gender(tags)

        # First occurrence wins, but fill in a gender discovered on a later duplicate.
        # The same word often appears under several senses ("laufen" for both "to move
        # quickly" and "to move quickly on two feet"); it is one answer, not two.
        if term not in found or (found[term] is None and gender is not None):
            found[term] = gender

    return tuple(found.items())


def row_to_entry(row: dict[str, Any], language_code: str) -> Entry | None:
    """Convert one upstream row for one target language, or None when it yields nothing."""
    word = clean_term(row.get("word"))
    if not word or not is_drillable_prompt(word):
        return None

    lang_code = row.get("lang_code")
    if lang_code and lang_code != "en":
        return None

    pos = row.get("pos") or None
    if pos not in CONTENT_POS:
        return None

    translations = extract_translations(row.get("translations"), language_code)
    if not translations:
        return None

    return Entry(word=word, pos=pos, translations=translations)


def rows_to_entries(rows: Iterable[dict[str, Any]], language_code: str) -> Iterator[Entry]:
    """Filter and convert a stream of upstream rows for one target language."""
    for row in rows:
        entry = row_to_entry(row, language_code)
        if entry is not None:
            yield entry
