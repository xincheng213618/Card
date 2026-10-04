using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ActualUseTargetReturnKind { Slash = 5600, LegacyVirtualSlash = 5601, OrdinaryTrick = 5602 }
public sealed record ActualUseTargetIdentity(long CardUseFrameId, long? ActionId, int ActorSeat, int ProviderSeat,
    CardKind EffectiveKind, int TargetSeat, int ActualTurnNumber, int ActualTurnOwnerSeat, long? LegacyProducerProgramId);
public sealed record ActualUseTargetTrickReturn(int EffectCardId, LegalActionKind ActionKind,
    int? TargetCardId, CardKind? RequiredCardKind);
public sealed record ActualUseTargetWindowFrame(long Id, long ParentFrameId, ActualUseTargetReturnKind ReturnKind,
    IReadOnlyList<ProgramTriggerCandidate> Candidates, IReadOnlyList<ProgramSkillWindowContext> Contexts,
    ActualUseTargetTrickReturn? TrickReturn = null, int CandidateIndex = 0)
     : ResolutionFrame(Id, ResolutionFrameKind.ActualUseTargetWindow, ResolutionFrameStep.ResolvingEffect)
{
    private readonly IReadOnlyList<ProgramTriggerCandidate> _candidates = Array.AsReadOnly(Candidates.ToArray());
    public IReadOnlyList<ProgramTriggerCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<ProgramSkillWindowContext> _contexts = PaidTargetFrameCollections.FreezeContexts(Contexts);
    public IReadOnlyList<ProgramSkillWindowContext> Contexts { get => _contexts; init => _contexts = PaidTargetFrameCollections.FreezeContexts(value); }
}
public sealed record ProgramPaidOwnTargetReceipt(int InstructionIndex, ActualUseTargetIdentity Use,
    CardConversionSource Source, string GameplayHash, int HpBefore, int HpAfter, int ActualLost, bool Applied = false);
public sealed record PaidOwnTargetHpEvent(long ProgramFrameId, ProgramPaidOwnTargetReceipt Receipt) : IGameEvent;
public sealed record PaidOwnTargetAppliedEvent(long ProgramFrameId, long CardUseFrameId, long? ActionId,
    int ActorSeat, int TargetSeat, CardKind EffectiveKind, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record EarnedActualEndingBenefit(long Id, long ProducerProgramFrameId, long CardUseFrameId,
    CardConversionSource PaidSource, string PaidGameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat,
    CardConversionSource BenefitSource, string BenefitGameplayHash);
public sealed record EarnedActualEndingBenefitIssuedEvent(EarnedActualEndingBenefit Benefit) : IGameEvent;
public sealed record EarnedActualEndingBenefitConsumedEvent(long Id, int ActualTurnNumber,
    int ActualTurnOwnerSeat, bool Applied) : IGameEvent;
public enum LostHpOwnedGiftStage { Drawing, Offering, Moving, Complete }
public sealed record ProgramLostHpOwnedGiftReceipt(int InstructionIndex, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int MaximumGiftCount,
    int ActualDrawCount, long DrawSequenceBefore, long DrawSequenceAfter, LostHpOwnedGiftStage Stage,
    IReadOnlyList<int> PaidCardIds, int DeliveredCount = 0, ProgramOwnedGiftPayment? LastPayment = null)
{
    private readonly IReadOnlyList<int> _paidCardIds = Array.AsReadOnly(PaidCardIds.ToArray());
    public IReadOnlyList<int> PaidCardIds { get => _paidCardIds; init => _paidCardIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ProgramOwnedGiftPayment(int CardId, CardLocation From, int RecipientSeat,
    long SequenceBefore, long SequenceAfter, bool Delivered);
public sealed record LostHpDrawGiftFrozenEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int MaximumGiftCount, int ActualDrawCount) : IGameEvent;
public sealed record LostHpOwnedCardGivenEvent(long ProgramFrameId, CardConversionSource Source,
    int CardId, CardLocation From, int RecipientSeat, int PaidCount, int DeliveredCount, int MaximumGiftCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record LostHpOwnedGiftFinishedEvent(long ProgramFrameId, int PaidCount, int DeliveredCount,
    int MaximumGiftCount, bool Completed) : IGameEvent;

internal interface IPaidTargetEndingProgramHost
{
    SkillProgramStepOutcome PayHpThenNullifyOwnActualUseTarget(ProgramSkillFrame frame);
    void ScheduleEarnedActualEndingBenefit(ProgramSkillFrame frame, string skillId, string bindingId);
    SkillProgramStepOutcome DrawLostHpThenOfferOwnedCardsUpTo(ProgramSkillFrame frame);
}
internal sealed class PayHpThenNullifyOwnActualUseTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new PayHpThenNullifyOwnActualUseTargetHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (e, c) => { c.LoseHp(e); c.NullifyCurrentCardEffect(); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OtherActualUseTargeted)];
}
internal sealed class ScheduleEarnedActualEndingBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleEarnedActualEndingBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new ScheduleEarnedActualEndingBenefitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "stateId", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), stateId: r.RequiredIdentifier("stateId"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OtherActualUseTargeted)];
}
internal sealed class DrawLostHpThenOfferOwnedCardsUpToDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawLostHpThenOfferOwnedCardsUpToHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 0,
            new(SkillProgramConditionKind.Always, 0, []), numberExpression: SkillProgramNumberExpression.OwnerLostHp)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class PayHpThenNullifyOwnActualUseTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IPaidTargetEndingProgramHost)h).PayHpThenNullifyOwnActualUseTarget(f);
}
public sealed class ScheduleEarnedActualEndingBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleEarnedActualEndingBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h)
    { ((IPaidTargetEndingProgramHost)h).ScheduleEarnedActualEndingBenefit(f, e.SourceBind!, e.StateId!); return SkillProgramStepOutcome.Continue; }
}
public sealed class DrawLostHpThenOfferOwnedCardsUpToHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IPaidTargetEndingProgramHost)h).DrawLostHpThenOfferOwnedCardsUpTo(f);
}

