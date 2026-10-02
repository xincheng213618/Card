namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsUnconvertedActualCardUse(CardActionContext action,
        SkillProgramTriggerWindow window, ProgramCardContinuation? continuation)
    {
        if (action.ConversionChain.Count != 0) return false;
        if (window == SkillProgramTriggerWindow.CardUseTargetsFinalized)
            return action.Type == CardActionType.Use &&
                _resolutionStack.LastOrDefault() is CardUseFrame use &&
                use.Action?.ActionId == action.ActionId && use.SourceSeat == action.ActorSeat;
        if (window != SkillProgramTriggerWindow.CardResponseAccepted ||
            continuation != ProgramCardContinuation.NullificationResponse ||
            !IsCompletedResponseUse(action, ProgramCardContinuation.NullificationResponse) ||
            _resolutionStack.LastOrDefault() is not NullificationWindowFrame pending ||
            ActiveNullificationWindow?.Id != pending.Id || action.OpponentSeat != pending.SourceSeat)
            return false;
        var parent = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == pending.ParentFrameId);
        return parent?.Action?.ActionId == action.ParentActionId;
    }
}
