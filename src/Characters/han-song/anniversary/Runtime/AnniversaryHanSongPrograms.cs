using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IAnniversaryHanSongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class AnniversaryHanSongDescriptor : ProgramOperationDescriptorBase
{
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan;
 public override ISkillProgramEffectHandler Handler {get;} = new AnniversaryHanSongHandler();
 public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
 public override ProgramOperationAiPolicy AiPolicy {get;} = new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(1));
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
  [new RequireTriggerWindows([SkillProgramTriggerWindow.DrawPhaseEnded,SkillProgramTriggerWindow.DiscardPhaseEnded])];
}
internal sealed class AnniversaryHanSongHandler : ISkillProgramEffectHandler
{
 public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan;
 public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryHanSongHost>().Resolve(frame);
}
internal static class AnniversaryHanSongComposition
{
 internal const string Yinbi="anniversary:yinbi", Shuaiyan="anniversary:shuaiyan";
 [ProgramCatalogValidation(45900)] internal static void Validate(string path,SkillProgram p)
 {
  if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>e.Op==SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan))return;
  if(p.Id!=Shuaiyan||p.Triggers.Count!=4||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0||p.Triggers.Any(t=>!Exact(t)))
   throw new InvalidOperationException(path+": Shuaiyan requires exactly its optional own/other native Draw/Discard phase completions and one owner operation.");
 }
 private static bool Exact(SkillProgramTrigger t)=>t.Optional&&t.IsSkillInvocation&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Priority==0&&t.OwnerRelation is null&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.UsageScope is null&&t.UsageLimit is null&&t.MarkerCost is null&&t.NamedUsageGroup is null&&
  t.CardKinds.Count==0&&t.CardCategories.Count==0&&t.Suits.Count==0&&t.SourceZones.Count==0&&t.DestinationZones.Count==0&&
  t.Effects is [{Op:SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan,Target:SkillProgramEffectTarget.Owner,TargetReference:null,ActorReference:null,Condition.Kind:SkillProgramConditionKind.Always}]&&
  (t.Id switch {
   "own-draw-ended"=>t.Window==SkillProgramTriggerWindow.DrawPhaseEnded&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&!t.AllowOwnDiscardPhaseEnded,
   "other-draw-ended"=>t.Window==SkillProgramTriggerWindow.DrawPhaseEnded&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving&&!t.AllowOwnDiscardPhaseEnded,
   "own-discard-ended"=>t.Window==SkillProgramTriggerWindow.DiscardPhaseEnded&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&t.AllowOwnDiscardPhaseEnded,
   "other-discard-ended"=>t.Window==SkillProgramTriggerWindow.DiscardPhaseEnded&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving&&!t.AllowOwnDiscardPhaseEnded,
   _=>false});
}
public enum AnniversaryHanSongStage { BenefitChoice,CardChoice,Drawing,DrawChildren,DiscardChildren,Complete }
public sealed record AnniversaryHanSongReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,int SubjectSeat,int DrawCount,
 AnniversaryHanSongStage Stage,NativeDrawOutcome? Draw=null,int DiscardId=0,CardLocation? DiscardFrom=null,long Before=0,long After=0,long DiscardSequence=0);
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public AnniversaryHanSongReceipt? AnniversaryHanSong {get;init;}}
public sealed record AnniversaryHanSongStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,int SubjectSeat,int DrawCount):IGameEvent;
public sealed record AnniversaryHanSongBenefitIssuedEvent(long FrameId,bool Draw,int Count):IGameEvent;
public sealed record AnniversaryHanSongDrawReturnedEvent(long FrameId,int Count,long Before,long After):IGameEvent;
public sealed record AnniversaryHanSongDiscardPaidEvent(long FrameId,int SubjectSeat,long Sequence):IGameEvent;
public sealed record AnniversaryHanSongCompletedEvent(long FrameId):IGameEvent;
