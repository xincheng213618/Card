namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WeiwanCostReason = "skill-program.le-xiao-qiao.weiwan.discard";
    private const string WeiwanObtainReason = "skill-program.le-xiao-qiao.weiwan.obtain";
    private static readonly Suit[] WeiwanSuits = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];
    private bool HasLeXiaoQiaoWeiwanCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain);
    private static CardConversionSource WeiwanSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private ActualDiscardRecoveryPhaseKey? WeiwanPlay(int actor)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        return _setupComplete && _winner == Winner.None && _phase == TurnPhase.Play && _currentSeat == actor &&
            _players[actor].IsAlive && phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } && phase.ActorSeat == actor &&
            IsRecordedActualDiscardRecoveryPhase(phase) ? phase : null;
    }
    private bool CanPayWeiwanQin(int owner, Card card)
    {
        var from = _cardZones.GetLocation(card.Id);
        return from.OwnerSeat == owner && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && HasQiqinEntityMark(card.Id) &&
            !IsSelfHandCategoryDiscardForbidden(owner, card, from, OwnedCardMoveIntent.Discard) &&
            !IsForeignEquipmentDiscardPrevented(owner, card, from, OwnedCardMoveIntent.Discard);
    }
    [ProgramCapability]
    private sealed class LeXiaoQiaoWeiwanCapability(GameEngine engine) : ILeXiaoQiaoWeiwanHost, ILeXiaoQiaoWeiwanSelectionQuery
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginWeiwan(f);
        public bool CanDiscard(int owner, Card card) => engine.CanPayWeiwanQin(owner, card);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28620, SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain)]
    private bool CanActivateWeiwan(ProgramActivationLegalityContext gate) => WeiwanPlay(gate.OwnerSeat) is { } phase &&
        !ProgramEventHistory<WeiwanStartedEvent>().Any(e => e.Source.OwnerSeat == gate.OwnerSeat && e.Phase == phase) &&
        GetHand(gate.Owner).Concat(GetEquipment(gate.Owner)).Any(c => CanPayWeiwanQin(gate.OwnerSeat, c));

    private SkillProgramStepOutcome BeginWeiwan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Weiwan is not null) { ContinueWeiwan(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != "le:weiwan" || f.ActivationId != "discard-qin-obtain-suits" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds is not [var id] || f.SelectedTargetSeats is not [var target] ||
            target == f.OwnerSeat || !IsValidPlayerSeat(target) || !_players[target].IsAlive || WeiwanPlay(f.OwnerSeat) is not { } phase ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            ProgramEventHistory<WeiwanStartedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == phase))
            throw new InvalidOperationException("Weiwan lost its one real Qin discard, other target or actual Play allowance.");
        var from = _cardZones.GetLocation(id); var card = _cardZones.CardsAt(from).Single(c => c.Id == id);
        if (!CanPayWeiwanQin(f.OwnerSeat, card)) throw new InvalidOperationException("Weiwan cannot borrow another zone or a non-Qin discard cost.");
        var r = new WeiwanReceipt(WeiwanSource(f), f.GameplayHash, phase, f.InstructionIndex, _turnNumber,
            _turnProgression.OwnerSeat, id, card.Kind, from, EffectiveSuit(_players[f.OwnerSeat], card), target, _movementSequence)
        { Stage = WeiwanStage.CostChildren, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { Weiwan = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WeiwanStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.ActualTurn,
            r.TurnOwner, id, card.Kind, from, r.CostSuit, target, r.SequenceBefore));
        MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new(WeiwanCostReason), (batch, moves) =>
        {
            var paid = moves.Single(); var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { Weiwan = current.Weiwan! with
                { CostBatchId = batch, CostSequence = paid.Sequence, CostTo = paid.To, After = paid.Sequence } });
            AdvanceEventRulesAndQueueFact(new WeiwanCostPaidEvent(f.Id, batch, id, card.Kind, paid.From, paid.To, paid.Sequence));
        });
        ContinueWeiwan(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainWeiwan(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginHpChangedProgramWindow(f.Id, f.Weiwan!.Branch == WeiwanBranch.LoseHp ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id);

    private void ContinueWeiwan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Weiwan: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertWeiwan(f);
        if (r.Stage == WeiwanStage.HealthIssuing)
        {
            if (!_players[r.TargetSeat].IsAlive || _winner != Winner.None) { CompleteWeiwan(f); return; }
            new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, r.TargetSeat, 1);
            if (_resolutionStack.LastOrDefault()?.Id != id) return;
            f = GetActiveProgramFrame(id); r = f.Weiwan!;
            if (r.Stage == WeiwanStage.HealthIssuing) throw new InvalidOperationException("Weiwan's one native HP deduction lost its typed result.");
        }
        if (DrainWeiwan(f)) return;
        f = GetActiveProgramFrame(id); r = f.Weiwan!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == WeiwanStage.CostChildren)
        {
            var samples = new List<WeiwanSuitSample>(); var before = _movementSequence;
            if (_winner == Winner.None && _players[f.OwnerSeat].IsAlive && _players[r.TargetSeat].IsAlive)
            {
                // Snapshot the entire group before any one selected entity leaves
                // its zone or removal hooks can change another candidate's suit.
                var target = _players[r.TargetSeat];
                var candidates = new[] { CardLocation.Hand(target.Seat), CardLocation.Equipment(target.Seat), CardLocation.Judgment(target.Seat) }
                    .SelectMany(location => _cardZones.CardsAt(location).Select(card => (Card: card, From: location, Suit: EffectiveSuit(target, card))))
                    .OrderBy(entry => entry.From.Zone).ThenBy(entry => entry.Card.Id).ToArray();
                foreach (var suit in WeiwanSuits.Where(suit => suit != r.CostSuit))
                {
                    var eligible = candidates.Where(entry => entry.Suit == suit).ToArray(); var random = _random.State;
                    var selected = eligible.Length == 0 ? ((Card Card, CardLocation From, Suit Suit)?)null : eligible[_random.Next(eligible.Length)];
                    samples.Add(new(samples.Count, suit, selected?.Card.Id, selected?.Card.Kind, selected?.From, eligible.Length, random, _random.State));
                }
            }
            ReplaceRuntimeTop(f = f with { Weiwan = r with { Stage = WeiwanStage.ObtainChildren, Samples = samples, Before = before, After = before },
                PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            foreach (var sample in samples) AdvanceEventRulesAndQueueFact(new WeiwanSuitSampledEvent(f.Id, sample.Index, sample.Suit,
                sample.CardId, sample.PrintedKind, sample.From, sample.CandidateCount, sample.RandomBefore, sample.RandomAfter, before));
            var ids = samples.Where(s => s.CardId is not null).Select(s => s.CardId!.Value).ToArray();
            if (ids.Length == 0) FinishWeiwanObtain(f.Id, 0, []);
            else MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(f.OwnerSeat), new(WeiwanObtainReason),
                (batch, records) => FinishWeiwanObtain(f.Id, batch, records));
            ContinueWeiwan(id); return;
        }
        if (r.Stage == WeiwanStage.ObtainChildren)
        {
            var branch = r.ActualCount switch { 1 => WeiwanBranch.LoseHp, 2 => WeiwanBranch.UnlimitedUse, 3 => WeiwanBranch.ForbidUse, _ => WeiwanBranch.None };
            ReplaceRuntimeTop(f = f with { Weiwan = r with { Branch = branch,
                Stage = branch == WeiwanBranch.LoseHp ? WeiwanStage.HealthIssuing : WeiwanStage.HealthChildren },
                PendingMovementContinuation = new(r.TargetSeat, 0, null) });
            if (branch is WeiwanBranch.UnlimitedUse or WeiwanBranch.ForbidUse)
                AdvanceEventRulesAndQueueFact(new WeiwanTurnLeaseIssuedEvent(new(f.Id, r.Source, r.GameplayHash, f.OwnerSeat,
                    r.TargetSeat, r.ActualTurn, r.TurnOwner, r.ActualCount, r.After)));
            ContinueWeiwan(id); return;
        }
        if (r.Stage == WeiwanStage.HealthChildren) { CompleteWeiwan(f); return; }
        throw new InvalidOperationException("Weiwan resumed outside its original paid cost/obtain/health cursor.");
    }
    private void FinishWeiwanObtain(long id, long batch, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = GetActiveProgramFrame(id); var r = f.Weiwan!;
        if (r.Stage != WeiwanStage.ObtainChildren || r.ActualCount != -1) throw new InvalidOperationException("Weiwan cannot obtain its random group twice.");
        var count = moves.Count(m => m.To == CardLocation.Hand(f.OwnerSeat));
        ReplaceRuntimeTop(f with { Weiwan = r with { ObtainBatchId = batch, After = _movementSequence, ActualCount = count } });
        foreach (var sample in r.Samples.Where(s => s.CardId is not null))
        {
            var move = moves.Single(m => m.CardId == sample.CardId);
            AdvanceEventRulesAndQueueFact(new WeiwanEntityObtainedEvent(id, sample.Index, move.CardId, move.CardKind, move.From, move.To, move.Sequence, batch));
        }
        AdvanceEventRulesAndQueueFact(new WeiwanObtainPaidEvent(id, batch, moves.Count, count, r.Before, _movementSequence));
    }
    private void CompleteWeiwan(ProgramSkillFrame f)
    {
        var r = f.Weiwan!;
        AdvanceEventRulesAndQueueFact(new WeiwanCompletedEvent(f.Id, r.CardId, r.TargetSeat, Math.Max(0, r.ActualCount), r.Branch ?? WeiwanBranch.None));
        ReplaceRuntimeTop(f = f with { Weiwan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28620)]
    private bool ResumeWeiwan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Weiwan.CostSequence: > 0 } f || f.Id != id) return false;
        ContinueWeiwan(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28620)]
    private bool ReturnWeiwanMovement(ProgramSkillFrame f)
    { if (f.Weiwan is null || f.PendingMovementContinuation is null) return false; ContinueWeiwan(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28620)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28620)]
    private bool ContinuePaidWeiwan(ProgramSkillFrame f) => HasPaidWeiwanSelection(f);
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 28620)]
    private void ObserveWeiwanHpLoss(IGameEvent payload)
    {
        if (payload is ProgramSkillHpLostEvent fact && _resolutionStack.LastOrDefault() is ProgramSkillFrame
            { Weiwan: { Stage: WeiwanStage.HealthIssuing, Branch: WeiwanBranch.LoseHp, HpLossCaptured: false } r } f &&
            fact.FrameId == f.Id && fact.SkillId == f.SkillId && fact.TargetSeat == r.TargetSeat && fact.Amount == 1)
        {
            ReplaceRuntimeTop(f with { Weiwan = r with { Stage = WeiwanStage.HealthChildren, HpLossCaptured = true } });
            AdvanceEventRulesAndQueueFact(new WeiwanHealthAppliedEvent(f.Id, fact.TargetSeat, fact.Amount, fact.RemainingHp, false));
        }
        else if (payload is IdentityHpDeductionPreventedEvent prevented && _resolutionStack.LastOrDefault() is IdentityAppearanceFrame appearance &&
            appearance.Id == prevented.AppearanceFrameId && appearance.Receipt is { Entry: IdentityAppearanceEntry.ProgramHpLoss, ParentFrameId: { } parent } native &&
            _resolutionStack.SingleOrDefault(frame => frame.Id == parent) is ProgramSkillFrame
            { Weiwan: { Stage: WeiwanStage.HealthIssuing, Branch: WeiwanBranch.LoseHp, HpLossCaptured: false } r2 } f2 &&
            native.OwnerSeat == r2.TargetSeat && native.OriginalInstructionIndex == f2.InstructionIndex && prevented.RequestedDeduction == 1)
        {
            ReplaceRuntimeFrame(f2.Id, f2 with { Weiwan = r2 with { Stage = WeiwanStage.HealthChildren, HpLossCaptured = true, AppearancePrevented = true } });
            AdvanceEventRulesAndQueueFact(new WeiwanHealthAppliedEvent(f2.Id, r2.TargetSeat, 0, _players[r2.TargetSeat].Hp, true));
        }
    }
}
