namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ChooseProgramOwnCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference? chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ChooseProgramOwnCardDiscard(frame, chooser, zones, reason);

    private IReadOnlyList<PromptChoice> BuildOwnCardDiscardChoices(
        long frameId,
        int chooserSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().BuildOwnCardDiscardChoices(frameId, chooserSeat, zones, reason);

    private void ResolveProgramOwnCardDiscardChoice(PromptChoice selected) =>
        GetRuntimeModule<ProgramPaidCardRuntime>().ResolveProgramOwnCardDiscardChoice(selected);

    [ProgramAiChoice(SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp, SkillProgramEffectOp.ChooseOwnCardDiscard)]
    private PromptChoice SelectAiProgramOwnCardDiscard(PendingDecision decision, ProgramSkillFrame frame)
    {
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var (selected, thought) = _aiBrains[decision.PlayerSeat].ChooseOwnCardDiscard(
            CreateSnapshot(decision.PlayerSeat), decision.Choices, skill.Name, ++_thoughtSequence);
        AddThought(thought);
        return selected;
    }
}
