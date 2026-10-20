using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WeiwanStage { CostChildren, ObtainChildren, HealthIssuing, HealthChildren }
public enum WeiwanBranch { None, LoseHp, UnlimitedUse, ForbidUse }
public sealed record WeiwanSuitSample(int Index, Suit Suit, int? CardId, CardKind? PrintedKind,
    CardLocation? From, int CandidateCount, uint RandomBefore, uint RandomAfter);
public sealed record WeiwanReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int InstructionIndex, int ActualTurn, int TurnOwner,
    int CardId, CardKind PrintedKind, CardLocation CostFrom, Suit CostSuit, int TargetSeat, long SequenceBefore)
{
    public WeiwanStage Stage { get; init; }
    public long CostBatchId { get; init; }
    public long CostSequence { get; init; }
    public CardLocation CostTo { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long ObtainBatchId { get; init; }
    public int ActualCount { get; init; } = -1;
    public WeiwanBranch? Branch { get; init; }
    public bool HpLossCaptured { get; init; }
    public bool AppearancePrevented { get; init; }
    private readonly IReadOnlyList<WeiwanSuitSample> _samples = Array.AsReadOnly(Array.Empty<WeiwanSuitSample>());
    public IReadOnlyList<WeiwanSuitSample> Samples
    { get => _samples; init => _samples = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeiwanReceipt? Weiwan { get; init; }
}

public sealed record WeiwanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner, int CardId, CardKind PrintedKind,
    CardLocation CostFrom, Suit CostSuit, int TargetSeat, long SequenceBefore) : IGameEvent;
public sealed record WeiwanCostPaidEvent(long FrameId, long BatchId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record WeiwanSuitSampledEvent(long FrameId, int Index, Suit Suit, int? CardId,
    CardKind? PrintedKind, CardLocation? From, int CandidateCount, uint RandomBefore, uint RandomAfter,
    long CaptureSequence) : IGameEvent;
public sealed record WeiwanEntityObtainedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence, long BatchId) : IGameEvent;
public sealed record WeiwanObtainPaidEvent(long FrameId, long BatchId, int PrimaryCount, int ActualCount,
    long Before, long After) : IGameEvent;
public sealed record WeiwanTurnLease(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActorSeat, int TargetSeat, int ActualTurn, int TurnOwner, int ActualCount, long SequenceAfter);
public sealed record WeiwanTurnLeaseIssuedEvent(WeiwanTurnLease Lease) : IGameEvent;
public sealed record WeiwanHealthAppliedEvent(long FrameId, int TargetSeat, int Amount,
    int RemainingHp, bool PreventedByAppearance) : IGameEvent;
public sealed record WeiwanCompletedEvent(long FrameId, int CardId, int TargetSeat, int ActualCount,
    WeiwanBranch Branch) : IGameEvent;

// Empty snapshots are intentional: a later lease cannot retroactively authorize
// or prohibit a use that was already accepted before its payment children.
public sealed record WeiwanUseSnapshot(int ActorSeat, int ActualTurn, int TurnOwner, long CaptureSequence)
{
    private readonly IReadOnlyList<WeiwanTurnLease> _leases = Array.AsReadOnly(Array.Empty<WeiwanTurnLease>());
    public IReadOnlyList<WeiwanTurnLease> Leases
    { get => _leases; init => _leases = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WeiwanUseReceipt(long UseId, long ActionId, CardKind OriginalKind,
    WeiwanUseSnapshot Snapshot, long? DeclarationId);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeiwanUseReceipt? WeiwanUse { get; init; }
}
public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeiwanUseSnapshot? Weiwan { get; init; }
}
public sealed record WeiwanDeclarationCapturedEvent(long DeclarationId, long OwnerFrameId,
    int ActorSeat, int ProviderSeat, int CardId, CardLocation From, CardKind DeclaredKind,
    int ActualTurn, int TurnOwner, long CaptureSequence, int LeaseCount) : IGameEvent;
public sealed record WeiwanDeclarationLeaseCapturedEvent(long DeclarationId, int Index, long LeaseFrameId) : IGameEvent;
public sealed record WeiwanUseAcceptedEvent(long UseId, long ActionId, int ActorSeat, CardKind OriginalKind,
    int ActualTurn, int TurnOwner, long CaptureSequence, int LeaseCount, long? DeclarationId) : IGameEvent;
public sealed record WeiwanUseLeaseCapturedEvent(long UseId, int Index, long LeaseFrameId) : IGameEvent;

internal interface ILeXiaoQiaoWeiwanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal interface ILeXiaoQiaoWeiwanSelectionQuery : IProgramSelectionQuery { bool CanDiscard(int owner, Card card); }
[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 28620, SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain)]
internal sealed class WeiwanSelectionFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries) =>
        queries.Get<ILeXiaoQiaoWeiwanSelectionQuery>().CanDiscard(context.OwnerSeat, card);
}
internal sealed class ResolveWeiwanQinDiscardAndSuitObtainDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving), new ConsumeSelectedCards(1)];
    public override ISkillProgramEffectHandler Handler { get; } = new WeiwanHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WeiwanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<ILeXiaoQiaoWeiwanHost>().Resolve(frame);
}
internal static class WeiwanComposition
{
    [ProgramCatalogValidation(1230)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:weiwan" && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain)) return;
        if (p.Id != "le:weiwan" || p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Activations is not [var a] ||
            a.Id != "discard-qin-obtain-suits" || a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
            a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.UsageGroup != a.Id ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null || a.CategoryTargetLedgerId is not null ||
            a.TargetPhaseLedgerId is not null || a.CardCountExpression is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 ||
            a.TargetRequiresEmptyEquipmentSlot || a.OtherCharactersOnly || a.ContinueAfterOwnerDeath ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveWeiwanQinDiscardAndSuitObtain, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Weiwan requires one real Qin HE discard, one other target, one actual-Play allowance and its full random suit obtains and actual-count outcome.");
    }
}
