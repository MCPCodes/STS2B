"""Makes Sim/Run/CardPoolLists.cs from the decompiled game: the seven card pools an Ironclad run
can draw from (the Ironclad's, colorless, curse, event, quest, status, token), each card with
its type, its rarity, whether it is multiplayer only, whether a fight may generate it, whether
a "random card" effect outside a fight may make it, and the timeline epoch that unlocks it. It reads names and numbers; no game code is copied. Run it
again after a game update, then run the selfcheck (CardPoolsChecks counts every list).

    python3 -I card_pools.py <decompiled game folder> <path of CardPoolLists.cs>
"""
import re, os, sys
R = sys.argv[1]
def read(p): return open(os.path.join(R, p), encoding='utf-8-sig').read()
def cards_in(text): return re.findall(r'ModelDb\.Card<(\w+)>\(\)', text)

# every pool of the game, in the order a card's pool is looked up (the five characters, then the shared pools)
ORDER = ['Ironclad', 'Silent', 'Regent', 'Necrobinder', 'Defect', 'Colorless', 'Curse', 'Deprecated', 'Event', 'Quest', 'Status', 'Token']
WANTED = ['Ironclad', 'Colorless', 'Curse', 'Event', 'Quest', 'Status', 'Token']
pools, pool_epochs = {}, {}
for name in ORDER:
    t = read('MegaCrit.Sts2.Core.Models.CardPools/%sCardPool.cs' % name)
    m = re.search(r'GenerateAllCards\(\)\s*\{(.*?)\n\t\}', t, re.S)
    pools[name] = cards_in(m.group(1))
    pool_epochs[name] = re.findall(r'IsEpochRevealed<(\w+)>', t)

# a card is in one pool only (the game takes the first pool that holds it)
seen = {}
for name in ORDER:
    for c in pools[name]:
        assert c not in seen, (c, seen[c], name)
        seen[c] = name

# the epochs that unlock cards
epoch_id, card_epoch = {}, {}
for f in sorted(os.listdir(R + '/MegaCrit.Sts2.Core.Timeline.Epochs')):
    t = read('MegaCrit.Sts2.Core.Timeline.Epochs/' + f)
    idm = re.search(r'string Id => "(\w+)"', t)
    if idm: epoch_id[f[:-3]] = idm.group(1)
    m = re.search(r'static List<CardModel> Cards\s*\{(.*?)\n\t\}', t, re.S)
    if m:
        for c in cards_in(m.group(1)):
            assert c not in card_epoch, c
            card_epoch[c] = f[:-3]
# a pool gates only by the epochs its own code names
for name in ORDER:
    for c in pools[name]:
        if c in card_epoch: assert card_epoch[c] in pool_epochs[name], (name, c)

def card(c):
    t = read('MegaCrit.Sts2.Core.Models.Cards/%s.cs' % c)
    m = re.search(r':\s*base\(\s*(-?\d+)\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)\s*,\s*TargetType\.(\w+)', t)
    assert m, c
    mp = re.search(r'MultiplayerConstraint => CardMultiplayerConstraint\.(\w+)', t)
    constraint = mp.group(1) if mp else 'None'
    assert constraint in ('None', 'MultiplayerOnly'), (c, constraint)   # no single-player-only card is in these pools
    gen = re.search(r'bool CanBeGeneratedInCombat => (\w+)', t)
    generated = (gen.group(1) == 'true') if gen else True
    mod = re.search(r'bool CanBeGeneratedByModifiers => (\w+)', t)
    by_modifiers = (mod.group(1) == 'true') if mod else True
    return {'name': c, 'type': m.group(2), 'rarity': m.group(3), 'multi': constraint == 'MultiplayerOnly', 'generated': generated,
            'by_modifiers': by_modifiers, 'epoch': epoch_id[card_epoch[c]] if c in card_epoch else None}

def line(c):
    d = card(c)
    s = 'new("%s", CardType.%s, CardRarity.%s' % (d['name'], d['type'], d['rarity'])
    if d['multi']: s += ', MultiplayerOnly: true'
    if not d['generated']: s += ', GeneratedInCombat: false'
    if not d['by_modifiers']: s += ', GeneratedByModifiers: false'
    if d['epoch']: s += ', Epoch: "%s"' % d['epoch']
    return s + ')'

def wrap(items, indent, width=150):
    lines = []; cur = indent
    for it in items:
        piece = it + ', '
        if len(cur) + len(piece) > width and cur.strip():
            lines.append(cur.rstrip()); cur = indent
        cur += piece
    if cur.strip(): lines.append(cur.rstrip())
    return '\n'.join(lines)

WHAT = {
    'Ironclad': "The Ironclad's own cards: card rewards, shops, and what a transformed Ironclad card becomes.",
    'Colorless': 'The colorless cards: a shop\'s two colorless slots, and what a transformed quest card or Event-, Ancient- or Token-rarity card becomes.',
    'Curse': 'The curses. A transformed curse becomes any other of them.',
    'Event': 'Cards only events and Ancients give.',
    'Quest': 'The quest cards.',
    'Status': 'The status cards monsters and cards make in a fight.',
    'Token': 'Cards only other cards and relics make.',
}
o = []
o.append('''namespace SpireMind.Arena;

// The game's card pools (v0.107.1), by class name and in the game's order. A random pick of the
// game runs over one of these lists, so the order and the completeness matter: one name too few
// shifts every pick after it. Read from the game's files by script (each pool's list, each
// card's type, rarity and flags from its class, the epochs' card lists); names and numbers only.
// Made by Sim/Run/Tools/card_pools.py: run it again after a game update, never edit by hand.
// CardPoolsChecks counts every list.

/// <summary>
/// One card of a game pool, whether the arena has built it or not: what the game's own filters
/// ask of a card before a random pick: type, rarity, multiplayer only, may a fight generate it
/// (the game's CanBeGeneratedInCombat), may a random pick outside a fight make it (the game's
/// CanBeGeneratedByModifiers: Neow's Bones picks its curses among those), and the timeline
/// epoch a profile must have revealed.
/// </summary>
public sealed record GameCard(string ClassName, CardType Type, CardRarity Rarity, bool MultiplayerOnly = false, bool GeneratedInCombat = true,
    bool GeneratedByModifiers = true, string? Epoch = null);

/// <summary>The lists <see cref="CardPools"/> is built from. Use the pools, not these.</summary>
internal static class CardPoolLists
{
    /// <summary>The game looks a card's pool up in this order (the five characters' pools first, then the shared ones) and takes the first that holds it.</summary>
    public static readonly IReadOnlyList<string> GameOrder = [%s];
''' % ', '.join('"%s"' % n for n in ORDER))
for name in WANTED:
    o.append('    /// <summary>%s %d cards.</summary>' % (WHAT[name], len(pools[name])))
    o.append('    public static readonly IReadOnlyList<GameCard> %s =\n    [\n' % name + wrap([line(c) for c in pools[name]], '        ') + '\n    ];\n')
o.append('}')
open(sys.argv[2], 'w').write('\n'.join(o) + '\n')
print({n: len(pools[n]) for n in ORDER})
