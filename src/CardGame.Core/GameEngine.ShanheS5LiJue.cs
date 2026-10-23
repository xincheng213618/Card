namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ShanheS5LangxiMinimumDamage(ProgramSkillFrame frame) =>
        ProgramInstructionResolver.Default.Features(GetProgramTrigger(frame))
            .HasOperation(SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage) ? 1 : 0;

    [ProgramCapability]
    private sealed class ShanheS5LiJueCapability(GameEngine engine) : IShanheS5LiJueProgramHost
    {
        public SkillProgramStepOutcome BeginPositiveRandomDamage(ProgramSkillFrame frame) => engine.BeginShanheS5Langxi(frame);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5750)]
    private bool CanOfferShanheS5Langxi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage) ||
        context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && LangxiTargets(candidate.OwnerSeat).Length > 0;

    private SkillProgramStepOutcome BeginShanheS5Langxi(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (frame.LiJue is not null || frame.InstructionIndex != 1 || frame.TriggerId is null ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
            index < 1 || _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Window != context.Window || parent.OwnerSeat != _currentSeat ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]) ||
            context.SourceSeat != _currentSeat || context.OwnerSeat != frame.OwnerSeat ||
            _turnNumber < 1 || !_players[_currentSeat].IsAlive || !LiJueCurrentSource(frame))
            throw new InvalidOperationException("S5 Langxi requires its original unpaid source at a living character's actual preparation window.");
        ReplaceRuntimeTop(frame = frame with
        {
            LiJue = new(LiJueSource(frame), frame.GameplayHash, frame.InstructionIndex, _turnNumber,
                context.ParentFrameId, LiJueStage.ChoosingTarget)
        });
        PublishLangxi(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    [ProgramAiChoice(SkillProgramEffectOp.ChooseHpBoundOtherAndDealPositiveRandomDamage)]
    private PromptChoice SelectAiShanheS5Langxi(PendingDecision decision, ProgramSkillFrame frame) => LangxiChoices(frame).First();
}
