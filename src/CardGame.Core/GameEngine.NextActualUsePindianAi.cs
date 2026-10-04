namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasNextActualUsePindianStructure(SkillProgramActivation activation) =>
        activation.Effects is [
            { Op: SkillProgramEffectOp.Pindian },
            { Op: SkillProgramEffectOp.GrantNextActualUseTargetAdjustment, Condition.Kind: SkillProgramConditionKind.PindianWon } grant,
            { Op: SkillProgramEffectOp.SetBooleanState, BooleanValue: true, Condition.Kind: SkillProgramConditionKind.PindianNotWon } failed,
            { Op: SkillProgramEffectOp.GrantTurnCardActionProhibition, Condition.Kind: SkillProgramConditionKind.PindianNotWon } ban] &&
        failed.StateId == grant.StateId && ban.ActionTypes.SequenceEqual([CardActionType.Use]) &&
        ban.CardKinds.All(k => GetProgramCardCategory(k) == SkillProgramCardCategory.Trick);

    private int[] OrderNextActualUsePindianCards(CharacterState owner, SkillProgramActivation activation, int[] cards) =>
        HasNextActualUsePindianStructure(activation)
            ? cards.OrderByDescending(id => EffectivePindianRank(owner, GetHand(owner).Single(c => c.Id == id)))
                .ThenBy(id => CardCatalog.Get(GetHand(owner).Single(c => c.Id == id).Kind).HandKeepValue).ThenBy(id => id).ToArray()
            : cards;

    private SkillProgramAiHint EstimateNextActualUsePindian(CharacterState owner, SkillProgramActivation activation, SkillProgramAiHint fallback)
    {
        if (!HasNextActualUsePindianStructure(activation) || GetHand(owner).Count == 0) return fallback;
        // Only the actor's own cards are read. The opponent is represented by
        // public hand counts and a uniform rank prior, without RNG consumption.
        var best = GetHand(owner).Max(c => EffectivePindianRank(owner, c));
        var otherCount = _players.Where(p => p.IsAlive && p.Seat != owner.Seat && GetHand(p).Count > 0)
            .Select(p => GetHand(p).Count).DefaultIfEmpty(1).Min();
        var winPrior = Math.Pow(Math.Clamp((best - 1d) / 13d, 0d, 1d), Math.Max(1, otherCount));
        var valueOfGrant = CurrentNextActualUseAdjustment(owner.Seat) is null ? 6d : 0d;
        var ownTricks = GetHand(owner).Count(c => GetProgramCardCategory(c.Kind) == SkillProgramCardCategory.Trick);
        var returnedCard = HasCardPolicy(owner, SkillProgramCardPolicyKind.PindianClaim);
        var cardCost = returnedCard ? 0d : 4d;
        return fallback with { ValueAdjustment = 2d + winPrior * valueOfGrant - (1d - winPrior) * ownTricks * 1.5d - cardCost,
            TargetValueAdjustment = -2d, PreferPindianInputOrder = true };
    }
}
