# Act 1 Underdocks encounters
Every Underdocks fight with line-up and HP, and how the act picks and orders hallway fights, elites and the boss.

## Facts (game files, v0.107.1)

### The fights

- HP is at ascension 0. "(asc: n)" is the HP from ascension 8 on.
- The name in brackets is the game's internal id for the encounter.
- Enemies act in the order listed.
- Each monster has a page: strategy/wiki/enemies/<name>.md, the name in lowercase with hyphens (corpse-slug.md, two-tailed-rat.md).
- A regular fight here is what the enemy pages call a normal fight.
- Single player: 2 enemies in one fight never start with the same HP while their HP range has a free value. So 3 Corpse Slugs always have 25, 26 and 27.
- Co-op: enemy HP is x players x 1.1, rounded down (2 players: x 2.2). Enemies in one fight can then start with the same HP. Each enemy page has its co-op numbers.
- Each enemy page says which of its facts were seen in a fight and which are read from the code only.

| Encounter | Kind | Who appears, HP | How it varies |
| --- | --- | --- | --- |
| Corpse Slugs [CorpseSlugsWeak] | weak | 2 Corpse Slugs, 25-27 each (asc: 27-29) | Always 2. The first starts on a random move of its 3-move loop, 1/3 each. The second starts 1 step later. |
| Seapunk [SeapunkWeak] | weak | 1 Seapunk 44-46 (asc: 47-49) | Fixed. |
| Sludge Spinner [SludgeSpinnerWeak] | weak | 1 Sludge Spinner 37-39 (asc: 41-42) | Fixed. |
| Toadpoles [ToadpolesWeak] | weak | 2 Toadpoles, 21-25 each (asc: 22-26) | Fixed. The first one opens by gaining 2 Thorns, the second by attacking for 7. |
| Many Corpse Slugs [CorpseSlugsNormal] | regular | 3 Corpse Slugs, 25-27 each (asc: 27-29) | Always 3. The first starts on a random move of its 3-move loop, 1/3 each. The second starts 1 step later, the third 2 steps later, so the 3 open with 3 different moves. |
| Cultists [CultistsNormal] | regular | Calcified Cultist 38-41 (asc: 39-42), Damp Cultist 51-53 (asc: 52-54) | Fixed. |
| Fossil Stalker [FossilStalkerNormal] | regular | 1 Fossil Stalker 51-53 (asc: 54-56) | Fixed. |
| Two Gremlins in a Trenchcoat [GremlinMercNormal] | regular | Gremlin Merc 47-49 (asc: 51-53), alone at the start | When it dies, a Sneaky Gremlin 10-14 (asc: 11-15) and a Fat Gremlin 13-17 (asc: 14-18) appear and act in that order. Both do nothing on their first enemy turn. On its second the Fat Gremlin escapes with the gold the Merc took; killing it first returns that gold. The fight ends when the Sneaky Gremlin is dead and the Fat Gremlin is dead or gone. |
| Haunted Ship [HauntedShipNormal] | regular | 1 Haunted Ship 63 (asc: 67) | Fixed. |
| Living Fog [LivingFogNormal] | regular | Living Fog 80 (asc: 82), alone at the start | On enemy turns 2, 4, 6 and so on the Fog adds 1 Gas Bomb, 7 HP (asc: 8). The bomb does not act on that turn. On the next enemy turn it acts before the Fog: it hits for 8 and dies. The fight ends when the Living Fog dies. |
| Punch Construct [PunchConstructNormal] | regular | 1 Punch Construct 55 (asc: 60) | Fixed. |
| Underdocks Wildlife [SeapunkNormal] | regular | Calcified Cultist 38-41 (asc: 39-42), Seapunk 44-46 (asc: 47-49) | Fixed. |
| Sewer Clam [SewerClamNormal] | regular | 1 Sewer Clam 56 (asc: 58) | Fixed. |
| Two-Tailed Rats [TwoTailedRatsNormal] | regular | 3 Two-Tailed Rats, 17-21 each (asc: 18-22) | On enemy turn 1 the 3 rats use 3 different moves; which rat uses which is 1 of 3 line-ups, 1/3 each. From enemy turn 3 a rat can summon 1 more rat with 17-21 HP: at most 3 summons a fight and 5 rats at once. The fight ends when every rat is dead. |
| Phantasmal Gardeners [PhantasmalGardenersElite] | elite | 4 Phantasmal Gardeners, 26-31 each (asc: 27-32) | Fixed. All 4 must die. |
| Skulking Colony [SkulkingColonyElite] | elite | 1 Skulking Colony 75 (asc: 80) | Fixed. |
| Terror Eel [TerrorEelElite] | elite | 1 Terror Eel 140 (asc: 150) | Fixed. |
| Waterfall Giant [WaterfallGiantBoss] | boss | 1 Waterfall Giant 240 (asc: 250) | Fixed. Brought to 0 HP on your turn T (2 or later) it does not die: it deals nothing on enemy turn T, explodes on enemy turn T+1, and then the fight is won. |
| Soul Fysh [SoulFyshBoss] | boss | 1 Soul Fysh 211 (asc: 221) | Fixed. |
| Lagavulin Matriarch [LagavulinMatriarchBoss] | boss | 1 Lagavulin Matriarch 222 (asc: 233) | Fixed. She starts asleep and does nothing on enemy turns 1-3, unless damage that takes HP from her wakes her earlier. |

