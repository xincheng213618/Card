using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SimultaneousPindianStage { Selecting = 21500, Revealing = 21501, Comparing = 21502, Consequence = 21503, MovementChildren = 21504, DeathChildren = 21505, Cleanup = 21506, RevealChildren = 21507, DeathCleanup = 21508, Claiming = 21509, ClaimMovementChildren = 21510, RandomPolicies = 21511, TopRefillChildren = 21512 }
public sealed record PindianRevealedCard(int Seat, int CardId, int OriginalRank, int Rank);
public sealed record SimultaneousPindianTopReceipt(int Seat,int CardId,int EffectiveRank,CardConversionSource Source,long ReservationMovementSequence);
public sealed record SimultaneousPindianReceipt
{
    private IReadOnlyList<int> _opponents = [];
    private IReadOnlyList<PindianRevealedCard> _cards = [];
    private IReadOnlyList<SimultaneousPindianTopReceipt> _top = [];
    public long ProgramFrameId { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int TurnNumber { get; init; }
    public int PhaseInstanceId { get; init; }
    public SimultaneousPindianStage Stage { get; init; }
    public IReadOnlyList<int> Opponents { get => _opponents; init => _opponents = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PindianRevealedCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public int OriginalSourceCardId { get; init; }
    public PindianRandomSelection? RandomSelection { get; init; }
    public IReadOnlyList<SimultaneousPindianTopReceipt> TopReceipts { get => _top; init => _top = Array.AsReadOnly(value.ToArray()); }
    public int SelectionIndex { get; init; }
    public int ComparisonIndex { get; init; }
    public int ConsequenceIndex { get; init; }
    public bool ComparisonIssued { get; init; }
    public PindianPolicyClaims? Claims { get; init; }
    public bool SelfMarkerIssued { get; init; }
    public bool Revealed { get; init; }
    public long RevealBefore { get; init; }
    public long RevealAfter { get; init; }
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public int? PaymentSeat { get; init; }
    public int? PaidCardId { get; init; }
    public CardLocation? PaidFrom { get; init; }
    public string? PaymentKind { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SimultaneousPindianReceipt? SimultaneousPindian { get; init; } }
public sealed record SimultaneousPindianStartedEvent(long ProgramFrameId, CardConversionSource Source, IReadOnlyList<int> Opponents) : IGameEvent;
public sealed record PindianCardsRevealedEvent(long ParentFrameId, IReadOnlyList<PindianRevealedCard> Cards, bool Simultaneous) : IGameEvent;
public sealed record PindianRevealRankAdjustedEvent(long WindowFrameId, CardConversionSource Source, int CardId, int Before, int Bonus, int After, string RestoredSkill, string UsageGroup, int PreviousPhaseUses) : IGameEvent;
public sealed record SimultaneousPindianConsequencePaidEvent(long ProgramFrameId, int ComparisonIndex, int ConsequenceIndex, int ParticipantSeat, string Branch, int? CardId, long MovementBefore, long MovementAfter) : IGameEvent;
public sealed record SimultaneousPindianCompletedEvent(long ProgramFrameId, CardConversionSource Source, int CompletedComparisons) : IGameEvent;
public sealed record SimultaneousPindianClaimsIssuedEvent(long ProgramFrameId, int ComparisonIndex, PindianPolicyClaims Claims) : IGameEvent;
public sealed record SimultaneousPindianClaimPaidEvent(long ProgramFrameId, int ComparisonIndex, int ClaimIndex, CardConversionSource Source, bool Taken, IReadOnlyList<int> CardIds, long MovementBefore, long MovementAfter) : IGameEvent;
public sealed record SimultaneousPindianTopCommittedEvent(long ProgramFrameId,int ParticipantSeat,CardConversionSource Source) : IGameEvent;
public sealed record PindianRevealWindowFrame : ResolutionFrame
{
    private IReadOnlyList<PindianRevealedCard> _cards = [];
    private IReadOnlyList<ProgramTriggerCandidate> _candidates = [];
    public PindianRevealWindowFrame(long id, long parentFrameId, bool simultaneous, IReadOnlyList<PindianRevealedCard> cards, IReadOnlyList<ProgramTriggerCandidate> candidates)
        : base(id, ResolutionFrameKind.PindianRevealWindow, ResolutionFrameStep.ResolvingEffect)
        => (ParentFrameId, Simultaneous, Cards, Candidates) = (parentFrameId, simultaneous, cards, candidates);
    public long ParentFrameId { get; init; }
    public bool Simultaneous { get; init; }
    public IReadOnlyList<PindianRevealedCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ProgramTriggerCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public int CandidateIndex { get; init; }
}
internal interface ISimultaneousPindianHost
{
    SkillProgramStepOutcome StartSimultaneousPindian(ProgramSkillFrame frame);
    void AddRaoshePindianRankAndRestoreGushe(ProgramSkillFrame frame, string skillId);
}
internal sealed class StartSimultaneousPindianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StartSimultaneousPindian;
    public override ISkillProgramEffectHandler Handler { get; } = new SimultaneousPindianHandler(SkillProgramEffectOp.StartSimultaneousPindian);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationLegalityPolicy LegalityPolicy => ProgramOperationLegalityPolicy.HandContest;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(5));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op", "target", "condition"); var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition()); RequireAlways(e, r.Path); return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ConsumeSelectedCards(1), new ReadTargetSet(1, 3)];
}
internal sealed class AddRaoshePindianRankAndRestoreGusheDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe;
    public override ISkillProgramEffectHandler Handler { get; } = new SimultaneousPindianHandler(SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(5));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op", "target", "sourceBind", "condition"); var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),sourceBind:r.RequiredIdentifier("sourceBind")); RequireAlways(e, r.Path); return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PindianCardsRevealed)];
}
internal sealed class SimultaneousPindianHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    { var h = host.GetCapability<ISimultaneousPindianHost>(); if (op == SkillProgramEffectOp.StartSimultaneousPindian) return h.StartSimultaneousPindian(f); h.AddRaoshePindianRankAndRestoreGushe(f,e.SourceBind!); return SkillProgramStepOutcome.Continue; }
}
internal static class SimultaneousPindianContract
{
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.StartSimultaneousPindian)) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.StartSimultaneousPindian }] || a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 3 ||
            a.TargetKind != SkillProgramTargetKind.OtherLivingWithHand || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || a.UsesPerPhase != 1 ||
            a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath)
            throw new InvalidOperationException($"{path}: simultaneous Pindian requires one own hand card, one-to-three other hand participants and one exact phase use.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe)) return;
        if (t.Effects is not [{ Op: SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe }] || t.Window != SkillProgramTriggerWindow.PindianCardsRevealed ||
            t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.NamedUsageGroup is not null)
            throw new InvalidOperationException($"{path}: JiCi requires one optional exact own Pindian reveal with no artificial usage quota.");
    }
}
