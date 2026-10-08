using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record RoundGainedRoundBoundaryEvent(int RoundNumber, int ActorSeat, long MovementSequence) : IGameEvent;
public sealed record RoundGainedMaterialAcquisition(int CardId, long MovementSequence, CardLocation OriginalSource);
public sealed record RoundGainedUseQualification(CardConversionSource Source, string GameplayHash, string PolicyId,
    int RoundNumber, long RoundStartMovementSequence, long CardUseFrameId, long ActionId, int ActorSeat, CardKind EffectiveKind)
{
    private readonly IReadOnlyList<RoundGainedMaterialAcquisition> _materials = Array.AsReadOnly(Array.Empty<RoundGainedMaterialAcquisition>());
    public IReadOnlyList<RoundGainedMaterialAcquisition> MaterialGains
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record RoundGainedUseQualifiedEvent(RoundGainedUseQualification Qualification) : IGameEvent;
public sealed record RoundGainedTrickTargetDraft(long CardUseFrameId, long ActionId, int InstructionIndex, RoundGainedUseQualification Qualification)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<PromptChoice> _choices = Array.AsReadOnly(Array.Empty<PromptChoice>());
    public IReadOnlyList<int> OriginalTargetSeats { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices
    { get => _choices; init => _choices = Array.AsReadOnly(value.Select(DesignatedExtraTargetDraft.FreezeChoice).ToArray()); }
}
public sealed record RoundGainedTrickTargetOfferedEvent(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, RoundGainedUseQualification Qualification, IReadOnlyList<int> OriginalTargetSeats,
    IReadOnlyList<PromptChoice> Choices) : IGameEvent;
public sealed record RoundGainedTrickTargetResolvedEvent(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, RoundGainedUseQualification Qualification, IReadOnlyList<int> OriginalTargetSeats,
    IReadOnlyList<int> AddedTargetSeats, IReadOnlyList<int> RemovedTargetSeats, IReadOnlyList<int> ResultTargetSeats) : IGameEvent;
public sealed record ProgramRoundGainedEquipmentDrawReceipt(int InstructionIndex, RoundGainedUseQualification Qualification,
    int DrawActual, long MovementSequenceBefore, long MovementSequenceAfter)
{
    public int DrawRoundNumber { get; init; }
    private readonly IReadOnlyList<int> _drawn = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> DrawnCardIds { get => _drawn; init => _drawn = Array.AsReadOnly(value.ToArray()); }
}
public sealed record RoundGainedEquipmentDrawIssuedEvent(long ProgramFrameId, CardConversionSource Source,
    RoundGainedUseQualification Qualification, int DrawActual, long MovementSequenceBefore, long MovementSequenceAfter,
    IReadOnlyList<int> DrawnCardIds) : IGameEvent
{ public int DrawRoundNumber { get; init; } }
public sealed record RoundGainedEquipmentDrawResolvedEvent(long ProgramFrameId, CardConversionSource Source,
    RoundGainedUseQualification Qualification, int DrawActual) : IGameEvent;

public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<RoundGainedUseQualification>? _roundGainedUseQualifications;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RoundGainedUseQualification>? RoundGainedUseQualifications
    { get => _roundGainedUseQualifications; init => _roundGainedUseQualifications = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RoundGainedTrickTargetDraft? RoundGainedTrickTargetDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRoundGainedEquipmentDrawReceipt? RoundGainedEquipmentDrawReceipt { get; init; }
}

internal static class RoundGainedSourceContract
{
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var (id, program) in programs)
        {
            var policies = program.CardPolicies.Where(p => p.Kind == SkillProgramCardPolicyKind.RoundGainedOtherSourceUse).ToArray();
            var targets = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget)).ToArray();
            var draws = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse)).ToArray();
            if (policies.Length + targets.Length + draws.Length == 0) continue;
            if (policies is not [var policy] || targets.Length != 1 || draws.Length != 1 ||
                policy.CardKinds.Count != 0 || policy.RequiredCardKinds.Count != 0 || policy.Value != 0 ||
                policy.InputSuit is not null || policy.OutputSuit is not null || policy.FactionId is not null || policy.OwnerRole is not null ||
                policy.DiscardCost != 0 || policy.ProviderDrawCount != 0 || policy.Condition.Kind != SkillProgramConditionKind.Always)
                throw new InvalidOperationException($"Invalid skill program at {id}: round-gained source use requires exactly one unconditional all-card policy, one trick target instruction and one equipment draw instruction in the same program.");
        }
    }
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget or SkillProgramEffectOp.DrawForRoundGainedEquipmentUse;
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => IsOperation(e.Op))) return;
        var equipment = t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse);
        if (t.Window != (equipment ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.CardUseTargetsFinalized) ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Subject is not null || !t.Optional ||
            t.SingleActionInstance == equipment || t.IncludeResponseUses || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.EvaluateConditionAtResolution || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null ||
            t.MarkerCost is not null || t.ChoiceGroup is not null || t.CardKinds.Count != 0 ||
            !(equipment ? t.CardCategories is [SkillProgramCardCategory.Equipment] : t.CardCategories is [SkillProgramCardCategory.InstantTrick]) ||
            t.Suits.Count != 0 || t.SourceZones.Count != 0 || t.DamageOccurrence is not null || t.MovementOccurrence is not null ||
            t.OnlyDesignatedCardTargets || t.AllowNoEventTarget || t.Effects.Count != 1 ||
            t.Effects[0].Target != SkillProgramEffectTarget.Owner || t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: round-gained source use requires a sole optional actual-actor ordinary-trick finalized instruction or equipment committed instruction without costs or filters.");
    }
}
internal interface IRoundGainedSourceUseHost
{
    SkillProgramStepOutcome AdjustOneRoundGainedOrdinaryTrickTarget(ProgramSkillFrame frame);
    SkillProgramStepOutcome DrawForRoundGainedEquipmentUse(ProgramSkillFrame frame);
}
internal abstract class RoundGainedSourceOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.RoundGainedSourceUseValue(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var result = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(result, r.Path); return result;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.DrawForRoundGainedEquipmentUse ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class AdjustOneRoundGainedOrdinaryTrickTargetDescriptor : RoundGainedSourceOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new RoundGainedSourceOperationHandler(SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget);
}
internal sealed class DrawForRoundGainedEquipmentUseDescriptor : RoundGainedSourceOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawForRoundGainedEquipmentUse;
    public override ISkillProgramEffectHandler Handler { get; } = new RoundGainedSourceOperationHandler(SkillProgramEffectOp.DrawForRoundGainedEquipmentUse);
}
internal sealed class RoundGainedSourceOperationHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host is IRoundGainedSourceUseHost typed ? operation switch
        {
            SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget => typed.AdjustOneRoundGainedOrdinaryTrickTarget(frame),
            SkillProgramEffectOp.DrawForRoundGainedEquipmentUse => typed.DrawForRoundGainedEquipmentUse(frame),
            _ => throw new InvalidOperationException("Unsupported round-gained source instruction.")
        } : throw new InvalidOperationException("The host cannot resolve round-gained source use.");
}
