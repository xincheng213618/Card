namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26210)]
    private void AssertHuoJunFenrui(ProgramSkillFrame f)
    {
        if (f.HuoJunFenrui is not { } r) return;
        if (r.Source != HuoJunSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.InstructionIndex != 1 ||
            f.TriggerId is null || !Enum.IsDefined(r.Stage) || r.TurnNumber != _turnNumber || r.TurnOwnerSeat != _turnProgression.OwnerSeat ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context || context.ParentFrameId != r.EndingWindowId ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == r.EndingWindowId) is not { } window ||
            window.OwnerSeat != f.OwnerSeat || window.ItemIndex < 0 || window.ItemIndex >= window.Items.Count ||
            window.Items[window.ItemIndex] is not { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate } || !MountObserverCandidateMatches(f, candidate))
            throw new InvalidOperationException("Fenrui lost its exact actual-ending candidate, source instance, frozen turn or owning effect cursor.");
        var payments = ProgramEventHistory<HuoJunFenruiCostPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.CostCardId is not { } id)
        {
            if (r.Stage != HuoJunFenruiStage.Cost || r.CostFrom is not null || r.RestoredSlot is not null || payments.Length != 0)
                throw new InvalidOperationException("An unpaid Fenrui cannot already own restored slots or paid native effects.");
            return;
        }
        if (r.CostFrom is not { } from || from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            payments is not [var paid] || paid.Source != r.Source || paid.CardId != id || paid.From != from ||
            paid.SequenceBefore != r.SequenceBefore || paid.SequenceAfter != r.SequenceAfter ||
            _cardMovements.Count(m => m.CardId == id && m.From == from && m.To == CardLocation.DiscardPile && m.Reason.Value == HuoJunDiscardReason &&
                m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter) != 1)
            throw new InvalidOperationException("Fenrui must retain the single real discarded entity and cannot repay it after a native child.");
        var restores = ProgramEventHistory<HuoJunFenruiSlotRestoredEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.RestoredSlot is { } slot ? !Enum.IsDefined(slot) || restores is not [var restored] || restored.OwnerSeat != f.OwnerSeat || restored.Slot != slot
            : restores.Length != 0 || r.Stage is not (HuoJunFenruiStage.CostChildren or HuoJunFenruiStage.Restore))
            throw new InvalidOperationException("Fenrui may restore exactly one currently abolished native category, once.");
        var issued = ProgramEventHistory<HuoJunFenruiEquipmentIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var returned = ProgramEventHistory<HuoJunFenruiEquipmentReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.NativeUseId == 0)
        {
            if (r.NativeActionId != 0 || r.EquipmentCardId is not null || r.EquipmentKind is not null || issued.Length != 0 || returned.Length != 0 ||
                r.Stage == HuoJunFenruiStage.NativeEquipment)
                throw new InvalidOperationException("Fenrui cannot synthesize an equipment use or return without one actual random deck entity.");
        }
        else if (r.NativeActionId <= 0 || r.EquipmentCardId is not { } equipment || r.EquipmentKind is not { } kind || !EquipmentCatalog.IsEquipment(kind) ||
            r.RestoredSlot != EquipmentCatalog.Get(kind).Slot || issued is not [var issue] || issue.Source != r.Source || issue.CardId != equipment ||
            issue.CardKind != kind || issue.NativeUseId != r.NativeUseId || issue.NativeActionId != r.NativeActionId || issue.Slot != r.RestoredSlot ||
            (r.Stage == HuoJunFenruiStage.NativeEquipment ? returned.Length != 0 : returned is not [var done] || done.NativeUseId != r.NativeUseId || done.NativeActionId != r.NativeActionId))
            throw new InvalidOperationException("Fenrui must preserve one exact actual native equipment action and its unique completed return.");
        var attempts = ProgramEventHistory<HuoJunFenruiDamageCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == HuoJunFenruiStage.DamageIssued ? r.DamageTargetSeat is not { } target || target == f.OwnerSeat || r.DamageAmount <= 0 ||
                attempts is not [var attempt] || attempt.Source != r.Source || attempt.TargetSeat != target || attempt.DamageAmount != r.DamageAmount ||
                attempt.OwnEquipmentCount - attempt.TargetEquipmentCount != r.DamageAmount ||
                ProgramEventHistory<HuoJunFenruiDamageCommittedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId) != 1
            : r.DamageTargetSeat is not null || r.DamageAmount != 0 || attempts.Length != 0)
            throw new InvalidOperationException("Fenrui's one game-long optional damage branch must be committed once with its exact current equipment-count difference.");
    }
}
