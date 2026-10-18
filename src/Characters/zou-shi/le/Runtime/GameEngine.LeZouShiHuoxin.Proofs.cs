namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHuoxin(ProgramSkillFrame f)
    {
        if (f.Huoxin is not { } r || f.SkillId != "le:huoxin" || f.TriggerId != "non-equipment-use" ||
            f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Source != HuoxinSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse ||
            f.WindowContext is not { } context || !TryGetHuoxinUse(f.OwnerSeat, context, out var window) ||
            r.WindowId != window.Id || r.UseId != window.ParentFrameId || r.ActionId != window.Action.ActionId ||
            r.EffectiveKind != window.Action.EffectiveKind || !Enum.IsDefined(r.Stage) ||
            r.Candidates is not System.Collections.IList { IsReadOnly: true } || r.Candidates.Distinct().Count() != r.Candidates.Count ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<HuoxinStartedEvent>().Count(e => e == new HuoxinStartedEvent(f.Id, r.Source, r.GameplayHash,
                r.WindowId, r.UseId, r.ActionId, r.EffectiveKind, r.EffectiveSuit)) != 1 ||
            ProgramEventHistory<HuoxinStartedEvent>().Count(e => e.ActionId == r.ActionId && e.Source.OwnerSeat == f.OwnerSeat) != 1 ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseCommitted) != 1)
            return false;
        var shown = ProgramEventHistory<HuoxinEntityShownEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var marked = ProgramEventHistory<HuoxinEntityMarkedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var paid = ProgramEventHistory<HuoxinObtainPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Paid)
            return r.Stage is HuoxinStage.TargetChoice or HuoxinStage.CardChoice && r.CardId is null && r.PrintedKind is null &&
                !r.WasMarked && !r.MayObtain && r.PaidSequence == 0 && r.BatchId == 0 && f.PendingMovementContinuation is null &&
                shown.Length == 0 && marked.Length == 0 && paid.Length == 0 &&
                (r.Stage == HuoxinStage.TargetChoice ? r.TargetSeat == -1 && r.Candidates.Count == 0 :
                    IsValidPlayerSeat(r.TargetSeat) && r.TargetSeat != f.OwnerSeat && r.Candidates.Count > 0);
        if (r.Stage is not (HuoxinStage.ObtainChoice or HuoxinStage.ObtainChildren) || !IsValidPlayerSeat(r.TargetSeat) ||
            r.TargetSeat == f.OwnerSeat || r.CardId is not { } id || r.PrintedKind is not { } kind || !r.Candidates.Contains(id) ||
            r.MayObtain != (r.WasMarked || r.EffectiveSuit != Suit.None && r.EffectiveSuit == r.ShownEffectiveSuit) ||
            shown is not [var s] || s != new HuoxinEntityShownEvent(f.Id, r.TargetSeat, id, kind, r.ShownEffectiveSuit, r.WasMarked, r.MayObtain) ||
            marked is not [var m] || m != new HuoxinEntityMarkedEvent(f.Id, r.Source, r.GameplayHash, r.TargetSeat, id, kind)) return false;
        if (r.Stage == HuoxinStage.ObtainChoice)
            return r.BatchId == 0 && r.PaidSequence == 0 && paid.Length == 0 && f.PendingMovementContinuation is null;
        return r.MayObtain && r.BatchId > 0 && r.PaidSequence > r.Before && r.PaidSequence <= r.After && paid is [var p] &&
            p == new HuoxinObtainPaidEvent(f.Id, r.BatchId, id, kind, CardLocation.Hand(r.TargetSeat), CardLocation.Hand(f.OwnerSeat), r.PaidSequence, r.Before, r.After) &&
            _cardMovements.Count(move => move.Sequence == p.MovementSequence && move.CardId == id && move.CardKind == kind &&
                move.From == p.From && move.To == p.To && move.Reason.Value == HuoxinObtainReason) == 1 &&
            (f.PendingMovementContinuation is null || IsHuoxinMovement(f, GetPausedPrivateOfferEffect(f), f.PendingMovementContinuation));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28951)]
    private void AssertHuoxin(ProgramSkillFrame f)
    { if (f.Huoxin is not null && !ValidHuoxin(f)) throw new InvalidOperationException("Huoxin lost its true-use origin, hidden slot draft, pre-mark suit/mark ticket or exact once-paid shown entity."); }
}
