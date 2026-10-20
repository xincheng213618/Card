using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum YuanJiStage { Drawing, DrawChildren, ReturnOrder, ReturnChildren }
public sealed record YuanJiMarkClaim(long DrawFrameId,int Index,int CardId,CardConversionSource Source,string GameplayHash,long MovementSequence);
public sealed record YuanJiDrawStep(int Index,int RecipientSeat,bool Top,int Requested,int Actual,long Before,long After,int? CardId=null,Suit? Suit=null);
public sealed record YuanJiReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,SkillProgramTriggerWindow Window,int ActualTurn,int TurnOwner,int Round,YuanJiStage Stage,int LoserSeat=-1,int OpportunityIndex=-1,int Cursor=0,long Before=0,long After=0)
{
 private IReadOnlyList<YuanJiMarkClaim> _claims=Array.Empty<YuanJiMarkClaim>();public IReadOnlyList<YuanJiMarkClaim> Claims{get=>_claims;init=>_claims=Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<int> _eligible=Array.Empty<int>();public IReadOnlyList<int> Eligible{get=>_eligible;init=>_eligible=Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<int> _ordered=Array.Empty<int>();public IReadOnlyList<int> Ordered{get=>_ordered;init=>_ordered=Array.AsReadOnly(value.ToArray());}
 private IReadOnlyList<YuanJiDrawStep> _draws=Array.Empty<YuanJiDrawStep>();public IReadOnlyList<YuanJiDrawStep> Draws{get=>_draws;init=>_draws=Array.AsReadOnly(value.ToArray());}
}
public sealed record YuanJiLossOpportunityEvent(int Index,long BatchId,CardConversionSource Source,string GameplayHash,int LoserSeat,int ActualTurn,int Round,int FirstCardId,int SecondCardId,long FirstSequence,long SecondSequence):IGameEvent;
public sealed record YuanJiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,SkillProgramTriggerWindow Window,int ActualTurn,int TurnOwner,int Round,int LoserSeat,int OpportunityIndex):IGameEvent;
public sealed record YuanJiDrawCompletedEvent(long FrameId,int Index,int RecipientSeat,bool Top,int Requested,int Actual,long Before,long After):IGameEvent;
public sealed record YuanJiShownEvent(long FrameId,int Index,int RecipientSeat,int CardId,Suit Suit,int Rank,long MovementSequence):IGameEvent;
public sealed record YuanJiAcquiredEvent(long FrameId,int Index,int CardId,long MovementSequence):IGameEvent;
public sealed record YuanJiMarkClosedEvent(long ReturnFrameId,long DrawFrameId,int Index,int CardId,bool WasInHand):IGameEvent;
public sealed record YuanJiReturnSelectedEvent(long FrameId,int Index,int CardId):IGameEvent;
public sealed record YuanJiReturnPaidEvent(long FrameId,int Count,bool Top,long Before,long After):IGameEvent;
public sealed record YuanJiJieyanDisabledEvent(long FrameId,int OwnerSeat,int ActualTurn,int Round,Suit FirstSuit,Suit SecondSuit):IGameEvent;
public sealed record YuanJiCompletedEvent(long FrameId,SkillProgramEffectOp Op):IGameEvent;
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public YuanJiReceipt? YuanJi{get;init;}}
internal interface IYuanJiHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class YuanJiDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new YuanJiHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(1));
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
}
internal sealed class ResolveJieyanOppositeEndDrawPairDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair;}
internal sealed class ResolveJinghuaRoundDrawDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJinghuaRoundDraw;}
internal sealed class ResolveJinghuaTurnReturnDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveJinghuaTurnReturn;}
internal sealed class ResolveShuiyueTurnDrawDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveShuiyueTurnDraw;}
internal sealed class ResolveShuiyueRoundReturnDescriptor:YuanJiDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveShuiyueRoundReturn;}
internal sealed class YuanJiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IYuanJiHost>().Resolve(f,op);}
internal static class YuanJiComposition
{
 internal static bool IsOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair or SkillProgramEffectOp.ResolveJinghuaRoundDraw or SkillProgramEffectOp.ResolveJinghuaTurnReturn or SkillProgramEffectOp.ResolveShuiyueTurnDraw or SkillProgramEffectOp.ResolveShuiyueRoundReturn;
 [ProgramCatalogValidation(700)]
 internal static void ValidateProgram(string path,SkillProgram p)
 {
  if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>IsOp(e.Op)))return;
  if(p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.Triggers.Any(t=>t.Effects is not[var e]||!IsOp(e.Op)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always))throw new InvalidOperationException(path+": YuanJi requires the exact native owner lifecycle and atomic-loss operations.");
  bool T(SkillProgramTrigger t,string id,SkillProgramTriggerWindow window,SkillProgramEffectOp op,bool optional)=>t.Id==id&&t.Window==window&&t.Optional==optional&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&t.Effects is[var e]&&e.Op==op;
  if(p.Id=="ol:jieyan"&&p.Triggers is[var loss]&&T(loss,"hand-pair",SkillProgramTriggerWindow.CardsMoved,SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair,true))return;
  if(p.Id=="ol:jinghua"&&p.Triggers is[var round,var turn]&&T(round,"round-draw",SkillProgramTriggerWindow.RoundStarting,SkillProgramEffectOp.ResolveJinghuaRoundDraw,false)&&T(turn,"turn-bottom",SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramEffectOp.ResolveJinghuaTurnReturn,false))return;
  if(p.Id=="ol:shuiyue"&&p.Triggers is[var end,var close]&&T(end,"turn-draw",SkillProgramTriggerWindow.AfterTurnEnded,SkillProgramEffectOp.ResolveShuiyueTurnDraw,false)&&T(close,"round-top",SkillProgramTriggerWindow.RoundEnding,SkillProgramEffectOp.ResolveShuiyueRoundReturn,false))return;
  throw new InvalidOperationException(path+": YuanJi must retain all exact atomic-loss, round-start, own-turn-start, actual-turn-end and old-round-end bindings.");
 }
}
