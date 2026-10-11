"""Makes Sim/Run/GameLists.cs from the decompiled game: the relic pools with each relic's rarity
and epoch, the four acts with their events, encounters (room type, weak flag, tags), Ancients
and boss order, the shared events, the epochs. It reads names and numbers; no game code is
copied. Run it again after a game update, then run the selfcheck (RunStreamsChecks counts
every list and rebuilds real saves from them).

    python3 -I game_lists.py <decompiled game folder> <path of GameLists.cs>
"""
import re, os, sys
R = sys.argv[1]
def read(p): return open(os.path.join(R, p), encoding='utf-8-sig').read()
def generic_list(text, kind):
    return re.findall(r'ModelDb\.%s<(\w+)>\(\)' % kind, text)
out = {}
# relic pools
pools = {}
for f in sorted(os.listdir(R + '/MegaCrit.Sts2.Core.Models.RelicPools')):
    t = read('MegaCrit.Sts2.Core.Models.RelicPools/' + f)
    m = re.search(r'GenerateAllRelics\(\)\s*\{(.*?)\n\t\}', t, re.S)
    names = generic_list(m.group(1), 'Relic') if m else []
    epochs = re.findall(r'IsEpochRevealed<(\w+)>', t)
    pools[f[:-3]] = {'relics': names, 'epochs': epochs}
out['relic_pools'] = pools
# relic rarities
rar = {}
for f in sorted(os.listdir(R + '/MegaCrit.Sts2.Core.Models.Relics')):
    t = read('MegaCrit.Sts2.Core.Models.Relics/' + f)
    m = re.search(r'RelicRarity Rarity => RelicRarity\.(\w+)', t)
    rar[f[:-3]] = m.group(1) if m else None
out['relic_rarity'] = rar
# epochs
ep = {}
for f in sorted(os.listdir(R + '/MegaCrit.Sts2.Core.Timeline.Epochs')):
    t = read('MegaCrit.Sts2.Core.Timeline.Epochs/' + f)
    idm = re.search(r'string Id => "(\w+)"', t)
    e = {'id': idm.group(1) if idm else None}
    for kind, key in (('Relic', 'relics'), ('Event', 'events')):
        m = re.search(r'static List<%sModel> %ss\s*\{(.*?)\n\t\}' % (kind, kind), t, re.S)
        if m: e[key] = generic_list(m.group(1), kind)
    ep[f[:-3]] = e
out['epochs'] = ep
# acts
acts = {}
for a in ['Overgrowth', 'Underdocks', 'Hive', 'Glory']:
    t = read('MegaCrit.Sts2.Core.Models.Acts/%s.cs' % a)
    d = {}
    d['boss_discovery'] = generic_list(re.search(r'BossDiscoveryOrder =>(.*?);', t, re.S).group(1), 'Encounter')
    d['ancients'] = generic_list(re.search(r'AllAncients =>(.*?);', t, re.S).group(1), 'AncientEvent')
    d['events'] = generic_list(re.search(r'AllEvents =>(.*?);', t, re.S).group(1), 'Event')
    d['weak'] = int(re.search(r'NumberOfWeakEncounters => (\d+)', t).group(1))
    d['rooms'] = int(re.search(r'BaseNumberOfRooms => (\d+)', t).group(1))
    d['index'] = int(re.search(r'int Index => (\d+)', t).group(1))
    d['default'] = re.search(r'bool IsDefault => (\w+)', t).group(1)
    encs = generic_list(re.search(r'GenerateAllEncounters\(\)\s*\{(.*?)\n\t\}', t, re.S).group(1), 'Encounter')
    el = []
    for e in encs:
        et = read('MegaCrit.Sts2.Core.Models.Encounters/%s.cs' % e)
        rt = re.search(r'RoomType RoomType => RoomType\.(\w+)', et).group(1)
        wk = re.search(r'bool IsWeak => (\w+)', et)
        tg = re.search(r'IEnumerable<EncounterTag> Tags =>(.*?);', et, re.S)
        tags = re.findall(r'EncounterTag\.(\w+)', tg.group(1)) if tg else []
        el.append({'name': e, 'room': rt, 'weak': (wk.group(1) if wk else 'false'), 'tags': tags})
    d['encounters'] = el
    acts[a] = d
