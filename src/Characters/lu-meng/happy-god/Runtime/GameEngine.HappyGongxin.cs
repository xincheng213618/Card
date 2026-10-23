namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource HappyGongxinSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class HappyGongxinCapability(GameEngine e) : IHappyGongxinHost
    { public void Bind(ProgramSkillFrame f) => e.BindHappyGongxin(f); }

    // Collection can precede the trigger-window push. The original native Use already exists;
    // the stronger immediate typed parent/candidate proof is required when the binding executes.
    private bool TryHappyGongxinCounterpart(int owner, string binding, ProgramSkillWindowContext context,
        out CardUseFrame use, out int target, out int other)
    {
        use = null!; target = other = -1;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            !IsValidPlayerSeat(owner) ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } action } actual ||
            action.ActionId != identity.CardActionId || action.ActorSeat != identity.ActorSeat || action.ActorSeat != actual.SourceSeat ||
            action.EffectiveKind != actual.CardKind || action.EffectiveDesignatedTargetSeats is not [var sole] ||
            !actual.TargetSeats.Contains(sole) || !IsValidPlayerSeat(sole)) return false;
        var counterpart = binding switch
        {
            "sole-outgoing" when action.ActorSeat == owner => sole,
            "sole-incoming" when sole == owner && action.ActorSeat != owner => action.ActorSeat,
            _ => -1
        };
        if (!IsValidPlayerSeat(counterpart)) return false;
        use = actual; target = sole; other = counterpart; return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 41600, SkillProgramEffectOp.BindHappyShenLuMengGongxinTarget)]
    private bool HappyGongxinEligible(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        TryHappyGongxinCounterpart(c.OwnerSeat, c.BindingId, context, out _, out _, out var other) &&
        _players[c.OwnerSeat].IsAlive && _players[other].IsAlive && GetHand(_players[other]).Count > 0;

    private void BindHappyGongxin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.HappyGongxin is not null ||
            f.TriggerId is not { } binding || f.WindowContext is not { } context ||
            !TryHappyGongxinCounterpart(f.OwnerSeat, binding, context, out var use, out var target, out var other) ||
            !_players[f.OwnerSeat].IsAlive || !_players[other].IsAlive || GetHand(_players[other]).Count == 0 ||
            _resolutionStack.SingleOrDefault(p => p.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            window.ParentFrameId != use.Id || window.Action.ActionId != use.Action!.ActionId ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, binding, f.SkillInstanceId, f.GameplayHash, 0), context, window))
            throw new InvalidOperationException("Happy Gongxin requires its original sole native Use and current candidate.");
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1].Id != window.Id || _resolutionStack[index - 2].Id != use.Id ||
            _skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, "named-group:" + HappyGongxinComposition.Group, SkillUsageScope.Turn) != 1)
            throw new InvalidOperationException("Happy Gongxin lost its immediate native parent or shared actual-turn payment.");
        var witness = new HappyGongxinWitness(HappyGongxinSource(f), f.GameplayHash, window.Id, use.Id, use.Action.ActionId,
            _turnNumber, use.Action.ActorSeat, target, other);
        ReplaceRuntimeTop(f with { SelectedTargetSeats = Array.AsReadOnly(new[] { other }), HappyGongxin = witness });
        AdvanceEventRulesAndQueueFact(new HappyGongxinTargetBoundEvent(f.Id, witness));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 41600)]
    private void RequireHappyGongxinWitness(ProgramSkillFrame f)
    {
        if (f.HappyGongxin is null)
        {
            if (f.SkillId == HappyGongxinComposition.Skill && f.InstructionIndex > 1)
                throw new InvalidOperationException("Happy Gongxin lost its original native target witness.");
            return;
        }
        if (!HasExactHappyGongxinParticipantSelection(f))
            throw new InvalidOperationException("Happy Gongxin lost its original sole Use, source instance, actual turn or bound hand owner.");
    }
    private bool HasExactHappyGongxinParticipantSelection(ProgramSkillFrame f)
    {
        if (f.HappyGongxin is not { } r) return false;
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (f.SkillId != HappyGongxinComposition.Skill || f.TriggerId is not { } binding || f.WindowContext is not { } context ||
            f.InstructionIndex < 1 || r.Source != HappyGongxinSource(f) || r.GameplayHash != f.GameplayHash || r.ActualTurn != _turnNumber ||
            !TryHappyGongxinCounterpart(f.OwnerSeat, binding, context, out var use, out var target, out var other) ||
            r.UseId != use.Id || r.ActionId != use.Action!.ActionId || r.ActorSeat != use.Action.ActorSeat || r.TargetSeat != target || r.HandOwnerSeat != other ||
            r.WindowId != context.ParentFrameId || f.SelectedTargetSeats is not [var selected] || selected != other ||
            index < 2 || _resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window || window.Id != r.WindowId ||
            _resolutionStack[index - 2].Id != use.Id ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, binding, f.SkillInstanceId, f.GameplayHash, 0), context, window) ||
            ProgramEventHistory<HappyGongxinTargetBoundEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var issued] || issued.Witness != r)
            return false;
        return true;
    }
    private bool IsExactHappyGongxinSelfReveal(ProgramSkillFrame f) =>
        f.InstructionIndex == 2 && f.TriggerId == "sole-outgoing" &&
        f.HappyGongxin is { ActorSeat: var actor, TargetSeat: var target, HandOwnerSeat: var holder } &&
        actor == f.OwnerSeat && target == actor && holder == actor && HasExactHappyGongxinParticipantSelection(f);
}
