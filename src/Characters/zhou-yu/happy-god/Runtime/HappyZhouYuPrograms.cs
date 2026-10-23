using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyZhouYuHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class HappyZhouYuQinyinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHappyZhouYuQinyin;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new HappyZhouYuQinyinHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered:ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseEnded)];
}
internal sealed class HappyZhouYuQinyinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHappyZhouYuQinyin;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int seat,ISkillProgramEffectHost host) => host.GetCapability<IHappyZhouYuHost>().Begin(frame);
}
internal static class HappyZhouYuComposition
{
    [ProgramCatalogValidation(41440)] internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Any(e=>e.Op==SkillProgramEffectOp.ResolveHappyZhouYuQinyin))return;
        if(p.Id!="happy:qinyin"||p.Activations.Count!=0||p.Modifiers.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||
            p.Triggers is not[{Id:"whole-turn-discard",Window:SkillProgramTriggerWindow.DiscardPhaseEnded,Subject:SkillProgramTriggerSubject.Owner,TurnOwnerScope:SkillProgramTurnOwnerScope.Own,AllowOwnDiscardPhaseEnded:true,Optional:true,UsageScope:null,MarkerCost:null,Condition.Kind:SkillProgramTriggerConditionKind.Always}t]||
            t.Effects is not[{Op:SkillProgramEffectOp.ResolveHappyZhouYuQinyin,Target:SkillProgramEffectTarget.Owner,TargetReference:null,Condition.Kind:SkillProgramConditionKind.Always}])
            throw new InvalidOperationException(path+": Happy Qinyin requires its actual own-discard ending, whole-turn real hand-discard qualification and one original three-way settlement.");
    }
}
public enum HappyZyStage { Choosing,Ready,HpLoss,Drawing,Children,Finished }
public sealed record HappyZyReceipt(CardConversionSource Source,string Hash,long Parent,int Turn,long DiscardInvocation,int DiscardCard)
{
    public HappyZyStage Stage {get;init;}
    public string? Mode {get;init;}
    public int Cursor {get;init;}
    public int Target {get;init;}=-1;
    public NativeDrawOutcome? Draw {get;init;}
    private readonly IReadOnlyList<int> _seats=[];
    public IReadOnlyList<int> Seats {get=>_seats;init=>_seats=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public HappyZyReceipt? HappyZhouYu {get;init;} }
public sealed record HappyZyStartedEvent(long FrameId,CardConversionSource Source,string Hash,long Parent,int Turn,long DiscardInvocation,int ParticipantCount):IGameEvent;
public sealed record HappyZyParticipantEvent(long FrameId,int Index,int Seat):IGameEvent;
public sealed record HappyZyModeEvent(long FrameId,string Mode):IGameEvent;
public sealed record HappyZyIssuedEvent(long FrameId,int Ordinal,int Target,string Mode,bool Skipped):IGameEvent;
public sealed record HappyZyDrawReturnedEvent(long FrameId,int Ordinal,int Target,int ActualCount,long Before,long After,long? AttemptFrameId):IGameEvent;
public sealed record HappyZyCompletedEvent(long FrameId):IGameEvent;
