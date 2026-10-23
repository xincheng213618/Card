namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginHappyGujingPayments(ProgramSkillFrame frame)
    {
        var receipt = frame.HappyWeiYan!;
        ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt with { Stage = HappyWeiYanStage.GujingDiscardChildren, Cursor = 0, SlotAbolished = false } });
        IssueNextHappyGujingPayment(frame);
    }
    private void IssueNextHappyGujingPayment(ProgramSkillFrame initial)
    {
        var frame = initial; var receipt = frame.HappyWeiYan!;
        while (receipt.Cursor < receipt.SelectedBones.Count)
        {
            var bone = receipt.SelectedBones[receipt.Cursor];
            var canDiscard = _players[bone.Holder].IsAlive && HasExactHappyBonePlacement(GetAdvancedCard(bone.CardId), bone.Holder) &&
                BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, bone.Holder, [CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).Any(c => c.Cards.Contains(bone.CardId));
            if (!canDiscard)
            {
                ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt = receipt with { Cursor = receipt.Cursor + 1 } }); continue;
            }
            var before = _movementSequence;
            ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt with { SelectedBone = bone, CardId = bone.CardId, Before = before, After = before, Paid = [], SlotAbolished = false }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(frame, frame.OwnerSeat, [bone.CardId], new(HappyGujingDiscard), (_, _) => SaveHappyWeiYanMove(frame.Id, HappyWeiYanStage.GujingDiscardChildren, before));
            ContinueHappyWeiYan(frame.Id); return;
        }
        var issue = new HappyWeiYanGujingIssue(frame.Id, receipt.Source, receipt.GameplayHash);
        _players[frame.OwnerSeat].HappyWeiYanGujing = issue;
        AdvanceEventRulesAndQueueFact(new HappyWeiYanGujingIssuedEvent(frame.Id, issue.Source, issue.GameplayHash));
        FinishHappyWeiYan(frame);
    }
    private bool ContinueHappyGujingPayment(ProgramSkillFrame frame)
    {
        if (DrainHappyWeiYan(frame)) return true;
        frame = GetActiveProgramFrame(frame.Id); var receipt = frame.HappyWeiYan!;
        if (receipt.SelectedBone is not { } bone || receipt.Cursor >= receipt.SelectedBones.Count || bone != receipt.SelectedBones[receipt.Cursor])
            throw new InvalidOperationException("Gujing lost the next selected role's real bone payment.");
        if (!receipt.SlotAbolished)
        {
            if (!receipt.Paid.Any(m => m.CardId == bone.CardId && m.From == CardLocation.Equipment(bone.Holder) && m.Reason.Value == HappyGujingDiscard))
                throw new InvalidOperationException("Gujing cannot abolish a slot without first discarding its selected physical bone.");
            ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt with { SlotAbolished = true } });
            SetEquipmentSlotCapacity(_players[bone.Holder], bone.Slot, 0);
            if (_movementSequence != receipt.After) SaveHappyWeiYanMove(frame.Id, HappyWeiYanStage.GujingDiscardChildren, receipt.Before);
            AdvanceEventRulesAndQueueFact(new HappyWeiYanGujingAbolishedEvent(frame.Id, receipt.Cursor, bone.Holder, bone.Slot, receipt.Before, _movementSequence));
            return ContinueHappyWeiYan(frame.Id);
        }
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null, HappyWeiYan = receipt with { Cursor = receipt.Cursor + 1, SelectedBone = null, SlotAbolished = false, Before = _movementSequence, After = _movementSequence, Paid = [] } });
        IssueNextHappyGujingPayment(frame); return true;
    }
    // This is a destination replacement before ownership is changed, not a second gain or a later theft.
    private CardLocation NormalizeHappyWeiYanGainDestination(Card card, CardLocation from, CardLocation to, CardMoveReason reason)
    {
        if (to is not { Zone: CardZoneKind.Hand, OwnerSeat: { } recipient } || !IsValidPlayerSeat(recipient) || !EquipmentCatalog.IsEquipment(card.Kind)) return to;
        var slot = EquipmentCatalog.Get(card.Kind).Slot;
        if (!_players[recipient].EquipmentAreaAbolished && _players[recipient].EquipmentSlotCapacity(slot) > 0) return to;
        foreach (var owner in _players.Where(p => p.IsAlive && p.HappyWeiYanGujing is not null).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)))
        {
            var issue = owner.HappyWeiYanGujing!;
            if (issue.Source.OwnerSeat != owner.Seat || issue.Source.SkillId != HappyWeiYanComposition.Gujing || issue.GameplayHash != _contentRegistry.GetSkill(issue.Source.SkillId).Program!.GameplayHash ||
                ProgramEventHistory<HappyWeiYanGujingIssuedEvent>().Count(e => e == new HappyWeiYanGujingIssuedEvent(issue.FrameId, issue.Source, issue.GameplayHash)) != 1)
                throw new InvalidOperationException("Gujing's permanent gain replacement lost its accepted limited issue.");
            return CardLocation.Hand(owner.Seat);
        }
        return to;
    }
}
