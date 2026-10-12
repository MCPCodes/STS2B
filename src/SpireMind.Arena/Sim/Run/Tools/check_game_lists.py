#!/usr/bin/env python3
"""A second reading of the game's lists, held against GameLists.cs.

    python3 -I check_game_lists.py <decompiled game folder> <path to GameLists.cs>

GameLists.cs is made by game_lists.py. This script was written by another reader and shares
nothing with it: it opens every class file of the decompiled game on its own and takes from it
what a run's generation reads (names, numbers and which epoch gates what; nothing is copied):

  - the shared relic pool and each character's pool, in the game's order, with every relic's
    rarity (from the relic's class) and the epoch that gates it (from the pool's own unlock test
    and the epochs' relic lists);
  - each act: its index, whether it is a default act, what unlocks it, its numbers of weak
    encounters and of rooms, its events, its encounters with room type, weak flag and tags,
    its Ancients with their gates, its order of introducing bosses;
  - the shared events and the ids of all epochs.

It prints "0 differences" when GameLists.cs says the same, else every difference with both
sides. Run it after game_lists.py on a new build of the game. It does not look at EventEpochs
or SharedAncients (three events and one Ancient, checked by hand against Event1Epoch to
Event3Epoch and UnlockState.SharedAncients).
"""
import glob
import os
import re
import sys

if len(sys.argv) != 3:
    sys.exit(__doc__)
GAME = sys.argv[1]
with open(sys.argv[2], encoding="utf-8") as handle:
    LISTS = handle.read()

problems = []


def src(folder, name):
    with open(os.path.join(GAME, folder, name + ".cs"), encoding="utf-8") as f:
        return f.read()


def body_of(text, signature):
    """The text of the member whose declaration holds `signature`: its braces, balanced, or its arrow expression."""
    at = text.index(signature)
    brace = text.index("{", at)
    arrow = text.find("=>", at)
    if arrow != -1 and arrow < brace and ";" in text[arrow:brace]:
        return text[arrow:text.index(";", arrow)]
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[brace:i + 1]
    raise ValueError(signature)


def same(what, game, lists):
    if game != lists:
        problems.append(f"{what}:\n  game : {game}\n  lists: {lists}")


def epoch_id(class_name):
    if class_name is None:
        return None
    return re.search(r'string Id => "(\w+)"', src("MegaCrit.Sts2.Core.Timeline.Epochs", class_name)).group(1)


# ---------- relics ----------

def relic_rarity(name):
    found = re.search(r"RelicRarity\s+Rarity\s*=>\s*RelicRarity\.(\w+)", src("MegaCrit.Sts2.Core.Models.Relics", name))
    return found.group(1) if found else None


# Which epoch classes list which relics.
epochs_of_relic = {}
for path in glob.glob(os.path.join(GAME, "MegaCrit.Sts2.Core.Timeline.Epochs", "*.cs")):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    if "List<RelicModel> Relics" not in text:
        continue
    for relic in re.findall(r"ModelDb\.Relic<(\w+)>\(\)", body_of(text, "List<RelicModel> Relics")):
        epochs_of_relic.setdefault(relic, []).append(os.path.basename(path)[:-3])


def pool(file):
    text = src("MegaCrit.Sts2.Core.Models.RelicPools", file)
    names = re.findall(r"ModelDb\.Relic<(\w+)>\(\)", body_of(text, "GenerateAllRelics()"))
    # Only the epochs the pool itself asks about gate its relics.
    asked = set(re.findall(r"IsEpochRevealed<(\w+)>", body_of(text, "GetUnlockedRelics("))) if "GetUnlockedRelics(" in text else set()
    entries = []
    for name in names:
        gates = [e for e in epochs_of_relic.get(name, []) if e in asked]
        if len(gates) > 1:
            problems.append(f"{name} is gated by two epochs: {gates}")
        entries.append((name, relic_rarity(name), epoch_id(gates[0]) if gates else None))
    return entries


def listed_relics(block):
    return [(m.group(1), m.group(2), m.group(3)) for m in re.finditer(r'new\("(\w+)", RelicRarity\.(\w+)(?:, "(\w+)")?\)', block)]


same("shared relic pool", pool("SharedRelicPool"), listed_relics(LISTS[LISTS.index("SharedRelics ="):LISTS.index("CharacterRelics =")]))
for character in ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect"]:
    start = LISTS.index(f'["{character}"] =')
    same(f"{character} relic pool", pool(character + "RelicPool"), listed_relics(LISTS[start:LISTS.index("],", start)]))


# ---------- acts ----------

