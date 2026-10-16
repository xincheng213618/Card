using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DingShangWanStage { OpeningDraw, OpeningGiftChoice, OpeningGiftChildren, OpeningDiscardChoice, OpeningDiscardChildren,
    PairOwnerDraw, PairTargetDraw, FengChoice, FengOwnerDraw, FengGiftChoice, FengGiftChildren, FengTargetDraw, FengDiscardChoice, FengDiscardChildren }
public sealed record DingShangWanReshuffle(long BatchId,long ParentFrameId,long Before,long After)
{
    private IReadOnlyList<CardMovementRecord> _movements=[];
    public IReadOnlyList<CardMovementRecord> Movements {get=>_movements;init=>_movements=Array.AsReadOnly(value.ToArray());}
}
public sealed record DingShangWanReceipt(CardConversionSource Source,string GameplayHash,int Instruction,long WindowId,
    SkillProgramTriggerWindow Window,SkillProgramEffectOp Operation,int Turn,int TurnOwner,DingShangWanStage Stage,
    int? Counterpart=null,long? DamageFrameId=null,long? ActionId=null,int? Recipient=null,string? Branch=null,
    int Payer=0,int Minimum=0,int Maximum=0,long Before=0,long After=0,int ActualCount=0)
{
    private IReadOnlyList<int> _candidates=[]; private IReadOnlyList<int> _selected=[]; private IReadOnlyList<int> _woodenOxGrainIds=[];
    private IReadOnlyList<DingShangWanReshuffle> _reshuffles=[];
    public IReadOnlyList<int> Candidates {get=>_candidates;init=>_candidates=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Selected {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> WoodenOxGrainIds {get=>_woodenOxGrainIds;init=>_woodenOxGrainIds=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<DingShangWanReshuffle> Reshuffles {get=>_reshuffles;init=>_reshuffles=Array.AsReadOnly(value.Select(s=>s with{Movements=s.Movements}).ToArray());}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public DingShangWanReceipt? DingShangWan {get;init;} }
public sealed record DingShangWanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,
    SkillProgramTriggerWindow Window,SkillProgramEffectOp Operation,int Turn,int TurnOwner,int? Counterpart,long? DamageFrameId,long? ActionId):IGameEvent;
public sealed record DingShangWanBranchEvent(long FrameId,string Branch,int? Recipient):IGameEvent;
public sealed record DingShangWanMovementIssuedEvent(long FrameId,DingShangWanStage Stage,int Payer,int? Recipient,long Before,long After,int Count):IGameEvent;
public sealed record DingShangWanReshuffleCapturedEvent(long FrameId,long BatchId,long Before,long After,int Count):IGameEvent;
public sealed record FudaoHandRecordedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int HandCount):IGameEvent;
public sealed record DingShangWanCompletedEvent(long FrameId,bool Applied):IGameEvent;
internal interface IDingShangWanHost { SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op); }
internal abstract class DingShangWanDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler {get;}=new DingShangWanHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class InitializeFudaoHandRecordDescriptor():DingShangWanDescriptor(SkillProgramEffectOp.InitializeFudaoHandRecord)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)]; }
internal sealed class DrawFudaoRecordedPairDescriptor():DingShangWanDescriptor(SkillProgramEffectOp.DrawFudaoRecordedPair)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)]; }
internal sealed class ResolveFengyanCounterpartDescriptor():DingShangWanDescriptor(SkillProgramEffectOp.ResolveFengyanCounterpart)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramTriggerWindow.CardResponseCompleted])]; }
internal sealed class DingShangWanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IDingShangWanHost>().Execute(f,op);
}
internal static class DingShangWanComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.InitializeFudaoHandRecord or SkillProgramEffectOp.DrawFudaoRecordedPair or SkillProgramEffectOp.ResolveFengyanCounterpart;
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(p.Activations.Any(a=>a.Effects.Any(e=>IsOperation(e.Op))))throw new InvalidOperationException(path+": Ding Shang Wan requires native game-start, actual-turn-end or completed-response/damage windows.");
        foreach(var t in p.Triggers.Where(t=>t.Effects.Any(e=>IsOperation(e.Op))))
        {
            if(t.Effects is not [var e]||!IsOperation(e.Op)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||
                t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||
                t.MarkerCost is not null||t.ChoiceGroup is not null||t.DeferredTurnEndOnly||t.AllowNoEventTarget||t.IncludeResponseUses||t.SingleActionInstance||t.OnlyDesignatedCardTargets||
                t.AllowOwnDiscardPhaseEnded||t.EvaluateConditionAtResolution||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.GainPhaseQualification is not null||
                t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Suits.Count!=0||t.DamageCardKinds.Count!=0||
                t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.ExcludedReasons.Count!=0||t.MovementDiscardOnly||t.IgnoreOwnSkillMovements||
                t.RequireNoCardConversion is not null||t.NoDyingAtActivation||
                (e.Op==SkillProgramEffectOp.InitializeFudaoHandRecord
                    ? t.Window!=SkillProgramTriggerWindow.GameStarting||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional||t.OwnerRelation is not null||t.RequireDamageSource is not null||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.DamageOccurrence is not null
                    : e.Op==SkillProgramEffectOp.DrawFudaoRecordedPair
                    ? t.Window!=SkillProgramTriggerWindow.AfterTurnEnded||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional||t.OwnerRelation is not null||t.RequireDamageSource is not null||t.DamageOccurrence is not null
                    : t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||
                        (t.Window==SkillProgramTriggerWindow.AfterDamageApplied
                            ? t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage||t.RequireDamageSource!=true
                            : t.Window!=SkillProgramTriggerWindow.CardResponseCompleted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.DamageOccurrence is not null||t.RequireDamageSource is not null)))
                throw new InvalidOperationException(path+": Ding Shang Wan requires one unfiltered exact native instruction with its declared timing and mandatory/optional direction.");
        }
        if(p.Triggers.Any(t=>t.Effects.Any(e=>e.Op is SkillProgramEffectOp.InitializeFudaoHandRecord or SkillProgramEffectOp.DrawFudaoRecordedPair))&&
            (p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.InitializeFudaoHandRecord}])!=1||
             p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.DrawFudaoRecordedPair}]&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own)!=1||
             p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.DrawFudaoRecordedPair}]&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving)!=1))
            throw new InvalidOperationException(path+": Fudao requires its one opening record and both actual-turn-end scopes.");
    }
}
