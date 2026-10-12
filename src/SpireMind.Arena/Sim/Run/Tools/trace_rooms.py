#!/usr/bin/env python3
"""A second reading of what a room rolls, number by number: rewards, shop, chest, rest site.

    python3 -I trace_rooms.py <decompiled game folder> trace <seed> <room,room,...> [rewards=<n>] [shops=<n>] [treasure=<n>] [offset=<x>] [chance=<x>]
    python3 -I trace_rooms.py <decompiled game folder> data  <seed>=<rooms> [<seed>=<rooms> ...]
    python3 -I trace_rooms.py <decompiled game folder> find  <rooms> <built.txt> [count] [first number]
    python3 -I trace_rooms.py <decompiled game folder> ties  <seed> [how many numbers of the shops stream]
    python3 -I trace_rooms.py <decompiled game folder> edges <seed>
    python3 -I trace_rooms.py <decompiled game folder> shop-ties <seed> <built.txt> [how many numbers] [how many of a kind] [kinds to leave out]
    python3 -I trace_rooms.py <decompiled game folder> exact <seed> <stream> <how many numbers> <threshold> [<threshold> ...]
    python3 -I trace_rooms.py <decompiled game folder> record <a .run file of the game's history folder>

This script shares nothing with the arena. It has its own generator (the game's xoshiro256**
seeded through SplitMix64), its own string hash, its own 32-bit float arithmetic, and it reads
the game's lists itself from the decompiled class files (names, numbers and rarities; nothing
is copied): the shared and the Ironclad relic pool, the Ironclad and the colorless card pool,
the Ironclad's and the shared potions, and the relics a shop never sells. From a seed it makes
both relic bags as the game does on its up_front stream, and then walks a list of rooms the
way the game's code rolls them, printing every number it draws and what the number decides.

Rooms, separated by commas:
    M        a hallway fight's rewards        E   an elite's rewards        B   a boss's rewards
    M+<n>    a hallway fight that also hands back <n> stolen gold (a gold row the set was made with)
    S        a shop (nothing is bought)       R   a rest site (Rest)        T   a chest (opened, relic taken)
    S!card3/potion1/card3   a shop whose bought places are filled again (a relic like The Courier is held): the
             character card of place 3 is bought, then the potion of place 1, then the new card of place 3
    A        Arcane Scroll's card (one Rare card of the character, no rarity roll, no upgrade roll)
    V        an event's relic (a rolled rarity, the front of the player's bag, taken)
A and V are there to hold the script to the game's own record of a real run (seed 1BUSLN0FY5:
A,M,M,V,S); they are not rooms of the checks.

The player of the walk takes the gold and the first card of every fight, leaves potions and
relic rows where they are, buys nothing, rests, and takes a chest's relic. None of that moves
a stream; a relic row that is left has left both bags all the same, as in the game.

'trace' prints the walk for a reader; the walk can start with its streams moved on to a counter and
with another pity offset or potion chance. 'data' prints it as the C# literals of RoomTraces.cs.
'find' looks for seeds whose walk meets nothing the arena has not built on a stream (the file
lists what is built: one class name a line under the headings [potions], [colorless],
[relics]) and whose first act is Overgrowth, so that the arena draws nothing again and its
rooms must be the game's own.
'ties' looks through one seed's shops stream for price factors whose product with a table
value is, as a 32-bit float, exactly a whole number and a half: the prices that tell half-to-even
from half-up, and 32-bit from 64-bit arithmetic. 'shop-ties' builds a whole shop around such a number (the
shop is rolled after its two streams were moved on), for the first character card, the first
relic and the first potion, and prints each as a line of RoomTraces.PriceTies. 'edges' prints a
seed's first hallway fight three times, with the potion chance or the pity offset set so that a
roll lands exactly on its threshold (RoomTraces.Edges): the game tests "below", not "at or below".
'exact' looks through a stream of a seed (rewards, shops, treasure_room_relics, ...) for the first
number that is exactly a threshold as a 32-bit float (RoomTraces.ExactRolls; 0.1 takes about 130
million numbers and some minutes). 'record' walks the game's own record of a run floor by floor
(fights, shops, chests, rest sites; a Gremlin Merc's fight hands stolen gold back) and says for
each floor whether the script gives the record's gold, cards, potion and relics; it stops at the
first event, since what an event draws is not this script's.

Everything unlocked, single player, ascension 0, act 1: the arena's profile.
"""
import os
import re
import struct
import sys

M64 = (1 << 64) - 1


def f32(x):
    """The nearest 32-bit float, as the game's float arithmetic rounds every result."""
    return struct.unpack("f", struct.pack("f", x))[0]


def i32(x):
    x &= 0xFFFFFFFF
    return x - (1 << 32) if x & 0x80000000 else x


def name_hash(text):
    """StringHelper.GetDeterministicHashCode: two running 32-bit sums over the even and the odd characters."""
    a = b = 352654597
    for i in range(0, len(text), 2):
        a = i32(((a << 5) + a) ^ ord(text[i]))
        if i == len(text) - 1:
            break
        b = i32(((b << 5) + b) ^ ord(text[i + 1]))
    return i32(a + b * 1566083941)


def rotl(x, k):
    return ((x << k) | (x >> (64 - k))) & M64


