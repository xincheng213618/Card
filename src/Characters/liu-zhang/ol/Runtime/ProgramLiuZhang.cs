using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum ZonghuIntent { Play,Dodge,Duel,Group,FactionDodge,FactionSlashPlayed,FactionSlashUse,BorrowedSword,Qinglong,ProgramSlash,ProgramNearestSlash,AssistedSlash,NearestLegalSlash,FixedSlash,GenericBasic,Dying }
public enum ZonghuStage { Declare,Victim,Card,CostChildren }
public sealed record ZonghuNeed(ZonghuIntent Intent,long OwnerFrameId,long RequestFrameId,long? ParentActionId,int ProviderSeat,int ActorSeat,int? RequesterSeat,int? OpponentSeat,int Cursor,int TargetIndex,int EffectRound,int InstructionIndex,SkillProgramEffectOp? RequestOp,IReadOnlyList<int> Targets)
{
 private IReadOnlyList<int> _targets=Array.AsReadOnly(Targets.ToArray());
 public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
 public bool Equals(ZonghuNeed? o)=>o is not null&&Intent==o.Intent&&OwnerFrameId==o.OwnerFrameId&&RequestFrameId==o.RequestFrameId&&ParentActionId==o.ParentActionId&&ProviderSeat==o.ProviderSeat&&ActorSeat==o.ActorSeat&&RequesterSeat==o.RequesterSeat&&OpponentSeat==o.OpponentSeat&&Cursor==o.Cursor&&TargetIndex==o.TargetIndex&&EffectRound==o.EffectRound&&InstructionIndex==o.InstructionIndex&&RequestOp==o.RequestOp&&Targets.SequenceEqual(o.Targets);
 public override int GetHashCode()=>HashCode.Combine(Intent,OwnerFrameId,RequestFrameId,ProviderSeat,ActorSeat,Cursor,InstructionIndex,Targets.Count);
}
public sealed record ZonghuGift(int CardId,CardKind Kind,CardLocation From,long Sequence,long BatchId);
public sealed record ZonghuPaidOrigin(long AttemptId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,ZonghuNeed Need,CardKind OutputKind,int TargetSeat,int RecipientSeat,IReadOnlyList<ZonghuGift> Gifts,int RequiredCount,long Before,long After)
{
 private IReadOnlyList<ZonghuGift> _gifts=Array.AsReadOnly(Gifts.ToArray());
 public IReadOnlyList<ZonghuGift> Gifts {get=>_gifts;init=>_gifts=Array.AsReadOnly(value.ToArray());}
}
public sealed record ZonghuReceipt(CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,ZonghuNeed Need,PendingDecision OriginalDecision,ZonghuStage Stage,
 [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]CardKind? OutputKind=null,
 [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? TargetSeat=null,
 [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]int? RecipientSeat=null,
 [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]ZonghuPaidOrigin? Paid=null,long Before=0,long After=0)
{
 private PendingDecision _original=RequestedDeckBasicFrame.FreezeDecision(OriginalDecision);
 public PendingDecision OriginalDecision {get=>_original;init=>_original=RequestedDeckBasicFrame.FreezeDecision(value);}
 private IReadOnlyList<int> _selected=Array.Empty<int>();public IReadOnlyList<int> Selected {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<int> _grain=Array.Empty<int>();public IReadOnlyList<int> GrainIds {get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
}
public sealed record ZonghuStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,ZonghuIntent Intent,long OwnerFrameId,long RequestFrameId,PromptId OriginalPromptId,long OriginalRevision,string OriginalDecisionHash):IGameEvent;
public sealed record ZonghuDeclaredEvent(long FrameId,CardKind OutputKind,int TargetSeat):IGameEvent;
public sealed record ZonghuRecipientSelectedEvent(long FrameId,int RecipientSeat):IGameEvent;
public sealed record ZonghuGiftSelectedEvent(long FrameId,int Index,int CardId):IGameEvent;
public sealed record ZonghuGiftPaidEvent(long FrameId,int Index,int CardId,CardKind Kind,CardLocation From,int RecipientSeat,long Sequence,long BatchId):IGameEvent;
public sealed record ZonghuCostPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int ActualTurnOwner,int RecipientSeat,int RequiredCount,long Before,long After):IGameEvent;
public sealed record ZonghuAttemptResolvedEvent(long FrameId,bool Matched,bool NativeLegal):IGameEvent;
public sealed record ZonghuUseIssuedEvent(long AttemptId,long UseId,long ActionId,int ActorSeat,int ProviderSeat,int? RequesterSeat,CardKind OutputKind):IGameEvent;
public sealed record ZonghuResponseIssuedEvent(long AttemptId,long OwnerFrameId,long ActionId,int ActorSeat,int ProviderSeat,int? RequesterSeat,CardKind OutputKind,ProgramCardContinuation Continuation):IGameEvent;
public sealed record ZonghuUseReturnedEvent(long AttemptId,long UseId,long ActionId):IGameEvent;
public sealed record ZonghuProgramReturn(ZonghuPaidOrigin Origin,long UseId,long ActionId,bool Returned=false);
public enum FengweiStage { Choice,DrawChildren }
public sealed record FengweiReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int Round,FengweiStage Stage,int Requested=0,int Actual=0,long Before=0,long After=0);
public sealed record FengweiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentId,int Round):IGameEvent;
public sealed record FengweiDrawCompletedEvent(long FrameId,CardConversionSource Source,int Round,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record FengweiAcquiredEvent(long FrameId,CardConversionSource Source,int Round,int CardId,long Sequence):IGameEvent;
public sealed record FengweiDrawSkippedEvent(long FrameId,CardConversionSource Source,int Round,long PhaseToken,int CardId):IGameEvent;
public sealed record FengweiDamageReceipt(long AttackId,int TargetSeat,int Cursor,int InheritedAmount,int Bonus,long InvoiceId,int? TransferredTarget=null,long? TransferWindowId=null);
public sealed record FengweiDamageFrozenEvent(long AttackId,int TargetSeat,int Cursor,int InheritedAmount,int Bonus,long InvoiceId):IGameEvent;
public sealed record FengweiDamageRebasedEvent(long AttackId,int TargetSeat,int Cursor,int InheritedAmount):IGameEvent;
public sealed record FengweiDamageTransferredEvent(long AttackId,int OriginalSeat,int TargetSeat,int Cursor,long WindowId):IGameEvent;
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuReceipt? Zonghu {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuProgramReturn? ZonghuReturn {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuResponseOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuKeAcceptedOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiReceipt? Fengwei {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiDamageReceipt? FengweiDamage {get;init;}
}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZonghuPaidOrigin? ZonghuResponseOrigin {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiDamageReceipt? FengweiDamage {get;init;}
}
public sealed partial record JudgmentFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FengweiDamageReceipt? FengweiDamage {get;init;}}
internal interface ILiuZhangHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class LiuZhangDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new LiuZhangHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(1));
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class ResolveFengweiRoundStartDrawDescriptor:LiuZhangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFengweiRoundStartDraw;}
internal sealed class ResolveFengweiActualDrawSkipDescriptor:LiuZhangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFengweiActualDrawSkip;}
internal sealed class ResolveZonghuRequiredUseRequestDescriptor:LiuZhangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveZonghuRequiredUseRequest;}
internal sealed class LiuZhangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op=>op;
 public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<ILiuZhangHost>().Resolve(f,op);
}
internal static class LiuZhangComposition
{
 internal static void ValidateProgram(string path,SkillProgram p)
 {
  var ops=p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Where(e=>e.Op is SkillProgramEffectOp.ResolveFengweiRoundStartDraw or SkillProgramEffectOp.ResolveFengweiActualDrawSkip or SkillProgramEffectOp.ResolveZonghuRequiredUseRequest).ToArray();if(ops.Length==0)return;
  if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||ops.Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always))throw new InvalidOperationException(path+": LiuZhang requires complete native owner operations.");
  if(p.Id=="ol:fengwei"&&p.Activations.Count==0&&p.Triggers is[var first,var skip]&&first.Id=="round-draw"&&first.Window==SkillProgramTriggerWindow.RoundStarting&&!first.Optional&&first.Effects is[{Op:SkillProgramEffectOp.ResolveFengweiRoundStartDraw}]&&skip.Id=="draw-skip"&&skip.Window==SkillProgramTriggerWindow.DrawPhaseStarting&&!skip.Optional&&skip.Effects is[{Op:SkillProgramEffectOp.ResolveFengweiActualDrawSkip}])return;
  if(p.Id=="ol:zonghu"&&p.Triggers.Count==0&&p.Activations is[var a]&&a.Id=="native-use-request"&&a.MinCards==0&&a.MaxCards==0&&a.MinTargets==0&&a.MaxTargets==0&&a.UsesPerTurn is null&&a.Effects is[{Op:SkillProgramEffectOp.ResolveZonghuRequiredUseRequest}])return;
  throw new InvalidOperationException(path+": LiuZhang must retain complete round draw, actual draw skip and native true-use request bindings.");
 }
}
