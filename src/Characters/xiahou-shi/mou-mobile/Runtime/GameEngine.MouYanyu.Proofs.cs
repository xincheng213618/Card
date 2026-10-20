namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouXiahouShiYanyuRoot(ProgramSkillFrame f)
    {
        if (f.MouXiahouShiYanyu is not { } r || f.MouXiahouShiQiaoshi is not null || r.Source != MouXiahouShiSource(f) || r.GameplayHash != f.GameplayHash ||
            f.SkillId != MouXiahouShiComposition.Yanyu || f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouXiahouShiComposition.IsYanyu(p) ||
            r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || r.Phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            r.Phase.TurnNumber != r.Turn.TurnNumber || r.Phase.ActualTurnOwnerSeat != r.Turn.OwnerSeat) return false;
        var started = new MouXiahouShiYanyuStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.Phase, r.Support, r.WindowId, r.FrozenDiscardCount);
        return ProgramEventHistory<MouXiahouShiYanyuStartedEvent>().Count(e => e == started) == 1 && MouXiahouShiTurnBefore(started, r.Turn) &&
            (r.Support ? ExactMouXiahouShiSupportContext(f) && r.WindowId == f.WindowContext!.ParentFrameId && r.FrozenDiscardCount > 0 && f.SelectedCardIds.Count == 0 :
                f.ActivationId == "discard-slash" && f.TriggerId is null && f.WindowContext is null && f.SelectedCardIds.Count == 1 && f.SelectedTargetSeats.Count == 0 && r.WindowId == 0 && r.FrozenDiscardCount == 0);
    }
    private bool ValidMouXiahouShiYanyu(ProgramSkillFrame f)
    {
        if (f.MouXiahouShiYanyu is not { } r || !MouXiahouShiYanyuRoot(f) || r.Before < 0 || r.Before > r.After || r.After > _movementSequence) return false;
        var paid = ProgramEventHistory<MouXiahouShiYanyuDiscardPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Support)
        {
            if (r.TargetSeat is not null || r.Stage == MouXiahouShiYanyuStage.TargetChoice) return false;
            if (r.Payment is not { } payment) return r.Stage == MouXiahouShiYanyuStage.Discarding && paid.Length == 0 && r.Draw is null;
            if (f.SelectedCardIds is not [var id] || payment.CardId != id || paid is not [var cost] || !ValidMouXiahouShiPaidHistory(cost) ||
                cost.Source != r.Source || cost.Turn != r.Turn || cost.Phase != r.Phase ||
                payment != new MouXiahouShiDiscardPayment(cost.CardId, cost.PrintedKind, cost.From, cost.To, cost.Sequence)) return false;
        }
        else
        {
            if (paid.Length != 0 || r.Payment is not null || r.Stage is MouXiahouShiYanyuStage.Discarding or MouXiahouShiYanyuStage.DiscardChildren ||
                r.FrozenDiscardCount != MouXiahouShiTurnDiscards(f.OwnerSeat, r.Turn)) return false;
            var accepted = ProgramEventHistory<MouXiahouShiYanyuTargetAcceptedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.TargetSeat is null) return r.Stage == MouXiahouShiYanyuStage.TargetChoice && accepted.Length == 0 && r.Draw is null;
            if (!IsValidPlayerSeat(r.TargetSeat.Value) || r.TargetSeat == f.OwnerSeat || accepted is not [var target] ||
                target != new MouXiahouShiYanyuTargetAcceptedEvent(f.Id, r.TargetSeat.Value, r.FrozenDiscardCount, checked(r.FrozenDiscardCount * 3))) return false;
        }
        return r.Stage is MouXiahouShiYanyuStage.DiscardChildren or MouXiahouShiYanyuStage.Drawing ? r.Draw is null :
            r.Stage == MouXiahouShiYanyuStage.DrawChildren && r.Draw is { } draw && MouXiahouShiDrawSigned(f, draw, r.Support ? r.TargetSeat!.Value : f.OwnerSeat, r.Support ? checked(r.FrozenDiscardCount * 3) : 1);
    }
}
