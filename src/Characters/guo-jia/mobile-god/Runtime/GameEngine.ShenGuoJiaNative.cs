namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanBeginShenGuoJiaJudgment(ProgramSkillFrame frame, int targetSeat, string reason, string resultBind, SkillProgramCardSetVisibility visibility) =>
        ValidShenGuoJia(frame) && frame.ShenGuoJia is { Stage: ShenGuoJiaStage.Judging, Operation: SkillProgramEffectOp.ResolveShenGuoJiaHuishi } receipt &&
        receipt.JudgmentId > 0 && targetSeat == frame.OwnerSeat && reason == "mobile-huishi" && resultBind == $"huishi-{receipt.Judgments.Count}" && visibility == SkillProgramCardSetVisibility.Public;
    private bool IsShenGuoJiaJudgment(JudgmentFrame judgment, ProgramSkillFrame frame) =>
        CanBeginShenGuoJiaJudgment(frame, judgment.TargetSeat, judgment.Reason, judgment.ProgramResultBind ?? "", judgment.ProgramResultVisibility ?? SkillProgramCardSetVisibility.Private) &&
        judgment.Id == frame.ShenGuoJia!.JudgmentId && judgment.ParentFrameId == frame.Id && judgment.SourceSeat == frame.OwnerSeat && judgment.Continuation == JudgmentContinuationKind.ProgramSkill;
    private void BeginShenGuoJiaJudgment(ProgramSkillFrame frame)
    {
        var receipt = frame.ShenGuoJia!; var judgmentId = _resolutionSequence + 1;
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.Judging, JudgmentId = judgmentId }, PendingMovementContinuation = null });
        _ = BeginJudgment(null, frame.OwnerSeat, "mobile-huishi", frame.Id, null, JudgmentContinuationKind.ProgramSkill,
            sourceSeat: frame.OwnerSeat, programResultBind: $"huishi-{receipt.Judgments.Count}", programResultVisibility: SkillProgramCardSetVisibility.Public);
    }
    private bool TryReturnShenGuoJiaJudgment(JudgmentFrame judgment)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenGuoJia: { Stage: ShenGuoJiaStage.Judging } receipt } frame || judgment.ParentFrameId != frame.Id) return false;
        if (!IsShenGuoJiaJudgment(judgment, frame) || _resolutionStack.Any(value => value.Id == judgment.Id)) throw new InvalidOperationException("Huishi requires its completed own native judgment.");
        var result = ProgramEventHistory<JudgmentResolvedEvent>().Single(fact => fact.ResolutionId == judgment.Id && fact.ParentResolutionId == frame.Id && fact.Reason == "mobile-huishi" && fact.TargetSeat == frame.OwnerSeat);
        var retained = result.CardId is { } id && IsUnclaimedJudgmentOutcomeCard(judgment, id) ? id : (int?)null;
        var handoff = retained is { } card ? _cardMovements.Last(movement => movement.CardId == card).Sequence : (long?)null;
        var outcome = new ShenGuoJiaJudgment(judgment.Id, result.CardId, result.Suit, result.Rank, handoff);
        var mayRepeat = result.Suit is { } suit && !receipt.Judgments.Any(previous => previous.Suit == suit) && _players[frame.OwnerSeat].MaxHp < 10;
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.JudgmentChildren, Judgments = receipt.Judgments.Append(outcome).ToArray(),
            Cards = retained is { } owned ? receipt.Cards.Append(owned).ToArray() : receipt.Cards, MayRepeat = mayRepeat, Before = handoff is { } sequence ? sequence - 1 : _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaJudgmentReturnedEvent(frame.Id, receipt.Judgments.Count, outcome));
        SetProgramCardSet(frame.Id, $"huishi-{receipt.Judgments.Count}", retained is { } bound ? [bound] : [], SkillProgramCardSetVisibility.Public,
            retained is not null ? [CardLocation.Processing] : [], result.Suit);
        ContinueShenGuoJia(frame.Id); return true;
    }
    private IReadOnlyList<ShenGuoJiaPlan> ShenGuoJiaPlans(int owner)
    {
        var actor = _players[owner];
        var options = BuildProgramOrdinaryTrickUseOptions(actor, physicalSuit: Suit.None, enforceUsePermission: true,
            beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true, physicalCardIds: []);
        return Array.AsReadOnly(NextActualUseOrdinaryTrickOptions(actor, options, Suit.None, null, Suit.None, false)
            .Where(option => IsOrdinaryTrick(option.EffectiveCardKind))
            .Select(option => new ShenGuoJiaPlan(option.EffectiveCardKind, option.ActionKind, option.TargetCardId, option.RequiredCardKind, option.NextActualUseAdjusted)
            { Targets = GetImplicitSelfCardUseTargets(option.EffectiveCardKind, owner, option.TargetSeats) }).ToArray());
    }
    private static bool SameShenGuoJiaPlan(ShenGuoJiaPlan left, ShenGuoJiaPlan right) =>
        (left.Kind, left.ActionKind, left.TargetCardId, left.RequiredKind, left.Adjusted) == (right.Kind, right.ActionKind, right.TargetCardId, right.RequiredKind, right.Adjusted) && left.Targets.SequenceEqual(right.Targets);
    private void IssueShenGuoJiaVirtualUse(ProgramSkillFrame frame)
    {
        var receipt = frame.ShenGuoJia!;
        if (!ValidShenGuoJia(frame) || receipt is not { Stage: ShenGuoJiaStage.CostChildren, Operation: SkillProgramEffectOp.ResolveShenGuoJiaZuoxing, Plan: { } plan } ||
            !ShenGuoJiaPlans(frame.OwnerSeat).Any(option => SameShenGuoJiaPlan(option, plan))) { FinishShenGuoJia(frame); return; }
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.IssuingUse } });
        if (plan.Adjusted) _selectedNextCardTargetSeats = plan.Targets;
        var origin = new ProgramVirtualOrdinaryTrickOrigin(frame.Id, frame.InstructionIndex, 0, frame.OwnerSeat, frame.OwnerSeat, frame.OwnerSeat,
            frame.SkillId, frame.ActivationId, frame.SkillInstanceId, frame.GameplayHash, receipt.Turn, plan.Kind, SkillProgramConditionKind.Always, null, null);
        var card = new Card(0, plan.Kind, Suit.None, 0);
        var id = BeginCardUse(card, frame.OwnerSeat, plan.Targets, plan.Kind, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin);
        var use = LifecycleCardUse(id)!;
        frame = _resolutionStack.OfType<ProgramSkillFrame>().Single(value => value.Id == frame.Id);
        ReplaceRuntimeFrame(frame.Id, frame = frame with { ShenGuoJia = frame.ShenGuoJia! with { Stage = ShenGuoJiaStage.NativeUse, UseId = id, ActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaNativeIssuedEvent(frame.Id, id, use.Action!.ActionId, plan.Kind, receipt.Benefactor));
        BeginJizhiOrNullificationWindow(id, card, frame.OwnerSeat, use.TargetSeats, plan.ActionKind, plan.TargetCardId, plan.RequiredKind, plan.Kind);
    }
    private bool IsShenGuoJiaVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: ShenGuoJiaZuoxing } origin ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == origin.ParentProgramFrameId) is not { ShenGuoJia: { Plan: { } plan } receipt } frame ||
            !ValidShenGuoJia(frame) || receipt.Stage is not (ShenGuoJiaStage.IssuingUse or ShenGuoJiaStage.NativeUse) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } action ||
            action.ProviderSeat != frame.OwnerSeat || use.SourceSeat != action.ActorSeat || action.ActionId != origin.CardActionId || action.EffectiveKind != plan.Kind || use.CardKind != plan.Kind ||
            action.EffectiveSuit is not (null or Suit.None) || action.EffectiveRank is not (null or 0) || action.EffectiveIsRed == true || !action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            origin.OwnerSeat != frame.OwnerSeat || origin.InitialActorSeat != frame.OwnerSeat || origin.InitialTargetSeat != frame.OwnerSeat || origin.BindingId != frame.ActivationId ||
            origin.SkillInstanceId != frame.SkillInstanceId || origin.GameplayHash != frame.GameplayHash || origin.InstructionIndex != frame.InstructionIndex || origin.LifecycleFrameId != 0 || origin.TurnNumber != receipt.Turn ||
            receipt.UseId != 0 && (receipt.UseId != use.Id || receipt.ActionId != action.ActionId)) return false;
        return ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Count(fact => fact.FrameId == frame.Id && fact.Purpose == "zuoxing" && fact.Seat == receipt.Benefactor && fact.After == fact.Before - 1) == 1 &&
            (ProgramEventHistory<CardUseDeclaredEvent>().Any(fact => fact.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, frame.OwnerSeat, frame.OwnerSeat) : action.ActorSeat == frame.OwnerSeat);
    }
    private bool TryReturnShenGuoJiaVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: ShenGuoJiaZuoxing } origin) return false;
        var frame = GetActiveProgramFrame(origin.ParentProgramFrameId); var receipt = frame.ShenGuoJia!;
        if (!ValidShenGuoJia(frame) || receipt.Stage != ShenGuoJiaStage.NativeUse || receipt.UseId != use.Id || receipt.ActionId != use.Action?.ActionId ||
            use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<ShenGuoJiaNativeReturnedEvent>().Any(fact => fact.FrameId == frame.Id) ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == use.Id && fact.CardId == 0 && fact.CardKind == receipt.Plan!.Kind) != 1)
            throw new InvalidOperationException("Zuoxing requires the same complete native zero-material Use once.");
        var hadAttack = use.CardAttack is { Active: true };
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.ReturnedUse } });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaNativeReturnedEvent(frame.Id, use.Id, receipt.ActionId, hadAttack));
        if (!hadAttack) FinishShenGuoJia(frame); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 38000)]
    private bool ReturnShenGuoJiaAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenGuoJia: { Stage: ShenGuoJiaStage.ReturnedUse } receipt } frame) return false;
        if (!ValidShenGuoJia(frame) || LifecycleCardUse(receipt.UseId) is not null || !ProgramEventHistory<ShenGuoJiaNativeReturnedEvent>().Any(fact => fact.FrameId == frame.Id && fact.HadAttack)) throw new InvalidOperationException("Zuoxing cannot resume before the native damage tail returns.");
        FinishShenGuoJia(frame); return true;
    }
}
