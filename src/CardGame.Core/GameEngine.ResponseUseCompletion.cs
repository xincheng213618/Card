namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Only a completed counterspell's own suspended HP-loss instruction may
    // retain the counterspell cursor while a child dying/rescue use is active.
    private bool TryGetCompletedNullificationDyingCosts(ProgramCardTriggerWindowFrame? window, out int[] costs)
    {
        costs = [];
        if (window?.CompletedResponseReturn?.Kind != ProgramCompletedResponseKind.Nullification ||
            ActiveDying is not { Continuation: DyingContinuationKind.ProgramSkill, KillerSeat: null } dying)
            return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == window.Id);
        if (index < 0 || index + 2 >= _resolutionStack.Count ||
            _resolutionStack[index + 1] is not ProgramSkillFrame program ||
            program.WindowContext?.ParentFrameId != window.Id || program.Id != dying.ParentFrameId ||
            _resolutionStack[index + 2] is not DyingFrame child || child.Id != dying.Id ||
            child.ParentFrameId != program.Id || child.VictimSeat != dying.VictimSeat || child.KillerSeat != dying.KillerSeat ||
            child.ResponderIndex != dying.ResponderIndex || !child.ResponderSeats.SequenceEqual(dying.ResponderSeats))
            return false;
        var effect = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!)
            .GetPausedInstruction(program.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.LoseHp || ResolveProgramEffectTarget(program, effect.Target) != dying.VictimSeat)
            return false;
        if (index + 3 < _resolutionStack.Count && _resolutionStack[index + 3] is CardUseFrame rescue)
        {
            if (rescue.DyingResponse is not { } response || response.ResolutionId != dying.Id ||
                response.ResponderSeat != dying.ResponderSeat || rescue.SourceSeat != response.ResponderSeat ||
                rescue.Action is not { Type: CardActionType.Use } action || action.ActorSeat != rescue.SourceSeat ||
                action.ProviderSeat != rescue.SourceSeat || !rescue.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
                rescue.CardKind is not (CardKind.Peach or CardKind.Alcohol) ||
                action.EffectiveKind != rescue.CardKind ||
                !action.EffectiveDesignatedTargetSeats.SequenceEqual(rescue.TargetSeats) ||
                !action.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(rescue.PhysicalCardIds ?? [rescue.CardId]) ||
                (rescue.CardKind == CardKind.Peach ? !response.UsedPeach : !response.UsedAlcohol || rescue.SourceSeat != dying.VictimSeat)) return false;
            costs = (rescue.PhysicalCardIds ?? [rescue.CardId]).Where(id => _cardZones.GetLocation(id).Zone == CardZoneKind.Processing).ToArray();
        }
        return true;
    }

    private static bool IsNullificationResponseProgramWindow(ProgramCardTriggerWindowFrame frame) =>
        frame.Continuation == ProgramCardContinuation.NullificationResponse ||
        frame.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification;

    // The existing response action remains a Response for accepted-response observers.
    // Only explicit opt-in completion observers see responses that the game calls uses.
    private static bool IsCompletedResponseUse(CardActionContext action, ProgramCardContinuation continuation) =>
        action.Type == CardActionType.Response && action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null &&
        action.ResponderSeat == action.ActorSeat &&
        (continuation == ProgramCardContinuation.NullificationResponse && action.EffectiveKind == CardKind.Nullification ||
         continuation == ProgramCardContinuation.Dodge && action.EffectiveKind == CardKind.Dodge);

    private bool IsCompletedResponseParentConsistent(ProgramCardTriggerWindowFrame window)
    {
        if (window.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification)
        {
            if (ActiveNullificationWindow is not { } pending || window.ParentFrameId != pending.Id ||
                window.Action.OpponentSeat != pending.SourceSeat) return false;
            var parent = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == pending.ParentFrameId);
            return parent is not null && window.Action.ParentActionId == parent.Action?.ActionId;
        }
        if (window.CompletedResponseReturn?.Kind != ProgramCompletedResponseKind.Dodge ||
            window.AttackOwnerFrameId is not { } ownerId || window.ParentFrameId != ownerId) return false;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == ownerId);
        return use?.CardAttack is { } attack && window.Action.ActorSeat == attack.TargetSeat &&
            window.Action.OpponentSeat == attack.SourceSeat && window.Action.ParentActionId == use.Action?.ActionId;
    }
    private static bool IsCompletedResponseUse(CardActionContext action, ProgramCompletedResponseKind kind) =>
        action.Type == CardActionType.Response && action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null &&
        action.ResponderSeat == action.ActorSeat &&
        (kind == ProgramCompletedResponseKind.Nullification && action.EffectiveKind == CardKind.Nullification ||
         kind == ProgramCompletedResponseKind.Dodge && action.EffectiveKind == CardKind.Dodge);

    private bool HasResponseUseCompletionObserver(CardActionContext action, ProgramCardContinuation continuation) =>
        IsCompletedResponseUse(action, continuation) && _players.Any(owner => owner.IsAlive &&
            (GetSkillBindingShard(owner)?.GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCompleted) ?? [])
                .Any(binding => binding.Trigger.IncludeResponseUses));

    private bool TryBeginCompletedResponseUsePrograms(CardAttackHandle? attack, CardActionContext action,
        ProgramCardContinuation continuation)
    {
        if (_winner != Winner.None || !HasResponseUseCompletionObserver(action, continuation)) return false;
        // Arrow Barrage Dodge and supplied faction cards are response-only. A Dodge use
        // must be the responder's own defense against the actual Slash parent.
        if (continuation == ProgramCardContinuation.Dodge &&
            (attack is null || attack.EffectiveCardKind is not { } incomingKind || !IsSlashCard(incomingKind) || attack.TargetSeat != action.ActorSeat)) return false;
        var kind = continuation == ProgramCardContinuation.Dodge
            ? ProgramCompletedResponseKind.Dodge
            : ProgramCompletedResponseKind.Nullification;
        return TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCompleted, [],
            continuation: null, completedResponseReturn: new(kind));
    }

    private Suit? FreezeCompletedResponseUseSuit(CharacterState owner, IReadOnlyList<Card> cards, CardKind kind)
    {
        if (kind is not (CardKind.Dodge or CardKind.Nullification) ||
            !(_contentRegistry.ProgramDependencies.CapturesCompletedResponseSuit ||
              TracksCurrentTurnUseKinds && kind == CardKind.Nullification)) return null;
        var suits = cards.Select(card => EffectiveSuit(owner, card)).Distinct().ToArray();
        return suits.Length == 1 ? suits[0] : null;
    }

    private void ContinueNullificationAfterResponseUse(CardActionContext action)
    {
        if (!TryBeginCompletedResponseUsePrograms(null, action, ProgramCardContinuation.NullificationResponse))
            ContinueNullificationWindow(ActiveNullificationWindow ??
                throw new InvalidOperationException("The completed counterspell lost its original chain."));
    }

    private void ContinueCompletedResponseUse(CardAttackHandle? attack, ProgramCardTriggerWindowFrame frame)
    {
        var completed = frame.CompletedResponseReturn ??
            throw new InvalidOperationException("The completed response use lost its typed continuation.");
        if (!IsCompletedResponseUse(frame.Action, completed.Kind))
            throw new InvalidOperationException("A completed response use changed its actor or card kind.");
        if (completed.Kind == ProgramCompletedResponseKind.Nullification)
            ContinueNullificationWindow(ActiveNullificationWindow ??
                throw new InvalidOperationException("The completed counterspell lost its original chain."));
        else ContinueFinishedCardResponse(attack ??
            throw new InvalidOperationException("The completed Dodge lost its original Slash."), frame.Action,
            ProgramCardContinuation.Dodge);
    }
}
