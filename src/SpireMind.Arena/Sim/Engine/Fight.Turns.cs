namespace SpireMind.Arena;

// The turn loop (rules-turns.md sections 2, 5 and 6). Every step sits where the game has it;
// the rule numbers say which.
public sealed partial class Fight
{
    /// <summary>Starts the turn of the side whose turn it now is (R11 for the player, R38 for the enemies).</summary>
    private void StartTurn()
    {
        if (!_s.InProgress) return;
        var side = _s.CurrentSide;
        var participants = CreaturesOnSide(side);

        // R11.2: powers note their amount at turn start.
        foreach (var creature in participants)
            foreach (var power in creature.PowerList) power.AmountOnTurnStart = power.Amount;

        // R11.3: the first hook, before monsters choose and before Block falls.
        Each(l => l.BeforeSideTurnStart(this, side, participants));

        if (side == Side.Player)
        {
            _s.Phase = TurnPhase.Start;
            // R11.4 and R42: every monster chooses its next move now, in list order. On turn 1 the
            // machines do not move on, so the first intents stand and no random number is used.
            foreach (var enemy in _s.Enemies.ToList()) enemy.Monster!.RollMove(this);
        }

        // R11.5, R12, R40: Block falls for the side that is starting; the player's not on turn 1.
        foreach (var creature in participants)
        {
            if (side == Side.Player && _s.Turn == 1) continue;
            if (AllListeners(l => l.ShouldClearBlock(this, creature), guarded: true)) creature.Block = 0;
        }

        // R11.6: runs on turn 1 too.
        foreach (var creature in participants) Each(l => l.AfterBlockCleared(this, creature));

        if (side == Side.Player) SetupPlayerTurn();

        // R11.8 / R38.6: Demon Form, Plating's countdown, Poison, Slow's reset.
        Each(l => l.AfterSideTurnStart(this, side, participants));
        Each(l => l.AfterSideTurnStartLate(this, side, participants));

        if (side == Side.Player)
        {
            // R11.10
            if (_s.Player.IsAlive)
            {
                _s.Phase = TurnPhase.AutoPrePlay;
                Each(l => l.AfterAutoPrePlayPhaseEnteredEarly(this));
                Each(l => l.AfterAutoPrePlayPhaseEntered(this));
                Each(l => l.AfterAutoPrePlayPhaseEnteredLate(this));
                _s.Phase = TurnPhase.Play;
            }
            // R11.11
            CheckWinCondition();
        }
        else
        {
            // R38.7
            if (CheckWinCondition()) return;
            ExecuteEnemyTurn();
        }
    }

    /// <summary>R11.7: energy, the hooks around the hand draw, the Innate move on turn 1, the draw.</summary>
    private void SetupPlayerTurn()
    {
        if (_s.Player.IsDead) return;

        // R13: energy is set to the maximum, not added to.
        if (AllListeners(l => l.ShouldResetEnergy(this), guarded: true)) _s.Energy = MaxEnergy;
        else _s.Energy += MaxEnergy;

        Each(l => l.AfterEnergyReset(this));
        Each(l => l.AfterEnergyResetLate(this));
        Each(l => l.BeforeHandDraw(this));
        Each(l => l.BeforeHandDrawLate(this));

        // R14
        var handDraw = Fold(BaseHandDraw, (l, n) => l.ModifyHandDraw(this, n));
        handDraw = Fold(handDraw, (l, n) => l.ModifyHandDrawLate(this, n));

        if (_s.Turn == 1)
        {
            // R10: each Innate card goes to the top, walking the pile from the top down, so they end
            // up on top in the reverse of their shuffled order; the draw covers all of them, up to 10.
            var innate = _s.Draw.Where(c => c.Has(CardKeywords.Innate)).ToList();
            foreach (var card in innate)
            {
                _s.Draw.Remove(card);
                _s.Draw.Insert(0, card);
            }
            handDraw = Math.Max(handDraw, innate.Count);
            handDraw = Math.Min(handDraw, HandLimit);
        }

        Draw(handDraw, fromHandDraw: true);

        Each(l => l.AfterPlayerTurnStartEarly(this));
        Each(l => l.AfterPlayerTurnStart(this));
        Each(l => l.AfterPlayerTurnStartLate(this));
    }

    /// <summary>The player's maximum energy: base 3, through the hook (Pyre).</summary>
    public int MaxEnergy => (int)Fold(_s.BaseMaxEnergy, (l, n) => l.ModifyMaxEnergy(this, n));

    /// <summary>The whole of "end_turn": the two phases of the player's turn end, the enemy turn, and
    /// the start of the next player turn (R32, R38).</summary>
    private void EndPlayerTurn()
    {
        EndPlayerTurnPhaseOne();
        if (!_s.InProgress) return;
        EndPlayerTurnPhaseTwo();

        // R38.1: the side switches; monsters added during the player turn may now act.
        SwitchSides();
        StartTurn();
    }

