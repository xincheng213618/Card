namespace CardGame.Core;

public enum ProgramDamageJudgmentPaymentStage
{
    Judging, ChoosingPayment, PaymentChildren, SuitEffectChildren,
    ChoosingSourceDiscard, SourceDiscardChildren, MatchingClaims, ClaimChildren, CleanupChildren, Complete
}

// All facts belong to the original actual-damage candidate. There is no pending
// use-ID table, private view payload, or mutable material collection here.
public sealed record ProgramDamageJudgmentSuitPayment(int InstructionIndex, long DamageWindowId,
    long DamageFrameId, long CardUseFrameId, long? CardActionId, int OwnerSeat, int SourceSeat, int TargetSeat,
    int OccurrenceIndex, long JudgmentFrameId, string Reason, string ResultBind,
    ProgramDamageJudgmentPaymentStage Stage = ProgramDamageJudgmentPaymentStage.Judging,
    long? LegacyVirtualProducerFrameId = null,
    int? JudgmentCardId = null, CardKind? JudgmentCardKind = null, Suit? JudgmentSuit = null, int? JudgmentRank = null,
    int? PaidCardId = null, CardKind? PaidCardKind = null, CardLocation? PaidFrom = null, CardLocation? PaidTo = null, Suit? PaidSuit = null,
    int? PaidRank = null, long? PaidMovementSequence = null, int SourceDiscardsRemaining = 0,
    bool JudgmentClaimIssued = false, bool PaymentClaimIssued = false,
    long? JudgmentClaimMovementSequence = null, long? PaymentClaimMovementSequence = null,
    long? SourceDiscardMovementSequence1 = null, long? SourceDiscardMovementSequence2 = null);

public sealed record ProgramJudgmentSuitPaymentCommittedEvent(long FrameId, string SkillId, string BindingId,
    string SkillInstanceId, int OwnerSeat, long JudgmentFrameId, int JudgmentCardId, int PaidCardId,
    long PaidMovementSequence, bool SuitMatched, bool RankMatched,
    Suit JudgmentSuit, int JudgmentRank, Suit PaidSuit, int PaidRank) : IGameEvent;

public sealed record ProgramJudgmentSuitPaymentCardClaimedEvent(long FrameId, string SkillId, string SkillInstanceId,
    int OwnerSeat, long JudgmentFrameId, int CardId, long MovementSequence, bool IsJudgmentCard) : IGameEvent;

internal interface IDamageJudgmentSuitPaymentHost
{
    SkillProgramStepOutcome JudgeDamageTargetThenOfferSuitDiscard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class JudgeDamageTargetThenOfferSuitDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new JudgeDamageTargetThenOfferSuitDiscardHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage | ProgramContextCapability.Judgment;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartJudgment,
        static (_, context) => context.PriceOptionalDamageJudgmentPayment());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "judgmentReason", "resultBind", "visibility", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner ||
            reader.RequiredEnum<SkillProgramCardSetVisibility>("visibility") != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException("Damage-target suit payment requires its owner's public judgment result.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, reader.Condition(),
            judgmentReason: reader.RequiredIdentifier("judgmentReason"), resultBind: reader.RequiredIdentifier("resultBind"),
            visibility: SkillProgramCardSetVisibility.Public, sourceRef: new(ProgramParticipantRef.EventSource));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new CreateCardSet(effect.ResultBind!, 1, true),
         new MoveCardSet(effect.ResultBind!, null, SkillProgramCardDestination.DiscardPile)];
}

public sealed class JudgeDamageTargetThenOfferSuitDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IDamageJudgmentSuitPaymentHost)host).JudgeDamageTargetThenOfferSuitDiscard(frame, effect);
}

internal sealed partial class ProgramAiEstimateContext
{
    // A public optional opportunity; selecting the real payment is scored later
    // from the finalized public suit/rank and the chooser's own visible cards.
    internal void PriceOptionalDamageJudgmentPayment() => _otherAdjustment += 8d;
}
