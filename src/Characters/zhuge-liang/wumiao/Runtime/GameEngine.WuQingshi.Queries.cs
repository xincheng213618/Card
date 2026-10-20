using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanWuZglQingshi(int owner, ProgramSkillWindowContext c)
    {
        var action = WuZglAction(c);
        return c.Window == SkillProgramTriggerWindow.CardUseCommitted && _phase == TurnPhase.Play && owner == _currentSeat && WuZglTurnOpen(_turnNumber, _currentSeat) &&
            action is not null && action.ActorSeat == owner && (action.Type == CardActionType.Use || action.ProviderSeat == owner && action.RequesterSeat is null && IsProgramResponseCardUse(_players[owner], action.EffectiveKind)) &&
            !ProgramEventHistory<WuZhugeLiangQingshiDisabledEvent>().Any(e => e.OwnerSeat == owner && e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat) &&
            !ProgramEventHistory<WuZhugeLiangQingshiAcceptedEvent>().Any(e => e.Source.OwnerSeat == owner && e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat && e.Name == ProgramBasicCardName(action.EffectiveKind)) &&
            GetHand(_players[owner]).Any(card => !action.PhysicalCards.Any(m => m.CardId == card.Id) && ProgramBasicCardName(AdvancedEffectiveHandKind(_players[owner], card)) == ProgramBasicCardName(action.EffectiveKind));
    }
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers, 60)]
    private IEnumerable<(CardUseEffectSource Source, int Amount)> WuZglQingshiDamage(IDamageAttempt attack) => attack.IsChainPropagation ? [] :
        ProgramEventHistory<WuZhugeLiangQingshiDamageEvent>().Where(e => e.UseId == attack.ResolutionId && e.TargetSeat == attack.TargetSeat &&
            LifecycleCardUse(e.UseId)?.Action is { } a && a.ActionId == e.ActionId && a.ActorSeat == e.Source.OwnerSeat && attack.CardUserSeat == a.ActorSeat)
        .Select(e => (new CardUseEffectSource(e.Source.SkillId, e.Source.BindingId, e.Source.OwnerSeat, e.Source.SkillInstanceId), 1));
}
