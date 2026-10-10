using System.Globalization;

namespace SpireMind.Arena;

// The three elites of Overgrowth (encounters-3.json): Bygone Effigy, Byrdonis, and Phrog
// Parasite with the Wrigglers it leaves behind, plus the three powers only they use. Every
// class follows its game class of the same name (v0.107.1); numbers are those of ascension 0.

/// <summary>Registers the Overgrowth elites, their monsters and their powers.</summary>
public sealed class Elites : IContentModule
{
    public void Register(Registry r)
    {
        r.Power<SlowPower>();
        r.Power<TerritorialPower>();
        r.Power<InfestedPower>();

        r.Monster<BygoneEffigy>();
        r.Monster<Byrdonis>();
        r.Monster<PhrogParasite>();
        r.Monster<Wriggler>();

        r.Encounter<BygoneEffigyElite>();
        r.Encounter<ByrdonisElite>();
        r.Encounter<PhrogParasiteElite>();
    }
}

// ---------- powers ----------

/// <summary>
/// Slow (Bygone Effigy). A counter of its own, not the applied amount, does the work: it goes
/// up by 1 after every run of a card's effect, powered attacks against the owner are multiplied
/// by 1 + 0.1 x counter, and it is back at 0 when the owner's side starts its turn. Never ticks
/// down and is never removed. Shown as counter x 10.
/// </summary>
public sealed class SlowPower : Power
{
    private const decimal FactorPerCard = 0.1m;

    private int _cardsPlayed;

    public override string Name => "Slow";
    public override PowerType Type => PowerType.Debuff;

    /// <summary>The percentage, so 0 (and left out of the snapshot) until a card was played this turn.</summary>
    public override int DisplayAmount => _cardsPlayed * 10;

    public override string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"Takes 10% more damage from attacks for each card played this turn (now {DisplayAmount}% more). Resets at the start of its turn.");

    /// <summary>
    /// Any card of any type, and a card that runs twice counts twice. The count moves only after
    /// the effect has resolved, so a card never raises its own damage and every hit of one card
    /// gets the same factor.
    /// </summary>
    public override void AfterCardPlayed(Fight f, CardPlay play) => _cardsPlayed++;

    /// <summary>In the same stage as Vulnerable and Weak, after Strength was added. A preview has no target, so card texts do not show it.</summary>
    public override decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(target, Owner) && props.IsPoweredAttack() ? 1m + FactorPerCard * _cardsPlayed : 1m;

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) _cardsPlayed = 0;
    }
}

/// <summary>
/// Territorial (Byrdonis): after every turn of its owner's side the owner gains Strength equal
/// to the amount, the first turn included. The moves come before this, so the intent shown
/// during the player's turn is what the attack will do.
/// </summary>
public sealed class TerritorialPower : Power
{
    public override string Name => "Territorial";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, gains {Amount} Strength.");

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        // A plain Strength application: lowering or removing the Strength does not stop the next one.
        if (participants.Contains(Owner)) f.ApplyPower<StrengthPower>(Owner, Amount, Owner);
    }
}

/// <summary>
/// Infested (Phrog Parasite): when its owner really dies, four Wrigglers that start stunned
/// take the wriggler slots in order; and as long as the power is there it keeps the fight from
/// ending, which is what carries the fight over the moment the only primary enemy is dead.
/// The number of Wrigglers is fixed; the amount (4) is only what is shown.
/// </summary>
public sealed class InfestedPower : Power
{
    public const int WrigglerCount = 4;

    public override string Name => "Infested";
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"When it dies, {WrigglerCount} Wrigglers take its place and the fight goes on.");

    public override void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner)) return;
        // One after another, so each HP roll avoids the values of the Wrigglers before it.
        for (var i = 0; i < WrigglerCount; i++)
            f.AddMonster(new Wriggler { StartStunned = true }, PhrogParasiteElite.WrigglerSlot(i));
    }

    public override bool ShouldStopCombatFromEnding(Fight f) => true;
}

// ---------- monsters ----------

/// <summary>
/// Bygone Effigy: 127 HP, starts with Slow. A fixed script with no branches: it sleeps on enemy
/// turn 1, gains 10 Strength on turn 2 and then hits for 13 (23 with that Strength) every turn.
/// Being hit does not wake it early.
/// </summary>
public sealed class BygoneEffigy : Monster
{
    private const int SlashDamage = 13;
    private const int WakeStrength = 10;
    private const int SlowAmount = 1;

    public override string Name => "Bygone Effigy";
    public override int MinHp => 127;
    public override int MaxHp => 127;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<SlowPower>(f, SlowAmount);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var sleep = m.Move("SLEEP_MOVE", Sleep, Intent.Sleep);
        var wake = m.Move("WAKE_MOVE", Wake, Intent.Buff);
        // The game lists this second sleep too, but no state leads to it.
        var sleepAgain = m.Move("SLEEP_MOVE_2", Sleep, Intent.Sleep);
        var slashes = m.Move("SLASHES_MOVE", Slash, Intent.Attack(SlashDamage));
        sleep.Then(wake).Then(slashes).Then(slashes);
        sleepAgain.Then(slashes);
        return sleep;
    }

    private void Sleep(Fight f) { }

    private void Wake(Fight f) => ApplyToSelf<StrengthPower>(f, WakeStrength);

    private void Slash(Fight f) => Attack(f, SlashDamage);
}

/// <summary>
/// Byrdonis: 81 to 84 HP, starts with Territorial 1. Swoop (17) and Peck (3 x3) in strict
/// alternation, Swoop first; with Territorial its Strength on enemy turn n is n - 1.
/// </summary>
public sealed class Byrdonis : Monster
{
    private const int SwoopDamage = 17;
    private const int PeckDamage = 3;
    private const int PeckHits = 3;
    private const int TerritorialAmount = 1;

