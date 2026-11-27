# Endings and the epilogue: ending a run at a great ambition, and the chronicle's last word

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §5 Phase 8 (optional endings, decided with the user: a great ambition offers to end the run with a chronicle epilogue; the player may decline and carry on; the world stays in the library for new characters), §3.7 (Ironman: the run ends only when nobody is left), §3.12 (the chronicle and its causes).
Depends on: the ambition tree (`01-ambitions-and-autopilot.md`), chronicle grammar (`12-localisation.md`, LC-2). Decision D1 sets the number of endings.

## 1. Goals
1. Finishing a great ambition offers an ending. Declining costs nothing and the offer stays.
2. Taking it freezes the run, writes an epilogue from what really happened, and hands the hero to the world.
3. The world stays in the library, and a new character can start in it after the legend.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Worlds/WorldLibrary.cs` (`RunInfo`), `Hero/Succession.cs` (the heir takes over), `Core/Chronicle.cs`, the Chronicle screen | **new:** `Hero/Ending.cs` (offer and freeze), `Chronicle/Epilogue.cs` (beats), content `endings/endings.jsonc` and `endings/beats.jsonc`, the Godot `UI/EpilogueScreen.cs` |

## 3. Rules

### 3.1 The offer
- When a great ambition is done, the game pauses and offers: **"End here"** or **"Carry on"**. The `EndingOffered` interrupt is always "pause", even on autopilot.
- "Carry on" keeps the offer in the Journal for the rest of this hero's life. It ends if the hero dies or the condition stops holding for 30 game days (a lost crown). An heir must earn their own.
- Ironman: the same. Taking an ending is final for the run.

### 3.2 Taking an ending
1. At the next tick barrier the sim stops. A last save is written to the run's slot, marked `ended` (read-only in the load list).
2. The epilogue is built (3.3) on a worker, within 2 s.
3. **The hero passes to the world.** The hero's person becomes an ordinary person run by the NPC AI. Property, offices, family and debts stay theirs. Companions leave the party as ordinary people.
4. The library entry gains the ending: its id, the day, the hero and the epilogue's beats.
5. **A new character in that world** can start "from the first day" (the world as generated) or "after the legend" (the ended state, with the old hero as an NPC). Default: after the legend.

### 3.3 The epilogue
Built only from the chronicle and the state at the end. Nothing is projected forward.
- **Required beats, in order:**
  1. origin and first ambition;
  2. each done ambition with its date and the cause of its last step;
  3. the 3 to 6 heaviest entries that name the hero (battles, offices, marriages, crimes seen, deaths of kin), by the chronicle weight;
  4. companions: alive, dead, or parted, with the cause;
  5. the heirs and holdings at the end;
  6. the ending's own beat ("The crown passed to...", "The barrow was quiet after...").
- **"What became of" lines** read the end state only: realm stability (unrest rung), the heir's age, the holdings' last-season profit, the hero's standing with each group.
- Each beat has 2 to 4 template variants. One is chosen by 0x810 keyed on the run id and the beat index, so the same run always reads the same.
- About 8 to 14 paragraphs. Each paragraph links to its chronicle entries.

### 3.4 Endings (D1 recommends six)
| Id | Great ambition | Its beat names |
|---|---|---|
| `dynasty` | found a dynasty: two successions of a titled fief in the hero's blood | the line and its seat |
| `crown` | take a crown: rule a realm | the realm and how it was won |
| `realm` | found a realm: your faction recognised by 2 realms | its first law and its border |
| `legend` | slay a legend: kill a monster in a tier-3 lair | the lair, the monster and the dead |
| `merchant_prince` | an L3 works, a guild office, and wealth of 10,000,000 mites (10,000 gp) | the works and the trade route |
| `holy_life` | an abbot's or bishop's office and a true relic kept | the house and the relic |

### 3.5 Integer math notes
- Entry weight is the chronicle's integer weight; ties break by entry id.
- Wealth in the ending conditions is `long` mites, as AT 3.5.

## 4. Saves
- **New section `ending`** (only in an ended save): `{ EndingId, Day, HeroPerson, Beats: [{ Kind, Variant, Entries }] }`. The text is rebuilt from beats in the current language, so an epilogue reads in whatever language is set.
- **Additive key** in the hero section: `EndedAt` (the day, or absent).
- `WorldLibrary.RunInfo` gains `EndingId` and `EndDay` (optional).

## 5. UI and strings
- The offer dialog; the Epilogue screen (a scroll of pixel panels, the hero's portrait, links into the Chronicle); "Save as text" writes `user://epilogues/<world>-<hero>.txt`.
- The library's world card shows its legends (ended runs) by name.
- About 120 strings (`ep_*`), every one a template through LC-2's grammar.

## 6. Golden tests
| Id | Name | Asserts |
|---|---|---|
| EN-1 | Offer | finishing a great ambition pauses with the offer, on autopilot too |
| EN-2 | Decline | "Carry on" keeps the offer; it ends after 30 days without the condition |
| EN-3 | Freeze | taking it writes the ended save at a barrier; that save loads read-only |
| EN-4 | Beats | a scripted run's epilogue has every required beat in order, each with entry links |
| EN-5 | Same run, same words | two builds of the same ended save give identical beat variants |
| EN-6 | Hero to the world | after the ending the hero's person is run by the NPC AI, keeps every deed and office; coin, mass and people exact |
| EN-7 | After the legend | a new character in that world finds the old hero as an NPC with the same id |
| EN-8 | From the first day | the same world started fresh has the generated hashes unchanged |
| EN-9 | Language | the epilogue rebuilt in the pseudo-locale has no English left and no missing key |
| EN-10 | Export | the text export matches the screen's paragraphs |

## 7. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-EN-1 endings | the offer, freeze, the hero to the world, the beats, the six endings | EN-1 to EN-6 | 0.8 |
| P8-EN-2 the screen and library | the Epilogue screen, the library's legends, after the legend, the export | EN-7 to EN-10 | 0.4 |

## 8. Technical defaults
- An ended run cannot be continued as the same hero. A new character is a new run.
- Achievements for endings (RL, D10) read the `ending` section only.
