using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouZhangXiuOlStage { Target, Cost, CostChildren, Issuing, NativeUse, Children, Defense, DefenseChildren, Drawing, DrawChildren, Penalty, PenaltyChildren }
public sealed record MouZhangXiuOlReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, MouZhangXiuOlStage Stage)
{
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public int TargetSeat { get; init; } = -1;
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int? PaidCardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool NativeReturned { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed record MouZhangXiuOlQiulieOrigin(long FrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int TargetSeat, ActualDiscardRecoveryPhaseKey Phase);
public sealed record MouZhangXiuOlZhuijiaoEntry(long UseId, long ActionId, CardConversionSource Source, string GameplayHash, CurrentTurnCardUseKindsRecordedEvent Previous)
{
    public bool Boosted { get; init; }
    public bool DrawReturned { get; init; }
    public bool PenaltyIssued { get; init; }
    public bool PenaltyReturned { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhangXiuOlReceipt? MouZhangXiuOl { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhangXiuOlQiulieOrigin? MouZhangXiuOlQiulie { get; init; }
    private IReadOnlyList<MouZhangXiuOlZhuijiaoEntry>? _mouZhangXiuOl;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<MouZhangXiuOlZhuijiaoEntry>? MouZhangXiuOlZhuijiao
    { get => _mouZhangXiuOl; init => _mouZhangXiuOl = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouZhangXiuOlHuntChosenEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TargetSeat) : IGameEvent;
public sealed record MouZhangXiuOlPhaseOfferedEvent(long FrameId, CardConversionSource Source, ActualDiscardRecoveryPhaseKey Phase, int TargetSeat) : IGameEvent;
public sealed record MouZhangXiuOlStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long UseId) : IGameEvent;
public sealed record MouZhangXiuOlDiscardPaidEvent(long FrameId, int PayerSeat, int CardId, long Before, long After, bool Defense) : IGameEvent;
public sealed record MouZhangXiuOlQiulieIssuedEvent(MouZhangXiuOlQiulieOrigin Origin) : IGameEvent;
public sealed record MouZhangXiuOlQiulieReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouZhangXiuOlQiulieNullifiedEvent(long FrameId, long UseId, int TargetSeat, int PaidCardId) : IGameEvent;
public sealed record MouZhangXiuOlQualifiedEvent(MouZhangXiuOlZhuijiaoEntry Entry) : IGameEvent;
public sealed record MouZhangXiuOlBoostedEvent(long FrameId, long UseId, CardConversionSource Source) : IGameEvent;
public sealed record MouZhangXiuOlDrawReturnedEvent(long FrameId, long UseId, int OwnerSeat, int Requested, int Actual, int Replacement) : IGameEvent;
public sealed record MouZhangXiuOlUseDamageEvent(long OriginFrameId, int? InstructionIndex, long DamageId, int Amount) : IGameEvent;
public sealed record MouZhangXiuOlPenaltyStartedEvent(long FrameId, long UseId, CardConversionSource Source) : IGameEvent;
public sealed record MouZhangXiuOlCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;

internal interface IMouZhangXiuOlHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MouZhangXiuOlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MouZhangXiuOlHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.ResolveMouZhangXiuQiulie => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],
        SkillProgramEffectOp.UseMouZhangXiuQiulieSlash => [new RequireTriggerWindows([SkillProgramTriggerWindow.JudgmentPhaseStarting, SkillProgramTriggerWindow.DrawPhaseStarting,
            SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramTriggerWindow.DiscardPhaseStarting, SkillProgramTriggerWindow.TurnEnding])],
        SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseBeforeTargetEffects)],
        SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)]
    };
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard |
        (Op is SkillProgramEffectOp.ResolveMouZhangXiuQiulie or SkillProgramEffectOp.UseMouZhangXiuQiulieSlash ? ProgramTrackedFact.ActualDiscardRecoveryPhase : ProgramTrackedFact.None));
}
internal sealed class ResolveMouZhangXiuQiulieDescriptor : MouZhangXiuOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhangXiuQiulie; }
internal sealed class UseMouZhangXiuQiulieSlashDescriptor : MouZhangXiuOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseMouZhangXiuQiulieSlash; }
internal sealed class DefendMouZhangXiuQiulieSlashDescriptor : MouZhangXiuOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash; }
internal sealed class ResolveMouZhangXiuZhuijiaoDescriptor : MouZhangXiuOlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration);
}
internal sealed class CompleteMouZhangXiuZhuijiaoDescriptor : MouZhangXiuOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao; }
internal sealed class MouZhangXiuOlHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouZhangXiuOlHost>().Resolve(frame, operation);
}
