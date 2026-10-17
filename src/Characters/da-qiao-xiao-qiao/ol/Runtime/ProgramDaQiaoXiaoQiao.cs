using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record DaQiaoXiaoQiaoLuoyanSource(string CapabilityGrantId,string CapabilitySkillInstanceId,string LeafSkillId);
public sealed partial record SkillGrant
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public DaQiaoXiaoQiaoLuoyanSource? DaQiaoXiaoQiaoLuoyanSource{get;init;}}
public enum DaQiaoXiaoQiaoStage{Deposit,DepositChildren,Burst,PileCards,TurnoverChildren,HandCards,Target,CostChildren,EquipmentChildren,DamageChildren}
public sealed record DaQiaoXiaoQiaoEntity(int Id,CardLocation From);
public sealed record DaQiaoXiaoQiaoReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,PublicPersistentPileSource Pile,DaQiaoXiaoQiaoStage Stage,
    string? Branch=null,int? TargetSeat=null,bool Paid=false,int MovementStep=0,long Before=0,long After=0,int Damage=0)
{
    private IReadOnlyList<DaQiaoXiaoQiaoEntity> _selected=Array.AsReadOnly(Array.Empty<DaQiaoXiaoQiaoEntity>()),_entities=Array.AsReadOnly(Array.Empty<DaQiaoXiaoQiaoEntity>());
    private IReadOnlyList<int> _grain=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<DaQiaoXiaoQiaoEntity> Selected{get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<DaQiaoXiaoQiaoEntity> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Grain{get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public DaQiaoXiaoQiaoReceipt? DaQiaoXiaoQiao{get;init;}}
public sealed record DaQiaoXiaoQiaoStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn):IGameEvent;
public sealed record DaQiaoXiaoQiaoMovementPaidEvent(long FrameId,int Step,DaQiaoXiaoQiaoStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record DaQiaoXiaoQiaoEntityPaidEvent(long FrameId,int Step,int CardId,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record DaQiaoXiaoQiaoTurnedEvent(long FrameId,bool FaceDown):IGameEvent;
public sealed record DaQiaoXiaoQiaoDamageIssuedEvent(long FrameId,int TargetSeat,int Amount):IGameEvent;
public sealed record DaQiaoXiaoQiaoCompletedEvent(long FrameId,string? Branch,int? TargetSeat):IGameEvent;
public sealed record DaQiaoXiaoQiaoLuoyanChangedEvent(int OwnerSeat,string GrantId,string SkillId,string InstanceId,DaQiaoXiaoQiaoLuoyanSource Source,bool Added):IGameEvent;
internal interface IDaQiaoXiaoQiaoHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,bool maintain);}
internal sealed class DaQiaoXiaoQiaoHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IDaQiaoXiaoQiaoHost>().Resolve(f,op==SkillProgramEffectOp.MaintainLuoyanPileSkills);}
internal abstract class DaQiaoXiaoQiaoDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler{get;}=new DaQiaoXiaoQiaoHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Damage,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(op==SkillProgramEffectOp.MaintainLuoyanPileSkills?SkillProgramTriggerWindow.GameStarting:SkillProgramTriggerWindow.DiscardPhaseStarting)];
}
internal sealed class ResolveXingwuDepositAndBurstDescriptor():DaQiaoXiaoQiaoDescriptor(SkillProgramEffectOp.ResolveXingwuDepositAndBurst){}
internal sealed class MaintainLuoyanPileSkillsDescriptor():DaQiaoXiaoQiaoDescriptor(SkillProgramEffectOp.MaintainLuoyanPileSkills){}
internal static class DaQiaoXiaoQiaoComposition
{
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        bool Own(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.ResolveXingwuDepositAndBurst or SkillProgramEffectOp.MaintainLuoyanPileSkills;
        if(!p.Triggers.Any(t=>t.Effects.Any(Own))&&!p.Activations.Any(a=>a.Effects.Any(Own)))return;
        if(p.Activations.Count!=0||p.Triggers.Count!=1||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||p.BooleanStates.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.DamageModifiers.Count!=0||p.LordSkillProjection)throw new InvalidOperationException(path+": Xingwu/Luoyan require their exact native owner bindings.");
        var t=p.Triggers[0];var x=p.Id=="ol:xingwu";var l=p.Id=="ol:luoyan";
        if(!x&&!l||t.Id!=(x?"discard-start-dance":"pile-derived-skills")||t.Window!=(x?SkillProgramTriggerWindow.DiscardPhaseStarting:SkillProgramTriggerWindow.GameStarting)||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional!=x||t.OwnerRelation is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.CardKinds.Count!=0||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.MovementOccurrence is not null||t.DamageOccurrence is not null||t.Effects is not[var e]||e.Op!=(x?SkillProgramEffectOp.ResolveXingwuDepositAndBurst:SkillProgramEffectOp.MaintainLuoyanPileSkills)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Unexpected Xingwu or Luoyan source/phase contract.");
    }
}
