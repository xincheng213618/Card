using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<YunzhengInitialEntity>? _yunzhengInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<YunzhengInitialEntity>? YunzhengInitialEntities
    { get => _yunzhengInitialEntities; init => _yunzhengInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuoxinReceipt? Huoxin { get; init; } }

internal interface ILeZouShiHost
{
    SkillProgramStepOutcome InitializeYunzheng(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveHuoxin(ProgramSkillFrame frame);
}
internal abstract class LeZouShiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(op == SkillProgramEffectOp.InitializeYunzhengInitialEntities ? SkillProgramTriggerWindow.GameStarting : SkillProgramTriggerWindow.CardUseCommitted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new LeZouShiHandler(op);
}
internal sealed class LeZouShiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.InitializeYunzhengInitialEntities ? host.GetCapability<ILeZouShiHost>().InitializeYunzheng(frame) : host.GetCapability<ILeZouShiHost>().ResolveHuoxin(frame);
}
internal static class LeZouShiComposition
{
    [ProgramCatalogValidation(870)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var ops = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id is not ("le:yunzheng" or "le:huoxin") && !ops.Any(e => e.Op is SkillProgramEffectOp.InitializeYunzhengInitialEntities or SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse)) return;
        if (p.Id is not ("le:yunzheng" or "le:huoxin") || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Triggers is not [var t] ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null ||
            t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.OrderGroup is not null ||
            t.MarkerCost is not null || t.SourceViewAsId is not null || t.DamageCardKinds.Count != 0 || t.Suits.Count != 0 ||
            t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
            t.ExcludedReasons.Count != 0 || t.MovementOccurrence is not null || t.DamageOccurrence is not null || t.GainPhaseQualification is not null ||
            t.EvaluateConditionAtResolution || t.NoDyingAtActivation || t.IgnoreOwnSkillMovements || t.DeferredTurnEndOnly ||
            t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded)
            throw new InvalidOperationException(path + ": Le Zou Shi requires her complete native opening and true-use contracts without extra limits.");
        if (p.Id == "le:huoxin") { new HuoxinCommittedUseContract().ValidateTrigger(t); return; }
        if (t.Id != "mark-initial-entities" || t.Window != SkillProgramTriggerWindow.GameStarting || t.Subject != SkillProgramTriggerSubject.Owner ||
            t.Optional || t.OwnerRelation is not null || t.SourceSkillId is not null || t.IncludeResponseUses || t.SingleActionInstance ||
            t.AllowNoEventTarget || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.InitializeYunzhengInitialEntities, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Yunzheng requires its exact frozen opening-Hand initialization.");
    }
}