out['acts'] = acts
G = out
ep_id = {k: v['id'] for k, v in G['epochs'].items()}
relic_epoch = {}
for k, v in G['epochs'].items():
    for r in v.get('relics', []): relic_epoch[r] = v['id']
event_epoch = {}
for k, v in G['epochs'].items():
    for e in v.get('events', []): event_epoch[e] = v['id']
# check that each pool only gates by the epochs its own code names
for pool, p in G['relic_pools'].items():
    named = {ep_id[e] for e in p['epochs']}
    for r in p['relics']:
        if r in relic_epoch: assert relic_epoch[r] in named, (pool, r)
def q(s): return '"%s"' % s
def relic_line(names):
    out = []
    for r in names:
        e = relic_epoch.get(r)
        out.append('new(%s, RelicRarity.%s%s)' % (q(r), G['relic_rarity'][r], (', ' + q(e)) if e else ''))
    return out
def wrap(items, indent, width=150):
    lines = []; cur = indent
    for it in items:
        piece = it + ', '
        if len(cur) + len(piece) > width and cur.strip():
            lines.append(cur.rstrip()); cur = indent
        cur += piece
    if cur.strip(): lines.append(cur.rstrip())
    return '\n'.join(lines)
all_epochs = re.findall(r'GetId<(\w+)>\(\)', open(R + '/MegaCrit.Sts2.Core.Timeline/EpochModel.cs').read().split('EpochIds =>')[1].split('});')[0])
act_unlock = {'Underdocks': 'UNDERDOCKS_EPOCH'}
anc_epoch = {('Overgrowth', 'Neow'): 'NEOW_EPOCH', ('Underdocks', 'Neow'): 'NEOW_EPOCH', ('Hive', 'Orobas'): 'OROBAS_EPOCH'}
o = []
o.append('''namespace SpireMind.Arena;

// The game's own lists (v0.107.1), by class name and in the game's order. A random pick or a
// shuffle of the game runs over one of these lists, so the order and the completeness matter:
// one name too few shifts every pick after it. Read from the game's files by script
// (pools, acts, epochs; the rarity from each relic's class); names and numbers only.
// Made by Sim/Run/Tools/game_lists.py: run it again after a game update, never edit by hand.
// RunStreamsChecks counts every list.

/// <summary>A relic's rarity, with the game's member order.</summary>
public enum RelicRarity { None, Starter, Common, Uncommon, Rare, Shop, Event, Ancient }

/// <summary>One relic of a pool. <paramref name="Epoch"/> is the timeline epoch a profile must have
/// revealed for the relic to be in its runs, or null when it is always there.</summary>
public sealed record GameRelic(string ClassName, RelicRarity Rarity, string? Epoch = null);

/// <summary>One encounter of an act: its room type, whether it is one of the act's weak
/// encounters, and its tags (the act's lists keep two encounters that share a tag apart).</summary>
public sealed record GameEncounter(string ClassName, RoomType Room, bool IsWeak, params string[] Tags)
{
    public bool SharesTagsWith(GameEncounter? other) => other is not null && Tags.Intersect(other.Tags).Any();
}

/// <summary>One Ancient an act can open with, and the epoch that unlocks it (null: always).</summary>
public sealed record GameAncient(string ClassName, string? Epoch = null);

/// <summary>One act as the game defines it.</summary>
/// <param name="Index">0 for a first act, 1 for a second, 2 for a third.</param>
/// <param name="IsDefault">The act a profile has from the start (Overgrowth, Hive, Glory).</param>
/// <param name="UnlockEpoch">The epoch that unlocks the act, or null.</param>
/// <param name="WeakEncounters">How many entries of the hallway list come from the weak encounters.</param>
/// <param name="Rooms">The length of the hallway list (the act's rooms between Ancient and boss, single player).</param>
/// <param name="Events">The act's own events in the game's order; the shared events follow them in a run's list.</param>
/// <param name="Encounters">Every encounter of the act in the game's order, which is the order the bags are filled in.</param>
/// <param name="BossDiscoveryOrder">A profile that has not met one of these gets the first unmet one as the boss.</param>
public sealed record GameAct(string ClassName, int Index, bool IsDefault, string? UnlockEpoch, int WeakEncounters, int Rooms,
    IReadOnlyList<string> Events, IReadOnlyList<GameEncounter> Encounters, IReadOnlyList<GameAncient> Ancients, IReadOnlyList<string> BossDiscoveryOrder)
{
    public IEnumerable<GameEncounter> Weak => Encounters.Where(e => e.Room == RoomType.Monster && e.IsWeak);
    public IEnumerable<GameEncounter> Regular => Encounters.Where(e => e.Room == RoomType.Monster && !e.IsWeak);
    public IEnumerable<GameEncounter> Elites => Encounters.Where(e => e.Room == RoomType.Elite);
    public IEnumerable<GameEncounter> Bosses => Encounters.Where(e => e.Room == RoomType.Boss);
}

public static class GameLists
{
    public const string Overgrowth = "Overgrowth";
    public const string Underdocks = "Underdocks";
    public const string Hive = "Hive";
    public const string Glory = "Glory";
''')
o.append('    /// <summary>The %d timeline epochs, by the id a save uses.</summary>' % len(all_epochs))
o.append('    public static readonly IReadOnlyList<string> Epochs =\n    [\n' + wrap([q(ep_id[e]) for e in all_epochs], '        ') + '\n    ];\n')
o.append('    /// <summary>The shared relic pool: %d relics in the game\'s order. A run\'s shared bag is filled from all of them,\n    /// the player\'s bag from those of the four bag rarities.</summary>' % len(G['relic_pools']['SharedRelicPool']['relics']))
o.append('    public static readonly IReadOnlyList<GameRelic> SharedRelics =\n    [\n' + wrap(relic_line(G['relic_pools']['SharedRelicPool']['relics']), '        ') + '\n    ];\n')
o.append('    /// <summary>Each character\'s own relic pool, 8 relics in the game\'s order (the first Starter one is its starting relic).</summary>')
o.append('    public static readonly IReadOnlyDictionary<string, IReadOnlyList<GameRelic>> CharacterRelics = new Dictionary<string, IReadOnlyList<GameRelic>>(StringComparer.Ordinal)\n    {')
for ch in ['Ironclad', 'Silent', 'Regent', 'Necrobinder', 'Defect']:
    o.append('        [%s] =\n        [\n' % q(ch) + wrap(relic_line(G['relic_pools'][ch + 'RelicPool']['relics']), '            ') + '\n        ],')
