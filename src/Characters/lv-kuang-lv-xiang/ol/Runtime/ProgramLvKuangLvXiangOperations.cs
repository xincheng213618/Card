using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record LvKuangLvXiangReceipt(CardConversionSource Source,string GameplayHash,int Instruction,int Turn,string Stage,int Target,int Actor=-1,long? OriginalUseId=null,long? OriginalActionId=null,QigongNativeReceipt? Native=null,bool Returned=false)
{
 private readonly IReadOnlyList<LiehouPayment> _payments=[];
 public IReadOnlyList<LiehouPayment> Payments{get=>_payments;init=>_payments=Array.AsReadOnly(value.ToArray());}
 private readonly IReadOnlyList<int> _issuing=[];
 public IReadOnlyList<int> IssuingIds{get=>_issuing;init=>_issuing=Array.AsReadOnly(value.ToArray());}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CardKind? IssuingKind{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public RoundOnceCardlessBasicPaymentReceipt? RoundOncePayment{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public DrawFundedDistinctBasicPayment? DrawFundedPayment{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ChainedStateBasicPayment? ChainedPayment{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public int? IssuingProvider{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QigongFactionRequest? Faction{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QigongDeclarationRequest? Declaration{get;init;}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public LvKuangLvXiangReceipt? LvKuangLvXiang{get;init;}}
public sealed partial record CardUseFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QigongNativeReceipt? Qigong{get;init;}}
public sealed record LvKuangLvXiangStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Instruction,int Turn,int Target,long? OriginalUseId,long? OriginalActionId):IGameEvent;
internal interface ILvKuangLvXiangProgramHost{SkillProgramStepOutcome ExecuteLvKuangLvXiang(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal sealed class LvKuangLvXiangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<ILvKuangLvXiangProgramHost>().ExecuteLvKuangLvXiang(f,op);}
internal abstract class LvKuangLvXiangDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>op;public override ISkillProgramEffectHandler Handler{get;}=new LvKuangLvXiangHandler(op);public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
}
internal static class LvKuangLvXiangComposition
{
 internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveQigongActualDodgedSlashRequest or SkillProgramEffectOp.ResolveLiehouRangeHandRelay;
 internal static void Activation(string path,SkillProgramActivation a){if(!a.Effects.Any(e=>Own(e.Op)))return;if(a.Effects is not[{Op:SkillProgramEffectOp.ResolveLiehouRangeHandRelay,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.OtherLiving||a.UsesPerPhase!=1||a.UsesPerTurn is not null||a.UsesPerGame is not null||a.MarkerCost is not null)throw new InvalidOperationException(path+": Liehou requires one actual other range participant, no preselected cards and one Play use.");}
 internal static void Trigger(string path,SkillProgramTrigger t){if(!t.Effects.Any(e=>Own(e.Op)))return;if(t.Effects is not[{Op:SkillProgramEffectOp.ResolveQigongActualDodgedSlashRequest,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||t.Window!=SkillProgramTriggerWindow.SlashFullyDodged||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!t.Optional||t.UsageLimit is not null||t.UsageScope is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.CardKinds.Count!=3||!t.CardKinds.ToHashSet().SetEquals([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash]))throw new InvalidOperationException(path+": Qigong needs one optional exact native fully-dodged Slash-family actor trigger without invented usage quota.");}
}
