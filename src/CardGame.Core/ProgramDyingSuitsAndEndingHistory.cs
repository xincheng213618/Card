namespace CardGame.Core;

public enum DyingSuitsStage { Recipient, Drawing, SelectingDiscard, Discarding, PeachIssued, Complete }
public sealed record DyingSuitsOriginalCursor(long DyingFrameId, long ParentFrameId, int VictimSeat, int? KillerSeat,
    DyingContinuationKind Continuation, ResolutionFrameStep Step, int ResponderIndex, IReadOnlyList<int> ResponderSeats,
    IReadOnlyList<string> AttemptedSelfDyingBindings, long EntryFrameId, int CandidateIndex, ResolutionFrameStep EntryStep,
    ProgramTriggerCandidate Candidate, ResolutionFrameKind ParentKind, ResolutionFrameStep ParentStep, string ParentHash,
    long? AttackOwnerFrameId = null, string? AttackOwnerHash = null)
{
    private readonly IReadOnlyList<int> _responderSeats = Array.AsReadOnly(ResponderSeats.ToArray());
    public IReadOnlyList<int> ResponderSeats { get => _responderSeats; init => _responderSeats = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<string> _attemptedSelfDyingBindings = Array.AsReadOnly(AttemptedSelfDyingBindings.ToArray());
    public IReadOnlyList<string> AttemptedSelfDyingBindings { get => _attemptedSelfDyingBindings; init => _attemptedSelfDyingBindings = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DyingSuitsOriginalCursorIssuedEvent(long ProgramFrameId, string CursorHash) : IGameEvent;
public sealed record DyingSuitsReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualRound, int ActualTurn, int TurnOwnerSeat, long EntryFrameId, long DyingFrameId, int VictimSeat,
    DyingSuitsOriginalCursor OriginalCursor, DyingSuitsStage Stage, IReadOnlyList<int> SelectedCardIds, IReadOnlyList<CardLocation> SelectedLocations,
    IReadOnlyList<Suit> DiscardSuits, int? RecipientSeat = null, long DrawBefore = 0, long DrawAfter = 0, int ActualDrawCount = 0,
    int RequiredDiscardCount = 0, long DiscardBefore = 0, long DiscardAfter = 0, DyingSuitsPeachReturn? PeachReturn = null)
{
    private readonly IReadOnlyList<int> _selectedCardIds = Array.AsReadOnly(SelectedCardIds.ToArray());
    public IReadOnlyList<int> SelectedCardIds { get => _selectedCardIds; init => _selectedCardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _selectedLocations = Array.AsReadOnly(SelectedLocations.ToArray());
    public IReadOnlyList<CardLocation> SelectedLocations { get => _selectedLocations; init => _selectedLocations = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<Suit> _discardSuits = Array.AsReadOnly(DiscardSuits.ToArray());
    public IReadOnlyList<Suit> DiscardSuits { get => _discardSuits; init => _discardSuits = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DyingSuitsPeachReturn(long ProgramFrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, long DyingFrameId, int VictimSeat, int ActorSeat, long CardUseFrameId, long CardActionId);
public sealed record DyingSuitsDrawIssuedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualRound, int ActualTurn, int TurnOwnerSeat, long EntryFrameId, long DyingFrameId, int VictimSeat,
    int RecipientSeat, long Before, long After, int ActualCount) : IGameEvent;
public sealed record DyingSuitsDiscardPaidEvent(long ProgramFrameId, int RecipientSeat, long Before, long After,
    IReadOnlyList<int> CardIds, IReadOnlyList<Suit> Suits) : IGameEvent
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<Suit> _suits = Array.AsReadOnly(Suits.ToArray());
    public IReadOnlyList<Suit> Suits { get => _suits; init => _suits = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DyingSuitsPeachIssuedEvent(DyingSuitsPeachReturn Return) : IGameEvent;
public sealed record DyingSuitsPeachReturnedEvent(DyingSuitsPeachReturn Return) : IGameEvent;

public sealed record OwnPlayEligibleUseRecordedEvent(int ActualTurn, int TurnOwnerSeat, long PhaseInstanceId,
    int ActorSeat, long CardActionId, CardKind EffectiveKind) : IGameEvent;
public sealed record EndingHistoricalUseSlot(long CardActionId, CardKind EffectiveKind, long PhaseInstanceId);
public sealed record EndingHistoricalUseReturn(long ProgramFrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, int ActualTurn, long EndingFrameId, int SlotIndex, long OriginalActionId,
    long CardUseFrameId, long CardActionId, int PhysicalCardId, CardKind EffectiveKind);
public sealed record EndingHistoricalUseReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long EndingFrameId, IReadOnlyList<EndingHistoricalUseSlot> Slots, int SlotIndex = 0,
    EndingHistoricalUseReturn? UseReturn = null, bool Complete = false)
{
    private readonly IReadOnlyList<EndingHistoricalUseSlot> _slots = Array.AsReadOnly(Slots.ToArray());
    public IReadOnlyList<EndingHistoricalUseSlot> Slots { get => _slots; init => _slots = Array.AsReadOnly(value.ToArray()); }
}
public sealed record EndingHistoricalUseIssuedEvent(EndingHistoricalUseReturn Return) : IGameEvent;
public sealed record EndingHistoricalUseReturnedEvent(EndingHistoricalUseReturn Return) : IGameEvent;

internal interface IDyingSuitsAndEndingHistoryHost
{
    SkillProgramStepOutcome DrawDiscardSuits(ProgramSkillFrame frame);
    SkillProgramStepOutcome EndingHistoricalUses(ProgramSkillFrame frame);
}
internal sealed class DrawThenDiscardSuitsForDyingPeachDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawThenDiscardSuitsForDyingPeachHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DyingRescue, static (_, c) => c.PublicControlValue(20d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op", "target", "condition"); var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition()); RequireAlways(e, r.Path); return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
internal sealed class UseOwnPlayHistoryAtEndingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseOwnPlayHistoryAtEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new UseOwnPlayHistoryAtEndingHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(12d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op", "target", "condition"); var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition()); RequireAlways(e, r.Path); return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class DrawThenDiscardSuitsForDyingPeachHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IDyingSuitsAndEndingHistoryHost)host).DrawDiscardSuits(f);
}
public sealed class UseOwnPlayHistoryAtEndingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseOwnPlayHistoryAtEnding;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IDyingSuitsAndEndingHistoryHost)host).EndingHistoricalUses(f);
}
internal static class DyingSuitsAndEndingHistoryComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        SkillProgramTriggerSubject subject, bool optional, SkillUsageScope? usageScope, int? usageLimit, SkillProgramTurnOwnerScope? turnOwnerScope)
    {
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach or SkillProgramEffectOp.UseOwnPlayHistoryAtEnding)) return;
        if (effects.Count != 1 || !optional || effects[0].Op == SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach &&
            (window != SkillProgramTriggerWindow.DyingEntering || subject != SkillProgramTriggerSubject.Any || usageScope != SkillUsageScope.Round || usageLimit != 1) ||
            effects[0].Op == SkillProgramEffectOp.UseOwnPlayHistoryAtEnding && (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner || turnOwnerScope != SkillProgramTurnOwnerScope.Own || usageScope is not null))
            throw new InvalidOperationException($"{path}: original-Dying suits and own-Ending historical use require their standalone optional typed window.");
    }
}
