using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HeJinMouzhuReceipt? HeJinMouzhu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HeJinYanhuoReceipt? HeJinYanhuo { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HeJinMouzhuUseOrigin? HeJinMouzhu { get; init; }
}
public sealed record HeJinDeathCards(int OwnerSeat)
{
    private readonly IReadOnlyList<int> _handIds = Array.AsReadOnly(Array.Empty<int>()), _equipmentIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> HandIds { get => _handIds; init => _handIds = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> EquipmentIds { get => _equipmentIds; init => _equipmentIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record DeathFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HeJinDeathCards? HeJinYanhuoCards { get; init; }
}

internal interface IHeJinProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class HeJinHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IHeJinProgramHost>().Resolve(frame, operation);
}
internal abstract class HeJinDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new HeJinHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal static class HeJinComposition
{
    [ProgramCatalogValidation(230)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var active = p.Activations.Where(a => a.Effects.Any(e => e.Op is SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault or SkillProgramEffectOp.ResolveYanhuoKillerDiscard)).ToArray();
        var triggers = p.Triggers.Where(t => t.Effects.Any(e => e.Op is SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault or SkillProgramEffectOp.ResolveYanhuoKillerDiscard)).ToArray();
        if (active.Length + triggers.Length == 0) return;
        if (p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": He Jin operations require their exact standalone program.");
        if (active.Length != 0)
        {
            if (p.Id != "ol:mouzhu" || p.Activations.Count != 1 || p.Triggers.Count != 0 || active is not [var a] ||
                a.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
                a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
                a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always ||
                a.Id != "demand-hand-and-assault" || a.UsageGroup != a.Id || a.MarkerCost is not null || a.CardCountExpression is not null || a.ContinueAfterOwnerDeath ||
                a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot ||
                !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) || a.EquipmentSlots.Count != 0 || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0)
                throw new InvalidOperationException(path + ": Mouzhu requires its once-per-Play zero-card one-other-target complete native assault activation.");
        }
        if (triggers.Length != 0)
        {
            if (p.Id != "ol:yanhuo" || p.Activations.Count != 0 || p.Triggers.Count != 1 || triggers is not [var t] ||
                t.Effects is not [{ Op: SkillProgramEffectOp.ResolveYanhuoKillerDiscard, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
                t.Window != SkillProgramTriggerWindow.OwnerDied || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.UsageScope is not null || t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.OwnerRelation is not null)
                throw new InvalidOperationException(path + ": Yanhuo requires one optional original-owner death binding and no foreign input.");
        }
    }
}
