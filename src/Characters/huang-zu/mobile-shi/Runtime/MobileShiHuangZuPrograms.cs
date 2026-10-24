using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record ShiHzStoredEvent(long GrantId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, long BatchId, int CardId, int DiscardSequence, PublicPersistentPileSource Pile,
    uint RandomBefore, uint RandomAfter, int CandidateCount, int CandidateIndex) : IGameEvent;
public sealed record ShiHzGrantConsumedEvent(long GrantId, long FrameId) : IGameEvent;
public sealed record ShiHzNativeIssuedEvent(long GrantId, long FrameId, long UseId, long ActionId, int CardId, int Target) : IGameEvent;
public sealed record ShiHzNativeReturnedEvent(long GrantId, long UseId) : IGameEvent;
public sealed record ShiHzColorsBoundEvent(long UseId, long ActionId, long PaymentFrameId, int OwnerSeat, bool Red, bool Black) : IGameEvent;
public sealed record ShiHzRecastPaidEvent(long FrameId, int TargetSeat, int CardId, CardLocation From, int Sequence) : IGameEvent;
public sealed record ShiHzRewardIssuedEvent(long FrameId, int OwnerSeat, int ActualCount) : IGameEvent;

public enum ShiHzStage { StoreChildren, UseChoice, IssuingUse, NativeUse, ReturnedUse, RecastChoice, RecastCostChildren, RecastDrawing, RecastDrawChildren, RewardDrawing, RewardChildren, CleanupChildren, Finished }
public sealed record ShiHzReceipt(CardConversionSource Source, string GameplayHash, int ActualTurn, long WindowId, ShiHzStage Stage)
{
    public long GrantId { get; init; }
    public int CardId { get; init; }
    public int Target { get; init; } = -1;
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public int ActualDraw { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<NativeRecastMaterial> _selected = [];
    public IReadOnlyList<NativeRecastMaterial> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiHzReceipt? ShiHuangZu { get; init; }
}
public sealed record ShiHzNativeUse(long GrantId, long ParentId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int CardId, CardKind Kind, CardLocation From, int Target, long UseId, long ActionId);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiHzNativeUse? ShiHuangZuUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiHzColorsBoundEvent? ShiHuangZuColors { get; init; }
}
internal interface IShiHuangZuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShiHuangZuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShiHuangZuHandler(op);
    public override ProgramOperationInteraction Interaction => op is SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang or SkillProgramEffectOp.ResolveMobileShiHuangZuStore ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeTrueUseProcessingEntry);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new ReadCardSet("chizhang-payment")],
        SkillProgramEffectOp.ResolveMobileShiHuangZuStore => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)],
        SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse => [new RequireTriggerWindow(SkillProgramTriggerWindow.ActualPhaseEnded)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]
    };
}
internal sealed class ResolveMobileShiHuangZuChizhangDescriptor() : ShiHuangZuDescriptor(SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang);
internal sealed class ResolveMobileShiHuangZuStoreDescriptor() : ShiHuangZuDescriptor(SkillProgramEffectOp.ResolveMobileShiHuangZuStore);
internal sealed class ResolveMobileShiHuangZuPhaseUseDescriptor() : ShiHuangZuDescriptor(SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse);
internal sealed class ResolveMobileShiHuangZuRecastDescriptor() : ShiHuangZuDescriptor(SkillProgramEffectOp.ResolveMobileShiHuangZuRecast);
internal sealed class ShiHuangZuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<IShiHuangZuHost>().Resolve(frame, op);
}

internal static class ShiHuangZuComposition
{
    [ProgramCatalogValidation(47900)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang or
            SkillProgramEffectOp.ResolveMobileShiHuangZuStore or SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse or SkillProgramEffectOp.ResolveMobileShiHuangZuRecast)) return;
        static bool Plain(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
            t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var okay = p.Id == "mobile:shi-chizhang" ? p.Triggers is [{ Id: "hand-damage-colors", Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, Optional: true, SingleActionInstance: true } t] &&
            t.Effects.Count == 3 && t.Effects[0].Op == SkillProgramEffectOp.SelectOwnedCards && t.Effects[0].Zones.SequenceEqual([CardZoneKind.Hand]) &&
            t.Effects[0].ResultBind == "chizhang-payment" && t.Effects[1].Op == SkillProgramEffectOp.ResolveMobileShiHuangZuChizhang &&
            t.Effects[2] is { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "chizhang-payment", Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true } :
            p.Id == "mobile:shi-duanyang" && p.Triggers.Count == 3 &&
            p.Triggers.Any(t => t.Id == "store-non-use-slash" && t.Window == SkillProgramTriggerWindow.DiscardPileReceived && t.Optional && t.UsageScope == SkillUsageScope.Turn && t.UsageLimit == 1 && Plain(t, SkillProgramEffectOp.ResolveMobileShiHuangZuStore)) &&
            p.Triggers.Any(t => t.Id == "use-current-phase-end" && t.Window == SkillProgramTriggerWindow.ActualPhaseEnded && !t.Optional && Plain(t, SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse)) &&
            p.Triggers.Any(t => t.Id == "damage-recast" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == SkillProgramTriggerSubject.DamageSource && t.Optional && Plain(t, SkillProgramEffectOp.ResolveMobileShiHuangZuRecast));
        if (!okay || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Shi Huang Zu requires a hand-damage cost, a once-turn actual discard entity, the exact current phase ending and native Slash damage recast.");
    }
}
