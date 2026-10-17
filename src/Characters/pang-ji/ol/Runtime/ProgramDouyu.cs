using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum DouyuWindowStage { BeforeReveal, WinDraw }
public enum DouyuStage { OpponentChoice, DrawWaiting, DrawChildren }

public sealed record DouyuOriginalCard(int Seat, int CardId, CardKind PrintedKind,
    CardLocation PaymentFrom, int OriginalRank, bool Top, long? ReservationSequence);
public sealed record DouyuAssignment(int Seat, int CardId, int Rank);
public sealed record DouyuSwap(long FrameId, long WindowId, int Ordinal,
    CardConversionSource Source, string GameplayHash, int OpponentSeat,
    int OwnerCardBefore, int OpponentCardBefore, int OwnerCardAfter, int OpponentCardAfter);
public sealed record DouyuOpportunity(ProgramTriggerCandidate Candidate)
{
    private readonly IReadOnlyList<int> _opponents = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Opponents { get => _opponents; init => _opponents = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DouyuContestReceipt(long ParentFrameId, bool Simultaneous,
    int SourceSeat, int ActualTurn, int TurnOwner, long SequenceBefore)
{
    private readonly IReadOnlyList<DouyuOriginalCard> _original = Array.AsReadOnly(Array.Empty<DouyuOriginalCard>());
    private readonly IReadOnlyList<DouyuAssignment> _assignments = Array.AsReadOnly(Array.Empty<DouyuAssignment>());
    private readonly IReadOnlyList<DouyuOpportunity> _opportunities = Array.AsReadOnly(Array.Empty<DouyuOpportunity>());
    private readonly IReadOnlyList<DouyuSwap> _swaps = Array.AsReadOnly(Array.Empty<DouyuSwap>());
    public IReadOnlyList<DouyuOriginalCard> OriginalCards { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DouyuAssignment> Assignments { get => _assignments; init => _assignments = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DouyuOpportunity> Opportunities { get => _opportunities; init => _opportunities = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DouyuSwap> Swaps { get => _swaps; init => _swaps = Array.AsReadOnly(value.ToArray()); }
    public bool Ready { get; init; }
    public bool Paid { get; init; }
    public long SequenceAfter { get; init; }
}
public sealed record DouyuWindowEntry(ProgramTriggerCandidate Candidate, long? SwapFrameId);
public sealed record DouyuWindowFrame : ResolutionFrame
{
    private readonly IReadOnlyList<DouyuWindowEntry> _entries = Array.AsReadOnly(Array.Empty<DouyuWindowEntry>());
    public DouyuWindowFrame(long id, long parent, DouyuWindowStage stage, int comparisonIndex,
        IReadOnlyList<DouyuWindowEntry> entries) : base(id, ResolutionFrameKind.DouyuWindow, ResolutionFrameStep.ResolvingEffect)
        => (ParentFrameId, Stage, ComparisonIndex, Entries) = (parent, stage, comparisonIndex, entries);
    public long ParentFrameId { get; init; }
    public DouyuWindowStage Stage { get; init; }
    public int ComparisonIndex { get; init; }
    public IReadOnlyList<DouyuWindowEntry> Entries { get => _entries; init => _entries = Array.AsReadOnly(value.ToArray()); }
    public int CandidateIndex { get; init; }
}
public sealed record DouyuTriggerContext(long ContestId, DouyuWindowStage Stage,
    int ComparisonIndex, long? SwapFrameId);
public sealed record DouyuReceipt(CardConversionSource Source, CardConversionSource IssuedSource,
    string GameplayHash, long ContestId, long WindowId, int ComparisonIndex,
    long? SwapFrameId, DouyuStage Stage)
{
    private readonly IReadOnlyList<int> _actual = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _replacement = Array.AsReadOnly(Array.Empty<int>());
    public long Before { get; init; }
    public long After { get; init; }
    public long? AttemptFrameId { get; init; }
    public bool DrawStarted { get; init; }
    public IReadOnlyList<int> ActualDrawIds { get => _actual; init => _actual = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> ReplacementGainedIds { get => _replacement; init => _replacement = Array.AsReadOnly(value.ToArray()); }
}
public abstract partial record ResolutionFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DouyuContestReceipt? DouyuContest { get; init; } }
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DouyuReceipt? Douyu { get; init; } }
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DouyuTriggerContext? Douyu { get; init; } }

public sealed record DouyuContestStartedEvent(long ContestId, bool Simultaneous, int SourceSeat,
    int ParticipantCount, int ActualTurn, int TurnOwner, long SequenceBefore) : IGameEvent;
public sealed record DouyuOriginalCardCommittedEvent(long ContestId, int Index, DouyuOriginalCard Card) : IGameEvent;
public sealed record DouyuCandidateCommittedEvent(long ContestId, int Ordinal,
    CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record DouyuSwapCommittedEvent(long ContestId, long WindowId, long FrameId, int Ordinal,
    CardConversionSource Source, string GameplayHash, int OpponentSeat,
    int OwnerCardBefore, int OpponentCardBefore, int OwnerCardAfter, int OpponentCardAfter) : IGameEvent;
public sealed record DouyuAssignmentFinalizedEvent(long ContestId, int Index, int Seat, int CardId, int Rank) : IGameEvent;
public sealed record DouyuNativePaymentEvent(long ContestId, int OriginalSeat, int CardId,
    CardKind PrintedKind, CardLocation From, long MovementSequence, bool AlreadyReserved) : IGameEvent;
public sealed record DouyuResultRecordedEvent(long ContestId, int ComparisonIndex, PindianResult Result) : IGameEvent;
public sealed record DouyuRewardClosedEvent(long ContestId, int ComparisonIndex, long SwapFrameId, bool DrawStarted) : IGameEvent;
public sealed record DouyuWinDrawStartedEvent(long FrameId, long ContestId, int ComparisonIndex,
    long SwapFrameId, CardConversionSource Source, int RecipientSeat, long Before) : IGameEvent;
public sealed record DouyuWinDrawReturnedEvent(long FrameId, long ContestId, int ComparisonIndex,
    long SwapFrameId, int RecipientSeat, int RequestedCount, int ActualCount, int ReplacementCount,
    long Before, long After, long? AttemptFrameId) : IGameEvent;
public sealed record DouyuWinDrawEntityEvent(long FrameId, int Index, int CardId, bool Replacement) : IGameEvent;

internal interface IDouyuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveDouyuPreRevealSwapAndWinDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new DouyuHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.PindianBeforeReveal, SkillProgramTriggerWindow.PindianResultDetermined])];
}
internal sealed class DouyuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IDouyuHost>().Resolve(frame);
}
internal static class DouyuComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
                .Any(e => e.Op == SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw)) return;
        if (p.Id != "ol:douyu" || p.Triggers.Count != 2 || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            !p.Triggers.Any(t => Exact(t, "pre-reveal", SkillProgramTriggerWindow.PindianBeforeReveal, true)) ||
            !p.Triggers.Any(t => Exact(t, "swapped-win", SkillProgramTriggerWindow.PindianResultDetermined, false)))
            throw new InvalidOperationException(path + ": Douyu requires its private native pre-reveal swap and exact issued swapped-pair win draw.");
    }
    private static bool Exact(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, bool optional) =>
        t.Id == id && t.Window == window && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional == optional &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.GainPhaseQualification is null &&
        t.RequireDamageSource is null && t.RequireNoCardConversion is null && t.CardCategories.Count == 0 &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets &&
        t.MovementOccurrence is null && t.DamageOccurrence is null && !t.EvaluateConditionAtResolution &&
        t.ChoiceGroup is null && t.OrderGroup is null && t.Suits.Count == 0 && t.MinimumRank == 1 && t.MaximumRank == 13 &&
        t.ExcludedReasons.Count == 0 && t.JudgmentReasons.Count == 0 && t.JudgmentSource is null &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && !t.IgnoreOwnSkillMovements &&
        !t.MovementDiscardOnly && t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Other &&
        t.HpChangeOccurrence == SkillProgramHpChangeOccurrence.PerEvent && t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive &&
        !t.NoDyingAtActivation && !t.DeferredTurnEndOnly && !t.AllowOwnDiscardPhaseEnded &&
        t.MarkerCost is null && t.OwnerRelation is null && t.SourceSkillId is null && t.SourceViewAsId is null &&
        !t.IncludeResponseUses && !t.SingleActionInstance && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.CardKinds.Count == 0 && t.DamageCardKinds.Count == 0 && t.Effects is [{ Op: SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw,
            Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
}
