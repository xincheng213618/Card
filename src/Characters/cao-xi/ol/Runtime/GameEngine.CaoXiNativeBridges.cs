namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Only actual native Draw producers call this. Pure rule/UI queries stay read-only.
    private int FreezeAndCaptureCaoXiNativeDrawBase(CharacterState player, bool replaced = false)
    {
        var units = FreezeCaoXiGangshuDrawUse(player);
        var count = replaced ? 0 : GetTurnDrawCount(player);
        CaptureCaoXiGangshuDrawUse(player, units);
        return count;
    }
    private void CompleteCaoXiNativeCounterspellBeforeUseCompleted(CardActionContext action, ProgramCardContinuation continuation)
    {
        if (!HasCaoXiGangshuCapability || continuation != ProgramCardContinuation.NullificationResponse ||
            ActiveNullificationWindow is not { NamedAmbushResponse: { } response } pending) return;
        if (response.ActionId != action.ActionId || action.EffectiveKind != CardKind.Nullification || action.Type != CardActionType.Response ||
            response.ResponderSeat != action.ActorSeat || !action.PhysicalCards.Any(c => c.CardId == response.CardId) ||
            _resolutionStack.LastOrDefault()?.Id != pending.Id)
            throw new InvalidOperationException("Gangshu requires the exact completed native counterspell before its nonbasic-use observer.");
        CompleteNamedAmbushCounterspell(pending);
    }

}
