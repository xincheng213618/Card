namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidOlGuixin(ProgramSkillFrame f)
    {
        if (f.OlGuixin is not { } r || f.SkillId != "ol:guixin" || f.InstructionIndex != 1 ||
            r.Source != OlGuixinSource(f) || r.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out _) || damage.TargetSeat != f.OwnerSeat ||
            window.Id != r.DamageWindowId || damage.Id != r.DamageFrameId || damage.Amount != r.DamageAmount ||
            f.WindowContext!.OccurrenceIndex != r.OccurrenceIndex || r.OccurrenceIndex < 0 || r.OccurrenceIndex >= damage.Amount ||
            r.OtherSeats.Count != _players.Count - 1 || r.OtherSeats.Distinct().Count() != r.OtherSeats.Count ||
            r.OtherSeats.Any(seat => !IsValidPlayerSeat(seat) || seat == f.OwnerSeat) || r.Cursor < 0 || r.Cursor > r.OtherSeats.Count ||
            r.OtherSeats is not System.Collections.IList { IsReadOnly: true } || r.Payments is not System.Collections.IList { IsReadOnly: true } ||
            r.Payments.Select(p => p.Movement.Cursor).Distinct().Count() != r.Payments.Count ||
            ProgramEventHistory<OlGuixinStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new OlGuixinStartedEvent(f.Id, r.Source, r.GameplayHash, window.Id, damage.Id, r.OccurrenceIndex, damage.Amount, string.Join(",", r.OtherSeats)))
            return false;
        var priorities = ProgramEventHistory<OlGuixinPriorityChosenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.PreferredZone is not { } preferred)
            return r.Stage is OlGuixinStage.PriorityChoice or OlGuixinStage.Finished && priorities.Length == 0 && r.Cursor == 0 &&
                r.Payments.Count == 0 && !r.TurnoverIssued && r.FaceDownBefore is null && r.FaceDownAfter is null && f.PendingMovementContinuation is null;
        if (!MobileGuixinZones.Contains(preferred) || priorities is not [var priority] || priority.PreferredZone != preferred || r.Stage == OlGuixinStage.PriorityChoice)
            return false;
        var prior = -1L;
        for (var cursor = 0; cursor <= r.Cursor && cursor < r.OtherSeats.Count; cursor++)
        {
            var payments = r.Payments.Where(p => p.Movement.Cursor == cursor).ToArray();
            if (payments.Length == 0)
            {
                if (cursor < r.Cursor && ProgramEventHistory<OlGuixinSeatSkippedEvent>().Count(e =>
                    e == new OlGuixinSeatSkippedEvent(f.Id, cursor, r.OtherSeats[cursor])) != 1) return false;
                continue;
            }
            if (payments is not [var paid]) return false;
            var p = paid.Movement;
            if (p.TargetSeat != r.OtherSeats[cursor] || p.From.OwnerSeat != p.TargetSeat || !MobileGuixinZones.Contains(p.From.Zone) ||
                p.CardId <= 0 || p.BatchId <= 0 || p.Before < prior || p.Before < 0 || p.After <= p.Before || p.After > _movementSequence ||
                p.Movements is not System.Collections.IList { IsReadOnly: true } || p.Movements is not [var movement] ||
                movement.CardId != p.CardId || movement.From != p.From || movement.Sequence <= p.Before || movement.Sequence != p.After ||
                movement.Reason.Value != OlGuixinReason || _cardMovements.Count(m => m == movement) != 1 ||
                ProgramEventHistory<OlGuixinMovementPaidEvent>().Where(e => e.FrameId == f.Id && e.Cursor == cursor).ToArray() is not [var invoice] ||
                invoice != new OlGuixinMovementPaidEvent(f.Id, cursor, p.TargetSeat, p.From.Zone, p.BatchId, p.Before, p.After) ||
                ProgramEventHistory<OlGuixinSeatSkippedEvent>().Any(e => e.FrameId == f.Id && e.Cursor == cursor) ||
                paid.Candidates is not System.Collections.IList { IsReadOnly: true } || paid.Candidates.Count == 0 ||
                paid.Candidates.Select(c => c.CardId).Distinct().Count() != paid.Candidates.Count ||
                !paid.Candidates.OrderBy(c => c.CardId).SequenceEqual(paid.Candidates) ||
                paid.Candidates.Any(c => c.CardId <= 0 || c.From.OwnerSeat != p.TargetSeat || !MobileGuixinZones.Contains(c.From.Zone) ||
                    _cardMovements.LastOrDefault(m => m.CardId == c.CardId && m.Sequence <= p.Before)?.To != c.From)) return false;
            var zone = OlGuixinPriority(preferred).First(z => paid.Candidates.Any(c => c.From.Zone == z));
            var choices = paid.Candidates.Where(c => c.From.Zone == zone).ToArray();
            var random = new DeterministicRandom(unchecked((int)paid.RandomBefore));
            var selected = choices[random.Next(choices.Length)];
            if (p.From.Zone != zone || paid.RandomBefore == 0 || random.State != paid.RandomAfter || selected.CardId != p.CardId || selected.From != p.From)
                return false;
            var card = GetAdvancedCard(p.CardId);
            var expected = card.IsGeneralWeapon && p.From.Zone == CardZoneKind.Equipment ||
                IsGuanduThunderChariotDeparture(card, p.From, CardLocation.Hand(f.OwnerSeat)) ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat);
            if (movement.To != NormalizeEphemeralCardDestination(card, p.From, expected)) return false;
            prior = p.After;
        }
        if (r.Payments.Any(p => p.Movement.Cursor < 0 || p.Movement.Cursor >= r.OtherSeats.Count || p.Movement.Cursor > r.Cursor) ||
            r.Stage == OlGuixinStage.MovementChildren && (r.Cursor >= r.OtherSeats.Count || r.Payments.All(p => p.Movement.Cursor != r.Cursor)) ||
            r.Stage == OlGuixinStage.Visiting && r.Payments.Any(p => p.Movement.Cursor == r.Cursor)) return false;
        if (r.TurnoverIssued)
            return r.Cursor == r.OtherSeats.Count && r.Stage is OlGuixinStage.TurnoverChildren or OlGuixinStage.Finished &&
                r.FaceDownBefore.HasValue && r.FaceDownAfter.HasValue &&
                ProgramEventHistory<OlGuixinTurnoverIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var turn] &&
                turn == new OlGuixinTurnoverIssuedEvent(f.Id, r.FaceDownBefore.Value, r.FaceDownAfter.Value);
        return r.FaceDownBefore is null && r.FaceDownAfter is null && r.Stage != OlGuixinStage.TurnoverChildren;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 40700)] private void RequireOlGuixin(ProgramSkillFrame f)
    {
        if (f.OlGuixin is null) return;
        if (!ValidOlGuixin(f) || f.PendingMovementContinuation is { } pending &&
            !OlGuixinMovementProof(f, ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)
                .GetPausedInstruction(f.InstructionIndex).Effect, pending))
            throw new InvalidOperationException("OL Guixin lost its original damage point, immutable priority, private RNG choice or actual paid movement.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 40700)]
    private bool OlGuixinMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ResolveOlShenCaoCaoGuixin && f.OlGuixin is { Stage: OlGuixinStage.MovementChildren } &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 &&
        pending.CoverageResultBind is null && ValidOlGuixin(f);
}
