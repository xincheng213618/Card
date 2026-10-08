namespace CardGame.Core;

public enum CurrentTurnHandExchangeStage { Offered, DrawChildren, GiftChildren, SelectingReturn, ReturnChildren }

/// <summary>The single owning instruction retains both the actual gift and its later return selection.</summary>
public sealed record ProgramCurrentTurnHandExchangeReceipt(
    int InstructionIndex, CardConversionSource Source, string GameplayHash,
    long ParentFrameId, int ActualTurnNumber, int ParticipantSeat,
    CurrentTurnHandExchangeStage Stage, int ActualDrawCount = 0,
    int DrawBefore = 0, int DrawAfter = 0, int FrozenGivenCount = 0,
    int GiftBefore = 0, int GiftAfter = 0, int RequiredReturnCount = 0,
    int ReturnBefore = 0, int ReturnAfter = 0)
{
    private IReadOnlyList<int> _given = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _locations = Array.AsReadOnly(Array.Empty<CardLocation>());
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> GivenCardIds { get => _given; init => _given = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> CandidateCardIds { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> CandidateLocations { get => _locations; init => _locations = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedCardIds { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}

// Entity identities remain on the private owning frame. The existing native movement facts own their projections.
public sealed record CurrentTurnHandExchangeOfferedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long ParentFrameId, int ActualTurnNumber, int ParticipantSeat) : IGameEvent;
public sealed record CurrentTurnHandExchangeDrawIssuedEvent(long FrameId, int ParticipantSeat,
    int ActualCount, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record CurrentTurnHandExchangeGiftIssuedEvent(long FrameId, int ParticipantSeat,
    int OwnerSeat, int FrozenGivenCount, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record CurrentTurnHandExchangeReturnStartedEvent(long FrameId, int FrozenGivenCount,
    int AvailableCount, int RequiredReturnCount) : IGameEvent;
public sealed record CurrentTurnHandExchangeReturnIssuedEvent(long FrameId, int OwnerSeat,
    int ParticipantSeat, int FrozenGivenCount, int ActualCount, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record CurrentTurnHandExchangeResolvedEvent(long FrameId, string Outcome,
    int FrozenGivenCount, int ReturnedCount) : IGameEvent;

internal static class CurrentTurnHandExchangeContract
{
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        SkillProgramTurnOwnerScope turnOwnerScope, bool optional, SkillProgramTriggerCondition condition)
    {
        if (!effects.Any(effect => effect.Op == SkillProgramEffectOp.OfferCurrentTurnHandExchange)) return;
        if (effects is not [{ Op: SkillProgramEffectOp.OfferCurrentTurnHandExchange,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner || optional ||
            turnOwnerScope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving) ||
            condition is not { Kind: SkillProgramTriggerConditionKind.Compare,
                Left.Kind: SkillProgramTriggerValueKind.EventTargetHandCount,
                Comparison: SkillProgramComparisonOperator.LessThanOrEqual,
                Right: { Kind: SkillProgramTriggerValueKind.IntegerConstant, Value: 1 } })
            throw new InvalidOperationException($"Invalid skill program at {path}: current-turn hand exchange requires one unconditional owner instruction in mandatory owner TurnEnding, own or otherLiving scope, and frozen eventTargetHandCount <= 1.");
    }
}

internal sealed class OfferCurrentTurnHandExchangeProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferCurrentTurnHandExchange;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferCurrentTurnHandExchangeSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public interface ICurrentTurnHandExchangeHost
{
    SkillProgramStepOutcome OfferCurrentTurnHandExchange(ProgramSkillFrame frame);
}
public sealed class OfferCurrentTurnHandExchangeSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferCurrentTurnHandExchange;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host is ICurrentTurnHandExchangeHost exchange
        ? exchange.OfferCurrentTurnHandExchange(frame)
        : throw new InvalidOperationException("The program host does not support current-turn hand exchange.");
}
