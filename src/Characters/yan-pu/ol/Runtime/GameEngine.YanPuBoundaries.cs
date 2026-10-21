namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource YanPuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool IsYanPuEndingCandidate(ProgramTriggerCandidate c) => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ResolveHuantuEnding }];
    private IReadOnlyList<ProgramTriggerCandidate> IncludeYanPuDrawObservers(CharacterState subject, IReadOnlyList<ProgramTriggerCandidate> existing)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveCardSkipDrawForEnding)) return existing;
        var extra = _players.Where(p => p.IsAlive && p.Seat != subject.Seat).SelectMany(p =>
            CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.DrawPhaseStarting)).Where(c =>
                GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.GiveCardSkipDrawForEnding }]);
        return Array.AsReadOnly(existing.Concat(extra).Distinct().OrderByDescending(c => c.Priority)
            .ThenBy(c => RelativeTableOrder(subject.Seat, c.OwnerSeat)).ThenBy(c => c.SkillId, StringComparer.Ordinal).ToArray());
    }
    private bool YanPuRoundUsed(int seat, string skill) => ProgramEventHistory<YanPuHuantuIssuedEvent>()
        .Any(e => e.Promise.Source.OwnerSeat == seat && e.Promise.Source.SkillId == skill && e.Promise.ActualRound == _roundNumber);
    private bool IsYanPuDrawContext(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.GiveCardSkipDrawForEnding }] &&
        context.Window == SkillProgramTriggerWindow.DrawPhaseStarting && context.SourceSeat == _currentSeat && context.TargetSeat == _currentSeat &&
        context.OwnerSeat == c.OwnerSeat && c.OwnerSeat != _currentSeat && _phase == TurnPhase.Draw && _roundNumber > 0 &&
        _players[c.OwnerSeat].IsAlive && _players[_currentSeat].IsAlive && !YanPuRoundUsed(c.OwnerSeat, c.SkillId) &&
        IsWithinAttackRange(c.OwnerSeat, _currentSeat) &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is
            { Window: SkillProgramTriggerWindow.DrawPhaseStarting, ActualPhaseSubstitution: null } parent && parent.OwnerSeat == _currentSeat &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == c &&
        (GetHand(_players[c.OwnerSeat]).Count > 0 || GetEquipment(_players[c.OwnerSeat]).Any(card =>
            !IsActiveProgramSourceEquipmentCard(c.OwnerSeat, c.SkillId, c.SkillInstanceId, card) &&
            !IsForeignEquipmentDiscardPrevented(c.OwnerSeat, card, CardLocation.Equipment(c.OwnerSeat), OwnedCardMoveIntent.Transfer)));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 26410)]
    private bool CollectYanPuCandidate(SkillProgramTrigger t) => t.Effects is not [{ Op: SkillProgramEffectOp.ResolveHuantuEnding }];
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26410)]
    private bool QualifyYanPuCandidate(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        t.Effects is [{ Op: SkillProgramEffectOp.GiveCardSkipDrawForEnding }] ? IsYanPuDrawContext(c, context) :
        t.Effects is [{ Op: SkillProgramEffectOp.DrawDyingExitRoundDistance }] ? IsYanPuDyingExitContext(c, context) : true;
    private bool ValidYanPuPromise(YanPuHuantuPromise p) => p.Id > 0 && p.Source.OwnerSeat != p.RecipientSeat &&
        p.ActualRound > 0 && p.ActualRound <= _roundNumber && p.ActualTurn > 0 && p.ActualTurnOwner == p.RecipientSeat &&
        p.From.OwnerSeat == p.Source.OwnerSeat && p.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash == p.GameplayHash &&
        ProgramEventHistory<YanPuHuantuIssuedEvent>().Count(e => e.Promise.Id == p.Id) == 1 &&
        ProgramEventHistory<YanPuHuantuIssuedEvent>().Any(e => e.Promise == p) &&
        ProgramEventHistory<YanPuStartedEvent>().Count(e => e.ProgramFrameId == p.Id && e.Source == p.Source && e.GameplayHash == p.GameplayHash &&
            e.WindowId == p.DrawWindowId && e.Round == p.ActualRound && e.Turn == p.ActualTurn && e.TurnOwner == p.ActualTurnOwner && e.Target == p.RecipientSeat && e.Stage == YanPuStage.GiftChoice) == 1 &&
        _cardMovements.Count(m => m.Sequence == p.MovementSequence && m.CardId == p.CardId && m.From == p.From &&
            m.To == CardLocation.Hand(p.RecipientSeat) && m.Reason.Value == YanPuGiftReason) == 1;
    private bool YanPuPromiseConsumed(long id) => ProgramEventHistory<YanPuHuantuConsumedEvent>().Any(e => e.PromiseId == id);
    private void AppendYanPuEndingItems(List<TurnEndingBoundaryItem> items)
    {
        foreach (var p in ProgramEventHistory<YanPuHuantuIssuedEvent>().Select(e => e.Promise).Where(p =>
            p.ActualTurn == _turnNumber && p.ActualTurnOwner == _currentSeat && !YanPuPromiseConsumed(p.Id)).ToArray())
        {
            if (!ValidYanPuPromise(p) || !_players[p.Source.OwnerSeat].IsAlive) continue;
            var t = _contentRegistry.GetSkill(p.Source.SkillId).Program!.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ResolveHuantuEnding }]);
            var c = new ProgramTriggerCandidate(p.Source.OwnerSeat, p.Source.SkillId, t.Id, p.Source.SkillInstanceId, p.GameplayHash, t.Priority, items.Count);
            items.Add(new(TurnEndingBoundaryItemKind.Program, t.Priority, $"yanpu-ending:{p.Id}", c, CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
        }
    }
    private YanPuHuantuPromise? CurrentYanPuEnding(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsYanPuEndingCandidate(c) || context.Window != SkillProgramTriggerWindow.TurnEnding || context.SourceSeat != _currentSeat || context.TargetSeat != _currentSeat ||
            context.OwnerSeat != c.OwnerSeat || context.OccurrenceIndex != c.OccurrenceIndex ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } w ||
            w.OwnerSeat != _currentSeat || w.TurnNumber != _turnNumber || w.ItemIndex < 0 || w.ItemIndex >= w.Items.Count || w.Items[w.ItemIndex].Candidate != c) return null;
        var p = ProgramEventHistory<YanPuHuantuIssuedEvent>().Select(e => e.Promise).SingleOrDefault(p => w.Items[w.ItemIndex].StableIdentity == $"yanpu-ending:{p.Id}");
        return p is not null && ValidYanPuPromise(p) && p.ActualTurn == _turnNumber && p.ActualTurnOwner == _currentSeat &&
            p.Source.OwnerSeat == c.OwnerSeat && p.Source.SkillId == c.SkillId && p.Source.SkillInstanceId == c.SkillInstanceId && p.GameplayHash == c.GameplayHash ? p : null;
    }
    private bool CanRunYanPuEnding(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        CurrentYanPuEnding(c, context) is { } p && !YanPuPromiseConsumed(p.Id) && _winner == Winner.None &&
        _players[c.OwnerSeat].IsAlive;
    private ProgramTriggerCandidate? YanPuCurrentEndingFrameCandidate(ProgramSkillFrame f) =>
        f.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } context &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } w &&
        w.ItemIndex >= 0 && w.ItemIndex < w.Items.Count && w.Items[w.ItemIndex].Candidate is { } c &&
        context.OccurrenceIndex == c.OccurrenceIndex && MountObserverCandidateMatches(f, c) ? c : null;
    private bool TryBeginYanPuDyingExit(DyingCompletionReceipt dying, bool survived)
    {
        if (!survived || _winner != Winner.None || !_players[dying.VictimSeat].IsAlive || _players[dying.VictimSeat].Hp <= 0 ||
            !(_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawDyingExitRoundDistance) ||
              _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingliuFactionChoice) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouJiangWeiFuzhan) || IsWushuangMode)) return false;
        var facts = _players.Where(p => p.IsAlive).ToDictionary(p => p.Seat, CaptureProgramTriggerFacts);
        var candidates = _players.Where(p => p.IsAlive).SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.DyingExited))
            .Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.DrawDyingExitRoundDistance or SkillProgramEffectOp.ResolveQingliuFactionChoice or SkillProgramEffectOp.ResolveMouJiangWeiFuzhan }] || IsWushuangDyingExitCandidate(c, dying.VictimSeat))
            .OrderBy(c => RelativeTableOrder(_currentSeat, c.OwnerSeat)).ThenByDescending(c => c.Priority).ToArray();
        if (candidates.Length == 0) return false;
        var ret = new YanPuDyingExitReturn(dying.FrameId, dying.ParentFrameId, dying.VictimSeat, dying.Continuation, _roundNumber, _turnNumber, _currentSeat);
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, dying.VictimSeat, SkillProgramTriggerWindow.DyingExited,
            Array.AsReadOnly(candidates), ProgramLifecycleContinuation.ResumeYanPuDyingExit, facts[dying.VictimSeat]) { ParticipantFacts = facts, YanPuDyingExit = ret });
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>(); return true;
    }
    private void ReturnYanPuDyingExit(ProgramLifecycleTriggerWindowFrame frame)
    {
        var ret = frame.YanPuDyingExit ?? throw new InvalidOperationException("YanPu exit lost its original completed Dying return.");
        if (_resolutionStack.LastOrDefault()?.Id != ret.DyingParentFrameId || ret.Round != _roundNumber || ret.Turn != _turnNumber || ret.TurnOwner != _currentSeat)
            throw new InvalidOperationException("YanPu exit lost its exact paused native producer.");
        var dying = new DyingCompletionReceipt(ret.DyingFrameId, ret.DyingParentFrameId, ret.VictimSeat, ret.Continuation);
        if (TryBeginFactionRecoveryDebts(dying, true)) return;
        ContinueDyingAfterFactionRecoveryDebts(dying, true);
    }
}
