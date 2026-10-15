using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BuZhiReceipt
{
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public long ParentFrameId {get;init;}
    public long? BatchId {get;init;}
    public int Turn {get;init;}
    public int InstructionIndex {get;init;}
    public int? Target {get;init;}
    public string Stage {get;init;}="";
    public string ReturnStage {get;init;}="";
    public long Before {get;init;}
    public long After {get;init;}
    public int Actual {get;init;}
    public bool DamageIssued {get;init;}
    private IReadOnlyList<int> _paid=Array.Empty<int>();
    public IReadOnlyList<int> PaidIds {get=>_paid;init=>_paid=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public BuZhiReceipt? BuZhi {get;init;}
}
public sealed record BuZhiStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,long? BatchId,int Turn,int? Target):IGameEvent;
public sealed record BuZhiMovementPaidEvent(long FrameId,string ReturnStage,int Target,long Before,long After,int Actual,IReadOnlyList<int> CardIds):IGameEvent
{
    private IReadOnlyList<int> _ids=Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds {get=>_ids;init=>_ids=Array.AsReadOnly(value.ToArray());}
}
public sealed record BuZhiBranchSelectedEvent(long FrameId,int ChooserSeat,string Branch):IGameEvent;
public sealed record BuZhiDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat,int Amount):IGameEvent;
internal interface IBuZhiProgramHost {SkillProgramStepOutcome ResolveBuZhi(ProgramSkillFrame f,bool gainLoss);}
internal sealed class OfferNativeBatchHandGainOrCardLossBenefitDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit;
    public override ISkillProgramEffectHandler Handler{get;}=new BuZhiHandler(true);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
internal sealed class ResolveRebelCountEquipmentChoiceDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveRebelCountEquipmentChoice;
    public override ISkillProgramEffectHandler Handler{get;}=new BuZhiHandler(false);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ReadSelectedTarget()];
}
internal sealed class BuZhiHandler(bool gainLoss):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>gainLoss?SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit:SkillProgramEffectOp.ResolveRebelCountEquipmentChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>h.GetCapability<IBuZhiProgramHost>().ResolveBuZhi(f,gainLoss);
}
internal static class BuZhiComposition
{
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(e=>e.Op==SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit))return;
        if(t.Effects is not [{Op:SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit}] || t.Window!=SkillProgramTriggerWindow.CardsMoved ||
            t.Subject!=SkillProgramTriggerSubject.Owner || t.Optional || t.MovementOccurrence!=SkillProgramMovementOccurrence.PerOwnerBatch || t.UsageScope is not null)
            throw new InvalidOperationException(path+": native gain/loss benefit requires one mandatory per-owner batch instruction with its own optional recipient choice.");
    }
    internal static void ValidateActivation(string path,SkillProgramActivation a)
    {
        if(!a.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveRebelCountEquipmentChoice))return;
        if(a.Effects is not [{Op:SkillProgramEffectOp.ResolveRebelCountEquipmentChoice}] || a.MinCards!=0 || a.MaxCards!=0 || a.MinTargets!=1 || a.MaxTargets!=1 ||
            a.TargetKind!=SkillProgramTargetKind.AnyLiving || a.UsesPerPhase!=1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind!=SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path+": rebel-count equipment choice requires one actual living equipped target and its native dynamic phase limit.");
    }
}
