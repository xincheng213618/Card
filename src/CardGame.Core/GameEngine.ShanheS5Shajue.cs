namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Keep the original OL operation and all native causal-entity receipts.
    // S5 changes only whether this mandatory binding is offered at entry.
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5793,
        SkillProgramEffectOp.ReplenishShaJueAndClaimDyingCause)]
    private bool CanOfferShanheS5Shajue(ProgramTriggerLegalityContext gate) =>
        gate.SkillId != "shanhe:s5-shajue-853" ||
        gate.WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering, XuRongDying: { } proof } &&
        proof.VictimSeat != gate.OwnerSeat && gate.WindowContext.TargetSeat == proof.VictimSeat &&
        proof.HpAtEntry < 0 && proof.RescueCardsRequired > 1 && proof.RescueCardsRequired == 1 - proof.HpAtEntry &&
        _resolutionStack.OfType<DyingFrame>().Any(d => d.Id == proof.DyingFrameId && d.XuRongProof == proof);
}
