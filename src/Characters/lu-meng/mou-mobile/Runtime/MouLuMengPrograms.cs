using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouLuMengKejiReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int? SelectedCardId, int MovementBefore, bool CostReturned = false, bool Paid = false);
public sealed record MouLuMengDujiangReceipt(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase);
public enum MouLuMengDuojingStage { Choosing, Taking, MovementChildren, Complete }
public sealed record MouLuMengDuojingReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, long CardUseId, long ActionId, ActualDiscardRecoveryPhaseKey Phase,
    IReadOnlyList<int> Targets, int TargetIndex = 0, MouLuMengDuojingStage Stage = MouLuMengDuojingStage.Choosing,
    int? TakenCardId = null, int? MovementSequence = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuMengKejiReceipt? MouLuMengKeji { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuMengDujiangReceipt? MouLuMengDujiang { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuMengDuojingReceipt? MouLuMengDuojing { get; init; }
}
public sealed record MouLuMengKejiReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLuMengKejiCostReturnedEvent(long FrameId, string Branch, bool Paid) : IGameEvent;
public sealed record MouLuMengDujiangAwakenedEvent(long FrameId, int OwnerSeat, CardConversionSource Source,
    string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int Armor) : IGameEvent;
public sealed record MouLuMengDuojingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long CardUseId, long ActionId, ActualDiscardRecoveryPhaseKey Phase, int TargetCount) : IGameEvent;
public sealed record MouLuMengDuojingOfferedEvent(long FrameId, int TargetIndex, int TargetSeat) : IGameEvent;
public sealed record MouLuMengDuojingSkippedEvent(long FrameId, int TargetIndex, int TargetSeat) : IGameEvent;
public sealed record MouLuMengDuojingArmorPaidEvent(long FrameId, int TargetIndex, int TargetSeat,
    long CardUseId, long ActionId, int ArmorBefore, int ArmorAfter) : IGameEvent;
public sealed record MouLuMengDuojingTakenEvent(long FrameId, int TargetIndex, int TargetSeat,
    int? CardId, int? MovementSequence) : IGameEvent;
public sealed record MouLuMengDuojingPhaseCreditEvent(long FrameId, int TargetIndex, int OwnerSeat,
    long CardUseId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLuMengDuojingCompletedEvent(long FrameId) : IGameEvent;

internal interface IMouLuMengHost
{
    SkillProgramStepOutcome Keji(ProgramSkillFrame frame);
    SkillProgramStepOutcome Dujiang(ProgramSkillFrame frame);
    SkillProgramStepOutcome Duojing(ProgramSkillFrame frame);
}
internal sealed class ResolveMouLuMengKejiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuMengKeji;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantArmor, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuMengHandler(SkillProgramEffectOp.ResolveMouLuMengKeji);
}
internal sealed class ResolveMouLuMengDujiangDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuMengDujiang;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuMengHandler(SkillProgramEffectOp.ResolveMouLuMengDujiang);
}
internal sealed class ResolveMouLuMengDuojingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuMengDuojing;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuMengHandler(SkillProgramEffectOp.ResolveMouLuMengDuojing);
}
internal sealed class MouLuMengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveMouLuMengKeji => host.GetCapability<IMouLuMengHost>().Keji(f),
        SkillProgramEffectOp.ResolveMouLuMengDujiang => host.GetCapability<IMouLuMengHost>().Dujiang(f),
        _ => host.GetCapability<IMouLuMengHost>().Duojing(f)
    };
}
