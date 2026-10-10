using System.Globalization;

namespace SpireMind.Arena;

// Overgrowth monsters, part C (encounters-1.json and encounters-2.json): the Shrinker Beetle,
// the Fuzzy Wurm Crawler, the five Ruby Raiders and the Vine Shambler, their five encounters,
// and Tangled, the one power only they use. Every number is the ascension-0 value of the game
// class of the same name (v0.107.1). None of these machines has a random branch, so none of
// them ever draws from the MonsterAi stream; the only random choice is the raiders' line-up.

/// <summary>Registers the monsters and encounters of part C, Tangled, and the marker Tangled puts on cards.</summary>
public sealed class MonstersC : IContentModule
{
    public void Register(Registry r)
    {
        // The game's Entangled class leaves its card line switched off, so a marked card reads as
        // before; the player sees the higher cost and the Tangled power.
        r.Affliction(new AfflictionDef(TangledPower.Marker, "Entangled"));
        r.Power<TangledPower>();

        r.Monster<ShrinkerBeetle>();
        r.Monster<FuzzyWurmCrawler>();
        r.Monster<AxeRubyRaider>();
        r.Monster<AssassinRubyRaider>();
        r.Monster<BruteRubyRaider>();
        r.Monster<CrossbowRubyRaider>();
        r.Monster<TrackerRubyRaider>();
        r.Monster<VineShambler>();

        r.Encounter<ShrinkerBeetleWeak>();
        r.Encounter<FuzzyWurmCrawlerWeak>();
        r.Encounter<OvergrowthCrawlers>();
        r.Encounter<RubyRaidersNormal>();
        r.Encounter<VineShamblerNormal>();
    }
}

// ---------- the power ----------

/// <summary>
/// Tangled (Vine Shambler's Grasping Vines). When it lands, every Attack card in the owner's
/// hand, draw, discard, exhaust and play piles that carries no marker yet is marked Entangled;
/// an unmarked Attack card that enters the fight later is marked too. A marked card costs the
/// power's amount more energy, in the normal cost pass: after the card's own cost changes and
/// before the late overrides, never for an X card or a card without a cost. It has no
/// duration: it is removed at the end of its owner's side's turn, after the hand was flushed,
/// and takes every marker with it. So Attacks cost more for exactly one player turn.
/// </summary>
public sealed class TangledPower : Power
{
    /// <summary>The class name of the marker on cards. It has no logic of its own.</summary>
    public const string Marker = "Entangled";

    public override string Name => "Tangled";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Attack cards cost {Amount} more energy until the end of this turn.");

    public override void AfterApplied(Fight f, Creature? applier, CardInstance? cardSource)
    {
        // Only a first application marks cards; more stacks just raise the amount.
        if (!Owner.IsPlayer) return;
        foreach (var card in f.AllCards)
            if (card.Type == CardType.Attack) Mark(f, card);
    }

    public override void AfterCardEnteredCombat(Fight f, CardInstance card)
    {
        if (Owner.IsPlayer && card.Type == CardType.Attack) Mark(f, card);
    }

    /// <summary>The marker does not stack and never replaces another one, so a card that carries any is left alone.</summary>
    private static void Mark(Fight f, CardInstance card)
    {
        if (card.Affliction is null) f.Afflict(card, Marker);
    }

    public override decimal ModifyEnergyCost(Fight f, CardInstance card, decimal cost) =>
        Owner.IsPlayer && card.Affliction == Marker ? cost + Amount : cost;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.RemovePower(this);
    }

    public override void AfterRemoved(Fight f, Creature oldOwner)
    {
        if (!oldOwner.IsPlayer) return;
        foreach (var card in f.AllCards)
            if (card.Affliction == Marker) f.ClearAffliction(card);
    }
}

// ---------- the monsters ----------

/// <summary>
/// Shrinker Beetle. 38 to 40 HP. Shrinks the player on its first turn, once; then Chomp and
/// Stomp alternate for the rest of the fight.
/// </summary>
public sealed class ShrinkerBeetle : Monster
{
    private const int ChompDamage = 7;
    private const int StompDamage = 13;

    /// <summary>A negative amount is Shrink without a duration: it never ticks and ends when this beetle dies.</summary>
    private const int ShrinkAmount = -1;

    public override string Name => "Shrinker Beetle";
    public override int MinHp => 38;
    public override int MaxHp => 40;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var shrinker = m.Move("SHRINKER_MOVE", Shrinker, Intent.DebuffStrong);
        var chomp = m.Move("CHOMP_MOVE", Chomp, Intent.Attack(ChompDamage));
        var stomp = m.Move("STOMP_MOVE", Stomp, Intent.Attack(StompDamage));
        shrinker.Then(chomp).Then(stomp).Then(chomp);
        return shrinker;
    }

    private void Shrinker(Fight f) => ApplyToPlayer<ShrinkPower>(f, ShrinkAmount);

    private void Chomp(Fight f) => Attack(f, ChompDamage);

    private void Stomp(Fight f) => Attack(f, StompDamage);
}

