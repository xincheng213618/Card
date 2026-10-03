namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasCharacterStateCardUseContinuation()
    {
        var windows = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
            .Where(frame => frame.CharacterStateContinuation is CharacterStateContinuation.CardUse or CharacterStateContinuation.VirtualBasicCardUse).ToArray();
        foreach (var window in windows)
        {
            var index = _resolutionStack.IndexOf(window);
            if (index < 1 || _resolutionStack[index - 1] is not CardUseFrame parent ||
                parent.Id != window.ResumeProgramFrameId ||
                (window.CharacterStateContinuation == CharacterStateContinuation.VirtualBasicCardUse ? parent.CardId != 0 || parent.VirtualBasicReturn is null || parent.VirtualBasicEffectApplied != true || window.ResumeCardId is not null || window.ResumeCardKind != parent.CardKind : parent.CardId != window.ResumeCardId) ||
                window.Continuation != ProgramLifecycleContinuation.ResumeCharacterStateChange ||
                window.Window is not (SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain) ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count)
                throw new InvalidOperationException("A character-state card continuation lost its exact parent or candidate cursor.");
        }
        return windows.Length != 0;
    }
}
