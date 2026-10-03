namespace CardGame.Core;

public sealed record ProgramHpDamageShieldDraft(int InstructionIndex, PlayerMarkerKind Marker,
    int HpBefore, int Maximum, IReadOnlyList<int> CandidateSeats, int TurnNumber, int TurnOwnerSeat);

public sealed record ProgramHpDamageShieldReceipt(int InstructionIndex, PlayerMarkerKind Marker,
    int OwnerSeat, string SkillId, string BindingId, string SkillInstanceId, string GameplayHash,
    int HpBefore, int HpAfter, int ActualLost, IReadOnlyList<int> TargetSeats,
    int TurnNumber, int TurnOwnerSeat, bool Granted = false);

/// <summary>A paid, independent protection. Its source is evidence, not ongoing eligibility.</summary>
public sealed record OneUseDamageShield(long ProducerFrameId, int InstructionIndex,
    int OwnerSeat, int TargetSeat, PlayerMarkerKind Marker, string SkillId,
    string BindingId, string SkillInstanceId, string GameplayHash);

public sealed record OneUseDamageShieldGrantedEvent(OneUseDamageShield Shield) : IGameEvent;
public sealed record OneUseDamageShieldConsumedEvent(long AttackResolutionId,
    OneUseDamageShield Shield, int PreventedAmount) : IGameEvent;

internal interface IHpDamageShieldProgramHost
{
    SkillProgramStepOutcome PayHpToGrantOneUseDamageShield(ProgramSkillFrame frame, PlayerMarkerKind marker);
}

internal sealed class PayHpToGrantOneUseDamageShieldDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayHpToGrantOneUseDamageShield;
    public override ISkillProgramEffectHandler Handler { get; } = new PayHpToGrantOneUseDamageShieldHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) =>
        {
            // One affordable HP buys one future full prevention. Prices only a
            // public protection opportunity; no hand or deck identities enter.
            context.LoseHp(new(SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.Owner, 1, effect.Condition));
            context.PublicControlValue(context.HpDamageShieldTargetValue ?? -1000d);
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "marker", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), marker: reader.RequiredEnum<PlayerMarkerKind>("marker"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];

    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, bool initialTarget, int initialTargetSetMaximum)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.PayHpToGrantOneUseDamageShield)) return;
        if (window != SkillProgramTriggerWindow.TurnEnding || effects.Count != 1 || initialTarget ||
            initialTargetSetMaximum != 0 || effects[0].Target != SkillProgramEffectTarget.Owner ||
            effects[0].Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: HP-paid shields require one standalone owner turn-ending instruction.");
    }
}

public sealed class PayHpToGrantOneUseDamageShieldHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PayHpToGrantOneUseDamageShield;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        ((IHpDamageShieldProgramHost)host).PayHpToGrantOneUseDamageShield(frame, effect.Marker!.Value);
}
