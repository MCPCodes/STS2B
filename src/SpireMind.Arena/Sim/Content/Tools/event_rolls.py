#!/usr/bin/env python3
"""What the events of EventsA.cs roll on a seed, worked out apart from the arena.

    python3 -I event_rolls.py <decompiled game folder> [seed ...]

EventsAChecks.cs holds the arena to numbers that were not taken from the arena. This script
is where they come from: its own reading of the game's random generator (the public-domain
xoshiro256** seeded through SplitMix64, one 53-bit fraction per number), of the game's string
hash, of how an event's stream is seeded (the run's seed number plus the player's slot plus
the hash of the event's id) and of each event's draws. The lists a transform picks from are
read out of the decompiled game's card pool and card files when the script runs; nothing of
the game is kept here. It shares no code with the arena.

It prints, for the seeds of the checks (or the seeds given):

  - This or That?: the gold (41 plus a whole number below 28);
  - The Sunken Statue: the gold (111 plus a whole number from -10 to 10), for the first and
    the second player (the record of a co-op run has both);
  - Jungle Maze Adventure: both amounts as rolled (150 and 50, each plus a 32-bit fraction
    from -15 up to 15 that the game turns into a decimal of seven digits) and what is paid;
    and what the amounts would be if the number were scaled in 32 bits, where that differs;
  - Tablet of Truth: the deck places of the four random upgrades on the starter deck;
  - Aroma of Chaos: what a Strike, Bash, Clumsy and Neow's Fury become (for Neow's Fury the
    game's first picks in order, since the arena draws again past cards it has not built);
  - Morphic Grove: the two new cards;
  - the rarities the first and the 26th to 28th number of the player's rewards stream roll
    for a relic.

After a game update, run it on the new decompile and compare with the numbers typed into
EventsAChecks.cs.
"""
import os
import re
import struct
import sys
from decimal import Decimal

if len(sys.argv) < 2:
    sys.exit(__doc__)
GAME = sys.argv[1]
SEEDS = sys.argv[2:] or ["1BUSLN0FY5", "KMEHXZW0R5", "EEXRY5WA1R", "E9U400ERJ4", "3FBTGQ63TM", "AG5XPLQQNY", "75F277YN2G",
                         "B1GYAHUBLESSTH1SRUNTHANKY0U#BLESSED", "A000000003"]
MASK = (1 << 64) - 1


def int32(x):
    x &= 0xFFFFFFFF
    return x - (1 << 32) if x & 0x80000000 else x


def name_hash(text):
    """The game's hash of a text: two running values over the even and the odd characters."""
    h1 = h2 = 352654597
    for i in range(0, len(text), 2):
        h1 = int32(((h1 << 5) + h1) ^ ord(text[i]))
        if i + 1 < len(text):
            h2 = int32(((h2 << 5) + h2) ^ ord(text[i + 1]))
    return int32(h1 + h2 * 1566083941)


def rotl(x, k):
    return ((x << k) | (x >> (64 - k))) & MASK


def float32(x):
    return struct.unpack("f", struct.pack("f", x))[0]


class Rng:
    def __init__(self, seed):
        z = seed & MASK
        self.s = []
        for _ in range(4):
            z = (z + 0x9E3779B97F4A7C15) & MASK
            t = z
            t = ((t ^ (t >> 30)) * 0xBF58476D1CE4E5B9) & MASK
            t = ((t ^ (t >> 27)) * 0x94D049BB133111EB) & MASK
            self.s.append(t ^ (t >> 31))
        self.counter = 0

    def fraction(self):
        s = self.s
        self.counter += 1
        result = (rotl((s[1] * 5) & MASK, 7) * 9) & MASK
        t = (s[1] << 17) & MASK
        s[2] ^= s[0]
        s[3] ^= s[1]
        s[1] ^= s[2]
        s[0] ^= s[3]
        s[2] ^= t
        s[3] = rotl(s[3], 45)
        return (result >> 11) * (1.0 / (1 << 53))

    def whole(self, low, high):
        """A whole number from low up to but not including high."""
        return low + int(self.fraction() * (high - low))

    def float_between(self, low, high):
        return float32(self.fraction() * (high - low) + low)


def event_stream(seed, event_id, slot=0):
    return Rng((int32(name_hash(seed)) + slot + name_hash(event_id)) & 0xFFFFFFFF)


def player_stream(seed, name, slot=0):
    return Rng((int32(name_hash(seed)) + slot + name_hash(name)) & 0xFFFFFFFF)


