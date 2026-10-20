namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool MouPtVisited(long useId, int owner, int target) => ProgramEventHistory<MouPangTongTargetVisitedEvent>().Any(e => e.UseId == useId && e.OwnerSeat == owner && e.TargetSeat == target);
    // This is the existing native notification, after the actual Use has been
    // mutated. Freeze eligible sources now so later acquisition cannot claim an
    // older target, and never move ordinary initial candidates to a final tail.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 30501)]
    private void CaptureMouPtLateTarget(ProgramCardUseTargetAddedEvent e)
    {
        if (!HasMouPangTongCapability || LifecycleCardUse(e.CardUseFrameId) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } action } use ||
            !use.TargetSeats.Contains(e.TargetSeat) || !_players[action.ActorSeat].IsAlive || _winner != Winner.None) return;
        var sources = new List<(CardConversionSource Source, string Hash)>();
        if (MouPtUpgraded(action.ActorSeat))
            foreach (var binding in GetSkillBindingShard(_players[action.ActorSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseTargetsFinalized).Where(b => b.SkillId == MouPangTongComposition.Lianhuan && b.Trigger.Id == "discard-unchained-targets"))
                sources.Add((new(binding.SkillId, binding.Trigger.Id, action.ActorSeat, binding.SkillInstanceId), binding.Program.GameplayHash));
        foreach (var paid in ProgramEventHistory<MouPangTongLianhuanPaidEvent>().Where(p => p.UseId == use.Id && p.ActionId == action.ActionId && p.Source.OwnerSeat == action.ActorSeat))
            sources.Add((paid.Source with { BindingId = "discard-unchained-targets" }, paid.GameplayHash));
        foreach (var (source, hash) in sources.Distinct())
        {
            var fact = new MouPangTongLateTargetNotifiedEvent(use.Id, action.ActionId, e.TargetSeat, source, hash);
            if (!ProgramEventHistory<MouPangTongLateTargetNotifiedEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
        }
    }
    private bool MouPtLateNotification(ProgramSkillWindowContext c, CardUseFrame use, CardConversionSource source, string hash) =>
        c.MouPangTongLateTargetSeat is { } target && c.TargetSeat == target && use.Action is { } action && use.TargetSeats.Contains(target) &&
        !MouPtVisited(use.Id, source.OwnerSeat, target) && ProgramEventHistory<MouPangTongLateTargetNotifiedEvent>().Contains(new(use.Id, action.ActionId, target, source, hash));
    private ProgramCardTriggerWindowFrame AppendMouPtLateTargets(ProgramCardTriggerWindowFrame frame)
    {
        if (!HasMouPangTongCapability || GetCardActionWindow(frame) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            LifecycleCardUse(frame.ParentFrameId) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } } use || _winner != Winner.None) return frame;
        var action = ActorHandLimitTargetCollectionAction(use); var added = new List<ProgramCardTriggerCandidate>();
        foreach (var notice in ProgramEventHistory<MouPangTongLateTargetNotifiedEvent>().Where(e => e.UseId == use.Id && e.ActionId == action.ActionId && use.TargetSeats.Contains(e.TargetSeat)))
        {
            var source = notice.Source;
            if (!_players[source.OwnerSeat].IsAlive || MouPtVisited(use.Id, source.OwnerSeat, notice.TargetSeat) ||
                frame.Candidates.Any(c => c.OwnerSeat == source.OwnerSeat && c.SkillId == source.SkillId && c.TriggerId == source.BindingId && c.SkillInstanceId == source.SkillInstanceId && c.FrozenContext?.MouPangTongLateTargetSeat == notice.TargetSeat)) continue;
            // A not-yet-run original candidate will traverse the current native
            // target list itself, at its original priority.
            if (!ProgramEventHistory<MouPangTongTargetsStartedEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == source.OwnerSeat) &&
                frame.Candidates.Skip(frame.CandidateIndex).Any(c => c.OwnerSeat == source.OwnerSeat && c.SkillId == source.SkillId && c.TriggerId == source.BindingId && c.FrozenContext?.MouPangTongLateTargetSeat is null)) continue;
            if (!HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) && !MouPtPaidBySource(use, source, notice.GameplayHash)) continue;
            var context = CreateCardActionProgramContext(action, SkillProgramTriggerWindow.CardUseTargetsFinalized, frame.Id, source.OwnerSeat, notice.TargetSeat, CaptureProgramTriggerFacts(_players[source.OwnerSeat], action)) with { MouPangTongLateTargetSeat = notice.TargetSeat };
            added.Add(new(source.OwnerSeat, notice.TargetSeat, source.SkillId, source.BindingId, notice.GameplayHash, source.SkillInstanceId, 0, context));
        }
        if (added.Count > 0) ReplaceRuntimeTop(frame = frame with { Candidates = Array.AsReadOnly(frame.Candidates.Concat(added).ToArray()) });
        return frame;
    }
}
