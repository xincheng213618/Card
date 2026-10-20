using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum YanRouStage { DiscardChoice, DiscardChildren, TargetChoice, RecoveryChildren, DrawChildren }
public sealed record YanRouReshuffle(long BatchId,long ParentFrameId,long Before,long After)
{
    private IReadOnlyList<CardMovementRecord> _movements=[];
    public IReadOnlyList<CardMovementRecord> Movements {get=>_movements;init=>_movements=Array.AsReadOnly(value.ToArray());}
}
public sealed record YanRouReceipt(CardConversionSource Source,string GameplayHash,long WindowId,SkillProgramTriggerWindow Window,
    SkillProgramEffectOp Operation,int Turn,int TurnOwner,YanRouStage Stage,int Payer,int Amount=0,long? UseId=null,long? ActionId=null,
    CardUseDebitIdentity? Debit=null,int? Target=null,int? CardId=null,CardLocation? From=null,long Before=0,long After=0,int ActualCount=0)
{
    private IReadOnlyList<int> _grain=[];private IReadOnlyList<YanRouReshuffle> _reshuffles=[];
    public IReadOnlyList<int> WoodenOxGrainIds {get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<YanRouReshuffle> Reshuffles {get=>_reshuffles;init=>_reshuffles=Array.AsReadOnly(value.Select(s=>s with{Movements=s.Movements}).ToArray());}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public YanRouReceipt? YanRou {get;init;} }
public sealed record YanRouStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,SkillProgramTriggerWindow Window,SkillProgramEffectOp Operation,int Turn,int TurnOwner,int Payer,int Amount,long? UseId,long? ActionId):IGameEvent;
public sealed record YanRouMovementIssuedEvent(long FrameId,YanRouStage Stage,int Payer,int? CardId,CardLocation? From,long Before,long After,int Count):IGameEvent;
public sealed record YanRouReshuffleCapturedEvent(long FrameId,long BatchId,long Before,long After,int Count):IGameEvent;
public sealed record YanRouChoutaoAppliedEvent(long FrameId,long UseId,long ActionId,int Payer,int CardId,bool Refunded):IGameEvent;
public sealed record YanRouXiangshuSpentEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,int Turn,int Target,int Amount):IGameEvent;
public sealed record YanRouCompletedEvent(long FrameId,bool Applied):IGameEvent;
internal interface IYanRouHost {SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class YanRouDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler {get;}=new YanRouHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class DiscardSlashUserForUncancelableDescriptor():YanRouDescriptor(SkillProgramEffectOp.DiscardSlashUserForUncancelable)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
internal sealed class RecoverAndDrawByTurnDamageDescriptor():YanRouDescriptor(SkillProgramEffectOp.RecoverAndDrawByTurnDamage)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];}
internal sealed class YanRouHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IYanRouHost>().Execute(f,op);}
internal static class YanRouComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.DiscardSlashUserForUncancelable or SkillProgramEffectOp.RecoverAndDrawByTurnDamage;
    [ProgramCatalogValidation(240)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(p.Activations.Any(a=>a.Effects.Any(e=>IsOperation(e.Op))))throw new InvalidOperationException(path+": Yan Rou requires actual finalized Slash or original Ending windows.");
        foreach(var t in p.Triggers.Where(t=>t.Effects.Any(e=>IsOperation(e.Op))))
        {
            if(t.Effects is not [var e]||!IsOperation(e.Op)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||!t.Optional||
                t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||
                t.MarkerCost is not null||t.ChoiceGroup is not null||t.DeferredTurnEndOnly||t.AllowNoEventTarget||t.IncludeResponseUses||t.SingleActionInstance||t.OnlyDesignatedCardTargets||t.AllowOwnDiscardPhaseEnded||t.EvaluateConditionAtResolution||
                t.SourceSkillId is not null||t.SourceViewAsId is not null||t.GainPhaseQualification is not null||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardCategories.Count!=0||t.Suits.Count!=0||t.DamageCardKinds.Count!=0||
                t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.ExcludedReasons.Count!=0||t.MovementDiscardOnly||t.IgnoreOwnSkillMovements||t.RequireNoCardConversion is not null||t.RequireDamageSource is not null||t.DamageOccurrence is not null||t.NoDyingAtActivation||
                (e.Op==SkillProgramEffectOp.DiscardSlashUserForUncancelable
                    ? t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation is not(SkillProgramCardActionOwnerRelation.Actor or SkillProgramCardActionOwnerRelation.Target)||!t.CardKinds.Order().SequenceEqual(new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash}.Order())
                    : t.Window!=SkillProgramTriggerWindow.TurnEnding||t.Subject!=SkillProgramTriggerSubject.Owner||t.OwnerRelation is not null||t.CardKinds.Count!=0))
                throw new InvalidOperationException(path+": Yan Rou requires one exact optional finalized Slash direction or own actual Ending instruction without foreign limits.");
        }
        if(p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.DiscardSlashUserForUncancelable))&&
            (p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.DiscardSlashUserForUncancelable}]&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor)!=1||
             p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.DiscardSlashUserForUncancelable}]&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Target)!=1))
            throw new InvalidOperationException(path+": Choutao requires both its using and targeted directions.");
    }
}
