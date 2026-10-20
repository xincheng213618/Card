using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum QushiStage { DrawChildren, SealCard, SealTarget, SealChildren, RemoveChildren, RewardChildren }
public sealed record QushiSeal(long SealId,CardConversionSource Source,string GameplayHash,int HolderSeat,int CardId,CardKind PrintedKind,SkillProgramCardCategory Category,CardLocation Location,long MovementSequence);
public sealed record QushiReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,long ParentId,QushiStage Stage)
{
    public bool Paid {get;init;} public long Before {get;init;} public long After {get;init;} public int Step {get;init;} public int DrawCount {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? CardId {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? HolderSeat {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QushiSeal? Seal {get;init;}
}
public sealed record QushiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,long ParentId):IGameEvent;
public sealed record QushiMovementPaidEvent(long FrameId,int Step,QushiStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record QushiSealPlacedEvent(QushiSeal Seal):IGameEvent;
public sealed record QushiSealRemovedEvent(long FrameId,long SealId,int HolderSeat,int CardId,long Sequence,bool HolderDeath):IGameEvent;
public sealed record QushiRewardIssuedEvent(long FrameId,long SealId,bool SameCategory,int MaximumTargets,int DrawCount,int ActualTurn,int HolderSeat):IGameEvent;
public sealed record QushiCompletedEvent(long FrameId):IGameEvent;
// One scalar snapshot per real native use revision; no entity list or pending-use sidecar.
public sealed record QushiActualUseEvent(int ActualTurn,int TurnOwner,long OriginId,long ActionId,bool ResponseUse,int ActorSeat,CardKind EffectiveKind,int TargetCount):IGameEvent;
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QushiReceipt? Qushi {get;init;}}
public sealed partial record ProgramSkillWindowContext
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? QushiSealId {get;init;}}
internal interface IQushiHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class QushiDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new QushiHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.ResolveQushiIssuedHolderEnding?[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]:[];
}
internal sealed class ResolveQushiDrawAndPrivateSealDescriptor:QushiDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal;}
internal sealed class ResolveQushiIssuedHolderEndingDescriptor:QushiDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQushiIssuedHolderEnding;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class QushiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IQushiHost>().Resolve(f,op);
}
internal static class GuoTuQushiComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal or SkillProgramEffectOp.ResolveQushiIssuedHolderEnding;
    [ProgramCatalogValidation(630)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op)))&&!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Activations is not[var a]||a.Id!="draw-seal"||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=0||a.MaxTargets!=0||a.UsesPerPhase!=1||a.UsesPerTurn is not null||a.Effects is not[{Op:SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||
            p.Triggers is not[var t]||t.Id!="issued-holder-ending"||t.Window!=SkillProgramTriggerWindow.TurnEnding||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional||t.OwnerRelation is not null||t.UsageLimit is not null||t.UsageScope is not null||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.IncludeResponseUses||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Effects is not[{Op:SkillProgramEffectOp.ResolveQushiIssuedHolderEnding,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||p.ViewAs.Count!=0)
            throw new InvalidOperationException(path+": Qushi requires one actual-Play activation and its mandatory exact issued holder Ending.");
    }
}
