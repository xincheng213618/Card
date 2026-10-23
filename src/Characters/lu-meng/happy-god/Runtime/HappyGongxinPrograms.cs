using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyGongxinHost { void Bind(ProgramSkillFrame frame); }
internal sealed class BindHappyShenLuMengGongxinTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindHappyShenLuMengGongxinTarget;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new HappyGongxinHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new SelectSingleTarget()];
}
internal sealed class HappyGongxinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BindHappyShenLuMengGongxinTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h)
    { h.GetCapability<IHappyGongxinHost>().Bind(f); return SkillProgramStepOutcome.Continue; }
}
internal static class HappyGongxinComposition
{
    internal const string Skill = "happy:gongxin", Group = "sole-red-hand";
    [ProgramCatalogValidation(41600)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.BindHappyShenLuMengGongxinTarget)) return;
        if (p.Id != Skill || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers is not [var outgoing, var incoming] || outgoing.Id != "sole-outgoing" || incoming.Id != "sole-incoming" ||
            outgoing.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || incoming.OwnerRelation != SkillProgramCardActionOwnerRelation.Target ||
            p.Triggers.Any(t => !Valid(t)))
            throw new InvalidOperationException(path + ": Happy Gongxin requires the two sole Use directions sharing one actual-turn quota and the native red reveal/disposition chain.");
    }
    private static bool Valid(SkillProgramTrigger t) =>
        t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.Optional &&
        t.OnlyDesignatedCardTargets == (t.OwnerRelation == SkillProgramCardActionOwnerRelation.Target) &&
        !t.IncludeResponseUses && !t.SingleActionInstance && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 &&
        t.SourceSkillId is null && t.SourceViewAsId is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.UsageScope == SkillUsageScope.Turn && t.UsageLimit == 1 && t.NamedUsageGroup == Group && t.DynamicUsageLimit is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.RequiredPolarity is null &&
        t.Effects is [
            { Op: SkillProgramEffectOp.BindHappyShenLuMengGongxinTarget, Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always },
            { Op: SkillProgramEffectOp.RevealTargetHandCard, Target: SkillProgramEffectTarget.Owner, RevealMode: SkillProgramRevealMode.Chooser, AllowDecline: true, ResultBind: "shown", Suits: [Suit.Heart, Suit.Diamond] },
            { Op: SkillProgramEffectOp.ChooseOption, Target: SkillProgramEffectTarget.Owner, ResultBind: "disposition", Condition.Kind: SkillProgramConditionKind.BoundCardsMatchSuits, Options: [{ Id: "obtain" }, { Id: "top" }] },
            { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: "shown", Destination: SkillProgramCardDestination.OwnerHand, AwaitMovementTriggers: true,
                Condition.Kind: SkillProgramConditionKind.ChoiceIs, Condition.SourceBind: "disposition", Condition.OptionId: "obtain" },
            { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: "shown", Destination: SkillProgramCardDestination.DrawPileTop, AwaitMovementTriggers: true,
                Condition.Kind: SkillProgramConditionKind.ChoiceIs, Condition.SourceBind: "disposition", Condition.OptionId: "top" }
        ];
}

public sealed record HappyGongxinWitness(CardConversionSource Source, string GameplayHash, long WindowId,
    long UseId, long ActionId, int ActualTurn, int ActorSeat, int TargetSeat, int HandOwnerSeat);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyGongxinWitness? HappyGongxin { get; init; } }
public sealed record HappyGongxinTargetBoundEvent(long FrameId, HappyGongxinWitness Witness) : IGameEvent;
