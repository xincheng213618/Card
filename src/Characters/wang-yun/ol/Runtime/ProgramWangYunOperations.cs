using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WangYunNativeUseOrigin
{
    public long ProgramFrameId {get;init;}
    public int InstructionIndex {get;init;}
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public int Turn {get;init;}
    public int PhaseInstance {get;init;}
    public string Kind {get;init;}="";
    public long UseId {get;init;}
    public long ActionId {get;init;}
    public int Actor {get;init;}
    public CardKind OutputKind {get;init;}
    private IReadOnlyList<CardActionCost> _materials=Array.Empty<CardActionCost>();
    public IReadOnlyList<CardActionCost> Materials {get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _targets=Array.Empty<int>();
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
    public long Before {get;init;}
    public long After {get;init;}
    public bool PaymentCompleted {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public RoundOnceCardlessBasicPaymentReceipt? RoundOncePayment {get;init;}
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunNativeUseOrigin? WangYunNativeOrigin {get;init;}
}
public sealed record WangYunPlanReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public int Turn {get;init;}
    public int PhaseInstance {get;init;}
    public int InstructionIndex {get;init;}
    public int Actor {get;init;}
    public int CostCardId {get;init;}
    public string Stage {get;init;}="";
    public string ReturnStage {get;init;}="";
    public bool CostPaid {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public int? WeaponCardId {get;init;}
    public int? SlashTarget {get;init;}
    public int? Recipient {get;init;}
    private IReadOnlyList<int> _ids=Array.Empty<int>();
    public IReadOnlyList<int> IssuingIds {get=>_ids;init=>_ids=Array.AsReadOnly(value.ToArray());}
    public CardKind? IssuingKind {get;init;}
    public WangYunNativeUseOrigin? NativeUse {get;init;}
    public bool NativeReturned {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public RoundOnceCardlessBasicPaymentReceipt? RoundOncePayment {get;init;}
}
public sealed record WangYunTrickReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public int Turn {get;init;}
    public int PhaseInstance {get;init;}
    public int InstructionIndex {get;init;}
    public int CardId {get;init;}
    public CardKind PrintedKind {get;init;}
    public CardLocation From {get;init;}
    public string Stage {get;init;}="";
    private IReadOnlyList<CardKind> _samples=Array.Empty<CardKind>();
    public IReadOnlyList<CardKind> Samples {get=>_samples;init=>_samples=Array.AsReadOnly(value.ToArray());}
    public CardKind? SelectedKind {get;init;}
    public WangYunNativeUseOrigin? NativeUse {get;init;}
    public bool NativeReturned {get;init;}
}
public sealed record WangYunAwakeningReceipt(CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int InstructionIndex);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunPlanReceipt? WangYunPlan {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunTrickReceipt? WangYunTrick {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunAwakeningReceipt? WangYunAwakening {get;init;}
}
public sealed record WangYunPlanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseInstance,int Actor,int CostCardId):IGameEvent;
public sealed record WangYunPlanMovementPaidEvent(long FrameId,string Next,int CardId,CardLocation From,CardLocation To,long Before,long After):IGameEvent;
public sealed record WangYunNativeUseIssuedEvent(WangYunNativeUseOrigin Origin):IGameEvent;
public sealed record WangYunNativePaymentCompletedEvent(WangYunNativeUseOrigin Origin):IGameEvent;
public sealed record WangYunNativeUseReturnedEvent(long ProgramFrameId,long UseId,long ActionId,string Kind):IGameEvent;
public sealed record WangYunLianjiDamageEvent(long ProgramFrameId,long UseId,long ActionId,int PlanOwner,int ActualActor,int Target,int Amount):IGameEvent;
public sealed record WangYunRandomTricksStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseInstance,int CardId,CardKind PrintedKind,CardLocation From,string SamplesFingerprint):IGameEvent;
public sealed record WangYunAwakenedEvent(long FrameId,int OwnerSeat,string LostSkillId,string AcquiredSkillId):IGameEvent;
internal interface IWangYunProgramHost
{
    SkillProgramStepOutcome ResolveLianjiPaidWeaponAndSlash(ProgramSkillFrame f);
    SkillProgramStepOutcome AwakenFromIssuedLianjiSlashDamage(ProgramSkillFrame f);
    SkillProgramStepOutcome UseThreeRandomTricksFromSelectedMaterial(ProgramSkillFrame f);
}
internal sealed class ResolveLianjiPaidWeaponAndSlashDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash;
    public override ISkillProgramEffectHandler Handler{get;}=new WangYunHandler(0);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Damage,static(e,c)=>c.Damage(new(SkillProgramEffectOp.Damage,SkillProgramEffectTarget.SelectedTarget,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),new ConsumeSelectedCards(1)];
}
internal sealed class AwakenFromIssuedLianjiSlashDamageDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage;
    public override ISkillProgramEffectHandler Handler{get;}=new WangYunHandler(1);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GrantSkills,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class UseThreeRandomTricksFromSelectedMaterialDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial;
    public override ISkillProgramEffectHandler Handler{get;}=new WangYunHandler(2);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ConsumeSelectedCards(1)];
}
internal sealed class WangYunHandler(int kind):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>kind switch{0=>SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash,1=>SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage,_=>SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial};
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>kind switch
    {0=>h.GetCapability<IWangYunProgramHost>().ResolveLianjiPaidWeaponAndSlash(f),1=>h.GetCapability<IWangYunProgramHost>().AwakenFromIssuedLianjiSlashDamage(f),_=>h.GetCapability<IWangYunProgramHost>().UseThreeRandomTricksFromSelectedMaterial(f)};
}
internal static class WangYunComposition
{
    internal static void ValidateActivation(string path,SkillProgramActivation a)
    {
        var features=ProgramInstructionResolver.Default.Features(a);
        if(features.HasOperation(SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage))throw new InvalidOperationException(path+": causal awakening requires its real preparation window.");
        if(features.HasOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash) &&
            (a.Effects is not [{Op:SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash}] || a.MinCards!=1 || a.MaxCards!=1 || a.MinTargets!=1 || a.MaxTargets!=1 ||
             a.TargetKind!=SkillProgramTargetKind.OtherLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || a.UsesPerPhase!=1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.MarkerCost is not null))
            throw new InvalidOperationException(path+": Lianji requires one real hand payment, one other actual actor and one actual Play usage.");
        if(features.HasOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) &&
            (a.Effects is not [{Op:SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial}] || a.MinCards!=1 || a.MaxCards!=1 || a.MinTargets!=0 || a.MaxTargets!=0 ||
             !a.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment]) || a.UsesPerPhase!=1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.MarkerCost is not null))
            throw new InvalidOperationException(path+": random three tricks require one actual Slash/equipment HE entity and one native phase usage.");
    }
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        var features=ProgramInstructionResolver.Default.Features(t);
        if(features.HasOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash) || features.HasOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial))
            throw new InvalidOperationException(path+": paid Lianji and random material use require their native activation, not a borrowed trigger.");
        if(features.HasOperation(SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage) &&
            (t.Effects is not [{Op:SkillProgramEffectOp.AwakenFromIssuedLianjiSlashDamage}] || t.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
             t.Subject!=SkillProgramTriggerSubject.Owner || t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own || t.Optional || t.UsageScope!=SkillUsageScope.Game || t.UsageLimit!=1 ||
             t.MarkerCost is not null || t.Condition.Kind!=SkillProgramTriggerConditionKind.Always))throw new InvalidOperationException(path+": Moucheng requires its exact own preparation and causal game-once rule without max-HP changes.");
    }
}
