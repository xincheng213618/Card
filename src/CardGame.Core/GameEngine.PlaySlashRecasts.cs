namespace CardGame.Core;
public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IEndingPairSlashLossHost
    {
        public SkillProgramStepOutcome DrawEndingPair(ProgramSkillFrame f, string state) => engine.BeginEndingPairDraw(f, state);
        public SkillProgramStepOutcome RecastSelectedPhysicalSlash(ProgramSkillFrame f) => engine.BeginActualPlaySlashRecast(f);
    }
    private SkillProgramStepOutcome BeginActualPlaySlashRecast(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.PlaySlashRecast is not null || f.TriggerId is not null || f.InstructionIndex != 1 || f.SelectedCardIds is not [var id] ||
            f.SelectedTargetSeats.Count != 0 || _phase != TurnPhase.Play || f.OwnerSeat != _currentSeat || _cardUseDebitPhaseInstanceId < 1)
            throw new InvalidOperationException("Slash recast requires its original one-card, zero-target actual Play activation.");
        var owner = _players[f.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId) || _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat))
        { CancelProgramBindingAndCleanup(f, "原手牌、来源或参与者已失效，没有支付重铸。"); return SkillProgramStepOutcome.AwaitChild; }
        var card = GetHand(owner).Single(c => c.Id == id);
        if (!IsSlashCard(card.Kind)) throw new InvalidOperationException("The active recast requires a printed physical Slash, Fire Slash or Thunder Slash.");
        var before = EndingPairSequence;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null), PlaySlashRecast =
            new(f.InstructionIndex, EndingPairSource(f), f.GameplayHash, _turnNumber, _turnProgression.OwnerSeat, f.OwnerSeat, _cardUseDebitPhaseInstanceId,
                id, card.Kind, before, before) });
        MoveCard(card, CardLocation.Hand(f.OwnerSeat), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        var after = EndingPairSequence; f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { PlaySlashRecast = f.PlaySlashRecast! with { SequenceAfter = after } });
        var r = GetActiveProgramFrame(f.Id).PlaySlashRecast!;
        AdvanceEventRulesAndQueueFact(new PlaySlashRecastPaidEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurnNumber, r.ActualTurnOwnerSeat,
            r.PhaseActorSeat, r.PhaseInstanceId, id, card.Kind, before, after));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeEndingPairOrSlashRecast(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.EndingPairDraw is not null) { ContinueEndingPairDraw(id); return true; }
        if (f.PlaySlashRecast is not { } r) return false;
        if (!ValidPlaySlashRecast(f)) throw new InvalidOperationException("A paid Slash recast lost its original actual phase/material or exact cost/reward invoice.");
        if (r.AwaitingMovement)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = null, PlaySlashRecast = r with { AwaitingMovement = false, CostDrained = true } });
            f = GetActiveProgramFrame(id); r = f.PlaySlashRecast!;
        }
        if (!r.DrawIssued)
        {
            var owner = _players[f.OwnerSeat];
            if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            {
                // The real paid recast survives cancellation; an unattempted
                // reward must not be recorded as an issued zero-card draw.
                AdvanceEventRulesAndQueueFact(new CardRecastEvent(owner.Seat, r.CardId, r.CardKind, 0));
                ReplaceRuntimeTop(f with { PlaySlashRecast = null, PendingMovementContinuation = null });
                CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "重铸成本已结算，来源、参与者或游戏失效，尚未发行的摸牌取消。");
                return true;
            }
            var before = EndingPairSequence;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null), PlaySlashRecast = r with { DrawIssued = true, AwaitingMovement = true, DrawSequenceBefore = before } });
            var count = DrawCards(owner, 1, true, CardMoveReasons.RecastDraw).Count;
            f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { PlaySlashRecast = f.PlaySlashRecast! with { DrawSequenceAfter = EndingPairSequence, ActualDrawCount = count } });
            AdvanceEventRulesAndQueueFact(new PlaySlashRecastDrawIssuedEvent(id, owner.Seat, before, EndingPairSequence, count));
            AdvanceEventRulesAndQueueFact(new CardRecastEvent(owner.Seat, r.CardId, r.CardKind, count));
            AdvanceRuntimeProgram(id); return true;
        }
        ReplaceRuntimeTop(f with { PlaySlashRecast = null });
        if (_winner != Winner.None) { CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "游戏已结束，重铸后继取消。"); return true; }
        // The fresh executor applies source/liveness cancellation after paid children.
        return false;
    }
    private bool ValidPlaySlashRecast(ProgramSkillFrame f)
    {
        if (f.PlaySlashRecast is not { } r || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 || f.TriggerId is not null ||
            r.Source != EndingPairSource(f) || r.GameplayHash != f.GameplayHash || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _turnProgression.OwnerSeat || r.PhaseActorSeat != f.OwnerSeat ||
            r.PhaseInstanceId != _cardUseDebitPhaseInstanceId || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat ||
            !f.SelectedCardIds.SequenceEqual([r.CardId]) || f.SelectedTargetSeats.Count != 0 || !IsSlashCard(r.CardKind) || r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not [{ Op: SkillProgramEffectOp.RecastSelectedPhysicalSlash }] ||
            _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.CardId == r.CardId && m.CardKind == r.CardKind &&
                m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.RecastDiscard) != 1 ||
            CompleteProgramEventHistory().OfType<PlaySlashRecastPaidEvent>().Count(e => e.ProgramFrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.ActualTurnNumber == r.ActualTurnNumber && e.ActualTurnOwnerSeat == r.ActualTurnOwnerSeat && e.PhaseActorSeat == r.PhaseActorSeat && e.PhaseInstanceId == r.PhaseInstanceId &&
                e.CardId == r.CardId && e.CardKind == r.CardKind && e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter) != 1) return false;
        if (r.AwaitingMovement && (f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat)) return false;
        var draws = CompleteProgramEventHistory().OfType<PlaySlashRecastDrawIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        return !r.DrawIssued ? r.DrawSequenceBefore == 0 && r.DrawSequenceAfter == 0 && r.ActualDrawCount == 0 && draws.Length == 0 :
            r.CostDrained && r.DrawSequenceBefore >= r.SequenceAfter && r.DrawSequenceAfter >= r.DrawSequenceBefore && r.ActualDrawCount is >= 0 and <= 1 &&
            draws is [var draw] && draw.OwnerSeat == f.OwnerSeat && draw.SequenceBefore == r.DrawSequenceBefore && draw.SequenceAfter == r.DrawSequenceAfter && draw.ActualCount == r.ActualDrawCount &&
            _cardMovements.Count(m => m.Sequence > r.DrawSequenceBefore && m.Sequence <= r.DrawSequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason == CardMoveReasons.RecastDraw) == r.ActualDrawCount;
    }
}
