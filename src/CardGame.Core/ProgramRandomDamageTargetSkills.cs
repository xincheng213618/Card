using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The frozen random choice and independent grant paid by one native Slash damage candidate.</summary>
public sealed record ProgramRandomDamageTargetSkillReceipt
{
    private IReadOnlyList<string> _candidates = Array.AsReadOnly(Array.Empty<string>());
    public long ProgramFrameId { get; init; }
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public long DamageWindowId { get; init; }
    public long DamageFrameId { get; init; }
    public long CardUseFrameId { get; init; }
    public long CardActionId { get; init; }
    public int TargetSeat { get; init; }
    public int DamageAmount { get; init; }
    public DamageNature Nature { get; init; }
    // Trusted rule evidence only. Player views never project this pool.
    public IReadOnlyList<string> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public required string SelectedSkillId { get; init; }
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    public required SkillGrant GrantedGrant { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRandomDamageTargetSkillReceipt? RandomDamageTargetSkill { get; init; }
}

public sealed record ProgramRandomDamageTargetSkillAcquiredEvent(ProgramRandomDamageTargetSkillReceipt Receipt) : IGameEvent;
public sealed record ProgramRandomDamageTargetSkillCompletedEvent(long ProgramFrameId) : IGameEvent;

internal interface IRandomDamageTargetSkillHost
{
    SkillProgramStepOutcome AcquireRandomDamageTargetSkill(ProgramSkillFrame frame);
}

internal sealed class AcquireRandomDamageTargetSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AcquireRandomDamageTargetSkill;
    public override ISkillProgramEffectHandler Handler { get; } = new AcquireRandomDamageTargetSkillHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnSkills, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];

    internal static bool IsTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.AcquireRandomDamageTargetSkill }];

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AcquireRandomDamageTargetSkill)) return;
        if (!IsTrigger(trigger) || trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied ||
            trigger.Subject != SkillProgramTriggerSubject.DamageSource || trigger.Optional ||
            trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.UsageScope is not null ||
            trigger.Effects[0] is not { Target: SkillProgramEffectTarget.Owner, TargetReference: null,
                Condition.Kind: SkillProgramConditionKind.Always })
            throw new InvalidOperationException($"Invalid skill program at {path}: random damage-target skill acquisition requires one mandatory, unconditional owner instruction on a perDamage damageSource binding.");
    }
}

public sealed class AcquireRandomDamageTargetSkillHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AcquireRandomDamageTargetSkill;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IRandomDamageTargetSkillHost>().AcquireRandomDamageTargetSkill(frame);
}
