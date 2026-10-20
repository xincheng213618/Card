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
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunPlanReceipt? WangYunPlan {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunTrickReceipt? WangYunTrick {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public WangYunAwakeningReceipt? WangYunAwakening {get;init;}
}
public sealed record WangYunNativeUseIssuedEvent(WangYunNativeUseOrigin Origin):IGameEvent;
public sealed record WangYunNativePaymentCompletedEvent(WangYunNativeUseOrigin Origin):IGameEvent;
public sealed record WangYunNativeUseReturnedEvent(long ProgramFrameId,long UseId,long ActionId,string Kind):IGameEvent;
internal interface IWangYunProgramHost
{
    SkillProgramStepOutcome ResolveLianjiPaidWeaponAndSlash(ProgramSkillFrame f);
    SkillProgramStepOutcome AwakenFromIssuedLianjiSlashDamage(ProgramSkillFrame f);
    SkillProgramStepOutcome UseThreeRandomTricksFromSelectedMaterial(ProgramSkillFrame f);
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
