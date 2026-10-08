namespace CardGame.Core;

/// <summary>One already accepted use owns its cancellation and actual material transfer.</summary>
public sealed record OverflowTargetCancellationReceipt(
    int InstructionIndex, CardConversionSource Source, string GameplayHash,
    long WindowFrameId, long CardUseFrameId, long ActionId, int ProviderSeat,
    CardKind EffectiveKind, CardActionContext ActionBeforeCancellation,
    int HandCount, int HandLimit, int? RecipientSeat,
    long SequenceBefore, long SequenceAfter, long? MovementBatchId, bool MovementIssued = false)
{
    private readonly IReadOnlyList<int> _originalTargets = Array.Empty<int>();
    private readonly IReadOnlyList<int> _beforeTargets = Array.Empty<int>();
    private readonly IReadOnlyList<int> _canceledTargets = Array.Empty<int>();
    private readonly IReadOnlyList<int> _resultTargets = Array.Empty<int>();
    private readonly IReadOnlyList<int> _materialIds = Array.Empty<int>();
    public IReadOnlyList<int> OriginalTargetSeats { get => _originalTargets; init => _originalTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> BeforeTargetSeats { get => _beforeTargets; init => _beforeTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> CanceledPrimaryTargetSeats { get => _canceledTargets; init => _canceledTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> ResultTargetSeats { get => _resultTargets; init => _resultTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> MaterialCardIds { get => _materialIds; init => _materialIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed record OverflowUseTargetsCanceledEvent(long ProgramFrameId,
    OverflowTargetCancellationReceipt Receipt) : IGameEvent;
public sealed record OverflowUseMaterialTransferIssuedEvent(long ProgramFrameId, long CardUseFrameId,
    long ActionId, int RecipientSeat, int ActualCount, long SequenceBefore, long SequenceAfter, long? MovementBatchId) : IGameEvent;
public sealed record OverflowTargetCancellationCompletedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId) : IGameEvent;

internal static class OverflowTargetCancellationContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit)) return;
        if (trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || trigger.Subject is not null || trigger.Optional ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !trigger.SingleActionInstance || trigger.IncludeResponseUses ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.EvaluateConditionAtResolution ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null ||
            trigger.MarkerCost is not null || trigger.ChoiceGroup is not null || trigger.SourceSkillId is not null ||
            trigger.SourceViewAsId is not null || trigger.RequireNoCardConversion is not null || trigger.GainPhaseQualification is not null ||
            trigger.IgnoreOwnSkillMovements || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 ||
            trigger.Suits.Count != 0 || trigger.SourceZones.Count != 0 || trigger.DamageOccurrence is not null ||
            trigger.MovementOccurrence is not null || trigger.OnlyDesignatedCardTargets || trigger.AllowNoEventTarget ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"Invalid skill program at {path}: overflow target cancellation requires one mandatory actor CardUseTargetsFinalized instruction for one actual use, without card filters, response uses or usage costs.");
    }
}

internal interface IOverflowTargetCancellationHost
{
    SkillProgramStepOutcome BeginOverflowTargetCancellation(ProgramSkillFrame frame);
}

internal sealed class CancelOtherCurrentUseTargetsIfOverHandLimitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit;
    public override ISkillProgramEffectHandler Handler { get; } = new OverflowTargetCancellationHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
}

internal sealed class OverflowTargetCancellationHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host is IOverflowTargetCancellationHost cancellation ? cancellation.BeginOverflowTargetCancellation(frame) :
            throw new InvalidOperationException("The program host does not support overflow target cancellation.");
}
