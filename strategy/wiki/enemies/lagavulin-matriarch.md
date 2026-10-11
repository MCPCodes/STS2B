# Lagavulin Matriarch
Underdocks boss, 222 HP: sleeps 3 turns behind 12 Block; awake Slash 19, 9x2, 12 + 12 Block, then -2 Strength and -2 Dexterity on you.

## Facts (game files, v0.107.1)

Numbers are for ascension 0. "(asc: n)" is the higher-ascension value: HP and the Block from Slash with Block from ascension 8, damage from ascension 9. Plating 12, Asleep 3 and Soul Siphon's numbers are the same at every ascension.

Where: boss fight Lagavulin Matriarch [LagavulinMatriarchBoss], act 1 Underdocks, one of its 3 bosses. Alone, enemy e0. Nothing joins. No randomness at all.

HP: 222 (asc: 233).

Co-op: HP x players x 1.1, rounded down (2 players: 488). Plating is 36 with 2 players and loses 2 a round instead of 1, so her Block on your turns 1-3 is 36, 36, 34. Asleep stays 3. The Block from Slash with Block is 26 with 2 players. Attacks and Soul Siphon hit every player. (Read from the code, not seen in a fight.)

Starts with Plating 12 and Asleep 3. She has 12 Block on your turn 1.

Plating 12:
- It gives her Block equal to its amount before your turn 1 and again at the end of every enemy turn. Her Block is wiped at the start of every enemy turn. So while she sleeps she starts each of your turns with exactly the Plating amount of Block, whatever you did to her Block the turn before.
- It loses 1 at the start of every enemy turn except the first.
- It is removed when she wakes, by damage or by herself. Awake she has no Plating.

Asleep 3:
- The amount is the number of enemy turns she still sleeps. It loses 1 at the end of every enemy turn. At 0 she wakes by herself: no stun, no Plating, no Block.
- Woken by damage: any damage instance that removes at least 1 HP from her and leaves her alive. Plating is removed at once, she is stunned, the move after the stun is Slash, and Asleep is removed.
- The test is HP lost, not Block broken. A hit her Block absorbs in full does not wake her, even when it takes her last Block.
- Chipping at her Block without breaking through achieves nothing: the Block is back on your next turn.
- Poison wakes her: it ticks at the start of the enemy turn and ignores Block. She is stunned for that same enemy turn and Slashes on the next one. (Read from the code path, not seen in a fight.)

Left alone (read from the code; the one recorded fight woke her on turn 1):

| Your turn | 1 | 2 | 3 | 4 |
| --- | --- | --- | --- | --- |
| Her Block | 12 | 12 | 11 | 0 |
| She shows | `sleep` | `sleep` | `sleep` | `attack 19` |
| Enemy turn | nothing | nothing | nothing | Slash 19 |

Moves:

| Move | Intent shown | Effect |
| --- | --- | --- |
| Sleep | `sleep` | Nothing. |
| Stunned | `stunned` | Nothing. Only when she was woken by damage. |
| Slash | `attack 19` | 1 hit: 19 base (asc: 21) + Strength. |
| Disembowel | `attack 9x2` | 2 hits: 9 base each (asc: 10) + Strength on every hit. |
| Slash with Block | `attack 12`, `defend` | 1 hit: 12 base (asc: 14) + Strength. Then gains 12 Block (asc: 14). The Block stays up through your next turn. The game calls this move Slash too; the name here is this page's. |
| Soul Siphon | `debuff`, `buff` | You get -2 Strength, then -2 Dexterity. Then she gains 2 Strength. No damage. |

Pattern: fixed, no randomness.
- While she has Asleep: Sleep every enemy turn.
- Awake: Slash, Disembowel, Slash with Block, Soul Siphon, repeat. It always starts with Slash.
- Next move: after Slash (`attack` alone) comes Disembowel, after Disembowel comes `attack` + `defend`, after that comes `debuff` + `buff`, after that comes Slash.
- Woken by damage on your turn W (1, 2 or 3): enemy turn W she is stunned, enemy turn W+1 is Slash.
- Waking her on turn 3 costs nothing: the first Slash is on enemy turn 4 either way. Waking her on turn 2 brings all her attacks forward by 1 turn, on turn 1 by 2 turns.

Damage by enemy turn, counted from her first Slash on enemy turn A, when nothing else changes her Strength. A is 4 when she is left alone or woken on turn 3, W + 1 when woken earlier:

| Enemy turn | A | A+1 | A+2 | A+3 | A+4 | A+5 | A+6 | A+7 | A+8 | A+9 | A+10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Move | Slash | Disembowel | Slash with Block | Soul Siphon | Slash | Disembowel | Slash with Block | Soul Siphon | Slash | Disembowel | Slash with Block |
| Damage | 19 | 9x2 = 18 | 12 | 0 | 21 | 11x2 = 22 | 14 | 0 | 23 | 13x2 = 26 | 16 |
| Her Block on your next turn | 0 | 0 | 12 | 0 | 0 | 0 | 12 | 0 | 0 | 0 | 12 |
| Your Strength and Dexterity lost so far | 0 | 0 | 0 | 2 | 2 | 2 | 2 | 4 | 4 | 4 | 4 |

Each 4-turn cycle deals 8 more than the last: 49, 57, 65.

Soul Siphon (numbers read from the code; the recorded fight ended on the turn it was shown):
- The -2 Strength and -2 Dexterity last for the rest of the fight and add up: -4 and -4 after two.
- -2 Strength: every hit of your attacks deals 2 less, before Weak and Vulnerable. A hit cannot go below 0.
- -2 Dexterity: every Block card gives 2 less. Block from powers and relics is not changed.
- Artifact on you: the Strength loss is applied first, so 1 Artifact stops it. The Dexterity loss needs a 2nd Artifact.

Worked numbers:
- Your turn 1, her Block 12. Strike 6 + Strike 6: 12 Block removed, 0 HP lost. She stays asleep and has 12 Block again on your turn 2.
- Bash 8, then Strike 6: 8 blocked, then 4 blocked and 2 through. She wakes.
- After 1 Soul Siphon: Strike 6 deals 4, Twin Strike 5x2 deals 3x2 = 6, Defend 5 gives 3. After 2: Strike 2, Twin Strike 1x2 = 2, Defend 1.
- Weak on her rounds down per hit. Slash 19 x 0.75 = 14.25, so 14. Disembowel 9 x 0.75 = 6.75, so 6x2 = 12. Slash with Block 12 x 0.75 = 9.
- 1 point of Strength taken from her removes 1 from each Slash and 2 from Disembowel.

Special rules:
- Awake, her only Block is the 12 from Slash with Block. It is up on the turn she shows `debuff` + `buff`.
- Her eyes opening at half HP is an animation only. No phases, no summons.

Status cards: none.

## How to fight it
Derived from the facts above, not from play.
- Your turns 1 and 2: no damage comes and waiting costs nothing. Play Powers and no Block. Do not remove HP from her. Hold Vulnerable and Weak: they would run down while she sleeps.
- Your turn 3: damage past her 11 Block is free. She does nothing on enemy turn 3 and her first Slash is on enemy turn 4 either way.
- Awake she deals 19, 18, 12, then 0 on the Soul Siphon turn. Play no Block on the turn she shows `debuff` + `buff`; she has 12 Block that turn.
- Each Soul Siphon costs you 2 Strength and 2 Dexterity for the rest of the fight and adds 8 to her next cycle. With A = 4 the first lands on enemy turn 7: your turns 3 to 7 come before it.
- After a Siphon, multi-hit attacks lose 2 on every hit. One big hit loses 2 once.

## Lessons
(none yet)