class Rng:
    """Rng over MegaRandom: every draw is one 64-bit step, and every draw counts."""

    def __init__(self, seed, name=None):
        seed &= 0xFFFFFFFF
        if name is not None:
            seed = (seed + (name_hash(name) & 0xFFFFFFFF)) & 0xFFFFFFFF
        self.seed = seed
        self.counter = 0
        x = seed
        s = []
        for _ in range(4):
            x = (x + 11400714819323198485) & M64
            z = x
            z = ((z ^ (z >> 30)) * 13787848793156543929) & M64
            z = ((z ^ (z >> 27)) * 10723151780598845931) & M64
            s.append(z ^ (z >> 31))
        self.s = s

    def _step(self):
        s0, s1, s2, s3 = self.s
        result = (rotl((s1 * 5) & M64, 7) * 9) & M64
        t = (s1 << 17) & M64
        s2 ^= s0
        s3 ^= s1
        s1 ^= s2
        s0 ^= s3
        s2 ^= t
        s3 = rotl(s3, 45)
        self.s = [s0, s1, s2, s3]
        return result

    def double(self):
        self.counter += 1
        return (self._step() >> 11) * 1.1102230246251565e-16

    def int_below(self, n):
        return int(self.double() * n)

    def int_between(self, low, high_exclusive):
        return int(self.double() * (high_exclusive - low)) + low

    def float(self, low=0.0, high=1.0):
        """Rng.NextFloat(min, max): the fraction times the float width plus the lower end, in doubles, then narrowed."""
        return f32(self.double() * f32(high - low) + low)


def round_even(x):
    """MathF.Round / Math.Round with the default rule: a half goes to the even neighbour."""
    return int(round(x))  # Python's round is half-to-even too, and exact on a float


# ---------- the game's lists, read from its class files ----------

class Game:
    def __init__(self, folder):
        self.folder = folder
        self.shared_relics = self._models("Models.RelicPools", "SharedRelicPool", "GenerateAllRelics", "Relic")
        self.ironclad_relics = self._models("Models.RelicPools", "IroncladRelicPool", "GenerateAllRelics", "Relic")
        self.relic_rarity = {r: self._one("Models.Relics", r, r"RelicRarity Rarity => RelicRarity\.(\w+)") for r in self.shared_relics + self.ironclad_relics}
        self.never_sold = sorted(r for r in self.shared_relics + self.ironclad_relics
                                 if re.search(r"IsAllowedInShops\s*=>\s*false", self._text("Models.Relics", r)))
        self.ironclad_cards = self._cards("IroncladCardPool")
        self.colorless_cards = self._cards("ColorlessCardPool")
        own = self._models("Timeline.Epochs", "Ironclad4Epoch", "Potions", "Potion")
        shared = self._models("Models.PotionPools", "SharedPotionPool", "GenerateAllPotions", "Potion")
        self.potions = [(p, self._one("Models.Potions", p, r"PotionRarity Rarity => PotionRarity\.(\w+)")) for p in own + shared]

    def _text(self, folder, name):
        with open(os.path.join(self.folder, "MegaCrit.Sts2.Core." + folder, name + ".cs"), encoding="utf-8") as f:
            return f.read()

    def _body(self, text, member):
        at = text.index(member)
        brace = text.index("{", at)
        depth = 0
        for i in range(brace, len(text)):
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
                if depth == 0:
                    return text[brace:i + 1]
        raise ValueError(member)

    def _models(self, folder, name, member, kind):
        return re.findall(r"ModelDb\." + kind + r"<(\w+)>\(\)", self._body(self._text(folder, name), member))

    def _one(self, folder, name, pattern):
        found = re.search(pattern, self._text(folder, name))
        if not found:
            raise ValueError(f"{name}: no {pattern}")
        return found.group(1)

    def _cards(self, pool):
        cards = []
        for name in self._models("Models.CardPools", pool, "GenerateAllCards", "Card"):
            text = self._text("Models.Cards", name)
            found = re.search(r":\s*base\(\s*-?\d+\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)", text)
            if not found:
                raise ValueError(f"{name}: no constructor")
            multiplayer_only = bool(re.search(r"MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.MultiplayerOnly", text))
            cards.append((name, found.group(1), found.group(2), multiplayer_only))
        return cards


BAG_RARITIES = ("Common", "Uncommon", "Rare", "Shop")


def make_bag(relics, rarity_of, rng):
    """RelicGrabBag.Populate: a list a rarity in the order the rarities first appear, each shuffled in that order."""
    lists = {}
    for relic in relics:
        lists.setdefault(rarity_of[relic], []).append(relic)
    for items in lists.values():
        n = len(items)
        while n > 1:
            n -= 1
            j = rng.int_below(n + 1)
            items[n], items[j] = items[j], items[n]
    return lists


def next_list(rarity):
    return {"Shop": "Common", "Common": "Uncommon", "Uncommon": "Rare"}.get(rarity)


def pull(bag, rarity, from_back, fits=lambda r: True):
    """RelicGrabBag.PullFromFront / PullFromBack. (Nothing is disallowed in act 1; the refill of the shared bag is not reached.)"""
    while rarity is not None and not any(fits(r) for r in bag.get(rarity, [])):
        rarity = next_list(rarity)
    if rarity is None:
        return None
    items = bag[rarity]
    order = range(len(items) - 1, -1, -1) if from_back else range(len(items))
    for i in order:
        if fits(items[i]):
            return items.pop(i)
    return None


def remove(bag, relic):
    for items in bag.values():
        if relic in items:
            items.remove(relic)


def relic_rarity_of(roll):
    return "Common" if roll < f32(0.5) else "Uncommon" if roll < f32(0.83) else "Rare"


RARE_ODDS = {"Regular": 0.03, "Elite": 0.1, "Boss": 1.0, "Shop": 0.09}
UNCOMMON_ODDS = {"Regular": 0.37, "Elite": 0.4, "Boss": 0.0, "Shop": 0.37}
STEP_UP = {"Basic": "Common", "Common": "Uncommon", "Uncommon": "Rare", "Rare": "Common"}


