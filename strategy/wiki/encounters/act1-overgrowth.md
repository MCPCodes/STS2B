# Act 1 Overgrowth encounters
Every Overgrowth fight with line-up and HP, and how the act picks and orders hallway fights, elites and the boss.

## Facts (game files, v0.107.1)

### The fights

- HP is at ascension 0. "(asc: n)" is the HP from ascension 8 on.
- The name in brackets is the game's internal id for the encounter.
- Enemies act in the order listed.
- Each monster has a page: strategy/wiki/enemies/<name>.md, the name in lowercase with hyphens (leaf-slime-m.md, eye-with-teeth.md).
- Slime HP: Leaf Slime (S) 11-15 (asc: 12-16). Twig Slime (S) 7-11 (asc: 8-12). Leaf Slime (M) 32-35 (asc: 33-36). Twig Slime (M) 26-28 (asc: 27-29).

| Encounter | Kind | Who appears, HP | How it varies |
| --- | --- | --- | --- |
| A Lone Nibbit [NibbitsWeak] | weak | 1 Nibbit 42-46 (asc: 44-48) | Fixed. |
| Fuzzy Wurm Crawler [FuzzyWurmCrawlerWeak] | weak | 1 Fuzzy Wurm Crawler 55-57 (asc: 58-59) | Fixed. |
| Shrinker Beetle [ShrinkerBeetleWeak] | weak | 1 Shrinker Beetle 38-40 (asc: 40-42) | Fixed. |
| Group of Slimes [SlimesWeak] | weak | small slime, medium slime, small slime | The 2 small ones are 1 Leaf Slime (S) and 1 Twig Slime (S); which is first is 50/50. The medium one is Leaf Slime (M) or Twig Slime (M), 50/50. |
| Cubex Construct [CubexConstructNormal] | regular | 1 Cubex Construct 65 (asc: 70) | Fixed. |
| Shroom and Slime [FlyconidNormal] | regular | medium slime, Flyconid 47-49 (asc: 51-53) | The slime is Leaf Slime (M) or Twig Slime (M), 50/50. |
| Fogmog [FogmogNormal] | regular | Fogmog 74 (asc: 78), alone at the start | On enemy turn 1 Fogmog adds 1 Eye with Teeth, 6 HP at every ascension. From then on the Eye acts before Fogmog. Killing the Eye only cancels its next action: it is back at 6 HP on your next turn. The fight ends when Fogmog dies. |
| Inklets [InkletsNormal] | regular | 3 Inklets, 11-17 each (asc: 12-18) | Fixed. |
| Mawler [MawlerNormal] | regular | 1 Mawler 72 (asc: 76) | Fixed. |
| A Pair of Nibbits [NibbitsNormal] | regular | 2 Nibbits, 42-46 each (asc: 44-48) | Fixed. |
| Overgrowth Crawlers [OvergrowthCrawlers] | regular | Shrinker Beetle 38-40 (asc: 40-42), Fuzzy Wurm Crawler 55-57 (asc: 58-59) | Fixed. |
| Ruby Raiders [RubyRaidersNormal] | regular | 3 raiders of 3 different types | 3 of these 5 types, any order, each type in 3 fights out of 5: Axe Raider 20-22 (asc: 21-23), Assassin Raider 18-23 (asc: 19-24), Brute Raider 30-33 (asc: 31-34), Crossbow Raider 18-21 (asc: 19-22), Tracker Raider 21-25 (asc: 22-26). |
| Swarm of Slimes [SlimesNormal] | regular | Twig Slime (M), Leaf Slime (M), then Leaf Slime (S) and Twig Slime (S) | Always all 4. Only the order of the 2 small ones varies, 50/50. |
| Strangler and Friend [SlitheringStranglerNormal] | regular | 1 companion group, then Slithering Strangler 53-55 (asc: 54-56) | The group, 1/3 each: Snapping Jaxfruit 31-33 (asc: 34-36); or 1 medium slime (Leaf or Twig, 50/50); or 2 small slimes (each Leaf or Twig, 50/50, both can be the same kind). So 2 or 3 enemies. |
| Overgrowth Flora [SnappingJaxfruitNormal] | regular | Snapping Jaxfruit 31-33 (asc: 34-36), Flyconid 47-49 (asc: 51-53) | Fixed. |
| Vine Shambler [VineShamblerNormal] | regular | 1 Vine Shambler 61 (asc: 64) | Fixed. |
| Bygone Effigy [BygoneEffigyElite] | elite | 1 Bygone Effigy 127 (asc: 132) | Fixed. |
| Byrdonis [ByrdonisElite] | elite | 1 Byrdonis 81-84 (asc: 90) | Fixed. |
| Phrog Parasite [PhrogParasiteElite] | elite | Phrog Parasite 61-64 (asc: 66-68), alone at the start | When it dies, 4 Wrigglers appear, 17-21 each (asc: 18-22). They skip their first enemy turn. The fight ends when all 4 are dead. |
| Vantom [VantomBoss] | boss | 1 Vantom 173 (asc: 183) | Fixed. |
| Ceremonial Beast [CeremonialBeastBoss] | boss | 1 Ceremonial Beast 252 (asc: 262) | Fixed. |
| The Kin [TheKinBoss] | boss | Kin Follower, Kin Follower, Kin Priest 190 (asc: 199). Followers 58-59 each (asc: 62-63) | Fixed. The fight ends when the Kin Priest dies; living Followers die with it. |

