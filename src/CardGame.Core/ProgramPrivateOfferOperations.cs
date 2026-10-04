using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record DeferredPrivateOfferIdentity(long DepositFrameId, string DepositBindingId,
    string ResolverBindingId, string GameplayHash, int TargetSeat, int CreatedTurn);

public sealed record DeferredPrivateOfferDue
{
    private IReadOnlyList<CardLocation> _locations = Array.Empty<CardLocation>();
    [JsonConstructor]
    public DeferredPrivateOfferDue(int turnNumber, int targetSeat, IReadOnlyList<CardLocation> locations)
    { TurnNumber = turnNumber; TargetSeat = targetSeat; Locations = locations; }
    public int TurnNumber { get; init; }
    public int TargetSeat { get; init; }
    public IReadOnlyList<CardLocation> Locations
    { get => _locations; init => _locations = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}

public enum PrivateOfferStage { DepositChildren, ChoosingExchange, ExchangeChildren, ObtainChildren, RemovalChildren, HpLoss, Complete }
public sealed record ProgramPrivateOfferReceipt(int InstructionIndex, CardLocation Location, int CardId,
    PrivateOfferStage Stage, long SequenceBefore, long SequenceAfter, int? PaymentCardId = null,
    CardLocation? PaymentFrom = null, int? DrawRecipient = null);
public sealed record PrivateCardOfferDepositedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int CreatedTurn, long Before, long After) : IGameEvent;
public sealed record PrivateCardOfferViewedEvent(long FrameId, long DepositFrameId, int TargetSeat, int ActualTurn) : IGameEvent;
public sealed record PrivateCardOfferSegmentIssuedEvent(long FrameId, long DepositFrameId, PrivateOfferStage Stage,
    int Count, long Before, long After) : IGameEvent;

internal interface IPrivateOfferProgramHost
{
    SkillProgramStepOutcome DepositPrivateCardOffer(ProgramSkillFrame frame, string bind, string resolver);
    SkillProgramStepOutcome ResolvePrivateCardOffer(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveGameTargetHandHp(ProgramSkillFrame frame, string stateId);
}

internal sealed class DepositBoundPrivateCardOfferDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositBoundPrivateCardOffer;
    public override ISkillProgramEffectHandler Handler { get; } = new PrivateOfferHandler(SkillProgramEffectOp.DepositBoundPrivateCardOffer);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (e, c) => c.LoseHp(new(SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.SelectedTarget, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "stateId", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget(),
         new RequireOwnedCardSet(e.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand]),
         new MoveCardSet(e.SourceBind!, null, SkillProgramCardDestination.OwnerPersistentZone)];
}

internal sealed class ResolveDeferredPrivateCardOfferDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDeferredPrivateCardOffer;
    public override ISkillProgramEffectHandler Handler { get; } = new PrivateOfferHandler(SkillProgramEffectOp.ResolveDeferredPrivateCardOffer);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal sealed class ResolveGameTargetHandHpChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveGameTargetHandHpChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new PrivateOfferHandler(SkillProgramEffectOp.ResolveGameTargetHandHpChoice);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PriceGameTargetHandHpChoice());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("Game-target hand/HP choice requires one selected target.");
        var e = new SkillProgramEffect(Op, target, 0, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}

internal sealed class PrivateOfferHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    {
        var h = (IPrivateOfferProgramHost)host;
        return op switch
        {
            SkillProgramEffectOp.DepositBoundPrivateCardOffer => h.DepositPrivateCardOffer(f, e.SourceBind!, e.StateId!),
            SkillProgramEffectOp.ResolveDeferredPrivateCardOffer => h.ResolvePrivateCardOffer(f),
            SkillProgramEffectOp.ResolveGameTargetHandHpChoice => h.ResolveGameTargetHandHp(f, e.StateId!),
            _ => throw new InvalidOperationException("Unknown private offer operation.")
        };
    }
}
