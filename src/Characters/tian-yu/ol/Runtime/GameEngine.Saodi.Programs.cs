namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 扫狄 resolve: the accepted optional trigger re-reads the finalized use,
    // turns every legal in-between character into an additional target and
    // commits one evidence record per added seat. With no usable in-between
    // character the invocation ends without a prompt or event.
    private SkillProgramStepOutcome SaodiProgramExpandTargets(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            active.WindowContext?.CardUse is null)
            return SkillProgramStepOutcome.Continue;
        var use = EnhancementCardUse(active);
        if (use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != active.OwnerSeat ||
            action.EffectiveDesignatedTargetSeats is not [var anchor] ||
            anchor == active.OwnerSeat || !IsValidPlayerSeat(anchor) || !_players[anchor].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var additions = SaodiBetweenSeats(active.OwnerSeat, anchor)
            .Where(seat => CanSaodiAddTarget(use, seat)).ToArray();
        if (additions.Length == 0)
            return SkillProgramStepOutcome.Continue;
        var targets = Array.AsReadOnly(use.TargetSeats.Concat(additions).ToArray());
        UpdateLifecycleCardUse(use.Id, old => old with
        {
            TargetSeats = targets,
            Action = CloneRoleAction(action, action.ActorSeat, targets),
            TargetsAdjusted = old.TargetsAdjusted || old.CardKind == CardKind.BorrowedSword
        });
        SyncIssuedTieredRoundZeroTrickTargetWindows(use.Id, action);
        var bindingId = GetProgramBindingId(active);
        foreach (var seat in additions)
            AdvanceEventRulesAndQueueFact(new ProgramSaodiTargetsAddedEvent(active.Id, active.SkillId,
                bindingId, active.SkillInstanceId, active.OwnerSeat, use.Id, action.ActionId, seat));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，" +
            string.Join("、", additions.Select(seat => _players[seat].Name)) + "也成为此牌的目标。",
            active.OwnerSeat);
        return SkillProgramStepOutcome.Continue;
    }

    // "你与其之间的角色": alive seats strictly between the two endpoints along
    // the shorter seating arc, counted the same way GetAliveSeatDistance counts
    // (dead seats are skipped, the target endpoint always counts). Equal arcs
    // contribute their union.
    private int[] SaodiBetweenSeats(int ownerSeat, int targetSeat)
    {
        var clockwise = CollectAliveSeatsOnArc(ownerSeat, targetSeat, step: 1);
        var counterClockwise = CollectAliveSeatsOnArc(ownerSeat, targetSeat, step: -1);
        int ArcLength(List<int> arc) => arc.Count + 1;
        if (ArcLength(clockwise) < ArcLength(counterClockwise))
            return clockwise.Order().ToArray();
        if (ArcLength(counterClockwise) < ArcLength(clockwise))
            return counterClockwise.Order().ToArray();
        return clockwise.Concat(counterClockwise).Distinct().Order().ToArray();
    }

    private List<int> CollectAliveSeatsOnArc(int ownerSeat, int targetSeat, int step)
    {
        var seats = new List<int>();
        var seat = ownerSeat;
        do
        {
            seat = (seat + step + _playerCount) % _playerCount;
            if (seat == targetSeat) break;
            if (_players[seat].IsAlive) seats.Add(seat);
        }
        while (seat != ownerSeat);
        return seats;
    }

    // One added target must be a legal recipient of the pending card, mirroring
    // the shared original-target checks. Distance never disqualifies an
    // in-between character: the OL text carries no reach requirement for them.
    private bool CanSaodiAddTarget(CardUseFrame use, int targetSeat)
    {
        if (use.Action is not { Type: CardActionType.Use } action ||
            !IsValidPlayerSeat(targetSeat) ||
            action.EffectiveDesignatedTargetSeats.Contains(targetSeat))
            return false;
        var actor = _players[action.ActorSeat];
        var target = _players[targetSeat];
        if (!target.IsAlive ||
            IsDirectedCardTargetProhibited(actor.Seat, targetSeat, use.CardKind) ||
            IsCardTargetProhibited(target, use.CardKind, action.EffectiveSuit ?? Suit.None, ActualTargetPolicyColor(action)) ||
            HasBeneficiarySuitShield(actor.Seat, targetSeat, action.EffectiveSuit))
            return false;
        if (IsSlashCard(use.CardKind))
            return CanUseSlashTarget(actor, target, new Card(-1, use.CardKind, action.EffectiveSuit ?? Suit.None, action.EffectiveRank ?? 0),
                effectiveKind: use.CardKind, ignoreDistance: true,
                existingUseFrameId: use.Id, specificEffectiveRank: action.EffectiveRank);
        return use.CardKind switch
        {
            CardKind.IronChain => true,
            CardKind.BorrowedSword => GetEquipment(target).Any(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon) &&
                _players.Any(victim => IsLegalBorrowedSwordSlashTarget(target, victim)),
            CardKind.Duel => targetSeat != actor.Seat,
            CardKind.FireAttack => GetHand(target).Count > 0,
            CardKind.Dismantlement or CardKind.Snatch => targetSeat != actor.Seat &&
                GetHand(target).Count + GetEquipment(target).Count + GetJudgment(target).Count > 0,
            _ => false
        };
    }
}
