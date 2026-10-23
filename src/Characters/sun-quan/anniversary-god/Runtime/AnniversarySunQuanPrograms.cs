using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversarySunQuanHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversarySunQuanDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversarySunQuanHandler(operation);
    public override ProgramOperationInteraction Interaction => operation == SkillProgramEffectOp.ResolveAnniversarySunQuanJichao ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => operation switch
    {
        SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingSetup => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn => [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramTriggerWindow.AfterDamageApplied])],
        SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)],
        _ => []
    };
}
internal sealed class AnniversarySunQuanSetupDescriptor() : AnniversarySunQuanDescriptor(SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingSetup);
internal sealed class AnniversarySunQuanReturnDescriptor() : AnniversarySunQuanDescriptor(SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn);
internal sealed class AnniversarySunQuanColorsDescriptor() : AnniversarySunQuanDescriptor(SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors);
internal sealed class AnniversarySunQuanJichaoDescriptor() : AnniversarySunQuanDescriptor(SkillProgramEffectOp.ResolveAnniversarySunQuanJichao);
internal sealed class AnniversarySunQuanHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversarySunQuanHost>().Resolve(f, operation);
}
internal static class AnniversarySunQuanComposition
{
    internal const string Cangming = "anniversary:cangming", Chouxi = "anniversary:chouxi", Jichao = "anniversary:jichao";
    internal static bool Owns(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingSetup or SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn or SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors or SkillProgramEffectOp.ResolveAnniversarySunQuanJichao;
    [ProgramCatalogValidation(40500)] internal static void Validate(string path, SkillProgram p)
    {
        static bool Exact(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Owns(e.Op)) && p.Id != Chouxi) return;
        var valid = p.Id switch
        {
            Cangming => p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Triggers.Count == 4 && p.Triggers.All(t => !t.Optional && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Subject == SkillProgramTriggerSubject.Owner && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && t.ChoiceGroup is null && t.RequiredPolarity is null &&
                (t.Id switch
                {
                    "initial-sea" => t.Window == SkillProgramTriggerWindow.GameStarting && Exact(t.Effects, SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingSetup),
                    "return-start" => t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && Exact(t.Effects, SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn),
                    "return-damage" => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && Exact(t.Effects, SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingReturn),
                    "sea-colors" => t.Window == SkillProgramTriggerWindow.CardsMoved && t.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && t.SourceZones.SequenceEqual([CardZoneKind.Hand]) && Exact(t.Effects, SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors),
                    _ => false
                })),
            Jichao => p.Triggers.Count == 0 && p.ViewAs.Count == 0 && p.Activations is [{ Id: "surge", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, UsesPerPhase: 1, UsesPerGame: null } a] && Exact(a.Effects, SkillProgramEffectOp.ResolveAnniversarySunQuanJichao),
            Chouxi => p.Triggers.Count == 0 && p.Activations.Count == 0 && p.ViewAs.Count > 0 && p.ViewAs.All(v => v.Id == "sea-" + v.OutputKind.ToString().ToLowerInvariant() && v.InputCount == 1 && !v.VariableInputCount && v.InputKinds.Count == 0 && v.InputSuits.Count == 0 && v.InputCategories.Count == 0 && v.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.WoodenOxGrain]) && v.Condition.Kind == SkillProgramConditionKind.Always && v.AllowSameKind && v.ForResponse == (v.OutputKind is CardKind.Dodge or CardKind.Nullification) && v.ForPlay != v.ForResponse && (CardUseCategoryCatalog.Get(v.OutputKind) == CardUseCategories.Basic ? v.ExtendedUse : v.OutputKind == CardKind.Nullification || v.SingleCardTrickUse)),
            _ => false
        };
        if (!valid || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": anniversary Sun Quan requires exact setup/issued return, native distinct-color reward, one Play surge and one-material sea-name conversions.");
    }
}
public enum AnniversarySunQuanStage { ChoosingBranch, ChoosingTarget, MovementChildren, Drawing, Finished }
public sealed record AnniversarySunQuanReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId)
{
    public AnniversarySunQuanStage Stage { get; init; }
    public int Branch { get; init; }
    public int Target { get; init; } = -1;
    public int ColorCount { get; init; }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<CardMovementRecord> _movements = [];
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversarySunQuanReceipt? AnniversarySunQuan { get; init; } }
public sealed record AnniversaryMingPileCreatedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int HolderSeat, CardLocation Location) : IGameEvent;
public sealed record AnniversarySunQuanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId) : IGameEvent;
public sealed record AnniversarySunQuanMovementPaidEvent(long FrameId, long BatchId, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversarySunQuanBranchChosenEvent(long FrameId, int Branch, int Target) : IGameEvent;
public sealed record AnniversarySunQuanCompletedEvent(long FrameId) : IGameEvent;
public sealed record AnniversaryJichaoLockedEvent(long FrameId, int OwnerSeat, string SkillInstanceId, string GameplayHash, int DamageTotal) : IGameEvent;
public sealed record AnniversaryChouxiNameUsedEvent(long ActionId, int OwnerSeat, int ActualTurn, CardKind Name, CardConversionSource Source) : IGameEvent;