/// <summary>
/// Fuzzy Wurm Crawler. 55 to 57 HP. A fixed loop of three: Acid Goop, Inhale (7 Strength),
/// Acid Goop again under a second id. The same 4-damage attack grows with every Inhale:
/// 4, buff, 11, 11, buff, 18, 18, buff, 25 ...
/// </summary>
public sealed class FuzzyWurmCrawler : Monster
{
    private const int AcidGoopDamage = 4;
    private const int InhaleStrength = 7;

    public override string Name => "Fuzzy Wurm Crawler";
    public override int MinHp => 55;
    public override int MaxHp => 57;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var first = m.Move("FIRST_ACID_GOOP", AcidGoop, Intent.Attack(AcidGoopDamage));
        var inhale = m.Move("INHALE", Inhale, Intent.Buff);
        var goop = m.Move("ACID_GOOP", AcidGoop, Intent.Attack(AcidGoopDamage));
        first.Then(inhale).Then(goop).Then(first);
        return first;
    }

    private void AcidGoop(Fight f) => Attack(f, AcidGoopDamage);

    private void Inhale(Fight f) => ApplyToSelf<StrengthPower>(f, InhaleStrength);
}

/// <summary>
/// Axe Raider. 20 to 22 HP. Swing, Swing, Big Swing, repeating. A Swing hits first and then
/// gives the raider Block, which stands through the player's next turn.
/// </summary>
public sealed class AxeRubyRaider : Monster
{
    private const int SwingDamage = 5;
    private const int SwingBlock = 5;
    private const int BigSwingDamage = 12;

    public override string Name => "Axe Raider";
    public override int MinHp => 20;
    public override int MaxHp => 22;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var swing1 = m.Move("SWING_1", Swing, Intent.Attack(SwingDamage), Intent.Defend);
        var swing2 = m.Move("SWING_2", Swing, Intent.Attack(SwingDamage), Intent.Defend);
        var bigSwing = m.Move("BIG_SWING", BigSwing, Intent.Attack(BigSwingDamage));
        swing1.Then(swing2).Then(bigSwing).Then(swing1);
        return swing1;
    }

    private void Swing(Fight f)
    {
        Attack(f, SwingDamage);
        GainBlock(f, SwingBlock);
    }

    private void BigSwing(Fight f) => Attack(f, BigSwingDamage);
}

/// <summary>Assassin Raider. 18 to 23 HP. One move, every turn: Killshot.</summary>
public sealed class AssassinRubyRaider : Monster
{
    private const int KillshotDamage = 10;

    public override string Name => "Assassin Raider";
    public override int MinHp => 18;
    public override int MaxHp => 23;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var killshot = m.Move("KILLSHOT_MOVE", Killshot, Intent.Attack(KillshotDamage));
        killshot.Then(killshot);
        return killshot;
    }

    private void Killshot(Fight f) => Attack(f, KillshotDamage);
}

/// <summary>
/// Brute Raider. 30 to 33 HP. Beat and Roar alternate, the attack first. Roar gives 3 Strength
/// to this raider only: 7, buff, 10, buff, 13 ...
/// </summary>
public sealed class BruteRubyRaider : Monster
{
    private const int BeatDamage = 7;
    private const int RoarStrength = 3;

    public override string Name => "Brute Raider";
    public override int MinHp => 30;
    public override int MaxHp => 33;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var beat = m.Move("BEAT_MOVE", Beat, Intent.Attack(BeatDamage));
        var roar = m.Move("ROAR_MOVE", Roar, Intent.Buff);
        beat.Then(roar).Then(beat);
        return beat;
    }

    private void Beat(Fight f) => Attack(f, BeatDamage);

    private void Roar(Fight f) => ApplyToSelf<StrengthPower>(f, RoarStrength);
}

/// <summary>
/// Crossbow Raider. 18 to 21 HP. Reload and Fire alternate, and it starts with Reload: no
/// attack on odd turns, 14 on even ones. The Block from Reload is up during the player turn
/// before each shot.
/// </summary>
public sealed class CrossbowRubyRaider : Monster
{
    private const int FireDamage = 14;
    private const int ReloadBlock = 3;

