# Data sources

## The source: English Wiktionary via kaikki.org

Lexicycle asks "what is the German for *house*?", so it needs **English lemmas as
headwords with German translations attached**. The English Wiktionary edition, extracted
by [wiktextract](https://github.com/tatuylonen/wiktextract) and published by
[kaikki.org](https://kaikki.org/dictionary/English/), is exactly that shape:

```jsonc
{
  "word": "furniture",
  "pos": "noun",
  "lang_code": "en",
  "translations": [
    { "lang": "German", "code": "de", "lang_code": "de",
      "sense": "large movable items", "tags": ["neuter"], "word": "Möbel" },
    { "lang": "German", "code": "de", "sense": "large movable items",
      "tags": ["neuter"], "word": "Mobiliar" },
    { "lang": "German", "code": "de", "sense": "large movable items",
      "tags": ["masculine"], "word": "Einrichtungsgegenstand" }
  ]
}
```

Everything needed is present and clean: whole-word translations, **gender on the
translation itself**, and a `sense` gloss that separates meanings.

The full extract is ~3.2 GB of JSONL. The `download` step streams it once and writes a
distilled, gzipped file holding only entries that have a translation into one of
`sources.TARGET_LANGUAGES` (German and Spanish, by default) and only the fields the
pipeline uses — a small fraction of the size. Nothing large is stored, and extraction
rules can be re-tuned offline without re-fetching.

Every configured language is captured in the same pass, so a second pair is **only** a
`build --pair en-es` away — it never means downloading the 3.2 GB extract again. See
"Adding a language pair" below.

## Why the German edition was rejected

This was tried first and the data disproved it. Recording it so it is not retried.

The candidates were
[`cstr/de-wiktionary-sqlite-full`](https://huggingface.co/datasets/cstr/de-wiktionary-sqlite-full)
(4.45 GB SQLite) and its upstream
[`cstr/de-wiktionary-extracted`](https://huggingface.co/datasets/cstr/de-wiktionary-extracted)
(287 MB parquet, 971,941 rows). The parquet is the same wiktextract content ~15× smaller,
so it was the right choice *between those two* — the SQLite is large only because it
explodes out 6M inflected forms, 3M definitions and 2.3M pronunciations.

The plan then assumed German Wiktionary's `translations` field would yield en↔de pairs
usable in either direction. **It does not.** In the German edition, wiktextract shatters
multi-word English translations into separate word-level entries:

| German lemma | Intended English | What the data actually contains |
| --- | --- | --- |
| `Möbel` | "piece of furniture" | `item`, `piece`, `of`, `furniture` |
| `Molle` | "glass of beer" | `glass`, `of`, `beer` |
| `Ausschank` | "selling of alcoholic drink" | `selling`, `of`, `alcoholic drink` |

This is fatal, not merely noisy. The fragments (`of`, `piece`, `glass`) are
**indistinguishable from genuine one-word translations**, so inverting the mapping
produces English function words as headwords with unrelated German answers:

```
the → fernmündlich | je | zeichnen        is → weihnachten
of  → Ausschank | Molle | Möbel           it → Fangen | haschen
```

Filtering cannot recover it. Part-of-speech filters, an English stopword list and a cap
on answers-per-prompt were all tried; the best combination discarded 89% of pairs and
still produced `their → prägen` and `made → fimschig`.

The German edition remains a reasonable source for **German→English** study material or
for German-side enrichment (IPA, inflected forms, examples) in Phase 5, where the
fragmentation of the English side does not matter.

Raw MediaWiki dumps are not a practical alternative either: they are wikitext plus
page-metadata SQL, and extracting structured senses means reimplementing wiktextract.

## Extraction rules

Implemented in `src/python/lexicycle_data/model.py`, and covered by tests.

| Rule | Reason |
| --- | --- |
| Keep only `lang_code == "en"` rows | Guards against a mixed dump |
| Keep only translations whose `code`/`lang_code` matches the pair being built | A distilled row can carry several target languages' translations together; `build --pair en-es` and `build --pair en-de` read the same file and each keeps only its own |
| Keep only `pos` in noun / verb / adj / adv | `the → der \| die \| das` is not vocabulary worth drilling |
| Keep only the **primary sense's** translations | "run" carries 41 German translations across dozens of senses; accepting all of them makes the question meaningless |
| Reject multi-word English prompts | Wiktionary headwords include phrases ("as in", "what if") that are not vocabulary. Costs us genuine phrasal verbs too — see R-509 |
| Reject English function words by stoplist | They arrive as adverbs and nouns, but `in → herein` and `that → dermaßen` are grammar, not vocabulary |
| Drop translations tagged obsolete, archaic, rare, dated, misspelling, nonstandard | Poor answers to require |
| Drop translations tagged with a **regional variant** (Alemannic, Swiss, Bavarian, Palatine, Rhine-Franconian, Low German, dialectal, colloquial, slang) | Wiktionary lists dialect forms beside the standard word, so "time" otherwise collects Zeit, Zit, Ziit and zeid as equally valid |
| Reject terms with a leading/trailing `-` or containing `...` | `"-ste"` and `"am ...-sten"` are endings, not words |
| Order answers by **target-language** frequency and keep the best 4, dropping any far rarer than the best | Source order does not put the standard word first — "love" offers `Liab` before `Liebe` — so frequency, not position, picks the answer shown to the learner |
| Strip a trailing `(...)` qualifier | `"Säbel (curved)"` → `"Säbel"` |
| Reject terms containing `[ ] { } < > \| / ; … "` | Square brackets wrap glosses. Stripping them would leave a plausible-looking but wrong term |
| Reject terms of more than three words | Those are explanations, not vocabulary. The limit still admits "sich freuen" and separable verbs |
| Reject terms with no letters | Punctuation fragments |
| Gender from the translation's own `tags` | `masculine` / `feminine` / `neuter` |

## Frequency ranking

Wiktionary has no notion of "most used", and its headword list skews heavily toward rare
and technical entries. Ranking the English side by real-world frequency is what turns the
dump into a usable beginner vocabulary.

`wordfreq`'s Zipf scale (~1 vanishingly rare, ~8 commonest) is inverted into an integer
score. Multi-word terms take the score of their rarest word, so "ice cream" ranks by
"cream".

> **`freq_rank` is not a rank.** The column name is a lie inherited from an early draft.
> `frequency.py` stores `round((8 − zipf) × 1000)`, so on the shipped dictionary it runs
> from **1,590 to 6,990** with only about **480 distinct values across 3,545 words** —
> heavily tied, never 1..N. Lower still means more common, so `ORDER BY freq_rank` is
> correct, but *filtering* on it as though it were an ordinal is not: `freq_rank BETWEEN 1
> AND 1000` matches nothing at all. Anything positional — "the 1,000 most common words" —
> must use `ORDER BY freq_rank, id` with `LIMIT`/`OFFSET`, and needs the `id` tiebreak
> because the ties would otherwise make the ordering unstable. `FrequencyBand` does this;
> see `SqliteDictionaryStore.BandWindow`.

## Generated schema

One database holds one language pair. The target-language table and its half of the join
table are named after the language code, so `words_de`/`de_id` for German and
`words_es`/`es_id` for Spanish — `database.schema_for(target_language)` generates it:

```sql
CREATE TABLE words_en (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE,
                       pos TEXT, freq_rank INTEGER);
CREATE TABLE words_<lang> (id INTEGER PRIMARY KEY, text TEXT NOT NULL UNIQUE,
                           gender TEXT);
CREATE TABLE translations (en_id INTEGER NOT NULL REFERENCES words_en(id),
                           <lang>_id INTEGER NOT NULL REFERENCES words_<lang>(id),
                           PRIMARY KEY (en_id, <lang>_id)) WITHOUT ROWID;
CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
```

`schema_version` is **2**; version 1 was the abandoned German-edition layout, which
carried `pos` on `words_de`. Enrichment arrives as nullable columns or side tables, so no
breaking migration is needed. Adding a language is a second *database file*, never a
wider schema — see below.

## Adding a language pair

The pipeline is pair-parameterised by design; English-Spanish shipping alongside
English-German is a configuration change, not a redesign:

```bash
python -m lexicycle_data download                   # captures every TARGET_LANGUAGES code
python -m lexicycle_data build --pair en-es --top-n 0
cp data/dist/lexicycle-dict-en-es.db src/Lexicycle/LexicycleApp/Resources/Raw/
```

`download` only needs re-running if `sources.TARGET_LANGUAGES` gains a code that an
existing distilled file does not carry — the default already includes `de` and `es`, so
most of the time `build --pair en-es` alone is enough against a distilled file fetched
for German. The app's `LanguagePair.All` (`LexicycleCore/Dictionary/LanguagePair.cs`)
needs the new pair added to the list; `AppDatabases` then copies in whichever bundled
`.db` files it finds and the home screen's language switcher appears automatically once
there is more than one.

A wholly new **third** language needs one addition: its code in
`sources.TARGET_LANGUAGES`, so the next `download` captures it. Everything downstream —
`model.py`, `database.py`, `SqliteDictionaryStore` — already takes the target language as
a parameter rather than assuming German.

## Size

Measured with `python -m lexicycle_data report --pair en-de`. The extract holds
1,492,836 rows, of which **3,775 English lemmas survive extraction** with at least one
German translation.

| top-N | EN words | DE words | pairs | size |
| --- | --- | --- | --- | --- |
| 1,000 | 1,000 | 1,537 | 1,649 | 184 KB |
| 5,000 | 3,648 | 5,126 | 5,517 | 536 KB |
| all | 3,648 | 5,126 | 5,517 | **536 KB** |

The whole dictionary is 536 KB, so the top-N cut is moot — ship all of it. Size was never
the binding constraint; quality was. The **en-es** dictionary, generated the same way, is
3,655 English words / 5,001 Spanish words / 5,367 pairs at 528 KB — run `report --pair
en-es` for the full breakdown by top-N.

Note that only ~5,100 English entries carry a translations table at all. Wiktionary
attaches translations to a fraction of its headwords, and the extraction rules below then
remove roughly a quarter of those. ~3,650 drillable words is a solid beginner-to-
intermediate vocabulary, not a comprehensive dictionary. These counts drift a little
between runs — kaikki.org's extract is refreshed periodically, so a re-`download` is
never byte-identical to the last one.

## Known limitations

Honest about what the data still gets wrong, so nobody re-discovers it:

- **Primary-sense selection is only as good as Wiktionary's sense order.** `go → machen`
  and `give → nachgeben` are both first-sense artefacts; the obvious answers are `gehen`
  and `geben`. Fixing this needs sense ranking rather than "take the first".
- **Untagged dialect forms survive when they are common enough.** German frequency
  ordering removes `Liab`, `Ziit` and `kemma`, but `home → Ham | Heim | …` still leads
  with a regionalism because `Ham` scores as a real German word.
- **Multi-word prompts are excluded entirely in v1** (R-509), which loses genuine phrasal
  verbs like "get in" and "make up" along with the junk like "as in" and "what if".
- Some noun senses leak into verb entries: `feel → Haptik | Oberflächenbeschaffenheit`.

Spot-check generated pairs before bundling them (R-307). The rejected German edition
looked perfectly healthy by row count.

## Licence

Upstream content is Wiktionary, **CC-BY-SA 4.0**. The generated database is a derived
work and carries the same licence — recorded in the `meta` table and to be credited on
the About screen (R-306). The `wiktextract` tooling itself is MIT.

## Fetching notes

`download` streams over HTTPS. If it fails with `CERTIFICATE_VERIFY_FAILED`, see the
`truststore` note in `src/python/README.md` — locally installed TLS-inspecting software
(on this developer's machine, Avast) signs traffic with a root CA that Python's bundled
`certifi` list does not carry.