- Event fight: Wrigglers [DenseVegetationEventEncounter], 4 Wrigglers that act from turn 1. It starts from one option of the Dense Vegetation event. It gives hallway rewards and uses no entry of the hallway list.

### How the act orders its fights

- All lists are fixed when the run starts.
- Hallway list: 15 entries. Entries 1-3 are 3 of the 4 weak encounters, in random order. Entries 4-15 are all 12 regular encounters, once each, in random order.
- Hallway fight k of the act is entry k. A Monster room takes the next entry. So does an Unknown room that turns out to be a fight.
- Fights 1-3 are weak and all different. From fight 4 on every fight is regular. No regular encounter comes twice in the act, so the next one is always one not fought yet.
- Tag rule: an entry does not share a tag with the entry before it. This also holds from fight 3 to fight 4. It is dropped only when the bag has no other encounter left: about 4% of lists, at fights 14-15 (estimate).
- Tags. Slimes: Group of Slimes, Swarm of Slimes, Shroom and Slime. Mushroom: Shroom and Slime, Overgrowth Flora. Shrinker: Shrinker Beetle, Overgrowth Crawlers. Crawler: Fuzzy Wurm Crawler, Overgrowth Crawlers. Nibbit: A Lone Nibbit. Every other encounter has no tag.
- After Group of Slimes the next hallway fight is not Swarm of Slimes or Shroom and Slime. After Shrinker Beetle or Fuzzy Wurm Crawler it is not Overgrowth Crawlers. Shroom and Slime is not next to Swarm of Slimes or Overgrowth Flora, in either order. A Pair of Nibbits can come right after A Lone Nibbit.
- Fight 4: Overgrowth Crawlers 4.6%, Shroom and Slime 6.6%, Swarm of Slimes 6.6%, each of the other 9 regular encounters 9.1%.
- Elite list: the 3 elites in random order, then the 3 again in a new order. Elite fight k of the act is entry k. Elites 1-3 are all different: after 2 elites the third is the one not fought yet. Elite 4 is never the same as elite 3.
- Boss: Vantom, Ceremonial Beast or The Kin, fixed at run start. 1/3 each on a profile that has met all 3. Before that it is the first one not yet met, in that order.

### Where they sit on the map

- Floor 1 is Neow. Floor 2 is always a Monster room: hallway fight 1.
- Floors 3-6 hold only Monster, Unknown and Shop rooms. A path has 1-5 Monster rooms before floor 7.
- Elites sit on floors 7-9 and 11-15 only. The map has 5 Elite nodes.
- Floor 10 is Treasure. Floor 16 is a Rest Site. Floor 17 is the boss.
- A typical path has 6-9 hallway fights (3 weak, 3-6 regular) and 0-2 elites (estimate).
- Room rules, Unknown room odds and rewards: strategy/wiki/run/act1.md.
- Act 1 is Underdocks on some runs. Its encounters are not covered here.

## Lessons
(none yet)