class Walk:
    """One run's rooms, rolled as the game rolls them. `say` gets a line for every number drawn."""

    def __init__(self, game, seed, say=None, rewards_at=0, shops_at=0, shops_state=None, offset=None, chance=None, treasure_at=0):
        self.game = game
        self.seed = seed
        self.say = say or (lambda line: None)
        number = name_hash(seed) & 0xFFFFFFFF
        up_front = Rng(number, "up_front")
        self.shared = make_bag(game.shared_relics, game.relic_rarity, up_front)
        own = [r for r in game.shared_relics + game.ironclad_relics if game.relic_rarity[r] in BAG_RARITIES]
        self.player = make_bag(own, game.relic_rarity, up_front)
        self.bags_at_start = ({k: list(v) for k, v in self.player.items()}, {k: list(v) for k, v in self.shared.items()})
        self.treasure = Rng(number, "treasure_room_relics")
        self.rewards = Rng(number + 0, "rewards")      # the player's streams: the seed number plus the player's slot, 0
        self.shops = Rng(number + 0, "shops")
        for _ in range(rewards_at):     # Rng.FastForwardCounter: one step a number
            self.rewards.double()
        for _ in range(treasure_at):
            self.treasure.double()
        if shops_state is not None:     # the same place, reached before by the caller
            self.shops.s, self.shops.counter = list(shops_state), shops_at
        else:
            for _ in range(shops_at):
                self.shops.double()
        self.offset = f32(-0.05) if offset is None else offset
        self.chance = f32(0.4) if chance is None else chance
        self.removals = 0
        self.rooms = []
        self.prices = []                # every price a shop rolled: (shops counter, table value, factor, exact product, price)
        self.exact_zero_upgrade_roll = False

    def counters(self):
        return (self.rewards.counter, self.shops.counter, self.treasure.counter)

    # ----- pieces -----

    def _rarity(self, table, changes_offset):
        offset = f32(0.0) if table == "Boss" else self.offset
        roll = self.rewards.float()
        rare = f32(f32(RARE_ODDS[table]) + offset)
        uncommon = f32(f32(UNCOMMON_ODDS[table]) + rare)
        rarity = "Rare" if roll < rare else "Uncommon" if roll < uncommon else "Common"
        self.say(f"    rewards #{self.rewards.counter}: {roll!r} against Rare below {rare!r}, Uncommon below {uncommon!r} (offset {offset!r}): {rarity}")
        if changes_offset:
            self.offset = f32(-0.05) if rarity == "Rare" else min(f32(self.offset + f32(0.01)), f32(0.4))
        return rarity

    def _upgrade_roll(self):
        roll = self.rewards.float()
        if roll == 0.0:
            self.exact_zero_upgrade_roll = True
        self.say(f"    rewards #{self.rewards.counter}: the upgrade roll, {roll!r}: no upgrade")

    def _potions(self, rng, count, stream):
        options = list(self.game.potions)
        made = []
        for _ in range(count):
            roll = rng.float()
            rarity = "Rare" if roll <= f32(0.1) else "Uncommon" if roll <= f32(0.35) else "Common"
            of_rarity = [p for p in options if p[1] == rarity]
            self.say(f"    {stream} #{rng.counter}: {roll!r}: a {rarity} potion")
            index = rng.int_between(0, len(of_rarity))
            potion = of_rarity[index]
            self.say(f"    {stream} #{rng.counter}: place {index} of {len(of_rarity)}: {potion[0]}")
            options.remove(potion)
            made.append(potion)
        return made

    # ----- rooms -----

    def fight(self, kind, stolen=0):
        table = {"M": "Regular", "E": "Elite", "B": "Boss"}[kind]
        before = self.counters()
        room = {"kind": kind, "stolen": stolen, "before": before}
        self.say(f"  {'hallway fight' if kind == 'M' else 'elite' if kind == 'E' else 'boss'}" + (f" that hands back {stolen} stolen gold" if stolen else ""))

        # Making the set: the potion roll (PotionRewardOdds.Roll). An elite adds half of 0.25 to the chance of this roll.
        need = f32(self.chance + f32((0.25 if kind == "E" else 0.0) * 0.5))
        roll = self.rewards.float()
        dropped = roll < need
        self.say(f"    rewards #{self.rewards.counter}: {roll!r} against a potion below {need!r}: {'a potion drops' if dropped else 'no potion'}")
        self.chance = f32(self.chance - f32(0.1)) if dropped else f32(self.chance + f32(0.1))

        # Filling the set, row by row in the order it was made in.
        low, high = {"M": (10, 20), "E": (35, 45), "B": (100, 100)}[kind]
        room["gold"] = self.rewards.int_between(low, high + 1)
        self.say(f"    rewards #{self.rewards.counter}: {room['gold']} gold ({low} to {high})")

        room["potion"] = None
        if dropped:
            room["potion"] = self._potions(self.rewards, 1, "rewards")[0][0]

        pool = [c for c in self.game.ironclad_cards if not c[3]]
        shown = []
        room["cards"] = []
        for _ in range(3):
            options = [c for c in pool if c[0] not in shown]
            rarity = self._rarity(table, changes_offset=True)
            have = {c[2] for c in options}
            while rarity not in have:
                rarity = STEP_UP[rarity]
            items = [c for c in options if c[2] == rarity]
            index = self.rewards.int_between(0, len(items))
            card = items[index]
            self.say(f"    rewards #{self.rewards.counter}: place {index} of {len(items)} {rarity} cards: {card[0]}")
            self._upgrade_roll()
            shown.append(card[0])
            room["cards"].append((card[0], rarity))

        room["relic_rarity"] = room["relic"] = None
        if kind == "E":
            roll = self.rewards.float()
            rarity = relic_rarity_of(roll)
            relic = pull(self.player, rarity, from_back=False)
            remove(self.shared, relic)
            self.say(f"    rewards #{self.rewards.counter}: {roll!r}: a {rarity} relic; the front of the player's {rarity} list: {relic}")
            room["relic_rarity"], room["relic"] = rarity, relic

        if stolen:
            again = self.rewards.int_between(stolen, stolen + 1)
            self.say(f"    rewards #{self.rewards.counter}: the gold row the set was made with draws its number all the same: {again} gold")

        room["offset"], room["chance"] = self.offset, self.chance
        room["after"] = self.counters()
        self.rooms.append(room)

    def shop(self):
        before = self.counters()
        room = {"kind": "S", "before": before}
        self.say("  shop")
        sale = self.shops.int_below(5)
        self.say(f"    shops #{self.shops.counter}: character card {sale} is on sale")
        room["sale"] = sale
        shown = []
        cards = []

        def price_of(base, on_sale=False):
            factor = self.shops.float(f32(0.95), f32(1.05))
            price = round_even(f32(base * factor))
            self.prices.append((self.shops.counter, base, factor, base * factor, price))
            self.say(f"    shops #{self.shops.counter}: factor {factor!r}; {base} x that is {f32(base * factor)!r}: {price} gold" + (f", halved: {price // 2}" if on_sale else ""))
            return price // 2 if on_sale else price

        pool = [c for c in self.game.ironclad_cards if not c[3]]
        for slot, card_type in enumerate(("Attack", "Attack", "Skill", "Skill", "Power")):
            options = [c for c in pool if c[0] not in shown and c[2] != "Basic"]
            rarity = self._rarity("Shop", changes_offset=False)
            first = rarity
            while not any(c[2] == rarity and c[1] == card_type for c in options):
                rarity = STEP_UP[rarity]
                if rarity == first:
                    raise ValueError("no rarity")
            items = [c for c in options if c[2] == rarity and c[1] == card_type]
            index = self.shops.int_between(0, len(items))
            card = items[index]
            self.say(f"    shops #{self.shops.counter}: place {index} of {len(items)} {rarity} {card_type} cards: {card[0]}")
            self._upgrade_roll()
            base = {"Rare": 150, "Uncommon": 75}.get(card[2], 50)
            price = price_of(base)
            shown.append(card[0])
            if slot == sale:
                price = price_of(base, on_sale=True)
            cards.append((card[0], price))

        colorless = [c for c in self.game.colorless_cards if not c[3]]
        for rarity in ("Uncommon", "Rare"):
            items = [c for c in colorless if c[0] not in shown and c[2] != "Basic" and c[2] == rarity]
            index = self.shops.int_between(0, len(items))
            card = items[index]
            self.say(f"    shops #{self.shops.counter}: place {index} of {len(items)} {rarity} colorless cards: {card[0]}")
            self._upgrade_roll()
            base = round_even(f32({"Rare": 150, "Uncommon": 75}[rarity] * f32(1.15)))
            shown.append(card[0])
            cards.append((card[0], price_of(base)))
        room["cards"] = cards

        rarities = []
        for _ in range(2):
            roll = self.rewards.float()
            rarities.append(relic_rarity_of(roll))
            self.say(f"    rewards #{self.rewards.counter}: {roll!r}: a {rarities[-1]} relic place")
        rarities.append("Shop")
        relics = []
        for rarity in rarities:
            relic = pull(self.player, rarity, from_back=True, fits=lambda r: r not in self.game.never_sold)
            remove(self.shared, relic)
            base = {"Common": 175, "Uncommon": 225, "Rare": 275, "Shop": 200}[self.game.relic_rarity[relic]]
            factor = self.shops.float(f32(0.85), f32(1.15))
            price = round_even(f32(base * factor))
            self.prices.append((self.shops.counter, base, factor, base * factor, price))
            self.say(f"    the back of the player's {rarity} list that a shop sells: {relic}")
            self.say(f"    shops #{self.shops.counter}: factor {factor!r}; {base} x that is {f32(base * factor)!r}: {price} gold")
            relics.append((rarity, relic, price))
        room["relics"] = relics

        potions = self._potions(self.shops, 3, "shops")
        room["potions"] = []
        for name, rarity in potions:
            room["potions"].append((name, price_of({"Rare": 100, "Uncommon": 75}.get(rarity, 50))))
        room["removal"] = 75 + 25 * self.removals
        room["offset"], room["chance"] = self.offset, self.chance
        room["after"] = self.counters()
        self.rooms.append(room)

    def arcane_scroll(self):
        rares = [c for c in self.game.ironclad_cards if not c[3] and c[2] == "Rare"]
        index = self.rewards.int_between(0, len(rares))
        self.say(f"    rewards #{self.rewards.counter}: place {index} of {len(rares)} Rare cards: {rares[index][0]}")

    def event_relic(self):
        roll = self.rewards.float()
        rarity = relic_rarity_of(roll)
        relic = pull(self.player, rarity, from_back=False)
        remove(self.shared, relic)
        self.say(f"    rewards #{self.rewards.counter}: {roll!r}: a {rarity} relic; the front of the player's {rarity} list: {relic}")

    def refill(self, buy):
        """A place of the last shop is bought and filled again (a relic says so: The Courier). MerchantCardEntry and
        MerchantPotionEntry, RestockAfterPurchase: a card place makes a new card of its type that the shelf does not show
        (the card just bought is still on it), never on sale, with one price; a potion place rolls any potion of the
        list, also one that is on the shelf, and prices it."""
        room = self.rooms[-1]
        shelf = room.setdefault("shelf_now", {"cards": list(room["cards"]), "potions": list(room["potions"])})
        kind, slot = buy.rstrip("0123456789"), int(buy[len(buy.rstrip("0123456789")):])
        self.say(f"  {buy} is bought and its place filled again")
        if kind == "card":
            paid = shelf["cards"][slot][1]
            card_type = ("Attack", "Attack", "Skill", "Skill", "Power")[slot]
            shown = [c[0] for c in shelf["cards"]]
            options = [c for c in self.game.ironclad_cards if not c[3] and c[0] not in shown and c[2] != "Basic"]
            rarity = self._rarity("Shop", changes_offset=False)
            while not any(c[2] == rarity and c[1] == card_type for c in options):
                rarity = STEP_UP[rarity]
            items = [c for c in options if c[2] == rarity and c[1] == card_type]
            index = self.shops.int_between(0, len(items))
            card = items[index]
            self.say(f"    shops #{self.shops.counter}: place {index} of {len(items)} {rarity} {card_type} cards: {card[0]}")
            self._upgrade_roll()
            base = {"Rare": 150, "Uncommon": 75}.get(card[2], 50)
            factor = self.shops.float(f32(0.95), f32(1.05))
            price = round_even(f32(base * factor))
            self.say(f"    shops #{self.shops.counter}: factor {factor!r}; {base} x that is {f32(base * factor)!r}: {price} gold (a place filled again is never on sale)")
            shelf["cards"][slot] = (card[0], price)
            now = card[0]
        elif kind == "potion":
            paid = shelf["potions"][slot][1]
            name, rarity = self._potions(self.shops, 1, "shops")[0]
            base = {"Rare": 100, "Uncommon": 75}.get(rarity, 50)
            factor = self.shops.float(f32(0.95), f32(1.05))
            price = round_even(f32(base * factor))
            self.say(f"    shops #{self.shops.counter}: factor {factor!r}; {base} x that is {f32(base * factor)!r}: {price} gold")
            shelf["potions"][slot] = (name, price)
            now = name
        else:
            raise ValueError(buy)
        room.setdefault("refills", []).append({"buy": buy, "paid": paid, "now": now, "price": price, "after": self.counters()})

    def rest(self):
        self.say("  rest site: Rest draws nothing")
        self.rooms.append({"kind": "R", "before": self.counters(), "after": self.counters()})

    def chest(self):
        before = self.counters()
        self.say("  chest")
        roll = self.treasure.float()
        rarity = relic_rarity_of(roll)
        relic = pull(self.shared, rarity, from_back=False)
        self.say(f"    treasure_room_relics #{self.treasure.counter}: {roll!r}: a {rarity} relic; the front of the shared {rarity} list: {relic}")
        entered = self.counters()
        gold = self.rewards.int_between(42, 53)
        self.say(f"    rewards #{self.rewards.counter}: the chest pays {gold} gold (42 to 52)")
        remove(self.player, relic)     # taken: an obtained relic leaves both bags
        self.rooms.append({"kind": "T", "before": before, "entered": entered, "after": self.counters(), "rarity": rarity, "relic": relic, "gold": gold})

    def run(self, rooms):
        for n, room in enumerate(rooms):
            self.say(f"room {n + 1}: {room}   (rewards at {self.rewards.counter}, shops at {self.shops.counter}, treasure_room_relics at {self.treasure.counter})")
            if room[0] in "MEB":
                self.fight(room[0], int(room[2:]) if "+" in room else 0)
            elif room[0] == "S":
                self.shop()
                for buy in room[2:].split("/") if len(room) > 1 else []:
                    self.refill(buy)
            elif room == "R":
                self.rest()
            elif room == "T":
                self.chest()
            elif room == "A":
                self.arcane_scroll()
            elif room == "V":
                self.event_relic()
            else:
                raise ValueError(room)
        return self


