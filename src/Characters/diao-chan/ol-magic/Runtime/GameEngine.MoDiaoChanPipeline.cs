namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 1)]
    private string? MoDcQingshiCardLabel(int cardId) => HasMoDcQingshiCard(cardId) ? "倾世" : null;

    [ProgramPipelineHook(ProgramPipelinePhase.SelectDiscardBatchIndexes, 1)]
    private int[]? MoDcRecaptureBatchIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate,
        ProgramInstructionFeatures features) =>
        features.HasOperation(SkillProgramEffectOp.ResolveMoDiaoChanQingshiDiscard)
            ? MoDcRecaptureIndexes(batch, candidate.OwnerSeat) : null;

    [ProgramPipelineHook(ProgramPipelinePhase.FilterPlayPhaseActions, 1)]
    private IReadOnlyList<LegalAction> FilterMoDiaoChanHandSelectionPipeline(CharacterState actor,
        IReadOnlyList<LegalAction> actions) => FilterMoDiaoChanHandSelection(actor, actions);
}
