using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WuZishengMovementSource(CardConversionSource Source, string GameplayHash);
public sealed record WuZishengMovementMaterial(int CardId, CardKind PrintedKind, CardLocation OriginalFrom,
    int TerminalSequence, bool Discard, long? DiscardInvocationId = null);
public sealed record WuZishengMovementQualification(WuZishengMovementSource Source, long BatchId, int VictimSeat)
{
    private readonly IReadOnlyList<WuZishengMovementMaterial> _materials = Array.AsReadOnly(Array.Empty<WuZishengMovementMaterial>());
    public IReadOnlyList<WuZishengMovementMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardMovementBatchContext
{
    private readonly IReadOnlyList<WuZishengMovementQualification>? _wuZisheng;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WuZishengMovementQualification>? WuZisheng
    { get => _wuZisheng; init => _wuZisheng = value is null ? null : Array.AsReadOnly(value.Select(q => q with { Materials = q.Materials }).ToArray()); }
}
public sealed record WuZishengDiscardAdmittedEvent(long InvocationId, long? ParentFrameId, int TurnNumber,
    int InitiatorSeat, int ExecutorSeat, int SequenceBefore, CardMoveReason Reason,
    WuZishengMovementSource Source) : IGameEvent;
public sealed record WuZishengDiscardMaterialAdmittedEvent(long InvocationId, int Index, int CardId,
    CardKind PrintedKind, CardLocation From, long? ProducerFrameId, int? InstructionIndex) : IGameEvent;
public sealed record WuZishengDiscardEntryBoundEvent(long InvocationId, int MaterialIndex, long BatchId,
    int MovementSequence, long? ProducerFrameId) : IGameEvent;
public sealed record WuZishengDiscardClosedEvent(long InvocationId, int SequenceAfter) : IGameEvent;
public sealed record WuZishengMovementQualifiedEvent(long BatchId, WuZishengMovementQualification Qualification) : IGameEvent;
public sealed record WuZishengMovementReceipt(long ParentWindowId, WuZishengMovementQualification Qualification,
    int RequestedDamage, int VictimHpBefore, bool Issued)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuZishengSuspendedDying? SuspendedDying { get; init; }
}
// Scalar hashes preserve the original native cursor without introducing another
// executable Dying object or a mutable sidecar indexed by its resolution id.
public sealed record WuZishengSuspendedDying(long DyingFrameId, long ParentFrameId, string DyingHash,
    string ParentHash, long? AttackOwnerFrameId, string? AttackOwnerHash);
public sealed record WuZishengSuspendedDyingIssuedEvent(long FrameId, WuZishengSuspendedDying Suspension) : IGameEvent;
public sealed record WuZishengSuspendedDyingReturnedEvent(long FrameId, long DyingFrameId, long? AttackOwnerFrameId) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuZishengMovementReceipt? WuZishengMovement { get; init; }
}
public sealed record WuZishengMovementDamageIssuedEvent(long FrameId, long ParentWindowId, long BatchId,
    CardConversionSource Source, string GameplayHash, int VictimSeat, int CardCount, int HpBefore, int RequestedDamage) : IGameEvent;
public sealed record WuZishengMovementCompletedEvent(long FrameId, long BatchId, int VictimSeat, int RequestedDamage) : IGameEvent;

internal interface IWuZhangFeiMovementHost
{ SkillProgramStepOutcome ResolveMovementDamage(ProgramSkillFrame frame); }
internal sealed class ResolveWuZhangFeiMovementDamageDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        (effect, context) => context.WuZishengMovementDamagePrior(effect));
    public override ISkillProgramEffectHandler Handler { get; } = new WuZhangFeiMovementHandler();
}
internal sealed class WuZhangFeiMovementHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWuZhangFeiMovementHost>().ResolveMovementDamage(frame);
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void WuZishengMovementDamagePrior(SkillProgramEffect effect) => Damage(new(SkillProgramEffectOp.Damage,
        SkillProgramEffectTarget.SelectedTarget, Math.Max(0, Math.Min(_publicContext.EventMovedCardCount ?? 1,
            _publicContext.SelectedTarget?.Hp ?? 1)), effect.Condition));
}
