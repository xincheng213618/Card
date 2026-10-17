using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record WuTuGuHanyongGrant(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int Round,int HpAtUse,int ProviderSeat,CardKind Kind);
public sealed record WuTuGuHanyongUse
{
    private IReadOnlyList<WuTuGuHanyongGrant> _grants=Array.AsReadOnly(Array.Empty<WuTuGuHanyongGrant>());
    public IReadOnlyList<WuTuGuHanyongGrant> Grants{get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}
}
public enum WuTuGuRanshangStage{Issuing,HpChildren}
public sealed record WuTuGuRanshangReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,int Amount,WuTuGuRanshangStage Stage);
public sealed partial record CardUseFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WuTuGuHanyongUse? WuTuGuHanyong{get;init;}}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public WuTuGuRanshangReceipt? WuTuGuRanshang{get;init;}}
public sealed record WuTuGuHanyongQualifiedEvent(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int Round,int HpAtUse,int ProviderSeat,CardKind Kind):IGameEvent;
public sealed record WuTuGuHanyongArmedEvent(WuTuGuHanyongGrant Grant):IGameEvent;
public sealed record WuTuGuFirePointAddedEvent(long ProgramFrameId,long DamageFrameId,long ParentWindowId,CardConversionSource Source,string GameplayHash,int Point,int Amount,int Count):IGameEvent;
public sealed record WuTuGuRanshangIssuedEvent(long ProgramFrameId,CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,int Amount):IGameEvent;
public sealed record WuTuGuRanshangPaidEvent(long ProgramFrameId,int Amount,int RemainingHp):IGameEvent;
public sealed record WuTuGuRanshangCompletedEvent(long ProgramFrameId):IGameEvent;
internal interface IWuTuGuHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class WuTuGuDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler{get;}=new WuTuGuHandler(op);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Damage,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(op==SkillProgramEffectOp.ArmHanyongNativeGroupDamage?SkillProgramTriggerWindow.CardUseCommitted:op==SkillProgramEffectOp.AccumulateRanshangFirePoint?SkillProgramTriggerWindow.AfterDamageApplied:SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class ArmHanyongNativeGroupDamageDescriptor():WuTuGuDescriptor(SkillProgramEffectOp.ArmHanyongNativeGroupDamage){}
internal sealed class AccumulateRanshangFirePointDescriptor():WuTuGuDescriptor(SkillProgramEffectOp.AccumulateRanshangFirePoint){}
internal sealed class ResolveRanshangEndingHpLossDescriptor():WuTuGuDescriptor(SkillProgramEffectOp.ResolveRanshangEndingHpLoss){}
internal sealed class WuTuGuHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IWuTuGuHost>().Resolve(f,op);}
internal static class WuTuGuComposition
{
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        bool Own(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ArmHanyongNativeGroupDamage or SkillProgramEffectOp.AccumulateRanshangFirePoint or SkillProgramEffectOp.ResolveRanshangEndingHpLoss;
        if(!p.Triggers.Any(t=>t.Effects.Any(Own))&&!p.Activations.Any(a=>a.Effects.Any(Own)))return;
        if(p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||p.BooleanStates.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.DamageModifiers.Count!=0||p.LordSkillProjection)
            throw new InvalidOperationException(path+": Wu Tu Gu requires independent native-use/fire/ending bindings.");
        if(p.Id=="ol:hanyong"&&p.Triggers is[var h]&&h.Id=="native-group-use"&&h.Window==SkillProgramTriggerWindow.CardUseCommitted&&h.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&h.Optional&&h.DamageOccurrence is null&&
            h.CardKinds.ToHashSet().SetEquals([CardKind.BarbarianAssault,CardKind.ArrowBarrage])&&h.Condition.Kind==SkillProgramTriggerConditionKind.Always&&h.Effects is[{Op:SkillProgramEffectOp.ArmHanyongNativeGroupDamage,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])return;
        if(p.Id!="ol:ranshang"||p.Triggers.Count!=2)throw new InvalidOperationException(path+": Unexpected Wu Tu Gu composition.");
        var fire=p.Triggers.SingleOrDefault(t=>t.Id=="actual-fire-point");var end=p.Triggers.SingleOrDefault(t=>t.Id=="ending-total-hp-loss");
        if(fire is null||end is null||fire.Window!=SkillProgramTriggerWindow.AfterDamageApplied||fire.Subject!=SkillProgramTriggerSubject.Owner||fire.Optional||fire.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamagePoint||fire.Condition.Kind!=SkillProgramTriggerConditionKind.DamageNatureIs||fire.Condition.DamageNature!=DamageNature.Fire||
            fire.Effects is not[{Op:SkillProgramEffectOp.AccumulateRanshangFirePoint,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||
            end.Window!=SkillProgramTriggerWindow.TurnEnding||end.Subject!=SkillProgramTriggerSubject.Owner||end.Optional||end.DamageOccurrence is not null||end.Condition.Kind!=SkillProgramTriggerConditionKind.Always||
            end.Effects is not[{Op:SkillProgramEffectOp.ResolveRanshangEndingHpLoss,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||
            p.Triggers.Any(t=>t.OwnerRelation is not null||t.UsageLimit is not null||t.MarkerCost is not null||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardKinds.Count!=0))
            throw new InvalidOperationException(path+": Ranshang requires actual fire points and one native equal-amount ending loss.");
    }
}