def encounter(name):
    text = src("MegaCrit.Sts2.Core.Models.Encounters", name)
    room = re.search(r"RoomType\s+RoomType\s*=>\s*RoomType\.(\w+)", text).group(1)
    weak = bool(re.search(r"bool\s+IsWeak\s*=>\s*true", text))
    tags = re.findall(r"EncounterTag\.(\w+)", body_of(text, " Tags")) if re.search(r"\bTags\b\s*(=>|\{)", text) else []
    return (name, room, weak, tuple(tags))


for act in ["Overgrowth", "Underdocks", "Hive", "Glory"]:
    text = src("MegaCrit.Sts2.Core.Models.Acts", act)
    events = re.findall(r"ModelDb\.Event<(\w+)>\(\)", body_of(text, "AllEvents"))
    encounters = [encounter(n) for n in re.findall(r"ModelDb\.Encounter<(\w+)>\(\)", body_of(text, "GenerateAllEncounters()"))]
    boss_order = re.findall(r"ModelDb\.Encounter<(\w+)>\(\)", body_of(text, "BossDiscoveryOrder"))
    ancients = re.findall(r"ModelDb\.AncientEvent<(\w+)>\(\)", body_of(text, "AllAncients"))
    gate_text = body_of(text, "GetUnlockedAncients(")
    gate_of = {ancient: epoch for epoch, ancient in re.findall(r"IsEpochRevealed<(\w+)>\(\)\)\s*\{\s*list\.Remove\(ModelDb\.AncientEvent<(\w+)>", gate_text)}
    weak = int(re.search(r"NumberOfWeakEncounters\s*=>\s*(\d+)", text).group(1))
    rooms = int(re.search(r"BaseNumberOfRooms\s*=>\s*(\d+)", text).group(1))
    index = int(re.search(r"int Index\s*=>\s*(\d+)", text).group(1))
    default = re.search(r"bool IsDefault\s*=>\s*(\w+)", text).group(1)
    unlock = re.search(r"IsUnlocked\(UnlockState unlockState\)\s*\{\s*return ([^;]+);", text).group(1)
    asked = re.search(r"IsEpochRevealed<(\w+)>", unlock)
    unlock = "null" if unlock == "true" else '"' + epoch_id(asked.group(1)) + '"'

    start = LISTS.index(f'new("{act}", Index:')
    following = re.search(r'new\("\w+", Index:', LISTS[start + 10:])
    block = LISTS[start:start + 10 + following.start()] if following else LISTS[start:LISTS.index("\n    ];", start)]
    head = re.search(r'Index: (\d+), IsDefault: (\w+), UnlockEpoch: (null|"\w+"), WeakEncounters: (\d+), Rooms: (\d+)', block)
    same(f"{act}: index, default, unlock, weak encounters, rooms", (index, default, unlock, weak, rooms),
         (int(head.group(1)), head.group(2), head.group(3), int(head.group(4)), int(head.group(5))))
    same(f"{act} events", events, re.findall(r'"(\w+)"', block[block.index(" Events:"):block.index(" Encounters:")]))
    listed = [(m.group(1), m.group(2), m.group(3) == "true", tuple(re.findall(r'"(\w+)"', m.group(4))))
              for m in re.finditer(r'new\("(\w+)", RoomType\.(\w+), (true|false)((?:, "\w+")*)\)', block[block.index(" Encounters:"):block.index("Ancients:")])]
    same(f"{act} encounters", encounters, listed)
    listed_ancients = [(m.group(1), m.group(2)) for m in re.finditer(r'new\("(\w+)"(?:, "(\w+)")?\)', block[block.index("Ancients:"):block.index("BossDiscoveryOrder:")])]
    same(f"{act} Ancients", [(a, epoch_id(gate_of.get(a))) for a in ancients], listed_ancients)
    same(f"{act} boss order", boss_order, re.findall(r'"(\w+)"', block[block.index("BossDiscoveryOrder:"):]))


# ---------- shared events, epochs ----------

model_db = src("MegaCrit.Sts2.Core.Models", "ModelDb")
same("shared events", re.findall(r"Event<(\w+)>\(\)", body_of(model_db, "IEnumerable<EventModel> AllSharedEvents")),
     re.findall(r'"(\w+)"', LISTS[LISTS.index("SharedEvents ="):LISTS.index("EventEpochs =")]))
ids = []
for path in glob.glob(os.path.join(GAME, "MegaCrit.Sts2.Core.Timeline.Epochs", "*.cs")):
    with open(path, encoding="utf-8") as f:
        ids.append(re.search(r'string Id => "(\w+)"', f.read()).group(1))
same("epochs", sorted(ids), sorted(re.findall(r'"(\w+_EPOCH)"', LISTS[LISTS.index("Epochs ="):LISTS.index("SharedRelics =")])))

print(f"{len(problems)} differences")
for problem in problems:
    print(problem)
sys.exit(1 if problems else 0)
