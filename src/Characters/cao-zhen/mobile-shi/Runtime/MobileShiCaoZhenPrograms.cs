using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiCaoZStage { TopChildren, Assign, AssignmentChildren, UseRemovalChildren, Discard, DiscardChildren, EndChoice, EndChildren, DamageChildren, CleanupChildren, Complete }
public sealed record ShiCaoZMaterial(long PlacementId, long SetupId, int Index, CardConversionSource Source, string Hash, int Holder, int CardId, CardKind Kind, Suit Suit, CardLocation Location, int Sequence);
public sealed record ShiCaoZReceipt(CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId, SkillProgramEffectOp Operation, ShiCaoZStage Stage)
{
    public int Holder { get; init; } = -1;
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public Suit UsedSuit { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public int Assigned { get; init; }
    public bool Damage { get; init; }
    public bool Paid { get; init; }
    private readonly IReadOnlyList<int> _pool = [];
    private readonly IReadOnlyList<ShiCaoZMaterial> _materials = [];
    public IReadOnlyList<int> Pool { get => _pool; init => _pool = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ShiCaoZMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiCaoZReceipt? ShiCaoZhen { get; init; } }
public sealed record ShiCaoZStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record ShiCaoZTopTakenEvent(long FrameId, int Index, int CardId, int Sequence) : IGameEvent;
public sealed record ShiCaoZPlacedEvent(ShiCaoZMaterial Material) : IGameEvent;
public sealed record ShiCaoZRemovedEvent(long PlacementId, long FrameId, int CardId, int Sequence, string Reason) : IGameEvent;
public sealed record ShiCaoZUseIssuedEvent(long FrameId, CardConversionSource Source, long UseId, long ActionId, int Holder, long PlacementId, Suit UsedSuit, bool DifferentColor) : IGameEvent;
public sealed record ShiCaoZDiscardPaidEvent(long FrameId, int Holder, int CardId, int Before, int After) : IGameEvent;
public sealed record ShiCaoZEndIssuedEvent(long FrameId, CardConversionSource Source, int Turn, int Holder, int Count) : IGameEvent;
public sealed record ShiCaoZEndChoiceEvent(long FrameId, bool Obtain, bool SourceAlive, int Count) : IGameEvent;
public sealed record ShiCaoZDamageIssuedEvent(long FrameId, int Source, int Target) : IGameEvent;
public sealed record ShiCaoZCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;

internal interface IShiCaoZHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp operation); }
internal abstract class ShiCaoZDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new ShiCaoZHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => operation != SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(operation switch
    {
        SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace => SkillProgramTriggerWindow.TurnEnding,
        SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse => SkillProgramTriggerWindow.CardUseCompleted,
        _ => SkillProgramTriggerWindow.AfterTurnEnded
    })];
}
internal sealed class ResolveMobileShiCaoZhenSifengPlaceDescriptor() : ShiCaoZDescriptor(SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace);
internal sealed class ResolveMobileShiCaoZhenSifengUseDescriptor() : ShiCaoZDescriptor(SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse);
internal sealed class ResolveMobileShiCaoZhenSifengEndDescriptor() : ShiCaoZDescriptor(SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengEnd);
internal sealed class ShiCaoZHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IShiCaoZHost>().Begin(f, operation);
}
internal static class ShiCaoZComposition
{
    [ProgramCatalogValidation(49900)] internal static void Validate(string path, SkillProgram p)
    {
        var operations = new[] { SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace, SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengUse, SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengEnd };
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => operations.Contains(e.Op))) return;
        if (p.Id != "mobile:shi-sifeng" || p.Triggers.Count != 3 || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 ||
            p.Triggers.Any(t => t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] || t.Condition.Kind != SkillProgramTriggerConditionKind.Always) ||
            !p.Triggers.Any(t => t.Id == "place-forecast" && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && t.Effects[0].Op == operations[0]) ||
            !p.Triggers.Any(t => t.Id == "issued-use" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && !t.Optional && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && t.IncludeResponseUses && t.Effects[0].Op == operations[1]) ||
            !p.Triggers.Any(t => t.Id == "issued-end" && t.Window == SkillProgramTriggerWindow.AfterTurnEnded && !t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.AnyLiving && t.Effects[0].Op == operations[2]))
            throw new InvalidOperationException(path + ": Sifeng requires exactly the current three-card ending placement, completed native Use and issued actual-turn-end obligations.");
    }
}
