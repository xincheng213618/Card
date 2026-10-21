using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MouLidanPermitEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ActionId, CardKind Kind, bool ForSlash) : IGameEvent;
public sealed record MouLidanConsumedEvent(long PermitFrameId, long PermitActionId, CardConversionSource Source,
    long UseId, long ActionId, int ActorSeat) : IGameEvent;
public sealed record MouLidanUseReceipt(CardConversionSource Source, string GameplayHash, long UseId, long ActionId,
    int ActorSeat, int ProviderSeat, int Maximum)
{
    public bool Offered { get; init; }
    private IReadOnlyList<int> _original = [];
    private IReadOnlyList<int> _added = [];
    public IReadOnlyList<int> Original { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Added { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLidanSelection(long UseId, long ActionId, int Maximum)
{
    private IReadOnlyList<int> _selected = [];
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLidanUseFrozenEvent(long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int ActorSeat, int ProviderSeat, int Maximum) : IGameEvent;
public sealed record MouLidanTargetsAppliedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source,
    string GameplayHash, int OriginalCount, int AddedCount) : IGameEvent;
public sealed record MouLidanTargetSlotEvent(long FrameId, int Index, int Seat, bool Added) : IGameEvent;
public sealed record MouDanchiDamage(long WindowId, long DamageId, long OriginalUseId, long? OriginalActionId,
    int SourceSeat, int VictimSeat, int Amount, int Turn);
public sealed record MouDanchiAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MouDanchiDamage Damage) : IGameEvent;
public sealed record MouDanchiPromiseEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MouDanchiDamage Damage, SkillProgramCardCategory Category) : IGameEvent;
public sealed record MouDanchiNextUseEvent(long PromiseId, long UseId, long ActionId, int ActorSeat,
    CardKind Kind, SkillProgramCardCategory Category, int Turn) : IGameEvent;
public sealed record MouDanchiSuspendedUse(long UseId, long ActionId, CardKind Kind, int SourceSeat, int TargetIndex,
    bool AttackActive, bool DuelActive, bool GroupActive, bool HasFireSelection, int? FireRevealedCardId);
public sealed record MouDanchiCursorSuspendedEvent(long FrameId, MouDanchiSuspendedUse Suspension) : IGameEvent;
public sealed record MouDanchiCursorRestoredEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public enum MouDanchiStage { TypeChoice, DrawIssuing, NativeUse, Children, SlashChoice, SlashIssuing, Complete }
public sealed record MouDanchiReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    long PromiseId, int Turn, int VictimSeat)
{
    public MouDanchiStage Stage { get; init; }
    public SkillProgramCardCategory Chosen { get; init; }
    public bool Different { get; init; }
    public long NextActionId { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public CardKind Kind { get; init; }
    public int TargetSeat { get; init; } = -1;
    public bool Returned { get; init; }
    public MouDanchiSuspendedUse? Suspension { get; init; }
    private IReadOnlyList<long> _batches = [];
    public IReadOnlyList<long> Batches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouDanchiNativeOrigin(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActorSeat, CardKind Kind);
public sealed record MouDanchiNativeIssuedEvent(MouDanchiNativeOrigin Origin) : IGameEvent;
public sealed record MouDanchiNativeReturnedEvent(long FrameId, long UseId, long ActionId, CardKind Kind) : IGameEvent;
public sealed record MouDanchiNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;
public sealed record MouDanchiCompletedEvent(long FrameId, long PromiseId, bool Reward) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLidanSelection? MouLidanSelection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDanchiReceipt? MouDanchi { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLidanUseReceipt? MouLidan { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDanchiNativeOrigin? MouDanchiOrigin { get; init; }
}
internal sealed record MouChengYuTargetOption(string Label)
{
    private IReadOnlyList<int> _seats = [];
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
}
internal interface IMouChengYuFacts : IProgramSelectionQuery
{
    int Turn { get; }
    bool GameOver { get; }
    bool Alive(int seat);
    bool Human(int seat);
    IReadOnlyList<T> History<T>() where T : IGameEvent;
    CardActionContext Action(ProgramSkillFrame frame);
    CardUseFrame? Use(long id);
    CardUseFrame? TargetUse(ProgramSkillFrame frame);
    MouDanchiDamage? Damage(ProgramSkillFrame frame);
    IReadOnlyList<MouChengYuTargetOption> TargetOptions(long useId, IReadOnlyList<int> selected);
    IReadOnlyList<int> SlashTargets(int actor);
    bool NativeParentMatches(ProgramSkillFrame frame);
}
internal interface IMouChengYuNative : IProgramRuntimeOperations
{
    void FreezeLidan(CardUseFrame use, MouLidanUseReceipt receipt);
    void ApplyTargets(ProgramSkillFrame frame, MouLidanSelection selection);
    bool IssueReward(ProgramSkillFrame frame, CardKind kind, int target);
    bool DrainChildren(ProgramSkillFrame frame);
    void Finish(ProgramSkillFrame frame);
    void PublishState();
}
internal interface IMouChengYuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class MouChengYuHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IMouChengYuHost>().Resolve(frame, operation);
}
internal abstract class MouChengYuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouChengYuHandler(Op);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeActionAppearance);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveMouChengYuLidanDescriptor : MouChengYuDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChengYuLidan; }
internal sealed class ResolveMouChengYuDanchiDescriptor : MouChengYuDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChengYuDanchi; }
