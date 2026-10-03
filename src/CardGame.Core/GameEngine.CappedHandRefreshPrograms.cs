using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : ICappedHandRefreshProgramHost
    {
        public SkillProgramStepOutcome DrawThenDiscardHandToMaximumHp(ProgramSkillFrame frame, int targetSeat, int cap) =>
            engine.BeginCappedHandRefresh(frame, targetSeat, cap);
    }

    private bool CappedHandRefreshSourceCurrent(ProgramSkillFrame frame)
    {
        if (!HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId)) return false;
        if (_players[frame.OwnerSeat].IsAlive) return true;
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } context ||
            _resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != frame.OwnerSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            GetCurrentDeathFrame(parent.DeathFrameId).VictimSeat != frame.OwnerSeat) return false;
        var candidate = parent.Candidates[parent.CandidateIndex];
        return candidate.OwnerSeat == frame.OwnerSeat && candidate.SkillId == frame.SkillId &&
            candidate.SkillInstanceId == frame.SkillInstanceId && candidate.BindingId == frame.TriggerId;
    }

    private SkillProgramStepOutcome BeginCappedHandRefresh(ProgramSkillFrame frame, int targetSeat, int cap)
    {
        if (frame.CappedHandRefresh is not null || frame.SelectedTargetSeats is not [var selected] || selected != targetSeat ||
            frame.WindowContext?.Window is not (SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.OwnerDied))
            throw new InvalidOperationException("Capped hand refresh lost its single selected target or exact rule window.");
        if (_winner != Winner.None || !_players[targetSeat].IsAlive || !CappedHandRefreshSourceCurrent(frame))
            return SkillProgramStepOutcome.Continue;
        var maximum = Math.Min(cap, Math.Max(0, _players[targetSeat].MaxHp));
        ReplaceRuntimeTop(frame with { CappedHandRefresh = new(targetSeat, maximum,
            ProgramCappedHandRefreshStage.Drawing, 0, Array.AsReadOnly(Array.Empty<int>()), Array.AsReadOnly(Array.Empty<int>())) });
        var drawn = DrawCards(_players[targetSeat], maximum, true, new("program.capped-hand-refresh.draw"));
        AdvanceEventRulesAndQueueFact(new ProgramCappedHandRefreshDrawnEvent(frame.Id, frame.OwnerSeat, targetSeat, maximum, drawn.Count));
        BeginCappedHandRefreshMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void BeginCappedHandRefreshMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.CappedHandRefresh!.TargetSeat, 0, null) });
        if (!TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.AwaitedProgramMovement) &&
            !TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frameId);
    }

    private bool ResumeCappedHandRefresh(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.CappedHandRefresh is not { } draft) return false;
        if (frame.PendingMovementContinuation is not null) return true;
        if (draft.Stage == ProgramCappedHandRefreshStage.Discarding)
        {
            FinishCappedHandRefresh(frame, completed: true);
            return true;
        }
        if (_winner != Winner.None || !_players[draft.TargetSeat].IsAlive || !CappedHandRefreshSourceCurrent(frame))
        {
            ClearPendingDecision();
            FinishCappedHandRefresh(frame, completed: false);
            return true;
        }
        if (draft.Stage == ProgramCappedHandRefreshStage.Selecting) return true;
        var hand = GetHand(_players[draft.TargetSeat]);
        var count = Math.Max(0, hand.Count - draft.Maximum);
        if (count == 0) { FinishCappedHandRefresh(frame, completed: true); return true; }
        ReplaceRuntimeTop(frame = frame with { CappedHandRefresh = draft with
        { Stage = ProgramCappedHandRefreshStage.Selecting, DiscardCount = count,
          CandidateCardIds = Array.AsReadOnly(hand.Select(c => c.Id).ToArray()) } });
        PublishCappedHandRefreshDiscard(frame);
        return true;
    }

    private bool ReturnCappedHandRefreshMovement(ProgramSkillFrame frame)
    {
        if (frame.CappedHandRefresh is not { } draft) return false;
        if (draft.Stage is not (ProgramCappedHandRefreshStage.Drawing or ProgramCappedHandRefreshStage.Discarding) ||
            frame.PendingMovementContinuation is not { } pending || pending.SubjectSeat != draft.TargetSeat ||
            pending.BeforeCount != 0 || pending.CoverageResultBind is not null)
            throw new InvalidOperationException("Capped hand refresh lost its exact completed movement child.");
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private IReadOnlyList<PromptChoice> CappedHandRefreshChoices(ProgramSkillFrame frame)
    {
        var draft = frame.CappedHandRefresh!;
        return Array.AsReadOnly(draft.CandidateCardIds.Except(draft.SelectedCardIds).Select(id =>
        {
            var card = GetHand(_players[draft.TargetSeat]).Single(c => c.Id == id);
            return new PromptChoice(new ChoiceId($"capped-hand.frame-{frame.Id}.pick-{draft.SelectedCardIds.Count}.card-{id}"),
                $"弃置【{card.DisplayName}】（{card.RankText}）", [id], [], new Dictionary<string, string>
                { ["program-action"] = "capped-hand-discard", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture),
                  ["selection-index"] = draft.SelectedCardIds.Count.ToString(CultureInfo.InvariantCulture) });
        }).ToArray());
    }

    private void PublishCappedHandRefreshDiscard(ProgramSkillFrame frame)
    {
        var draft = frame.CappedHandRefresh!; var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.TargetSeat,
            $"【{skill.Name}】将手牌弃至 {draft.Maximum} 张，还需选择 {draft.DiscardCount - draft.SelectedCardIds.Count} 张。",
            draft.CandidateCardIds.Except(draft.SelectedCardIds).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.TargetSeat,
          SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 弃置手牌", skill.Description),
          Choices = CappedHandRefreshChoices(frame) };
        _status = _players[draft.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveCappedHandRefreshChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Capped hand discard lost its frame.");
        var draft = frame.CappedHandRefresh ?? throw new InvalidOperationException("Capped hand discard lost its paid draw.");
        if (draft.Stage != ProgramCappedHandRefreshStage.Selecting ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != draft.TargetSeat ||
            !prompt.Choices.Any(choice => choice.Id == selected.Id) || selected.Cards is not [var cardId] ||
            selected.Targets.Count != 0 || selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("selection-index") != draft.SelectedCardIds.Count.ToString(CultureInfo.InvariantCulture) ||
            !draft.CandidateCardIds.Contains(cardId) || draft.SelectedCardIds.Contains(cardId))
            throw new InvalidOperationException("Capped hand discard changed its exact private selection.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[draft.TargetSeat].IsAlive || !CappedHandRefreshSourceCurrent(frame) ||
            draft.CandidateCardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(draft.TargetSeat)))
        { FinishCappedHandRefresh(frame, completed: false); return; }
        var ids = Array.AsReadOnly(draft.SelectedCardIds.Append(cardId).ToArray());
        draft = draft with { SelectedCardIds = ids };
        if (ids.Count < draft.DiscardCount)
        { ReplaceRuntimeTop(frame = frame with { CappedHandRefresh = draft }); PublishCappedHandRefreshDiscard(frame); return; }
        ReplaceRuntimeTop(frame with { CappedHandRefresh = draft with { Stage = ProgramCappedHandRefreshStage.Discarding } });
        MoveCards(ids.Select(id => GetHand(_players[draft.TargetSeat]).Single(c => c.Id == id)).ToArray(),
            CardLocation.Hand(draft.TargetSeat), CardLocation.DiscardPile, new("program.capped-hand-refresh.discard"));
        BeginCappedHandRefreshMovement(frame.Id);
    }

    private void FinishCappedHandRefresh(ProgramSkillFrame frame, bool completed)
    {
        var draft = frame.CappedHandRefresh!;
        ReplaceRuntimeTop(frame with { CappedHandRefresh = null });
        AdvanceEventRulesAndQueueFact(new ProgramCappedHandRefreshCompletedEvent(frame.Id, frame.OwnerSeat,
            draft.TargetSeat, draft.Maximum, draft.Stage == ProgramCappedHandRefreshStage.Discarding ? draft.DiscardCount : 0, completed));
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertCappedHandRefresh(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.CappedHandRefresh is not { } draft) return;
        if (paused.Op != SkillProgramEffectOp.DrawThenDiscardHandToMaximumHp ||
            frame.SelectedTargetSeats is not [var target] || target != draft.TargetSeat ||
            draft.Maximum < 0 || draft.Maximum > paused.Amount || draft.DiscardCount < 0 ||
            !Enum.IsDefined(draft.Stage) || draft.CandidateCardIds.Distinct().Count() != draft.CandidateCardIds.Count ||
            draft.SelectedCardIds.Distinct().Count() != draft.SelectedCardIds.Count ||
            draft.SelectedCardIds.Any(id => !draft.CandidateCardIds.Contains(id)) ||
            draft.SelectedCardIds.Count > draft.DiscardCount ||
            draft.CandidateCardIds is not System.Collections.IList { IsReadOnly: true } ||
            draft.SelectedCardIds is not System.Collections.IList { IsReadOnly: true })
            throw new InvalidOperationException("Capped hand refresh changed its frozen threshold or private owning draft.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && draft.Stage == ProgramCappedHandRefreshStage.Selecting &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != target ||
             draft.DiscardCount <= draft.SelectedCardIds.Count || !AssistedChoicesEqual(prompt.Choices, CappedHandRefreshChoices(frame))))
            throw new InvalidOperationException("Capped hand refresh lost its target-owned private discard prompt.");
    }
}