# ---------- output ----------

def cs_float(x):
    return f"{x!r}f"


def cs_list(items):
    return "[" + ", ".join(f'"{i}"' for i in items) + "]"


def cs_counters(c):
    return f"new({c[0]}, {c[1]}, {c[2]})"


def print_data(game, walks):
    for walk, rooms in walks:
        player, shared = walk.bags_at_start
        print(f'        new("{walk.seed}", "{",".join(rooms)}",')
        for bag, names in ((player, BAG_RARITIES), (shared, ("Common", "Uncommon", "Rare"))):
            print("            new(")
            for i, rarity in enumerate(names):
                relics = bag[rarity]
                rows = [", ".join(f'"{r}"' for r in relics[at:at + 8]) for at in range(0, len(relics), 8)]
                print("                [" + ",\n                 ".join(rows) + "]" + ("," if i < len(names) - 1 else ""))
            print("            ),")
        print("            [")
        for r in walk.rooms:
            k = r["kind"]
            if k in "MEB":
                cards = cs_list(c[0] for c in r["cards"])
                rarities = cs_list(c[1] for c in r["cards"])
                potion = "null" if r["potion"] is None else f'"{r["potion"]}"'
                relic = "null, null" if r["relic"] is None else f'"{r["relic_rarity"]}", "{r["relic"]}"'
                print(f'                new FightRoom("{k}", {r["stolen"]}, {cs_counters(r["before"])}, {cs_counters(r["after"])}, {r["gold"]}, {potion},')
                print(f'                    {cards}, {rarities}, {relic}, {cs_float(r["offset"])}, {cs_float(r["chance"])}),')
            elif k == "S":
                print(f'                new ShopRoomTrace({cs_counters(r["before"])}, {cs_counters(r["after"])}, {r["sale"]},')
                print(f'                    {cs_list(c[0] for c in r["cards"])}, [{", ".join(str(c[1]) for c in r["cards"])}],')
                print(f'                    {cs_list(x[0] for x in r["relics"])}, {cs_list(x[1] for x in r["relics"])}, [{", ".join(str(x[2]) for x in r["relics"])}],')
                print(f'                    {cs_list(p[0] for p in r["potions"])}, [{", ".join(str(p[1]) for p in r["potions"])}], {r["removal"]}),')
                for refill in r.get("refills", []):
                    print(f'                //   a line of RoomTraces.Refilled: new("{refill["buy"]}", {refill["paid"]}, "{refill["now"]}", {refill["price"]}, {cs_counters(refill["after"])}),')
            elif k == "R":
                print(f'                new RestRoom({cs_counters(r["before"])}),')
            else:
                print(f'                new ChestRoom({cs_counters(r["before"])}, {cs_counters(r["entered"])}, {cs_counters(r["after"])}, "{r["rarity"]}", "{r["relic"]}", {r["gold"]}),')
        print("            ]),")


