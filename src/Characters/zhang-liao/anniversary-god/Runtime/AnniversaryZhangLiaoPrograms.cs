using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryZhangLiaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryZhangLiaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryZhangLiaoHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindows(op switch
    {
        SkillProgramEffectOp.ResolveAnniversaryZhangLiaoCuixi => [SkillProgramTriggerWindow.OtherActualTurnStarted],
        SkillProgramEffectOp.ResolveAnniversaryZhangLiaoDuorui => [SkillProgramTriggerWindow.BeforeDamageApplied],
        _ => [SkillProgramTriggerWindow.DrawPhaseStarting, SkillProgramTriggerWindow.DiscardPhaseEnded]
    })];
}
internal sealed class AnniversaryCuixiDescriptor() : AnniversaryZhangLiaoDescriptor(SkillProgramEffectOp.ResolveAnniversaryZhangLiaoCuixi);
internal sealed class AnniversaryDuoruiDescriptor() : AnniversaryZhangLiaoDescriptor(SkillProgramEffectOp.ResolveAnniversaryZhangLiaoDuorui);
internal sealed class AnniversaryZhitiDescriptor() : AnniversaryZhangLiaoDescriptor(SkillProgramEffectOp.ResolveAnniversaryZhangLiaoZhiti);
internal sealed class AnniversaryZhangLiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryZhangLiaoHost>().Resolve(f, op);
}
internal static class AnniversaryZhangLiaoComposition
{
    internal static bool Owns(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversaryZhangLiaoCuixi or SkillProgramEffectOp.ResolveAnniversaryZhangLiaoDuorui or SkillProgramEffectOp.ResolveAnniversaryZhangLiaoZhiti;
    [ProgramCatalogValidation(40750)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => Owns(e.Op))) return;
        static bool E(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null;
        var valid = p.Id switch
        {
            "anniversary:cuixi" => p.Triggers is [{ Id: "actual-foreign-turn", Window: SkillProgramTriggerWindow.OtherActualTurnStarted, Subject: SkillProgramTriggerSubject.Owner, Optional: true } t] && E(t, SkillProgramEffectOp.ResolveAnniversaryZhangLiaoCuixi),
            "anniversary:duorui" => p.Triggers is [{ Id: "damage-abolish", Window: SkillProgramTriggerWindow.BeforeDamageApplied, Subject: SkillProgramTriggerSubject.DamageSource, Optional: false } t] && E(t, SkillProgramEffectOp.ResolveAnniversaryZhangLiaoDuorui),
            "anniversary:zhiti" => p.Triggers.Count == 2 && p.Triggers.All(t => t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional && E(t, SkillProgramEffectOp.ResolveAnniversaryZhangLiaoZhiti) && (t.Id == "abolished-draw" && t.Window == SkillProgramTriggerWindow.DrawPhaseStarting || t.Id == "abolished-discard" && t.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving)),
            _ => false
        };
        if (!valid || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": anniversary Zhang Liao requires the formal Cuixi foreign start, damage-source temporary slot abolition and native Zhiti Draw/Discard boundaries.");
    }
}
public enum AnniversaryZhangLiaoStage { Payment, PaymentChildren, VictimChoice, Drawing, DrawChildren, Damage, SlotChoice, SlotChildren, DiscardChoice, DiscardChildren, Finished }
public sealed record AnniversaryZhangLiaoPayment(int Ordinal,long Before,long After)
{
    private readonly IReadOnlyList<CardMovementRecord> _movements=[];
    public IReadOnlyList<CardMovementRecord> Movements { get=>_movements;init=>_movements=Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryZhangLiaoReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, int Target)
{
    public AnniversaryZhangLiaoStage Stage { get; init; }
    public int Count { get; init; }
    public int PaidCount { get; init; }
    public int Branch { get; init; }
    public EquipmentSlot? Slot { get; init; }
    public int CapacityBefore { get; init; }
    public long PhaseToken { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<CardMovementRecord> _paid = [];
    public IReadOnlyList<CardMovementRecord> Paid { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<AnniversaryZhangLiaoPayment> _payments=[];
    public IReadOnlyList<AnniversaryZhangLiaoPayment> Payments { get=>_payments;init=>_payments=Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryZhangLiaoReceipt? AnniversaryZhangLiao { get; init; } }
public sealed record AnniversaryZhangLiaoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, int ActualTurn, int Target, int Count) : IGameEvent;
public sealed record AnniversaryZhangLiaoPaidEvent(long FrameId, int Ordinal, long Before, long After, int Count) : IGameEvent;
public sealed record AnniversaryCuixiBranchEvent(long FrameId, CardConversionSource Source, int ActualTurn, int Target, int Branch) : IGameEvent;
public sealed record AnniversaryDuoruiLeaseEvent(long FrameId, CardConversionSource Source, int Target, EquipmentSlot Slot, int CapacityBefore, long PhaseToken) : IGameEvent;
public sealed record AnniversaryDuoruiExpiredEvent(long LeaseFrameId, int Target, EquipmentSlot Slot, long PhaseToken, string Reason) : IGameEvent;
public sealed record AnniversaryZhangLiaoCompletedEvent(long FrameId) : IGameEvent;
