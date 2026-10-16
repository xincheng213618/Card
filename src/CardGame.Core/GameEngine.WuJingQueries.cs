namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWuJingCompletedUseObserver(ProgramCardTriggerWindowFrame window)
    {
        var index = _resolutionStack.FindIndex(frame => ReferenceEquals(frame, window));
        return window.Continuation is ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard &&
            index >= 0 && index + 1 < _resolutionStack.Count &&
            _resolutionStack[index + 1] is ProgramSkillFrame { WuJing: { } receipt, WindowContext.CardUse: { } identity } root &&
            receipt.WindowId == window.Id && receipt.UseId == window.ParentFrameId &&
            receipt.ActionId == window.Action.ActionId && root.WindowContext.ParentFrameId == window.Id &&
            identity.ParentCardUseFrameId == window.ParentFrameId && identity.CardActionId == window.Action.ActionId &&
            (index + 2 < _resolutionStack.Count && WuJingFirstChild(root, _resolutionStack[index + 2]) ||
             index + 2 == _resolutionStack.Count &&
             receipt is { Stage: WuJingStage.NativeChildren, NativeCompleted: true, NativeOrigin: { } origin } &&
             origin.ProgramFrameId == root.Id && origin.IncomingUseId == window.ParentFrameId &&
             origin.IncomingActionId == window.Action.ActionId && LifecycleCardUse(origin.UseId) is null &&
             ProgramEventHistory<WuJingHejiReturnedEvent>().Count(e => e.ProgramFrameId == root.Id && e.UseId == origin.UseId) == 1 &&
             ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == origin.UseId) == 1);
    }

    private bool IsTurnSlashChoiceUseForbidden(int actorSeat, PromptChoice choice)
    {
        if (!HasWuJingLiubing || choice.Cards is not [var id])
            return IsTurnPhysicalUseForbidden(actorSeat, choice.Cards);
        TryReadConversionSource(choice.Parameters, out var conversion);
        var kind = choice.Parameters.TryGetValue("effective-kind", out var name) &&
            Enum.TryParse<CardKind>(name, out var parsed) ? parsed : CardKind.Slash;
        var card = GetAdvancedCard(id);
        var suit = WuJingLiubingUsedSuit(actorSeat, card, kind, [id],
            conversion is null ? [] : [conversion], CaptureUsedCardSuit(actorSeat, [id], card));
        return IsHandCategoryMaterialRestricted(choice.Cards) ||
            suit is { } value && IsTurnSuitUseForbidden(actorSeat, value);
    }
}