def read_built(path):
    built = {}
    section = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("["):
                section = line.strip("[]")
                built[section] = set()
            else:
                built[section].add(line)
    return built


def meets_only_built(game, walk, built):
    """Nothing the walk rolled on a stream is missing in the arena, and no relic list ran dry of built relics."""
    if walk.exact_zero_upgrade_roll:
        return False
    player, shared = ({k: list(v) for k, v in walk.bags_at_start[0].items()}, {k: list(v) for k, v in walk.bags_at_start[1].items()})
    for r in walk.rooms:
        k = r["kind"]
        if k in "MEB":
            if r["potion"] is not None and r["potion"] not in built["potions"]:
                return False
            if r["relic"] is not None:
                if not any(x in built["relics"] for x in player[r["relic_rarity"]]):
                    return False
                given = next(x for x in player[r["relic_rarity"]] if x in built["relics"])
                player[r["relic_rarity"]].remove(given)
        elif k == "S":
            if any(c[0] not in built["colorless"] for c in r["cards"][5:]):
                return False
            if any(p[0] not in built["potions"] for p in r["potions"]):
                return False
            for rarity, _, _ in r["relics"]:
                candidates = [x for x in reversed(player[rarity]) if x in built["relics"] and x not in game.never_sold]
                if not candidates:
                    return False
                player[rarity].remove(candidates[0])
        elif k == "T":
            if not any(x in built["relics"] for x in shared[r["rarity"]]):
                return False
    return True


