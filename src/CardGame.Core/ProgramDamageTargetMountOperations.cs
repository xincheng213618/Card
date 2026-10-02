namespace CardGame.Core;

/// <summary>Private finite payment draft. An actual record, never an intended card list, owns the claim tail.</summary>
public sealed record ProgramDamageTargetMount(int InstructionIndex, int OwnerSeat, int TargetSeat,
    long DamageWindowId, long DamageFrameId, long CardUseFrameId, long CardActionId,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash,
    ProgramDamageTargetMountReceipt? Receipt = null, bool ClaimIssued = false, long? ClaimMovementSequence = null);
public sealed record ProgramDamageTargetMountReceipt(long MovementSequence, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, string Reason, bool IsMount);

internal interface IDamageTargetMountProgramHost
{
    SkillProgramStepOutcome DiscardDamageTargetAndClaimMount(ProgramSkillFrame frame);
}
internal sealed class DiscardDamageTargetAndClaimMountDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDamageTargetAndClaimMount;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardDamageTargetAndClaimMountHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.DamageTargetMountValue());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("The damage-target mount operation is owner-only.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
public sealed class DiscardDamageTargetAndClaimMountHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDamageTargetAndClaimMount;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h is IDamageTargetMountProgramHost host ? host.DiscardDamageTargetAndClaimMount(f) :
            throw new NotSupportedException("Actual damage-target mount payment is unavailable.");
}
