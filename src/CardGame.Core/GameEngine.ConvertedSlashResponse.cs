namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 界关兴张苞 父魂: the target of a converted Slash may only respond with hand
    // cards of the same color as the converted physical cards. Mixed-color pairs
    // impose no single-color requirement.
    private bool? ConvertedSlashSameColorResponseColor(CardAttackHandle attack)
    {
        if (attack.ProgramSkillCardUseFrameId is not { } frameId ||
            _resolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(frame => frame.Id == frameId) is not { } frame)
            return null;
        var source = _players[attack.CardUserSeat];
        if (!CardPolicies(source, SkillProgramCardPolicyKind.ConvertedSlashSameColorResponseOnly,
                attack.EffectiveCardKind).Any(item => item.Source.SkillId == frame.SkillId))
            return null;
        if (attack.PhysicalCards.Count == 0) return null;
        var reds = attack.PhysicalCards.Count(card => IsRedSuit(EffectiveSuit(source, card)));
        if (reds == attack.PhysicalCards.Count) return true;
        return reds == 0 ? false : null;
    }
}
