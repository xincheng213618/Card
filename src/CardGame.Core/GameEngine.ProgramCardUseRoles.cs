namespace CardGame.Core;

public sealed record ProgramCardUseActorReplacedEvent(long FrameId, string SkillId, int OwnerSeat,
    long CardUseFrameId, int PreviousActorSeat, int ActorSeat, int ProviderSeat) : IGameEvent;
public sealed record ProgramCardUseTargetAddedEvent(long FrameId, string SkillId, int OwnerSeat,
    long CardUseFrameId, int TargetSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private CardUseFrame GetProgramRoleCardUse(ProgramSkillFrame frame, bool ownerMayBeTarget = false)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } context } ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == context.ParentCardUseFrameId) is not { Action: { } action } use ||
            action.ActionId != context.CardActionId || action.Type != CardActionType.Use ||
            action.EffectiveDesignatedTargetSeats.Count != 1 ||
            !IsProgramCardUseRoleOwner(frame.OwnerSeat, action, ownerMayBeTarget))
            throw new InvalidOperationException("A card-use role change requires the owner's unique-target designation window.");
        return use;
    }

    // Role changes are normally driven by the card user. Adding a target can also be driven by a
    // character who is already that card's sole designated target, which is how a Slash passes on
    // to another character without moving away from the original target.
    private static bool IsProgramCardUseRoleOwner(int ownerSeat, CardActionContext action, bool ownerMayBeTarget) =>
        action.ActorSeat == ownerSeat ||
        ownerMayBeTarget && action.EffectiveDesignatedTargetSeats.Contains(ownerSeat) &&
        action.EffectiveKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private IReadOnlyList<int> GetProgramCardUseRoleTargets(ProgramSkillFrame frame, bool ownerMayBeTarget = false)
    {
        var use = GetProgramRoleCardUse(frame, ownerMayBeTarget);
        var action = use.Action!;
        return _players.Where(target => !action.EffectiveDesignatedTargetSeats.Contains(target.Seat) &&
            CanBeProgramCardUseRoleTarget(use, target)).Select(target => target.Seat).ToArray();
    }

    private IReadOnlyList<int> GetProgramCardUseRoleTargets(int ownerSeat, ProgramSkillWindowContext? context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } cardUse } ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == cardUse.ParentCardUseFrameId) is not { Action: { } action } use ||
            action.ActionId != cardUse.CardActionId ||
            !IsProgramCardUseRoleOwner(ownerSeat, action, ownerMayBeTarget: true) ||
            action.EffectiveDesignatedTargetSeats.Count != 1) return [];
        return _players.Where(target => !action.EffectiveDesignatedTargetSeats.Contains(target.Seat) &&
            CanBeProgramCardUseRoleTarget(use, target)).Select(target => target.Seat).ToArray();
    }

    private bool CanBeProgramCardUseRoleTarget(CardUseFrame use, CharacterState target)
    {
        var action = use.Action!;
        var actor = _players[action.ActorSeat];
        var kind = action.EffectiveKind;
        if (!target.IsAlive || target.Seat == actor.Seat || IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) ||
            IsCardTargetProhibited(target, kind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) || HasBeneficiarySuitShield(actor.Seat, target.Seat, action.EffectiveSuit)) return false;
        var physical = action.PhysicalCards.Count > 0
            ? _cardZones.CardsAt(_cardZones.GetLocation(action.PhysicalCards[0].CardId)).Single(card => card.Id == action.PhysicalCards[0].CardId)
            : new Card(-1, kind, action.EffectiveSuit ?? Suit.None, action.EffectiveRank ?? 0);
        return kind switch
        {
            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash =>
                CanUseSlashTarget(actor, target, physical, action.ConversionChain.FirstOrDefault(), kind, HasIssuedFirstPlayUseDistance(use.Id), use.Id, physicalCardIds:action.PhysicalCards.Select(c=>c.CardId).ToArray()),
            CardKind.Duel => true,
            CardKind.Snatch => HasTargetCard(target) && (HasIssuedProvenanceUseDistance(use.Id, actor.Seat) || HasCardDistanceExemption(actor, target, kind, use.Id) ||
                HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance, kind) || GetCombatDistance(actor.Seat, target.Seat) == 1),
            CardKind.Dismantlement => HasTargetCard(target),
            CardKind.FireAttack => GetHand(target).Count > 0,
            CardKind.IronChain => true,
            CardKind.BorrowedSword => GetWeapon(target) is not null && _players.Any(victim => IsLegalBorrowedSwordSlashTarget(target, victim)),
            _ => false
        };
    }

    private void ReplaceCurrentProgramCardUseActor(ProgramSkillFrame frame, int actorSeat)
    {
        var use = GetProgramRoleCardUse(frame);
        // A legal invitee may give away its final card (or weapon). The paid
        // transfer cannot revoke the already selected role-change option.
        if (!_players[actorSeat].IsAlive || actorSeat == frame.OwnerSeat ||
            use.Action!.EffectiveDesignatedTargetSeats.Contains(actorSeat) ||
            !frame.SelectedTargetSeats.SequenceEqual([actorSeat]))
            throw new InvalidOperationException("The replacement actor lost its selected role-change participant.");
        var previous = use.SourceSeat;
        var action = CaptureReplacedFactionActor(CloneRoleAction(use.Action!, actorSeat, use.TargetSeats), actorSeat);
        UpdateProgramRoleCardUse(use with { SourceSeat = actorSeat, Action = action }, action);
        ClearProvenanceUseOnActorChange(use.Id, actorSeat);
        AdvanceEventRulesAndQueueFact(new ProgramCardUseActorReplacedEvent(frame.Id, frame.SkillId, frame.OwnerSeat,
            use.Id, previous, actorSeat, action.ProviderSeat));
    }

    private SkillProgramStepOutcome AddCurrentProgramCardUseTarget(ProgramSkillFrame frame, int targetSeat)
    {
        var use = GetProgramRoleCardUse(frame, ownerMayBeTarget: true);
        if (!GetProgramCardUseRoleTargets(frame, ownerMayBeTarget: true).Contains(targetSeat))
            throw new InvalidOperationException("The additional target is not legal for the current card.");
        if (use.CardKind == CardKind.BorrowedSword)
            return SelectProgramAddedBorrowedSwordVictim(frame, use, targetSeat);
        CompleteProgramCardUseTargetAddition(frame, use, use.TargetSeats.Append(targetSeat).ToArray(), targetSeat);
        return SkillProgramStepOutcome.Continue;
    }

    private void CompleteProgramCardUseTargetAddition(ProgramSkillFrame frame, CardUseFrame use, IReadOnlyList<int> targets, int targetSeat)
    {
        var action = CloneRoleAction(use.Action!, use.SourceSeat, targets);
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action }, action);
        AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, use.Id, targetSeat));
    }

    private CardActionContext CloneRoleAction(CardActionContext action, int actorSeat, IReadOnlyList<int> targets) =>
        new(action.ActionId, action.ParentActionId, action.Type, actorSeat, action.ProviderSeat,
            action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, targets,
            action.PhysicalCards, action.ConversionChain,
            action.EffectiveKind == CardKind.BorrowedSword ? targets.Where((_, index) => index % 2 == 0).ToArray() : targets,
            action.EffectiveSuit, action.EffectiveRank, HasBlackTrickTargetPolicy || action.FactionOrigin is not null || HasComparedBlackSlashPolicies && IsSlashCard(action.EffectiveKind) ? action.EffectiveIsRed : null, action.FactionOrigin);

    private void UpdateProgramRoleCardUse(CardUseFrame use, CardActionContext action)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (index < 0) throw new InvalidOperationException("A card-use role change lost its parent.");
        ReplaceRuntimeFrame(_resolutionStack[index].Id, use);
        for (var cursor = index + 1; cursor < _resolutionStack.Count; cursor++)
            if (_resolutionStack[cursor] is ProgramCardTriggerWindowFrame window && window.ParentFrameId == use.Id)
                ReplaceRuntimeFrame(_resolutionStack[cursor].Id, window with { Action = action });
    }
}
