namespace CardGame.Core;

// A scalar fact about one actual use. It holds no hand entities or mutable
// collections; turn history is the ledger, not a second pending use object.
public sealed record CurrentTurnCardUseKindsRecordedEvent(
    int TurnNumber, int TurnOwnerSeat, int ActorSeat, long OriginFrameId,
    long? CardActionId, int HandSuitMask, int CardCategoryMask,
    bool IsNullificationUse = false) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TracksCurrentTurnUseKinds => _contentRegistry?.ProgramDependencies.TracksCurrentTurnUseKinds == true;

    private void ObserveCurrentTurnUseKinds(IGameEvent payload)
    {
        if (!TracksCurrentTurnUseKinds) return;
        if (payload is CardActionAcceptedEvent accepted)
        {
            var response = accepted.Action;
            if (response.Type != CardActionType.Response || response.EffectiveKind != CardKind.Nullification ||
                response.ActorSeat != response.ProviderSeat || response.ResponderSeat != response.ActorSeat ||
                response.RequesterSeat is not null) return;
            var window = _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
            var parent = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == window?.ParentFrameId);
            if (window is null || parent is null || response.ParentActionId != parent.Action?.ActionId ||
                response.OpponentSeat != window.SourceSeat || !IsValidPlayerSeat(response.ActorSeat))
                throw new InvalidOperationException("Actual Nullification use requires its exact live counterspell window and parent use.");
            if (EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent)
                .OfType<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.CardActionId == response.ActionId)) return;
            RecordCurrentTurnUseKinds(window.Id, response.ActorSeat, response, CardKind.Nullification, true);
            return;
        }
        if (payload is not CardUseDeclaredEvent declared) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == declared.ResolutionId);
        if (use is null || use.SourceSeat != declared.SourceSeat || use.CardId != declared.CardId ||
            use.CardKind != declared.CardKind || !IsValidPlayerSeat(use.SourceSeat))
            throw new InvalidOperationException("Actual turn use requires its exact declared card-use frame.");
        var action = use.Action;
        if (action is not null && action.Type != CardActionType.Use) return;
        if (action is not null && (action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind))
            throw new InvalidOperationException("Actual turn use differs from its frozen effective action.");
        // Legacy virtual uses have a real CardUse frame but deliberately no
        // Action. They add a type, never a manufactured hand suit or action.
        if (action is null && (use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            !IsActualTurnLegacyVirtualUse(use))) return;
        if (EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent)
            .OfType<CurrentTurnCardUseKindsRecordedEvent>().Any(e => !e.IsNullificationUse && e.OriginFrameId == use.Id)) return;
        RecordCurrentTurnUseKinds(use.Id, use.SourceSeat, action, use.CardKind, false);
    }

    private bool IsActualTurnLegacyVirtualUse(CardUseFrame use)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame producer ||
            producer.OwnerSeat != use.SourceSeat || producer.TriggerId is null) return false;
        var program = _contentRegistry.GetSkill(producer.SkillId).Program;
        if (program is null || program.GameplayHash != producer.GameplayHash) return false;
        var paused = ProgramInstructionResolver.Default.Resolve(producer, program)
            .GetPausedInstruction(producer.InstructionIndex).Effect;
        if (use.CardKind == CardKind.Slash)
            return producer.SelectedTargetSeats.Count == 1 && producer.SelectedTargetSeats.SequenceEqual(use.TargetSeats) &&
                paused is { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash, UseCardActionWindows: false };
        if (use.CardKind != CardKind.Alcohol || paused.Op != SkillProgramEffectOp.UseVirtualDyingAlcohol ||
            producer.WindowContext is not { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            context.TargetSeat != use.SourceSeat || !use.TargetSeats.SequenceEqual([use.SourceSeat])) return false;
        var dying = _resolutionStack.OfType<DyingFrame>().SingleOrDefault(d => d.Id == context.ParentFrameId);
        return dying is not null && dying.VictimSeat == use.SourceSeat &&
            dying.ResponderIndex >= 0 && dying.ResponderIndex < dying.ResponderSeats.Count &&
            dying.ResponderSeats[dying.ResponderIndex] == use.SourceSeat;
    }

    private void RecordCurrentTurnUseKinds(long frameId, int actorSeat, CardActionContext? action,
        CardKind effectiveKind, bool nullificationUse)
    {
        var handSuit = action is { ProviderSeat: var provider } && provider == action.ActorSeat &&
            action.PhysicalCards.Count > 0 && action.PhysicalCards.All(c => c.From == CardLocation.Hand(action.ActorSeat))
                ? ActualTurnSuitBit(action.EffectiveSuit) : 0;
        var category = GetProgramCardCategory(effectiveKind);
        AdvanceEventRulesAndQueueFact(new CurrentTurnCardUseKindsRecordedEvent(
            _turnNumber, _currentSeat, actorSeat, frameId, action?.ActionId,
            handSuit, 1 << (int)category, nullificationUse));
    }

    private static int ActualTurnSuitBit(Suit? suit) => suit switch
    {
        Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0
    };

    private (int HandSuits, int Categories) CurrentTurnUseKinds(int actorSeat)
    {
        if (!TracksCurrentTurnUseKinds) return (0, 0);
        var hand = 0; var categories = 0;
        foreach (var fact in EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent)
            .OfType<CurrentTurnCardUseKindsRecordedEvent>().Where(e => e.TurnNumber == _turnNumber &&
                e.TurnOwnerSeat == _currentSeat && e.ActorSeat == actorSeat))
        {
            hand |= fact.HandSuitMask; categories |= fact.CardCategoryMask;
        }
        return (System.Numerics.BitOperations.PopCount((uint)hand),
            System.Numerics.BitOperations.PopCount((uint)categories));
    }

    // 观微 condition: the current turn player used at least two cards this turn
    // and every one of those uses carried exactly one shared hand suit. A use
    // without hand-card material (a virtual declaration) has no suit and fails
    // the shared-suit requirement.
    private bool CurrentTurnOwnerUsedSameSuitCards()
    {
        if (!TracksCurrentTurnUseKinds) return false;
        var mask = 0;
        var uses = 0;
        foreach (var fact in EventsSinceLastBoundary(e => e is TurnStartedEvent or TurnEndedEvent)
            .OfType<CurrentTurnCardUseKindsRecordedEvent>().Where(e => e.TurnNumber == _turnNumber &&
                e.TurnOwnerSeat == _currentSeat && e.ActorSeat == _currentSeat))
        {
            if (fact.HandSuitMask == 0) return false;
            mask |= fact.HandSuitMask;
            uses++;
        }
        return uses >= 2 && System.Numerics.BitOperations.PopCount((uint)mask) == 1;
    }

    private int CurrentTurnCategoryDrawCount(ProgramSkillFrame frame, int targetSeat)
    {
        var context = frame.WindowContext;
        var window = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
            .SingleOrDefault(w => w.Id == context?.ParentFrameId);
        if (targetSeat != frame.OwnerSeat || context?.Window != SkillProgramTriggerWindow.PlayEnding ||
            window is null || window.Window != SkillProgramTriggerWindow.PlayEnding || window.OwnerSeat != frame.OwnerSeat ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex] is not { } candidate ||
            candidate.OwnerSeat != frame.OwnerSeat || candidate.SkillId != frame.SkillId ||
            candidate.SkillInstanceId != frame.SkillInstanceId || candidate.BindingId != frame.TriggerId ||
            candidate.GameplayHash != frame.GameplayHash || context.OwnerSeat != frame.OwnerSeat ||
            candidate.OccurrenceIndex != context.OccurrenceIndex ||
            context.Facts?.CurrentTurnUsedCardCategoryCount is not { } count ||
            window.Facts.CurrentTurnUsedCardCategoryCount != count || count is < 0 or > 3)
            throw new InvalidOperationException("Type-count draw requires its exact own Play-ending candidate and frozen public count.");
        return count;
    }
}
