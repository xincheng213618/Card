using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyDianWeiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame,SkillProgramEffectOp operation); }
internal abstract class HappyDianWeiDescriptor(SkillProgramEffectOp operation):ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>operation;
    public override ISkillProgramEffectHandler Handler{get;}=new HappyDianWeiHandler(operation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.Recover,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>operation switch
    {
        SkillProgramEffectOp.GrantHappyDianWeiShenwei=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],
        SkillProgramEffectOp.ConsumeHappyDianWeiShenwei=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)],
        _=>[new RequireTriggerWindow(SkillProgramTriggerWindow.HappyGuardRemoved),new CreateChoiceResult("elai-mode",["recover","damage","none"]),new SelectSingleTarget()]
    };
}
internal sealed class GrantHappyDianWeiShenweiDescriptor():HappyDianWeiDescriptor(SkillProgramEffectOp.GrantHappyDianWeiShenwei);
internal sealed class ConsumeHappyDianWeiShenweiDescriptor():HappyDianWeiDescriptor(SkillProgramEffectOp.ConsumeHappyDianWeiShenwei);
internal sealed class ResolveHappyDianWeiElaiDescriptor():HappyDianWeiDescriptor(SkillProgramEffectOp.ResolveHappyDianWeiElai);
internal sealed class HappyDianWeiHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IHappyDianWeiHost>().Resolve(f,op);
}
public enum HappyGuardStage { GrantChoice,GrantCommitted,GuardChoice,GuardPaid,RemovedChildren,Transferred,Declined,ElaiChoice,ElaiCommitted }
public sealed record HappyGuardReceipt(CardConversionSource Source,string GameplayHash,long ParentId,int ActualTurn,HappyGuardStage Stage,
    int GuardedSeat=-1,long AttackId=0,int DamageSource=-1,int Amount=0,DamageNature Nature=DamageNature.Normal,int GrantLimit=0,int ElaiTarget=-1,string? ElaiMode=null);
public sealed record HappyGuardRemovalReturn(long ParentId,int RemovedSeat,int GuardOwnerSeat,long RemovalId,bool DeathParent);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyGuardReceipt? HappyGuard{get;init;}
}
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public HappyGuardRemovalReturn? HappyGuardRemoval{get;init;}
}
public sealed record HappyGuardStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentId,int Turn,HappyGuardStage Stage,int GuardedSeat,long AttackId,int Amount,int GrantLimit):IGameEvent;
public sealed record HappyGuardPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long WindowId,long AttackId,int GuardedSeat,int DamageSource,int Amount,DamageNature Nature):IGameEvent;
public sealed record HappyGuardTransferredEvent(long FrameId,long AttackId,int OriginalTarget,int Target,int Amount):IGameEvent;
public sealed record HappyGuardElaiChoiceEvent(long FrameId,long RemovalId,int OwnerSeat,string Mode,int TargetSeat):IGameEvent;
internal static class HappyDianWeiComposition
{
    internal const string Shenwei="happy:shenwei",Elai="happy:elai",Kuangxi="happy:kuangxi";
    internal static bool IsOwnOp(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.GrantHappyDianWeiShenwei or SkillProgramEffectOp.ConsumeHappyDianWeiShenwei or SkillProgramEffectOp.ResolveHappyDianWeiElai;
    [ProgramCatalogValidation(41980)]internal static void Validate(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>IsOwnOp(e.Op)))return;
        var valid=p.Id==Shenwei?p.Triggers is[
            {Id:"grant-guards",Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,Subject:SkillProgramTriggerSubject.Owner,Optional:true,Effects:[{Op:SkillProgramEffectOp.GrantHappyDianWeiShenwei}]},
            {Id:"guard-damage",Window:SkillProgramTriggerWindow.BeforeDamageApplied,Subject:SkillProgramTriggerSubject.Any,Optional:false,Effects:[{Op:SkillProgramEffectOp.ConsumeHappyDianWeiShenwei}]}]:
            p.Id==Elai&&p.Triggers is[{Id:"removed-guard",Window:SkillProgramTriggerWindow.HappyGuardRemoved,Subject:SkillProgramTriggerSubject.Any,Optional:false,Effects:[
                {Op:SkillProgramEffectOp.ResolveHappyDianWeiElai},
                {Op:SkillProgramEffectOp.Recover,Target:SkillProgramEffectTarget.Owner,Amount:1,Condition:{Kind:SkillProgramConditionKind.ChoiceIs,SourceBind:"elai-mode",OptionId:"recover"}},
                {Op:SkillProgramEffectOp.Damage,Target:SkillProgramEffectTarget.SelectedTarget,Amount:1,DamageNature:DamageNature.Normal,Condition:{Kind:SkillProgramConditionKind.ChoiceIs,SourceBind:"elai-mode",OptionId:"damage"}}]}];
        if(!valid||p.Activations.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.BooleanStates.Count!=0||p.CardIdentities.Count!=0||p.Contributions.Count!=0||
            p.Triggers.Any(t=>t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.MarkerCost is not null||t.Effects.Where(e=>IsOwnOp(e.Op)).Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path+": Happy Dian Wei requires original turn-start guards, the protected character's real damage choice, and at most one native Elai outcome.");
    }
}
