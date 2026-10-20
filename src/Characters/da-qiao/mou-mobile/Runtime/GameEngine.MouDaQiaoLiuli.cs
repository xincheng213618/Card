namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouDaQiaoLiuli => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart);
    private MouDaQiaoLiuliMarkGrantedEvent? LiveMouDaQiaoMark()
    {
        var last = ProgramEventHistory<MouDaQiaoLiuliMarkGrantedEvent>().LastOrDefault();
        return last is not null && !ProgramEventHistory<MouDaQiaoLiuliMarkConsumedEvent>().Any(e => e.GrantId == last.GrantId) ? last : null;
    }
    private bool HasUnissuedMouDaQiaoPhase(int owner) => HasMouDaQiaoLiuli && LiveMouDaQiaoMark() is { } grant && grant.RecipientSeat == owner &&
        _players[owner].IsAlive && !ProgramEventHistory<MouDaQiaoLiuliPhaseIssuedEvent>().Any(e => e.GrantId == grant.GrantId);
    private bool PaidMouDaQiaoLiuli(ProgramSkillFrame f) => f.SkillId == MouDaQiaoComposition.Liuli && f.TriggerId == "redirect-slash" &&
        f.WindowContext is { Window: SkillProgramTriggerWindow.SlashTargetRedirecting } && f.InstructionIndex is >= 2 and <= 5 &&
        f.CardSetBindings.SingleOrDefault(b => b.Name == "liuli-cost") is { CardIds: [var id], FrozenRevealedSuit: not null } &&
        ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ParentFrameId == f.Id && e.ExecutorSeat == f.OwnerSeat && e.SubjectSeat == f.OwnerSeat && e.ActualCount == 1 &&
            e.Reason.Value == "skill-program.mou-mobile:liuli.SelectAndMoveOwnedCard" && _cardMovements.Any(m => m.CardId == id && m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter && m.From.OwnerSeat == f.OwnerSeat && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment));
    private bool MouDaQiaoHeartAvailable(int owner, int actualTurn) => !ProgramEventHistory<MouDaQiaoLiuliMarkGrantedEvent>().Any(e => e.Source.OwnerSeat == owner && e.ActualTurn == actualTurn);
    private SkillProgramStepOutcome ResolveMouDaQiaoHeart(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!PaidMouDaQiaoLiuli(f)) throw new InvalidOperationException("Liuli's Heart clause requires its exact already-paid native HE discard and frozen suit.");
        if (f.InstructionIndex == 5)
        {
            if (f.MouDaQiaoLiuli is not { Choosing: false } chosen) throw new InvalidOperationException("Liuli lost its earlier exact reward decision before native redirect.");
            if (chosen.MarkTargetSeat is { } target && _players[f.OwnerSeat].IsAlive && _players[target].IsAlive && _winner == Winner.None && MouDaQiaoHeartAvailable(f.OwnerSeat, chosen.ActualTurn))
                GrantMouDaQiaoMark(f, chosen, target);
            ReplaceRuntimeTop(f with { MouDaQiaoLiuli = null }); return SkillProgramStepOutcome.Continue;
        }
        if (f.InstructionIndex != 3 || f.MouDaQiaoLiuli is not null || f.SelectedTargetSeats is not [var redirect] || f.WindowContext is not { } context)
            throw new InvalidOperationException("Liuli's reward choice belongs between its native payment and original redirect instruction.");
        var binding = GetProgramCardSet(f, "liuli-cost"); var actor = context.CardUse?.ActorSeat ?? context.SourceSeat ?? throw new InvalidOperationException("Liuli lost its actual Slash user.");
        var r = new MouDaQiaoLiuliReceipt(MouDaQiaoSource(f), f.GameplayHash, context.ParentFrameId, _turnNumber, actor, redirect,
            binding.CardIds.Single(), binding.FrozenRevealedSuit!.Value, Choosing: false);
        ReplaceRuntimeTop(f = f with { MouDaQiaoLiuli = r });
        AdvanceEventRulesAndQueueFact(new MouDaQiaoLiuliCostFrozenEvent(f.Id, r.Source, r.ActualTurn, actor, redirect, r.PaidCardId, r.PaidSuit));
        if (r.PaidSuit != Suit.Heart || !MouDaQiaoHeartAvailable(f.OwnerSeat, r.ActualTurn) || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        var choices = MouDaQiaoHeartChoices(f);
        if (choices.Count == 1) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { MouDaQiaoLiuli = r with { Choosing = true } });
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, "可选择一名其他角色获得全场唯一的流离标记。", actor);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> MouDaQiaoHeartChoices(ProgramSkillFrame f)
    {
        var r = f.MouDaQiaoLiuli!;
        var p = new Dictionary<string, string> { ["program-action"] = "mou-da-qiao-liuli", ["frame-id"] = f.Id.ToString() };
        var choices = _players.Where(t => t.IsAlive && t.Seat != f.OwnerSeat && t.Seat != r.OriginalActorSeat).Select(t =>
            new PromptChoice(new($"mou-da-qiao.liuli.{f.Id}.{t.Seat}"), "令其获得流离标记", [], [t.Seat], p)).ToList();
        choices.Add(new(new($"mou-da-qiao.liuli.{f.Id}.decline"), "不放置标记", [], [], p)); return Array.AsReadOnly(choices.ToArray());
    }
    [ProgramAnswer("mou-da-qiao-liuli")]
    private void AnswerMouDaQiaoHeart(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDaQiaoLiuli: { Choosing: true } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], MouDaQiaoHeartChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Liuli requires its current actual-owner reward choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f with { MouDaQiaoLiuli = r with { Choosing = false, MarkTargetSeat = choice.Targets.Count == 0 ? null : choice.Targets.Single() } });
        AdvanceRuntimeProgram(f.Id);
    }
    private void SetMouDaQiaoMarker(long frameId, int target, int count, int issuer)
    {
        var before = _players[target].Markers.GetValueOrDefault(PlayerMarkerKind.MouDaQiaoLiuli);
        if (before == count) return;
        if (count == 0) _players[target].Markers.Remove(PlayerMarkerKind.MouDaQiaoLiuli); else _players[target].Markers[PlayerMarkerKind.MouDaQiaoLiuli] = count;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frameId, target, PlayerMarkerKind.MouDaQiaoLiuli, count - before, count, issuer, "skill-program.mou-mobile:liuli.mark"));
    }
    private void GrantMouDaQiaoMark(ProgramSkillFrame f, MouDaQiaoLiuliReceipt r, int target)
    {
        var old = LiveMouDaQiaoMark();
        foreach (var p in _players.Where(p => p.Markers.GetValueOrDefault(PlayerMarkerKind.MouDaQiaoLiuli) != 0)) SetMouDaQiaoMarker(f.Id, p.Seat, 0, f.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new MouDaQiaoLiuliMarkGrantedEvent(f.Id, r.Source, r.GameplayHash, target, r.ActualTurn, old?.GrantId));
        SetMouDaQiaoMarker(f.Id, target, 1, f.OwnerSeat);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart)]
    private PromptChoice AiMouDaQiaoHeart(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Targets is [var target] && AreProgramDistributionAllies(_players[f.OwnerSeat], _players[target])) ?? p.Choices.Last();

    private IReadOnlyList<ProgramTriggerCandidate> IncludeMouDaQiaoPhaseCandidates(CharacterState owner, SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        if (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || !HasMouDaQiaoLiuli) return ordinary;
        var rest = ordinary.Where(c => c.SkillId != MouDaQiaoComposition.Liuli || c.BindingId != "marked-turn-start").ToArray();
        if (owner.Seat != _currentSeat || !HasUnissuedMouDaQiaoPhase(owner.Seat)) return Array.AsReadOnly(rest);
        var grant = LiveMouDaQiaoMark()!;
        return Array.AsReadOnly(rest.Append(new ProgramTriggerCandidate(owner.Seat, MouDaQiaoComposition.Liuli, "marked-turn-start", grant.Source.SkillInstanceId, grant.GameplayHash, 0)).ToArray());
    }
    private bool IsIssuedMouDaQiaoPhaseCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c) => candidate.SkillId == MouDaQiaoComposition.Liuli && candidate.BindingId == "marked-turn-start" &&
        c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && candidate.OwnerSeat == _currentSeat && HasUnissuedMouDaQiaoPhase(candidate.OwnerSeat) &&
        LiveMouDaQiaoMark() is { } grant && candidate.SkillInstanceId == grant.Source.SkillInstanceId && candidate.GameplayHash == grant.GameplayHash &&
        _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } parent &&
        parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == candidate;
    private SkillProgramStepOutcome BeginMouDaQiaoLiuliPhase(ProgramSkillFrame f)
    {
        if (f.MouDaQiaoLiuliPhase is not null) { FinishMouDaQiaoLiuliPhase(f); return SkillProgramStepOutcome.AwaitChild; }
        if (f.WindowContext is not { } c || !IsIssuedMouDaQiaoPhaseCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), c))
            throw new InvalidOperationException("Liuli's extra Play needs its exact issued recipient and native actual-turn-start cursor.");
        var grant = LiveMouDaQiaoMark()!; var r = new MouDaQiaoLiuliPhaseReceipt(grant.GrantId, grant.Source, grant.GameplayHash, f.OwnerSeat, _turnNumber, c.ParentFrameId);
        ReplaceRuntimeTop(f = f with { MouDaQiaoLiuliPhase = r });
        AdvanceEventRulesAndQueueFact(new MouDaQiaoLiuliPhaseIssuedEvent(f.Id, r.GrantId, f.OwnerSeat, _turnNumber, c.ParentFrameId));
        return ScheduleProgramPhase(f, TurnPhase.Play, SkillProgramPhaseContinuation.BeforeNormalPreparation);
    }
    private bool IsReturningMouDaQiaoLiuliPhase(ProgramSkillFrame f) => f.MouDaQiaoLiuliPhase is { } r &&
        ProgramEventHistory<ProgramPhaseScheduledEvent>().Any(e => e.FrameId == f.Id && !e.Started && e.Phase == TurnPhase.Play) &&
        ProgramEventHistory<MouDaQiaoLiuliPhaseIssuedEvent>().Count(e => e.FrameId == f.Id && e.GrantId == r.GrantId && e.RecipientSeat == f.OwnerSeat && e.ActualTurn == r.ActualTurn) == 1;
    private void FinishMouDaQiaoLiuliPhase(ProgramSkillFrame f)
    {
        if (!IsReturningMouDaQiaoLiuliPhase(f)) throw new InvalidOperationException("Liuli's issued Play can return only through its original native schedule.");
        var r = f.MouDaQiaoLiuliPhase!;
        if (LiveMouDaQiaoMark()?.GrantId == r.GrantId) SetMouDaQiaoMarker(f.Id, f.OwnerSeat, 0, r.Source.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new MouDaQiaoLiuliMarkConsumedEvent(f.Id, r.GrantId));
        AdvanceEventRulesAndQueueFact(new MouDaQiaoLiuliPhaseReturnedEvent(f.Id, r.GrantId, f.OwnerSeat, r.ActualTurn));
        ReplaceRuntimeTop(f = f with { MouDaQiaoLiuliPhase = null }); FinishProgramSkill(f, true);
    }
}
