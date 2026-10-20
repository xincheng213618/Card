using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramDamageCardColorHealthReceipt(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, long BeforeDamageWindowId, long AttackResolutionId,
    long? CardUseFrameId, long? CardActionId, int? PhysicalCardId, int OwnerSeat, int? DamageSourceSeat,
    int Amount, CardKind EffectiveCardKind, Suit EffectiveSuit, bool IsRed, int HpBefore, int MaxHpBefore, int MaxHpAfter);
public sealed record ProgramDamageCardColorHealthPaidEvent(ProgramDamageCardColorHealthReceipt Receipt) : IGameEvent;
public sealed record ProgramDamageCardColorHealthCompletedEvent(long ProgramFrameId) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramDamageCardColorHealthReceipt? DamageCardColorHealth { get; init; }
}

internal interface IDamageCardColorHealthHost
{
    SkillProgramStepOutcome ResolveDamageCardColorHealth(ProgramSkillFrame frame);
}

internal sealed class ResolveDamageCardColorHealthDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDamageCardColorHealth;
    public override ISkillProgramEffectHandler Handler { get; } = new DamageCardColorHealthHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    internal static bool IsTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveDamageCardColorHealth }];
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDamageCardColorHealth)) return;
        if (!IsTrigger(trigger) || trigger.Window != SkillProgramTriggerWindow.BeforeDamageApplied ||
            trigger.Subject != SkillProgramTriggerSubject.DamageTarget || trigger.Optional ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.DamageOccurrence is not null ||
            trigger.UsageScope is not null)
            throw new InvalidOperationException($"Invalid skill program at {path}: damage-card color health requires one mandatory unconditional damageTarget before-damage instruction.");
    }
}

internal sealed class DamageCardColorHealthHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDamageCardColorHealth;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IDamageCardColorHealthHost>().ResolveDamageCardColorHealth(frame);
}
