using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryJiedaoHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryJiedaoDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler => new AnniversaryJiedaoHandler(operation);
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardDamageModifier, static (_, c) => c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(operation == SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao ? SkillProgramTriggerWindow.BeforeDamageApplied : SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class ChooseAnniversaryMangYaChangJiedaoDescriptor() : AnniversaryJiedaoDescriptor(SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao);
internal sealed class PayAnniversaryMangYaChangJiedaoDescriptor() : AnniversaryJiedaoDescriptor(SkillProgramEffectOp.PayAnniversaryMangYaChangJiedao);
internal sealed class AnniversaryJiedaoHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryJiedaoHost>().Execute(frame, operation);
}
internal static class AnniversaryJiedaoComposition
{
    internal const string Skill = "anniversary:jiedao", Choose = "first-source-damage", Pay = "surviving-victim-cost";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao or SkillProgramEffectOp.PayAnniversaryMangYaChangJiedao;
    [ProgramCatalogValidation(44400)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => Handles(e.Op))) return;
        if (p.Id != Skill || p.Triggers.Count != 2 || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.Triggers.Any(t => !Exact(t)))
            throw new InvalidOperationException(path + ": Jiedao requires its exact source-stage choice and issued surviving-victim logical discard.");
    }
    private static bool Exact(SkillProgramTrigger t) => t.Subject == SkillProgramTriggerSubject.Any && t.OwnerRelation is null && t.Priority == 0 &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.SourceSkillId is null && !t.IncludeResponseUses && t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] &&
        (t.Id == Choose && t.Window == SkillProgramTriggerWindow.BeforeDamageApplied && t.Optional && t.IsSkillInvocation && t.DamageOccurrence is null && e.Op == SkillProgramEffectOp.ChooseAnniversaryMangYaChangJiedao ||
         t.Id == Pay && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && !t.Optional && !t.IsSkillInvocation && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && e.Op == SkillProgramEffectOp.PayAnniversaryMangYaChangJiedao);
}

public sealed record AnniversaryJiedaoSourceReceipt(long WindowId, long ParentId, long AttackId, CardConversionSource Source,
    string Hash, int ActualTurn, int Target, int ChainCursor, bool ChainPropagation, int OriginalAmount, int MaximumBonus);
public sealed record AnniversaryJiedaoGrant(long FrameId, AnniversaryJiedaoSourceReceipt Origin, int Bonus);
public sealed record AnniversaryJiedaoWindowIssuedEvent(AnniversaryJiedaoSourceReceipt Receipt) : IGameEvent;
public sealed record AnniversaryJiedaoWindowReturnedEvent(long WindowId, long AttackId, int Target, int ChainCursor) : IGameEvent;
public sealed record AnniversaryJiedaoChosenEvent(AnniversaryJiedaoGrant Grant) : IGameEvent;
public sealed record AnniversaryJiedaoCostBoundEvent(long FrameId, long GrantFrameId, long DamageFrameId) : IGameEvent;
public sealed record AnniversaryJiedaoCostPaidEvent(long FrameId, long GrantFrameId, long Before, long After, int ActualCount) : IGameEvent;
public enum AnniversaryJiedaoStage { BonusChoice, DiscardChoice, DiscardChildren }
public sealed record AnniversaryJiedaoFrame(AnniversaryJiedaoStage Stage, AnniversaryJiedaoSourceReceipt Origin,
    AnniversaryJiedaoGrant? Grant = null, long? DamageFrameId = null, long ParentWindowId = 0, int Required = 0, long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record BeforeDamageProgramWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryJiedaoSourceReceipt? AnniversaryJiedaoSource { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryJiedaoFrame? AnniversaryJiedao { get; init; }
}
