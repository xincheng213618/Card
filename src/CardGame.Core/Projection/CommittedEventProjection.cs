namespace CardGame.Core;

/// <summary>
/// Detaches collections in built-in committed facts from the rule operation that
/// produced them. The journal and its observers can then share one immutable
/// payload without exposing writable arrays or live policy/deposit collections.
/// Scalar-only facts and CardActionContext are already immutable.
/// </summary>
internal static class CommittedEventProjection
{
    // The invoice can also be embedded in trusted movement/payment frames. Its
    // action constructor already detaches every nested list; copy it explicitly
    // here so callers need not depend on the producer's object identity.
    internal static LiJueUseDiscardInvoice FreezeLiJueUseDiscard(LiJueUseDiscardInvoice value)
    {
        var a = value.Action;
        return value with
        {
            FinishedMovements = FreezeList(value.FinishedMovements),
            Action = new CardActionContext(a.ActionId, a.ParentActionId, a.Type, a.ActorSeat, a.ProviderSeat,
                a.RequesterSeat, a.ResponderSeat, a.OpponentSeat, a.EffectiveKind, a.TargetSeats,
                a.PhysicalCards, a.ConversionChain, a.DesignatedTargetSeats,
                a.EffectiveSuit, a.EffectiveRank, a.EffectiveIsRed, a.FactionOrigin)
        };
    }

    private static RoundGainedUseQualification FreezeRoundQualification(RoundGainedUseQualification value) =>
        value with { MaterialGains = FreezeList(value.MaterialGains) };
    private static RandomAreaAcquisitionStepReceipt FreezeRandomAreaStep(RandomAreaAcquisitionStepReceipt value) =>
        value with { EligibleZones = FreezeList(value.EligibleZones), Materials = FreezeList(value.Materials),
            Movements = FreezeList(value.Movements) };
    private static ProgramRandomAreaAcquisitionReceipt FreezeRandomAreaReceipt(ProgramRandomAreaAcquisitionReceipt value) =>
        value with { OtherSeats = FreezeList(value.OtherSeats),
            Steps = Array.AsReadOnly(value.Steps.Select(FreezeRandomAreaStep).ToArray()) };
    private static GameStartingNativeMovementOrigin FreezeGameStartingMovement(GameStartingNativeMovementOrigin value) =>
        value with { Materials = FreezeList(value.Materials) };
    private static RicePileReceipt FreezeRiceReceipt(RicePileReceipt value) => value with
    { Selected = FreezeList(value.Selected), CardIds = FreezeList(value.CardIds), From = FreezeList(value.From),
        Choices = Array.AsReadOnly(value.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) };

    private static JianjiNativeReceipt FreezeJianjiReceipt(JianjiNativeReceipt receipt) => receipt with
    {
        Action = receipt.Action with
        {
            TargetSeats = FreezeList(receipt.Action.TargetSeats),
            AdditionalConversionSources = receipt.Action.AdditionalConversionSources is { } sources ? FreezeList(sources) : null,
            SelectableCardIds = FreezeList(receipt.Action.SelectableCardIds),
            SelectableTargetSeats = FreezeList(receipt.Action.SelectableTargetSeats),
            SelectableCardTargets = receipt.Action.SelectableCardTargets is { } pairs ? FreezeList(pairs) : null,
            SelectableTargetSequences = receipt.Action.SelectableTargetSequences is { } sequences
                ? Array.AsReadOnly(sequences.Select(sequence => (IReadOnlyList<int>)FreezeList(sequence)).ToArray()) : null
        }
    };

    private static NativeDrawOutcome FreezeNativeDrawOutcome(NativeDrawOutcome value) => value with
    { ActualDrawIds = FreezeList(value.ActualDrawIds), Replacement = value.Replacement is { } replacement
        ? replacement with { GainedIds = FreezeList(replacement.GainedIds) } : null };

    private static MoZhangFeiReceipt FreezeMoZhangFeiReceipt(MoZhangFeiReceipt value) => value with
    { GainedIds = FreezeList(value.GainedIds), DrawOutcome = value.DrawOutcome is { } outcome ? FreezeNativeDrawOutcome(outcome) : null };