    public override string Name => "Crossbow Raider";
    public override int MinHp => 18;
    public override int MaxHp => 21;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var reload = m.Move("RELOAD_MOVE", Reload, Intent.Defend);
        var fire = m.Move("FIRE_MOVE", Fire, Intent.Attack(FireDamage));
        reload.Then(fire).Then(reload);
        return reload;
    }

    private void Reload(Fight f) => GainBlock(f, ReloadBlock);

    private void Fire(Fight f) => Attack(f, FireDamage);
}

/// <summary>
/// Tracker Raider. 21 to 25 HP. Track on its first turn (2 Frail on the player), then Hounds
/// every turn: 8 hits of 1, each worked out and blocked on its own.
/// </summary>
public sealed class TrackerRubyRaider : Monster
{
    private const int TrackFrail = 2;
    private const int HoundsDamage = 1;
    private const int HoundsHits = 8;

    public override string Name => "Tracker Raider";
    public override int MinHp => 21;
    public override int MaxHp => 25;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var track = m.Move("TRACK_MOVE", Track, Intent.Debuff);
        var hounds = m.Move("HOUNDS_MOVE", Hounds, Intent.Attack(HoundsDamage, HoundsHits));
        track.Then(hounds).Then(hounds);
        return track;
    }

    private void Track(Fight f) => ApplyToPlayer<FrailPower>(f, TrackFrail);

    private void Hounds(Fight f) => Attack(f, HoundsDamage, HoundsHits);
}

/// <summary>
/// Vine Shambler. Always 61 HP. A fixed loop of three: Swipe (6 twice), Grasping Vines (8, then
/// Tangled on the player), Chomp (16). Tangled lands on the enemy turn and makes the player's
/// Attacks cost 1 more on the turn that follows: player turns 3, 6, 9 ...
/// </summary>
public sealed class VineShambler : Monster
{
    private const int SwipeDamage = 6;
    private const int SwipeHits = 2;
    private const int GraspingVinesDamage = 8;
    private const int GraspingVinesTangled = 1;
    private const int ChompDamage = 16;

    public override string Name => "Vine Shambler";
    public override int MinHp => 61;
    public override int MaxHp => 61;

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var swipe = m.Move("SWIPE_MOVE", Swipe, Intent.Attack(SwipeDamage, SwipeHits));
        var vines = m.Move("GRASPING_VINES_MOVE", GraspingVines, Intent.Attack(GraspingVinesDamage), Intent.CardDebuff);
        var chomp = m.Move("CHOMP_MOVE", Chomp, Intent.Attack(ChompDamage));
        swipe.Then(vines).Then(chomp).Then(swipe);
        return swipe;
    }

    private void Swipe(Fight f) => Attack(f, SwipeDamage, SwipeHits);

    private void GraspingVines(Fight f)
    {
        Attack(f, GraspingVinesDamage);
        ApplyToPlayer<TangledPower>(f, GraspingVinesTangled);
    }

    private void Chomp(Fight f) => Attack(f, ChompDamage);
}

// ---------- the encounters ----------

/// <summary>One Shrinker Beetle, no slot. One of the act's weak encounters.</summary>
public sealed class ShrinkerBeetleWeak : Encounter
{
    public override bool IsWeak => true;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new ShrinkerBeetle(), null)];
}

/// <summary>One Fuzzy Wurm Crawler, no slot. One of the act's weak encounters.</summary>
public sealed class FuzzyWurmCrawlerWeak : Encounter
{
    public override bool IsWeak => true;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new FuzzyWurmCrawler(), null)];
}

/// <summary>A Shrinker Beetle, then a Fuzzy Wurm Crawler, in that acting order. No slots, nothing random.</summary>
public sealed class OvergrowthCrawlers : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new ShrinkerBeetle(), null), (new FuzzyWurmCrawler(), null)];
}

/// <summary>
/// Three raiders of three different kinds. Three picks, one after another, from the
/// encounter's own stream: each is uniform over the kinds not picked yet, in the game's list
/// order, and costs one draw. The acting order is the pick order; no slots.
/// </summary>
public sealed class RubyRaidersNormal : Encounter
{
    private const int Raiders = 3;

    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng)
    {
        var left = new List<Func<Monster>>
        {
            () => new AxeRubyRaider(),
            () => new AssassinRubyRaider(),
            () => new BruteRubyRaider(),
            () => new CrossbowRubyRaider(),
            () => new TrackerRubyRaider(),
        };
        var lineUp = new List<(Monster Monster, string? Slot)>(Raiders);
        for (var i = 0; i < Raiders; i++)
        {
            var pick = rng.NextItem(left)!;
            left.Remove(pick);
            lineUp.Add((pick(), null));
        }
        return lineUp;
    }
}

/// <summary>One Vine Shambler, no slot.</summary>
public sealed class VineShamblerNormal : Encounter
{
    public override IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng) =>
        [(new VineShambler(), null)];
}
