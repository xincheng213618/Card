using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BeiMiHuBeforeTurnProof(long PreludeFrameId,int UpcomingTurnNumber,int ActualRoundNumber,int UpcomingOwnerSeat,long RoundStartedEventSequence);
public sealed record BeiMiHuReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public long ParentFrameId {get;init;}
    public int Turn {get;init;}
    public int Round {get;init;}
    public int InstructionIndex {get;init;}
    public string Stage {get;init;}="";
    public int? DamageTarget {get;init;}
    public long? DamageParent {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public int ActualDraw {get;init;}
    public int CumulativeDraw {get;init;}
    public int MaximumBefore {get;init;}
    public int MaximumAfter {get;init;}
    public int RecoveryRequested {get;init;}
    public long? CardUseId {get;init;}
    public long? ActionId {get;init;}
    private IReadOnlyList<int> _candidates=Array.Empty<int>();
    public IReadOnlyList<int> Candidates {get=>_candidates;init=>_candidates=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _originalTargets=Array.Empty<int>();
    public IReadOnlyList<int> OriginalTargets {get=>_originalTargets;init=>_originalTargets=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _selectedTargets=Array.Empty<int>();
    public IReadOnlyList<int> SelectedTargets {get=>_selectedTargets;init=>_selectedTargets=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<PromptChoice> _choices=Array.Empty<PromptChoice>();
    public IReadOnlyList<PromptChoice> Choices {get=>_choices;init=>_choices=Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray());}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public BeiMiHuReceipt? BeiMiHu {get;init;}
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public BeiMiHuTargetMutation? BeiMiHuTargetMutation {get;init;}
}
public sealed record BeiMiHuChoiceStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int Round,string Kind,IReadOnlyList<int> Candidates):IGameEvent;
public sealed record BeiMiHuKuiPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,int Turn,int Round,string Kind,int TargetSeat,int Delta,int Remaining):IGameEvent;
internal interface IBeiMiHuProgramHost
{
    SkillProgramStepOutcome BeginBeiMiHuOperation(ProgramSkillFrame frame,SkillProgramEffectOp op);
}
internal sealed class BeiMiHuOperationHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IBeiMiHuProgramHost>().BeginBeiMiHuOperation(frame,op);
}
internal abstract class BeiMiHuDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler{get;}=new BeiMiHuOperationHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,_)=>{});
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)=>ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[];
}
internal static class BeiMiHuComposition
{
 internal static void ValidateActivation(string path,SkillProgramActivation activation)
 {
  if(activation.Effects.Any(e=>(int)e.Op is >=23200 and <=23205))throw new InvalidOperationException(path+": Bei Mi Hu operations require exact actual lifecycle/damage/card-action trigger windows.");
 }
 internal static void ValidateTrigger(string path,SkillProgramTrigger trigger)
 {
  var owned=trigger.Effects.Where(e=>(int)e.Op is >=23200 and <=23205).ToArray();if(owned.Length==0)return;
  if(trigger.Effects is not[var effect] || effect!=owned[0] || effect.Target!=SkillProgramEffectTarget.Owner || effect.Condition.Kind!=SkillProgramConditionKind.Always ||
     trigger.Condition.Kind!=SkillProgramTriggerConditionKind.Always || trigger.MarkerCost is not null || trigger.NamedUsageGroup is not null || trigger.DynamicUsageLimit is not null)
   throw new InvalidOperationException(path+": one exact native Bei Mi Hu instruction is required without unrelated costs or conditions.");
  var valid=effect.Op switch
  {
   SkillProgramEffectOp.MarkUnmarkedOtherBeforeActualTurn=>trigger.Window==SkillProgramTriggerWindow.TurnBeforeStart && trigger.Subject==SkillProgramTriggerSubject.Owner && trigger.Optional && trigger.UsageScope is null,
   SkillProgramEffectOp.MarkLowestHpUnmarkedOtherAtRoundStart=>trigger.Window==SkillProgramTriggerWindow.RoundStarting && trigger.Subject==SkillProgramTriggerSubject.Owner && !trigger.Optional && trigger.UsageScope is null,
   SkillProgramEffectOp.DrawAfterActualKuiDamage=>trigger.Window==SkillProgramTriggerWindow.AfterDamageApplied && trigger.Subject==SkillProgramTriggerSubject.Any && !trigger.Optional && trigger.DamageOccurrence==SkillProgramDamageOccurrence.PerDamage && trigger.UsageScope is null && trigger.DamageCardKinds.Count==0,
   SkillProgramEffectOp.AwakenFromActualGujuCards=>trigger.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && trigger.Subject==SkillProgramTriggerSubject.Owner && trigger.TurnOwnerScope==SkillProgramTurnOwnerScope.Own && !trigger.Optional && trigger.UsageScope==SkillUsageScope.Game && trigger.UsageLimit==1,
   _=>trigger.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized && trigger.Subject is null && trigger.Optional && trigger.SingleActionInstance && !trigger.IncludeResponseUses &&
      trigger.OwnerRelation==(effect.Op==SkillProgramEffectOp.ConsumeKuiAppendLegalTargets?SkillProgramCardActionOwnerRelation.Actor:SkillProgramCardActionOwnerRelation.Target) &&
      trigger.CardKinds.Count==0 && trigger.CardCategories.Count==2 && trigger.CardCategories.Contains(SkillProgramCardCategory.Basic) && trigger.CardCategories.Contains(SkillProgramCardCategory.InstantTrick) && trigger.UsageScope is null && trigger.SourceSkillId is null && trigger.SourceViewAsId is null
  };
  if(!valid)throw new InvalidOperationException(path+": Bei Mi Hu requires its exact compulsory/optional native window, occurrence and actor/target category contract.");
 }
}
