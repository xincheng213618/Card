namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouYuanShaoCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ReserveMouYuanShaoReward);
    private static bool IsMouYuanShaoTargetTrigger(string skill, SkillProgramTrigger trigger) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_yuan_shao_mou_mobile_MouYuanShao.IsMouYuanShaoTargetTrigger(skill,trigger);
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 30100)]
    private void CaptureMouYuanShaoTurn(TurnStartedEvent turn)
    {
        if (!HasMouYuanShaoCapability) return;
        AdvanceEventRulesAndQueueFact(new MouYuanShaoTurnStartedEvent(turn.TurnNumber, turn.ActorSeat,
            ProgramEventHistory<TurnStartedEvent>().Count(), _movementSequence));
    }
    private MouYuanShaoTurnStartedEvent MouYuanShaoTurn() => ProgramEventHistory<MouYuanShaoTurnStartedEvent>().LastOrDefault() ?? new(0, _currentSeat, 0, 0);
    private int MouYuanShaoActualRewards(int owner, string skill)
    {
        var start = MouYuanShaoTurn(); var reason = $"skill-program.{skill}.{SkillProgramEffectOp.Draw}";
        return _cardMovements.Count(m => m.Sequence > start.MovementBefore && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner) && m.Reason.Value == reason);
    }
    private bool CanMouYuanShaoReward(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c)
    {
        if (!_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None || c.CardUse is not { } action ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } w ||
            w.Action.ActionId != action.CardActionId || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count) return false;
        if (candidate.SkillId == MouYuanShaoComposition.Luanji)
        {
            if (c.Window != SkillProgramTriggerWindow.CardResponseCompleted || w.ResponseCompletion is not { CostsDrained: true, OriginalContinuation: ProgramCardContinuation.GroupResponse or ProgramCardContinuation.FactionDefenseDodge } ||
                w.Action.EffectiveKind != CardKind.Dodge || w.Action.ProviderSeat == candidate.OwnerSeat || w.Action.ParentActionId is not { } parentAction ||
                LifecycleCardUse(w.ParentFrameId) is not { Action: { Type: CardActionType.Use } parent } use || parent.ActionId != parentAction ||
                parent.EffectiveKind != CardKind.ArrowBarrage || parent.ActorSeat != candidate.OwnerSeat || use.SourceSeat != candidate.OwnerSeat ||
                MouYuanShaoActualRewards(candidate.OwnerSeat, candidate.SkillId) >= 3) return false;
            return !ProgramEventHistory<MouYuanShaoRewardReservedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId && e.ActionId == action.CardActionId);
        }
        if (candidate.SkillId != MouYuanShaoComposition.Xueyi || c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || c.TargetSeat is not { } target ||
            w.Action.Type != CardActionType.Use || w.Action.ActorSeat != candidate.OwnerSeat ||
            !HasSkillRoleQualification(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId, Role.Lord) ||
            MouYuanShaoActualRewards(candidate.OwnerSeat, candidate.SkillId) >= 2) return false;
        var source = new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId);
        return ProgramEventHistory<MouYuanShaoDesignationEvent>().Any(e => e.UseId == w.ParentFrameId && e.ActionId == action.CardActionId && e.TargetSeat == target &&
                e.Source == source && e.GameplayHash == candidate.GameplayHash && e.OtherQun) &&
            !ProgramEventHistory<MouYuanShaoRewardReservedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId && e.ActionId == action.CardActionId && e.TargetSeat == target);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30100, SkillProgramEffectOp.ReserveMouYuanShaoReward)]
    private bool CanOfferMouYuanShaoReward(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) => CanMouYuanShaoReward(candidate, context);
    private SkillProgramStepOutcome ReserveMouYuanShaoReward(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { CardUse: { } action } c)
            throw new InvalidOperationException("Yuan Shao's reward requires its exact native card-action candidate.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority);
        if (!CanMouYuanShaoReward(candidate, c)) throw new InvalidOperationException("Yuan Shao's native response/designation reward is no longer current.");
        AdvanceEventRulesAndQueueFact(new MouYuanShaoRewardReservedEvent(f.Id, new(f.SkillId, f.TriggerId, f.OwnerSeat, f.SkillInstanceId), c.ParentFrameId,
            action.ParentCardUseFrameId, action.CardActionId, c.TargetSeat ?? -1, MouYuanShaoTurn()));
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramCapability] private sealed class MouYuanShaoCapability(GameEngine engine) : IMouYuanShaoHost
    { public SkillProgramStepOutcome Reserve(ProgramSkillFrame frame) => engine.ReserveMouYuanShaoReward(frame); }

    // Every native designation is observed once, even with no current source:
    // gaining Xueyi inside an older target's window does not replay that target.
    private void CaptureMouYuanShaoDesignation(CardActionContext action, SkillProgramTriggerWindow window)
    {
        if (!HasMouYuanShaoCapability || window != SkillProgramTriggerWindow.CardUseTargetsFinalized || action.Type != CardActionType.Use ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId) is not { } use) return;
        var targets = action.EffectiveKind == CardKind.BorrowedSword ? action.TargetSeats.Where((_, i) => i % 2 == 0) : action.EffectiveDesignatedTargetSeats;
        foreach (var target in targets.Distinct().Where(t => IsValidPlayerSeat(t) && _players[t].IsAlive))
        {
            if (ProgramEventHistory<MouYuanShaoDesignationEvent>().Any(e => e.UseId == use.Id && e.TargetSeat == target)) continue;
            var bindings = GetSkillBindingShard(_players[action.ActorSeat]).GetInstanceTriggers(window).Where(b =>
                IsMouYuanShaoTargetTrigger(b.SkillId, b.Trigger) && HasSkillRoleQualification(_players[action.ActorSeat], b.SkillId, b.SkillInstanceId, Role.Lord)).ToArray();
            var otherQun = target != action.ActorSeat && GetEffectiveFactionId(_players[target]) == "qun";
            if (bindings.Length == 0) AdvanceEventRulesAndQueueFact(new MouYuanShaoDesignationEvent(use.Id, action.ActionId, target, null, null, otherQun));
            foreach (var binding in bindings) AdvanceEventRulesAndQueueFact(new MouYuanShaoDesignationEvent(use.Id, action.ActionId, target,
                new(binding.SkillId, binding.Trigger.Id, action.ActorSeat, binding.SkillInstanceId), binding.Program.GameplayHash, otherQun));
        }
    }
    private ProgramCardTriggerWindowFrame AppendMouYuanShaoTargets(ProgramCardTriggerWindowFrame frame)
    {
        if (!HasMouYuanShaoCapability || GetCardActionWindow(frame) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            LifecycleCardUse(frame.ParentFrameId) is not { Action: { Type: CardActionType.Use } } use || _winner != Winner.None ||
            MouYuanShaoActualRewards(use.SourceSeat, MouYuanShaoComposition.Xueyi) >= 2) return frame;
        var action = ActorHandLimitTargetCollectionAction(use);
        CaptureMouYuanShaoDesignation(action, SkillProgramTriggerWindow.CardUseTargetsFinalized);
        var claims = ProgramEventHistory<MouYuanShaoDesignationEvent>().Where(e => e.UseId == use.Id && e.Source is not null && e.OtherQun).ToArray();
        var added = CollectSharedCardActionCandidates(action, SkillProgramTriggerWindow.CardUseTargetsFinalized, use.TargetSeats, null, frame.Continuation)
            .Where(c => c.SkillId == MouYuanShaoComposition.Xueyi && claims.Any(e => e.TargetSeat == c.OpponentSeat && e.Source!.SkillInstanceId == c.SkillInstanceId) &&
                !frame.Candidates.Any(old => old.SkillId == c.SkillId && old.OwnerSeat == c.OwnerSeat && old.OpponentSeat == c.OpponentSeat && old.SkillInstanceId == c.SkillInstanceId) &&
                !ProgramEventHistory<MouYuanShaoRewardReservedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId && e.ActionId == action.ActionId && e.TargetSeat == c.OpponentSeat))
            .Select(c => c with { FrozenContext = c.FrozenContext! with { ParentFrameId = frame.Id, CardUse = c.FrozenContext.CardUse! with { ParentCardUseFrameId = use.Id } } }).ToArray();
        if (added.Length != 0) ReplaceRuntimeTop(frame = frame with { Candidates = Array.AsReadOnly(frame.Candidates.Concat(added).ToArray()) });
        return frame;
    }
    private bool TryBeginMouYuanShaoLateSlashTarget(CardAttackHandle attack)
    {
        if (!HasMouYuanShaoCapability || _winner != Winner.None || LifecycleCardUse(attack.ResolutionId) is not { ProgramUseAccepted: true, Action: { Type: CardActionType.Use } } use ||
            _resolutionStack.LastOrDefault()?.Id != use.Id || !IsSlashCard(use.CardKind) ||
            MouYuanShaoActualRewards(use.SourceSeat, MouYuanShaoComposition.Xueyi) >= 2) return false;
        var action = ActorHandLimitTargetCollectionAction(use);
        CaptureMouYuanShaoDesignation(action, SkillProgramTriggerWindow.CardUseTargetsFinalized);
        var claims = ProgramEventHistory<MouYuanShaoDesignationEvent>().Where(e => e.UseId == use.Id && e.Source is not null && e.OtherQun).ToArray();
        var windowId = _resolutionSequence + 1;
        var added = CollectSharedCardActionCandidates(action, SkillProgramTriggerWindow.CardUseTargetsFinalized, use.TargetSeats, null, ProgramCardContinuation.Slash)
            .Where(c => c.SkillId == MouYuanShaoComposition.Xueyi && claims.Any(e => e.TargetSeat == c.OpponentSeat && e.Source!.SkillInstanceId == c.SkillInstanceId) &&
                !ProgramEventHistory<MouYuanShaoRewardReservedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId && e.ActionId == action.ActionId && e.TargetSeat == c.OpponentSeat))
            .OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId, StringComparer.Ordinal)
            .Select(c => c with { FrozenContext = c.FrozenContext! with { ParentFrameId = windowId, CardUse = c.FrozenContext.CardUse! with { ParentCardUseFrameId = use.Id } } }).ToArray();
        if (added.Length == 0) return false;
        _resolutionSequence++;
        PushRuntimeFrame(new ProgramCardTriggerWindowFrame(windowId, use.Id, action, ProgramCardContinuation.Slash, Array.AsReadOnly(added), AttackOwnerFrameId: use.Id));
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }
}
