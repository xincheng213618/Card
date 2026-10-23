using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryCuiLieHost{SkillProgramStepOutcome Begin(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class AnniversaryCuiLieDescriptor:ProgramOperationDescriptorBase
{
 public override ISkillProgramEffectHandler Handler=>new AnniversaryCuiLieHandler(Op);
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ProgramOperationInteraction Interaction=>Op==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw?ProgramOperationInteraction.Automatic:ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy=>new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));
 public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeActionAppearance,Activated:ProgramTrackedFact.NativeActionAppearance);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op switch{SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue=>[new ReadSelectedTarget()],SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)],_=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PindianCardsRevealed)]};
}
internal sealed class ResolveAnniversaryCuiLieZijueDescriptor:AnniversaryCuiLieDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue;}
internal sealed class ResolveAnniversaryCuiLieChibiDescriptor:AnniversaryCuiLieDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi;}
internal sealed class ResolveAnniversaryCuiLieZijuePindianDrawDescriptor:AnniversaryCuiLieDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw;}
internal sealed class AnniversaryCuiLieHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IAnniversaryCuiLieHost>().Begin(f,op);}
internal static class AnniversaryCuiLieComposition
{
 internal const string Zijue="anniversary:zijue",Chibi="anniversary:chibi";
 [ProgramCatalogValidation(46900)]internal static void Validate(string path,SkillProgram p)
 {
  if(!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(e=>e.Op is SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue or SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi or SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw))return;
  if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0)throw new InvalidOperationException(path+": Cui Lie requires his native paid operations without unrelated policies.");
  if(p.Id==Zijue&&p.Activations is[{Id:"declare-number",MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,TargetKind:SkillProgramTargetKind.OtherLiving,UsesPerTurn:null,UsesPerPhase:1,Effects:[{Op:SkillProgramEffectOp.ResolveAnniversaryCuiLieZijue,Target:SkillProgramEffectTarget.Owner}]}]&&p.Triggers is[{Id:"paid-pindian-draw",Window:SkillProgramTriggerWindow.PindianCardsRevealed,Subject:SkillProgramTriggerSubject.Owner,Optional:false,IsSkillInvocation:false,Effects:[{Op:SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw,Target:SkillProgramEffectTarget.Owner}]}])return;
  if(p.Id==Chibi&&p.Activations.Count==0&&p.Triggers is[{Id:"far-target-contest",Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,OwnerRelation:SkillProgramCardActionOwnerRelation.Observer,SingleActionInstance:true,Optional:true,Effects:[{Op:SkillProgramEffectOp.ResolveAnniversaryCuiLieChibi,Target:SkillProgramEffectTarget.Owner}]}])return;
  throw new InvalidOperationException(path+": Cui Lie requires his exact phase activation, revealed-Pindian reward and native far-target observer.");
 }
}
public enum AnniversaryCuiLieStage{Number,Payment,GiveChildren,RecoveryChildren,Drawing,DrawChildren,Pindian,PindianChildren,BorrowedVictim,MutationChildren,Completed}
public sealed record AnniversaryCuiLieReceipt(CardConversionSource Source,string Hash,SkillProgramEffectOp Operation,long ParentId,int Turn,int Phase,int Actor,AnniversaryCuiLieStage Stage)
{
 private IReadOnlyList<int> _cards=Array.AsReadOnly(Array.Empty<int>()),_materials=Array.AsReadOnly(Array.Empty<int>());
 private IReadOnlyList<CardMovementRecord> _moves=Array.AsReadOnly(Array.Empty<CardMovementRecord>());
 public IReadOnlyList<int> Cards{get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<CardMovementRecord> Moves{get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
 public int Number{get;init;}
 public int Cursor{get;init;}
 private IReadOnlyList<long> _leases=Array.AsReadOnly(Array.Empty<long>());
 public IReadOnlyList<long> RewardLeases{get=>_leases;init=>_leases=Array.AsReadOnly(value.ToArray());}
 public long Before{get;init;}
 public long After{get;init;}
 public long PindianId{get;init;}
 public PindianResult? Result{get;init;}
 public NativeDrawOutcome? Draw{get;init;}
 public long UseId{get;init;}
 public long ActionId{get;init;}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public AnniversaryCuiLieReceipt? AnniversaryCuiLie{get;init;}}
public sealed record AnniversaryCuiLieStartedEvent(long FrameId,CardConversionSource Source,string Hash,SkillProgramEffectOp Operation,long ParentId,int Turn,int Phase,int Actor):IGameEvent;
public sealed record AnniversaryCuiLieNumberEvent(long FrameId,int Recipient,int Number):IGameEvent;
public sealed record AnniversaryCuiLieGiftEvent(long FrameId,int Count,long Before,long After):IGameEvent;
public sealed record AnniversaryCuiLieLeaseEvent(long FrameId,CardConversionSource Source,string Hash,int Turn,int Amount):IGameEvent;
public sealed record AnniversaryCuiLieDrawReturnedEvent(long FrameId,int Recipient,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record AnniversaryCuiLiePindianIssuedEvent(long FrameId,long ContestId,int Actor):IGameEvent;
public sealed record AnniversaryCuiLiePindianReturnedEvent(long FrameId,long ContestId,int SourceRank,int OpponentRank,bool Won):IGameEvent;
public sealed record AnniversaryCuiLieDisabledEvent(CardConversionSource Source,int Turn,long FrameId):IGameEvent;
public sealed record AnniversaryCuiLieUseChangedEvent(long FrameId,CardConversionSource Source,string Hash,long UseId,long ActionId,int Actor,bool Cancelled):IGameEvent
{
 public long MaterialSequenceBefore{get;init;}
 private IReadOnlyList<int> _before=Array.AsReadOnly(Array.Empty<int>()),_after=Array.AsReadOnly(Array.Empty<int>()),_materials=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> BeforeTargets{get=>_before;init=>_before=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> AfterTargets{get=>_after;init=>_after=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed record AnniversaryCuiLieMaterialClaimedEvent(long FrameId,long UseId,long ActionId,int Recipient,int Count,long Before,long After):IGameEvent;
public sealed record AnniversaryCuiLieCompletedEvent(long FrameId):IGameEvent;