- Event fight: Punch Constructs [PunchOffEventEncounter], 2 Punch Constructs that each start 2-9 HP down, at 46-53 of 55. It starts from one option of the Punch Off event, which can come up from total floor 6 on. It gives hallway rewards plus 1 relic and 1 potion per player, and uses no entry of the hallway list. See punch-construct.md. Of the 10 Underdocks events it is the only one that starts a fight.

### When act 1 is the Underdocks

- Act 1 is Overgrowth or the Underdocks. The Underdocks has to be unlocked first.
- Single player: the first run after the unlock is always the Underdocks. After that each run is 50/50.
- Co-op: it is never forced. Each run is 50/50 once any player has unlocked it.

### How the act orders its fights

- All lists are fixed when the run starts.
- Hallway list: 15 entries, 14 in co-op. Entries 1-3 are 3 of the 4 weak encounters, in random order. Entries 4-13 are all 10 regular encounters, once each, in random order.
- The last entries (14-15, in co-op 14) repeat regular encounters. No path gets that far: a path has at most 13 hallway rooms, 12 in co-op.
- Hallway fight k of the act is entry k. A Monster room takes the next entry. So does an Unknown room that turns out to be a fight.
- Fights 1-3 are weak and all different. Each weak encounter is left out in 1 run of 4. From fight 4 on every fight is regular. No encounter comes twice in the act, so the next regular one is always one not fought yet.
- Tag rule: an entry does not share a tag with the entry before it. It is never dropped in this act.
- Tags. Slugs: Corpse Slugs, Many Corpse Slugs. Seapunk: Seapunk, Underdocks Wildlife. Every other encounter has no tag.
- The rule only matters from fight 3 to fight 4: after Corpse Slugs the next hallway fight is not Many Corpse Slugs, and after Seapunk it is not Underdocks Wildlife. No 2 weak encounters share a tag, and no 2 regular ones do.
- Fight 4: Many Corpse Slugs 7.8%, Underdocks Wildlife 7.8%, each of the other 8 regular encounters 10.6%.
- Elite list: the 3 elites in random order, then the 3 again in a new order. Elite fight k of the act is entry k. Elites 1-3 are all different: after 2 elites the third is the one not fought yet. Elite 4 is never the same as elite 3.
- Boss: Waterfall Giant, Soul Fysh or Lagavulin Matriarch, fixed at run start. 1/3 each on a profile that has met all 3. Before that it is the first one not yet met, in that order. In co-op a boss counts as met when any player has met it.
- The Underdocks has no fixed fight order for a new profile's first run. Overgrowth has one.
- Seen in 2 runs (solo 2026-10-09, co-op 2026-10-11): 3 different weak fights first, then regular fights with no repeat.

### Where they sit on the map

- The map is built by the same rules as in Overgrowth.
- Floor 1 is Neow. Floor 2 is always a Monster room: hallway fight 1.
- Floors 3-6 hold only Monster, Unknown and Shop rooms. A path has 1-5 Monster rooms before floor 7.
- Elites sit on floors 7-9 and 11-15 only. The map has 5 Elite nodes.
- Floor 10 is Treasure. Floor 16 is a Rest Site. Floor 17 is the boss.
- A typical path has 6-9 hallway fights (3 weak, 3-6 regular) and 0-2 elites (estimate).
- Co-op: the act is 1 floor shorter. Floor 9 is Treasure, floor 15 is a Rest Site, floor 16 is the boss. Elites sit on floors 7-8 and 10-14 only. Floors 1-6 are as in single player. No path estimate was made for the co-op map.
- Seen in the co-op run of 2026-10-11: Treasure on floor 9, elites on floors 10 and 13, Rest Site on floor 15, boss on floor 16.
- Room rules and Unknown room odds: strategy/wiki/run/act1.md. They are the same in both first acts. The other 9 Underdocks events are not covered there or here.
- Act 1 is Overgrowth on the other runs: strategy/wiki/encounters/act1-overgrowth.md.

## Lessons
(none yet)
