namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasYuanchouArmorBypass(long useId, int targetSeat)
    {
        if (!IsValidPlayerSeat(targetSeat) || LifecycleCardUse(useId) is not { Action: { Type: CardActionType.Use, EffectiveIsRed: false } action } use ||
            !IsSlashCard(use.CardKind) || !IsSlashCard(action.EffectiveKind) || ActualTargetPolicyColor(action) != false || !use.TargetSeats.Contains(targetSeat) || action.ActorSeat != use.SourceSeat || action.ActorSeat == targetSeat) return false;
        return HasCardPolicy(_players[action.ActorSeat], SkillProgramCardPolicyKind.BlackSlashIgnoresArmorBothDirections) ||
            HasCardPolicy(_players[targetSeat], SkillProgramCardPolicyKind.BlackSlashIgnoresArmorBothDirections);
    }
}
