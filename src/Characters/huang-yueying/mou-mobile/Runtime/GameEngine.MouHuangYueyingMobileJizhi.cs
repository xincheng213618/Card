namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouHuangYueyingMobile => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouHuangYueyingJizhi) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment);
    private static CardConversionSource MouHySource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class MouHuangYueyingMobileCapability(GameEngine g) : IMouHuangYueyingMobileHost, IMouHuangYueyingMobileQuery
    {
        public bool CanPlace(int owner, int target) => g.MouHyEquipmentChoices(owner, target).Any();
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveMouHuangYueyingJizhi => g.BeginMouHyJizhi(f),
            SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment => g.BeginMouHyQicai(f),
            _ => g.BeginMouHyQiTransfer(f)
        };
    }
    private SkillProgramStepOutcome BeginMouHyJizhi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouHuangYueyingMobile is not null) { ContinueMouHy(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouHuangYueyingMobileComposition.Jizhi || f.InstructionIndex != 1 || f.WindowContext is not { } c ||
            !(f.TriggerId == "ordinary-trick-used" && c.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects ||
              f.TriggerId == "nullification-used" && c.Window == SkillProgramTriggerWindow.CardResponseAccepted))
            throw new InvalidOperationException("Mobile Jizhi requires the exact native ordinary-trick Use or Nullification response.");
        var source = MouHySource(f);
        ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = new(source, f.GameplayHash, SkillProgramEffectOp.ResolveMouHuangYueyingJizhi,
            MouHuangYueyingMobileStage.Drawing, _turnNumber, _turnProgression.OwnerSeat, c.ParentFrameId, f.OwnerSeat, Paid: true) });
        AdvanceEventRulesAndQueueFact(new MouHuangYueyingJizhiStartedEvent(f.Id, source, f.GameplayHash, c.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat));
        BeginOperationProgramDraw(f, f.OwnerSeat, 1, new("skill-program.mou-mobile:jizhi.draw"));
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouHuangYueyingJizhi)]
    private void ReturnMouHyJizhiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MouHuangYueyingMobile is not { Operation: SkillProgramEffectOp.ResolveMouHuangYueyingJizhi, Stage: MouHuangYueyingMobileStage.Drawing } r ||
            outcome.RequestedCount != 1 || outcome.RecipientSeat != f.OwnerSeat)
            throw new InvalidOperationException("Jizhi lost its exact native Draw1 return.");
        foreach (var id in outcome.ActualDrawIds)
        {
            var movement = _cardMovements.Single(m => m.CardId == id && m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.mou-mobile:jizhi.draw");
            AdvanceEventRulesAndQueueFact(new MouHuangYueyingJizhiExemptEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwnerSeat, id, movement.Sequence));
        }
        ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = r with { Stage = MouHuangYueyingMobileStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouHuangYueyingJizhiDrawReturnedEvent(f.Id, outcome.AttemptFrameId, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count));
        ContinueMouHy(f.Id);
    }
    private bool IsMouHyJizhiHandLimitExempt(CharacterState owner, int cardId) => HasMouHuangYueyingMobile && owner.IsAlive && cardId > 0 &&
        _cardZones.GetLocation(cardId) == CardLocation.Hand(owner.Seat) && (_turnNumber == 0 || HasOngoingWholeActualUseTurn) &&
        ProgramEventHistory<MouHuangYueyingJizhiExemptEvent>().Any(e => e.Source.OwnerSeat == owner.Seat && e.CardId == cardId &&
            e.ActualTurn == _turnNumber && e.TurnOwnerSeat == _turnProgression.OwnerSeat);
    private bool DrainMouHy(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMouHy(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHuangYueyingMobile: { } r } f || f.Id != id) return;
        if (r.Stage is MouHuangYueyingMobileStage.Choosing or MouHuangYueyingMobileStage.Drawing) return;
        if (DrainMouHy(f)) return;
        FinishMouHy(GetActiveProgramFrame(id));
    }
    private void FinishMouHy(ProgramSkillFrame f)
    {
        var r = f.MouHuangYueyingMobile!;
        if (r.Operation == SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains)
            AdvanceEventRulesAndQueueFact(new MouHuangYueyingQiClaimSettledEvent(f.Id, r.GrantId!.Value, r.ClaimSequence!.Value));
        AdvanceEventRulesAndQueueFact(new MouHuangYueyingMobileCompletedEvent(f.Id, r.Operation, r.Paid));
        ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
}
