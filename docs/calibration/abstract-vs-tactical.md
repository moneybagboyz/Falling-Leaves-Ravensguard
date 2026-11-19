# Abstract battles against the live fight (abstract-battles-v1 4.3, 4.4)

Made with `dotnet run --project tools/Ravensguard.Calibrate -c Release -- --abstract equiv --seeds 200 --seed0 20000` on the shipped content (wound tables hash `3972AD104E0FFE93`,
1,078 cells x 4,000 attack actions, 5,502 recorded wound sets; rebuilt in the Phase 4 close-out for the weapon and armour changes, `charge_pm` 700). The constants were fitted on seeds 1000 on (`--abstract fit`); seeds 20000 to 20199 are fresh. Casualties are the dead and
the down at the end of the fight; "fled" counts the fled and those still running; the wound mix is each casualty's worst blow by the tables' classifier (no blow lands "light" under
the v2 resolver: a cut is pain 20 or more); "bled out after" is the share of casualties dead after two minutes untreated. The tactical rows are stored in
`tests/.../Combat/Abstract/TacticalReference.cs` for the fast AB-3 test.

| Scenario | n | casualties 0 | casualties 1 | fled 0 | fled 1 | side 0 wins | mean s | mix light/serious/grave/severed (pm) | bled out after |
|---|---|---|---|---|---|---|---|---|---|
| A tactical | 200 | 3.46 | 2.57 | 1.0 | 1.5 | 25.5% | 14.3 | 0 / 199 / 765 / 34 | 43.5% |
| A abstract | 200 | 3.78 | 2.60 | 1.7 | 2.6 | 23.5% | 14.0 | 0 / 215 / 752 / 31 | 42.7% |
| B tactical | 200 | 3.91 | 3.56 | 4.1 | 6.6 | 58.5% | 10.2 | 0 / 280 / 663 / 55 | 44.2% |
| B abstract | 200 | 2.77 | 6.85 | 4.3 | 10.2 | 73.0% | 19.1 | 0 / 261 / 733 / 5 | 40.9% |
| C tactical | 200 | 3.92 | 10.19 | 1.9 | 12.3 | 94.0% | 26.0 | 0 / 89 / 574 / 336 | 39.2% |
| C abstract | 200 | 1.43 | 9.23 | 2.2 | 5.1 | 99.5% | 22.6 | 0 / 203 / 632 / 163 | 42.3% |
| D tactical | 200 | 0.10 | 7.72 | 0.0 | 0.7 | 100.0% | 48.3 | 0 / 547 / 402 / 49 | 23.3% |
| D abstract | 200 | 0.02 | 10.26 | 0.0 | 0.0 | 100.0% | 42.7 | 0 / 343 / 602 / 54 | 40.0% |
| D on mud tactical | 200 | 5.08 | 3.22 | 2.8 | 3.2 | 23.0% | 150.4 | 0 / 188 / 715 / 95 | 13.6% |
| D on mud abstract | 200 | 4.21 | 3.65 | 0.0 | 0.8 | 19.5% | 100.7 | 0 / 284 / 663 / 52 | 23.8% |
| A flanked tactical | 200 | 3.77 | 5.25 | 1.6 | 2.5 | 60.0% | 18.4 | 0 / 226 / 738 / 34 | 43.3% |
| A flanked abstract | 200 | 1.26 | 4.92 | 0.6 | 0.1 | 98.0% | 13.8 | 0 / 320 / 637 / 42 | 33.7% |
| A without leaders tactical | 200 | 2.61 | 2.06 | 1.9 | 2.4 | 28.0% | 18.7 | 0 / 178 / 783 / 38 | 41.3% |
| A without leaders abstract | 200 | 2.60 | 1.67 | 2.4 | 4.2 | 21.5% | 15.1 | 0 / 150 / 808 / 41 | 46.5% |

First pass of the charge (MT-12's measure: the foot downed or killed by blows in the 10 s from the first wound): live 1.26 dry, 0.41 on mud; abstract 1.31 dry, 0.07 on mud
(with `charge_pm` 1,000 the rebuilt tables gave 1.50; 700 brings it back inside MT-12's 15%).

After the Phase 4 close-out (the tables rebuilt for the weapon and armour changes, the live fights refought): still **17 of the 45 comparisons** of the five gate scenarios outside
the 4.4 gate, the same count as before, but not the same ones. Inside it now: all of A, C's win rate, raiders' casualties and length (the archers nock as they back off, which
shortened the live fight to 26 s), D's win rate, length and the riders' casualties, mud's win rate and the bandits' casualties. Outside it: six of B's nine
(the abstract men-at-arms win 73% against the live 58.5%, lose 2.8 against 3.9 and take 19 s against 10: the live levy's spear, faster and countered inside its reach, and the
abstract tables, where a point now skids off a helm, moved apart), C's archer-and-spear line (1.43 against 3.92 casualties) and three mix classes, D's foot casualties (+33%) and two
mix classes, mud's length (101 against 150 s), the militia's casualties there and two mix classes. The flanked variant is still the worst: the abstract militia barely feel the six
bandits round their flank. See the Notes at the end of `docs/specs/phase4/abstract-battles-v1.md`.

Before the close-out (tables `B8342AD2275C1A22`): inside the gate A (but the bandits' casualties, +29%), A without leaders (but two rare wound-mix shares), B's win rate (50.0 against
52.5) and its own side's casualties, C's win rate and the raiders' casualties (9.23 against 9.30), D's win rate, duration and the riders' casualties, mud's win rate; outside it B's
levy casualties (+61%) and length (+58%), C's line (1.31 against 3.96) and length, D's foot casualties and the charge's wound mix, mud's length (106 against 151 s) and the rarer
mix classes.
