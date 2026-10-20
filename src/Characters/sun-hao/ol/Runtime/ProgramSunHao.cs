using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SunHaoCanshiReceipt? SunHaoCanshi { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SunHaoChouhaiReceipt? SunHaoChouhai { get; init; }
}
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SunHaoChouhaiReceipt? SunHaoChouhai { get; init; } }
public sealed partial record JudgmentFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public SunHaoChouhaiReceipt? SunHaoChouhai { get; init; } }
internal interface ISunHaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class SunHaoDescriptor(SkillProgramEffectOp op, SkillProgramTriggerWindow window) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new SunHaoHandler(op);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(window)];
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
}
internal sealed class SunHaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<ISunHaoHost>().Resolve(f, op); }
internal sealed class SunHaoCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "ol:sun-hao-canshi";
    public void ValidateTrigger(SkillProgramTrigger t) => SunHaoComposition.ValidateCanshiTrigger(t);
}
internal static class SunHaoComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.GrantSunHaoCanshiDrawObligation or SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard;
    internal static void ValidateCanshiTrigger(SkillProgramTrigger t)
    {
        var draw = t.Id == "normal-draw-add-wounded";
        if (t.Effects is not [var e] || e.Op != (draw ? SkillProgramEffectOp.GrantSunHaoCanshiDrawObligation : SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard) ||
            e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Window != (draw ? SkillProgramTriggerWindow.DrawPhaseStarting : SkillProgramTriggerWindow.CardUseCommitted) || t.Optional != draw ||
            (draw ? t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || t.IncludeResponseUses :
                t.Id != "issued-turn-use-discard" || t.Subject is not null || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses) ||
            t.Priority != 0 || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null || t.MarkerCost is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.RequireNoCardConversion is not null ||
            t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageOccurrence is not null || t.RequireDamageSource is not null || t.MovementOccurrence is not null)
            throw new InvalidOperationException("Sun Hao requires his exact optional normal-draw addition and mandatory issued committed-use discard.");
    }
    [ProgramCatalogValidation(510)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var own = p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op))) || p.CardPolicies.Any(x => x.Kind is SkillProgramCardPolicyKind.OtherWuTreatedWoundedDuringOwnTurn or SkillProgramCardPolicyKind.EmptyHandDamageTakenBonus);
        if (!own) return;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection)
            throw new InvalidOperationException(path+": Sun Hao's operations and native metadata cannot be combined with unrelated producers.");
        if (p.Id == "ol:sun-hao-canshi")
        {
            if (p.Triggers.Count != 2 || p.CardPolicies.Count != 0 || p.Triggers.Select(t=>t.Id).Distinct().Count()!=2) throw new InvalidOperationException(path+": incomplete Canshi.");
            foreach (var t in p.Triggers) ValidateCanshiTrigger(t);
        }
        else if (p.Triggers.Count != 0 || p.CardPolicies is not [var policy] ||
            policy.Kind != (p.Id == "ol:guiming" ? SkillProgramCardPolicyKind.OtherWuTreatedWoundedDuringOwnTurn : p.Id == "ol:chouhai" ? SkillProgramCardPolicyKind.EmptyHandDamageTakenBonus : (SkillProgramCardPolicyKind)(-1)) ||
            policy.Value != 0 || policy.CardKinds.Count != 0 || policy.RequiredCardKinds.Count != 0 || policy.Condition.Kind != SkillProgramConditionKind.Always ||
            policy.InputSuit is not null || policy.OutputSuit is not null || policy.FactionId is not null || policy.OwnerRole is not null || policy.DiscardCost != 0 || policy.ProviderDrawCount != 0)
            throw new InvalidOperationException(path+": Chouhai and Guiming require only their exact zero-value native metadata.");
    }
}
