namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsValidPaidColorDamageClaimFrame(ProgramSkillFrame root)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
        if (root.PaidColorConversion is { } paid)
        {
            var p = paid.Origin; var binding = GetProgramCardSet(root, paid.SourceBind);
            if (!MatchesPaidColorDrawOwner(root) || paid.InstructionIndex != root.InstructionIndex || root.InstructionIndex != 2 ||
                plan.Instructions.Count != 2 || plan.Instructions[1].Op != SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel ||
                plan.Instructions[1].SourceBind != paid.SourceBind || p.ActualTurnNumber != _turnNumber || p.TurnOwnerSeat != _currentSeat ||
                p.GameplayHash != root.GameplayHash || !Enum.IsDefined(p.EffectiveSuit) || !IsActualPaidColorMovement(root.OwnerSeat, p) ||
                binding.CardIds is not [var card] || card != p.CardId || binding.SourceLocations is not [var from] || from != p.From ||
                CompleteProgramEventHistory().OfType<ProgramPaidColorConversionPaidEvent>().Count(e => e.ProgramFrameId == root.Id &&
                    e.EffectIndex == paid.InstructionIndex - 1 && e.Origin == p && e.Source == new CardConversionSource(root.SkillId,
                        GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId)) != 1) return false;
        }
        if (root.ActualTurnDamageClaim is { } claim)
        {
            if (!MatchesActualTurnDamageEnding(root) || root.InstructionIndex != 1 || claim.InstructionIndex != 0 ||
                plan.Instructions.Count != 1 || plan.Instructions[0].Op != SkillProgramEffectOp.ClaimActualTurnDamageEntities ||
                claim.ActualTurnNumber != _turnNumber || claim.TurnOwnerSeat != _currentSeat ||
                claim.EndingFrameId != root.WindowContext!.ParentFrameId || claim.OccurrenceIndex != root.WindowContext.OccurrenceIndex ||
                claim.CardIds.Any(id => id <= 0) || claim.CardIds.Distinct().Count() != claim.CardIds.Count ||
                claim.SequenceBefore < 0 || claim.SequenceAfter != claim.SequenceBefore + claim.CardIds.Count) return false;
            var events = CompleteProgramEventHistory().OfType<ActualTurnDamageEntityClaimedEvent>().Where(e => e.ProgramFrameId == root.Id).ToArray();
            if (!events.Select(e => e.CardId).SequenceEqual(claim.CardIds) || events.Where((e, index) => e.ClaimIndex != index ||
                e.MovementSequence != claim.SequenceBefore + index + 1 || e.Source != new CardConversionSource(root.SkillId,
                    GetProgramBindingId(root), root.OwnerSeat, root.SkillInstanceId) || e.GameplayHash != root.GameplayHash ||
                e.ActualTurnNumber != claim.ActualTurnNumber || e.TurnOwnerSeat != claim.TurnOwnerSeat ||
                !CompleteProgramEventHistory().OfType<ActualTurnCardDamageEntityEvent>().Any(d => d.DamageFrameId == e.OriginalDamageFrameId &&
                    d.ActualTurnNumber == claim.ActualTurnNumber && d.TurnOwnerSeat == claim.TurnOwnerSeat && d.VictimSeat == root.OwnerSeat &&
                    d.CardId == e.CardId && d.Amount > 0 && d.MaterialIndex >= 0 && d.MaterialIndex < d.MaterialCount) ||
                _cardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId &&
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == ActualTurnDamageClaimReason) != 1).Any()) return false;
            if (_cardMovements.Any(m => m.Sequence > claim.SequenceBefore && m.Sequence <= claim.SequenceAfter &&
                (m.From != CardLocation.DiscardPile || m.To != CardLocation.Hand(root.OwnerSeat) ||
                 m.Reason.Value != ActualTurnDamageClaimReason || !claim.CardIds.Contains(m.CardId)))) return false;
        }
        return root.PaidColorConversion is not null || root.ActualTurnDamageClaim is not null;
    }
    private void AssertPaidColorDamageClaimFrame(ProgramSkillFrame root)
    {
        if ((root.PaidColorConversion is not null || root.ActualTurnDamageClaim is not null) && !IsValidPaidColorDamageClaimFrame(root))
            throw new InvalidOperationException("A paid color or independent damage claim lost its exact original candidate, cost and movement ledger.");
    }

    private ProgramSkillFrame? PaidColorDamageClaimObserverRoot(long? exactDamageWindowId = null)
    {
        for (var index = 1; index < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root ||
                root.PaidColorConversion is null && root.ActualTurnDamageClaim is null || !IsValidPaidColorDamageClaimFrame(root)) continue;
            if (exactDamageWindowId is { } windowId && !_resolutionStack.Skip(index + 1).Any(f =>
                f.Id == windowId && (f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame))) continue;
            if (index == _resolutionStack.Count - 1) return root;
            if (root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat) continue;
            var before = root.PaidColorConversion?.Origin.MovementSequence - 1 ?? root.ActualTurnDamageClaim!.SequenceBefore;
            var after = root.PaidColorConversion?.Origin.SequenceAfter ?? root.ActualTurnDamageClaim!.SequenceAfter;
            if (_resolutionStack[index + 1] is CardsMovedTriggerWindowFrame movement)
            {
                if (movement.Batch.ParentFrameId != root.Id || movement.Batch.OriginSkillId != root.SkillId ||
                    movement.Batch.OriginSkillInstanceId != root.SkillInstanceId || movement.Batch.OriginOwnerSeat != root.OwnerSeat ||
                    movement.Batch.AwaitingProgramFrameId is { } waiting && waiting != root.Id || movement.Batch.Movements.Count == 0 ||
                    movement.Batch.Movements.Any(m => m.Sequence <= before || m.Sequence > after || !_cardMovements.Contains(m))) continue;
            }
            else
            {
                // Only a paid Silver Lion removal can enter a recovery before
                // this root's ordinary card movement observers.
                if (root.PaidColorConversion is not { Origin.From.Zone: CardZoneKind.Equipment } lion ||
                    !_cardMovements.Any(m => m.Sequence == lion.Origin.MovementSequence && m.CardId == lion.Origin.CardId &&
                        m.From == CardLocation.Equipment(root.OwnerSeat) && m.CardKind == CardKind.SilverLion &&
                        m.To == CardLocation.DiscardPile && m.Reason.Value == PaidColorDiscardReason)) continue;
                if (_resolutionStack[index + 1] is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        hp.Change.SourceSeat != root.OwnerSeat || hp.Change.TargetSeat != root.OwnerSeat || hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount != 1) continue;
                }
                else if (_resolutionStack[index + 1] is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != root.OwnerSeat || recovery.Attempt.TargetSeat != root.OwnerSeat || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != PaidColorDiscardReason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
            {
                // The paid root and initial movement prefix above are exact.
                // This existing typed edge also proves a gain observer's real
                // Program damage, its own prevention/applied windows and Dying.
                if (!PaidColorDamageClaimObserverEdge(child)) { valid = false; break; }
                // Validate the incoming edge first; then accept only an exact
                // existing whole rescue/Alcohol subtree, including its children.
                if (_resolutionStack[child] is DyingFrame d && (IsPaidHandRepaymentRescueRide(child, d) ||
                    IsPaidHandRepaymentProgramAlcoholRide(child, d) || PolicyCounterspellVirtualAlcoholRide(child, d) ||
                    PaidObserverDamageVirtualAlcoholRide(child, d))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    // The caller has already locked this new paid root's original candidate,
    // scalar payment and exact first ledger edge. No old generic edge changes.
    private bool PaidColorDamageClaimObserverEdge(int index)
    {
        var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
        if (PaidObserverDamageDyingFaceEdge(index)) return true;
        if (parent is ProgramSkillFrame attack && child is DyingFrame { Continuation: DyingContinuationKind.AttackHpLoss } replaced)
            return PaidObserverAttackHpLossDyingMatches(attack, replaced);
        if (parent is DyingFrame { Continuation: DyingContinuationKind.Damage or DyingContinuationKind.AttackHpLoss } dying && child is ProgramSkillFrame response)
            return PaidObserverDamageDyingProgramMatches(response, dying);
        return EquipmentDonationDamageObserverEdge(index);
    }
    private bool HasPaidColorDamageClaimDamageObserver(long exactDamageWindowId) =>
        PaidColorDamageClaimObserverRoot(exactDamageWindowId) is not null;
    private bool IsPaidColorDamageClaimProgramDying() => ActiveDying is { } dying &&
        PaidColorDamageClaimObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsPaidColorDamageClaimMovement(ProgramSkillFrame root, SkillProgramEffect? op, ProgramMovementContinuation pending) =>
        pending.SubjectSeat == root.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (op?.Op == SkillProgramEffectOp.DiscardBoundCardForOppositeTurnDuel && root.PaidColorConversion is not null ||
         op?.Op == SkillProgramEffectOp.ClaimActualTurnDamageEntities && root.ActualTurnDamageClaim is not null) && IsValidPaidColorDamageClaimFrame(root);
    private void AssertPaidColorDamageClaims()
    {
        AssertPaidColorTurnConversions();
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.PaidColorConversion is not null || f.ActualTurnDamageClaim is not null))
            if (!IsValidPaidColorDamageClaimFrame(f) || PaidColorDamageClaimObserverRoot()?.Id != f.Id)
                throw new InvalidOperationException("A paid color or damage claim lost its precise owning movement/recovery/Dying subtree.");
    }
}
