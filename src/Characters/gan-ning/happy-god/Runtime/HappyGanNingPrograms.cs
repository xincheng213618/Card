using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyPoxiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,string bind); }
internal sealed class ResolveHappyGanNingPoxiDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveHappyGanNingPoxi;
    public override ISkillProgramEffectHandler Handler=>new ResolveHappyPoxiHandler();
    public override ProgramFactRequirements FactRequirements=>new(Activated:ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,context)=>context.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op","target","sourceBind","condition");
        var effect=new SkillProgramEffect(Op,reader.RequiredEnum<SkillProgramEffectTarget>("target"),0,reader.Condition(),sourceBind:reader.RequiredIdentifier("sourceBind"));
        if(effect.Target!=SkillProgramEffectTarget.Owner)throw new InvalidOperationException(reader.Path+": Happy Poxi resolves on its actual original owner.");
        RequireAlways(effect,reader.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>[new ReadSelectedTarget(),new ReadCardSet(effect.SourceBind!)];
}
internal sealed class ResolveHappyPoxiHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveHappyGanNingPoxi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host)=>host.GetCapability<IHappyPoxiHost>().Resolve(frame,effect.SourceBind!);
}
public enum HappyPoxiStage { DiscardChildren,MaximumChildren,RecoveryChildren,Drawing,DrawChildren }
public sealed record HappyPoxiReceipt(CardConversionSource Source,string GameplayHash,int TargetSeat,int OwnCount,int ActualTurn,HappyPoxiStage Stage)
{
    private IReadOnlyList<ShenLuSuMoveInvoice> _cost=Array.AsReadOnly(Array.Empty<ShenLuSuMoveInvoice>());
    public IReadOnlyList<ShenLuSuMoveInvoice> Cost {get=>_cost;init=>_cost=Array.AsReadOnly(value.ToArray());}
    public long Before {get;init;}
    public long After {get;init;}
    public long BatchId {get;init;}
    public int MaximumBefore {get;init;}
    public int MaximumAfter {get;init;}
    public long DrawBefore {get;init;}
    public long DrawAfter {get;init;}
    public int DrawCount {get;init;}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyPoxiReceipt? HappyPoxi {get;init;}
}
public sealed record HappyPoxiPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int TargetSeat,int OwnCount,int ActualTurn,long BatchId,long Before,long After):IGameEvent;
public sealed record HappyPoxiBenefitIssuedEvent(long FrameId,int OwnCount,int MaximumBefore,int MaximumAfter):IGameEvent;
public sealed record HappyPoxiDrawReturnedEvent(long FrameId,int Requested,int Actual,long Before,long After):IGameEvent;

internal static class HappyPoxiComposition
{
    [ProgramCatalogValidation(41680)] internal static void Validate(string path,SkillProgram program)
    {
        if(!program.Activations.SelectMany(a=>a.Effects).Concat(program.Triggers.SelectMany(t=>t.Effects)).Any(e=>e.Op==SkillProgramEffectOp.ResolveHappyGanNingPoxi))return;
        if(program.Id!="happy:poxi"||program.Triggers.Count!=0||program.Activations is not
            [{Id:"three-suit-discard",MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,TargetKind:SkillProgramTargetKind.OtherLiving,UsesPerPhase:1,UsesPerTurn:null,UsesPerGame:null,
              Effects:[{Op:SkillProgramEffectOp.SelectDistinctSuitHandDiscards,Target:SkillProgramEffectTarget.SelectedTarget,Amount:3,ResultBind:"three-suit-hands"},
                {Op:SkillProgramEffectOp.ResolveHappyGanNingPoxi,Target:SkillProgramEffectTarget.Owner,SourceBind:"three-suit-hands"}]}]||
            program.CardPolicies.Count!=0||program.Modifiers.Count!=0||program.BooleanStates.Count!=0||program.ViewAs.Count!=0||program.CardIdentities.Count!=0||
            program.Activations.SelectMany(a=>a.Effects).Any(e=>e.Condition.Kind!=SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path+": Happy Poxi requires the exact once-per-Play three-suit private discard and original-owner outcomes.");
    }
}
