namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DrainMoQuanyu(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueMoQuanyu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoQuanyu: { } r } f || f.Id != id || _pendingDecision is not null) return;
        AssertMoQuanyu(f);
        if (r.Stage == MoQuanyuStage.Choosing)
        {
            while (r.Votes.Count < r.Participants.Count)
            {
                var participant = r.Participants[r.Votes.Count];
                if (participant.AvailableMask != 0 && _players[participant.Seat].IsAlive) { PublishMoQuanyu(f); return; }
                r = r with { Votes = r.Votes.Append(new(participant.Seat, MoQuanyuEffect.None)).ToArray() };
                ReplaceRuntimeTop(f = f with { MoQuanyu = r });
            }
            var ownerChoice = r.Kind == MoQuanyuInvocationKind.Round
                ? r.Votes.Single(v => v.Seat == f.OwnerSeat).Effect : r.OwnerChoiceBefore;
            var matching = ownerChoice == MoQuanyuEffect.None ? 0 : r.Votes.Count(v => v.Effect == ownerChoice);
            r = r with { DrawCount = Math.Min(3, matching), Stage = MoQuanyuStage.Drawing };
            ReplaceRuntimeTop(f = f with { MoQuanyu = r });
            foreach (var (vote, i) in r.Votes.Select((v, i) => (v, i)))
            {
                var participant = r.Participants[i];
                AdvanceEventRulesAndQueueFact(new MoQuanyuChoiceRevealedEvent(id, i, f.OwnerSeat, vote.Seat,
                    r.RoundNumber, vote.Effect, participant.HistoryMask, participant.HistoryMask | MoQuanyuBit(vote.Effect)));
                AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 的【权御】：{_players[vote.Seat].Name} 选择了{MoQuanyuEffectName(vote.Effect)}。", f.OwnerSeat, vote.Seat);
            }
            AdvanceEventRulesAndQueueFact(new MoQuanyuRevealCompletedEvent(id, ownerChoice, matching, r.DrawCount));
        }
        if (r.Stage == MoQuanyuStage.Drawing)
        {
            if (f.NativeDraw is not null) return;
            if (r.DrawCount == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { CompleteMoQuanyu(f); return; }
            AdvanceEventRulesAndQueueFact(new MoQuanyuDrawRequestedEvent(id, f.OwnerSeat, r.DrawCount));
            BeginOperationProgramDraw(f, f.OwnerSeat, r.DrawCount, new(MoQuanyuDrawReason)); return;
        }
        if (DrainMoQuanyu(f)) return;
        CompleteMoQuanyu(GetActiveProgramFrame(id));
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMoSunQuanQuanyuRoundChoices, SkillProgramEffectOp.ResolveMoSunQuanQuanyuTargetChoice)]
    private void ReturnMoQuanyuDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MoQuanyu is not { Stage: MoQuanyuStage.Drawing } r || outcome.RecipientSeat != f.OwnerSeat ||
            outcome.RequestedCount != r.DrawCount || ProgramEventHistory<MoQuanyuDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Quanyu lost its one native matching-choice draw return.");
        ReplaceRuntimeTop(f = f with { MoQuanyu = r with { Stage = MoQuanyuStage.DrawChildren, DrawOutcome = outcome },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MoQuanyuDrawReturnedEvent(f.Id, outcome.RequestedCount, outcome.ActualDrawIds.Count,
            outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueMoQuanyu(f.Id);
    }

    private void CompleteMoQuanyu(ProgramSkillFrame f)
    {
        AssertMoQuanyu(f); var r = f.MoQuanyu!;
        if (r.Stage == MoQuanyuStage.Choosing || f.NativeDraw is not null ||
            ProgramEventHistory<MoQuanyuChoicesCompletedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Quanyu cannot complete before every private choice and native child returns.");
        var count = r.DrawOutcome?.RequestedCount ?? 0;
        AdvanceEventRulesAndQueueFact(new MoQuanyuChoicesCompletedEvent(f.Id, r.Kind, count));
        var returned = r.TargetRequest is { } request ? new MoQuanyuTargetChoiceReturn(f.Id, request, r.Votes.Single().Effect, count) : null;
        if (returned is not null) AdvanceEventRulesAndQueueFact(new MoQuanyuTargetChoiceReturnedEvent(returned));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
        if (returned is not null)
        {
            if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame parent || parent.Id != returned.Request.ParentFrameId ||
                !IsMoSunQuanTianenQuanyuRequest(parent, returned.Request))
                throw new InvalidOperationException("Quanyu target-only completion lost its exact paid Tianen parent.");
            ReturnMoSunQuanTianenQuanyu(parent, returned);
        }
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28910)]
    private bool ResumeMoQuanyu(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MoQuanyu: not null } f || f.Id != id) return false; ContinueMoQuanyu(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28910)]
    private bool ReturnMoQuanyuMovement(ProgramSkillFrame f)
    { if (f.MoQuanyu is null) return false; ContinueMoQuanyu(f.Id); return true; }
}
