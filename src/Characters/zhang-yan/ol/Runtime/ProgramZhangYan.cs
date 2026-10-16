using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum ZhangYanStage { SujiCard, SujiTarget, IssuingUse, NativeUse, NativeChildren, RewardChoice, RewardChildren, LangdaoVotes, LangdaoTargets }
public enum LangdaoOption { Damage, ExtraTarget, NoResponse }
public sealed record ZhangYanVote(int Seat,LangdaoOption Option);
public sealed record ZhangYanSujiReturn(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int Turn,int PhaseOwner,int OriginalTarget,int CardId,CardKind PrintedKind,CardLocation From);
public sealed record ZhangYanReceipt(CardConversionSource Source,string GameplayHash,long WindowId,SkillProgramEffectOp Operation,int Turn,int PhaseOwner,ZhangYanStage Stage,
    int? CardId=null,CardLocation? From=null,int? Target=null,ZhangYanSujiReturn? NativeReturn=null,bool NativeCompleted=false,long Before=0,long After=0,long? BatchId=null,long? DamageId=null)
{
 private IReadOnlyList<int> _grain=[],_targets=[];private IReadOnlyList<LangdaoOption> _options=[];private IReadOnlyList<ZhangYanVote> _votes=[];
 public IReadOnlyList<int> Grain {get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> AddedTargets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<LangdaoOption> Options {get=>_options;init=>_options=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<ZhangYanVote> Votes {get=>_votes;init=>_votes=Array.AsReadOnly(value.ToArray());}
}
public sealed record ZhangYanLangdaoUse(long ProgramFrameId,long WindowId,long ActionId,CardConversionSource Source,string GameplayHash,int OriginalActor,int OriginalTarget,int DamageBonus,int ExtraCount,bool NoResponse,bool Killed=false,bool Settled=false)
{
 private IReadOnlyList<int> _original=[],_added=[];private IReadOnlyList<LangdaoOption> _selected=[];
 public IReadOnlyList<int> OriginalTargets {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> AddedTargets {get=>_added;init=>_added=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<LangdaoOption> SelectedOptions {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZhangYanReceipt? ZhangYan {get;init;} }
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZhangYanSujiReturn? ZhangYanSuji {get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ZhangYanLangdaoUse? ZhangYanLangdao {get;init;}
}
public sealed record ZhangYanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,SkillProgramEffectOp Operation,int Turn,int PhaseOwner,long? DamageId):IGameEvent;
public sealed record ZhangYanSujiIssuedEvent(ZhangYanSujiReturn Return):IGameEvent;
public sealed record ZhangYanSujiPaidEvent(long FrameId,long UseId,int CardId,CardLocation From,CardLocation To,long Before,long After,long BatchId):IGameEvent;
public sealed record ZhangYanSujiReturnedEvent(ZhangYanSujiReturn Return):IGameEvent;
public sealed record ZhangYanRewardIssuedEvent(long FrameId,long UseId,long DamageId,int PhaseOwner,int CardId,CardLocation From,long Before,long After):IGameEvent;
public sealed record ZhangYanLangdaoOptionFrozenEvent(long FrameId,int Index,LangdaoOption Option):IGameEvent;
public sealed record ZhangYanLangdaoVoteRevealedEvent(long FrameId,int Index,int Seat,LangdaoOption Option):IGameEvent;
public sealed record ZhangYanLangdaoIssuedEvent(long UseId,long ActionId,long FrameId,CardConversionSource Source,string GameplayHash,int OriginalActor,int OriginalTarget,int DamageBonus,int ExtraCount,bool NoResponse):IGameEvent;
public sealed record ZhangYanLangdaoTargetAddedEvent(long FrameId,long UseId,long ActionId,int Target,int Index):IGameEvent;
public sealed record ZhangYanLangdaoRedirectedEvent(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int InstructionIndex,int TargetIndex,int OriginalTarget,int NewTarget):IGameEvent;
public sealed record ZhangYanLangdaoKilledEvent(long UseId,long ActionId,long DyingId,long DamageId,int Victim):IGameEvent;
public sealed record ZhangYanLangdaoSettledEvent(long UseId,long ActionId,CardConversionSource Source,bool Killed):IGameEvent;
public sealed record ZhangYanLangdaoOptionRemovedEvent(long UseId,long ActionId,CardConversionSource Source,LangdaoOption Option):IGameEvent;
public sealed record ZhangYanCompletedEvent(long FrameId,bool Applied):IGameEvent;
internal interface IZhangYanHost {SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class ZhangYanDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op=>op;
 public override ISkillProgramEffectHandler Handler {get;}=new ZhangYanHandler(op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class UseBlackCardAtWoundedPlayStartDescriptor():ZhangYanDescriptor(SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];}
internal sealed class ResolveLangdaoSimultaneousSlashDescriptor():ZhangYanDescriptor(SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
internal sealed class ObtainSujiDamagedPhaseOwnerCardDescriptor():ZhangYanDescriptor(SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
internal sealed class ZhangYanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IZhangYanHost>().Execute(f,op);}
internal static class ZhangYanComposition
{
 internal static bool IsOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart or SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash or SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard;
 internal static void ValidateProgram(string path,SkillProgram p)
 {
  if(p.Activations.Any(a=>a.Effects.Any(e=>IsOperation(e.Op))))throw new InvalidOperationException(path+": Zhang Yan needs native phase/action/damage windows.");
  foreach(var t in p.Triggers.Where(t=>t.Effects.Any(e=>IsOperation(e.Op))))
  {
   if(t.Effects is not[var e]||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.MarkerCost is not null||t.ChoiceGroup is not null||t.DeferredTurnEndOnly||t.AllowNoEventTarget||t.IncludeResponseUses||t.SingleActionInstance||t.OnlyDesignatedCardTargets||t.AllowOwnDiscardPhaseEnded||t.EvaluateConditionAtResolution||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.GainPhaseQualification is not null||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardCategories.Count!=0||t.Suits.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.MovementDiscardOnly||t.IgnoreOwnSkillMovements||t.RequireNoCardConversion is not null||t.RequireDamageSource is not null||t.NoDyingAtActivation||
     (e.Op==SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart?t.Window!=SkillProgramTriggerWindow.PlayPhaseStarting||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional||t.OwnerRelation is not null||t.CardKinds.Count!=0||t.DamageOccurrence is not null||t.TurnOwnerScope is not(SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving):
      e.Op==SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard?t.Window!=SkillProgramTriggerWindow.AfterDamageApplied||t.Subject!=SkillProgramTriggerSubject.Any||t.Optional||t.OwnerRelation is not null||t.CardKinds.Count!=0||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage:
      t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!t.Optional||!t.CardKinds.SequenceEqual([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash])||t.DamageOccurrence is not null))throw new InvalidOperationException(path+": Zhang Yan needs a complete exact unfiltered native trigger.");
  }
  if(p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart))&&
    (p.Triggers.Count(t=>t.Effects is[{Op:SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart}]&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own)!=1||p.Triggers.Count(t=>t.Effects is[{Op:SkillProgramEffectOp.UseBlackCardAtWoundedPlayStart}]&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving)!=1||p.Triggers.Count(t=>t.Effects is[{Op:SkillProgramEffectOp.ObtainSujiDamagedPhaseOwnerCard}])!=1))throw new InvalidOperationException(path+": Suji needs both real phase directions and its actual damage reward.");
 }
}
