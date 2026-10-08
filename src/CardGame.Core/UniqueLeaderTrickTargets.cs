using System.Text.Json.Serialization;

namespace CardGame.Core;

// Qualification is captured once, after the native Use has paid its materials.
// Later designation observers do not change the original unique-target opportunity.
public sealed record UniqueLeaderTrickQualificationEvent(long CardUseFrameId, long ActionId,
    int ActorSeat, int? UniqueLargestHandSeat, int? UniqueLargestHpSeat,
    int OriginalPrimaryTargetCount, int? OriginalSinglePrimaryTargetSeat) : IGameEvent;

public sealed record UniqueLeaderTrickTargetDraft(long CardUseFrameId, long ActionId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<PromptChoice> _choices = Array.AsReadOnly(Array.Empty<PromptChoice>());
    public IReadOnlyList<int> OriginalTargetSeats
    { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices
    { get => _choices; init => _choices = Array.AsReadOnly(value.Select(DesignatedExtraTargetDraft.FreezeChoice).ToArray()); }
}

public sealed record UniqueLeaderTrickTargetOfferedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    int InstructionIndex, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    IReadOnlyList<int> OriginalTargetSeats, IReadOnlyList<int> CandidateTargetSeats) : IGameEvent;
public sealed record UniqueLeaderTrickTargetResolvedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    int InstructionIndex, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    IReadOnlyList<int> OriginalTargetSeats, IReadOnlyList<int> AddedTargetSeats,
    IReadOnlyList<int> ResultTargetSeats) : IGameEvent;

public sealed record JoinedTrickDamageBenefit(long ProgramFrameId, long CardUseFrameId, long ActionId,
    CardConversionSource Source, string GameplayHash);
public sealed record JoinedTrickDamageBenefitIssuedEvent(JoinedTrickDamageBenefit Benefit) : IGameEvent;
public sealed record ProgramJoinedTrickDamageRewardReceipt(int InstructionIndex, JoinedTrickDamageBenefit Benefit,
    int FrozenDrawCount, int DrawActual, long MovementSequenceBefore, long MovementSequenceAfter);
public sealed record JoinedTrickDamageRewardDrawIssuedEvent(long ProgramFrameId, JoinedTrickDamageBenefit Benefit,
    int FrozenDrawCount, int DrawActual, long MovementSequenceBefore, long MovementSequenceAfter) : IGameEvent;
public sealed record JoinedTrickDamageRewardResolvedEvent(long ProgramFrameId, JoinedTrickDamageBenefit Benefit,
    int FrozenDrawCount, int DrawActual) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UniqueLeaderTrickTargetDraft? UniqueLeaderTrickTargetDraft { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramJoinedTrickDamageRewardReceipt? JoinedTrickDamageRewardReceipt { get; init; }
}
public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<JoinedTrickDamageBenefit>? _joinedTrickDamageBenefits;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JoinedTrickDamageBenefit>? JoinedTrickDamageBenefits
    { get => _joinedTrickDamageBenefits; init => _joinedTrickDamageBenefits = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}

internal static class UniqueLeaderTrickTargetContract
{
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var (id, program) in programs)
        {
            var joins = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage)).ToArray();
            var rewards = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage)).ToArray();
            if ((joins.Length > 0 || rewards.Length > 0) && (joins.Length != 1 || rewards.Length != 1))
                throw new InvalidOperationException($"Invalid skill program at {id}: a joined-trick damage reward requires exactly one matching join producer and one completed reward in the same program.");
        }
    }
    internal static bool IsTargetOperation(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition or
        SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage;
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => IsTargetOperation(e.Op) || e.Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage)) return;
        var reward = t.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage);
        if (t.Window != (reward ? SkillProgramTriggerWindow.CardUseCompleted : SkillProgramTriggerWindow.CardUseTargetsFinalized) ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Subject is not null ||
            t.Optional == reward || !t.SingleActionInstance || t.IncludeResponseUses ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.EvaluateConditionAtResolution ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.UsageScope is not null || t.UsageLimit is not null ||
            t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null ||
            t.CardKinds.Count != 0 || t.CardCategories is not [SkillProgramCardCategory.InstantTrick] ||
            t.Suits.Count != 0 || t.SourceZones.Count != 0 || t.DamageOccurrence is not null || t.MovementOccurrence is not null ||
            t.OnlyDesignatedCardTargets || t.AllowNoEventTarget || t.Effects.Count != 1 ||
            t.Effects[0].Target != SkillProgramEffectTarget.Owner || t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: unique-leader trick targets require one optional finalized observer instruction, or its mandatory completed reward, for a single actual ordinary-trick action without filters or costs.");
    }
}

internal interface IUniqueLeaderTrickTargetHost
{
    SkillProgramStepOutcome OfferUniqueLargestHandTrickTargetAddition(ProgramSkillFrame frame);
    SkillProgramStepOutcome JoinUniqueLargestHpTrickTargetAndDrawAfterDamage(ProgramSkillFrame frame);
    SkillProgramStepOutcome DrawAfterJoinedTrickDamage(ProgramSkillFrame frame);
}
internal abstract class UniqueLeaderTrickOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (_, context) => context.PublicControlValue(8d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var result = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(result, r.Path); return result;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.DrawAfterJoinedTrickDamage
            ? SkillProgramTriggerWindow.CardUseCompleted : SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class OfferUniqueLargestHandTrickTargetAdditionDescriptor : UniqueLeaderTrickOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition;
    public override ISkillProgramEffectHandler Handler { get; } = new UniqueLeaderTrickHandler(SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition);
}
internal sealed class JoinUniqueLargestHpTrickTargetAndDrawAfterDamageDescriptor : UniqueLeaderTrickOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new UniqueLeaderTrickHandler(SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage);
}
internal sealed class DrawAfterJoinedTrickDamageDescriptor : UniqueLeaderTrickOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAfterJoinedTrickDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new UniqueLeaderTrickHandler(SkillProgramEffectOp.DrawAfterJoinedTrickDamage);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
}
internal sealed class UniqueLeaderTrickHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host is IUniqueLeaderTrickTargetHost typed ? operation switch
        {
            SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition => typed.OfferUniqueLargestHandTrickTargetAddition(frame),
            SkillProgramEffectOp.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage => typed.JoinUniqueLargestHpTrickTargetAndDrawAfterDamage(frame),
            SkillProgramEffectOp.DrawAfterJoinedTrickDamage => typed.DrawAfterJoinedTrickDamage(frame),
            _ => throw new InvalidOperationException("Unsupported unique-leader trick instruction.")
        } : throw new InvalidOperationException("The host cannot resolve unique-leader trick targets.");
}
