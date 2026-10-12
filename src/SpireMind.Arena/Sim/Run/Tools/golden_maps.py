"""Makes Sim/Run/MapGoldenData.cs from real saves of the game: the saved map of the first act of
each of the five seeds (the save's own JSON, unchanged), the path of the one run that was
played on, as its last save records it, and the paths of whole real runs as the game's own
run history files record them (history/*.run: no map, but the seed and, per act, the kind of
every point entered and the rooms it held). FILES and RUNS name the files.

    python3 -I golden_maps.py <folder of the saves> <path of MapGoldenData.cs>
"""
import json, sys, os
SRC = sys.argv[1]
FILES = [
    'run-164206-14bb2d424ffb.save',
    'run-164247-76723b820cae.save',
    'run-164306-b9fa0c83f2b6.save',
    'run-164409-761d393b4972.save',
    'run-164514-abe3bfe862ef.save',
]
PLAYED = 'later-1011-164738-d2d32c57f49b.save'
# (file, what it is, acts whose map was not the standard one and why)
RUNS = [
    ('1791592972.run', 'a single Ironclad at ascension 0, won', {1: 'the card Spoils Map, gained on floor 15 of the first act, replaced the second act\'s map by its own kind'}),
    ('1783968626.run', 'two players at ascension 7 (eight elites a map, one row less), lost at the last boss; Juzu Bracelet was taken from the elite on the tenth point of the first act', {}),
    ('1791745191.run', 'two players at ascension 0 (one row less), lost at the first boss', {}),
]
o = []
o.append('''namespace SpireMind.Arena;

/// <summary>
/// The maps of real saves of the game, v0.107.1, Ironclad, ascension 0, single player: the
/// evidence the map generator is checked against (MapChecks). Mark made the saves on 2026-10-11
/// by starting a standard run and choosing Save and Quit on Neow's screen; the files are kept
/// unchanged in <c>sts2-notes/golden-saves</c>. Here each is the save's seed, its first act and
/// that act's <c>saved_map</c>, the save's own JSON with nothing reordered or renamed: the grid's
/// width and height, every point in the game's order with its kind, whether it can be modified
/// (the key is left out when it cannot) and its children in the order the game lists them, the
/// start point, the boss, and the first-row points the paths started from. Then the path of the
/// one run that was played on. Made from the files by Sim/Run/Tools/golden_maps.py; never edit
/// by hand.
/// </summary>
internal static class MapGoldenData
{
    public sealed record Save(string File, string Seed, string Act, string Map);

    /// <summary>One visited point of the played run: its place, its kind and the kind of each room entered on it.</summary>
    public sealed record Step(int Col, int Row, string PointType, string[] Rooms);

    /// <summary>One point a real run entered: its kind, and the kind of each room it held (an Unknown point's room is what it was rolled to be).</summary>
    public sealed record Visit(string PointType, string[] Rooms);

    /// <summary>One act of a real run. <paramref name="OtherMap"/> says why the act's map was not the standard one, or is null.</summary>
    public sealed record ActPath(string Act, string? OtherMap, Visit[] Points);

    /// <summary>A real run of v0.107.1 from the game's run history: the seed, the players, the ascension, and the path through each act.</summary>
    public sealed record RunPath(string File, string What, string Seed, string Build, int Players, int Ascension, ActPath[] Acts);

    public static readonly IReadOnlyList<Save> Saves =
    [''')
for f in FILES:
    d = json.load(open(os.path.join(SRC, f)))
    m = json.dumps(d['acts'][0]['saved_map'], separators=(',', ':'))
    assert '"""' not in m
    o.append('        new("%s", "%s", "%s",' % (f, d['rng']['seed'], d['acts'][0]['id']))
    o.append('            """' + m + '"""),')
o.append('    ];')
d = json.load(open(os.path.join(SRC, PLAYED)))
o.append('')
o.append('    /// <summary>The save of seed %s that was written five map points into the run.</summary>' % d['rng']['seed'])
o.append('    public const string PlayedFile = "%s";' % PLAYED)
o.append('    public const string PlayedSeed = "%s";' % d['rng']['seed'])
o.append('')
o.append('    /// <summary>That save\'s <c>visited_map_coords</c>, each with the kind and the rooms its <c>map_point_history</c> records.')
o.append('    /// The game writes a save when a point is entered, before it decides the room and records the visit, so the last place has no entry yet.</summary>')
o.append('    public static readonly IReadOnlyList<Step> PlayedPath =')
o.append('    [')
hist = d['map_point_history'][0]
for i, c in enumerate(d['visited_map_coords']):
    if i < len(hist):
        o.append('        new(%d, %d, "%s", [%s]),' % (c['col'], c['row'], hist[i]['map_point_type'], ', '.join('"%s"' % r['room_type'] for r in hist[i]['rooms'])))
    else:
        o.append('        new(%d, %d, "", []),' % (c['col'], c['row']))
o.append('    ];')
o.append('')
o.append('    /// <summary>How many numbers that save\'s <c>unknown_map_point</c> stream had given.</summary>')
o.append('    public const int PlayedUnknownCounter = %d;' % d['rng']['counters']['unknown_map_point'])
o.append('')
o.append('    /// <summary>Whole real runs, from <c>golden-saves/history</c>.</summary>')
o.append('    public static readonly IReadOnlyList<RunPath> Runs =')
o.append('    [')
for f, what, other in RUNS:
    d = json.load(open(os.path.join(SRC, 'history', f)))
    assert d['build_id'] == 'v0.107.1' and d['game_mode'] == 'standard' and not d['modifiers'], f
    o.append('        new("%s", "%s", "%s", "%s", %d, %d,' % (f, what.replace('"', '\\"'), d['seed'], d['build_id'], len(d['players']), d['ascension']))
    o.append('        [')
    for i, act in enumerate(d['map_point_history']):
        o.append('            new("%s", %s,' % (d['acts'][i], ('"%s"' % other[i]) if i in other else 'null'))
        o.append('            [')
        line = []
        for e in act:
            line.append('new("%s", [%s])' % (e['map_point_type'], ', '.join('"%s"' % r['room_type'] for r in e['rooms'])))
        for k in range(0, len(line), 4):
            o.append('                ' + ', '.join(line[k:k + 4]) + ',')
        o.append('            ]),')
    o.append('        ]),')
o.append('    ];')
o.append('}')
open(sys.argv[2], 'w').write('\n'.join(o) + '\n')
