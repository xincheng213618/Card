namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A completed paid rescue can be followed by another from the same seat.
    // Correlate the actual use to this binding, rather than its net HP result:
    // recovery may have been redirected or followed by a different HP change.
    private bool ProgramPaidDyingRescue(ProgramSkillFrame frame, DyingFrame dying)
    {
        var started = false;
        long? roundPileAlcoholUse = null;
        foreach (var fact in CompleteProgramEventHistory())
        {
            if (fact is ProgramBindingStartedEvent begin && begin.FrameId == frame.Id)
            {
                started = begin.OwnerSeat == frame.OwnerSeat && begin.SkillId == frame.SkillId &&
                    begin.BindingId == frame.TriggerId && begin.SkillInstanceId == frame.SkillInstanceId &&
                    begin.Window == SkillProgramTriggerWindow.DyingResponse;
                continue;
            }
            if (!started) continue;
            if (fact is ProgramDyingRescueEvent paid && paid.DyingFrameId == dying.Id &&
                paid.SkillId == frame.SkillId && paid.OwnerSeat == frame.OwnerSeat && paid.VictimSeat == dying.VictimSeat)
                return true;
            if (fact is ProgramRecoveryAppliedEvent recovery && recovery.ProgramFrameId == frame.Id &&
                recovery.Receipt.TargetSeat == dying.VictimSeat && recovery.Receipt.HpAfter > recovery.Receipt.HpBefore)
                return true;
            if (fact is ProgramRoundPileAlcoholIssuedEvent issued && issued.ProgramFrameId == frame.Id &&
                issued.DyingFrameId == dying.Id && issued.VictimSeat == dying.VictimSeat &&
                issued.Source == new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId))
                roundPileAlcoholUse = issued.CardUseFrameId;
            if (fact is CardUseFinishedEvent { CardKind: CardKind.Alcohol } finished &&
                roundPileAlcoholUse == finished.ResolutionId)
                return true;
        }
        return false;
    }
}
