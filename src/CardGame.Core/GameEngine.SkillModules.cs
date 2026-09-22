namespace CardGame.Core;

/// <summary>One command/AI entry point for module interactions, independent of skill names.</summary>
public sealed partial class GameEngine
{
    private CommandResult SubmitModulePromptAnswer(PromptChoice selected)
    {
        if (_pendingDecision?.SkillPrompt is null ||
            _resolutionStack.LastOrDefault() is not (PhaseSkillFrame or PindianFrame))
            return Reject(CommandErrorCode.InvalidPrompt, "There is no current module interaction.");
        return Accept(() =>
        {
            if (_resolutionStack[^1] is PindianFrame) ResolvePindianChoice(selected);
            else ResolvePhaseSkillChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private bool IsAiModulePending() =>
        _pendingDecision is { SkillPrompt: not null } decision && !_players[decision.PlayerSeat].IsHuman;

    private void ResolvePendingAiModule()
    {
        if (!IsAiModulePending()) throw new InvalidOperationException("There is no AI module interaction.");
        switch (_resolutionStack.LastOrDefault())
        {
            case PindianFrame: ResolvePendingAiPindian(); break;
            case PhaseSkillFrame phase:
                ResolvePhaseSkillChoice(phase.Plan.AiPrefersActivation ? phase.Plan.Activate : phase.Plan.Skip);
                PublishState();
                break;
            default: throw new InvalidOperationException("The module interaction lost its owning frame.");
        }
    }
}