    public override string Name => "Byrdonis";
    public override int MinHp => 81;
    public override int MaxHp => 84;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<TerritorialPower>(f, TerritorialAmount);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var swoop = m.Move("SWOOP_MOVE", Swoop, Intent.Attack(SwoopDamage));
        var peck = m.Move("PECK_MOVE", Peck, Intent.Attack(PeckDamage, PeckHits));
        swoop.Then(peck).Then(swoop);
        return swoop;
    }

    private void Swoop(Fight f) => Attack(f, SwoopDamage);

    private void Peck(Fight f) => Attack(f, PeckDamage, PeckHits);
}

/// <summary>
/// Phrog Parasite: 61 to 64 HP, starts with Infested. Infect (3 Infections into the discard
/// pile) and Lash (4 x4) in strict alternation, Infect first. Its death leaves four Wrigglers.
/// </summary>
public sealed class PhrogParasite : Monster
{
    private const int LashDamage = 4;
    private const int LashHits = 4;
    private const int InfectCards = 3;
    private const int InfestedAmount = 4;

    public override string Name => "Phrog Parasite";
    public override int MinHp => 61;
    public override int MaxHp => 64;

    public override void AfterAddedToRoom(Fight f) => ApplyToSelf<InfestedPower>(f, InfestedAmount);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var infect = m.Move("INFECT_MOVE", Infect, Intent.Status(InfectCards));
        var lash = m.Move("LASH_MOVE", Lash, Intent.Attack(LashDamage, LashHits));
        infect.Then(lash).Then(infect);
        // The game builds this branch as well, but nothing leads to it: the MonsterAi stream is never used.
        m.Random("RAND").Add(infect, 1f, MoveRepeat.CannotRepeat).Add(lash, 1f, MoveRepeat.CannotRepeat);
        return infect;
    }

    private void Lash(Fight f) => Attack(f, LashDamage, LashHits);

    // No creator: the game names none for cards a monster hands out.
    private void Infect(Fight f) => f.AddStatusCards("Infection", InfectCards, PileType.Discard);
}

/// <summary>
/// Wriggler: 17 to 21 HP, no starting powers, a primary enemy (no Minion). The ones Infested
/// makes start on a do-nothing move of their own that shows a stun intent; after it the slot
/// decides the first real move (wriggler1 and wriggler3 bite, wriggler2 and wriggler4 wriggle),
/// and from there Nasty Bite (6) and Wriggle (1 Infection, then 2 Strength) alternate.
/// </summary>
public sealed class Wriggler : Monster
{
    private const int BiteDamage = 6;
    private const int WriggleStrength = 2;
    private const int WriggleCards = 1;

    /// <summary>True for the Wrigglers of Phrog Parasite: the first move is the do-nothing SPAWNED_MOVE.
    /// False goes straight to the move its slot decides.</summary>
    public bool StartStunned { get; set; }

    public override string Name => "Wriggler";
    public override int MinHp => 17;
    public override int MaxHp => 21;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var bite = m.Move("NASTY_BITE_MOVE", Bite, Intent.Attack(BiteDamage));
        var wriggle = m.Move("WRIGGLE_MOVE", Wriggle, Intent.Buff, Intent.Status(WriggleCards));
        // Its own move with a stun intent, not the engine's stun state: the snapshot reads "stun".
        var spawned = m.Move("SPAWNED_MOVE", Spawned, Intent.Stun);

        // Read when the branch is visited, not when the machine is built. Any other slot name finds no state and throws, as in the game.
        var init = m.Conditional("INIT_MOVE")
            .When(() => Creature.Slot == PhrogParasiteElite.WrigglerSlot(0), bite)
            .When(() => Creature.Slot == PhrogParasiteElite.WrigglerSlot(1), wriggle)
            .When(() => Creature.Slot == PhrogParasiteElite.WrigglerSlot(2), bite)
            .When(() => Creature.Slot == PhrogParasiteElite.WrigglerSlot(3), wriggle);

        spawned.FollowUp = init;
        bite.Then(wriggle).Then(bite);
        return StartStunned ? spawned : init;
    }

    private void Spawned(Fight f) { }

    private void Bite(Fight f) => Attack(f, BiteDamage);

    private void Wriggle(Fight f)
    {
        // The card first, then the Strength. No creator, as for the Phrog's.
        f.AddStatusCards("Infection", WriggleCards, PileType.Discard);
        ApplyToSelf<StrengthPower>(f, WriggleStrength);
    }
}

// ---------- encounters ----------

/// <summary>One Bygone Effigy, no slot. Nothing in the line-up is random and nothing joins later.</summary>
public sealed class BygoneEffigyElite : Encounter
{
    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new BygoneEffigy(), null)];
}

/// <summary>One Byrdonis, no slot. Only its HP is rolled.</summary>
public sealed class ByrdonisElite : Encounter
{
    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Byrdonis(), null)];
}

/// <summary>
/// One Phrog Parasite in the slot "phrog". The four wriggler slots are empty until it dies;
/// the slot order is also the acting order of the Wrigglers.
/// </summary>
public sealed class PhrogParasiteElite : Encounter
{
    public const string PhrogSlot = "phrog";

    private static readonly string[] SlotOrder = [PhrogSlot, WrigglerSlot(0), WrigglerSlot(1), WrigglerSlot(2), WrigglerSlot(3)];

    /// <summary>"wriggler1" for index 0, up to "wriggler4".</summary>
    public static string WrigglerSlot(int index) => string.Create(CultureInfo.InvariantCulture, $"wriggler{index + 1}");

    public override RoomType RoomType => RoomType.Elite;
    public override IReadOnlyList<string> Slots => SlotOrder;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new PhrogParasite(), PhrogSlot)];
}
