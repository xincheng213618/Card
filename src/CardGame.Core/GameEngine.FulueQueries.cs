namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasFulueCompletedCounterChild(ProgramCardTriggerWindowFrame window)
    {
        var index = _resolutionStack.FindIndex(frame => ReferenceEquals(frame, window));
        return window.Continuation is ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard &&
            index >= 0 && index + 2 < _resolutionStack.Count &&
            _resolutionStack[index + 1] is ProgramSkillFrame { Fulue: { } receipt, WindowContext.CardUse: { } identity } root &&
            HasIssuedFulueRequest(root) && root.WindowContext.ParentFrameId == window.Id &&
            receipt.UseId == window.ParentFrameId && receipt.ActionId == window.Action.ActionId &&
            identity.ParentCardUseFrameId == window.ParentFrameId && identity.CardActionId == window.Action.ActionId &&
            _resolutionStack[index + 2] is CardUseFrame { FulueCounter: { } invoice } counter &&
            invoice.ProgramFrameId == root.Id && ValidFulueCounterInvoice(counter, invoice);
    }
}
