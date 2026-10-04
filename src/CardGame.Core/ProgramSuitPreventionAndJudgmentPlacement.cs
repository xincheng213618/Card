namespace CardGame.Core;

public enum SuitPreventionBenefitStage { ChoosingCost, ChoosingRecipient, CostPaid, Damaging, LosingHp, Drawing, Gifting, Finished }
public enum SuitPreventionBenefitBranch { DamageAndDraw, LoseHpAndGift }
public sealed record ProgramSuitPreventionBenefitReceipt(int InstructionIndex, long BeforeDamageFrameId,
    long OriginalAttackFrameId, int SourceSeat, bool SourceLess, int OriginalTargetSeat, int OriginalAmount,
    SuitPreventionBenefitStage Stage, int? CostCardId = null, CardLocation? CostFrom = null, Suit? CostEffectiveSuit = null,
    int? RecipientSeat = null, SuitPreventionBenefitBranch? Branch = null, long CostMovementSequence = 0,
    long MovementSequenceBefore = 0, long MovementSequenceAfter = 0, int RequestedDrawCount = 0,
    int ActualDrawCount = 0, int HpBefore = 0, long GiftMovementSequence = 0, bool GiftUnavailable = false);
public enum MatchedJudgmentPlacementStage { ChoosingDestination, Placed, ChoosingSelfDiscard, SelfDiscarded }
public sealed record ProgramMatchedJudgmentPlacementReceipt(int InstructionIndex, string SourceBind,
    long JudgmentFrameId, int CardId, Suit EffectiveSuit, CardLocation ResultFrom,
    long ResultMovementSequence, MatchedJudgmentPlacementStage Stage, int? RecipientSeat = null,
    bool OnTop = false, long PlacementMovementSequence = 0, int? SelfDiscardCardId = null,
    CardLocation? SelfDiscardFrom = null, long SelfDiscardMovementSequence = 0);

// Each public scalar fact is emitted only after the corresponding actual public payment/move.
public sealed record ProgramSuitPreventionPaymentEvent(long ProgramFrameId, long BeforeDamageFrameId,
    long OriginalAttackFrameId, int OwnerSeat, int CostCardId, CardLocation CostFrom, Suit EffectiveSuit,
    int RecipientSeat, SuitPreventionBenefitBranch Branch, long MovementSequence) : IGameEvent;
public sealed record ProgramSuitPreventionBenefitIssuedEvent(long ProgramFrameId, int RecipientSeat,
    SuitPreventionBenefitBranch Branch, int? SourceSeat, int Amount) : IGameEvent;
public sealed record ProgramSuitPreventionDamageCompletedEvent(long ProgramFrameId, long OriginalAttackFrameId,
    int RecipientSeat, bool Applied) : IGameEvent;
public sealed record ProgramSuitPreventionGiftEvent(long ProgramFrameId, int RecipientSeat, int CostCardId,
    long MovementSequence, bool Unavailable) : IGameEvent;
public sealed record ProgramSuitPreventionDrawEvent(long ProgramFrameId, int RecipientSeat, int Requested,
    int Actual, long FirstMovementSequence, long LastMovementSequence) : IGameEvent;
public sealed record ProgramMatchedJudgmentPlacedEvent(long ProgramFrameId, long JudgmentFrameId, int CardId,
    int? RecipientSeat, bool OnTop, long MovementSequence) : IGameEvent;
public sealed record ProgramMatchedJudgmentSelfDiscardedEvent(long ProgramFrameId, long JudgmentFrameId,
    int CardId, CardLocation From, long MovementSequence) : IGameEvent;

internal interface ISuitPreventionAndJudgmentPlacementHost
{
    SkillProgramStepOutcome DiscardSuitPreventDamageAndBenefit(ProgramSkillFrame frame);
    SkillProgramStepOutcome PlaceMatchedJudgmentCard(ProgramSkillFrame frame, string sourceBind, IReadOnlyList<Suit> suits);
}
internal sealed class DiscardSuitPreventDamageAndBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new SuitPreventionAndJudgmentPlacementHandler(SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "suits", "amount", "maximumCards", "condition");
        var suits = r.RequiredEnumArray<Suit>("suits"); var max = r.RequiredInt("maximumCards");
        if (suits.Count == 0 || suits.Distinct().Count() != suits.Count || suits.Contains(Suit.None) || max is < 1 or > 20)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: distinct ordinary suits and maximumCards1..20 required.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(), suits: suits, maximumCards: max);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
internal sealed class PlaceMatchedJudgmentCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceMatchedJudgmentCard;
    public override ISkillProgramEffectHandler Handler { get; } = new SuitPreventionAndJudgmentPlacementHandler(SkillProgramEffectOp.PlaceMatchedJudgmentCard);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "suits", "condition");
        var suits = r.RequiredEnumArray<Suit>("suits");
        if (suits.Count == 0 || suits.Distinct().Count() != suits.Count || suits.Contains(Suit.None))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.suits: distinct ordinary suits required.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), suits: suits);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSingleCardSet(e.SourceBind!),
         new MoveCardSet(e.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}
internal sealed class SuitPreventionAndJudgmentPlacementHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit
            ? ((ISuitPreventionAndJudgmentPlacementHost)host).DiscardSuitPreventDamageAndBenefit(f)
            : ((ISuitPreventionAndJudgmentPlacementHost)host).PlaceMatchedJudgmentCard(f, e.SourceBind!, e.Suits);
}
internal static class SuitPreventionAndJudgmentPlacementComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardSuitPreventDamageAndBenefit) &&
            (effects.Count != 1 || window != SkillProgramTriggerWindow.BeforeDamageApplied || subject != SkillProgramTriggerSubject.DamageTarget))
            throw new InvalidOperationException($"Invalid skill program at {path}: suit prevention requires standalone before-damage damageTarget.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.PlaceMatchedJudgmentCard) &&
            (effects.Count != 2 || window != SkillProgramTriggerWindow.TurnEnding || scope != SkillProgramTurnOwnerScope.Own ||
             effects[0].Op != SkillProgramEffectOp.StartJudgment || effects[0].Target != SkillProgramEffectTarget.Owner ||
             effects[0].Visibility != SkillProgramCardSetVisibility.Public || effects[1].Op != SkillProgramEffectOp.PlaceMatchedJudgmentCard ||
             effects[0].ResultBind != effects[1].SourceBind || effects[0].SourceRef is not null))
            throw new InvalidOperationException($"Invalid skill program at {path}: placement requires own ending judgment then its exact public result.");
    }
}
