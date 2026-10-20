using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum FulueOption { Damage, Obtain }
public enum FulueStage { Option, Obtain, MovementChildren, CounterRequest }
public sealed record FulueUseEntry(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int UserSeat,int TargetSeat,FulueOption Option,bool Counter);
public sealed record FulueCounterInvoice(long ProgramFrameId,long OriginalUseId,long OriginalActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int ActorSeat,int TargetSeat,FulueOption OtherOption)
{ public bool Applied{get;init;} }
public sealed record FulueReceipt(long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int UserSeat,int TargetSeat,bool Counter,FulueStage Stage)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FulueOption? Option{get;init;}
    public long Before{get;init;} public long After{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public CardActionCost? Obtained{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? MoveSequence{get;init;}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FulueReceipt? Fulue{get;init;} }
public sealed partial record CardUseFrame
{
    private IReadOnlyList<FulueUseEntry>? _fulue;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<FulueUseEntry>? FulueEntries{get=>_fulue;init=>_fulue=value is null?null:Array.AsReadOnly(value.ToArray());}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public FulueCounterInvoice? FulueCounter{get;init;}
}
public sealed record FulueOptionExecutedEvent(FulueUseEntry Entry):IGameEvent;
public sealed record FulueCounterRequestedEvent(long ProgramFrameId,FulueCounterInvoice Invoice):IGameEvent;
public sealed record FulueCounterUseIssuedEvent(long UseId,long ActionId,FulueCounterInvoice Invoice):IGameEvent;
public sealed record FulueObtainPaidEvent(long ProgramFrameId,long UseId,long ActionId,int RecipientSeat,CardActionCost Material,long Before,long After,long MoveSequence):IGameEvent;
internal interface IPeiYuanShaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp op); }
internal abstract class PeiYuanShaoDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new PeiYuanShaoHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash
        ?[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted),new SelectSingleTarget()]
        :[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class OfferFulueSingleTargetOptionDescriptor:PeiYuanShaoDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferFulueSingleTargetOption;}
internal sealed class RequestFulueZeroDamageCounterSlashDescriptor:PeiYuanShaoDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash;}
internal sealed class ResolveFulueCounterOptionDescriptor:PeiYuanShaoDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFulueCounterOption;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class PeiYuanShaoHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IPeiYuanShaoHost>().Resolve(f,op);}
internal static class PeiYuanShaoComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.OfferFulueSingleTargetOption or SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash or SkillProgramEffectOp.ResolveFulueCounterOption;
    [ProgramCatalogValidation(370)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Id!="ol:fulue"||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Triggers.Count!=3)throw new InvalidOperationException(path+": Fulue requires all three exact native Use instructions.");
        foreach(var t in p.Triggers)
        {
            var op=t.Effects.FirstOrDefault()?.Op;
            if(op is null||!Handles(op.Value)||t.Subject is not null||t.Optional||t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.IncludeResponseUses||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Effects[0].Target!=SkillProgramEffectTarget.Owner||t.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always)
                throw new InvalidOperationException(path+": Fulue requires exact mandatory lifecycle instructions with optional choices owned by their receipt.");
            if(op==SkillProgramEffectOp.RequestFulueZeroDamageCounterSlash)
            {
                if(t.Window!=SkillProgramTriggerWindow.CardUseCompleted||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!t.SingleActionInstance||t.Effects is not[_,{Op:SkillProgramEffectOp.RequestSlashAgainstChosenTarget,Target:SkillProgramEffectTarget.SelectedTarget,ResultBind:"fulue-counter",ChooserRef.Kind:ProgramParticipantRef.SelectedTarget}])
                    throw new InvalidOperationException(path+": Fulue counter must return through its exact existing normal-range native assisted Slash request.");
            }
            else if(t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.Effects.Count!=1||t.OwnerRelation!=(op==SkillProgramEffectOp.OfferFulueSingleTargetOption?SkillProgramCardActionOwnerRelation.Actor:SkillProgramCardActionOwnerRelation.Observer))
                throw new InvalidOperationException(path+": Fulue benefits require the actual finalized native target frontier.");
        }
    }
}
