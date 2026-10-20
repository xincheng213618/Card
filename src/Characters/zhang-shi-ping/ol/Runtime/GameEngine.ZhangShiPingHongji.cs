namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasHongjiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmHongjiNextPhase);
    private static CardConversionSource ZhangShiPingSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_shi_ping_ol_ZhangShiPingHongji.ZhangShiPingSource(f);
    [ProgramCapability]
    private sealed class ZhangShiPingCapability(GameEngine engine) : IZhangShiPingProgramHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ArmHongjiNextPhase => engine.BeginHongjiPromise(f),
            SkillProgramEffectOp.ResolveHongjiGrantedPhase => engine.BeginHongjiExtraPhase(f),
            _ => engine.BeginXingGu(f, op)
        };
    }
    private (bool Draw, bool Play) HongjiEligibility(int owner, int recipient, long? parentId)
    {
        if (!HasHongjiCapability || _roundNumber < 1 || !IsValidPlayerSeat(recipient) || !_players[recipient].IsAlive ||
            recipient != _currentSeat || !_players[owner].IsAlive) return (false, false);
        var previous = ProgramEventHistory<ZhangShiPingHongjiGrantedEvent>().Where(e => e.Source.OwnerSeat == owner && e.Source.SkillId == "ol:hongji").ToArray();
        if (parentId is { } parent && previous.Any(e => e.PreparationParentId == parent)) return (false, false);
        var window = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == parentId);
        var counts = window?.ParticipantFacts is { Count: > 0 } frozen
            ? frozen.ToDictionary(p => p.Key, p => p.Value.CurrentHandCount)
            : _players.Where(p => p.IsAlive).ToDictionary(p => p.Seat, p => GetHand(p).Count);
        if (!counts.TryGetValue(recipient, out var hand)) return (false, false);
        return (hand == counts.Values.Min() && !previous.Any(e => e.RoundNumber == _roundNumber && e.Phase == TurnPhase.Draw),
            hand == counts.Values.Max() && !previous.Any(e => e.RoundNumber == _roundNumber && e.Phase == TurnPhase.Play));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26610)]
    private bool CanOfferZhangShiPing(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects is [{ Op: SkillProgramEffectOp.ArmHongjiNextPhase }])
        {
            if (context.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || context.SourceSeat != _currentSeat) return false;
            var eligible = HongjiEligibility(c.OwnerSeat, _currentSeat, context.ParentFrameId > 0 ? context.ParentFrameId : null);
            return eligible.Draw || eligible.Play;
        }
        if (t.Effects is [{ Op: SkillProgramEffectOp.ResolveHongjiGrantedPhase }]) return HasIssuedHongjiCandidate(c, context);
        if (t.Effects is [{ Op: SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond }]) return XingGuPlacementChoices(c.OwnerSeat, c.SkillInstanceId).Count > 0;
        return true;
    }
    private bool IsHongjiPhaseCandidate(ProgramTriggerCandidate c) => c.SkillId == "ol:hongji" &&
        _contentRegistry.GetSkill(c.SkillId).Program?.Triggers.Any(t => t.Id == c.BindingId && t.Effects is [{ Op: SkillProgramEffectOp.ResolveHongjiGrantedPhase }]) == true;
    private IEnumerable<ZhangShiPingHongjiGrantedEvent> OutstandingHongjiPromises(int recipient, SkillProgramTriggerWindow window)
    {
        if (!HasHongjiCapability || !_players[recipient].IsAlive || recipient != _currentSeat || _winner != Winner.None) return [];
        var phase = window == SkillProgramTriggerWindow.DrawPhaseEnded ? TurnPhase.Draw : TurnPhase.Play;
        if (window is not (SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.PlayEnding) || _phase != phase) return [];
        var consumed = ProgramEventHistory<ZhangShiPingHongjiConsumedEvent>().Select(e => e.PromiseFrameId).ToHashSet();
        return ProgramEventHistory<ZhangShiPingHongjiGrantedEvent>().Where(e => e.ActualTurn == _turnNumber && e.RecipientSeat == recipient && e.Phase == phase &&
            !consumed.Contains(e.FrameId) && !HongjiPromiseReservedByPhaseParent(e));
    }
    // A native phase ending already froze all of its sibling trigger opportunities.
    // A first sibling's inserted phase cannot borrow a later sibling's promise.
    private bool HongjiPromiseReservedByPhaseParent(ZhangShiPingHongjiGrantedEvent promise)
    {
        for (var schedule = _programPhaseSchedule; schedule is { Frame.ZhangShiPingHongji: { Stage: HongjiStage.ExtraPhase } receipt }; schedule = receipt.SuspendedSchedule)
        {
            if (receipt.ActualTurn != promise.ActualTurn || receipt.RecipientSeat != promise.RecipientSeat || receipt.Phase != promise.Phase ||
                schedule.ParentFrame is not { } parent || parent.Id != receipt.ParentWindowId || parent.OwnerSeat != promise.RecipientSeat ||
                parent.Window != (promise.Phase == TurnPhase.Draw ? SkillProgramTriggerWindow.DrawPhaseEnded : SkillProgramTriggerWindow.PlayEnding)) continue;
            if (parent.Candidates.Skip(parent.CandidateIndex).Any(c => c.OwnerSeat == promise.Source.OwnerSeat && c.SkillId == promise.Source.SkillId &&
                c.SkillInstanceId == promise.Source.SkillInstanceId && c.GameplayHash == promise.GameplayHash && IsHongjiPhaseCandidate(c))) return true;
        }
        return false;
    }
    private bool HasOutstandingHongjiPhase(CharacterState recipient, SkillProgramTriggerWindow window) => OutstandingHongjiPromises(recipient.Seat, window).Any();
    private IReadOnlyList<ProgramTriggerCandidate> IncludeHongjiPhaseCandidates(CharacterState turnOwner, SkillProgramTriggerWindow window,
        IReadOnlyList<ProgramTriggerCandidate> candidates)
    {
        if (!HasHongjiCapability) return candidates;
        var result = candidates.ToList();
        foreach (var promise in OutstandingHongjiPromises(turnOwner.Seat, window))
        {
            var p = _contentRegistry.GetSkill(promise.Source.SkillId).Program!;
            if (p.GameplayHash != promise.GameplayHash) continue;
            var t = p.Triggers.Single(t => t.Window == window && t.Effects is [{ Op: SkillProgramEffectOp.ResolveHongjiGrantedPhase }]);
            var candidate = new ProgramTriggerCandidate(promise.Source.OwnerSeat, promise.Source.SkillId, t.Id, promise.Source.SkillInstanceId, promise.GameplayHash, t.Priority);
            if (!result.Any(c => c.OwnerSeat == candidate.OwnerSeat && c.SkillId == candidate.SkillId && c.BindingId == candidate.BindingId && c.SkillInstanceId == candidate.SkillInstanceId)) result.Add(candidate);
        }
        return Array.AsReadOnly(result.OrderBy(c => (c.OwnerSeat - turnOwner.Seat + _playerCount) % _playerCount).ThenByDescending(c => c.Priority)
            .ThenBy(c => c.SkillId, StringComparer.Ordinal).ThenBy(c => c.BindingId, StringComparer.Ordinal).ToArray());
    }
    private bool HasIssuedHongjiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsHongjiPhaseCandidate(c) || context.Window is not (SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.PlayEnding) ||
            context.SourceSeat != _currentSeat || context.TargetSeat != _currentSeat || context.OwnerSeat != c.OwnerSeat || _winner != Winner.None ||
            !_players[_currentSeat].IsAlive || _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.Window != context.Window || window.OwnerSeat != _currentSeat || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            window.Candidates[window.CandidateIndex] is not { } current || current.OwnerSeat != c.OwnerSeat || current.SkillId != c.SkillId ||
            current.BindingId != c.BindingId || current.SkillInstanceId != c.SkillInstanceId || current.GameplayHash != c.GameplayHash) return false;
        bool Matches(ZhangShiPingHongjiGrantedEvent p) => p.Source.OwnerSeat == c.OwnerSeat && p.Source.SkillId == c.SkillId &&
            p.Source.SkillInstanceId == c.SkillInstanceId && p.GameplayHash == c.GameplayHash && p.ActualTurn == _turnNumber && p.RecipientSeat == _currentSeat &&
            p.Phase == (context.Window == SkillProgramTriggerWindow.DrawPhaseEnded ? TurnPhase.Draw : TurnPhase.Play);
        return OutstandingHongjiPromises(_currentSeat, context.Window).Any(Matches) ||
            _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OwnerSeat == c.OwnerSeat && f.SkillId == c.SkillId && f.SkillInstanceId == c.SkillInstanceId &&
                f.WindowContext == context && f.ZhangShiPingHongji is { Promise: { } p } r && r.ParentWindowId == window.Id && Matches(p) &&
                ProgramEventHistory<ZhangShiPingHongjiConsumedEvent>().Any(e => e.PromiseFrameId == p.FrameId && e.FrameId == f.Id && e.ParentWindowId == window.Id));
    }
    private SkillProgramStepOutcome BeginHongjiPromise(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:hongji" || f.InstructionIndex != 1 || f.ZhangShiPingHongji is not null ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context || context.SourceSeat != _currentSeat)
            throw new InvalidOperationException("Hongji requires its exact actual preparation source and zero-input instruction.");
        var eligible = HongjiEligibility(f.OwnerSeat, _currentSeat, context.ParentFrameId);
        if (!eligible.Draw && !eligible.Play) throw new InvalidOperationException("Hongji no longer has an unused qualifying preparation branch.");
        ReplaceRuntimeTop(f = f with { ZhangShiPingHongji = new(ZhangShiPingSource(f), f.GameplayHash, f.InstructionIndex, context.ParentFrameId,
            _turnNumber, _currentSeat, HongjiStage.Choose, eligible.Draw, eligible.Play) });
        PublishHongji(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HongjiChoices(ProgramSkillFrame f)
    {
        var r = f.ZhangShiPingHongji!; var choices = new List<PromptChoice>();
        void Add(TurnPhase phase, string branch, string label) => choices.Add(new(new($"hongji.{f.Id}.{branch}"), label, [], [r.RecipientSeat],
            new Dictionary<string, string> { ["program-action"] = "zhang-shi-ping-hongji", ["branch"] = branch, ["phase"] = phase.ToString() }));
        if (r.DrawEligible) Add(TurnPhase.Draw, "draw", "下个实际摸牌阶段结束后执行额外摸牌阶段");
        if (r.PlayEligible) Add(TurnPhase.Play, "play", "下个实际出牌阶段结束后执行额外出牌阶段");
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishHongji(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = HongjiChoices(f);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "鸿济：选择本次准备阶段的一项效果。", [], [f.ZhangShiPingHongji!.RecipientSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("zhang-shi-ping-hongji")]
    private void AnswerHongji(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ZhangShiPingHongji: { Stage: HongjiStage.Choose } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], HongjiChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Hongji lost its exact frozen preparation choice.");
        AssertZhangShiPing(f); ClearPendingDecision();
        var phase = Enum.Parse<TurnPhase>(choice.Parameters["phase"]);
        AdvanceEventRulesAndQueueFact(new ZhangShiPingHongjiGrantedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, _roundNumber, r.RecipientSeat, phase, r.ParentWindowId));
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, ZhangShiPingHongji = null });
        FinishProgramSkill(f, completed: true);
    }
    private SkillProgramStepOutcome BeginHongjiExtraPhase(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ZhangShiPingHongji is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context ||
            !HasIssuedHongjiCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), context))
            throw new InvalidOperationException("Hongji requires its exact unconsumed issued actual-phase candidate.");
        var p = OutstandingHongjiPromises(_currentSeat, context.Window).Single(p => p.Source.OwnerSeat == f.OwnerSeat && p.Source.SkillId == f.SkillId && p.Source.SkillInstanceId == f.SkillInstanceId);
        ReplaceRuntimeTop(f = f with { ZhangShiPingHongji = new(p.Source, f.GameplayHash, f.InstructionIndex, context.ParentFrameId, _turnNumber,
            p.RecipientSeat, HongjiStage.ExtraPhase, Phase: p.Phase, ReturnPhase: _phase, ReturnActualPhase: CurrentActualDiscardRecoveryPhase(), Promise: p)
            { SuspendedSchedule = _programPhaseSchedule } });
        AdvanceEventRulesAndQueueFact(new ZhangShiPingHongjiConsumedEvent(p.FrameId, f.Id, p.Source, _turnNumber, p.RecipientSeat, p.Phase, context.ParentFrameId));
        if (!TryBeginZhangShiPingHongjiPhase(f, p.Phase, p.RecipientSeat)) throw new InvalidOperationException("Hongji could not suspend its exact native phase-ending parent.");
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void ResumeZhangShiPingHongjiPhase(long frameId)
    {
        var f = GetActiveProgramFrame(frameId);
        if (f.ZhangShiPingHongji is not { Stage: HongjiStage.ExtraPhase, Promise: { } p } r || r.ActualTurn != _turnNumber)
            throw new InvalidOperationException("Hongji phase return lost its exact consumed promise.");
        AdvanceEventRulesAndQueueFact(new ZhangShiPingHongjiReturnedEvent(p.FrameId, frameId, r.ActualTurn, r.RecipientSeat, r.Phase));
        ReplaceRuntimeTop(f = f with { ZhangShiPingHongji = r with { Stage = HongjiStage.Returned } });
        FinishProgramSkill(f with { ZhangShiPingHongji = null }, completed: true);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26610)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26610)]
    private bool ContinueIssuedZhangShiPing(ProgramSkillFrame f) => f.ZhangShiPingXingGu is not null || f.ZhangShiPingHongji is { Stage: HongjiStage.ExtraPhase or HongjiStage.Returned } ||
        f.ZhangShiPingHongji is null && f.TriggerId is { } binding && f.InstructionIndex is 0 or 1 && f.WindowContext is { } context &&
            HasIssuedHongjiCandidate(new(f.OwnerSeat, f.SkillId, binding, f.SkillInstanceId, f.GameplayHash, 0), context);
    [ProgramAiChoice(SkillProgramEffectOp.ArmHongjiNextPhase)]
    private PromptChoice AiHongji(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "play" && GetHand(_players[f.ZhangShiPingHongji!.RecipientSeat]).Count >= 4) ?? p.Choices.First();
}
