namespace SpireMind.Arena;

/// <summary>
/// Nibbit (encounters-1.json), the worked example of a monster. 42 to 46 HP. Three moves in a
/// fixed cycle, Butt, Slice, Hiss; the encounter decides where it enters the cycle.
/// </summary>
public sealed class Nibbit : Monster
{
    private const int ButtDamage = 12;
    private const int SliceDamage = 6;
    private const int SliceBlock = 5;
    private const int HissStrength = 2;

    /// <summary>The front Nibbit of NibbitsNormal: opens with Slice. The back one opens with Hiss.</summary>
    public bool IsFront { get; set; }

    /// <summary>The single Nibbit of NibbitsWeak: opens with Butt.</summary>
    public bool IsAlone { get; set; }

    public override string Name => "Nibbit";
    public override int MinHp => 42;
    public override int MaxHp => 46;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var butt = m.Move("BUTT_MOVE", Butt, Intent.Attack(ButtDamage));
        var slice = m.Move("SLICE_MOVE", Slice, Intent.Attack(SliceDamage), Intent.Defend);
        var hiss = m.Move("HISS_MOVE", Hiss, Intent.Buff);
        butt.Then(slice).Then(hiss).Then(butt);

        // Resolved once, when the monster joins the fight; no random number.
        var init = m.Conditional("INIT_MOVE");
        if (IsAlone) init.When(() => IsAlone, butt);
        else init.When(() => !IsFront, hiss).When(() => IsFront, slice);
        return init;
    }

    private void Butt(Fight f) => Attack(f, ButtDamage);

    private void Slice(Fight f)
    {
        Attack(f, SliceDamage);
        // Move Block: it stays through the player's next turn and falls at the start of the next enemy turn.
        GainBlock(f, SliceBlock);
    }

    private void Hiss(Fight f) => ApplyToSelf<StrengthPower>(f, HissStrength);
}

/// <summary>One Nibbit alone, no slot. One of the act's weak encounters.</summary>
public sealed class NibbitsWeak : Encounter
{
    public override bool IsWeak => true;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Nibbit { IsAlone = true }, null)];
}

/// <summary>Two Nibbits in the slots "front" and "back"; the front one acts first.</summary>
public sealed class NibbitsNormal : Encounter
{
    public override IReadOnlyList<string> Slots => ["front", "back"];

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new Nibbit { IsFront = true }, "front"), (new Nibbit(), "back")];
}
