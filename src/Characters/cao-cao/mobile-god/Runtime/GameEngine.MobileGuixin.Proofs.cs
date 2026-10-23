namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMobileGuixin(ProgramSkillFrame f)
    {
        if (f.MobileGuixin is not { } r || f.SkillId != "mobile:guixin" || f.InstructionIndex != 1 ||
            r.Source != MobileGuixinSource(f) || r.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out _) || damage.TargetSeat != f.OwnerSeat ||
            window.Id != r.DamageWindowId || damage.Id != r.DamageFrameId || damage.Amount != r.DamageAmount ||
            f.WindowContext!.OccurrenceIndex != r.OccurrenceIndex || r.OccurrenceIndex < 0 || r.OccurrenceIndex >= damage.Amount ||
            r.OtherSeats.Count != _players.Count - 1 || r.OtherSeats.Distinct().Count() != r.OtherSeats.Count ||
            r.OtherSeats.Any(seat => !IsValidPlayerSeat(seat) || seat == f.OwnerSeat) || r.Cursor < 0 || r.Cursor > r.OtherSeats.Count ||
            r.Payments.Select(p => p.Cursor).Distinct().Count() != r.Payments.Count ||
            ProgramEventHistory<MobileGuixinStartedEvent>().Count(e => e == new MobileGuixinStartedEvent(f.Id, r.Source, r.GameplayHash,
                window.Id, damage.Id, r.OccurrenceIndex, damage.Amount, string.Join(",", r.OtherSeats))) != 1) return false;
        var prior = -1L;
        for (var cursor = 0; cursor <= r.Cursor && cursor < r.OtherSeats.Count; cursor++)
        {
            var payments = r.Payments.Where(p => p.Cursor == cursor).ToArray();
            if (payments.Length == 0)
            {
                if (cursor < r.Cursor && ProgramEventHistory<MobileGuixinSeatSkippedEvent>().Count(e =>
                    e == new MobileGuixinSeatSkippedEvent(f.Id, cursor, r.OtherSeats[cursor])) != 1) return false;
                continue;
            }
            if (payments is not [var p] || p.TargetSeat != r.OtherSeats[cursor] || p.From.OwnerSeat != p.TargetSeat ||
                !MobileGuixinZones.Contains(p.From.Zone) || p.CardId <= 0 || p.BatchId <= 0 || p.Before < prior || p.Before < 0 ||
                p.After <= p.Before || p.After > _movementSequence || p.Movements is not [var movement] ||
                movement.CardId != p.CardId || movement.From != p.From || movement.Sequence <= p.Before || movement.Sequence != p.After ||
                movement.Reason.Value != MobileGuixinReason || _cardMovements.Count(m => m == movement) != 1 ||
                ProgramEventHistory<MobileGuixinMovementPaidEvent>().Count(e => e == new MobileGuixinMovementPaidEvent(f.Id, cursor,
                    p.TargetSeat, p.BatchId, 1, p.Before, p.After)) != 1 ||
                ProgramEventHistory<MobileGuixinSeatSkippedEvent>().Any(e => e.FrameId == f.Id && e.Cursor == cursor)) return false;
            var card = GetAdvancedCard(p.CardId);
            var expected = card.IsGeneralWeapon && p.From.Zone == CardZoneKind.Equipment ||
                IsGuanduThunderChariotDeparture(card, p.From, CardLocation.Hand(f.OwnerSeat)) ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat);
            if (movement.To != NormalizeEphemeralCardDestination(card, p.From, expected)) return false;
            prior = p.After;
        }
        if (r.Payments.Any(p => p.Cursor < 0 || p.Cursor >= r.OtherSeats.Count || p.Cursor > r.Cursor) ||
            r.Stage == MobileGuixinStage.MovementChildren && (r.Cursor >= r.OtherSeats.Count || r.Payments.All(p => p.Cursor != r.Cursor)) ||
            r.Stage == MobileGuixinStage.Choosing && r.Payments.Any(p => p.Cursor == r.Cursor)) return false;
        if (r.TurnoverIssued)
            return r.Cursor == r.OtherSeats.Count && r.Stage is MobileGuixinStage.TurnoverChildren or MobileGuixinStage.Finished &&
                r.FaceDownBefore.HasValue && r.FaceDownAfter.HasValue &&
                ProgramEventHistory<MobileGuixinTurnoverIssuedEvent>().Count(e => e == new MobileGuixinTurnoverIssuedEvent(f.Id,
                    r.FaceDownBefore.Value, r.FaceDownAfter.Value)) == 1;
        return r.FaceDownBefore is null && r.FaceDownAfter is null && r.Stage != MobileGuixinStage.TurnoverChildren;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 40300)] private void RequireMobileGuixin(ProgramSkillFrame f)
    {
        if (f.MobileGuixin is null) return;
        if (!ValidMobileGuixin(f) || f.PendingMovementContinuation is { } pending &&
            !MobileGuixinMovementProof(f, ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)
                .GetPausedInstruction(f.InstructionIndex).Effect, pending))
            throw new InvalidOperationException("Mobile Guixin lost its original damage point, source instance, paid card movement or continuation cursor.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 40300)]
    private bool MobileGuixinMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ResolveMobileShenCaoCaoGuixin && f.MobileGuixin is { Stage: MobileGuixinStage.MovementChildren } &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 &&
        pending.CoverageResultBind is null && ValidMobileGuixin(f);
}