o.append('    };\n')
shared_events = "BrainLeech CrystalSphere DollRoom FakeMerchant PotionCourier RanwidTheElder RelicTrader RoomFullOfCheese SelfHelpBook SlipperyBridge StoneOfAllTime Symbiote TeaMaster TheFutureOfPotions TheLegendsWereTrue ThisOrThat WarHistorianRepy WelcomeToWongos".split()
o.append('    /// <summary>The %d events every act adds after its own, in the game\'s order.</summary>' % len(shared_events))
o.append('    public static readonly IReadOnlyList<string> SharedEvents =\n    [\n' + wrap([q(e) for e in shared_events], '        ') + '\n    ];\n')
o.append('    /// <summary>Events a profile only meets once their epoch is revealed.</summary>')
o.append('    public static readonly IReadOnlyDictionary<string, string> EventEpochs = new Dictionary<string, string>(StringComparer.Ordinal)\n    {\n' + '\n'.join('        [%s] = %s,' % (q(e), q(ep)) for e, ep in sorted(event_epoch.items(), key=lambda kv: kv[1])) + '\n    };\n')
o.append('    /// <summary>The Ancients that belong to no act; a run deals them to its second and third act.</summary>')
o.append('    public static readonly IReadOnlyList<GameAncient> SharedAncients = [new("Darv", "DARV_EPOCH")];\n')
o.append('    /// <summary>The four acts in the game\'s order. Two have index 0: a run rolls one of them as its first act.</summary>')
o.append('    public static readonly IReadOnlyList<GameAct> Acts =\n    [')
for a in ['Overgrowth', 'Underdocks', 'Hive', 'Glory']:
    A = G['acts'][a]
    encs = []
    for e in A['encounters']:
        rt = {'Monster': 'RoomType.Monster', 'Elite': 'RoomType.Elite', 'Boss': 'RoomType.Boss'}[e['room']]
        encs.append('new(%s, %s, %s%s)' % (q(e['name']), rt, e['weak'], ''.join(', ' + q(t) for t in e['tags'])))
    ancs = ['new(%s%s)' % (q(x), (', ' + q(anc_epoch[(a, x)])) if (a, x) in anc_epoch else '') for x in A['ancients']]
    o.append('        new(%s, Index: %d, IsDefault: %s, UnlockEpoch: %s, WeakEncounters: %d, Rooms: %d,' % (q(a), A['index'], A['default'], q(act_unlock[a]) if a in act_unlock else 'null', A['weak'], A['rooms']))
    o.append('            Events:\n            [\n' + wrap([q(e) for e in A['events']], '                ') + '\n            ],')
    o.append('            Encounters:\n            [\n' + wrap(encs, '                ') + '\n            ],')
    o.append('            Ancients: [' + ', '.join(ancs) + '],')
    o.append('            BossDiscoveryOrder: [' + ', '.join(q(b) for b in A['boss_discovery']) + ']),')
