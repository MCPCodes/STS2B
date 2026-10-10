namespace SpireMind.Arena;

// Monsters joining, leaving and being stunned (rules-turns.md R6, R39, R44, R50).
public sealed partial class Fight
{
    /// <summary>
    /// R6: creates the creature and rolls its HP on the Niche stream: every value of the range
    /// that no enemy now in the list has as its max HP, one of them uniformly; with none left, a
    /// plain roll over the range. One draw either way.
    /// </summary>
    private Creature CreateCreature(Monster monster, string? slot, int? fixedHp)
    {
        int hp;
        if (fixedHp is { } given)
        {
            hp = given;
        }
        else
        {
            var free = new List<int>();
            for (var v = monster.MinHp; v <= monster.MaxHp; v++)
                if (!_s.Enemies.Any(e => e.MaxHp == v)) free.Add(v);
            hp = free.Count > 0 ? _s.Rng.Niche.NextItem(free) : _s.Rng.Niche.NextInt(monster.MinHp, monster.MaxHp + 1);
        }

        var creature = new Creature(_s.NextCreatureId++, Side.Enemy, monster.Name, hp, hp, monster, slot);
        monster.Creature = creature;
        _s.Enemies.Add(creature);
        return creature;
    }

    /// <summary>Enemies with a slot keep the encounter's slot order; the sort is stable for the rest.</summary>
    private void SortEnemiesBySlot()
    {
        var slots = _info.Slots;
        int Rank(Creature c) => c.Slot is null ? -1 : IndexOf(slots, c.Slot);
        var sorted = _s.Enemies.Select((c, i) => (c, i)).OrderBy(x => Rank(x.c)).ThenBy(x => x.i).Select(x => x.c).ToList();
        _s.Enemies.Clear();
        _s.Enemies.AddRange(sorted);
    }

    private static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }

    /// <summary>The snapshots number enemies in the order the mod first sees them and never reuse a number.</summary>
    private void AddToRoster(Creature enemy)
    {
        if (enemy.RosterIndex >= 0) return;
        enemy.RosterIndex = _s.Roster.Count;
        _s.Roster.Add(enemy);
    }

    /// <summary>R5.6 / R8: starting powers, then, on the player's side of the round, the first move.</summary>
    private void AfterCreatureAdded(Creature enemy)
    {
        enemy.Monster!.AfterAddedToRoom(this);
        if (_s.CurrentSide == Side.Player) enemy.Monster.RollMove(this);
    }

    /// <summary>
    /// Adds a monster in the middle of a fight (a summon). Its HP is rolled, it gets its starting
    /// powers, and its first move is settled at once when it joins during the player's turn. One
    /// that joins during the enemy turn does not act that turn (R39).
    /// </summary>
    public Creature AddMonster(Monster monster, string? slot = null)
    {
        if (!_s.InProgress) throw new InvalidOperationException("A monster can only be added to a fight in progress.");
        var creature = CreateCreature(monster, slot, null);
        monster.SetUpForCombat();
        if (slot is not null) SortEnemiesBySlot();
        // The mod numbers new enemies in list order the next time it looks; several added in one go
        // (four Wrigglers) arrive in slot order either way.
        AddToRoster(creature);
        AfterCreatureAdded(creature);
        Each(l => l.AfterCreatureAddedToCombat(this, creature), guarded: false);
        return creature;
    }

    /// <summary>Adds a monster by its class name.</summary>
    public Creature AddMonster(string className, string? slot = null) => AddMonster(Content.Registry.NewMonster(className), slot);

    private void RemoveFromEnemies(Creature enemy)
    {
        _s.Enemies.Remove(enemy);
        enemy.InCombat = false;
    }

    /// <summary>
    /// R44: replaces the monster's intended move at once with a do-nothing move it must perform
    /// once; afterwards <paramref name="nextMoveId"/> follows, or by default the move it was about
    /// to make. Nothing happens to a dead monster.
    /// </summary>
    public void Stun(Creature enemy, string? nextMoveId = null)
    {
        if (enemy.Monster is null) throw new InvalidOperationException("The player cannot be stunned.");
        if (!enemy.InCombat || enemy.IsDead) return;
        enemy.Monster.Stun(nextMoveId);
    }

    /// <summary>R50: the monster leaves without dying. It no longer counts for the win condition.</summary>
    public void Escape(Creature enemy)
    {
        if (enemy.IsDead || !enemy.InCombat) return;
        enemy.PowerList.Clear();
        _s.Escaped++;
        RemoveFromEnemies(enemy);
    }
}