SEED_LETTERS = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"   # the game's seeds have no O and no I


def seed_number(n):
    """A ten-letter seed for a whole number, so that a search can be repeated."""
    letters = []
    x = (n * 6364136223846793005 + 1442695040888963407) & M64
    x ^= x >> 29
    for _ in range(10):
        letters.append(SEED_LETTERS[x % len(SEED_LETTERS)])
        x //= len(SEED_LETTERS)
    return "".join(letters)


def float_steps(x, steps):
    """The 32-bit float that many steps above (or below) another."""
    bits = struct.unpack("I", struct.pack("f", x))[0]
    return struct.unpack("f", struct.pack("I", bits + steps if x >= 0 else bits - steps))[0]


def find_edges(game, seed):
    """One hallway fight at the start of a seed, three times, each with one of the run's two odds set so that a roll
    lands exactly ON a threshold: the cases that tell "below" from "at or below"."""
    plain = Walk(game, seed)
    potion_roll = plain.rewards.float()
    plain.rewards.double()
    card_roll = plain.rewards.float()                    # the first card's rarity roll: the third number when no potion drops
    walk = Walk(game, seed, chance=potion_roll)
    walk.fight("M")
    room = walk.rooms[0]
    print(f'        new("{seed}", "the potion roll equals the potion chance: no potion", {f32(-0.05)!r}f, {potion_roll!r}f, '
          f'{"null" if room["potion"] is None else chr(34) + room["potion"] + chr(34)}, "{room["cards"][0][1]}", "{room["cards"][0][0]}"),')
    assert potion_roll >= f32(0.4), "pick a seed whose first fight drops no potion at the starting chance"
    for what, threshold in (("Rare", lambda o: f32(f32(0.03) + o)), ("Uncommon", lambda o: f32(f32(0.37) + f32(f32(0.03) + o)))):
        guess = f32(card_roll - (0.03 if what == "Rare" else 0.4))
        offset = next((float_steps(guess, k) for k in range(-8, 9) if threshold(float_steps(guess, k)) == card_roll), None)
        if offset is None:
            print(f"        // no offset puts the {what} threshold exactly on {card_roll!r}")
            continue
        walk = Walk(game, seed, offset=offset)
        walk.fight("M")
        room = walk.rooms[0]
        below = "Uncommon" if what == "Rare" else "Common"
        assert room["cards"][0][1] == below and room["potion"] is None
        print(f'        new("{seed}", "the first card\'s roll equals the {what} threshold: {below}", {offset!r}f, {f32(0.4)!r}f, null, "{room["cards"][0][1]}", "{room["cards"][0][0]}"),'
              f'   // the roll is {card_roll!r}')


