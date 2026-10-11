"""Makes Sim/Run/RunGoldenData.cs from real saves of the game: each save reduced to the parts the
checks read (seed and counters, odds, each act's lists, both relic bags, the relics held, the
unlock state), unchanged otherwise. FILES names the saves and says what each one is.

    python3 -I golden_data.py <folder of the saves> <path of RunGoldenData.cs>
"""
import json, sys, os
SRC = sys.argv[1]
FILES = [
    ('run-164206-14bb2d424ffb.save', 'saved on Neow\'s screen, nothing picked'),
    ('run-164247-76723b820cae.save', 'saved on Neow\'s screen, nothing picked'),
    ('run-164306-b9fa0c83f2b6.save', 'saved on Neow\'s screen, nothing picked; the first act is Underdocks'),
    ('run-164409-761d393b4972.save', 'saved on Neow\'s screen, nothing picked'),
    ('run-164514-abe3bfe862ef.save', 'saved on Neow\'s screen, nothing picked'),
    ('later-1011-164738-d2d32c57f49b.save', 'the run of run-164514 three rooms later: two fights won, then an Unknown room that was the event This or That, which gave Stone Cracker'),
]
def reduce(d):
    p = d['players'][0]
    out = {
        'rng': d['rng'],
        'odds': d['odds'],
        'acts': [{'id': a['id'], 'rooms': {k: a['rooms'][k] for k in ('event_ids', 'normal_encounter_ids', 'elite_encounter_ids', 'boss_id', 'ancient_id', 'events_visited', 'normal_encounters_visited', 'elite_encounters_visited', 'boss_encounters_visited')}} for a in d['acts']],
        'shared_relic_grab_bag': d['shared_relic_grab_bag'],
        'players': [{'character_id': p['character_id'], 'rng': p['rng'], 'odds': p['odds'], 'relic_grab_bag': p['relic_grab_bag'], 'relics': [{'id': r['id']} for r in p['relics']], 'unlock_state': p['unlock_state']}],
    }
    return out
def text(o):
    # one line per top-level part, so a diff of this file stays readable
    lines = ['{']
    keys = list(o.keys())
    for i, k in enumerate(keys):
        lines.append('  %s: %s%s' % (json.dumps(k), json.dumps(o[k], separators=(',', ':')), ',' if i < len(keys) - 1 else ''))
    lines.append('}')
    return '\n'.join(lines)
o = []
o.append('''namespace SpireMind.Arena;

/// <summary>
/// Real saves of the game, v0.107.1, Ironclad, ascension 0, single player: the evidence the
/// run generation is checked against (RunStreamsChecks). Mark made them on 2026-10-11 by
/// starting a standard run and choosing Save and Quit; the files are kept unchanged in
/// <c>sts2-notes/golden-saves</c>. Here each is the save's own JSON reduced to the parts the
/// checks read: the seed and the stream counters, the odds, each act's lists, both relic bags,
/// the relics held and the unlock state. Nothing is reordered or renamed. Made from the files
/// by Sim/Run/Tools/golden_data.py; never edit by hand.
/// </summary>
internal static class RunGoldenData
{
    public sealed record Save(string File, string What, string Json);

    public static readonly IReadOnlyList<Save> Saves =
    [''')
for f, what in FILES:
    d = json.load(open(os.path.join(SRC, f)))
    o.append('        new("%s", "%s",' % (f, what.replace('"', '\\"')))
    o.append('            """')
    for line in text(reduce(d)).split('\n'):
        o.append('            ' + line)
    o.append('            """),')
o.append('    ];')
o.append('}')
open(sys.argv[2], 'w').write('\n'.join(o) + '\n')
print(os.path.getsize(sys.argv[2]))
