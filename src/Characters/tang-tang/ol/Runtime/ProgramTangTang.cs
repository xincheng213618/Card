using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum TangTangStage { ShownCard, FirstPeer, ShowChildren, PeerOption, TakeCards, TakeChildren, GiftChildren, HpIssuing, HpChildren, RepeatPeer, BenefitTarget, DrawChildren }
public sealed record TangTangEntity(int CardId, CardKind Kind, CardLocation From, Suit Suit);
public sealed record TangTangOwnedOrigin(int CardId, int OwnerSeat, CardLocation From, Suit Suit);
public sealed record TangTangGain(long BatchId, int OwnerSeat, int RecipientSeat, int CardId, bool Foreign, Suit Suit,
    long EntrySequence, long GainSequence, CardLocation From, int OtherHandCardId, Suit OtherHandSuit, long NativeDrawId = 0);
public sealed partial record CardMovementBatchContext
{
    private readonly IReadOnlyList<TangTangGain>? _tangTangGains;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TangTangGain>? TangTangGains { get => _tangTangGains; init => _tangTangGains = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record TangTangReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, int TurnOwner, long WindowId, TangTangStage Stage)
{
    public bool Paid { get; init; }
    public int Peer { get; init; } = -1;
    public int Recipient { get; init; } = -1;
    public int Step { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int ActualCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ActualDiscardRecoveryPhaseKey? PlayPhase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TangTangEntity? Shown { get; init; }
    private IReadOnlyList<int> _selected = [], _viewed = [], _grain = [];
    private IReadOnlyList<TangTangEntity> _materials = [], _candidates = [];
    private IReadOnlyList<TangTangGain> _gains = [];
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Viewed { get => _viewed; init => _viewed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Grain { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<TangTangEntity> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<TangTangEntity> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<TangTangGain> Gains { get => _gains; init => _gains = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TangTangReceipt? TangTang { get; init; } }

public sealed record TangTangStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, int TurnOwner, long WindowId, long PlayToken) : IGameEvent;
public sealed record ZexingShownEvent(long FrameId, int OwnerSeat, int CardId, CardKind Kind, CardLocation From, Suit Suit) : IGameEvent;
public sealed record ZexingPeerChosenEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, int Peer, int Index) : IGameEvent;
public sealed record ZexingOptionChosenEvent(long FrameId, int Peer, int Option) : IGameEvent;
public sealed record ZexingHandViewedEvent(long FrameId, int ViewerSeat, int Peer, int Count) : IGameEvent;
public sealed record ZexingHpLossIssuedEvent(long FrameId, int Peer, int Before, int After) : IGameEvent;
public sealed record TangTangMovementMaterialEvent(long FrameId, int Step, int Index, int CardId, CardKind Kind, CardLocation From, Suit Suit) : IGameEvent;
public sealed record TangTangGrainEvent(long FrameId, int Step, int Index, int CardId) : IGameEvent;
public sealed record TangTangEntityMovedEvent(long FrameId, int Step, TangTangStage Stage, long BatchId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record TangTangMovementIssuedEvent(long FrameId, int Step, TangTangStage Stage, long Before, long After, int Count) : IGameEvent;
public sealed record ZhiyiOwnedOriginEvent(long BatchId, long Sequence, int CardId, int OwnerSeat, CardLocation From, CardLocation To, Suit Suit) : IGameEvent;
public sealed record ZhiyiTransferProducerEvent(long EntrySequence, int RecipientSeat, long ProducerId) : IGameEvent;
public sealed record ZhiyiGainFrozenEvent(TangTangGain Gain) : IGameEvent;
public sealed record ZhiyiOpportunityPaidEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, long BatchId) : IGameEvent;
public sealed record ZhiyiRecipientChosenEvent(long FrameId, int RecipientSeat) : IGameEvent;
public sealed record TangTangCompletedEvent(long FrameId, bool Applied) : IGameEvent;

internal interface ITangTangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class TangTangDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op == SkillProgramEffectOp.ResolveZhiyiGainColorBenefit ? [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)] : [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new TangTangHandler(op);
}
internal sealed class ResolveZexingShownCardChainDescriptor() : TangTangDescriptor(SkillProgramEffectOp.ResolveZexingShownCardChain) {
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.TangTang is { Paid: true };
}
internal sealed class ResolveZhiyiGainColorBenefitDescriptor() : TangTangDescriptor(SkillProgramEffectOp.ResolveZhiyiGainColorBenefit) {
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.TangTang is { Paid: true };
}
internal sealed class TangTangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<ITangTangHost>().Resolve(f, op);
}
internal static class TangTangComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveZexingShownCardChain or SkillProgramEffectOp.ResolveZhiyiGainColorBenefit;
    [ProgramCatalogValidation(690)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => Handles(e.Op))) && !p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op)))) return;
        if (p.Id is not ("ol:zexing" or "ol:zhiyi") || p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Tang Tang requires her complete shown-card chain and one shared actual-turn gain opportunity.");
        if (p.Id == "ol:zexing")
        {
            if (p.Triggers.Count != 0 || p.Activations is not [var a] || a.Id != "shown-card-chain" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.UsesPerTurn is not null || a.UsesPerPhase != 1 || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveZexingShownCardChain, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Zexing requires exactly one native zero-input activation per actual Play phase.");
            return;
        }
        if (p.Activations.Count != 0 || p.Triggers is not [var t] || t.Id != "gain-color" || t.Window != SkillProgramTriggerWindow.CardsMoved || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || t.Priority != 0 || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || t.OwnerRelation is not null || !t.SourceZones.SequenceEqual([CardZoneKind.Hand]) || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.DamageOccurrence is not null || t.DynamicUsageLimit is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.IncludeResponseUses || t.SingleActionInstance || t.MarkerCost is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.OrderGroup is not null || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveZhiyiGainColorBenefit, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Zhiyi requires its exact optional frozen whole-gain binding without a second direction quota.");
    }
}