def find_exact(seed, stream, limit, thresholds):
    """The first place of a stream where the number is exactly a threshold as a 32-bit float."""
    targets = []
    for threshold in thresholds:
        t = f32(threshold)
        targets.append(((t + float_steps(t, -1)) / 2, (t + float_steps(t, 1)) / 2, t))
    rng = Rng(name_hash(seed) & 0xFFFFFFFF, stream)
    s0, s1, s2, s3 = rng.s
    found = set()
    n = 0
    while n < limit and len(found) < len(targets):       # the generator's step written out, for speed
        n += 1
        x = (s1 * 5) & M64
        x = ((x << 7) | (x >> 57)) & M64
        result = (x * 9) & M64
        t = (s1 << 17) & M64
        s2 ^= s0
        s3 ^= s1
        s1 ^= s2
        s0 ^= s3
        s2 ^= t
        s3 = ((s3 << 45) | (s3 >> 19)) & M64
        u = (result >> 11) * 1.1102230246251565e-16
        for low, high, target in targets:
            if low <= u <= high and target not in found and f32(u) == target:
                print(f"{seed} {stream} #{n}: {u!r} is {target!r} as a 32-bit float", flush=True)
                found.add(target)
    print(f"looked at {n} numbers")


def walk_record(game, path):
    """The game's own record of a run, floor by floor, against the script."""
    import json
    with open(path, encoding="utf-8") as f:
        record = json.load(f)

    def plain(model_id):                                 # CARD.TWIN_STRIKE and TwinStrike both become twinstrike
        return model_id.split(".", 1)[-1].replace("_", "").lower()

    walk = Walk(game, record["seed"])
    print(f"seed {record['seed']}: the first act is {first_act(record['seed'])}; the record says {record['acts'][0]}")
    floor = 0
    for act in record["map_point_history"]:
        for point in act:
            floor += 1
            stats = point["player_stats"][0]
            rooms = point.get("rooms", [])
            if len(rooms) != 1:
                print(f"floor {floor}: {len(rooms)} rooms on one map point: the walk stops here")
                return
            kind, model = rooms[0].get("room_type"), rooms[0].get("model_id") or ""
            cards = sorted(plain(c["card"]["id"]) for c in stats.get("card_choices", []))
            potions = sorted(plain(p["choice"]) for p in stats.get("potion_choices") or [])
            relics = sorted(plain(r["choice"]) for r in stats.get("relic_choices") or [])
            if kind in ("monster", "elite", "boss"):
                walk.fight({"monster": "M", "elite": "E", "boss": "B"}[kind], 5 if "GREMLIN_MERC" in model else 0)
                room = walk.rooms[-1]
                mine = (room["gold"], sorted(plain(c[0]) for c in room["cards"]), [plain(room["potion"])] if room["potion"] else [], [plain(room["relic"])] if room["relic"] else [])
                same = mine == (stats.get("gold_gained"), cards, potions, relics)
                print(f"floor {floor} {kind}: {room['gold']} gold, {', '.join(c[0] for c in room['cards'])}, potion {room['potion']}, relic {room['relic']}: "
                      + ("as the record" if same else f"THE RECORD HAS {stats.get('gold_gained')} gold, {cards}, {potions}, {relics}"))
            elif kind == "shop":
                walk.shop()
                room = walk.rooms[-1]
                same = (set(cards) <= {plain(c[0]) for c in room["cards"]} and sorted(plain(p[0]) for p in room["potions"]) == potions
                        and sorted(plain(x[1]) for x in room["relics"]) == relics)
                print(f"floor {floor} shop: {', '.join(c[0] for c in room['cards'])}; {', '.join(x[1] for x in room['relics'])}; {', '.join(p[0] for p in room['potions'])}: "
                      + ("as the record (which lists what was left)" if same else f"THE RECORD HAS {cards}, {relics}, {potions}"))
            elif kind == "rest_site":
                walk.rest()
                same = True
                print(f"floor {floor} rest site")
            elif kind == "treasure":
                walk.chest()
                room = walk.rooms[-1]
                same = [plain(room["relic"])] == relics and room["gold"] == stats.get("gold_gained")
                print(f"floor {floor} chest: {room['relic']}, {room['gold']} gold: " + ("as the record" if same else f"THE RECORD HAS {relics}, {stats.get('gold_gained')} gold"))
            elif floor == 1 and model.endswith("NEOW"):
                same = True
                print(f"floor 1 Neow: {relics} was taken (a relic that draws from rewards is not walked: Arcane Scroll is room A of 'trace')")
            else:
                print(f"floor {floor}: {kind} {model}: the walk stops here")
                return
            if not same:
                return


def first_act(seed):
    """ActModel.GetRandomList for the first act with everything unlocked and discovered: one pick between Overgrowth and Underdocks."""
    return ("Overgrowth", "Underdocks")[Rng(name_hash(seed) & 0xFFFFFFFF, "act_selection").int_between(0, 2)]


def find_ties(seed, count):
    number = name_hash(seed) & 0xFFFFFFFF
    rng = Rng(number + 0, "shops")
    narrow = (f32(0.95), f32(1.05))
    wide = (f32(0.85), f32(1.15))
    bases = [(50, narrow, "a Common card or potion"), (75, narrow, "an Uncommon card or potion"), (150, narrow, "a Rare card"),
             (100, narrow, "a Rare potion"), (86, narrow, "an Uncommon colorless card"), (172, narrow, "a Rare colorless card"),
             (175, wide, "a Common relic"), (225, wide, "an Uncommon relic"), (275, wide, "a Rare relic"), (200, wide, "a Shop relic")]
    for _ in range(count):
        u = rng.double()
        for base, (low, high), what in bases:
            factor = f32(u * f32(high - low) + low)
            exact = base * factor                      # exact in a double: two 24-bit numbers
            product = f32(exact)
            if product - int(product) != 0.5:
                continue
            even = round_even(product)
            up = int(product) + 1
            in_doubles = round_even(exact) if exact - int(exact) != 0.5 else even
            # the same factor worked out without the float subtraction of the range
            other_factor = f32(u * (high - low) + low)
            print(f"shops #{rng.counter} as the price of {what} ({base}): factor {factor!r}, product {exact!r}, as a float {product!r}: "
                  f"{even} gold; half-up would say {up}; 64-bit arithmetic would say {in_doubles}"
                  + ("" if other_factor == factor else f"; a factor made with a double width is {other_factor!r}"))


