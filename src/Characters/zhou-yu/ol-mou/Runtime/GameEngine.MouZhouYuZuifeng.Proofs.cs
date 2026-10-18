namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouZuifengHistory(long frameId, out MouZuifengStartedEvent started, out MouZuifengCostPaidEvent paid)
    {
        started = null!; paid = null!;
        if (ProgramEventHistory<MouZuifengStartedEvent>().Where(e => e.FrameId == frameId).ToArray() is not [var s] ||
            ProgramEventHistory<MouZuifengCostPaidEvent>().Where(e => e.FrameId == frameId).ToArray() is not [var p] ||
            s.Source.SkillId != MouZuifengSkill || s.Source.BindingId != MouZuifengBinding || s.Source != p.Source || s.GameplayHash != p.GameplayHash ||
            s.Phase != p.Phase || s.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || s.Phase.ActorSeat != s.Source.OwnerSeat ||
            s.Phase.TurnNumber != s.ActualTurn || s.Phase.ActualTurnOwnerSeat != s.TurnOwner || !IsRecordedActualDiscardRecoveryPhase(s.Phase) ||
            _contentRegistry.GetSkill(s.Source.SkillId).Program?.GameplayHash != s.GameplayHash ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == frameId && e.SkillId == s.Source.SkillId &&
                e.ActivationId == s.Source.BindingId && e.OwnerSeat == s.Source.OwnerSeat) != 1 ||
            !IsValidPlayerSeat(p.TargetSeat) || p.CardId <= 0 || p.BatchId <= 0 || p.From != CardLocation.Hand(s.Source.OwnerSeat) || p.To == p.From ||
            p.EffectiveSuit is not (Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond) || p.Before < 0 || p.Sequence <= p.Before ||
            p.After < p.Sequence || p.After > _movementSequence || p.MaxHpAtPayment <= 0 || p.UseOrdinal <= 0 || p.UseOrdinal > p.MaxHpAtPayment ||
            ProgramEventHistory<MouZuifengCostChosenEvent>().Count(e => e == new MouZuifengCostChosenEvent(frameId,p.CardId,p.PrintedKind,p.EffectiveSuit)) != 1 ||
            _cardMovements.Count(m => m.Sequence == p.Sequence && m.CardId == p.CardId && m.CardKind == p.PrintedKind &&
                m.From == p.From && m.To == p.To && m.Reason.Value == MouZuifengDiscardReason) != 1) return false;
        var payments = ProgramEventHistory<MouZuifengCostPaidEvent>().Where(e => e.Source.OwnerSeat == s.Source.OwnerSeat && e.Phase == s.Phase).ToArray();
        if (payments.Count(e => e.Sequence <= p.Sequence) != p.UseOrdinal || payments.Select(e => e.Sequence).Distinct().Count() != payments.Length) return false;
        started = s; paid = p; return true;
    }
    private bool ValidMouZuifengStarted(ProgramSkillFrame f) => f.MouZuifeng is { } r &&
        f.SkillId == MouZuifengSkill && f.ActivationId == MouZuifengBinding && f.TriggerId is null && f.WindowContext is null &&
        f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && f.InstructionIndex == 1 && r.InstructionIndex == f.InstructionIndex &&
        r.Source == MouZuifengSource(f) && r.GameplayHash == f.GameplayHash && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == r.GameplayHash &&
        r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && r.Phase.ActorSeat == f.OwnerSeat && r.Phase.TurnNumber == r.ActualTurn &&
        r.Phase.ActualTurnOwnerSeat == r.TurnOwner && IsRecordedActualDiscardRecoveryPhase(r.Phase) &&
        ProgramEventHistory<MouZuifengStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var s] &&
        s == new MouZuifengStartedEvent(f.Id,r.Source,r.GameplayHash,r.Phase,r.ActualTurn,r.TurnOwner) &&
        ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId && e.OwnerSeat == f.OwnerSeat) == 1;
    private bool IsPaidMouZuifeng(ProgramSkillFrame f) => f.MouZuifeng is { Paid: true } r && ValidMouZuifengStarted(f) &&
        ValidMouZuifengHistory(f.Id,out var s,out var p) && s.Source == r.Source && p.TargetSeat == r.TargetSeat &&
        p.CardId == r.CostId && p.PrintedKind == r.CostKind && p.EffectiveSuit == r.CostSuit && p.BatchId == r.CostBatchId && p.Sequence == r.CostSequence;
    private bool MouZuifengCostBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.MouZuifeng is { Stage: MouZuifengStage.CostChildren } r &&
        IsPaidMouZuifeng(f) && b.Id == r.CostBatchId && b.ParentFrameId == f.Id && b.OriginOwnerSeat == f.OwnerSeat &&
        b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements is [var m] &&
        _cardMovements.Contains(m) && m.Sequence == r.CostSequence && m.CardId == r.CostId && m.CardKind == r.CostKind &&
        m.From == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == MouZuifengDiscardReason;
    private bool IsMouZuifengMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.MouZuifeng is { Stage: MouZuifengStage.CostChildren or MouZuifengStage.NativeChildren } && IsPaidMouZuifeng(f) &&
        effect?.Op == SkillProgramEffectOp.ResolveMouZhouYuZuifeng && f.PendingMovementContinuation == pending &&
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,29020)]
    private void AssertMouZuifeng(ProgramSkillFrame f)
    {
        if (f.MouZuifeng is not { } r) return;
        if (!ValidMouZuifengStarted(f) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Viewed is not System.Collections.IList { IsReadOnly: true } || r.NativeBatches is not System.Collections.IList { IsReadOnly: true } ||
            r.Viewed.Select(v => v.CardId).Distinct().Count() != r.Viewed.Count || r.NativeBatches.Distinct().Count() != r.NativeBatches.Count ||
            r.Paid && !IsPaidMouZuifeng(f) || !r.Paid && (r.Stage is not (MouZuifengStage.CostChoice or MouZuifengStage.TargetChoice) ||
                r.CostBatchId != 0 || r.CostSequence != 0 || r.TargetSeat != -1))
            throw new InvalidOperationException("Zuifeng lost its exact actual-Play source, dynamic paid quota or original Hand discard invoice.");
        if (r.CostId is { } cost && (r.CostKind is null || r.CostSuit is not (Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond) ||
            ProgramEventHistory<MouZuifengCostChosenEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var chosen] ||
            chosen != new MouZuifengCostChosenEvent(f.Id,cost,r.CostKind.Value,r.CostSuit)))
            throw new InvalidOperationException("Zuifeng changed the privately chosen unique-suit original cost.");
        if (r.Viewed.Count > 0)
        {
            if (!IsPaidMouZuifeng(f) || ProgramEventHistory<MouZuifengHandViewedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var view] ||
                view.ViewerSeat != f.OwnerSeat || view.TargetSeat != r.TargetSeat || view.HandCount != r.Viewed.Count || view.Sequence < r.CostSequence ||
                ProgramEventHistory<MouZuifengViewedEntityEvent>().Count(e => e.FrameId == f.Id) != r.Viewed.Count ||
                r.Viewed.Where((v,i) => v.Index != i || v.CardId <= 0 || ProgramEventHistory<MouZuifengViewedEntityEvent>().Count(e =>
                    e == new MouZuifengViewedEntityEvent(f.Id,i,v.CardId,v.PrintedKind)) != 1).Any())
                throw new InvalidOperationException("Zuifeng lost its frozen complete private Hand view.");
        }
        else if (r.Stage is not (MouZuifengStage.CostChoice or MouZuifengStage.TargetChoice or MouZuifengStage.CostChildren))
            throw new InvalidOperationException("Zuifeng cannot show an entity without first viewing a real nonempty Hand.");
        if (r.ShownId is { } shown)
        {
            if (!r.Viewed.Any(v => v.CardId == shown && v.PrintedKind == r.ShownKind) || r.ShownKind is null ||
                ProgramEventHistory<MouZuifengShownEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var show] ||
                show != new MouZuifengShownEvent(f.Id,f.OwnerSeat,r.TargetSeat,shown,r.ShownKind.Value,r.ShownSuit,r.ShownSuit == r.CostSuit) ||
                ProgramEventHistory<CardsRevealedEvent>().Count(e => e.ResolutionId == f.Id && e.Cards.Count == 1 && e.Cards[0].Id == shown) != 1)
                throw new InvalidOperationException("Zuifeng's public show no longer identifies one real original from its private full Hand view.");
        }
        else if (r.Stage is not (MouZuifengStage.CostChoice or MouZuifengStage.TargetChoice or MouZuifengStage.CostChildren or MouZuifengStage.HandViewChoice))
            throw new InvalidOperationException("Zuifeng cannot issue fire or Alcohol without its original public show.");
        if (r.FireIssued && (r.ShownSuit != r.CostSuit || ProgramEventHistory<MouZuifengFireIssuedEvent>().Count(e =>
            e == new MouZuifengFireIssuedEvent(f.Id,f.OwnerSeat,r.TargetSeat,r.ShownId!.Value)) != 1) ||
            r.FireReturned && (!r.FireIssued || ProgramEventHistory<MouZuifengFireReturnedEvent>().Count(e => e == new MouZuifengFireReturnedEvent(f.Id,r.TargetSeat)) != 1) ||
            r.Stage is MouZuifengStage.AlcoholChoice or MouZuifengStage.NativeIssuing or MouZuifengStage.NativeUse or MouZuifengStage.NativeChildren && r.FireIssued && !r.FireReturned)
            throw new InvalidOperationException("Zuifeng cannot return or replace its native once-issued fire damage before the paid children drain.");
        if (r.UseId > 0)
        {
            if (!IsPaidMouZuifeng(f) || r.ActionId <= 0 || r.Stage is not (MouZuifengStage.NativeUse or MouZuifengStage.NativeChildren) ||
                ProgramEventHistory<MouZuifengAlcoholIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var issued] ||
                issued.UseId != r.UseId || issued.ActionId != r.ActionId || issued.ActorSeat != r.TargetSeat || issued.TargetSeat != f.OwnerSeat ||
                issued.CardId != r.ShownId || issued.PrintedKind != r.ShownKind || issued.EffectiveSuit != r.WineSuit || issued.EffectiveRank != r.WineRank ||
                r.WinePaidSequence <= r.WineBefore || ProgramEventHistory<MouZuifengAlcoholPaidEvent>().Count(e => e.FrameId == f.Id &&
                    e.UseId == r.UseId && e.ActionId == r.ActionId && e.CardId == r.ShownId && e.Sequence == r.WinePaidSequence) != 1 ||
                r.UseReturned != (r.Stage == MouZuifengStage.NativeChildren) ||
                r.UseReturned && (LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<MouZuifengAlcoholReturnedEvent>().Count(e =>
                    e == new MouZuifengAlcoholReturnedEvent(f.Id,r.UseId,r.ActionId)) != 1))
                throw new InvalidOperationException("Zuifeng lost its exact paid real Alcohol use, printed material or typed once-only native return.");
        }
        else if (r.ActionId != 0 || r.WinePaidSequence != 0 || r.UseReturned || r.NativeBatches.Count != 0)
            throw new InvalidOperationException("Zuifeng has no native Use owner for this payment or return.");
        if (f.PendingMovementContinuation is { } pending && !IsMouZuifengMovement(f,GetPausedPrivateOfferEffect(f),pending))
            throw new InvalidOperationException("Zuifeng's movement continuation lost its original paid instruction.");
    }
}
