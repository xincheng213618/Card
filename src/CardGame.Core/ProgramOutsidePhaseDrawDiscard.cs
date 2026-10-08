using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record NativeDrawMaterial(int CardId, CardKind PrintedKind, long MovementSequence);
public sealed record NativeDrawInvocationProof
{
    private IReadOnlyList<NativeDrawMaterial> _materials = Array.AsReadOnly(Array.Empty<NativeDrawMaterial>());
    public long InvocationId { get; init; }
    public long? ParentFrameId { get; init; }
    public int OwnerSeat { get; init; }
    public int RequestedCount { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int PhaseActorSeat { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public CardConversionSource? DirectProducer { get; init; }
    public required CardMoveReason Reason { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long LastBatchId { get; init; }
    public IReadOnlyList<NativeDrawMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public int ActualCount => Materials.Count;
}
public sealed record NativeDrawInvocationRecordedEvent(long InvocationId, int OwnerSeat, int Requested,
    int ActualCount, int TurnNumber, int ActualTurnOwnerSeat, int PhaseActorSeat,
    ActualDiscardRecoveryPhaseKey? Phase, long? ParentFrameId, CardConversionSource? DirectProducer,
    CardMoveReason Reason, long SequenceBefore, long SequenceAfter, long LastBatchId) : IGameEvent;

public enum OutsidePhaseDrawDiscardStage { Choosing, MovementChildren, Complete }
public sealed record OutsidePhaseDiscardMaterial(int CardId, CardKind PrintedKind, CardLocation From,
    int Slot, bool IsGeneralWeapon);
public sealed record ProgramOutsidePhaseDrawDiscardReceipt
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<OutsidePhaseDiscardMaterial> _materials = Array.AsReadOnly(Array.Empty<OutsidePhaseDiscardMaterial>());
    private IReadOnlyList<DiscardRecoveryEntity> _original = Array.AsReadOnly(Array.Empty<DiscardRecoveryEntity>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public SkillProgramEffectOp Op { get; init; }
    public long WindowFrameId { get; init; }
    public long OriginalBatchId { get; init; }
    public int OriginalMovementIndex { get; init; }
    public ActualDiscardRecoveryPhaseKey? OriginalPhase { get; init; }
    public NativeDrawInvocationProof? OriginalDraw { get; init; }
    public IReadOnlyList<DiscardRecoveryEntity> OriginalDiscards
    { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public OutsidePhaseDrawDiscardStage Stage { get; init; }
    public IReadOnlyList<int> EligibleTargets
    { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<OutsidePhaseDiscardMaterial> EligibleMaterials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public int? ParticipantSeat { get; init; }
    public OutsidePhaseDiscardMaterial? PaidMaterial { get; init; }
    public bool MovementIssued { get; init; }
    public int ActualCount { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long? BatchId { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOutsidePhaseDrawDiscardReceipt? OutsidePhaseDrawDiscard { get; init; }
}
public sealed record OutsidePhaseDrawDiscardStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, SkillProgramEffectOp Op, long WindowFrameId, long OriginalBatchId,
    long? NativeDrawInvocationId) : IGameEvent;
public sealed record OutsidePhaseDrawDiscardMovementIssuedEvent(long FrameId, SkillProgramEffectOp Op,
    int ParticipantSeat, int ActualCount, long SequenceBefore, long SequenceAfter, long? BatchId,
    OutsidePhaseDiscardMaterial? PaidMaterial) : IGameEvent;
public sealed record OutsidePhaseDrawDiscardCompletedEvent(long FrameId, int ActualCount) : IGameEvent;

internal interface IOutsidePhaseDrawDiscardProgramHost
{
    SkillProgramStepOutcome OutsidePhaseDrawDiscard(ProgramSkillFrame frame, SkillProgramEffectOp op);
}
internal abstract class OutsidePhaseDrawDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw
        ? new DrawAfterActualOutsideDrawHandler() : new DiscardAfterActualOutsideDiscardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw
            ? SkillProgramTriggerWindow.CardsGained : SkillProgramTriggerWindow.DiscardPileReceived)];
}
internal sealed class DrawAfterActualOutsideDrawDescriptor : OutsidePhaseDrawDiscardDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAfterActualOutsideDraw; }
internal sealed class DiscardAfterActualOutsideDiscardDescriptor : OutsidePhaseDrawDiscardDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardAfterActualOutsideDiscard; }
public abstract class OutsidePhaseDrawDiscardHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IOutsidePhaseDrawDiscardProgramHost)host).OutsidePhaseDrawDiscard(frame, op);
}
public sealed class DrawAfterActualOutsideDrawHandler : OutsidePhaseDrawDiscardHandler
{ public DrawAfterActualOutsideDrawHandler() : base(SkillProgramEffectOp.DrawAfterActualOutsideDraw) { } }
public sealed class DiscardAfterActualOutsideDiscardHandler : OutsidePhaseDrawDiscardHandler
{ public DiscardAfterActualOutsideDiscardHandler() : base(SkillProgramEffectOp.DiscardAfterActualOutsideDiscard) { } }
internal static class OutsidePhaseDrawDiscardComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.DrawAfterActualOutsideDraw or SkillProgramEffectOp.DiscardAfterActualOutsideDiscard;
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op))))
            throw new InvalidOperationException($"Invalid skill program at {path}: outside-phase draw/discard operations require their real movement triggers.");
        foreach (var t in program.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))))
        {
            if (t.Effects.Count != 1 || t.Effects[0] is not { Target: SkillProgramEffectTarget.Owner,
                    Amount: 1, Condition.Kind: SkillProgramConditionKind.Always } ||
                t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.UsageScope is not null || t.UsageLimit is not null ||
                t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.MarkerCost is not null || t.ChoiceGroup is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null ||
                t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.IgnoreOwnSkillMovements ||
                t.Suits.Count != 0 || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.GainPhaseQualification is not null)
                throw new InvalidOperationException($"Invalid skill program at {path}: outside-phase draw/discard requires one unconditional mandatory unlimited owner/batch operation.");
            var draw = t.Effects[0].Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw;
            if (draw ? t.Window != SkillProgramTriggerWindow.CardsGained || !t.DestinationZones.SequenceEqual([CardZoneKind.Hand]) || t.MovementDiscardOnly :
                t.Window != SkillProgramTriggerWindow.DiscardPileReceived || !t.MovementDiscardOnly ||
                t.DiscardOwnerScope != SkillProgramDiscardOwnerScope.Own ||
                !t.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]))
                throw new InvalidOperationException($"Invalid skill program at {path}: the outside-phase operation lost its exact native draw or owned HEJ discard boundary.");
        }
    }
}
