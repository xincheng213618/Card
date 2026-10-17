namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OrdinaryBingyiDrawReason = "skill-program.gu-yong.bingyi.draw";
    private const string OrdinaryBingyiShownBind = "ordinary-bingyi-shown-hand";
    private bool HasOrdinaryGuYongCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi);
    private bool IsOrdinaryGuYongBingyiTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi }];
    private static CardConversionSource OrdinaryGuYongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool OrdinaryBingyiPhaseSpent(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<OrdinaryBingyiShownEvent>().Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == "ol:bingyi" && e.Phase == phase);
    private bool IsOrdinaryBingyiDiscard(CardMovementRecord m, int owner) => _cardMovements.Contains(m) && GetProgramDiscardSource(m) is { OwnerSeat: { } seat, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } && seat == owner;

    private bool OrdinaryBingyiMovementRoot(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.SkillId != "ol:bingyi" || c.BindingId != "ordinary-discard-reveal-share" || !IsOrdinaryGuYongBingyiTrigger(GetProgramTrigger(c)) || context.Window != SkillProgramTriggerWindow.DiscardPileReceived || context.OwnerSeat != c.OwnerSeat || context.SourceSeat != c.OwnerSeat || context.OccurrenceIndex != c.OccurrenceIndex || context.MovementIndex != c.OccurrenceIndex || context.MovementBatch is not { DiscardRecoveryPhase: { } phase } batch || !IsRecordedActualDiscardRecoveryPhase(phase) || phase.TurnNumber != batch.TurnNumber || batch.Movements.Count == 0 || c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count || !IsOrdinaryBingyiDiscard(batch.Movements[c.OccurrenceIndex], c.OwnerSeat)) return false;
        var first = batch.Movements.Select((m, i) => (m, i)).FirstOrDefault(x => IsOrdinaryBingyiDiscard(x.m, c.OwnerSeat));
        if (first.m is null || first.i != c.OccurrenceIndex || _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent || parent.Batch.Id != batch.Id || parent.Batch.DiscardRecoveryPhase != phase || !parent.Batch.Movements.SequenceEqual(batch.Movements) || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c) return false;
        return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27598)]
    private bool CanOfferOrdinaryBingyi(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!IsOrdinaryGuYongBingyiTrigger(trigger)) return true;
        return _players[c.OwnerSeat].IsAlive && GetHand(_players[c.OwnerSeat]).Count > 0 && OrdinaryBingyiMovementRoot(c, context) && !OrdinaryBingyiPhaseSpent(c.OwnerSeat, context.MovementBatch!.DiscardRecoveryPhase!);
    }
    [ProgramCapability]
    private sealed class OrdinaryGuYongCapability(GameEngine engine) : IOrdinaryGuYongHost
    {
        public SkillProgramStepOutcome Bingyi(ProgramSkillFrame f) => engine.BeginOrdinaryBingyi(f);
    }
}
