using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WuHfVictimAvailable(int owner, int target) => _players[target].IsAlive && GetHand(_players[target]).Count > 0 &&
        !ProgramEventHistory<WuHuangFuSongBottomPaidEvent>().Any(e => e.OwnerSeat == owner && e.TargetSeat == target && e.Turn == WuHfTurn());
    private CardUseFrame? WuHfLianjieUse(int owner, ProgramSkillWindowContext c)
    {
        if (c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || c.CardUse is not { } identity || identity.ActorSeat != owner ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { WuHuangFuSongUse: { HandUse: true, EffectiveRank: { } rank } r, Action: { } a } use ||
            use.Id != r.UseId || a.ActionId != identity.CardActionId || a.ActorSeat != owner || a.ProviderSeat != owner || !WuHfTurnOpen(r.Turn) ||
            a.EffectiveDesignatedTargetSeats.Count == 0 || !GetHand(_players[owner]).Where(card => !a.PhysicalCards.Any(cost => cost.CardId == card.Id)).All(card => rank <= EffectiveOwnedCardRank(_players[owner], card))) return null;
        return _players.Any(p => WuHfVictimAvailable(owner, p.Seat)) ? use : null;
    }
}
