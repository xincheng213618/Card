using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyLuSuHost
{
    SkillProgramStepOutcome BeginHappyDingzhou(ProgramSkillFrame frame);
}
internal sealed class ResolveHappyShenLuSuDingzhouDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveHappyShenLuSuDingzhou;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)=>ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler=>new HappyDingzhouHandler();
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new ReadSelectedTarget()];
}
internal sealed class HappyDingzhouHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveHappyShenLuSuDingzhou;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host)=>
        host.GetCapability<IHappyLuSuHost>().BeginHappyDingzhou(frame);
}

public enum HappyDingzhouStage { BranchChoice,FieldGiftChoice,FieldGiftChildren,FieldGainChildren,HandGainChildren,HandReturnChoice,HandReturnChildren }
public sealed record HappyDingzhouReceipt(CardConversionSource Source,string GameplayHash,int TargetSeat,HappyDingzhouStage Stage)
{
    public int Branch {get;init;}
    public int Amount {get;init;}
    public int? RequiredReturnCount {get;init;}
    private IReadOnlyList<int> _chosen=Array.AsReadOnly(Array.Empty<int>()),_obtained=Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<ShenLuSuMoveInvoice> _invoices=Array.AsReadOnly(Array.Empty<ShenLuSuMoveInvoice>());
    public IReadOnlyList<int> Chosen {get=>_chosen;init=>_chosen=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Obtained {get=>_obtained;init=>_obtained=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<ShenLuSuMoveInvoice> Invoices {get=>_invoices;init=>_invoices=Array.AsReadOnly(value.ToArray());}
    public long Before {get;init;}
    public long After {get;init;}
    public long BatchId {get;init;}
    public bool Paid {get;init;}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyDingzhouReceipt? HappyDingzhou {get;init;}
}
public sealed record HappyDingzhouStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat):IGameEvent;
public sealed record HappyDingzhouBranchChosenEvent(long FrameId,int Branch,int Count):IGameEvent;
public sealed record HappyDingzhouReturnRequiredEvent(long FrameId,int OriginalCount,int PayableCount):IGameEvent;
public sealed record HappyDingzhouCardChosenEvent(long FrameId,int Index):IGameEvent;
public sealed record HappyDingzhouMovementPaidEvent(long FrameId,HappyDingzhouStage Stage,long BatchId,int Count,long Before,long After):IGameEvent;

internal static class HappyLuSuComposition
{
    [ProgramCatalogValidation(41280)] internal static void Validate(string path,SkillProgram program)
    {
        if(!program.Activations.SelectMany(a=>a.Effects).Concat(program.Triggers.SelectMany(t=>t.Effects)).Any(e=>e.Op==SkillProgramEffectOp.ResolveHappyShenLuSuDingzhou))return;
        if(program.Id!="happy:dingzhou"||program.Triggers.Count!=0||program.Activations is not
            [{Id:"exchange",MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,TargetKind:SkillProgramTargetKind.OtherLiving,UsesPerPhase:1,UsesPerTurn:null,UsesPerGame:null,
                Effects:[{Op:SkillProgramEffectOp.ResolveHappyShenLuSuDingzhou,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]}]||
            program.ViewAs.Count!=0||program.BooleanStates.Count!=0||program.CardPolicies.Count!=0||program.Modifiers.Count!=0||program.CardIdentities.Count!=0)
            throw new InvalidOperationException(path+": Happy Dingzhou requires one exact once-per-Play native two-branch exchange.");
    }
}
