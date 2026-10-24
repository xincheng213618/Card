using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MobileShiWangChangStage { Targets, Drawing, DrawChildren, Complete, Mode, Resolving }
public sealed record MobileShiWangChangReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, int ActualTurn, int TurnOwner, int Maximum, MobileShiWangChangStage Stage)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public int Index { get; init; }
    public bool NonBasicGained { get; init; }
    public string? Mode { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int Actor { get; init; } = -1;
    public CardKind Kind { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiWangChangReceipt? MobileShiWangChang { get; init; }
}
public sealed record MobileShiWangChangStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int ActualTurn, int TurnOwner, int Maximum, long UseId, long ActionId, int Actor, CardKind Kind) : IGameEvent;
public sealed record MobileShiKaijiTargetCommittedEvent(long FrameId, int Index, int Target) : IGameEvent;
public sealed record MobileShiKaijiDrawReturnedEvent(long FrameId, int Index, int Target, int Actual, int Replacement,
    bool NonBasicGained, long Before, long After) : IGameEvent;
public sealed record MobileShiKaijiRewardBoundEvent(long FrameId, bool Qualified) : IGameEvent;
public sealed record MobileShiShepanChosenEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int Round, CardKind Name, string Mode, long UseId, long ActionId, int Actor) : IGameEvent;
public sealed record MobileShiShepanResolvedEvent(long FrameId, long UseId, long ActionId, int Owner,
    int Actor, int OwnerHand, int ActorHand, bool Ineffective) : IGameEvent;

internal interface IMobileShiWangChangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MobileShiWangChangDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MobileShiWangChangHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class SelectMobileShiKaijiTargetsDescriptor : MobileShiWangChangDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectMobileShiKaijiTargets;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class BindMobileShiKaijiRewardDescriptor : MobileShiWangChangDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindMobileShiKaijiReward;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow), new CreateChoiceResult("kaiji-reward", ["yes", "no"])];
}
internal sealed class ChooseMobileShiShepanDescriptor : MobileShiWangChangDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseMobileShiShepan;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseBeforeTargetEffects), new RequireContext(ProgramContextCapability.CardAction), new CreateChoiceResult("shepan-mode", ["draw", "top"])];
}
internal sealed class ResolveMobileShiShepanDescriptor : MobileShiWangChangDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiShepan;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseBeforeTargetEffects), new RequireChoiceOptions("shepan-mode", ["draw", "top"])];
}
internal sealed class MobileShiWangChangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiWangChangHost>().Resolve(f, op);
}
