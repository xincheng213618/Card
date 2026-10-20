using System.Globalization;
using System.Numerics;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuZhangFeiXianlue = "wumiao:xianlue";
    private const string WuZhangFeiXianlueDrawReason = "skill-program.wu-zhang-fei.xianlue.draw";
    private bool HasWuZhangFeiXianlueCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWuZhangFeiXianlue);
    private static int WuZhangFeiRankCount(int mask) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhang_fei_wumiao_WuZhangFeiXianlue.WuZhangFeiRankCount(mask);
    private WuZhangFeiRankCycleChangedEvent WuZhangFeiCycle(int owner) =>
        ProgramEventHistory<WuZhangFeiRankCycleChangedEvent>().LastOrDefault(e => e.OwnerSeat == owner) ?? new(owner, 1, 0, 13, 0, "initial", 0);
    private bool CanObserveWuZhangFeiHand(int owner, int target) => HasWuZhangFeiXianlueCapability &&
        IsValidPlayerSeat(owner) && IsValidPlayerSeat(target) && target != owner && _players[owner].IsAlive && _players[target].IsAlive &&
        _phase == TurnPhase.Play && _currentSeat == owner && _cardUseDebitPhaseInstanceId > 0 &&
        !ProgramEventHistory<WuZhangFeiXianlueStartedEvent>().Any(e => e.Source.OwnerSeat == owner && e.TargetSeat == target &&
            e.ActualTurn == _turnNumber && e.PhaseInstance == _cardUseDebitPhaseInstanceId) &&
        EventsSinceLastBoundary(e => e is TurnStartedEvent).Any(e =>
            e is DamageAppliedEvent { Amount: > 0 } damage && damage.TargetSeat == target ||
            e is CardMovedEvent move && move.From.OwnerSeat == target && move.From != move.To);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 29061, SkillProgramEffectOp.ResolveWuZhangFeiXianlue)]
    private bool CanStartWuZhangFeiXianlue(ProgramActivationLegalityContext gate) =>
        gate.SkillId == WuZhangFeiXianlue && gate.Activation.Id == "observe-ranks" &&
        _players.Any(target => CanObserveWuZhangFeiHand(gate.OwnerSeat, target.Seat));
    private SkillProgramStepOutcome BeginWuZhangFeiXianlue(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.SkillId != WuZhangFeiXianlue || f.TriggerId is not null || f.ActivationId != "observe-ranks" || f.InstructionIndex != 1 ||
            f.WindowContext is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var target] ||
            !CanObserveWuZhangFeiHand(f.OwnerSeat, target) || f.WuZhangFeiXianlue is not null ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1)
            throw new InvalidOperationException("Xianlue requires one currently eligible other Hand and its unused actual Play target.");
        var cycle = WuZhangFeiCycle(f.OwnerSeat);
        var hand = GetHand(_players[target]).Select(card => new WuZhangFeiXianlueObservedEntity(card.Id, card.Kind,
            EffectiveOwnedCardRank(_players[target], card))).ToArray();
        var observed = hand.Where(card => card.Rank is >= 1 and <= 13).Aggregate(0, (mask, card) => mask | 1 << (card.Rank - 1));
        var afterMask = cycle.RankMask | observed;
        var count = WuZhangFeiRankCount(afterMask); var beforeCount = WuZhangFeiRankCount(cycle.RankMask); var earned = 0;
        for (var gate = 0; gate < 3; gate++)
            if ((cycle.PaidGates & 1 << gate) == 0 && beforeCount <= 3 * (gate + 1) && count > 3 * (gate + 1)) earned |= 1 << gate;
        var receipt = new WuZhangFeiXianlueReceipt(WuZhangFeiSource(f), f.GameplayHash, target, _turnNumber,
            _cardUseDebitPhaseInstanceId, cycle.CycleId, cycle.RankMask, observed, earned, WuZhangFeiXianlueStage.Observing) { Hand = hand };
        ReplaceRuntimeTop(f = f with { WuZhangFeiXianlue = receipt });
        AdvanceEventRulesAndQueueFact(new WuZhangFeiXianlueStartedEvent(f.Id, receipt.Source, f.GameplayHash,
            target, _turnNumber, receipt.PhaseInstance, cycle.CycleId, cycle.RankMask, hand.Length));
        // Claim each gate before any draw child. Completion itself follows the
        // awarded draws, unless a real nested Dying independently clears it.
        AdvanceEventRulesAndQueueFact(cycle with { RankMask = afterMask, PaidGates = cycle.PaidGates | earned, Reason = "observe", ParentId = f.Id });
        AdvanceEventRulesAndQueueFact(new WuZhangFeiXianlueObservedEvent(f.Id, f.OwnerSeat, target, cycle.CycleId,
            cycle.RankMask, observed & ~cycle.RankMask, earned));
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = WuZhangFeiObservedChoices(f);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            $"显略：已观看 { _players[target].Name } 的手牌，当前记录 {count}/{cycle.CompletionTarget} 种点数。", [], [target], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description), Choices = choices };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> WuZhangFeiObservedChoices(ProgramSkillFrame f) =>
        [new(new($"wu-zhang-fei.observed.{f.Id}"), "已观看，继续结算。", [], [f.WuZhangFeiXianlue!.TargetSeat],
            new Dictionary<string, string> { ["program-action"] = "wu-zhang-fei-observed", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })];
    [ProgramAnswer("wu-zhang-fei-observed")]
    private void AnswerWuZhangFeiObserved(PromptChoice choice)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); AssertWuZhangFeiOperation(f);
        if (f.WuZhangFeiXianlue is not { Stage: WuZhangFeiXianlueStage.Observing } r ||
            !AssistedChoicesEqual([choice], WuZhangFeiObservedChoices(f)))
            throw new InvalidOperationException("Xianlue observation must return through its original private acknowledgement.");
        ClearPendingDecision(); ReplaceRuntimeTop(f with { WuZhangFeiXianlue = r with { Stage = WuZhangFeiXianlueStage.Drawing } });
        ContinueWuZhangFeiXianlue(f.Id);
    }
    private void ContinueWuZhangFeiXianlue(long id)
    {
        var f = GetActiveProgramFrame(id); AssertWuZhangFeiOperation(f); var r = f.WuZhangFeiXianlue!;
        if (r.Stage == WuZhangFeiXianlueStage.Observing) return;
        if (r.Stage == WuZhangFeiXianlueStage.DrawChildren)
        {
            if (AwaitWuZhangFeiChildren(f.Id)) return;
            f = GetActiveProgramFrame(id); r = f.WuZhangFeiXianlue!;
            ReplaceRuntimeTop(f = f with { WuZhangFeiXianlue = r with { Stage = WuZhangFeiXianlueStage.Drawing,
                ReturnedGates = r.ReturnedGates | 1 << r.CurrentGate!.Value, CurrentGate = null, DrawOutcome = null } });
            r = f.WuZhangFeiXianlue!;
        }
        var next = Enumerable.Range(0, 3).Where(gate => (r.EarnedGates & 1 << gate) != 0 && (r.ReturnedGates & 1 << gate) == 0).Select(gate => (int?)gate).FirstOrDefault();
        if (next is { } gate && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        {
            if (r.CurrentGate is null)
            {
                ReplaceRuntimeTop(f = f with { WuZhangFeiXianlue = r with { CurrentGate = gate } });
                AdvanceEventRulesAndQueueFact(new WuZhangFeiXianlueDrawIssuedEvent(f.Id, r.OriginCycleId, gate));
            }
            if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, 3, new(WuZhangFeiXianlueDrawReason));
            return;
        }
        var cycle = WuZhangFeiCycle(f.OwnerSeat);
        if (cycle.CycleId == r.OriginCycleId && WuZhangFeiRankCount(cycle.RankMask) >= cycle.CompletionTarget)
            CompleteWuZhangFeiCycle(cycle, f.Id);
        AdvanceEventRulesAndQueueFact(new WuZhangFeiXianlueCompletedEvent(f.Id, r.OriginCycleId));
        FinishProgramSkill(f, true);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWuZhangFeiXianlue)]
    private void ReturnWuZhangFeiXianlueDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.WuZhangFeiXianlue is not { Stage: WuZhangFeiXianlueStage.Drawing, CurrentGate: { } gate } r ||
            outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 3 ||
            ProgramEventHistory<WuZhangFeiXianlueDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Gate == gate))
            throw new InvalidOperationException("Xianlue cannot repeat a claimed gate or substitute another native draw return.");
        ReplaceRuntimeTop(f = f with { WuZhangFeiXianlue = r with { Stage = WuZhangFeiXianlueStage.DrawChildren, DrawOutcome = outcome },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuZhangFeiXianlueDrawReturnedEvent(f.Id, r.OriginCycleId, gate,
            outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueWuZhangFeiXianlue(f.Id);
    }
    private void CompleteWuZhangFeiCycle(WuZhangFeiRankCycleChangedEvent cycle, long parent)
    {
        if (WuZhangFeiCycle(cycle.OwnerSeat) != cycle || WuZhangFeiRankCount(cycle.RankMask) < cycle.CompletionTarget)
            throw new InvalidOperationException("Xianlue may clear only the currently completed original cycle.");
        var before = _skillRuntimeState.GetUsage(cycle.OwnerSeat, "wumiao:haoxian", "collect-rank-three", SkillUsageScope.Game);
        _skillRuntimeState.ClearUsage(cycle.OwnerSeat, "wumiao:haoxian", "collect-rank-three", SkillUsageScope.Game);
        AdvanceEventRulesAndQueueFact(new WuZhangFeiHaoxianResetEvent(cycle.OwnerSeat, cycle.CycleId, parent, "collect-rank-three", before, 0));
        AdvanceEventRulesAndQueueFact(new WuZhangFeiRankCycleChangedEvent(cycle.OwnerSeat, checked(cycle.CycleId + 1), 0, 13, 0, "complete", parent));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29061)]
    private void ObserveWuZhangFeiDying(IGameEvent payload)
    {
        if (!HasWuZhangFeiXianlueCapability || payload is not PlayerDyingEvent fact || fact.KillerSeat is not { } owner ||
            fact.VictimSeat == owner || !_players[owner].IsAlive || !HasRuntimeSkill(_players[owner], WuZhangFeiXianlue) ||
            _resolutionStack.LastOrDefault() is not DyingFrame { Continuation: DyingContinuationKind.Damage } dying || dying.Id != fact.ResolutionId ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(candidate => candidate.Id == dying.ParentFrameId) is not { Amount: > 0 } damage ||
            damage.SourceSeat != owner || damage.TargetSeat != fact.VictimSeat || CurrentDamageAttempt is not { IsSourceLess: false, DamageWasApplied: true } attack ||
            attack.SourceSeat != owner || attack.TargetSeat != fact.VictimSeat || attack.ResolutionId != damage.ParentFrameId ||
            ProgramEventHistory<WuZhangFeiDyingReducedEvent>().Any(e => e.DyingFrameId == dying.Id)) return;
        var cycle = WuZhangFeiCycle(owner); var after = Math.Max(1, cycle.CompletionTarget - 3);
        AdvanceEventRulesAndQueueFact(new WuZhangFeiDyingReducedEvent(dying.Id, damage.Id, owner, fact.VictimSeat,
            cycle.CycleId, cycle.CompletionTarget, after));
        var reduced = cycle with { CompletionTarget = after, Reason = "dying", ParentId = dying.Id };
        AdvanceEventRulesAndQueueFact(reduced);
        if (WuZhangFeiRankCount(reduced.RankMask) >= after) CompleteWuZhangFeiCycle(reduced, dying.Id);
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 100)]
    private CardSnapshot[] WuZhangFeiPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewer)
        .SelectMany(f => f.WuZhangFeiUse is { Stage: WuZhangFeiUseStage.Selecting } sample
            ? sample.Sample.Select(id => ToSnapshot(GetAdvancedCard(id)))
            : f.WuZhangFeiXianlue is { Stage: WuZhangFeiXianlueStage.Observing } observed
                ? observed.Hand.Select(card => ProjectHandCard(_players[observed.TargetSeat], GetAdvancedCard(card.CardId))) : [])
        .ToArray();
    private string? WuZhangFeiPublicCycleDescription(int owner, IReadOnlyList<GeneralSkillDefinition>? primary,
        IReadOnlyList<GeneralSkillDefinition>? secondary)
    {
        if (!HasWuZhangFeiXianlueCapability || !(primary ?? []).Concat(secondary ?? []).Any(skill => skill.ContentId == WuZhangFeiXianlue)) return null;
        var cycle = WuZhangFeiCycle(owner);
        return $"显略：第{cycle.CycleId}轮，已记录{WuZhangFeiRankCount(cycle.RankMask)}/{cycle.CompletionTarget}种点数。";
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuZhangFeiXianlue)]
    private PromptChoice SelectAiWuZhangFeiObserved(PendingDecision decision, ProgramSkillFrame frame) => decision.Choices.Single();
    [ProgramCapability]
    private sealed class WuZhangFeiXianlueCapability(GameEngine engine) : IWuZhangFeiXianlueHost, IWuZhangFeiXianlueSelection
    {
        public SkillProgramStepOutcome Observe(ProgramSkillFrame frame) => engine.BeginWuZhangFeiXianlue(frame);
        public bool CanObserve(int ownerSeat, int targetSeat) => engine.CanObserveWuZhangFeiHand(ownerSeat, targetSeat);
    }
}