internal static class PaidTargetEndingComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope, bool optional)
    {
        if ((window == SkillProgramTriggerWindow.OtherActualUseTargeted ||
            effects.Any(e => e.Op is SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget or SkillProgramEffectOp.ScheduleEarnedActualEndingBenefit)) &&
            !effects.Any(e => e.Op is SkillProgramEffectOp.OfferHalfHandRecipientSupport or SkillProgramEffectOp.DrawThenNullifyOwnMultiTargetTrick or
                SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget or SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer))
        {
            bool Choice(SkillProgramEffect e, string option) => e.Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
                e.Condition.SourceBind == "benefit" && e.Condition.OptionId == option;
            if (window != SkillProgramTriggerWindow.OtherActualUseTargeted || subject != SkillProgramTriggerSubject.Owner ||
                !optional || effects.Count != 4 || effects[0].Op != SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget ||
                effects[1] is not { Op: SkillProgramEffectOp.ChooseOption, Target: SkillProgramEffectTarget.Owner, ResultBind: "benefit" } ||
                !effects[1].Options.Select(o => o.Id).SequenceEqual(["obtain", "ending", "skip"]) ||
                effects[2] is not { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Target: SkillProgramEffectTarget.Owner,
                    ChooserRef.Kind: ProgramParticipantRef.Owner, CardOwnerRef.Kind: ProgramParticipantRef.EventSource,
                    Destination: SkillProgramCardDestination.OwnerHand, SkipIfNoCards: true, AwaitMovementTriggers: true } ||
                !effects[2].Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) || !Choice(effects[2], "obtain") ||
                effects[3].Op != SkillProgramEffectOp.ScheduleEarnedActualEndingBenefit || !Choice(effects[3], "ending"))
                throw new InvalidOperationException($"Invalid skill program at {path}: a paid target requires one exact cost/nullification and mutually exclusive obtain/actual-ending/skip choices.");
        }
        if (scope == SkillProgramTurnOwnerScope.EarnedActualEnding || effects.Any(e => e.Op == SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo))
            if (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner || effects.Count != 1 ||
                effects[0].Op != SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo ||
                scope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.EarnedActualEnding) ||
                optional != (scope == SkillProgramTurnOwnerScope.Own))
                throw new InvalidOperationException($"Invalid skill program at {path}: lost-HP gifts require one own optional or exact earned mandatory actual Ending instruction.");
    }
}

internal static class PaidTargetFrameCollections
{
    private static IReadOnlyDictionary<K,V>? Map<K,V>(IReadOnlyDictionary<K,V>? source) where K : notnull => source is null ? null :
        new System.Collections.ObjectModel.ReadOnlyDictionary<K,V>(source.ToDictionary(p => p.Key, p => p.Value));
    private static IReadOnlyList<T>? List<T>(IReadOnlyList<T>? source) => source is null ? null : Array.AsReadOnly(source.ToArray());
    internal static IReadOnlyList<ProgramSkillWindowContext> FreezeContexts(IReadOnlyList<ProgramSkillWindowContext> contexts) =>
        Array.AsReadOnly(contexts.Select(c => c.Facts is not { } f ? c : c with { Facts = f with {
            BooleanStates = Map(f.BooleanStates), MarkerCounts = Map(f.MarkerCounts), GlobalMarkerCounts = Map(f.GlobalMarkerCounts),
            EventTargetMarkerCounts = Map(f.EventTargetMarkerCounts), PublicPersistentPileCounts = Map(f.PublicPersistentPileCounts),
            CardUseConversionSkillIds = List(f.CardUseConversionSkillIds), UnfulfilledPhaseColorRestrictionInstances = List(f.UnfulfilledPhaseColorRestrictionInstances),
            BlockedDamageSourceSkills = List(f.BlockedDamageSourceSkills), LowHandPopulationSeats = List(f.LowHandPopulationSeats)
        } }).ToArray());
}
