namespace CardGame.Core;

public enum ProgramDamageTargetObtainStage { SelectingCard, ObtainChildren, BranchReady, DrawChildren, SelectingDuelTarget, DuelIssued, Complete }
/// <summary>An owning compound. Only the actual into-Hand movement can produce its benefit receipt.</summary>
public sealed record ProgramDamageTargetObtain(int InstructionIndex, int OwnerSeat, int VictimSeat,
    long DamageWindowId, long DamageFrameId, long OuterCardUseFrameId, long OuterActionId,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash,
    ProgramDamageTargetObtainStage Stage = ProgramDamageTargetObtainStage.SelectingCard,
    ProgramDamageTargetObtainReceipt? Receipt = null, long? ObtainMovementSequence = null);
public sealed record ProgramDamageTargetObtainReceipt(long MovementSequence, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, string Reason, bool IsEquipment);
public sealed record ProgramDamageTargetDuelOrigin(long ParentProgramFrameId, int InstructionIndex,
    long ReceiptSequence, long OuterCardUseFrameId, long DamageWindowId, long DamageFrameId, long OuterActionId,
    int InitialActorSeat, int VictimChooserSeat, int InitialTargetSeat,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, int TurnNumber,
    bool AttackStarted = false);
internal interface IDamageTargetObtainProgramHost
{
    SkillProgramStepOutcome ObtainDamageTargetCardAndResolveCategory(ProgramSkillFrame frame);
}
internal sealed class ObtainDamageTargetCardAndResolveCategoryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDamageTargetCardAndResolveCategory;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainDamageTargetCardAndResolveCategoryHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.DamageTargetObtainValue());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("Damage-target obtain is owner-only.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
public sealed class ObtainDamageTargetCardAndResolveCategoryHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDamageTargetCardAndResolveCategory;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host is IDamageTargetObtainProgramHost h ? h.ObtainDamageTargetCardAndResolveCategory(f) :
            throw new NotSupportedException("Actual damage-target obtain is unavailable.");
}
