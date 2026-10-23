namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueHappyGuoJiaUse(ProgramSkillFrame frame)
    {
        var receipt = frame.HappyGuoJia!;
        if (!ValidHappyGj(frame) || receipt is not { Stage: HappyGuoJiaStage.CostChildren, Operation: SkillProgramEffectOp.ResolveHappyGuoJiaZuoxing, Plan: { } plan } ||
            !HappyGjPlans(frame.OwnerSeat).Any(option => SameShenGuoJiaPlan(option, plan))) { FinishHappyGj(frame); return; }
        ReplaceRuntimeTop(frame = frame with { HappyGuoJia = receipt with { Stage = HappyGuoJiaStage.IssuingUse } });
        if (plan.Adjusted) _selectedNextCardTargetSeats = plan.Targets;
        var origin = new ProgramVirtualOrdinaryTrickOrigin(frame.Id, frame.InstructionIndex, 0, frame.OwnerSeat, frame.OwnerSeat, frame.OwnerSeat,
            frame.SkillId, frame.ActivationId, frame.SkillInstanceId, frame.GameplayHash, receipt.Turn, plan.Kind, SkillProgramConditionKind.Always, null, null);
        var card = new Card(0, plan.Kind, Suit.None, 0);
        var id = BeginCardUse(card, frame.OwnerSeat, plan.Targets, plan.Kind, physicalCardIds: [], virtualOrdinaryTrickOrigin: origin);
        var use = LifecycleCardUse(id)!;
        frame = _resolutionStack.OfType<ProgramSkillFrame>().Single(value => value.Id == frame.Id);
        ReplaceRuntimeFrame(frame.Id, frame = frame with { HappyGuoJia = frame.HappyGuoJia! with { Stage = HappyGuoJiaStage.NativeUse, UseId = id, ActionId = use.Action!.ActionId } });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaNativeIssuedEvent(frame.Id, id, use.Action!.ActionId, plan.Kind, receipt.Benefactor));
        BeginJizhiOrNullificationWindow(id, card, frame.OwnerSeat, use.TargetSeats, plan.ActionKind, plan.TargetCardId, plan.RequiredKind, plan.Kind);
    }
    private bool IsHappyGuoJiaVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: HappyGjZuoxing } origin ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == origin.ParentProgramFrameId) is not { HappyGuoJia: { Plan: { } plan } receipt } frame ||
            !ValidHappyGj(frame) || receipt.Stage is not (HappyGuoJiaStage.IssuingUse or HappyGuoJiaStage.NativeUse) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } action ||
            action.ProviderSeat != frame.OwnerSeat || use.SourceSeat != action.ActorSeat || action.ActionId != origin.CardActionId || action.EffectiveKind != plan.Kind || use.CardKind != plan.Kind ||
            action.EffectiveSuit is not (null or Suit.None) || action.EffectiveRank is not (null or 0) || action.EffectiveIsRed == true || !action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            origin.OwnerSeat != frame.OwnerSeat || origin.InitialActorSeat != frame.OwnerSeat || origin.InitialTargetSeat != frame.OwnerSeat || origin.BindingId != frame.ActivationId ||
            origin.SkillInstanceId != frame.SkillInstanceId || origin.GameplayHash != frame.GameplayHash || origin.InstructionIndex != frame.InstructionIndex || origin.LifecycleFrameId != 0 || origin.TurnNumber != receipt.Turn ||
            receipt.UseId != 0 && (receipt.UseId != use.Id || receipt.ActionId != action.ActionId)) return false;
        return ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Count(fact => fact.FrameId == frame.Id && fact.Purpose == "happy-zuoxing" && fact.Seat == receipt.Benefactor && fact.After == fact.Before - 1) == 1 &&
            (ProgramEventHistory<CardUseDeclaredEvent>().Any(fact => fact.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, frame.OwnerSeat, frame.OwnerSeat) : action.ActorSeat == frame.OwnerSeat);
    }
    private bool TryReturnHappyGuoJiaVirtualUse(CardUseFrame use)
    {
        if (use.VirtualOrdinaryTrickOrigin is not { SkillId: HappyGjZuoxing } origin) return false;
        var frame = GetActiveProgramFrame(origin.ParentProgramFrameId); var receipt = frame.HappyGuoJia!;
        if (!ValidHappyGj(frame) || receipt.Stage != HappyGuoJiaStage.NativeUse || receipt.UseId != use.Id || receipt.ActionId != use.Action?.ActionId ||
            use.Step != ResolutionFrameStep.Completed || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<ShenGuoJiaNativeReturnedEvent>().Any(fact => fact.FrameId == frame.Id) ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == use.Id && fact.CardId == 0 && fact.CardKind == receipt.Plan!.Kind) != 1)
            throw new InvalidOperationException("Zuoxing requires the same complete native zero-material Use once.");
        var hadAttack = use.CardAttack is { Active: true };
        ReplaceRuntimeTop(frame = frame with { HappyGuoJia = receipt with { Stage = HappyGuoJiaStage.ReturnedUse } });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaNativeReturnedEvent(frame.Id, use.Id, receipt.ActionId, hadAttack));
        if (!hadAttack) FinishHappyGj(frame); return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 40900)]
    private bool ReturnHappyGuoJiaAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyGuoJia: { Stage: HappyGuoJiaStage.ReturnedUse } receipt } frame) return false;
        if (!ValidHappyGj(frame) || LifecycleCardUse(receipt.UseId) is not null || !ProgramEventHistory<ShenGuoJiaNativeReturnedEvent>().Any(fact => fact.FrameId == frame.Id && fact.HadAttack)) throw new InvalidOperationException("Zuoxing cannot resume before the native damage tail returns.");
        FinishHappyGj(frame); return true;
    }
}
