namespace CardGame.Core;

public sealed record ProgramEquipmentPairPaymentReceipt(int InstructionIndex, int FirstSeat, int SecondSeat,
    int FirstEquipmentCount, int SecondEquipmentCount, int OwnerLostHp, int RequiredPaymentCount,
    string ResultBind, int ActualTurnNumber, long MovementSequenceBefore, long? ExchangeMovementSequenceBefore = null);
public sealed record ProgramEquipmentPairPaymentFrozenEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int FirstSeat, int SecondSeat, int FirstEquipmentCount, int SecondEquipmentCount, int OwnerLostHp,
    int RequiredPaymentCount, string ResultBind, int ActualTurnNumber, long MovementSequenceBefore) : IGameEvent;
public sealed record ProgramEquipmentPairExchangeStartedEvent(long FrameId, int FirstSeat, int SecondSeat,
    long MovementSequenceBefore) : IGameEvent;

internal interface IEquipmentPairPaymentProgramHost
{
    SkillProgramStepOutcome SelectEquipmentPairAndPayment(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class SelectEquipmentPairAndPaymentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectEquipmentPairAndPayment;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectEquipmentPairAndPaymentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ExchangeSelectedTargetHands, static (e, c) => c.SelectPayableEquipmentPair(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new SelectTargetSet(2, 2), new CaptureSourceCard(effect.ResultBind!, int.MaxValue, false, SkillProgramEffectTarget.Owner)];
}
public sealed class SelectEquipmentPairAndPaymentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectEquipmentPairAndPayment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IEquipmentPairPaymentProgramHost)host).SelectEquipmentPairAndPayment(f, e);
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void SelectPayableEquipmentPair(SkillProgramEffect effect)
    {
        // Availability already requires a currently payable public equipment pair.
        // A bounded public prior does not inspect other players' hidden hand identities.
        _bindings[effect.ResultBind!] = UnknownCards(0d, ownerHeld: true);
        _otherAdjustment += 4d;
    }
}
