namespace CardGame.Core;

internal interface IProgramSelectionAiState : IProgramSelectionQuery
{
    int MarkerCount(int ownerSeat, PlayerMarkerKind marker);
    IReadOnlyList<Card> OwnerHand(int ownerSeat);
    int EffectivePindianRank(int ownerSeat, Card card);
    SkillProgramCardCategory CardCategory(CardKind kind);
    bool HasNextActualUseAdjustment(int ownerSeat);
    bool HasPindianClaimPolicy(int ownerSeat);
}

internal interface IProgramSelectionRewardState : IProgramSelectionQuery
{
    IEnumerable<ProgramTargetCommitContext> Declarations { get; }
    IEnumerable<SkillProgramEffect> EligibleRewardEffects(int ownerSeat);
}

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 10, SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice)]
internal sealed class MatchingRecastSelectionAi : IProgramSelectionAiDecorator
{
    public SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint,
        IProgramSelectionQueries queries) => hint with { MatchingNameOrEquipmentRecastInput = true };
}

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 20, SkillProgramEffectOp.RecastOwnedCardsAndDuel)]
internal sealed class ArbitraryRecastSelectionAi : IProgramSelectionAiDecorator
{
    public SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint,
        IProgramSelectionQueries queries) => hint with { ArbitraryOwnedRecastInput = true };
}

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 30, SkillProgramEffectOp.StartSimultaneousPindian)]
internal sealed class SimultaneousPindianSelectionAi : IProgramSelectionAiDecorator
{
    public SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint,
        IProgramSelectionQueries queries) => hint with
        {
            PreferPindianInputOrder = true,
            ValueAdjustment = hint.ValueAdjustment -
                (queries.Get<IProgramSelectionAiState>().MarkerCount(context.OwnerSeat, PlayerMarkerKind.RaoShe) >= 6 ? 12d : 0d)
        };
}

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 40, SkillProgramEffectOp.Pindian)]
internal sealed class NextActualUsePindianSelectionAi : IProgramSelectionAiDecorator
{
    internal static bool HasStructure(SkillProgramActivation activation,
        Func<CardKind, SkillProgramCardCategory> category) =>
        activation.Effects is [
            { Op: SkillProgramEffectOp.Pindian },
            { Op: SkillProgramEffectOp.GrantNextActualUseTargetAdjustment, Condition.Kind: SkillProgramConditionKind.PindianWon } grant,
            { Op: SkillProgramEffectOp.SetBooleanState, BooleanValue: true, Condition.Kind: SkillProgramConditionKind.PindianNotWon } failed,
            { Op: SkillProgramEffectOp.GrantTurnCardActionProhibition, Condition.Kind: SkillProgramConditionKind.PindianNotWon } ban] &&
        failed.StateId == grant.StateId && ban.ActionTypes.SequenceEqual([CardActionType.Use]) &&
        ban.CardKinds.All(kind => category(kind) == SkillProgramCardCategory.Trick);

    public SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint,
        IProgramSelectionQueries queries)
    {
        var state = queries.Get<IProgramSelectionAiState>();
        if (!HasStructure(context.Activation, state.CardCategory)) return hint;
        var hand = state.OwnerHand(context.OwnerSeat);
        if (hand.Count == 0) return hint;
        // Only the actor's own card faces enter this estimate. Opponents
        // contribute public hand counts and a uniform rank prior, without RNG.
        var best = hand.Max(card => state.EffectivePindianRank(context.OwnerSeat, card));
        var participants = queries.Get<ISelectionParticipantQueries>();
        var otherCount = participants.Seats.Where(seat => participants.IsAlive(seat) && seat != context.OwnerSeat &&
                participants.HandCount(seat) > 0)
            .Select(participants.HandCount).DefaultIfEmpty(1).Min();
        var winPrior = Math.Pow(Math.Clamp((best - 1d) / 13d, 0d, 1d), Math.Max(1, otherCount));
        var valueOfGrant = state.HasNextActualUseAdjustment(context.OwnerSeat) ? 0d : 6d;
        var ownTricks = hand.Count(card => state.CardCategory(card.Kind) == SkillProgramCardCategory.Trick);
        var cardCost = state.HasPindianClaimPolicy(context.OwnerSeat) ? 0d : 4d;
        return hint with
        {
            ValueAdjustment = 2d + winPrior * valueOfGrant - (1d - winPrior) * ownTricks * 1.5d - cardCost,
            TargetValueAdjustment = -2d,
            PreferPindianInputOrder = true
        };
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 50, SkillProgramEffectOp.SelectRelativeZoneDemandTarget)]
internal sealed class FirstTargetRewardSelectionAi : IProgramSelectionAiDecorator
{
    public SkillProgramAiHint Decorate(ProgramSelectionContext context, SkillProgramAiHint hint,
        IProgramSelectionQueries queries)
    {
        var participants = queries.Get<ISelectionParticipantQueries>();
        var hand = participants.HandCount(context.OwnerSeat);
        var equipment = participants.EquipmentCount(context.OwnerSeat);
        var possible = participants.Seats.Where(seat => participants.IsAlive(seat) && seat != context.OwnerSeat &&
            (participants.HandCount(seat) > hand - (hand > 0 ? 1 : 0) ||
             participants.EquipmentCount(seat) > equipment - (equipment > 0 ? 1 : 0))).ToArray();
        if (possible.Length == 0) return hint with { ValueAdjustment = hint.ValueAdjustment - 20d };
        var rewards = queries.Get<IProgramSelectionRewardState>();
        var prior = rewards.Declarations.Where(declaration => declaration.OwnerSeat == context.OwnerSeat &&
                declaration.SourceProgramId == context.SkillId && declaration.SourceActivationId == context.Activation.Id)
            .Select(declaration => declaration.TargetSeat).ToHashSet();
        if (possible.All(prior.Contains)) return hint;
        var reward = rewards.EligibleRewardEffects(context.OwnerSeat)
            .Where(effect => effect.Op == SkillProgramEffectOp.DrawOnFirstProgramTargetEncounter &&
                effect.SkillIds.Single() == context.SkillId && effect.StateId == context.Activation.Id)
            .Sum(effect => effect.Amount);
        return hint with { ValueAdjustment = hint.ValueAdjustment + reward * 8d };
    }
}
