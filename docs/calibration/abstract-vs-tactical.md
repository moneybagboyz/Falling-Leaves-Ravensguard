# Abstract battles against the live fight (abstract-battles-v1 4.3, 4.4)

Made with `dotnet run --project tools/Ravensguard.Calibrate -c Release -- --abstract equiv --seeds 200 --seed0 20000` on the shipped content (wound tables hash `B8342AD2275C1A22`,
1,078 cells x 4,000 attack actions, 5,531 recorded wound sets). The constants were fitted on seeds 1000 on (`--abstract fit`); seeds 20000 to 20199 are fresh. Casualties are the dead and
the down at the end of the fight; "fled" counts the fled and those still running; the wound mix is each casualty's worst blow by the tables' classifier (no blow lands "light" under
the v2 resolver: a cut is pain 20 or more); "bled out after" is the share of casualties dead after two minutes untreated. The tactical rows are stored in
`tests/.../Combat/Abstract/TacticalReference.cs` for the fast AB-3 test.

| Scenario | n | casualties 0 | casualties 1 | fled 0 | fled 1 | side 0 wins | mean s | mix light/serious/grave/severed (pm) | bled out after |
|---|---|---|---|---|---|---|---|---|---|
| A tactical | 200 | 3.81 | 2.03 | 0.9 | 1.1 | 15.0% | 14.2 | 0 / 177 / 793 / 29 | 46.8% |
| A abstract | 200 | 3.92 | 2.62 | 1.9 | 2.7 | 18.5% | 14.4 | 0 / 178 / 762 / 58 | 46.9% |
| B tactical | 200 | 4.08 | 3.43 | 3.6 | 7.7 | 52.5% | 10.5 | 0 / 247 / 668 / 84 | 45.2% |
| B abstract | 200 | 3.58 | 5.52 | 4.0 | 12.8 | 50.0% | 16.6 | 0 / 254 / 726 / 19 | 39.3% |
| C tactical | 200 | 3.96 | 9.30 | 1.5 | 13.0 | 96.0% | 31.5 | 0 / 90 / 569 / 339 | 33.6% |
| C abstract | 200 | 1.31 | 9.23 | 2.1 | 4.9 | 99.5% | 22.2 | 0 / 186 / 637 / 175 | 42.9% |
| D tactical | 200 | 0.09 | 7.53 | 0.0 | 0.8 | 100.0% | 48.9 | 0 / 549 / 401 / 49 | 23.0% |
| D abstract | 200 | 0.01 | 10.01 | 0.0 | 0.0 | 100.0% | 43.3 | 0 / 334 / 594 / 70 | 41.6% |
| D on mud tactical | 200 | 4.89 | 3.18 | 2.6 | 3.1 | 25.0% | 151.0 | 0 / 186 / 723 / 89 | 11.8% |
| D on mud abstract | 200 | 4.06 | 4.43 | 0.0 | 0.4 | 23.0% | 106.3 | 0 / 251 / 689 / 58 | 21.6% |
| A flanked tactical | 200 | 4.12 | 4.54 | 1.5 | 2.7 | 41.5% | 18.5 | 0 / 206 / 754 / 38 | 42.4% |
| A flanked abstract | 200 | 1.46 | 4.81 | 0.9 | 0.1 | 96.5% | 14.8 | 0 / 204 / 700 / 95 | 41.5% |
| A without leaders tactical | 200 | 2.61 | 1.51 | 1.4 | 1.8 | 13.0% | 14.7 | 0 / 162 / 805 / 31 | 45.3% |
| A without leaders abstract | 200 | 2.63 | 1.94 | 2.0 | 3.6 | 17.0% | 14.7 | 0 / 107 / 814 / 77 | 48.6% |

First pass of the charge (MT-12's measure: the foot downed or killed by blows in the 10 s from the first wound): live 1.31 dry, 0.41 on mud; abstract 1.31 dry, 0.14 on mud.

Inside the 4.4 gate: A (but the bandits' casualties, +29%), A without leaders (but two rare wound-mix shares), B's win rate (50.0 against 52.5) and its own side's casualties,
C's win rate and the raiders' casualties (9.23 against 9.30), D's win rate, duration and the riders' casualties, mud's win rate. Outside it (17 of the 45 comparisons of the five gate
scenarios): B's levy casualties (+61%) and length (+58%), C's archer-and-spear line (1.31 against 3.96 casualties: the live raiders swamp the ten spears before they break; the
abstract spears hold) and length, D's foot casualties (+33%) and the charge's wound mix (live: mostly serious, the trample and the fall; abstract: grave, the lance rows), mud's length
(106 against 151 s: the live riders circle in the mud for minutes), and the rarer mix classes. The flanked variant is the worst: the abstract militia barely feel the six bandits round
their flank. See the Notes at the end of `docs/specs/phase4/abstract-battles-v1.md`.
