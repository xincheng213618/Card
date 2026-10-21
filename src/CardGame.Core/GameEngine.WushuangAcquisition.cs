namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginWushuangAcquisition(long windowId, WushuangShopOffer offer)
    {
        if (offer.TacticId is not { } id || WushuangTactics.ById[id].SkillId is not { } skillId ||
            _contentRegistry.GetSkill(skillId).Program is not { } program ||
            program.Triggers.SingleOrDefault(t => t.Id == "acquired" && t.Window == SkillProgramTriggerWindow.SkillsChanged) is not { } trigger)
            return false;
        if (_resolutionStack.LastOrDefault() is not WushuangIntermissionFrame parent || parent.Id != windowId ||
            parent.Acquisition is not null || !_wushuangPlayers[parent.SeatCursor].Purchased.Any(o => o.Id == offer.Id))
            throw new InvalidOperationException("A tactic acquisition lost its paid shop window.");
        var instance = GetRuntimeSkillInstanceId(_players[parent.SeatCursor], skillId);
        var receipt = new WushuangAcquisitionReceipt(parent.SeatCursor, id, offer.Id, skillId, instance,
            program.GameplayHash, parent.Opening || !_players[parent.SeatCursor].IsHuman);
        ReplaceRuntimeTop(parent with { Acquisition = receipt });
        var candidate = new ProgramTriggerCandidate(receipt.Seat, skillId, trigger.Id, instance, program.GameplayHash, trigger.Priority);
        BeginProgramBinding(candidate, new(SkillProgramTriggerWindow.SkillsChanged, windowId, receipt.Seat,
            SourceSeat: receipt.Seat, TargetSeat: receipt.Seat));
        return true;
    }

    [ProgramPipelineHook(ProgramPipelinePhase.ReturnCompletedProgramBinding, 62000)]
    private bool ReturnWushuangAcquisitionBinding(ProgramSkillFrame frame, bool completed)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.SkillsChanged } context ||
            _resolutionStack.LastOrDefault() is not WushuangIntermissionFrame parent || parent.Acquisition is not { } receipt)
            return false;
        if (!IsWushuangMode || context.ParentFrameId != parent.Id || frame.TriggerId != "acquired" ||
            frame.OwnerSeat != receipt.Seat || parent.SeatCursor != receipt.Seat || frame.SkillId != receipt.SkillId ||
            frame.SkillInstanceId != receipt.SkillInstanceId || frame.GameplayHash != receipt.GameplayHash ||
            !_wushuangPlayers[receipt.Seat].Tactics.Contains(receipt.TacticId) ||
            !_wushuangPlayers[receipt.Seat].Purchased.Any(o => o.Id == receipt.OfferId && o.TacticId == receipt.TacticId))
            throw new InvalidOperationException("A tactic acquisition lost its exact paid parent, source or offer.");
        ReplaceRuntimeTop(parent = parent with { Acquisition = null });
        if (receipt.AdvanceSeat) AdvanceWushuangShopSeat(parent);
        else CompleteWushuangShopAction(parent.Id);
        return true;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 62000)]
    private bool AllowsWushuangAcquiredBinding(ProgramTriggerLegalityContext gate)
    {
        if (gate.Candidate.BindingId != "acquired" || !gate.Candidate.SkillId.StartsWith("wushuang:tactic-", StringComparison.Ordinal)) return true;
        return gate.Context.Window == SkillProgramTriggerWindow.SkillsChanged &&
            _resolutionStack.OfType<WushuangIntermissionFrame>().LastOrDefault() is { Acquisition: { } receipt } parent &&
            gate.Context.ParentFrameId == parent.Id && receipt.Seat == gate.Candidate.OwnerSeat &&
            receipt.SkillId == gate.Candidate.SkillId && receipt.SkillInstanceId == gate.Candidate.SkillInstanceId &&
            receipt.GameplayHash == gate.Candidate.GameplayHash;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 62001)]
    private void AssertWushuangAcquisitionOwner(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.TriggerId != "acquired" || !frame.SkillId.StartsWith("wushuang:tactic-", StringComparison.Ordinal)) return;
        var parent = _resolutionStack.OfType<WushuangIntermissionFrame>().SingleOrDefault(p => p.Id == frame.WindowContext?.ParentFrameId);
        if (!IsWushuangMode || parent?.Acquisition is not { } receipt || parent.SeatCursor != frame.OwnerSeat ||
            receipt.Seat != frame.OwnerSeat || receipt.SkillId != frame.SkillId || receipt.SkillInstanceId != frame.SkillInstanceId ||
            receipt.GameplayHash != frame.GameplayHash || !_wushuangPlayers[frame.OwnerSeat].Tactics.Contains(receipt.TacticId) ||
            !_wushuangPlayers[frame.OwnerSeat].Purchased.Any(o => o.Id == receipt.OfferId && o.TacticId == receipt.TacticId))
            throw new InvalidOperationException("The acquired tactic requires its exact committed purchase and owning shop frame.");
    }
}
