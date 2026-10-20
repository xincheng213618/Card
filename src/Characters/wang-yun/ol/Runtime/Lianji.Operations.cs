using System.Text.Json.Serialization;
namespace CardGame.Core;
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
public sealed record WangYunPlanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseInstance,int Actor,int CostCardId):IGameEvent;
public sealed record WangYunPlanMovementPaidEvent(long FrameId,string Next,int CardId,CardLocation From,CardLocation To,long Before,long After):IGameEvent;
public sealed record WangYunLianjiDamageEvent(long ProgramFrameId,long UseId,long ActionId,int PlanOwner,int ActualActor,int Target,int Amount):IGameEvent;
internal sealed class ResolveLianjiPaidWeaponAndSlashDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash;
    public override ISkillProgramEffectHandler Handler{get;}=new WangYunHandler(0);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Damage,static(e,c)=>c.Damage(new(SkillProgramEffectOp.Damage,SkillProgramEffectTarget.SelectedTarget,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),new ConsumeSelectedCards(1)];
}
