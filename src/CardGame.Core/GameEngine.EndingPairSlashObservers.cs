namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsEndingPairMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (e?.Op == SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal && f.EndingPairDraw is { AwaitingMovement: true } && ValidEndingPairDraw(f) ||
         e?.Op == SkillProgramEffectOp.RecastSelectedPhysicalSlash && f.PlaySlashRecast is { AwaitingMovement: true } && ValidPlaySlashRecast(f));
    private bool ReturnEndingPairMovement(ProgramSkillFrame f)
    {
        if (f.PendingMovementContinuation is not { } pending || f.EndingPairDraw is null && f.PlaySlashRecast is null) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if (!IsEndingPairMovement(f, effect, pending)) throw new InvalidOperationException("The actual Ending/recast movement lost its exact issued paid instruction.");
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private void AssertEndingPairSlashState(ProgramSkillFrame f)
    {
        if (f.EndingPairDraw is not null && !ValidEndingPairDraw(f) || f.PlaySlashRecast is not null && !ValidPlaySlashRecast(f))
            throw new InvalidOperationException("The actual Ending pair or Slash recast lost its original typed issuance/phase and real cost/reward ledger.");
    }
    private bool EndingPairPaidFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (child is not CardsMovedTriggerWindowFrame moved || moved.Batch.ParentFrameId != f.Id ||
            moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat ||
            moved.Batch.OriginSkillId != f.SkillId || moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
        long before, after; CardLocation from, to; CardMoveReason reason; int? id = null;
        if (f.EndingPairDraw is { AwaitingMovement: true } pair && ValidEndingPairDraw(f))
        {
            var draw = pair.Cursor == 0 ? pair.FirstDraw : pair.SecondDraw; if (draw is null || draw.ActualCount != 1) return false;
            before = draw.SequenceBefore; after = draw.SequenceAfter; from = CardLocation.DrawPile; to = CardLocation.Hand(draw.RecipientSeat);
            reason = new($"skill-program.{f.SkillId}.ending-pair.{pair.Cursor}");
        }
        else if (f.PlaySlashRecast is { AwaitingMovement: true } recast && ValidPlaySlashRecast(f))
        {
            if (recast.DrawIssued) { before = recast.DrawSequenceBefore; after = recast.DrawSequenceAfter; from = CardLocation.DrawPile; to = CardLocation.Hand(f.OwnerSeat); reason = CardMoveReasons.RecastDraw; }
            else { before = recast.SequenceBefore; after = recast.SequenceAfter; from = CardLocation.Hand(f.OwnerSeat); to = CardLocation.DiscardPile; reason = CardMoveReasons.RecastDiscard; id = recast.CardId; }
        }
        else return false;
        return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after && m.From == from && m.To == to && m.Reason == reason &&
            (id is null || m.CardId == id));
    }
    private ProgramSkillFrame? EndingPairPaidObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame f || !EndingPairPaidFirstChild(f, _resolutionStack[i + 1])) continue;
            var valid = true;
            for (var n = i + 1; n < _resolutionStack.Count; n++)
            {
                // Local union only after the original real cost/draw invoice and
                // first incoming movement have been proved. No observer presence gate.
                if (!PaidObserverDamageDyingFaceEdge(n) && !EquipmentDonationDamageObserverEdge(n) &&
                    !(_resolutionStack[n - 1] is ProgramSkillFrame p && _resolutionStack[n] is DyingFrame { Continuation: DyingContinuationKind.AttackHpLoss } loss && PaidObserverAttackHpLossDyingMatches(p, loss)) &&
                    !(_resolutionStack[n - 1] is DyingFrame dying && _resolutionStack[n] is ProgramSkillFrame response && PaidObserverDamageDyingProgramMatches(response, dying)))
                { valid = false; break; }
                if (_resolutionStack[n] is DyingFrame d && (IsPaidHandRepaymentProgramAlcoholRide(n, d) || IsPaidHandRepaymentRescueRide(n, d) ||
                    PolicyCounterspellVirtualAlcoholRide(n, d) || PaidObserverDamageVirtualAlcoholRide(n, d))) break;
            }
            if (valid) return f;
        }
        return null;
    }
    private bool IsEndingPairSlashProgramDying() => ActiveDying is { } dying && EndingPairPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasEndingPairSlashDamageObserver(long windowId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == windowId && f is DamageTriggerWindowFrame);
        return index >= 2 && _resolutionStack[index] is DamageTriggerWindowFrame window && _resolutionStack[index - 1] is DamageFrame damage &&
            damage.Id == window.ParentFrameId && _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == damage.ParentFrameId) is { AttackAttempt: not null, AttackReturn: not null } attack &&
            CurrentDamageAttempt?.ResolutionId == attack.Id && EndingPairPaidObserverRoot() is { } root &&
            _resolutionStack.FindIndex(f => f.Id == root.Id) < _resolutionStack.FindIndex(f => f.Id == attack.Id);
    }
}
