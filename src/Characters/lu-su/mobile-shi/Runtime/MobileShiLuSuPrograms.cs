using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiLsStage { Target, Pair, ExchangeChildren, Benefit, Discard, DiscardChildren, Drawing, DrawChildren, Finished }
public sealed record ShiLsReceipt(CardConversionSource Source, string Hash, int Turn, long? WindowId, ShiLsStage Stage)
{
    public long GrantId { get; init; }
    public long BatchId { get; init; }
    public int Target { get; init; } = -1;
    public int Lesser { get; init; } = -1;
    public int X { get; init; }
    public int Required { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public int FirstCount { get; init; }
    public int SecondCount { get; init; }
    public bool Paid { get; init; }
    private readonly IReadOnlyList<int> _seats = [], _cards = [];
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiLsReceipt? ShiLuSu { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CardUsePaymentMovementReceipt? ShiLuSuPayment { get; init; } }
public sealed record ShiLsStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, long? WindowId) : IGameEvent;
public sealed record ShiLsHandGrantEvent(long GrantId, CardConversionSource Source, string Hash, int Turn, int Beneficiary) : IGameEvent;
public sealed record ShiLsHandGrantExpiredEvent(long GrantId, long PreludeId) : IGameEvent;
public sealed record ShiLsMaterialAuthorizedEvent(long GrantId, long ActionId, int Actor, int CardId, CardLocation From, long UseId, int Before, int PaidSequence) : IGameEvent;
public sealed record ShiLsRefillIssuedEvent(long FrameId, long GrantId, long BatchId, int Requested) : IGameEvent;
public sealed record ShiLsExchangeIssuedEvent(long FrameId, int First, int Second, int FirstBefore, int SecondBefore, int Before) : IGameEvent;
public sealed record ShiLsExchangeCompletedEvent(long FrameId, int After) : IGameEvent;
public sealed record ShiLsBenefitBoundEvent(long FrameId, int Target, int X, bool Drawing) : IGameEvent;
public sealed record ShiLsDiscardPaidEvent(long FrameId, int Count, int Before, int After) : IGameEvent;
public sealed record ShiLsUsePaymentCapturedEvent(long UseId, long ActionId, long BatchId) : IGameEvent;

internal interface IShiLuSuHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class ShiLuSuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShiLuSuHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveMobileShiLuSuHaoshiRefill ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeLogicalDiscard, Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiLuSuHaoshi => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)],
        SkillProgramEffectOp.ResolveMobileShiLuSuHaoshiRefill => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)],
        _ => []
    };
}
internal sealed class ResolveMobileShiLuSuHaoshiDescriptor() : ShiLuSuDescriptor(SkillProgramEffectOp.ResolveMobileShiLuSuHaoshi);
internal sealed class ResolveMobileShiLuSuDimengDescriptor() : ShiLuSuDescriptor(SkillProgramEffectOp.ResolveMobileShiLuSuDimeng);
internal sealed class ResolveMobileShiLuSuHaoshiRefillDescriptor() : ShiLuSuDescriptor(SkillProgramEffectOp.ResolveMobileShiLuSuHaoshiRefill);
internal sealed class ShiLuSuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IShiLuSuHost>().Begin(f, op);
}
internal static class ShiLuSuComposition
{
    [ProgramCatalogValidation(48900)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = new[] { SkillProgramEffectOp.ResolveMobileShiLuSuHaoshi, SkillProgramEffectOp.ResolveMobileShiLuSuDimeng, SkillProgramEffectOp.ResolveMobileShiLuSuHaoshiRefill };
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => ops.Contains(e.Op))) return;
        static bool Plain(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = p.Id == "mobile:shi-haoshi" ? p.Activations.Count == 0 && p.Triggers.Count == 2 &&
            p.Triggers.Any(t => t.Id == "share-hand" && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional && Plain(t.Effects, ops[0])) &&
            p.Triggers.Any(t => t.Id == "shared-last-hand" && t.Window == SkillProgramTriggerWindow.CardsMoved && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional &&
                t.SourceZones.SequenceEqual([CardZoneKind.Hand]) && t.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && Plain(t.Effects, ops[2])) :
            p.Id == "mobile:shi-dimeng" && p.Triggers.Count == 0 && p.Activations is [{ Id: "exchange-hands", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 1, UsesPerTurn: null } a] && Plain(a.Effects, ops[1]);
        if (!valid || p.ViewAs.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": Shi Lu Su requires the ending hand grant, actual Hand movement refill and once-Play activation.");
    }
}
