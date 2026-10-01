namespace CardGame.Core;
internal interface IAlternatingBenefitsProgramHost
{
    void ApplyAlternatingChoiceBenefit(ProgramSkillFrame frame, string sourceBind, string stateId);
    SkillProgramStepOutcome ObtainDeckCardWithConsecutiveTarget(ProgramSkillFrame frame, int targetSeat, SkillProgramEffect effect);
}
internal sealed record RequireOwnTurnBoundary : ProgramResourceOperation;
internal sealed record RequireChoiceOptions(string Name, IReadOnlyList<string> Options) : ProgramResourceOperation;
internal sealed class ApplyAlternatingChoiceBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyAlternatingChoiceBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new ApplyAlternatingChoiceBenefitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (e,c)=>c.GrantTurnRuleModifier(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","sourceBind","stateId","condition");
        var effect=new SkillProgramEffect(Op,FilterBoundCardsProgramOperationDescriptor.Owner(r),0,r.Condition(),sourceBind:r.RequiredIdentifier("sourceBind"),stateId:r.RequiredIdentifier("stateId"));
        RequireAlways(effect,r.Path);return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),new RequireChoiceOptions(e.SourceBind!,["draw","targets"])];
}
internal sealed class ObtainDeckCardWithConsecutiveTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainDeckCardWithConsecutiveTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainDeckCardWithConsecutiveTargetHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e,c)=>c.Draw(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op","target","stateId","resultBind","cardSuits","cardCategories","condition");
        if(r.RequiredEnum<SkillProgramEffectTarget>("target")!=SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("A consecutive-target deck gift requires another selected participant.");
        var suits=r.RequiredEnumArray<Suit>("cardSuits");var categories=r.RequiredEnumArray<SkillProgramCardCategory>("cardCategories");
        if(suits.Count==0 || categories.Count==0) throw new InvalidOperationException("Deck matching requires nonempty suits and categories.");
        var e=new SkillProgramEffect(Op,SkillProgramEffectTarget.SelectedTarget,1,r.Condition(),stateId:r.RequiredIdentifier("stateId"),resultBind:r.RequiredIdentifier("resultBind"),suits:suits,cardCategories:categories);
        RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding),new RequireOwnTurnBoundary(),new ReadSelectedTarget(),new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),new CreateChoiceResult(e.ResultBind!,["repeat","new"])];
}
public sealed class ApplyAlternatingChoiceBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ApplyAlternatingChoiceBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)
    {((IAlternatingBenefitsProgramHost)host).ApplyAlternatingChoiceBenefit(f,e.SourceBind!,e.StateId!);return SkillProgramStepOutcome.Continue;}
}
public sealed class ObtainDeckCardWithConsecutiveTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ObtainDeckCardWithConsecutiveTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>((IAlternatingBenefitsProgramHost)host).ObtainDeckCardWithConsecutiveTarget(f,seat,e);
}
