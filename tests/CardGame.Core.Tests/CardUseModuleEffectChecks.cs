using CardGame.Core;

internal static class CardUseModuleEffectChecks
{
    private const string SkillId = "fixture:card-use-effects";

    public static void TurnStateUsesActionSemanticsStableOrderAndExpiration()
    {
        var state = new TurnCardUseEffectStore();
        var firstSource = new CardUseEffectSource(
            SkillId,
            "program:play-starting",
            0,
            $"seat-0:{SkillId}");
        var secondSource = new CardUseEffectSource(
            "fixture:second-source",
            "program:play-starting",
            0,
            "seat-0:fixture:second-source");

        var firstAdjustment = state.GrantTargetAdjustment(
            3,
            0,
            101,
            1,
            firstSource,
            new GrantNextCardTargetAdjustment(
                CardUseCategories.Basic | CardUseCategories.InstantTrick));
        var duplicate = state.GrantTargetAdjustment(
            3,
            0,
            101,
            1,
            firstSource,
            new GrantNextCardTargetAdjustment(
                CardUseCategories.Basic | CardUseCategories.InstantTrick));
        var secondAdjustment = state.GrantTargetAdjustment(
            3,
            0,
            102,
            0,
            secondSource,
            new GrantNextCardTargetAdjustment(CardUseCategories.Basic));
        state.GrantProhibition(
            3,
            0,
            101,
            2,
            firstSource,
            new ForbidCardUseUntilTurnEnd(
                CardUseCategories.InstantTrick | CardUseCategories.Equipment));
        state.GrantProhibition(
            3,
            0,
            102,
            1,
            secondSource,
            new ForbidCardUseUntilTurnEnd(CardUseCategories.Basic));
        var damageModifier = state.GrantDamageModifier(
            3,
            0,
            104,
            0,
            firstSource,
            [CardKind.Duel, CardKind.Slash],
            1);
        var duplicateDamageModifier = state.GrantDamageModifier(
            3,
            0,
            104,
            0,
            firstSource,
            [CardKind.Slash, CardKind.Duel],
            1);

        Require(firstAdjustment == duplicate && state.TargetAdjustments.Count == 2,
            "The same frame/effect grant must be idempotent.");
        Require(damageModifier == duplicateDamageModifier && state.DamageModifiers.Count == 1 &&
                state.GetDamageModifiers(3, 0, 0, 0, CardKind.Slash, isChainPropagation: false)
                    .SequenceEqual([damageModifier]) &&
                state.GetDamageModifiers(3, 0, 1, 0, CardKind.Duel, isChainPropagation: false).Count == 0 &&
                state.GetDamageModifiers(3, 0, 0, 0, CardKind.FireSlash, isChainPropagation: false).Count == 0 &&
                state.GetDamageModifiers(3, 0, 0, 0, CardKind.Slash, isChainPropagation: true).Count == 0,
            "Card-damage modifiers must be idempotent and match only direct owner-used configured cards.");
        Require(firstAdjustment.GrantSequence < secondAdjustment.GrantSequence &&
                state.GetTargetAdjustments(3, 0, 0, CardKind.Slash)
                    .Select(item => item.GrantSequence)
                    .SequenceEqual([firstAdjustment.GrantSequence, secondAdjustment.GrantSequence]),
            "Matching target adjustments must retain stable grant order.");
        Require(state.IsCardUseForbidden(3, 0, 0, CardKind.FireAttack, CardActionType.Use) &&
                state.IsCardUseForbidden(3, 0, 0, CardKind.Nullification, CardActionType.Use) &&
                state.IsCardUseForbidden(3, 0, 0, CardKind.Crossbow, CardActionType.Use) &&
                state.IsCardUseForbidden(3, 0, 0, CardKind.Slash, CardActionType.Use),
            "Independent prohibitions must union their final effective categories.");
        Require(!state.IsCardUseForbidden(3, 0, 0, CardKind.Nullification, CardActionType.Response) &&
                !state.IsCardUseForbidden(3, 0, 1, CardKind.Nullification, CardActionType.Use),
            "A response and another actor must not inherit the owner's use prohibition.");
        Require(state.ConsumeTargetAdjustment(firstAdjustment.GrantSequence) &&
                !state.ConsumeTargetAdjustment(firstAdjustment.GrantSequence),
            "A target adjustment must be consumable exactly once.");

        var later = state.GrantProhibition(
            4,
            1,
            103,
            0,
            new CardUseEffectSource("fixture:later", "program:play-starting", 1, "seat-1:fixture:later"),
            new ForbidCardUseUntilTurnEnd(CardUseCategories.DelayedTrick));
        var expired = state.ExpireTurn(3, 0);
        Require(expired.SequenceEqual(expired.Order()) &&
                state.Prohibitions.Count == 1 && state.Prohibitions[0] == later &&
                !state.TargetAdjustments.Any() && !state.DamageModifiers.Any(),
            "True turn end must expire exactly that turn's grants without touching a later turn.");
        state.AssertInvariants();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
