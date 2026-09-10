"""English word-frequency ranking.

Wiktionary has no notion of "most used words" — its 970k German lemmas are dominated by
taxonomy and rare compounds. Ranking the English side by real-world frequency is what
turns the dump into a useful beginner vocabulary.
"""

from __future__ import annotations

from typing import Callable, Iterable

#: Rank given to a term wordfreq has never seen. Sorts after every known word.
UNKNOWN_RANK = 10**9

RankLookup = Callable[[str], int]


def build_rank_lookup(language: str = "en") -> RankLookup:
    """Return ``term -> rank`` where rank 1 is the most common word.

    Multi-word terms take the rank of their rarest word, so "ice cream" ranks by "cream".
    """
    from wordfreq import zipf_frequency

    def rank(term: str) -> int:
        words = term.lower().split()
        if not words:
            return UNKNOWN_RANK

        zipf = min(zipf_frequency(word, language) for word in words)
        if zipf <= 0:
            return UNKNOWN_RANK

        # Zipf runs ~1 (vanishingly rare) to ~8 (the commonest words). Invert it into a
        # rank-like integer; the exact scale does not matter, only the ordering.
        return int(round((8.0 - zipf) * 1000))

    return rank


def null_rank_lookup() -> RankLookup:
    """A lookup that knows nothing — used by tests so they need no wordfreq install."""
    return lambda _term: UNKNOWN_RANK


def rank_terms(terms: Iterable[str], lookup: RankLookup) -> dict[str, int]:
    return {term: lookup(term) for term in terms}
