using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record HuaManProgress(int MansiCards,bool Awakened=false);
public sealed record HuaManDirectionalUse(int ActualTurn,long ActionId,long NativeUseId,int ActorSeat,int ProviderSeat,CardKind Kind,bool Finalized=false)
{
 private readonly IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record SouyingQualification(int ActualTurn,long NativeUseId,long ActionId,int ActorSeat,int ProviderSeat,int TargetSeat,int DirectionOrdinal);
public sealed record SouyingNativeMutation(long ProgramFrameId,CardConversionSource Source,string GameplayHash,SouyingQualification Qualification,long PaymentBefore,long PaymentAfter,int DiscardedId,long ClaimBefore=0,long ClaimAfter=0,bool Cancelled=false)
{
 private readonly IReadOnlyList<int> _cards=Array.AsReadOnly(Array.Empty<int>());
 private readonly IReadOnlyList<int> _beforeTargets=Array.AsReadOnly(Array.Empty<int>());
 public IReadOnlyList<int> BeforeTargets{get=>_beforeTargets;init=>_beforeTargets=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> ClaimedEntities{get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
}
public enum HuaManStage{DiscardChoice,DiscardChildren,MansiDraw,ClaimChildren,XiliPayerDraw,XiliSourceDraw,MaximumChildren,RecoveryChildren,MaleChoice,GrantChildren}
public sealed record HuaManReceipt(CardConversionSource Source,string GameplayHash,int Instruction,long WindowId,int ActualTurn,HuaManStage Stage,long Before=0,long After=0,int ActualCount=0,int? DiscardedId=null,int? OtherSeat=null,long? DamageAttemptResolutionId=null,int? DamageTargetSeat=null,SouyingQualification? Souying=null,int RecoveryRequested=0,bool Paid=false,int NativeOwnerHpBefore=0,int NativeOwnerHpAfter=0);
public sealed partial class CharacterState
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuaManProgress? HuaManProgress{get;set;}
 private IReadOnlyList<HuaManDirectionalUse> _huaManUses=Array.AsReadOnly(Array.Empty<HuaManDirectionalUse>());
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<HuaManDirectionalUse>? HuaManUses{get=>_huaManUses.Count==0?null:_huaManUses;internal set=>_huaManUses=Array.AsReadOnly((value??[]).ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HuaManReceipt? HuaMan{get;init;}}
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SouyingQualification? HuaManSouyingQualification{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SouyingNativeMutation? HuaManSouyingMutation{get;init;}
}
public sealed partial record ProgramSkillWindowContext{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SouyingQualification? HuaManSouying{get;init;}}
public sealed record HuaManStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,int ActualTurn,SkillProgramEffectOp Operation):IGameEvent;
public sealed record HuaManMovementPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,HuaManStage Stage,long Before,long After,int ActualCount,int SubjectSeat):IGameEvent;
public sealed record MansiActualCardsObtainedEvent(long FrameId,CardConversionSource Source,int Before,int ActualObtained,int After):IGameEvent;
public sealed record SouyingNativeMutationEvent(long FrameId,long NativeUseId,long ActionId,CardConversionSource Source,bool Cancelled,int ClaimedCount,long PaymentBefore,long PaymentAfter,long ClaimBefore,long ClaimAfter):IGameEvent;
public sealed record XiliDamageIncreasedEvent(long FrameId,long DamageAttemptResolutionId,long BeforeDamageWindowId,int PayerSeat,int SourceSeat,int TargetSeat,int Before,int After):IGameEvent;
public sealed record ZhanyuanCompletedEvent(long FrameId,int OwnerSeat,int? MaleSeat,bool LostMansi):IGameEvent;
public sealed record HuaManCompletedEvent(long FrameId,CardConversionSource Source,string GameplayHash):IGameEvent;
internal interface IHuaManProgramHost{SkillProgramStepOutcome ExecuteHuaMan(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class HuaManDescriptor:ProgramOperationDescriptorBase
{
 public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
 public override ISkillProgramEffectHandler Handler=>new HuaManHandler(Op);
 public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
 public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
}
internal sealed class DrawAndCountMansiActualBarbarianDamageDescriptor:HuaManDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawAndCountMansiActualBarbarianDamage;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];}
internal sealed class PaySouyingDirectionalSoleTargetDescriptor:HuaManDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PaySouyingDirectionalSoleTarget;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
internal sealed class AwakenZhanyuanAndOfferXiliDescriptor:HuaManDescriptor{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
public override SkillProgramEffectOp Op=>SkillProgramEffectOp.AwakenZhanyuanAndOfferXili;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];}
internal sealed class PayXiliActualOtherOwnerDamageDescriptor:HuaManDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PayXiliActualOtherOwnerDamage;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
internal sealed class HuaManHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHuaManProgramHost>().ExecuteHuaMan(f,op);}
internal static class HuaManComposition
{
 internal static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.DrawAndCountMansiActualBarbarianDamage or SkillProgramEffectOp.PaySouyingDirectionalSoleTarget or SkillProgramEffectOp.AwakenZhanyuanAndOfferXili or SkillProgramEffectOp.PayXiliActualOtherOwnerDamage;
 internal static bool IsMansiAllHandActivation(SkillProgramActivation a)=>a.MinCards==1&&a.MaxCards==int.MaxValue&&a.MinTargets==0&&a.MaxTargets==0&&a.TargetKind==SkillProgramTargetKind.AnyLiving&&a.SourceZones.SequenceEqual([CardZoneKind.Hand])&&a.UsesPerPhase==1&&a.UsesPerTurn is null&&a.UsesPerGame is null&&a.MarkerCost is null&&a.CategoryTargetLedgerId is null&&a.TargetPhaseLedgerId is null&&!a.TargetRequiresEmptyEquipmentSlot&&a.Condition.Kind==SkillProgramConditionKind.Always&&!a.ContinueAfterOwnerDeath&&!a.SelectedCardsSameSuit&&!a.SelectedCardsDistinctSuits&&a.CardCountExpression is null&&a.EquipmentSlots.Count==0&&a.CardKinds.Count==0&&a.CardSuits.Count==0&&a.CardCategories.Count==0&&a.UsageGroup==a.Id&&a.Effects is[{Op:SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always,OutputKind:CardKind.BarbarianAssault,SourceBind:"all-hand-barbarian"}];
 internal static void Activation(string path,SkillProgramActivation a)
 {
  if(a.Effects.Any(e=>Own(e.Op)))throw new InvalidOperationException(path+": Hua Man operations require actual native trigger windows.");
 }
 internal static void Trigger(string path,SkillProgramTrigger t)
 {
  if(!t.Effects.Any(e=>Own(e.Op)))return;
  if(t.Effects.Count!=1||t.Effects[0].Target!=SkillProgramEffectTarget.Owner||t.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.SingleActionInstance||t.IncludeResponseUses||t.SourceSkillId is not null||t.SourceViewAsId is not null)throw new InvalidOperationException(path+": Hua Man requires one exact native operation with no unrelated cost or producer scope.");
  var op=t.Effects[0].Op;
  if(op==SkillProgramEffectOp.DrawAndCountMansiActualBarbarianDamage){if(t.Window!=SkillProgramTriggerWindow.AfterDamageApplied||t.Subject!=SkillProgramTriggerSubject.Any||t.OwnerRelation is not null||t.Optional||t.UsageLimit is not null||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage)throw new InvalidOperationException(path+": Mansi requires each actual Barbarian damage occurrence.");}
  else if(op==SkillProgramEffectOp.PaySouyingDirectionalSoleTarget){if(t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Observer||t.Subject is not null||!t.Optional||t.UsageScope!=SkillUsageScope.Turn||t.UsageLimit!=1)throw new InvalidOperationException(path+": Souying shares one quota across both real directions in each actual turn.");}
  else if(op==SkillProgramEffectOp.AwakenZhanyuanAndOfferXili){if(t.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow||t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.Optional||t.UsageScope!=SkillUsageScope.Game||t.UsageLimit!=1)throw new InvalidOperationException(path+": Zhanyuan requires the actual native preparation once in game.");}
  else if(t.Window!=SkillProgramTriggerWindow.BeforeDamageApplied||t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||!t.Optional||t.UsageScope!=SkillUsageScope.Turn||t.UsageLimit!=1)throw new InvalidOperationException(path+": Xili requires one optional actual other-source damage in each true turn.");
 }
}
