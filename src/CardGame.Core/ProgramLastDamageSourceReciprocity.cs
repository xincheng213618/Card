using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum LastDamageSourceReciprocityDirection { DrawOwner, DiscardSource }
public enum LastDamageSourceReciprocityStage { ChoosingDiscard, MovementChildren, Complete }
public sealed record LastDamageSourceReciprocityMaterial(int CardId, CardKind PrintedKind, CardLocation From, bool IsGeneralWeapon = false);

// Memory deliberately belongs to owner/skill/state, rather than one temporary
// grant instance. Losing and reacquiring the skill retains its recorded source;
// damage while the skill is unqualified does not update that memory.
public sealed record LastDamageSourceRecordedEvent(int OwnerSeat, string SkillId, string StateId,
    int SourceSeat, int? PreviousSourceSeat, long DamageFrameId, long AttackFrameId, int Amount,
    DamageNature Nature, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record LastDamageSourceReciprocityEligibleEvent(int OwnerSeat, string SkillId, string StateId,
    string SkillInstanceId, string GameplayHash, string BindingId, LastDamageSourceReciprocityDirection Direction,
    long DamageFrameId, long AttackFrameId, int SourceSeat, int TargetSeat, int Amount, DamageNature Nature,
    int RecordedSourceSeat) : IGameEvent;
public sealed record LastDamageSourceReciprocityStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, string StateId, LastDamageSourceReciprocityDirection Direction,
    long DamageWindowId, long DamageFrameId, long AttackFrameId, int SourceSeat, int TargetSeat,
    int Amount, DamageNature Nature, int ParticipantSeat, int RecordedSourceSeat) : IGameEvent;
public sealed record LastDamageSourceReciprocityMovementIssuedEvent(long FrameId, int ParticipantSeat,
    LastDamageSourceReciprocityDirection Direction, int ActualCount, long SequenceBefore,
    long SequenceAfter, long? BatchId) : IGameEvent;
public sealed record LastDamageSourceReciprocityCompletedEvent(long FrameId, int ActualCount) : IGameEvent;

public sealed record ProgramLastDamageSourceReceipt
{
    private IReadOnlyList<LastDamageSourceReciprocityMaterial> _materials = Array.AsReadOnly(Array.Empty<LastDamageSourceReciprocityMaterial>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string StateId { get; init; }
    public LastDamageSourceReciprocityDirection Direction { get; init; }
    public long DamageWindowId { get; init; }
    public long DamageFrameId { get; init; }
    public long AttackFrameId { get; init; }
    public int SourceSeat { get; init; }
    public int TargetSeat { get; init; }
    public int Amount { get; init; }
    public DamageNature Nature { get; init; }
    public int ParticipantSeat { get; init; }
    public int RecordedSourceSeat { get; init; }
    public LastDamageSourceReciprocityStage Stage { get; init; }
    public IReadOnlyList<LastDamageSourceReciprocityMaterial> EligibleMaterials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public bool MovementIssued { get; init; }
    public int ActualCount { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long? BatchId { get; init; }
    public LastDamageSourceReciprocityMaterial? PaidMaterial { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramLastDamageSourceReceipt? LastDamageSourceReciprocity { get; init; }
}

internal interface ILastDamageSourceReciprocityProgramHost
{
    SkillProgramStepOutcome LastDamageSourceReciprocity(ProgramSkillFrame frame, string stateId);
}
internal sealed class LastDamageSourceReciprocityDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LastDamageSourceReciprocity;
    public override ISkillProgramEffectHandler Handler { get; } = new LastDamageSourceReciprocityHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
public sealed class LastDamageSourceReciprocityHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LastDamageSourceReciprocity;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((ILastDamageSourceReciprocityProgramHost)host).LastDamageSourceReciprocity(frame, effect.StateId!);
}
internal static class LastDamageSourceReciprocityComposition
{
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, bool optional,
        SkillUsageScope? usageScope, int? usageLimit, SkillProgramTurnOwnerScope turnOwnerScope)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.LastDamageSourceReciprocity)) return;
        if (effects is not [{ Op: SkillProgramEffectOp.LastDamageSourceReciprocity,
                Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always }] ||
            window != SkillProgramTriggerWindow.AfterDamageApplied ||
            subject is not (SkillProgramTriggerSubject.Owner or SkillProgramTriggerSubject.DamageSource) ||
            optional || usageScope is not null || usageLimit is not null || turnOwnerScope != SkillProgramTurnOwnerScope.Own)
            throw new InvalidOperationException($"Invalid skill program at {path}: last-damage reciprocity requires one mandatory unlimited after-damage owner/source instruction.");
    }

    internal static void ValidateProgram(string path, SkillProgram program)
    {
        var triggers = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.LastDamageSourceReciprocity)).ToArray();
        foreach (var group in triggers.GroupBy(t => t.Effects.Single().StateId, StringComparer.Ordinal))
            if (group.Count() != 2 || group.Count(t => t.Subject == SkillProgramTriggerSubject.Owner) != 1 ||
                group.Count(t => t.Subject == SkillProgramTriggerSubject.DamageSource) != 1 ||
                group.Any(t => t.DamageOccurrence is not (null or SkillProgramDamageOccurrence.PerDamage) ||
                    t.DamageCardKinds.Count != 0 || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                    t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null))
                throw new InvalidOperationException($"Invalid skill program at {path}: each stable last-source state requires exactly one unconditional per-damage trigger in each direction.");
    }
}