o.append('    ];\n')
o.append('''    private static readonly Dictionary<string, GameRelic> RelicsByName =
        SharedRelics.Concat(CharacterRelics.Values.SelectMany(r => r)).ToDictionary(r => r.ClassName, StringComparer.Ordinal);

    /// <summary>The act of a class name.</summary>
    /// <exception cref="ArgumentException">No act has that name.</exception>
    public static GameAct Act(string className) =>
        Acts.FirstOrDefault(a => a.ClassName == className) ?? throw new ArgumentException($"'{className}' is not an act ({string.Join(", ", Acts.Select(a => a.ClassName))}).", nameof(className));

    /// <summary>The acts a run can have at each act index, in the game's order: [Overgrowth, Underdocks], [Hive], [Glory].</summary>
    public static IReadOnlyList<IReadOnlyList<GameAct>> ActsByIndex { get; } =
        Enumerable.Range(0, Acts.Max(a => a.Index) + 1).Select(i => (IReadOnlyList<GameAct>)Acts.Where(a => a.Index == i).ToList()).ToList();

    /// <summary>A relic of the shared pool or of a character's pool, by class name.</summary>
    /// <exception cref="ArgumentException">The name is in neither (an event or Ancient relic never sits in a bag).</exception>
    public static GameRelic BagRelic(string className) =>
        RelicsByName.TryGetValue(className, out var relic) ? relic
            : throw new ArgumentException($"'{className}' is not a relic of the shared pool or of a character's pool.", nameof(className));

    public static bool IsBagRelic(string className) => RelicsByName.ContainsKey(className);
}''')
open(sys.argv[2], 'w').write('\n'.join(o) + '\n')
print(len(all_epochs), 'epochs;', event_epoch, len(G['relic_pools']['SharedRelicPool']['relics']))