    private void EndPlayerTurnPhaseOne()
    {
        IReadOnlyList<Creature> participants = [_s.Player];

        // R32.2
        _s.Phase = TurnPhase.AutoPostPlay;
        Each(l => l.AfterAutoPostPlayPhaseEntered(this));
        _s.Phase = TurnPhase.End;

        // R32.3: three passes; Plating's Block arrives in the Early one.
        Each(l => l.BeforeSideTurnEndVeryEarly(this, Side.Player, participants));
        Each(l => l.BeforeSideTurnEndEarly(this, Side.Player, participants));
        Each(l => l.BeforeSideTurnEnd(this, Side.Player, participants));

        // R32.4
        if (CheckWinCondition()) return;

        // R32.6 to R32.8: one scan of the hand, left to right. Ethereal cards without an
        // end-of-turn effect are exhausted first (Retain does not save them, R33); then the cards
        // with an end-of-turn effect resolve one at a time (R34).
        if (!IsOverOrEnding)
        {
            var turnEnd = new List<CardInstance>();
            var ethereal = new List<CardInstance>();
            foreach (var card in _s.Hand)
            {
                if (card.Def.HasTurnEndInHandEffect) turnEnd.Add(card);
                else if (card.Has(CardKeywords.Ethereal) && AllListeners(l => l.ShouldEtherealTrigger(this, card), guarded: true)) ethereal.Add(card);
            }
            foreach (var card in ethereal) Exhaust(card, causedByEthereal: true);
            foreach (var card in turnEnd)
            {
                MoveCard(card, PileType.Play);
                card.Def.TurnEndInHand!(this, card);
                if (card.Has(CardKeywords.Ethereal)) Exhaust(card, causedByEthereal: true);
                else MoveCard(card, PileType.Discard);
            }
        }

        // R32.9
        Each(l => l.BeforeFlush(this));
        Each(l => l.BeforeFlushLate(this));

        // R32.10
        CheckWinCondition();
    }

    private void EndPlayerTurnPhaseTwo()
    {
        // R35: a plain move to the discard pile in hand order, not a "discard".
        if (_s.Player.IsAlive)
        {
            var flush = AllListeners(l => l.ShouldFlush(this), guarded: true);
            var flushed = new List<CardInstance>();
            var retained = new List<CardInstance>();
            foreach (var card in _s.Hand)
            {
                if (!flush || card.Has(CardKeywords.Retain) || card.RetainThisTurn) retained.Add(card);
                else flushed.Add(card);
            }
            MoveCards(flushed, PileType.Discard);
            Each(l => l.AfterFlush(this, flushed, retained));
            // R37
            EndOfTurnCleanup();
        }

        // R36: temporary Strength, Rage, No Draw and Constrict end or act here, after the flush.
        IReadOnlyList<Creature> participants = [_s.Player];
        Each(l => l.AfterSideTurnEnd(this, Side.Player, participants));
        Each(l => l.AfterSideTurnEndLate(this, Side.Player, participants));
    }

    private void EndOfTurnCleanup()
    {
        foreach (var card in AllCards) card.EndOfTurnCleanup();
    }

    /// <summary>R16, R38.1, R38.12: to the enemy side, or back to the player with the round and turn numbers raised.</summary>
    private void SwitchSides()
    {
        if (_s.CurrentSide == Side.Player)
        {
            _s.CurrentSide = Side.Enemy;
        }
        else
        {
            _s.CurrentSide = Side.Player;
            _s.Round++;
            _s.Turn++;
        }
        foreach (var enemy in _s.Enemies) enemy.Monster!.SpawnedThisTurn = false;
    }

    /// <summary>R38.8 to R38.12.</summary>
    private void ExecuteEnemyTurn()
    {
        if (!_s.InProgress) return;

        // R39: a copy of the list as it is now; a monster that left it is skipped, one that joins
        // during this turn is not in the copy.
        foreach (var enemy in _s.Enemies.ToList())
        {
            if (!_s.Enemies.Contains(enemy)) continue;
            TakeTurn(enemy);
            CheckWinCondition();
            if (!_s.InProgress) return;
        }

        // R38.9 to R38.11
        var enemies = CreaturesOnSide(Side.Enemy);
        Each(l => l.BeforeSideTurnEndVeryEarly(this, Side.Enemy, enemies));
        Each(l => l.BeforeSideTurnEndEarly(this, Side.Enemy, enemies));
        Each(l => l.BeforeSideTurnEnd(this, Side.Enemy, enemies));
        EndOfTurnCleanup();
        // R52: Vulnerable, Weak and Frail tick here, for everyone.
        Each(l => l.AfterSideTurnEnd(this, Side.Enemy, enemies));
        Each(l => l.AfterSideTurnEndLate(this, Side.Enemy, enemies));

        // R38.12
        CheckWinCondition();
        if (!_s.InProgress) return;

        if (_s.Turn >= _info.MaxTurns)
        {
            TimeOut();
            return;
        }
        SwitchSides();
        StartTurn();
    }

    /// <summary>R41: the monster performs the move it intends. One that was added this turn waits.
    /// A monster that died during its own move leaves the list only when the move is done.</summary>
    private void TakeTurn(Creature enemy)
    {
        var monster = enemy.Monster!;
        if (monster.SpawnedThisTurn) return;
        monster.PerformMove(this);
        if (enemy.IsDead && _s.Enemies.Contains(enemy) && ShouldRemoveAfterDeath(enemy)) RemoveFromEnemies(enemy);
    }
}
