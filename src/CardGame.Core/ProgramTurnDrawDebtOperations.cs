namespace CardGame.Core;

public sealed record ProgramExtraDrawReceipt(int InstructionIndex, long DrawWindowFrameId, int ActualTurnNumber,
    int TurnOwnerSeat, string StateId, int RequestedCount, int ActualCount, long MovementSequenceBefore, long MovementSequenceAfter);
public sealed record ProgramTurnDrawDebtPayment(int InstructionIndex, string StateId, string ResultBind,
    long ExtraDrawProgramFrameId, int EndingFactionCount, int RequiredPaymentCount, long MovementSequenceBefore);
public sealed record ProgramExtraDrawIssuedEvent(long FrameId, int ActualTurnNumber, int TurnOwnerSeat,
    CardConversionSource Source, string GameplayHash, string StateId, int RequestedCount, int ActualCount) : IGameEvent;
public sealed record ProgramTurnDrawDebtPaymentStartedEvent(long FrameId, int ActualTurnNumber, int OwnerSeat,
    CardConversionSource Source, string StateId, long ExtraDrawProgramFrameId, int EndingFactionCount, int RequiredPaymentCount) : IGameEvent;
public sealed record ActualTurnDamageCardUsedEvent(int ActualTurnNumber, int TurnOwnerSeat, int ActorSeat,
    long CardUseFrameId, CardKind EffectiveKind) : IGameEvent;

internal interface ITurnDrawDebtProgramHost
{
    SkillProgramStepOutcome DrawExtraAndArmTurnDamageUseDebt(ProgramSkillFrame frame, string stateId);
    SkillProgramStepOutcome SelectTurnDamageUseDebtPayment(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class DrawExtraAndArmTurnDamageUseDebtDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawExtraAndArmTurnDamageUseDebt;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawExtraAndArmTurnDamageUseDebtHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.DrawExtraPublicFactions(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting), new RequireOwnTurnBoundary()];
}
public sealed class DrawExtraAndArmTurnDamageUseDebtHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawExtraAndArmTurnDamageUseDebt;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ITurnDrawDebtProgramHost)host).DrawExtraAndArmTurnDamageUseDebt(frame, effect.StateId!);
}
internal sealed class SelectTurnDamageUseDebtPaymentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTurnDamageUseDebtPayment;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectTurnDamageUseDebtPaymentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectOwnedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            stateId: r.RequiredIdentifier("stateId"), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary(),
         new CaptureSourceCard(effect.ResultBind!, int.MaxValue, false, SkillProgramEffectTarget.Owner)];
}
public sealed class SelectTurnDamageUseDebtPaymentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTurnDamageUseDebtPayment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((ITurnDrawDebtProgramHost)host).SelectTurnDamageUseDebtPayment(frame, effect);
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void DrawExtraPublicFactions(SkillProgramEffect effect) => Draw(new(SkillProgramEffectOp.Draw,
        SkillProgramEffectTarget.Owner, _publicContext.PublicLivingFactionCount ?? 0, effect.Condition));
}
