namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IBoundDiscardSlashBenefitsHost
    {
        public SkillProgramStepOutcome DiscardBoundCardForTurnSlashBenefits(ProgramSkillFrame frame, string sourceBind) =>
            engine.BeginBoundDiscardSlashBenefits(frame.Id, sourceBind);
        public void ScheduleFirstRoundGameUsageRefund(ProgramSkillFrame frame) => engine.ScheduleProgramFirstRoundRefund(frame);
    }
    private SkillProgramStepOutcome BeginBoundDiscardSlashBenefits(long id, string bind)
    {
        var frame = GetActiveProgramFrame(id);
        var source = GetProgramCardSet(frame, bind);
        if (frame.BoundDiscardSlashBenefits is not null) throw new InvalidOperationException("A paid discard cannot run twice.");
        if (_winner != Winner.None || frame.TriggerId is not null || _phase != TurnPhase.Play || frame.OwnerSeat != _turnProgression.OwnerSeat ||
            !_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            source.CardIds is not [var cardId] || source.SourceLocations is not [var from] || from.OwnerSeat != frame.OwnerSeat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || _cardZones.GetLocation(cardId) != from)
        {
            CancelProgramBindingAndCleanup(frame, "弃牌成本或技能来源已失效，未授予回合收益。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var card = _cardZones.CardsAt(from).Single(c => c.Id == cardId);
        var suit = GetProgramEffectiveSuit(_players[frame.OwnerSeat], card);
        var expectedSequence = _cardMovements.Count == 0 ? 1 : _cardMovements[^1].Sequence + 1;
        ReplaceRuntimeTop(frame with { BoundDiscardSlashBenefits = new(frame.InstructionIndex, bind, card.Id, card.Kind, from, suit, expectedSequence) });
        MoveCard(card, from, CardLocation.DiscardPile, new("skill-program.bound-discard-slash.cost"));
        AdvanceRuntimeProgram(id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeBoundDiscardSlashBenefits(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id || frame.BoundDiscardSlashBenefits is not { } paid) return false;
        AssertBoundDiscardSlashReceipt(frame);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(id)) return true;
        var owner = _players[frame.OwnerSeat];
        if (_winner == Winner.None && owner.IsAlive && HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId))
        {
            var source = CreateProgramTurnEffectSource(frame);
            var range = _turnCardUseEffects.GrantRuleModifier(_turnNumber, _turnProgression.OwnerSeat, id, paid.InstructionIndex - 1,
                source, SkillRuleQuery.AttackRange, SkillRuleOperation.Unlimited, 0);
            AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(range));
            var policy = _turnCardUseEffects.GrantSlashSuitAllowance(_turnNumber, _turnProgression.OwnerSeat, id, paid.InstructionIndex - 1, source, paid.EffectiveSuit);
            AdvanceEventRulesAndQueueFact(new TurnSlashSuitAllowanceGrantedEvent(policy));
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { BoundDiscardSlashBenefits = null });
        if (_winner != Winner.None)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "游戏已结束，弃牌后续收益取消。");
            return true;
        }
        return false;
    }
    private void AssertBoundDiscardSlashReceipt(ProgramSkillFrame frame)
    {
        if (frame.BoundDiscardSlashBenefits is not { } paid) return;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var source = GetProgramCardSet(frame, paid.SourceBind);
        if (frame.TriggerId is not null || frame.InstructionIndex != paid.InstructionIndex || paid.InstructionIndex < 1 ||
            paid.InstructionIndex > plan.Instructions.Count || plan.Instructions[paid.InstructionIndex - 1].Op != SkillProgramEffectOp.DiscardBoundCardForTurnSlashBenefits ||
            plan.Instructions[paid.InstructionIndex - 1].SourceBind != paid.SourceBind || paid.SourceLocation.OwnerSeat != frame.OwnerSeat ||
            paid.SourceLocation.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || !Enum.IsDefined(paid.EffectiveSuit) ||
            source.CardIds is not [var original] || original != paid.CardId || source.SourceLocations is not [var location] || location != paid.SourceLocation ||
            !_cardMovements.Any(m => m.Sequence == paid.MovementSequence && m.CardId == paid.CardId && m.From == paid.SourceLocation &&
                m.Reason.Value == "skill-program.bound-discard-slash.cost" && m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame))
            throw new InvalidOperationException("A paid discard lost its exact instruction, original binding or real movement.");
    }
    private bool HasTurnSlashSuitAllowance(int owner, Suit? suit) => _turnCardUseEffects.HasSlashSuitAllowance(_turnNumber, _turnProgression.OwnerSeat, owner, suit);
    private void ScheduleProgramFirstRoundRefund(ProgramSkillFrame frame)
    {
        ValidateProgramTurnEffectGrant(frame);
        var activation = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Activation;
        if (frame.TriggerId is not null || activation?.UsesPerGame != 1 || frame.InstructionIndex != 1 || frame.OwnerSeat != _turnProgression.OwnerSeat ||
            _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId, activation.UsageGroup, SkillUsageScope.Game) != 1)
            throw new InvalidOperationException("A limited refund requires the first instruction of the exact consumed game activation.");
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive || _roundNumber != 1) return;
        var refund = _turnCardUseEffects.ScheduleGameUsageRefund(_turnNumber, _turnProgression.OwnerSeat, frame.Id, frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame), activation.UsageGroup);
        AdvanceEventRulesAndQueueFact(new FirstRoundGameUsageRefundScheduledEvent(refund));
    }
    private void ResolveFirstRoundGameUsageRefunds(int turn, int turnSeat)
    {
        foreach (var refund in _turnCardUseEffects.FirstRoundGameUsageRefunds.Where(r => r.TurnNumber == turn && r.TurnSeat == turnSeat))
        {
            var source = refund.Source;
            var refunded = _winner == Winner.None && _players[source.OwnerSeat].IsAlive &&
                HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) &&
                _skillRuntimeState.GetUsage(source.OwnerSeat, source.SkillId, refund.UsageId, SkillUsageScope.Game) == 1 &&
                _skillRuntimeState.ClearUsage(source.OwnerSeat, source.SkillId, refund.UsageId, SkillUsageScope.Game);
            AdvanceEventRulesAndQueueFact(new FirstRoundGameUsageRefundResolvedEvent(refund, refunded));
        }
    }
    private IReadOnlyList<TurnSlashSuitAllowance>? GetTurnSlashSuitAllowancesSnapshot(int owner)
    {
        var policies = _turnCardUseEffects.SlashSuitAllowances.Where(p => p.TurnNumber == _turnNumber && p.TurnSeat == _turnProgression.OwnerSeat && p.Source.OwnerSeat == owner).ToArray();
        return policies.Length == 0 ? null : Array.AsReadOnly(policies);
    }
    private IReadOnlyList<FirstRoundGameUsageRefund>? GetFirstRoundGameUsageRefundsSnapshot(int owner)
    {
        var refunds = _turnCardUseEffects.FirstRoundGameUsageRefunds.Where(p => p.TurnNumber == _turnNumber && p.TurnSeat == _turnProgression.OwnerSeat && p.Source.OwnerSeat == owner).ToArray();
        return refunds.Length == 0 ? null : Array.AsReadOnly(refunds);
    }
}
