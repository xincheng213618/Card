namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsRecipientContestMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (f.UniqueHpPeer is { Stage: UniqueHpPeerStage.CostChildren or UniqueHpPeerStage.DrawChildren } peer &&
            effect?.Op == SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer && pending.SubjectSeat == peer.ChooserSeat && ValidUniqueHpPeer(f) ||
         f.RecipientContest is { Stage: RecipientContestStage.GiftChildren } && effect?.Op == SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian &&
            pending.SubjectSeat == f.OwnerSeat && ValidRecipientContest(f));
    private bool ReturnRecipientContestMovement(ProgramSkillFrame f)
    {
        if (f.UniqueHpPeer is null && f.RecipientContest is null || f.PendingMovementContinuation is not { } pending) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (!IsRecipientContestMovement(f, effect, pending)) throw new InvalidOperationException("A discard/draw or atomic gift lost its exact pending invoice.");
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool RecipientContestFirstPaidChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        long before, after; int actor; string reason; bool draw;
        UniqueHpPeerInvoice? invoice = null;
        if (f.UniqueHpPeer is { Stage: UniqueHpPeerStage.CostChildren or UniqueHpPeerStage.DrawChildren } peer && ValidUniqueHpPeer(f))
        {
            invoice = peer.ChooserSeat == f.OwnerSeat ? peer.OwnerInvoice : peer.PeerInvoice; if (invoice is null) return false;
            actor = invoice.ActorSeat; draw = peer.Stage == UniqueHpPeerStage.DrawChildren;
            before = draw ? invoice.DrawBefore : invoice.CostBefore; after = draw ? invoice.DrawAfter : invoice.CostAfter;
            reason = draw ? UniqueHpPeerDrawReason(f, actor) : UniqueHpPeerCostReason(f);
        }
        else if (f.RecipientContest is { Stage: RecipientContestStage.GiftChildren } gift && ValidRecipientContest(f))
        { before = gift.SequenceBefore; after = gift.SequenceAfter; actor = f.OwnerSeat; reason = RecipientContestGiftReason(f); draw = false; }
        else return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == f.Id && moved.ResumeProgramFrameId == f.Id &&
                (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == f.Id) &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > before && m.Sequence <= after &&
                    (draw ? m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(actor) && m.Reason.Value == reason :
                     f.RecipientContest is { } g ? g.GiftCardIds.Contains(m.CardId) && m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(g.RecipientSeat) && m.Reason.Value == reason :
                     invoice!.CardIds.Select((id, n) => (id, from: invoice.SourceLocations[n])).Any(p => p.id == m.CardId && p.from == m.From &&
                         m.Reason.Value == reason && (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && GetAdvancedCard(m.CardId).IsGeneralWeapon))) ||
                     !draw && invoice is not null && m.Sequence > after && m.From == CardLocation.WoodenOxGrain(actor) &&
                     m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                     invoice.CardIds.Select((id, n) => (id, from: invoice.SourceLocations[n])).Any(p => p.from == CardLocation.Equipment(actor) &&
                         _cardMovements.Any(cost => cost.Sequence > before && cost.Sequence <= after && cost.CardId == p.id && cost.CardKind == CardKind.WoodenOx &&
                             cost.From == p.from && cost.To == CardLocation.DiscardPile && cost.Reason.Value == reason))));
        if (draw || invoice is null || !invoice.CardIds.Where((id, n) => invoice.SourceLocations[n] == CardLocation.Equipment(actor)).Any(id =>
            _cardMovements.Any(m => m.Sequence > before && m.Sequence <= after && m.CardId == id && m.CardKind == CardKind.SilverLion &&
                m.From == CardLocation.Equipment(actor) && m.To == CardLocation.DiscardPile && m.Reason.Value == reason))) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == actor && hp.Change.TargetSeat == actor && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame replaced && RecoveryReplacementFrameRidesOn(replaced, f) &&
            replaced.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && replaced.Attempt.SourceSeat == actor &&
            replaced.Attempt.TargetSeat == actor && replaced.Attempt.Amount == 1 && replaced.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            replaced.Attempt.Completion.MoveReason?.Value == reason;
    }
    private bool RecipientContestPaidPrefix(int rootIndex, int lastIndex)
    {
        if (rootIndex < 0 || lastIndex <= rootIndex || lastIndex >= _resolutionStack.Count ||
            _resolutionStack[rootIndex] is not ProgramSkillFrame f || !RecipientContestFirstPaidChild(f, _resolutionStack[rootIndex + 1])) return false;
        for (var i = rootIndex + 1; i <= lastIndex; i++)
        {
            // The root-to-first incoming edge is already proved above. Every
            // later edge is exact; whole rescue suffixes are used only after it.
            if (i > rootIndex + 1 && !(HalfHandPaidDamageObserverEdge(i) || PaidTargetObserverEdge(i))) return false;
            if (_resolutionStack[i] is DyingFrame d && i < lastIndex &&
                (IsRoundPricedPileAlcoholRide(i, d) || IsPaidHandRepaymentProgramAlcoholRide(i, d) || IsPaidHandRepaymentRescueRide(i, d) || PolicyCounterspellVirtualAlcoholRide(i, d) || PaidObserverDamageVirtualAlcoholRide(i, d)))
                return lastIndex == _resolutionStack.Count - 1;
        }
        return true;
    }
    private ProgramSkillFrame? RecipientContestPaidObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
            if (RecipientContestPaidPrefix(i, _resolutionStack.Count - 1)) return (ProgramSkillFrame)_resolutionStack[i];
        return null;
    }
    private bool HasUniqueHpPeerActualTargetObserver(long useId)
    {
        var index = _resolutionStack.FindIndex(x => x is ActualUseTargetWindowFrame w && w.ParentFrameId == useId);
        if (index < 1 || index + 1 >= _resolutionStack.Count || _resolutionStack[index + 1] is not ProgramSkillFrame f ||
            f.UniqueHpPeer is not { CardUseFrameId: var original } || original != useId || !ValidUniqueHpPeer(f)) return false;
        return index + 2 == _resolutionStack.Count || RecipientContestPaidPrefix(index + 1, _resolutionStack.Count - 1);
    }
    private bool IsRecipientContestProgramDying() => ActiveDying is { } dying && RecipientContestPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasRecipientContestDamageObserver(long windowId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == windowId && f is DamageTriggerWindowFrame);
        return index >= 2 && _resolutionStack[index] is DamageTriggerWindowFrame window && _resolutionStack[index - 1] is DamageFrame damage &&
            window.ParentFrameId == damage.Id && _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == damage.ParentFrameId) is { AttackAttempt: not null, AttackReturn: not null } attack &&
            CurrentDamageAttempt?.ResolutionId == attack.Id && RecipientContestPaidObserverRoot() is { } root &&
            _resolutionStack.FindIndex(f => f.Id == root.Id) < _resolutionStack.FindIndex(f => f.Id == attack.Id);
    }
    private bool AllowsRecipientContestNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != observer.Id || observer.InstructionIndex < 1 ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.Damage || amount != effect.Amount || source != effect.ActorReference || nature != effect.DamageNature ||
            target != (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target))) return false;
        return RecipientContestPaidObserverRoot() is { UniqueHpPeer: { } receipt } &&
            ActiveCardAttack?.ResolutionId == receipt.CardUseFrameId && CurrentDamageAttempt?.ResolutionId == receipt.CardUseFrameId;
    }
}