    // Keep collection-bearing built-in facts here when adding a mechanism.
    // Rules see the original fact first; only the committed projection is copied.
    public static IGameEvent Freeze(IGameEvent payload) => payload switch
    {
        MouSmTargetsDisclosedEvent value => value with { Targets = FreezeList(value.Targets), HeldEventSequences = FreezeList(value.HeldEventSequences), HeldLogSequences = FreezeList(value.HeldLogSequences) },
        WushuangTairanDrawnEvent value => value with { CardIds = FreezeList(value.CardIds) },
        WushuangChoufaRewrittenEvent value => value with { CardIds = FreezeList(value.CardIds) },
        WushuangJinzhiPaidEvent value => value with { Action = new CardActionContext(value.Action.ActionId, value.Action.ParentActionId,
            value.Action.Type, value.Action.ActorSeat, value.Action.ProviderSeat, value.Action.RequesterSeat,
            value.Action.ResponderSeat, value.Action.OpponentSeat, value.Action.EffectiveKind, value.Action.TargetSeats,
            value.Action.PhysicalCards, value.Action.ConversionChain, value.Action.DesignatedTargetSeats,
            value.Action.EffectiveSuit, value.Action.EffectiveRank, value.Action.EffectiveIsRed, value.Action.FactionOrigin) },
        NativeMaterialSlashExtraTargetsIssuedEvent value => value with
        {
            Origin = value.Origin with
            {
                PhysicalCardIds = FreezeList(value.Origin.PhysicalCardIds),
                TargetSeats = FreezeList(value.Origin.TargetSeats),
                Claims = Array.AsReadOnly(value.Origin.Claims.Select(claim => claim with
                    { TargetSeats = FreezeList(claim.TargetSeats) }).ToArray())
            }
        },
        MoZhangFeiColorArrivedEvent value => value with
        { Change = value.Change with { FrozenLossCandidates = FreezeOptionalList(value.Change.FrozenLossCandidates) } },
        MoZhangFeiStartedEvent value => value with { Receipt = FreezeMoZhangFeiReceipt(value.Receipt) },
        MoZhangFeiDrawReturnedEvent value => value with { Outcome = FreezeNativeDrawOutcome(value.Outcome) },
        RandomAreaAcquisitionStartedEvent value => value with { Receipt = FreezeRandomAreaReceipt(value.Receipt) },
        RandomAreaAcquisitionRegionPaidEvent value => value with { Step = FreezeRandomAreaStep(value.Step) },
        SequentialDeckKindGrowthStepPaidEvent value => value with
        { Step = value.Step with { Movements = FreezeList(value.Step.Movements) } },
        ProgramPindianHandAcquiredEvent value => value with { Receipt = value.Receipt with { Materials = FreezeList(value.Receipt.Materials) } },
        RecoveryAmountBonusAppliedEvent value => value with { Receipt = value.Receipt with { Sources = FreezeList(value.Receipt.Sources) } },
        DamageTargetRegionObtainPaidEvent value => value with
        {
            Receipt = value.Receipt with { Materials = value.Receipt.Materials.ToArray(), Movements = value.Receipt.Movements.ToArray() }
        },
        WuZishengMovementQualifiedEvent value => value with
        { Qualification = value.Qualification with { Materials = FreezeList(value.Qualification.Materials) } },
        WuHaoxianShuffledEvent value => value with
        { BeforeOrder = FreezeList(value.BeforeOrder), AfterOrder = FreezeList(value.AfterOrder) },
        GameStartingNativeMovementRecordedEvent value => value with { Origin = FreezeGameStartingMovement(value.Origin) },
        NativeDrawInvocationRecordedEvent { GameStartingMovementOrigin: { } origin } value => value with
            { GameStartingMovementOrigin = origin with { MovementRoot = FreezeGameStartingMovement(origin.MovementRoot) } },
        ShenwuSharedGodCandidatesEvent value => value with { GeneralIds = FreezeList(value.GeneralIds) },
        ShenwuExpandedRandomEvent value => value with { Damage = FreezeList(value.Damage) },
        NianFormCycleStartedEvent value => value with { GeneralIds = FreezeList(value.GeneralIds) },
        // CardActionContext detaches all nested collections at construction;
        // the other embedded native payment records contain scalars only.
        QigongNativeIssuedEvent value => value with { Receipt = value.Receipt with { } },
        QigongNativePaidEvent value => value with { Receipt = value.Receipt with { } },
        NeifaTargetsResolvedEvent value => value with { OriginalTargets = FreezeList(value.OriginalTargets), ResultTargets = FreezeList(value.ResultTargets) },
        JianjiUseIssuedEvent value => value with { Receipt = FreezeJianjiReceipt(value.Receipt) },
        JianjiUseReturnedEvent value => value with { Receipt = FreezeJianjiReceipt(value.Receipt) },
        HuiyunNativeIssuedEvent value => value with { Invoice = value.Invoice with { Plan = HuiyunFreeze.Action(value.Invoice.Plan) } },
        HuiyunNativeReturnedEvent value => value with { Invoice = value.Invoice with { Plan = HuiyunFreeze.Action(value.Invoice.Plan) } },
        SuFeiActualUseRecordedEvent value => value with { Use = value.Use with { PrimaryTargets = FreezeList(value.Use.PrimaryTargets) } },
        SimaHuiChoiceStartedEvent value => value with { Candidates = FreezeList(value.Candidates) },
        SimaHuiTopViewedEvent value => value with { CardIds = FreezeList(value.CardIds) },
        SimaHuiUnassignedReturnedEvent value => value with { CardIds = FreezeList(value.CardIds) },
        SimaHuiYeyanSealsPaidEvent value => value with { GrantSourceIds = FreezeList(value.GrantSourceIds), MaterialCardIds = FreezeList(value.MaterialCardIds) },
        XuRongCardsPaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        XuRongCausalClaimIssuedEvent value => value with { Receipt = value.Receipt with { CardIds = FreezeList(value.Receipt.CardIds) } },
        BeiMiHuChoiceStartedEvent value => value with { Candidates = FreezeList(value.Candidates) },
        BeiMiHuAwakeningCompletedEvent value => value with { MarkedSeats = FreezeList(value.MarkedSeats) },
        BeiMiHuTargetsPaidEvent value => value with { Receipt = value.Receipt with
            { BeforeTargets = FreezeList(value.Receipt.BeforeTargets), AfterTargets = FreezeList(value.Receipt.AfterTargets), PaidMarkerSeats = FreezeList(value.Receipt.PaidMarkerSeats) } },
        WangYunNativeUseIssuedEvent value => value with { Origin = value.Origin with { Materials = FreezeList(value.Origin.Materials), Targets = FreezeList(value.Origin.Targets) } },
        WangYunNativePaymentCompletedEvent value => value with { Origin = value.Origin with { Materials = FreezeList(value.Origin.Materials), Targets = FreezeList(value.Origin.Targets) } },
        FrozenHandRosterStartedEvent value => value with { Seats = FreezeList(value.Seats) },
        BuZhiMovementPaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        OldHandDispositionPaymentEvent value => value with { CardIds = FreezeList(value.CardIds) },
        ProgramBao2025StartedEvent value => value with { Receipt = value.Receipt with { Choices = FreezeList(value.Receipt.Choices) } },
        ProgramBao2025MovementIssuedEvent value => value with { Receipt = value.Receipt with { Choices = FreezeList(value.Receipt.Choices) } },
        ProgramLimitedSkillInstancesResetEvent value => value with { Entries = FreezeList(value.Entries) },
        ChosenAssaultUseIssuedEvent value => value with { SelectedTargets = FreezeList(value.SelectedTargets) },
        DyingRecipientSkillChoiceEvent value => value with { LostGrantIds = FreezeList(value.LostGrantIds) },
        SimultaneousPindianStartedEvent value => value with { Opponents = FreezeList(value.Opponents) },
        PindianCardsRevealedEvent value => value with { Cards = FreezeList(value.Cards) },
        SimultaneousPindianClaimsIssuedEvent value => value with { Claims = value.Claims with { Claims = Array.AsReadOnly(value.Claims.Claims.Select(c => c with { CardIds = FreezeList(c.CardIds) }).ToArray()) } },
        SimultaneousPindianClaimPaidEvent value => value with { CardIds = FreezeList(value.CardIds) },
        DeathGrowthDrawIssuedEvent value => value with { Receipt = value.Receipt with { SkillIds = FreezeList(value.Receipt.SkillIds) } },
        DamageZoneAcquisitionStartedEvent value => value with { Receipt = value.Receipt with {
            Cards = FreezeList(value.Receipt.Cards), From = FreezeList(value.Receipt.From),
            Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        DamageZoneAcquisitionPaidEvent value => value with { Receipt = value.Receipt with {
            Cards = FreezeList(value.Receipt.Cards), From = FreezeList(value.Receipt.From),
            Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        AdjacentRedTargetAddedEvent value => value with { OriginalTargets = FreezeList(value.OriginalTargets),
            AddedTargets = FreezeList(value.AddedTargets), ResultTargets = FreezeList(value.ResultTargets) },
        DamageTargetBudgetOfferedEvent value => value with { Receipt = value.Receipt with {
            OriginalTargets = FreezeList(value.Receipt.OriginalTargets), AddedTargets = FreezeList(value.Receipt.AddedTargets),
            Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        DamageTargetBudgetResolvedEvent value => value with { Receipt = value.Receipt with {
            OriginalTargets = FreezeList(value.Receipt.OriginalTargets), AddedTargets = FreezeList(value.Receipt.AddedTargets),
            Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) }, ResultTargets = FreezeList(value.ResultTargets) },
        ExtraEffectsAndRankGuessOfferedEvent value => value with { Receipt = value.Receipt with {
            RevealedCardIds = FreezeList(value.Receipt.RevealedCardIds), Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        ExtraEffectsAndRankGuessPaidEvent value => value with { Receipt = value.Receipt with {
            RevealedCardIds = FreezeList(value.Receipt.RevealedCardIds), Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        RicePileStartedEvent value => value with { Receipt = FreezeRiceReceipt(value.Receipt) },
        RicePilePaymentIssuedEvent value => value with { Receipt = FreezeRiceReceipt(value.Receipt) },
        IncomingDistanceCourtesyOfferedEvent value => value with { Receipt = value.Receipt with
            { Benefits = FreezeList(value.Receipt.Benefits), Movements = FreezeList(value.Receipt.Movements),
                Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        IncomingDistanceCourtesySelectedEvent value => value with { Receipt = value.Receipt with
            { Benefits = FreezeList(value.Receipt.Benefits), Movements = FreezeList(value.Receipt.Movements),
                Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        NamedAmbushOfferedEvent value => value with { Receipt = value.Receipt with
            { Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        DamageSourceHandChoiceOfferedEvent value => value with { Receipt = value.Receipt with
            { SelectedCards = FreezeList(value.Receipt.SelectedCards), Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        DamageSourceHandChoicePaidEvent value => value with { Receipt = value.Receipt with
            { SelectedCards = FreezeList(value.Receipt.SelectedCards), Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        EndRefillNextTurnOfferedEvent value => value with { Receipt = value.Receipt with
            { Targets = FreezeList(value.Receipt.Targets), Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        EndRefillNextTurnIssuedEvent value => value with { Receipt = value.Receipt with
            { Targets = FreezeList(value.Receipt.Targets), Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        SoleTargetProtectionPaidEvent value => value with { Receipt = value.Receipt with
            { OriginalTargets = FreezeList(value.Receipt.OriginalTargets), Materials = FreezeList(value.Receipt.Materials) } },
        SoleTargetReplacedEvent value => value with { BeforeTargets = FreezeList(value.BeforeTargets), AfterTargets = FreezeList(value.AfterTargets) },
        SoleTargetAfterProtectionRedirectedEvent value => value with { BeforeTargets = FreezeList(value.BeforeTargets), AfterTargets = FreezeList(value.AfterTargets) },
        CurrentEnhancementSlashRedirectedEvent value => value with { BeforeTargets = FreezeList(value.BeforeTargets), AfterTargets = FreezeList(value.AfterTargets) },
        SoleTargetQuarantineIssuedEvent value => value with { Receipt = value.Receipt with
            { TargetSeats = FreezeList(value.Receipt.TargetSeats), Materials = FreezeList(value.Receipt.Materials) } },
        SoleTargetQuarantineSettledEvent value => value with { CardIds = FreezeList(value.CardIds) },
        TargetedOrdinaryTrickOfferedEvent value => value with
        { Receipt = value.Receipt with { OriginalTargets = FreezeList(value.Receipt.OriginalTargets),
            Choices = Array.AsReadOnly(value.Receipt.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) } },
        TargetedOrdinaryTrickResolvedEvent value => value with
        { BeforeTargets = FreezeList(value.BeforeTargets), AddedTargets = FreezeList(value.AddedTargets), ResultTargets = FreezeList(value.ResultTargets) },
        FriendPredictionRevealedEvent value => value with { Predictions = FreezeList(value.Predictions) },
        OverflowUseTargetsCanceledEvent value => value with
        {
            Receipt = value.Receipt with
            {
                OriginalTargetSeats = FreezeList(value.Receipt.OriginalTargetSeats),
                BeforeTargetSeats = FreezeList(value.Receipt.BeforeTargetSeats),
                CanceledPrimaryTargetSeats = FreezeList(value.Receipt.CanceledPrimaryTargetSeats),
                ResultTargetSeats = FreezeList(value.Receipt.ResultTargetSeats),
                MaterialCardIds = FreezeList(value.Receipt.MaterialCardIds)
            }
        },
        RecipientCategorySlashTargetsResolvedEvent value => value with
        { BeforeTargets = FreezeList(value.BeforeTargets), AddedTargets = FreezeList(value.AddedTargets), ResultTargets = FreezeList(value.ResultTargets) },
        DrawAdviceDiscardIssuedEvent value => value with { CardIds = FreezeList(value.CardIds) },
        ProgramYuanziDamageDrawEvent value => value with { DrawnCardIds = FreezeList(value.DrawnCardIds) },
        ProgramLiejieSourceDiscardEvent value => value with { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramTongxieArmedEvent value => value with { MemberSeats = FreezeList(value.MemberSeats) },
        ProgramTongxieFollowUpResolvedEvent value => value with
        { UsedBy = FreezeList(value.UsedBy), DeclinedBy = FreezeList(value.DeclinedBy), UsedCardIds = FreezeList(value.UsedCardIds) },
        DesignatedExtraTargetOfferedEvent value => value with
        { OriginalTargetSeats = FreezeList(value.OriginalTargetSeats), CandidateTargetSeats = FreezeList(value.CandidateTargetSeats) },
        RoundGainedUseQualifiedEvent value => value with
        { Qualification = FreezeRoundQualification(value.Qualification) },
        RoundGainedTrickTargetOfferedEvent value => value with
        { Qualification = FreezeRoundQualification(value.Qualification), OriginalTargetSeats = FreezeList(value.OriginalTargetSeats),
            Choices = Array.AsReadOnly(value.Choices.Select(PromptChoiceProjection.Freeze).ToArray()) },
        RoundGainedTrickTargetResolvedEvent value => value with
        { Qualification = FreezeRoundQualification(value.Qualification), OriginalTargetSeats = FreezeList(value.OriginalTargetSeats),
            AddedTargetSeats = FreezeList(value.AddedTargetSeats), RemovedTargetSeats = FreezeList(value.RemovedTargetSeats),
            ResultTargetSeats = FreezeList(value.ResultTargetSeats) },
        RoundGainedEquipmentDrawIssuedEvent value => value with
        { Qualification = FreezeRoundQualification(value.Qualification), DrawnCardIds = FreezeList(value.DrawnCardIds) },
        RoundGainedEquipmentDrawResolvedEvent value => value with
        { Qualification = FreezeRoundQualification(value.Qualification) },
        UniqueLeaderTrickTargetOfferedEvent value => value with
        { OriginalTargetSeats = FreezeList(value.OriginalTargetSeats), CandidateTargetSeats = FreezeList(value.CandidateTargetSeats) },
        UniqueLeaderTrickTargetResolvedEvent value => value with
        { OriginalTargetSeats = FreezeList(value.OriginalTargetSeats), AddedTargetSeats = FreezeList(value.AddedTargetSeats),
            ResultTargetSeats = FreezeList(value.ResultTargetSeats) },
        DesignatedExtraTargetResolvedEvent value => value with
        { OriginalTargetSeats = FreezeList(value.OriginalTargetSeats), AddedTargetSeats = FreezeList(value.AddedTargetSeats),
            ResultTargetSeats = FreezeList(value.ResultTargetSeats) },
        ProgramJiezhenConvertedEvent value => value with { ReplacedSkillIds = FreezeList(value.ReplacedSkillIds) },
        ProgramJiezhenRestoredEvent value => value with { RestoredSkillIds = FreezeList(value.RestoredSkillIds) },
        ProgramDaoshuEvent value => value with { RevealedCardIds = FreezeList(value.RevealedCardIds) },
        ProgramChangjiEndingEvent value => value with { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramZhuihuanResolvedEvent value => value with
        {
            ArmFrameIds = FreezeList(value.ArmFrameIds),
            DamagedSeats = FreezeList(value.DamagedSeats),
            DiscardedSeats = FreezeList(value.DiscardedSeats)
        },
        ProgramZhanyiCategoryChosenEvent value => value with { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramLuochongResolvedEvent value => value with { DiscardedCardIds = FreezeList(value.DiscardedCardIds) },
        ProgramBijingPunishEvent value => value with
        {
            LostCardIds = FreezeList(value.LostCardIds),
            DiscardedCardIds = FreezeList(value.DiscardedCardIds)
        },
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
        ProgramRandomDamageTargetSkillAcquiredEvent value => value with
        { Receipt = value.Receipt with { Candidates = FreezeList(value.Receipt.Candidates) } },
        ProgramAlcoholSlashSkillsReplacedEvent value => value with
        {
            Receipt = value.Receipt with
            {
                LostSkillIds = FreezeList(value.Receipt.LostSkillIds),
                RemovedGrants = FreezeList(value.Receipt.RemovedGrants),
                RevokedGrants = FreezeList(value.Receipt.RevokedGrants)
            }
        },
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
        ProgramPlayPhaseSkillsGrantedEvent value => value with { Grants = FreezeList(value.Grants) },
        ProgramTurnSkillsGrantedEvent value => value with
        { GrantedSkillIds = FreezeList(value.GrantedSkillIds) },
        ProgramSelectedCardEffectsNullifiedEvent value => value with
        { TargetSeats = FreezeList(value.TargetSeats) },
        ProgramCardSubsetSelectedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        CharacterSkillsLostEvent value => value with
        { SkillIds = FreezeList(value.SkillIds) },
        ProgramRandomPhaseSkillsOfferedEvent value => value with
        { SkillIds = FreezeList(value.SkillIds) },
        ProgramHandDraftRevealedEvent value => value with
        { RecipientSeats = FreezeList(value.RecipientSeats), CardIds = FreezeList(value.CardIds) },
        ProgramResponseEntityClaimedEvent value => value with
        { CardIds = FreezeList(value.CardIds) },
        CardDamageModifierGrantedEvent value => value with
        { Modifier = value.Modifier with { CardKinds = FreezeList(value.Modifier.CardKinds) } },
        CardRecoveryModifierGrantedEvent value => value with
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