def other_readings(exact):
    """What the same product gives under the two rules the game does not use: a half always up, and 64-bit arithmetic."""
    product = f32(exact)
    half_up = int(product) + 1 if product - int(product) >= 0.5 else int(product)
    return half_up, round_even(exact)


def find_shop_ties(game, seed, built, numbers, wanted, skip=()):
    """Shops of one seed, each rolled from another place of its two streams, in which the price of the first character
    card, the first relic or the first potion is a tie that tells the game's arithmetic from another."""
    number = name_hash(seed) & 0xFFFFFFFF
    rng = Rng(number + 0, "shops")
    narrow, wide = f32(f32(1.05) - f32(0.95)), f32(f32(1.15) - f32(0.85))
    found = {"card": 0, "relic": 0, "potion": 0}
    for kind in skip:
        found[kind] = wanted
    states = {}                         # the stream's state before each of the last numbers
    for _ in range(numbers):
        states[rng.counter] = list(rng.s)
        states.pop(rng.counter - 30, None)
        u = rng.double()
        at = rng.counter
        for kind, place, width, low, bases in (("card", 3, narrow, f32(0.95), (50, 75, 150)), ("relic", 17, wide, f32(0.85), (175, 225, 275)),
                                               ("potion", 26, narrow, f32(0.95), (50, 75, 100))):
            if found[kind] >= wanted or at < place:
                continue
            factor = f32(u * width + low)
            if not any(f32(b * factor) - int(f32(b * factor)) == 0.5 for b in bases):
                continue
            for rewards_at in range(0, 12):
                walk = Walk(game, seed, rewards_at=rewards_at, shops_at=at - place, shops_state=states[at - place])
                walk.shop()
                room = walk.rooms[0]
                counter, base, factor, exact, price = next(p for p in walk.prices if p[0] == at)
                if f32(exact) - int(f32(exact)) != 0.5:
                    continue
                half_up, in_doubles = other_readings(exact)
                if half_up == price and in_doubles == price:
                    continue
                if kind == "card" and room["sale"] == 0:
                    continue
                if kind != "card" and any(c[0] not in built["colorless"] for c in room["cards"][5:]):
                    continue
                if kind == "potion" and any(p[0] not in built["potions"] for p in room["potions"]):
                    continue
                thing = room["cards"][0][0] if kind == "card" else room["relics"][0][0] if kind == "relic" else room["potions"][0][0]
                print(f'        new("{seed}", {rewards_at}, {at - place}, "{kind}", "{thing}", {base}, {factor!r}f, {price}, {half_up}, {in_doubles}),'
                      f'   // {base} x {factor!r} is {exact!r}, as a 32-bit float {f32(exact)!r}')
                found[kind] += 1
                break
        if all(n >= wanted for n in found.values()):
            return


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    game = Game(sys.argv[1])
    command = sys.argv[2]
    if command == "trace":
        seed, rooms = sys.argv[3], sys.argv[4].split(",")
        start = dict(arg.split("=") for arg in sys.argv[5:])
        print(f"seed {seed}: the first act is {first_act(seed)}")
        Walk(game, seed, print, rewards_at=int(start.get("rewards", 0)), shops_at=int(start.get("shops", 0)), treasure_at=int(start.get("treasure", 0)),
             offset=f32(float(start["offset"])) if "offset" in start else None, chance=f32(float(start["chance"])) if "chance" in start else None).run(rooms)
    elif command == "data":
        walks = []
        for arg in sys.argv[3:]:
            seed, rooms = arg.split("=")
            walks.append((Walk(game, seed).run(rooms.split(",")), rooms.split(",")))
        print_data(game, walks)
    elif command == "find":
        rooms = sys.argv[3].split(",")
        built = read_built(sys.argv[4])
        wanted = int(sys.argv[5]) if len(sys.argv) > 5 else 3
        n = int(sys.argv[6]) if len(sys.argv) > 6 else 0
        found = 0
        while found < wanted:
            seed = seed_number(n)
            n += 1
            if first_act(seed) != "Overgrowth":
                continue
            walk = Walk(game, seed).run(rooms)
            if meets_only_built(game, walk, built):
                potions = sum(1 for r in walk.rooms if r.get("potion"))
                print(f"{seed}  (number {n - 1}; {potions} potions dropped)")
                found += 1
    elif command == "shop-ties":
        find_shop_ties(game, sys.argv[3], read_built(sys.argv[4]), int(sys.argv[5]) if len(sys.argv) > 5 else 3000000,
                       int(sys.argv[6]) if len(sys.argv) > 6 else 4, sys.argv[7].split(",") if len(sys.argv) > 7 else ())
    elif command == "exact":
        find_exact(sys.argv[3], sys.argv[4], int(sys.argv[5]), [float(x) for x in sys.argv[6:]])
    elif command == "record":
        walk_record(game, sys.argv[3])
    elif command == "edges":
        find_edges(game, sys.argv[3])
    elif command == "ties":
        find_ties(sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 2000000)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main()
