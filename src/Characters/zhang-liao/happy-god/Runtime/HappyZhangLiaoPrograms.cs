using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyZhangLiaoHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class HappyZhangLiaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new HappyZhangLiaoHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindows(op switch
    {
        SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui => [SkillProgramTriggerWindow.PlayPhaseStarting],
        SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage => [SkillProgramTriggerWindow.BeforeDamageApplied, SkillProgramTriggerWindow.AfterDamageApplied],
        _ => [SkillProgramTriggerWindow.DrawPhaseStarting, SkillProgramTriggerWindow.DiscardPhaseEnded]
    })];
}
internal sealed class HappyDuoruiDescriptor() : HappyZhangLiaoDescriptor(SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui);
internal sealed class HappyDuoruiDamageDescriptor() : HappyZhangLiaoDescriptor(SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage);
internal sealed class HappyZhitiDescriptor() : HappyZhangLiaoDescriptor(SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti);
internal sealed class HappyZhangLiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IHappyZhangLiaoHost>().Begin(f, op);
}
internal static class HappyZhangLiaoComposition
{
    internal static bool Own(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui or SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage or SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti;
    [ProgramCatalogValidation(40980)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => Own(e.Op))) return;
        static bool E(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.MarkerCost is null && t.UsageLimit is null && t.Effects is [{ Target:SkillProgramEffectTarget.Owner, TargetReference:null, Condition.Kind:SkillProgramConditionKind.Always } e] && e.Op == op;
        bool valid = p.Id switch
        {
            "happy:duorui" => p.Triggers.Count == 3 && p.Triggers.All(t => t.Id switch
            {
                "take-hand" => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional && E(t,SkillProgramEffectOp.ResolveHappyZhangLiaoDuorui),
                "colored-damage" => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.DamageSource && !t.Optional && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && E(t,SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage),
                "restore-source" => t.Window == SkillProgramTriggerWindow.BeforeDamageApplied && t.Subject == SkillProgramTriggerSubject.DamageTarget && !t.Optional && E(t,SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage), _ => false
            }),
            "happy:zhiti" => p.Triggers.Count == 2 && p.Triggers.All(t => !t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && E(t,SkillProgramEffectOp.ResolveHappyZhangLiaoZhiti) && (t.Id == "wounded-slots-draw" && t.Window == SkillProgramTriggerWindow.DrawPhaseStarting || t.Id == "wounded-slots-discard" && t.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving)),
            _ => false
        };
        if (!valid || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Happy Zhang Liao requires his formal take/color/damage restoration and wounded-plus-slots thresholds.");
    }
}
public enum HappyZlStage { Target, Hand, GainChildren, SlotChildren, Restoring, RestoreChildren, Drawing, DrawChildren, Discard, DiscardChildren, Finished }
public sealed record HappyZlPayment(int Ordinal,long Before,long After)
{
    private readonly IReadOnlyList<CardMovementRecord> _moves=[];
    public IReadOnlyList<CardMovementRecord> Moves { get=>_moves; init=>_moves=Array.AsReadOnly(value.ToArray()); }
}
public sealed record HappyZlReceipt(CardConversionSource Source,string Hash,SkillProgramEffectOp Op,long Parent,int Turn)
{
    public HappyZlStage Stage {get;init;}
    public int Target {get;init;} = -1;
    public int Count {get;init;}
    public int Cursor {get;init;}
    public int Restored {get;init;}
    public int Card {get;init;}
    public bool? Color {get;init;}
    public EquipmentSlot? Slot {get;init;}
    public int Capacity {get;init;}
    public uint RandomBefore {get;init;}
    public uint RandomAfter {get;init;}
    public NativeDrawOutcome? Draw {get;init;}
    private readonly IReadOnlyList<int> _view=[];
    public IReadOnlyList<int> View {get=>_view;init=>_view=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<EquipmentSlot> _slots=[];
    public IReadOnlyList<EquipmentSlot> RandomSlots {get=>_slots;init=>_slots=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<HappyZlLeaseEvent> _restore=[];
    public IReadOnlyList<HappyZlLeaseEvent> Restore {get=>_restore;init=>_restore=Array.AsReadOnly(value.ToArray());}
    private readonly IReadOnlyList<HappyZlPayment> _payments=[];
    public IReadOnlyList<HappyZlPayment> Payments {get=>_payments;init=>_payments=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyZlReceipt? HappyZhangLiao {get;init;} }
public sealed record HappyZlCounterspell(long Window,long Parent,int Depth,long ActionId,int Actor,int Target,bool Color,long GrantFrame);
public sealed partial record NullificationWindowFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyZlCounterspell? HappyZhangLiao {get;init;} }
public sealed record HappyZlStartedEvent(long FrameId,CardConversionSource Source,string Hash,SkillProgramEffectOp Op,long Parent,int Turn) : IGameEvent;
public sealed record HappyZlPaidEvent(long FrameId,int Ordinal,long Before,long After) : IGameEvent;
public sealed record HappyZlColorEvent(long FrameId,CardConversionSource Source,int Turn,int Target,bool? Color) : IGameEvent;
public sealed record HappyZlLeaseEvent(long FrameId,CardConversionSource Source,int Target,EquipmentSlot Slot,int Capacity) : IGameEvent;
public sealed record HappyZlLeaseExpiredEvent(long LeaseFrameId,int Target,EquipmentSlot Slot,string Reason) : IGameEvent;
public sealed record HappyZlRestoredEvent(long FrameId,long LeaseFrameId,int Target,EquipmentSlot Slot) : IGameEvent;
public sealed record HappyZlCounterspellEvent(HappyZlCounterspell Receipt) : IGameEvent;
public sealed record HappyZlCompletedEvent(long FrameId) : IGameEvent;
