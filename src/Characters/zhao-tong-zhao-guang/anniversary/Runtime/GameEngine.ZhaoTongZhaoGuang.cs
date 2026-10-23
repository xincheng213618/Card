namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnniversaryQingrenDrawReason = "skill-program.anniversary:qingren.draw";
    [ProgramCapability] private sealed class AnniversaryZhaoTongZhaoGuangHost(GameEngine engine) : IAnniversaryZhaoTongZhaoGuangHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginAnniversaryZhaoTongZhaoGuang(f, op); }
    private CardConversionSource AnniversaryZtgSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int AnniversaryYizanCount(int owner, int? actualTurn = null, long cutoff = long.MaxValue) => ProgramEventHistory<AnniversaryYizanInvokedEvent>().Count(e =>
        e.Source.OwnerSeat == owner && e.ActionId <= cutoff && (actualTurn is null || e.ActualTurn == actualTurn) &&
        _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 44600, SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan)]
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44600, SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan)]
    private bool QualifyAnniversaryLongyuan(ProgramTriggerLegalityContext q) => !AnniversaryYizanModified(q.OwnerSeat) &&
        (AnniversaryYizanCount(q.OwnerSeat) >= 3 || HasShenGuoJiaAwakeningCondition(q.OwnerSeat, q.SkillId, q.Candidate.BindingId));

    private SkillProgramStepOutcome BeginAnniversaryZhaoTongZhaoGuang(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!AnniversaryZtgOriginal(f) || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            op == SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan && (f.SkillId != AnniversaryZhaoTongZhaoGuangComposition.Longyuan || f.InstructionIndex != 3 || AnniversaryYizanModified(f.OwnerSeat)) ||
            op == SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren && (f.SkillId != AnniversaryZhaoTongZhaoGuangComposition.Qingren || f.InstructionIndex != 1 || f.AnniversaryQingren is not null))
            throw new InvalidOperationException($"Zhao Tong/Zhao Guang requires the exact original ending candidate and current instruction ({f.Id}/{f.SkillId}/{f.TriggerId}/{f.InstructionIndex}).");
        var source = AnniversaryZtgSource(f);
        if (op == SkillProgramEffectOp.AwakenAnniversaryZhaoTongZhaoGuangLongyuan)
        {
            AdvanceEventRulesAndQueueFact(new AnniversaryLongyuanAwakenedEvent(f.Id, source, f.GameplayHash, _turnNumber));
            AdvanceEventRulesAndQueueFact(new SkillAwakenedEvent(f.OwnerSeat, f.SkillId, _players[f.OwnerSeat].MaxHp, Array.AsReadOnly(Array.Empty<string>())));
            return SkillProgramStepOutcome.Continue;
        }
        var count = AnniversaryYizanCount(f.OwnerSeat, _turnNumber, _cardActionSequence);
        var receipt = new AnniversaryQingrenReceipt(source, f.GameplayHash, f.WindowContext!.ParentFrameId, _turnNumber, _cardActionSequence, count,
            count == 0 ? AnniversaryQingrenStage.Completed : AnniversaryQingrenStage.Drawing);
        ReplaceRuntimeTop(f = f with { AnniversaryQingren = receipt });
        AdvanceEventRulesAndQueueFact(new AnniversaryQingrenIssuedEvent(f.Id, source, f.GameplayHash, receipt.ParentId, receipt.Turn, receipt.CutoffActionId, count));
        if (count == 0) return SkillProgramStepOutcome.Continue;
        BeginOperationProgramDraw(f, f.OwnerSeat, count, new(AnniversaryQingrenDrawReason));
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawAnniversaryZhaoTongZhaoGuangQingren)]
    private void ReturnAnniversaryQingrenDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryQingren!;
        if (!ValidAnniversaryQingren(f) || r.Stage != AnniversaryQingrenStage.Drawing || f.NativeDraw is not null ||
            draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != r.Count || draw.SequenceAfter != _movementSequence || r.Draw is not null)
            throw new InvalidOperationException("Qingren requires its original once-issued native draw return.");
        ReplaceRuntimeTop(f with { AnniversaryQingren = r with { Stage = AnniversaryQingrenStage.DrawChildren, Draw = draw }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryQingrenDrawReturnedEvent(f.Id, r.Count, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter));
        ContinueAnniversaryQingren(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 44600)] private bool ContinueAnniversaryQingren(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryQingren: { Stage: AnniversaryQingrenStage.DrawChildren } r } f || f.Id != id || _pendingDecision is not null) return false;
        if (!ValidAnniversaryQingren(f)) throw new InvalidOperationException("Qingren lost its original paid draw or native movement children.");
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f with { AnniversaryQingren = r with { Stage = AnniversaryQingrenStage.Completed }, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44600)] private bool ReturnAnniversaryQingrenMovement(ProgramSkillFrame f) =>
        f.AnniversaryQingren is not null && ContinueAnniversaryQingren(f.Id);
}
