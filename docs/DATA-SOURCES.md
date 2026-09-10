# Data sources

## Why not the Wiktionary SQLite dump

The obvious candidate was
[`cstr/de-wiktionary-sqlite-full`](https://huggingface.co/datasets/cstr/de-wiktionary-sqlite-full)
— a "complete, lossless, fully normalized" SQLite of German Wiktionary. It is **4.45 GB
compressed**, because it explodes everything into relational rows: 3M senses, 3M
definitions, 6M inflected forms, 2.3M pronunciations, plus hypernyms, meronyms,
holonyms, descendants and more.

Lexicycle needs almost none of that. For one-to-one pairs the required fields are the
German lemma, its part of speech, its gender, and its English translations.

Its upstream,
[`cstr/de-wiktionary-extracted`](https://huggingface.co/datasets/cstr/de-wiktionary-extracted),
carries **the same wiktextract content in 5 parquet files totalling 287 MB** (971,941
rows) — roughly 15× smaller. Parquet is columnar, so reading only the five columns the
pipeline needs moves far less than even that off disk.

**Decision: the parquet dataset is the pipeline input.** The full SQLite stays a
documented fallback for Phase 5, when example sentences, IPA and inflected forms become
useful (R-502).

Note that the raw MediaWiki dumps are *not* a practical alternative: they are wikitext
plus page-metadata SQL, and extracting structured senses from them means reimplementing
wiktextract.

## Why no English Wiktionary source in v1

German Wiktionary's `translations` field already yields German↔English pairs, usable in
either direction. With 1.1M translations upstream and only a few thousand kept, coverage
of common vocabulary is ample. Adding an English-edition source later is a configuration
change in `sources.py`, not a redesign.

## Upstream row shape

Verified against the live dataset (not assumed):

```jsonc
{
  "word": "Haus",
  "pos": "noun",
  "lang_code": "de",
  "tags": ["neuter"],                       // entry-level grammatical gender
  "translations": [
    { "lang": "Englisch", "lang_code": "en", "word": "house",
      "sense": "...", "sense_index": "1", "uncertain": false }
  ]
}
```

Other columns present but unused in v1: `senses`, `sounds` (IPA, audio), `forms`,
`synonyms`, `categories`, `etymology_texts`, `hyphenations`, `pos_title`, `lang`.

## Extraction rules

Implemented in `src/python/lexicycle_data/model.py`, and covered by tests.

| Rule | Reason |
| --- | --- |
| Keep only `lang_code == "de"` rows | Guards against a mixed dump |
| Keep only translations with `lang_code == "en"` | v1 is the en-de pair |
| Drop `uncertain: true` translations | Wiktionary itself is unsure; poor quiz answers |
| Strip a trailing `(...)` qualifier | `"schadenfreude (loanword)"` → `"schadenfreude"` |
| Reject terms containing `[ ] { } < > \| / ; … "` | In this dataset square brackets wrap glosses. Stripping them would leave a plausible-looking but wrong term — `"malicious joy [at another's misfortune]"` must not become `"malicious joy"` |
| Reject terms of more than three words | Those are explanations, not vocabulary |
| Reject terms with no letters | Punctuation fragments |
| Gender from entry-level `tags` | `masculine` / `feminine` / `neuter` |

## Frequency ranking

Wiktionary has no notion of "most used". Its 970k German lemmas skew heavily toward
taxonomy and rare compounds — the first rows of the dataset are `Subfamilia`,
`Subregnum`, `Superphylum`. Ranking the English side by real-world frequency is what
turns the dump into a usable beginner vocabulary.

`wordfreq`'s Zipf scale (~1 vanishingly rare, ~8 commonest) is inverted into an integer
rank. Multi-word terms take the rank of their rarest word, so "ice cream" ranks by
"cream".

## Generated schema

```sql
CREATE TABLE words_en (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE,
                       freq_rank INTEGER);
CREATE TABLE words_de (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE,
                       gender TEXT, pos TEXT);
CREATE TABLE translations (en_id INTEGER NOT NULL REFERENCES words_en(id),
                           de_id INTEGER NOT NULL REFERENCES words_de(id),
                           PRIMARY KEY (en_id, de_id)) WITHOUT ROWID;
CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
```

Enrichment arrives as nullable columns or side tables, so no breaking migration is
needed. `meta` records `schema_version`, `pair`, `source`, `license` and `top_n`.

## Size

Run `python -m lexicycle_data report` after `download` to fill this in from measurement
rather than estimate. Expect a few hundred KB at 5,000 English words and low single-digit
MB at 50,000 — the tables hold short strings and integer ids and nothing else, so bundle
size is not expected to constrain the choice.

| top-N | EN words | DE words | pairs | size |
| --- | --- | --- | --- | --- |
| _pending first `report` run_ | | | | |

## Licence

Upstream content is Wiktionary, **CC-BY-SA 4.0**. The generated database is a derived
work and carries the same licence — recorded in the `meta` table and to be credited on
the About screen (R-306). The `wiktextract` tooling itself is MIT.
