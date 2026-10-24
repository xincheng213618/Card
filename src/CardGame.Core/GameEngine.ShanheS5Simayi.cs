namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome GrantShanheS5Baiyin(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SkillId != "shanhe:s5-baiyin-14193" || active.TriggerId != "awakening" ||
            active.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId))
            throw new InvalidOperationException("S5 Baiyin requires its original owner preparation frame.");
        // The source body already prints Jilue. Awakening must not create a
        // second independently usable instance, including while the first is suppressed.
        var grants = _players[frame.OwnerSeat].SkillGrants.Grants;
        GrantProgramSkills(active, grants.Any(grant => grant.SkillId == "ol:jilue-2016") ? [] : ["ol:jilue-2016"]);
        return SkillProgramStepOutcome.Continue;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 59000,
        SkillProgramEffectOp.ChangeAttributedMarker)]
    private bool AllowsShanheS5SimayiDiscardRen(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId != "shanhe:s5-renjie-14192" || gate.Trigger.Id != "discard-ren") return true;
        // The declared OL2016 dependency already captures native movement timing.
        // Qualify the frozen movement, not the phase after its child triggers return.
        return gate.WindowContext is
            {
                Window: SkillProgramTriggerWindow.CardsMoved,
                MovementBatch: { MovementTiming: { Phase: TurnPhase.Discard } timing } batch,
                MovementIndex: { } index
            } && timing.PhaseActorSeat == gate.OwnerSeat && batch.TurnNumber == _turnNumber &&
            index >= 0 && index < batch.Movements.Count &&
            _cardMovements.Contains(batch.Movements[index]) &&
            GetProgramDiscardSource(batch.Movements[index]) == CardLocation.Hand(gate.OwnerSeat);
    }

    [ProgramCapability]
    private sealed class ShanheS5BaiyinCapability(GameEngine engine) : IShanheS5BaiyinHost
    { public SkillProgramStepOutcome GrantShanheS5Baiyin(ProgramSkillFrame frame) => engine.GrantShanheS5Baiyin(frame); }
}
