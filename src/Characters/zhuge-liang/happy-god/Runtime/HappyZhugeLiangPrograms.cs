namespace CardGame.Core;

internal interface IHappyZhugeLiangHost
{
    void GrantMist(ProgramSkillFrame frame);
}
internal sealed class GrantHappyZhugeLiangDawuDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.GrantHappyZhugeLiangDawu;
    public override ISkillProgramEffectHandler Handler=>new GrantHappyDawuHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)=>ParseOwnerAlways(reader);
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GrantArmor,static(_,context)=>context.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect)=>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding),new ReadCardSet("stars")];
}
internal sealed class GrantHappyDawuHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.GrantHappyZhugeLiangDawu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host)
    {
        host.GetCapability<IHappyZhugeLiangHost>().GrantMist(frame);return SkillProgramStepOutcome.Continue;
    }
}
public sealed record HappyDawuGrantedEvent(long FrameId,CardUseEffectSource Source,string GameplayHash,int ActualTurn,long PaymentSequence):IGameEvent;

internal static class HappyZhugeLiangComposition
{
    internal const string Dawu="happy:dawu",MistState="mist-active";
    [ProgramCatalogValidation(41480)] internal static void Validate(string path,SkillProgram program)
    {
        if(!program.Triggers.SelectMany(t=>t.Effects).Concat(program.Activations.SelectMany(a=>a.Effects))
            .Any(e=>e.Op==SkillProgramEffectOp.GrantHappyZhugeLiangDawu))return;
        if(program.Id!=Dawu||program.Activations.Count!=0||program.Triggers is not
            [{Id:"self-mist",Window:SkillProgramTriggerWindow.TurnEnding,Subject:SkillProgramTriggerSubject.Owner,TurnOwnerScope:SkillProgramTurnOwnerScope.Own,
                Optional:true,UsageLimit:null,UsageScope:null,Condition.Kind:SkillProgramTriggerConditionKind.Always,Effects:
                [{Op:SkillProgramEffectOp.SelectOwnedCards,Target:SkillProgramEffectTarget.Owner,Amount:1,NumberExpression:null,ResultBind:"stars",Zones:[CardZoneKind.PrivateReserve]},
                 {Op:SkillProgramEffectOp.MoveBoundCards,Target:SkillProgramEffectTarget.Owner,SourceBind:"stars",ExceptBind:null,Destination:SkillProgramCardDestination.DiscardPile,AwaitMovementTriggers:true},
                 {Op:SkillProgramEffectOp.GrantHappyZhugeLiangDawu,Target:SkillProgramEffectTarget.Owner}]}]||
            program.BooleanStates is not[{Id:MistState,InitialValue:false,Visibility:SkillProgramStateVisibility.Public,ResetScope:SkillProgramStateResetScope.Game}]||
            program.ViewAs.Count!=0||program.Modifiers.Count!=0||program.CardPolicies.Count!=0||program.CardIdentities.Count!=0||
            program.Triggers.SelectMany(t=>t.Effects).Any(e=>e.Condition.Kind!=SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path+": Happy Dawu requires its exact own-Ending one-star native payment and next-owner-turn self defense.");
    }
}
