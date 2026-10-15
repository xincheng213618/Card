namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardUseFrame NormalizeImplicitDrawTwoUseTarget(CardUseFrame use)
    {
        if (use.CardKind != CardKind.DrawTwo || use.TargetSeats.Count != 0 || use.Action is not { } old ||
            !HasCaoChunCapability && !HasNeifaCapability && !HasSuFeiCapability && !HasJianjiNativeUseCapability && !HasFanChouCapability && !HasGuoSiCapability &&
            !HasTargetedOrdinaryTrickAdjustment && !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ProtectOtherSoleCardTarget) &&
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget) &&
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiAppendLegalTargets)) return use;
        var targets = Array.AsReadOnly(new[] { use.SourceSeat });
        var action = CloneDesignatedExtraTargetAction(old, targets);
        use = use with { TargetSeats = targets, Action = action };
        UpdateProgramRoleCardUse(use, action); return use;
    }
    private void RefreshCurrentUseAddedTargetCandidates(CardUseFrame use, IReadOnlyList<int> addedMainTargets)
    {
        if (use.Action is not { Type: CardActionType.Use } action || addedMainTargets.Count == 0) return;
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w => w.ParentFrameId == use.Id &&
            GetCardActionWindow(w) == SkillProgramTriggerWindow.CardUseTargetsFinalized);
        if (window is null) throw new InvalidOperationException("A target addition lost its actual designation window.");
        var newCandidates = CollectSharedCardActionCandidates(action, SkillProgramTriggerWindow.CardUseTargetsFinalized, addedMainTargets,
            null, window.Continuation).Where(c => addedMainTargets.Contains(c.OwnerSeat) &&
            _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Single(t => t.Id == c.TriggerId).OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
            !window.Candidates.Any(old => old.OwnerSeat == c.OwnerSeat && old.SkillId == c.SkillId && old.TriggerId == c.TriggerId && old.SkillInstanceId == c.SkillInstanceId))
            .OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId, StringComparer.Ordinal).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal)
            .Select(c => c with { FrozenContext = c.FrozenContext! with { ParentFrameId = window.Id,
                CardUse = c.FrozenContext.CardUse! with { ParentCardUseFrameId = use.Id } } }).ToArray();
        if (newCandidates.Length > 0) ReplaceRuntimeFrame(window.Id, window with { Candidates = Array.AsReadOnly(window.Candidates.Concat(newCandidates).ToArray()) });
    }
}
