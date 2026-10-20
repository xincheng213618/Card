namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOtherOwnedCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ChooseProgramOtherOwnedCardDiscard(frame, chooser, zones, reason);

    private IReadOnlyList<PromptChoice> BuildOtherOwnedCardDiscardChoices(
        long frameId,
        int chooserSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().BuildOtherOwnedCardDiscardChoices(frameId, chooserSeat, zones, reason);

    private void ResolveProgramOtherOwnedCardDiscardChoice(PromptChoice selected) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ResolveProgramOtherOwnedCardDiscardChoice(selected);

    [ProgramAiChoice(
        SkillProgramEffectOp.DiscardDamageTargetAndClaimMount,
        SkillProgramEffectOp.ChooseOtherOwnedCardDiscard)]
    private PromptChoice SelectAiProgramOtherOwnedCardDiscard(PendingDecision decision, ProgramSkillFrame frame)
    {
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var (selected, thought) = _aiBrains[decision.PlayerSeat].ChooseOtherOwnedCardDiscard(
            CreateSnapshot(decision.PlayerSeat), decision.Choices, skill.Name, ++_thoughtSequence);
        AddThought(thought);
        return selected;
    }
}
