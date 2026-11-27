# Localisation: the string audit, the freeze, a pseudo-locale, chronicle grammar, and translations

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §2.7 (strings through `tr()` from Phase 0; generated chronicle text uses localisable templates), §3.6 (flavour lines from localisable templates), §5 Phase 8 (localisation, including generated chronicle text).
Carried in: about 95 `wm_` strings against a budget of 40 (phase-4); the human read of the English strings (Phases 4 and 5, waiting on the user). Decision D6 (languages at 1.0).

## 1. Goals
1. Every player-facing string is a key, and the game runs end to end in a pseudo-locale with nothing English left.
2. Generated text (chronicle, flavour, rumours, epilogues) can agree in gender and number, and choose word forms, in any language.
3. If D6 names languages, they ship complete at 1.0; if not, adding one later is a file and a font.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Game/localization/en.csv` (about 7,600 rows, RFC 4180, checked by `SelfTest.CsvChecks`) | **extended:** a `context` column for translators; `xx.csv` (pseudo); one CSV a shipped language |
| chronicle, flavour and rumour templates with `{tokens}` | **extended:** token forms and select forms (3.3) |
| one bundled OFL pixel font (Latin) | **extended:** fallbacks per script as D6 needs |

## 3. Rules

### 3.1 The audit and freeze (LC-1)
- **The audit:** a scan of the Game and content for text that reaches the screen without a key (literals in `Label.Text`, `string.Format` of English, enum names). Each is fixed or listed as allowed (debug only).
- **Budgets:** each screen's string family keeps its budget; the watch family (`wm_`) is cut from about 95 to 40 or its budget is raised with a reason.
- **The freeze** (about month 8.3): after it, a new or changed English row needs a reason in the commit and goes into the next batch.
- **The human read** of the English rows happens before the freeze (waiting on the user).
- **Context:** every row gets a short note for translators (where it shows, what its tokens are, its length limit in pixels at 1x).

### 3.2 The pseudo-locale
- `xx` is generated from English: accented letters, brackets at both ends, and **35% longer** text.
- CI runs the `--autoplay` screenshot pass in `xx` at 960x540 and 1280x800. A string that is clipped or still plain English fails.

### 3.3 Grammar in templates (LC-2)
- **Token forms:** `{person}` takes forms the language defines, for example `{person.gen}` (the possessive). Each language file defines its forms for names from the generated name tables (a suffix rule by ending, plus exceptions).
- **Select forms:** `{person.gender|m:...|f:...}` and `{count.plural|one:...|few:...|many:...|other:...}` with the CLDR plural categories for each language.
- **Authoring rule for English** (so translators can follow): put names in positions that need no inflection where possible; never build a sentence from two half-sentence keys.
- Templates are checked: every token resolves, every select has an `other`, every language's plural set matches CLDR's.

### 3.4 Fonts
- The bundled pixel font covers Latin, Latin Extended-A and the punctuation the pseudo-locale uses.
- A language in another script (for example Simplified Chinese) needs an OFL pixel font with that coverage at 12 and 16 px, shipped with its licence. The check lists every code point of a CSV that its font lacks.

### 3.5 Translations (LC-3, only if D6 names any)
- **Batches:** batch 1 at about month 5 (every row then), batch 2 at the freeze (the rows changed since), back by about month 9.5.
- A shipped language must be complete: a missing key falls back to English, is counted in the load report, and fails the language's CI check.
- Size: about 90,000 English words now (rows, the Codex, dialogue, templates), about 100,000 at the freeze.

### 3.6 Integer math notes
- Plural categories use integer rules only (CLDR's operands on whole numbers; game amounts are integers).
- Numbers and money are formatted by language rules (digit grouping, the decimal mark) in the Game.

## 4. Saves
None. Chronicle entries store a type and parameters, never text (as today), so a save reads in any language.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| LC-1 | No literals | the audit finds no unkeyed player-facing string outside the allowed list |
| LC-2 | Budgets | every string family within its budget, or a logged reason |
| LC-3 | Pseudo-locale | the screenshot pass in `xx` has no clipped and no English string at 960x540 and 1280x800 |
| LC-4 | Context | every row has a translator note and a pixel limit |
| LC-5 | Tokens | every template token resolves for 1,000 generated chronicle entries in each language |
| LC-6 | Selects | gender and plural selects choose the right branch for each CLDR category of a test language |
| LC-7 | Name forms | a test language's possessive rule gives the expected form for 200 generated names |
| LC-8 | Complete | each shipped language has every key; fallbacks counted as 0 |
| LC-9 | Font coverage | every code point in each shipped CSV is in its font |
| LC-10 | Same save, any language | one save's chronicle, epilogue and Codex read in each shipped language with no missing key |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-LC-1 the audit and freeze | the audit, budgets, context notes, the pseudo-locale in CI, the freeze | LC-1 to LC-4 | 0.6 |
| P8-LC-2 chronicle grammar | token forms, selects, name forms, the template check | LC-5 to LC-7 | 0.6 |
| P8-LC-3 translations | the batches, fonts, completeness (only if D6 names languages) | LC-8 to LC-10 | 0.3 |

LC-2 comes early (step 1) so the epilogue's and the new content's templates are written with its forms.