def as_decimal(x):
    """A 32-bit float as the game's decimal: seven significant digits."""
    return Decimal(format(x, ".6e"))


def pool(name):
    source = open(os.path.join(GAME, "MegaCrit.Sts2.Core.Models.CardPools", name + ".cs")).read()
    return re.findall(r"ModelDb\.Card<(\w+)>\(\)", source[source.index("GenerateAllCards"):])


def card(name):
    source = open(os.path.join(GAME, "MegaCrit.Sts2.Core.Models.Cards", name + ".cs")).read()
    made = re.search(r"base\(\s*(-?\d+)\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)", source)
    multiplayer = re.search(r"MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.MultiplayerOnly", source) is not None
    return made.group(2), made.group(3), multiplayer


def transform_options(original, own_pool):
    """What a card can become: its own pool (the colorless one for a Quest card and for Event, Ancient and Token rarity),
    Common, Uncommon and Rare cards only unless the original is a Status or a Curse, never itself, never a multiplayer-only card."""
    kind, rarity, _ = card(original)
    if kind == "Quest" or rarity in ("Event", "Ancient", "Token"):
        own_pool = "ColorlessCardPool"
    options = []
    for name in pool(own_pool):
        _, other, multiplayer = card(name)
        if rarity not in ("Curse", "Status") and other not in ("Common", "Uncommon", "Rare"):
            continue
        if name != original and not multiplayer:
            options.append(name)
    return options


def relic_rarity(fraction):
    x = float32(fraction)
    return "Common" if x < float32(0.5) else "Uncommon" if x < float32(0.83) else "Rare"


STARTER = ["StrikeIronclad"] * 5 + ["DefendIronclad"] * 4 + ["Bash"]

for seed in SEEDS:
    print(seed)
    print("  This or That?: gold", event_stream(seed, "THIS_OR_THAT").whole(41, 69))
    print("  The Sunken Statue: gold", 111 + event_stream(seed, "SUNKEN_STATUE").whole(-10, 11),
          "(second player:", str(111 + event_stream(seed, "SUNKEN_STATUE", 1).whole(-10, 11)) + ")")

    rng = event_stream(seed, "JUNGLE_MAZE_ADVENTURE")
    first, second = rng.fraction(), rng.fraction()
    solo = Decimal(150) + as_decimal(float32(first * 30.0 - 15.0))
    join = Decimal(50) + as_decimal(float32(second * 30.0 - 15.0))
    # What a fraction scaled in 32 bits would give, to see on which seeds the game's 64-bit scaling shows.
    narrow = [as_decimal(float32(float32(float32(x) * 30.0) - 15.0)) for x in (first, second)]
    print("  Jungle Maze Adventure: Solo Quest", solo, "pays", int(solo), "| Join Forces", join, "pays", int(join),
          "" if (Decimal(150) + narrow[0], Decimal(50) + narrow[1]) == (solo, join)
          else "(scaled in 32 bits: " + str(Decimal(150) + narrow[0]) + " and " + str(Decimal(50) + narrow[1]) + ")")

    rng = event_stream(seed, "TABLET_OF_TRUTH")
    left = list(range(len(STARTER)))
    places = []
    for _ in range(4):
        places.append(left.pop(rng.whole(0, len(left))))
    print("  Tablet of Truth: the random upgrades land on deck places", places, [STARTER[p] for p in places])

    for original, own in (("StrikeIronclad", "IroncladCardPool"), ("Bash", "IroncladCardPool"), ("Clumsy", "CurseCardPool")):
        options = transform_options(original, own)
        rng = event_stream(seed, "AROMA_OF_CHAOS")
        print("  Aroma of Chaos:", original, "becomes", options[rng.whole(0, len(options))], "(of", len(options), "cards)")
    options = transform_options("NeowsFury", "EventCardPool")
    rng = event_stream(seed, "AROMA_OF_CHAOS")
    print("  Aroma of Chaos: Neow's Fury, the game's picks in order (of", len(options), "cards):",
          ", ".join(options[rng.whole(0, len(options))] for _ in range(8)))

    basics = transform_options("StrikeIronclad", "IroncladCardPool")
    rng = event_stream(seed, "MORPHIC_GROVE")
    print("  Morphic Grove: the first Basic card becomes", basics[rng.whole(0, len(basics))], "and the second", basics[rng.whole(0, len(basics))])

    rng = player_stream(seed, "rewards")
    rolls = [relic_rarity(rng.fraction()) for _ in range(28)]
    print("  rewards as relic rarities: number 1", rolls[0], "| numbers 26 to 28", rolls[25], rolls[26], rolls[27])
