namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsPindianHandReturn(ProgramSkillFrame f) => f.TriggerId is null &&
        ProgramInstructionResolver.Default.FindActivation(_contentRegistry.GetSkill(f.SkillId).Program!, f.ActivationId) is { PindianHandReturn: true } a &&
        PindianHandReturnContract.Matches(a.Effects);

    private void RecordPindianHandContestReturn(ProgramSkillFrame f, PindianFrame native)
    {
        if (!IsPindianHandReturn(f)) return;
        var result = native.Result!;
        if (native.ParentFrameId != f.Id || f.InstructionIndex != 1 || f.PindianHandReturn is not null ||
            native.SourceSeat != f.OwnerSeat || f.SelectedTargetSeats is not [var target] || native.OpponentSeat != target ||
            native.ProgramResultBind != SimpleProgramPindianResultBind ||
            ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == native.Id && e.SkillId == f.SkillId && e.Result == result) != 1)
            throw new InvalidOperationException("Hand return lost its exact native Pindian result and parent.");
        ReplaceRuntimeTop(f with { PindianHandReturn = new(f.Id,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            native.Id, target, result.SourceRank, result.OpponentRank) });
        AdvanceEventRulesAndQueueFact(new ProgramPindianHandContestReturnedEvent(f.Id, native.Id,
            f.OwnerSeat, target, result.SourceRank, result.OpponentRank, result.SourceWon));
    }

    private long? BeginPindianWholeHandAcquisition(ProgramSkillFrame f, string bind, SkillProgramCardDestination destination)
    {
        if (!IsPindianHandReturn(f) || f.InstructionIndex != 3 || destination != SkillProgramCardDestination.OwnerHand) return null;
        var receipt = RequirePindianHandReturn(f);
        var cards = GetProgramCardSet(f, bind);
        if (receipt.AcquisitionPaid || !_players[f.OwnerSeat].IsAlive || !_players[receipt.TargetSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !cards.CardIds.Order().SequenceEqual(GetHand(_players[receipt.TargetSeat]).Select(c => c.Id).Order()) ||
            cards.SourceLocations.Any(location => location != CardLocation.Hand(receipt.TargetSeat)))
            throw new InvalidOperationException("Pindian whole-hand acquisition lost its exact unpaid opponent hand.");
        return OutsidePhaseMovementSequence;
    }

    private void CommitPindianWholeHandAcquisition(long frameId, string bind, long before)
    {
        var f = GetActiveProgramFrame(frameId); var r = RequirePindianHandReturn(f);
        var ids = GetProgramCardSet(f, bind).CardIds;
        var after = OutsidePhaseMovementSequence;
        var moves = _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after).ToArray();
        if (moves.Length != ids.Count || !moves.Select(m => m.CardId).Order().SequenceEqual(ids.Order()) ||
            moves.Any(m => m.From != CardLocation.Hand(r.TargetSeat) || m.To != CardLocation.Hand(f.OwnerSeat) ||
                m.Reason != new CardMoveReason($"skill-program.{f.SkillId}.{SkillProgramEffectOp.MoveBoundCards}")))
            throw new InvalidOperationException("Pindian whole-hand acquisition must move every frozen material exactly once.");
        var paid = r with { AcquisitionPaid = true, SequenceBefore = before, SequenceAfter = after,
            Materials = moves.Select(m => new PindianHandReturnMaterial(m.CardId, m.Sequence)).ToArray() };
        ReplaceRuntimeTop(f with { PindianHandReturn = paid });
        AdvanceEventRulesAndQueueFact(new ProgramPindianHandAcquiredEvent(paid));
    }

    private ProgramPindianHandReturnReceipt RequirePindianHandReturn(ProgramSkillFrame f)
    {
        if (!IsPindianHandReturn(f) || f.PindianHandReturn is not { } r || r.FrameId != f.Id ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || f.SelectedTargetSeats is not [var target] || target != r.TargetSeat ||
            f.PindianResultBindings.SingleOrDefault(b => b.Name == SimpleProgramPindianResultBind) is not { SourceWon: true } result ||
            result.SourceSeat != f.OwnerSeat || result.OpponentSeat != target || result.SourceRank != r.SourceRank ||
            result.OpponentRank != r.OpponentRank || ProgramEventHistory<ProgramPindianHandContestReturnedEvent>().Count(e =>
                e.FrameId == f.Id && e.PindianFrameId == r.PindianFrameId && e.OwnerSeat == f.OwnerSeat &&
                e.TargetSeat == target && e.SourceRank == r.SourceRank && e.OpponentRank == r.OpponentRank && e.Won) != 1)
            throw new InvalidOperationException("Pindian hand return lost its exact native winning contest.");
        if (!r.AcquisitionPaid) return r;
        var issued = ProgramEventHistory<ProgramPindianHandAcquiredEvent>().Where(e => e.Receipt.FrameId == f.Id).ToArray();
        var moves = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        if (issued is not [var fact] || fact.Receipt.Source != r.Source || fact.Receipt.GameplayHash != r.GameplayHash ||
            fact.Receipt.PindianFrameId != r.PindianFrameId || fact.Receipt.TargetSeat != r.TargetSeat ||
            fact.Receipt.SourceRank != r.SourceRank || fact.Receipt.OpponentRank != r.OpponentRank || !fact.Receipt.AcquisitionPaid ||
            fact.Receipt.SequenceBefore != r.SequenceBefore || fact.Receipt.SequenceAfter != r.SequenceAfter ||
            !fact.Receipt.Materials.SequenceEqual(r.Materials) ||
            r.Materials is not System.Collections.IList { IsReadOnly: true } || r.Materials.Count != moves.Length ||
            !r.Materials.SequenceEqual(moves.Select(m => new PindianHandReturnMaterial(m.CardId, m.Sequence))) ||
            moves.Any(m => m.From != CardLocation.Hand(target) || m.To != CardLocation.Hand(f.OwnerSeat) ||
                m.Reason != new CardMoveReason($"skill-program.{f.SkillId}.{SkillProgramEffectOp.MoveBoundCards}")))
            throw new InvalidOperationException("Pindian hand return lost its issued atomic acquisition payment.");
        return r;
    }

    private void FreezePindianHalfReturn(ProgramSkillFrame f, int count)
    {
        if (!IsPindianHandReturn(f) || f.InstructionIndex != 4) return;
        var r = RequirePindianHandReturn(f);
        if (!r.AcquisitionPaid || r.ReturnCount is not null || f.PendingMovementContinuation is not null ||
            count != GetHand(_players[f.OwnerSeat]).Count / 2)
            throw new InvalidOperationException("Half-hand return must freeze once after every acquisition child returns.");
        ReplaceRuntimeTop(f with { PindianHandReturn = r with { ReturnCount = count } });
        AdvanceEventRulesAndQueueFact(new ProgramPindianHalfReturnCountEvent(f.Id, f.OwnerSeat, r.TargetSeat,
            GetHand(_players[f.OwnerSeat]).Count, count));
    }

    private bool HasPaidPindianHandReturn(ProgramSkillFrame f)
    {
        if (f.PindianHandReturn is not { AcquisitionPaid: true }) return false;
        _ = RequirePindianHandReturn(f); return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 7003)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 7003)]
    private bool ContinuePaidPindianHandReturn(ProgramSkillFrame f) => HasPaidPindianHandReturn(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 7003)]
    private void AssertPindianHandReturn(ProgramSkillFrame f)
    {
        if (f.PindianHandReturn is not { } r || r.SourceRank <= r.OpponentRank) return;
        _ = RequirePindianHandReturn(f);
        if (r.ReturnCount is { } count && ProgramEventHistory<ProgramPindianHalfReturnCountEvent>().Count(e =>
            e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.TargetSeat == r.TargetSeat &&
            e.ReturnCount == count && e.HandCount / 2 == count) != 1)
            throw new InvalidOperationException("The half-hand obligation changed after its actual post-gain selection.");
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 7003)]
    private bool ResumeInvalidPindianHandReturn(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || !HasPaidPindianHandReturn(f)) return false;
        var r = f.PindianHandReturn!;
        if (_players[f.OwnerSeat].IsAlive && _players[r.TargetSeat].IsAlive) return false;
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(f.Id)) return true;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null, OwnedCardSelection = null });
        CancelProgramBindingAndCleanup(GetActiveProgramFrame(f.Id), "已付获牌子流程结清，死亡参与者不再执行赠牌。");
        return true;
    }
}
