namespace CardGame.Core;

/// <summary>
/// Detaches collections in built-in committed facts from the rule operation that
/// produced them. The journal and its observers can then share one immutable
/// payload without exposing writable arrays or live policy/deposit collections.
/// Scalar-only facts and CardActionContext are already immutable.
/// </summary>
internal static class CommittedEventProjection
{
    // Keep collection-bearing built-in facts here when adding a mechanism.
    // Rules see the original fact first; only the committed projection is copied.
    public static IGameEvent Freeze(IGameEvent payload) => payload switch
    {
        ShortRangeSlashTargetResolvedEvent value => value with { Receipt = value.Receipt is { } receipt
            ? receipt with { OriginalTargets = FreezeList(receipt.OriginalTargets) } : null },
        KuangfuHandDiscardPaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        CompletedCategoryCompoundTargetsIssuedEvent value => value with
            { OriginalTargets = FreezeList(value.OriginalTargets), Targets = FreezeList(value.Targets) },
        ActualOwnTurnHandGainsRecordedEvent value => value with { Gains = FreezeList(value.Gains) },
        ForeignTurnHandGainsRecordedEvent value => value with { Gains = FreezeList(value.Gains) },
        ForeignTurnHandGainsCleanupPaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        PublicPilePreparationPaidEvent value => value with { CardIds = FreezeList(value.CardIds), From = FreezeList(value.From) },
        SourceCurseLossRosterIssuedEvent value => value with { Losses = FreezeList(value.Losses) },
        DyingSuitsDiscardPaidEvent value => value with { CardIds = FreezeList(value.CardIds), Suits = FreezeList(value.Suits) },
        DynamicDiscardDamagePaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        SlashTargetPenaltyPaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        ShownEntityTurnPolicyGrantedEvent value => value with
        { Policy = value.Policy with { AffectedSeats = FreezeList(value.Policy.AffectedSeats), RestrictionSequences = FreezeList(value.Policy.RestrictionSequences) } },
        ShownEntityUseBenefitIssuedEvent value => value with
        { Policy = value.Policy with { AffectedSeats = FreezeList(value.Policy.AffectedSeats), RestrictionSequences = FreezeList(value.Policy.RestrictionSequences) } },
        ActualDiscardRecoveryClaimedEvent value => value with { CardIds = FreezeList(value.CardIds) },
        ProgramCurrentUsePhysicalCardsClaimedEvent value => value with
        { Claim = value.Claim with { CardIds = FreezeList(value.Claim.CardIds) } },
        CardDeclarationCommittedEvent value => value with { TargetSeats = FreezeList(value.TargetSeats) },
        PlayPhaseSuitAllowanceGrantedEvent value => value with { Suits = FreezeList(value.Suits), DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramDiscardBudgetCommittedEvent value => value with
        { Participants = FreezeList(value.Participants) },
        ProgramRedDiscardRecoveryCommittedEvent value => value with
        { Seats = FreezeList(value.Seats) },
        CompletedFactionCostGiftedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        ProgramViewAsConvertedEvent value => value with
        { PhysicalCardIds = FreezeList(value.PhysicalCardIds), TargetSeats = FreezeList(value.TargetSeats) },
        TurnCardUseEffectsExpiredEvent value => value with
        { GrantSequences = FreezeList(value.GrantSequences) },
        GeneralSelectionRequestedEvent value => value with
        { CandidateIds = FreezeList(value.CandidateIds) },
        GodFactionSelectionRequestedEvent value => value with
        { FactionIds = FreezeList(value.FactionIds) },
        SkillsAcquiredEvent value => value with
        { SkillIds = FreezeList(value.SkillIds) },
        SkillAwakenedEvent value => value with
        { AcquiredSkillIds = FreezeList(value.AcquiredSkillIds) },
        HandLimitDiscardedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        GroupCardUsedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        CardsRevealedEvent value => value with
        { Cards = FreezeList(value.Cards) },
        TargetsConfirmedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        LihuoSlashUsedEvent value => value with
        { PhysicalCardIds = FreezeList(value.PhysicalCardIds), TargetSeats = FreezeList(value.TargetSeats) },
        ProgramCardTargetCountAppliedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats), ContributionSourceIds = FreezeList(value.ContributionSourceIds) },
        JiefanStartedEvent value => value with
        { ResponderSeats = FreezeList(value.ResponderSeats) },
        JiefanChoiceResolvedEvent value => value with
        { DrawnCardIds = FreezeList(value.DrawnCardIds) },
        IronChainResolvedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        DamageTriggerWindowOpenedEvent value => value with
        { Candidates = FreezeList(value.Candidates) },
        MijiResolvedEvent value => value with
        { DrawnCardIds = FreezeList(value.DrawnCardIds), GivenCardIds = FreezeList(value.GivenCardIds), TargetSeats = FreezeList(value.TargetSeats) },
        LongyinResolvedEvent value => value with
        { DrawnCardIds = FreezeList(value.DrawnCardIds) },
        ChunlaoStoredEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        StoneAxeResolvedEvent value => value with
        { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ZhangbaSerpentSpearConvertedEvent value => value with
        { PhysicalCardIds = FreezeList(value.PhysicalCardIds) },
        QinglongCrescentBladeResolvedEvent value => value with
        { SlashCardIds = FreezeList(value.SlashCardIds) },
        IceSwordResolvedEvent value => value with
        { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramUniqueRankDyingResolvedEvent value => value with
        { WoundCardIds = FreezeList(value.WoundCardIds) },
        LuanjiConvertedEvent value => value with
        { PhysicalCardIds = FreezeList(value.PhysicalCardIds) },
        FangtianHalberdUsedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        ZhuqueFanConvertedEvent value => value with
        { PhysicalCardIds = FreezeList(value.PhysicalCardIds), TargetSeats = FreezeList(value.TargetSeats) },
        FactionDefenseRequestedEvent value => value with
        { CandidateSeats = FreezeList(value.CandidateSeats) },
        ZaiqiResolvedEvent value => value with
        { RevealedCardIds = FreezeList(value.RevealedCardIds), HeartCardIds = FreezeList(value.HeartCardIds), GainedCardIds = FreezeList(value.GainedCardIds) },
        JuxiangCardClaimedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        FactionSlashRequestedEvent value => value with
        { CandidateSeats = FreezeList(value.CandidateSeats) },
        DeckRankCardsObtainedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        SlashTargetsReplacedEvent value => value with
        { PreviousTargets = FreezeList(value.PreviousTargets), Targets = FreezeList(value.Targets) },
        DeferredPublicPileObtainedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        ProgramOwnerSkillsReplacedEvent value => value with
        { LostSkillIds = FreezeList(value.LostSkillIds) },
        ProgramHoldCardsPlacedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        PojunHoldReturnedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        ProgramGameFactionAttackRangeTargetsGrantedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        RedAdditionalTargetsConsumedEvent value => value with
        { Targets = FreezeList(value.Targets) },
        CompletedCardGiftedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        ProgramWeaponDamageChoiceEvent value => value with
        { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramHandComparisonResolvedEvent value => value with
        { RevealedIds = FreezeList(value.RevealedIds) },
        ProgramHandColorDiscardEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        ProgramAttackRangeAidStartedEvent value => value with
        { ResponderSeats = FreezeList(value.ResponderSeats) },
        ProgramAttackRangeAidChoiceResolvedEvent value => value with
        { DrawnCardIds = FreezeList(value.DrawnCardIds) },
        ProgramDamageCardsClaimedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        ProgramRandomHandCardsTakenEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        ProgramRandomCardsTakenFromCharactersEvent value => value with
        { SourceSeats = FreezeList(value.SourceSeats), Zones = FreezeList(value.Zones) },
        ProgramOwnedZoneCardsDiscardedEvent value => value with
        { Zones = FreezeList(value.Zones) },
        ProgramCardsRevealedEvent value => value with
        { Cards = FreezeList(value.Cards) },
        ProgramTurnSkillsGrantedEvent value => value with
        { GrantedSkillIds = FreezeList(value.GrantedSkillIds) },
        ProgramSelectedCardEffectsNullifiedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        ProgramCardSubsetSelectedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        CharacterSkillsLostEvent value => value with
        { SkillIds = FreezeList(value.SkillIds) },
        ProgramHandDraftRevealedEvent value => value with
        { RecipientSeats = FreezeList(value.RecipientSeats), CardIds = FreezeList(value.CardIds) },
        ProgramResponseEntityClaimedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        CardDamageModifierGrantedEvent value => value with
        { Modifier = value.Modifier with { CardKinds = FreezeList(value.Modifier.CardKinds) } },
        CardActionProhibitionGrantedEvent value => value with
        {
            Prohibition = value.Prohibition with
            {
                CardKinds = FreezeList(value.Prohibition.CardKinds),
                ActionTypes = FreezeList(value.Prohibition.ActionTypes),
                Suits = FreezeOptionalList(value.Prohibition.Suits)
            }
        },
        TurnRuleModifierGrantedEvent value => value with
        { Modifier = value.Modifier with { CardKinds = FreezeOptionalList(value.Modifier.CardKinds) } },
        TurnHandLimitCardKindExemptionGrantedEvent value => value with
        { Policy = value.Policy with { CardKinds = FreezeList(value.Policy.CardKinds) } },
        DirectedTurnCardPolicyGrantedEvent value => value with
        { Policy = value.Policy with { CardKinds = FreezeList(value.Policy.CardKinds) } },
        DeferredPublicPileDepositedEvent value => value with
        { Deposit = value.Deposit with { CardIds = FreezeList(value.Deposit.CardIds) } },
        _ => payload
    };

    private static IReadOnlyList<T> FreezeList<T>(IReadOnlyList<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<T>? FreezeOptionalList<T>(IReadOnlyList<T>? values) =>
        values is null ? null : FreezeList(values);
}
