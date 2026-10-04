namespace CardGame.Core;

public sealed record FrozenFactionRecoveryReceipt(int InstructionIndex, long DyingFrameId,
    int FactionCount, int GameDamagePoints, bool DrawIssued = false, bool FaceIssued = false);
public sealed record FrozenFactionRecoveryCapturedEvent(long FrameId, CardConversionSource Source,
    long DyingFrameId, int FactionCount, int GameDamagePoints) : IGameEvent;
public sealed record FrozenFactionHandDrawIssuedEvent(long FrameId, int OwnerSeat, int FactionCount, int DrawCount) : IGameEvent;
internal interface IFrozenFactionRecoveryProgramHost
{
    void FreezeLivingFactionRecovery(ProgramSkillFrame frame);
    void DrawToFrozenFactionCount(ProgramSkillFrame frame);
    void TurnOverIfFrozenFactionCountExceedsGameDamage(ProgramSkillFrame frame);
}
internal abstract class FrozenFactionRecoveryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            new(SkillProgramConditionKind.Always, 0, []));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SelfDyingResponse)];
}
internal sealed class FreezeLivingFactionRecoveryDescriptor : FrozenFactionRecoveryDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.FreezeLivingFactionRecovery;
    public override ISkillProgramEffectHandler Handler { get; } = new FreezeLivingFactionRecoveryHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RecoverTo,
        static (_, c) => c.RecoverTo(new(SkillProgramEffectOp.RecoverTo, SkillProgramEffectTarget.Owner, 0,
            new(SkillProgramConditionKind.Always, 0, []), numberExpression: SkillProgramNumberExpression.LivingFactionCount,
            minimumValue: 1, clampToMaxHp: true)));
}
internal sealed class DrawToFrozenFactionCountDescriptor : FrozenFactionRecoveryDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawToFrozenFactionCount;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawToFrozenFactionCountHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 3,
            new(SkillProgramConditionKind.Always, 0, []))));
}
internal sealed class TurnOverIfFrozenFactionCountExceedsGameDamageDescriptor : FrozenFactionRecoveryDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TurnOverIfFrozenFactionCountExceedsGameDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new TurnOverIfFrozenFactionCountExceedsGameDamageHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TurnOver,
        static (effect, context) => context.TurnOver(effect));
}
public sealed class FreezeLivingFactionRecoveryHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.FreezeLivingFactionRecovery;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h)
    { ((IFrozenFactionRecoveryProgramHost)h).FreezeLivingFactionRecovery(f); return SkillProgramStepOutcome.Continue; }
}
public sealed class DrawToFrozenFactionCountHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawToFrozenFactionCount;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h)
    { ((IFrozenFactionRecoveryProgramHost)h).DrawToFrozenFactionCount(f); return SkillProgramStepOutcome.Continue; }
}
public sealed class TurnOverIfFrozenFactionCountExceedsGameDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TurnOverIfFrozenFactionCountExceedsGameDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h)
    { ((IFrozenFactionRecoveryProgramHost)h).TurnOverIfFrozenFactionCountExceedsGameDamage(f); return SkillProgramStepOutcome.Continue; }
}
internal static class FrozenFactionRecoveryComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        SkillUsageScope? usageScope, int? usageLimit)
    {
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.FreezeLivingFactionRecovery or SkillProgramEffectOp.DrawToFrozenFactionCount or SkillProgramEffectOp.TurnOverIfFrozenFactionCountExceedsGameDamage)) return;
        if (window != SkillProgramTriggerWindow.SelfDyingResponse || subject != SkillProgramTriggerSubject.Owner ||
            usageScope != SkillUsageScope.Game || usageLimit != 1 || effects.Count != 3 ||
            effects[0].Op != SkillProgramEffectOp.FreezeLivingFactionRecovery ||
            effects[1].Op != SkillProgramEffectOp.DrawToFrozenFactionCount ||
            effects[2].Op != SkillProgramEffectOp.TurnOverIfFrozenFactionCountExceedsGameDamage)
            throw new InvalidOperationException($"Invalid skill program at {path}: frozen faction recovery requires one game-limited exact self-dying recover/draw/face sequence.");
    }
}
