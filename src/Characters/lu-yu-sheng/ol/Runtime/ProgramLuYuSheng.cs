using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum LuYuShengStage { BottomChoice, BottomDiscardChildren, DrawChildren, RunweiChoice, RunweiDiscardChoice, RunweiDiscardChildren }
public sealed record LuYuShengReshuffle(long BatchId,long ParentFrameId,long Before,long After)
{
    private IReadOnlyList<CardMovementRecord> _moves=[];
    public IReadOnlyList<CardMovementRecord> Movements {get=>_moves;init=>_moves=Array.AsReadOnly(value.ToArray());}
}
public sealed record LuYuShengReceipt(CardConversionSource Source,string GameplayHash,long WindowId,SkillProgramTriggerWindow Window,
    SkillProgramEffectOp Operation,int Turn,int TurnOwner,LuYuShengStage Stage,int Payer,ActualDiscardRecoveryPhaseKey? Phase=null,
    long? AttackId=null,int OriginalDamage=0,int ChainCursor=0,bool ChainPropagation=false,int? SourceSeat=null,
    string? Branch=null,int? CardId=null,CardLocation? From=null,int Count=0,long Before=0,long After=0,int ActualCount=0)
{
    private IReadOnlyList<int> _bottom=[],_selected=[],_grain=[];private IReadOnlyList<LuYuShengReshuffle> _shuffles=[];
    public IReadOnlyList<int> BottomCards {get=>_bottom;init=>_bottom=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Selected {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> WoodenOxGrainIds {get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<LuYuShengReshuffle> Reshuffles {get=>_shuffles;init=>_shuffles=Array.AsReadOnly(value.Select(s=>s with{Movements=s.Movements}).ToArray());}
}
public sealed record LuYuShengDamageReceipt(long WindowId,long AttackId,int TargetSeat,int ChainCursor,int Before,int After,int? TransferredTargetSeat=null,long? TransferWindowId=null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public LuYuShengReceipt? LuYuSheng {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public LuYuShengDamageReceipt? LuYuShengDamage {get;init;}
}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public LuYuShengDamageReceipt? LuYuShengDamage {get;init;}}
public sealed partial record JudgmentFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public LuYuShengDamageReceipt? LuYuShengDamage {get;init;}}
public sealed record LuYuShengStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,SkillProgramEffectOp Operation,int Turn,int TurnOwner,int Payer,long? AttackId,int Amount):IGameEvent;
public sealed record LuYuShengBottomRevealedEvent(long FrameId,int Index,int CardId,Suit Suit):IGameEvent;
public sealed record LuYuShengMovementIssuedEvent(long FrameId,LuYuShengStage Stage,int Payer,long Before,long After,int Count):IGameEvent;
public sealed record LuYuShengReshuffleCapturedEvent(long FrameId,long BatchId,long Before,long After,int Count):IGameEvent;
public sealed record LuYuShengRunweiChosenEvent(long FrameId,int Payer,string Branch):IGameEvent;
public sealed record LuYuShengRunweiPhaseIssuedEvent(long FrameId,CardConversionSource Source,ActualDiscardRecoveryPhaseKey Phase,int Delta):IGameEvent;
public sealed record LuYuShengDamageAdjustedEvent(long FrameId,LuYuShengDamageReceipt Receipt,int Hearts,bool Applied):IGameEvent;
public sealed record LuYuShengDamageRebasedEvent(long WindowId,long AttackId,int TargetSeat,int ChainCursor,int Amount):IGameEvent;
public sealed record LuYuShengDamageTransferredEvent(long WindowId,long AttackId,int TargetSeat,int OriginalTargetSeat,int ChainCursor,long TransferWindowId):IGameEvent;
public sealed record LuYuShengCompletedEvent(long FrameId,bool Applied):IGameEvent;
internal interface ILuYuShengHost{SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class LuYuShengDescriptor(SkillProgramEffectOp op):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>op;
    public override ISkillProgramEffectHandler Handler {get;}=new LuYuShengHandler(op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.ChooseOption,static(_,c)=>c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
}
internal sealed class RevealBottomHeartsAndDrawDescriptor():LuYuShengDescriptor(SkillProgramEffectOp.RevealBottomHeartsAndDraw)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];}
internal sealed class DiscardBottomHeartsForDamageReductionDescriptor():LuYuShengDescriptor(SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];}
internal sealed class RunweiDrawOrDiscardForActualPhaseDescriptor():LuYuShengDescriptor(SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseStarting)];}
internal sealed class LuYuShengHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<ILuYuShengHost>().Execute(f,op);}
internal static class LuYuShengComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.RevealBottomHeartsAndDraw or SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction or SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase;
    [ProgramCatalogValidation(260)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(p.Activations.Any(a=>a.Effects.Any(e=>IsOperation(e.Op))))throw new InvalidOperationException(path+": Lu Yu Sheng requires actual Draw, before-damage or Discard windows.");
        foreach(var t in p.Triggers.Where(t=>t.Effects.Any(e=>IsOperation(e.Op))))
        {
            if(t.Effects is not [var e]||!IsOperation(e.Op)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||
                t.UsageScope is not null||t.UsageLimit is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.MarkerCost is not null||t.ChoiceGroup is not null||t.DeferredTurnEndOnly||t.AllowNoEventTarget||t.IncludeResponseUses||t.SingleActionInstance||t.OnlyDesignatedCardTargets||t.AllowOwnDiscardPhaseEnded||t.EvaluateConditionAtResolution||
                t.SourceSkillId is not null||t.SourceViewAsId is not null||t.GainPhaseQualification is not null||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Suits.Count!=0||t.DamageCardKinds.Count!=0||t.MovementReasons.Count!=0||t.ExcludedMovementReasons.Count!=0||t.ExcludedReasons.Count!=0||t.MovementDiscardOnly||t.IgnoreOwnSkillMovements||t.RequireNoCardConversion is not null||t.RequireDamageSource is not null||t.DamageOccurrence is not null||t.NoDyingAtActivation||t.OwnerRelation is not null||
                (e.Op==SkillProgramEffectOp.RevealBottomHeartsAndDraw?t.Window!=SkillProgramTriggerWindow.DrawPhaseStarting||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own||t.DrawPhaseMode!=SkillProgramDrawPhaseMode.Additive:
                 e.Op==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction?t.Window!=SkillProgramTriggerWindow.BeforeDamageApplied||t.Subject!=SkillProgramTriggerSubject.DamageTarget||t.Optional||t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own:
                 t.Window!=SkillProgramTriggerWindow.DiscardPhaseStarting||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional))
                throw new InvalidOperationException(path+": Lu Yu Sheng requires a complete exact unfiltered native trigger with its mandatory/optional direction.");
        }
        if(p.Triggers.Any(t=>t.Effects.Any(e=>e.Op is SkillProgramEffectOp.RevealBottomHeartsAndDraw or SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction))&&
            (p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.RevealBottomHeartsAndDraw}])!=1||p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction}])!=1))throw new InvalidOperationException(path+": Cangxin needs both draw and first-damage directions.");
        if(p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase))&&
            (p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase}]&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own)!=1||p.Triggers.Count(t=>t.Effects is [{Op:SkillProgramEffectOp.RunweiDrawOrDiscardForActualPhase}]&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving)!=1))throw new InvalidOperationException(path+": Runwei requires own and foreign actual Discard directions.");
    }
}
