namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource YuanYinSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardMoveReason YuanYinDrawReason => new("skill-program.anniversary:moshou.draw");
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedBeforeActivity, 49300)]
    [ProgramTargetWindow(ProgramTargetWindowStage.ImplicitSelfDrawTwo, 49300)]
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 49300)]
    private bool YuanYinTargetWindows(ProgramTargetCardWindowQuery q) => _players.Any(p => p.IsAlive &&
        GetSkillBindingShard(p).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseTargetsFinalized).Any(b => b.SkillId == AnniversaryYuanYinComposition.Moshou));
    [ProgramCapability] private sealed class AnniversaryYuanYinHost(GameEngine e) : IAnniversaryYuanYinHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou ? e.BeginYuanYinMoshou(f) : e.BeginYuanYinYunjiu(f); }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 49300, SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou, SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu)]
    private bool CanOfferYuanYin(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu }]) return CanOfferYuanYinYunjiu(c, w);
        return _players[c.OwnerSeat].HasHpValue && w is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } card, Facts.CardActionCardIsBlack: true } &&
            LifecycleCardUse(card.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } a } use && a.ActionId == card.CardActionId &&
            use.TargetSeats.Contains(c.OwnerSeat);
    }
    private SkillProgramStepOutcome BeginYuanYinMoshou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryYuanYin is not null || f.InstructionIndex != 1 || f.SkillId != AnniversaryYuanYinComposition.Moshou || f.TriggerId != "black-target-cycle" ||
            f.WindowContext is not { CardUse: { } card } w || !CanOfferYuanYin(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), w))
            throw new InvalidOperationException("Moshou requires its original real black-card target after the optional activation.");
        var source = YuanYinSource(f);
        var previous = ProgramEventHistory<AnniversaryYuanYinMoshouAdvancedEvent>().LastOrDefault(e => e.Source == source && e.Hash == f.GameplayHash);
        var requested = previous is { Next: > 0 } ? previous.Next : Math.Max(1, _players[f.OwnerSeat].MaxHp);
        var next = requested <= 1 ? 0 : requested - 1;
        var r = new AnniversaryYuanYinReceipt(source, f.GameplayHash, w.ParentFrameId, _turnNumber, "moshou", "drawing")
        { UseId = card.ParentCardUseFrameId, ActionId = card.CardActionId, Requested = requested, Next = next };
        ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinStartedEvent(f.Id, source, r.Hash, r.ParentId, r.Turn, r.Mode));
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinMoshouIssuedEvent(f.Id, source, r.Hash, r.UseId, r.ActionId, requested, next));
        BeginOperationProgramDraw(f, f.OwnerSeat, requested, YuanYinDrawReason); return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou)] private void ReturnYuanYinMoshouDraw(ProgramSkillFrame f, NativeDrawOutcome draw)
    {
        if (f.AnniversaryYuanYin is not { Mode: "moshou", Stage: "drawing", Draw: null } r || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != r.Requested || draw.SequenceAfter < draw.SequenceBefore || draw.SequenceAfter > _movementSequence)
            throw new InvalidOperationException("Moshou lost its one original native draw request and typed result.");
        ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = r with { Stage = "draw-children", Draw = draw, Before = draw.SequenceBefore, After = draw.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinMoshouAdvancedEvent(f.Id, r.Source, r.Hash, r.Requested, r.Next, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter));
        ContinueYuanYin(f.Id);
    }
    private bool DrainYuanYin(ProgramSkillFrame f) => TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 49300)] private bool ContinueYuanYin(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryYuanYin: { } r } f || f.Id != id) return false;
        if (r.Mode == "yunjiu") { ContinueYuanYinYunjiu(f); return true; }
        if (r.Stage == "drawing") return true;
        if (!ValidYuanYin(f)) throw new InvalidOperationException("Yuan Yin lost his original paid black-target draw.");
        if (DrainYuanYin(f)) return true;
        FinishYuanYin(GetActiveProgramFrame(id)); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 49300)] private bool ReturnYuanYinMovement(ProgramSkillFrame f) => f.AnniversaryYuanYin is not null && ContinueYuanYin(f.Id);
    private void FinishYuanYin(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinCompletedEvent(f.Id));
        ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
}
