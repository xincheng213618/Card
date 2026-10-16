namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginZhangShiPingHongjiPhase(ProgramSkillFrame frame, TurnPhase phase, int recipientSeat)
    {
        if (!HasHongjiCapability || frame.ZhangShiPingHongji is not { Stage: HongjiStage.ExtraPhase, Promise: { } promise } receipt ||
            receipt.Phase != phase || phase is not (TurnPhase.Draw or TurnPhase.Play) || receipt.RecipientSeat != recipientSeat ||
            recipientSeat != _currentSeat || recipientSeat != _turnProgression.OwnerSeat || receipt.ActualTurn != _turnNumber ||
            _winner != Winner.None || !_players[recipientSeat].IsAlive || receipt.SuspendedSchedule != _programPhaseSchedule ||
            receipt.ReturnPhase != _phase || receipt.ReturnActualPhase != CurrentActualDiscardRecoveryPhase() ||
            receipt.ReturnActualPhase is not { } actual || !IsRecordedActualDiscardRecoveryPhase(actual) || actual.ActorSeat != recipientSeat ||
            actual.Kind != (phase == TurnPhase.Draw ? ActualDiscardRecoveryPhaseKind.Draw : ActualDiscardRecoveryPhaseKind.Play) ||
            _resolutionStack.Count < 2 || _resolutionStack[^1] != frame ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent || parent.Id != receipt.ParentWindowId ||
            frame.WindowContext?.ParentFrameId != parent.Id || parent.OwnerSeat != recipientSeat ||
            parent.Window != (phase == TurnPhase.Draw ? SkillProgramTriggerWindow.DrawPhaseEnded : SkillProgramTriggerWindow.PlayEnding) ||
            !ProgramEventHistory<ZhangShiPingHongjiConsumedEvent>().Any(e => e.FrameId == frame.Id && e.PromiseFrameId == promise.FrameId &&
                e.Source == receipt.Source && e.ActualTurn == _turnNumber && e.RecipientSeat == recipientSeat && e.Phase == phase && e.ParentWindowId == parent.Id)) return false;
        var drawParents = _resolutionStack.Take(_resolutionStack.Count - 2).ToArray();
        if (drawParents.Length > 0 && (phase != TurnPhase.Draw ||
            drawParents.Any(parentFrame => parentFrame is not DrawPhaseObligationFrame) || !HasDrawPhaseObligationBoundaryFrame() ||
            parent.ResumeDrawPhaseObligationFrameId != drawParents[^1].Id)) return false;
        receipt = receipt with { DrawPhaseParents = drawParents.Cast<DrawPhaseObligationFrame>().ToArray() };
        ReplaceRuntimeTop(frame = frame with { ZhangShiPingHongji = receipt });
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        PopResolutionFrame(parent.Id, ResolutionFrameKind.ProgramLifecycleTriggerWindow);
        foreach (var drawParent in receipt.DrawPhaseParents.Reverse())
            PopResolutionFrame(drawParent.Id, ResolutionFrameKind.DrawPhaseObligation);
        _programPhaseSchedule = new(frame, parent, phase, SkillProgramPhaseContinuation.BeforeNormalPreparation)
        {
            DiscardRecoveryReturnPhase = receipt.ReturnActualPhase,
            AfterTurnEndedReturn = receipt.SuspendedSchedule?.AfterTurnEndedReturn
        };
        AdvanceEventRulesAndQueueFact(new ProgramPhaseScheduledEvent(frame.Id, frame.SkillId, frame.TriggerId!, frame.OwnerSeat, phase, Started: true));
        if (phase == TurnPhase.Play) EnterPlayPhase(_players[recipientSeat]);
        else
        {
            _phase = TurnPhase.Draw;
            StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Draw, recipientSeat);
            if (!TryBeginDrawPhaseProgramWindow(_players[recipientSeat], false))
            {
                ConsumeDuFuRenPhase(ActualDiscardRecoveryPhaseKind.Draw, recipientSeat);
                ConsumeChengGongYingDraw(recipientSeat);
                DrawCards(_players[recipientSeat], FreezeAndCaptureCaoXiNativeDrawBase(_players[recipientSeat]), true);
                CompleteTurnStartAfterDraw(_players[recipientSeat], DelayedTurnEffects.None);
            }
        }
        return true;
    }

    private bool TryResumeZhangShiPingHongjiPhase()
    {
        if (_programPhaseSchedule is not { Frame.ZhangShiPingHongji: { Stage: HongjiStage.ExtraPhase } receipt } schedule) return false;
        var frame = schedule.Frame;
        if (schedule.ParentFrame is not { } parent || receipt.ParentWindowId != parent.Id ||
            receipt.ActualTurn != _turnNumber || receipt.RecipientSeat != _currentSeat || receipt.RecipientSeat != _turnProgression.OwnerSeat ||
            schedule.Phase != receipt.Phase || _resolutionStack.Count != 0 || _pendingDecision is not null ||
            receipt.ReturnActualPhase is not { } original || !IsRecordedActualDiscardRecoveryPhase(original) ||
            original.TurnNumber != _turnNumber || original.ActualTurnOwnerSeat != _currentSeat || original.ActorSeat != _currentSeat)
            throw new InvalidOperationException("Hongji lost its exact completed inserted phase and native parent return.");
        _programPhaseSchedule = receipt.SuspendedSchedule;
        AdvanceEventRulesAndQueueFact(new ProgramPhaseScheduledEvent(frame.Id, frame.SkillId, frame.TriggerId!, frame.OwnerSeat, schedule.Phase, Started: false));
        if (receipt.DrawPhaseParents.Count > 0)
        {
            if (receipt.Phase != TurnPhase.Draw || parent.ResumeDrawPhaseObligationFrameId != receipt.DrawPhaseParents[^1].Id)
                throw new InvalidOperationException("Hongji lost its frozen actual draw producer chain.");
            _phase = receipt.ReturnPhase;
            if (_winner == Winner.None && _players[receipt.RecipientSeat].IsAlive)
                AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseRestoredEvent(frame.Id, original));
            else ClearActualDiscardRecoveryPhase();
            foreach (var drawParent in receipt.DrawPhaseParents) PushRuntimeFrame(drawParent);
            PushRuntimeFrame(parent);
            PushRuntimeFrame(frame);
            ResumeZhangShiPingHongjiPhase(frame.Id);
            return true;
        }
        if (_winner != Winner.None || !_players[receipt.RecipientSeat].IsAlive)
        {
            ClearActualDiscardRecoveryPhase();
            CompleteDetachedProgramBinding(frame, completed: false);
            if (_winner == Winner.None && _programPhaseSchedule?.AfterTurnEndedReturn is not null)
            {
                _phase = receipt.ReturnPhase;
                ResumeScheduledProgramPhase();
                return true;
            }
            CancelScheduledProgramPhase();
            if (_winner == Winner.None) FinalizeEndTurn(_players[receipt.RecipientSeat]);
            return true;
        }
        _phase = receipt.ReturnPhase;
        AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryPhaseRestoredEvent(frame.Id, original));
        PushRuntimeFrame(parent);
        PushRuntimeFrame(frame);
        ResumeZhangShiPingHongjiPhase(frame.Id);
        return true;
    }

    private bool TryCancelZhangShiPingHongjiPhase()
    {
        if (_programPhaseSchedule is not { Frame.ZhangShiPingHongji: { Stage: HongjiStage.ExtraPhase } receipt } schedule) return false;
        _programPhaseSchedule = receipt.SuspendedSchedule;
        ClearActualDiscardRecoveryPhase();
        AdvanceEventRulesAndQueueFact(new ProgramPhaseScheduledEvent(schedule.Frame.Id, schedule.Frame.SkillId,
            schedule.Frame.TriggerId!, schedule.Frame.OwnerSeat, schedule.Phase, Started: false));
        CompleteDetachedProgramBinding(schedule.Frame, completed: false);
        CancelScheduledProgramPhase();
        return true;
    }

    private bool CanPlaceZhangShiPingMount(int owner, int target, Card card, CardLocation from) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeXingGuMountPile) &&
        IsValidPlayerSeat(owner) && IsValidPlayerSeat(target) && owner != target && _players[owner].IsAlive && _players[target].IsAlive &&
        from.Zone == CardZoneKind.PublicPersistentPile && from.OwnerSeat == owner && _cardZones.GetLocation(card.Id) == from &&
        PublicPileSources(owner).Any(source => source.SkillId == "ol:xinggu" && source.Location == from) && IsXingGuMount(card) &&
        !_players[target].EquipmentAreaAbolished && _players[target].EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot) > 0;

    private void PlaceZhangShiPingMount(ProgramSkillFrame frame, int target, Card card, CardLocation from)
    {
        if (GetActiveProgramFrame(frame.Id) != frame || frame.ZhangShiPingXingGu is not { Stage: XingGuStage.PlacementChildren } receipt ||
            receipt.CardId != card.Id || receipt.RecipientSeat != target || receipt.PileLocation != from ||
            !CanPlaceZhangShiPingMount(frame.OwnerSeat, target, card, from))
            throw new InvalidOperationException("Xinggu lost its exact public mount, native equipment slot or accepted placement.");
        var slot = EquipmentCatalog.Get(card.Kind).Slot;
        var equipped = GetEquipment(_players[target]).Where(item => EquipmentCatalog.Get(item.Kind).Slot == slot).ToArray();
        var replaced = equipped.Length >= _players[target].EquipmentSlotCapacity(slot) ? equipped[0] : null;
        if (replaced is not null)
        {
            CopyFirstReplacedWeapon(card, replaced);
            MoveCard(replaced, CardLocation.Equipment(target), CardLocation.DiscardPile, CardMoveReasons.EquipmentReplace);
        }
        MoveCard(card, from, CardLocation.Equipment(target), CardMoveReasons.EquipmentEnter);
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(frame.Id, target, slot, card.Id, card.Kind, replaced?.Id));
        AddLog("EquipmentChanged", $"{_players[frame.OwnerSeat].Name} 将【{card.DisplayName}】置入 {_players[target].Name} 的装备区。", target);
    }
}
