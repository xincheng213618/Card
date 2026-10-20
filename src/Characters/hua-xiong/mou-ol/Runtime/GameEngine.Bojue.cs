using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouHxBojueDrawReason = "skill-program.mou-ol:bojue.draw";
    private const string MouHxBojueDiscardReason = "skill-program.mou-ol:bojue.discard";
    private static CardConversionSource MouHxBojueSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class MouHxBojueCapability(GameEngine e) : IMouHxBojueHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f) => e.BeginMouHxBojue(f); }

    private SkillProgramStepOutcome BeginMouHxBojue(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != MouHuaXiongOlComposition.Bojue || f.ActivationId != "simultaneous-choice" || f.TriggerId is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var peer] || peer == f.OwnerSeat ||
            !_players[peer].IsAlive || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play || f.MouHxBojue is not null)
            throw new InvalidOperationException("Bojue requires its real current Play activation and one other living participant.");
        var r = new MouHxBojueReceipt(MouHxBojueSource(f), f.GameplayHash, _turnNumber, peer,
            GetHand(_players[f.OwnerSeat]).Count + GetHand(_players[peer]).Count);
        ReplaceRuntimeTop(f = f with { MouHxBojue = r });
        AdvanceEventRulesAndQueueFact(new MouHxBojueStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, peer, r.InitialHandSum));
        PublishMouHxBojue(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private int MouHxBojueActor(ProgramSkillFrame f) => f.MouHxBojue!.Cursor == 0 ? f.OwnerSeat : f.MouHxBojue.Peer;
    private int MouHxBojueOther(ProgramSkillFrame f) => f.MouHxBojue!.Cursor == 0 ? f.MouHxBojue.Peer : f.OwnerSeat;
    private int MouHxBojueChooser(ProgramSkillFrame f) => f.MouHxBojue!.Stage == MouHxBojueStage.Commit
        ? f.MouHxBojue.Votes.Count == 0 ? f.OwnerSeat : f.MouHxBojue.Peer : MouHxBojueActor(f);

    private IReadOnlyList<PromptChoice> MouHxBojueChoices(ProgramSkillFrame f)
    {
        var r = f.MouHxBojue!;
        var chooser = MouHxBojueChooser(f);
        PromptChoice Option(string branch, string text) => new(new($"mou-hx-bojue.{f.Id}.{r.Stage}.{r.Cursor}.{r.Votes.Count}.{branch}"), text, [], [],
            new Dictionary<string, string> { ["program-action"] = "mou-hua-xiong-bojue", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage == MouHxBojueStage.Commit)
        {
            var result = new List<PromptChoice> { Option("draw", "摸一张牌") };
            if (_players[chooser].IsAlive && BuildOwnedCardPaymentChoices(f.Id, chooser, chooser,
                    [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard).Count > 0)
                result.Add(Option("discard", "弃置一张牌"));
            return Array.AsReadOnly(result.ToArray());
        }
        if (r.Stage is not (MouHxBojueStage.Discard or MouHxBojueStage.OtherDiscard)) return [];
        var foreign = r.Stage == MouHxBojueStage.OtherDiscard;
        var holder = foreign ? MouHxBojueOther(f) : chooser;
        if (!_players[holder].IsAlive || !_players[chooser].IsAlive) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, chooser, holder,
            foreign ? [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment] : [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => c with { Id = new($"mou-hx-bojue.{f.Id}.{r.Stage}.{r.Cursor}.{c.Id.Value}"),
                Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-hua-xiong-bojue", ["branch"] = "card" } }).ToArray());
    }

    private void PublishMouHxBojue(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var choices = MouHxBojueChoices(f);
        if (choices.Count == 0)
        {
            if (f.MouHxBojue!.Stage == MouHxBojueStage.Commit) FinishMouHxBojue(f);
            else CompleteMouHxBojueItem(f, false);
            return;
        }
        var chooser = MouHxBojueChooser(f);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, f.MouHxBojue!.Stage == MouHxBojueStage.Commit
            ? "搏决：分别密选摸一张或弃一张，双方提交后公开并依次执行。" : "搏决：选择一张合法的牌弃置。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("mou-hua-xiong-bojue")]
    private void AnswerMouHxBojue(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHxBojue: { } r } f || !ValidMouHxBojue(f) ||
            _pendingDecision?.PlayerSeat != MouHxBojueChooser(f) || !MouHxBojueChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Bojue accepts only its current participant's original private choice or native protected card slot.");
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MouHxBojueStage.Commit)
        {
            var votes = r.Votes.Append(new MouHxBojueVote(MouHxBojueChooser(f), choice.Parameters["branch"] == "draw")).ToArray();
            ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Votes = votes } });
            if (votes.Length == 1) { PublishMouHxBojue(f); return; }
            for (var i = 0; i < votes.Length; i++) AdvanceEventRulesAndQueueFact(new MouHxBojueVoteRevealedEvent(f.Id, i, votes[i].Seat, votes[i].Draw));
            ReplaceRuntimeTop(f = f with { MouHxBojue = f.MouHxBojue! with { Stage = MouHxBojueStage.Branch } });
            ContinueMouHxBojue(f.Id);
            return;
        }
        PayMouHxBojueDiscard(f, choice);
    }

    private void PayMouHxBojueDiscard(ProgramSkillFrame f, PromptChoice choice)
    {
        var r = f.MouHxBojue!;
        var actor = MouHxBojueActor(f);
        var holder = r.Stage == MouHxBojueStage.OtherDiscard ? MouHxBojueOther(f) : actor;
        var from = new CardLocation(Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]), holder);
        var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        var reason = new CardMoveReason(MouHxBojueDiscardReason);
        ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Stage = MouHxBojueStage.Children, PaidCard = card.Id,
            PaidBy = actor, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        // Executor is the participant who actually discards; ownership alone is not a discard invoice.
        var draft = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, actor, [card.Id], reason, f.Id);
        MoveCard(card, from, CardLocation.DiscardPile, reason, m =>
        {
            AdvanceEventRulesAndQueueFact(new MouHxBojueDiscardedEvent(f.Id, r.Cursor, actor, card.Id, m.From, m.To, m.Sequence, r.Delta is not null));
        }, disclosure: CardMovementDisclosure.FaceUp);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { MouHxBojue = f.MouHxBojue! with { After = _movementSequence } });
        CompleteNativeLogicalDiscardInvocation(draft);
        ContinueMouHxBojue(f.Id);
    }

    private bool DrainMouHxBojue(ProgramSkillFrame f) => TryBeginAdvancedSkillsChanged(f.Id) ||
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);

    private void ContinueMouHxBojue(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { MouHxBojue: { } r } f && f.Id == id)
        {
            if (!ValidMouHxBojue(f)) throw new InvalidOperationException("Bojue lost its original source, accepted pair, or once-paid native continuation.");
            if (r.Stage is MouHxBojueStage.NativeUse or MouHxBojueStage.Issuing or MouHxBojueStage.Drawing) return;
            if (r.Stage is MouHxBojueStage.Children or MouHxBojueStage.NativeChildren)
            {
                if (DrainMouHxBojue(f)) return;
                CompleteMouHxBojueItem(GetActiveProgramFrame(id), r.UseId > 0);
                return;
            }
            if (_winner != Winner.None) { FinishMouHxBojue(f); return; }
            if (r.Stage == MouHxBojueStage.Branch)
            {
                if (r.Cursor == 2)
                {
                    var sum = GetHand(_players[f.OwnerSeat]).Count + GetHand(_players[r.Peer]).Count;
                    var delta = Math.Abs(sum - r.InitialHandSum);
                    AdvanceEventRulesAndQueueFact(new MouHxBojueDeltaEvent(f.Id, r.InitialHandSum, sum, delta));
                    ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Delta = delta, Cursor = 0, Stage = MouHxBojueStage.Followup } });
                    continue;
                }
                var actor = MouHxBojueActor(f);
                if (!_players[actor].IsAlive) { CompleteMouHxBojueItem(f, false); return; }
                if (!r.Votes[r.Cursor].Draw)
                {
                    ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Stage = MouHxBojueStage.Discard } });
                    PublishMouHxBojue(f);
                    return;
                }
                ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Stage = MouHxBojueStage.Drawing } });
                BeginOperationProgramDraw(f, actor, 1, new(MouHxBojueDrawReason));
                return;
            }
            if (r.Stage == MouHxBojueStage.Followup)
            {
                if (r.Cursor == 2 || r.Delta is not (0 or 2)) { FinishMouHxBojue(f); return; }
                if (!_players[MouHxBojueActor(f)].IsAlive || !_players[MouHxBojueOther(f)].IsAlive) { CompleteMouHxBojueItem(f, false); return; }
                if (r.Delta == 0)
                {
                    ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Stage = MouHxBojueStage.OtherDiscard } });
                    PublishMouHxBojue(f);
                    return;
                }
                IssueMouHxBojueSlash(f);
                return;
            }
            if (_pendingDecision is null) PublishMouHxBojue(f);
            return;
        }
    }

    private void CompleteMouHxBojueItem(ProgramSkillFrame f, bool used)
    {
        var r = f.MouHxBojue!;
        if (r.Delta is null) AdvanceEventRulesAndQueueFact(new MouHxBojueBranchReturnedEvent(f.Id, r.Cursor, MouHxBojueActor(f)));
        else AdvanceEventRulesAndQueueFact(new MouHxBojueFollowupReturnedEvent(f.Id, r.Cursor, used));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MouHxBojue = r with {
            Stage = r.Delta is null ? MouHxBojueStage.Branch : MouHxBojueStage.Followup, Cursor = r.Cursor + 1,
            PaidCard = null, PaidBy = -1, Before = 0, After = 0, UseId = 0, ActionId = 0, Returned = false, DrawOutcome = null } });
        ContinueMouHxBojue(f.Id);
    }
    private void FinishMouHxBojue(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new MouHxBojueCompletedEvent(f.Id, f.MouHxBojue!.Delta ?? -1));
        ReplaceRuntimeTop(f = f with { MouHxBojue = null, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouHuaXiongBojue)]
    private void ReturnMouHxBojueDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MouHxBojue is not { Stage: MouHxBojueStage.Drawing } r || o.RecipientSeat != MouHxBojueActor(f) || o.RequestedCount != 1 ||
            o.SequenceAfter < o.SequenceBefore || o.SequenceAfter > _movementSequence || r.DrawOutcome is not null)
            throw new InvalidOperationException("Bojue lost its exact participant's single requested native draw.");
        ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Stage = MouHxBojueStage.Children, DrawOutcome = o,
            Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouHxBojueDrawReturnedEvent(f.Id, r.Cursor, o.RecipientSeat, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.AttemptFrameId));
        ContinueMouHxBojue(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 33300)]
    private bool ResumeMouHxBojue(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHxBojue: not null } f || f.Id != id) return false; ContinueMouHxBojue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 33300)]
    private bool ReturnMouHxBojueMovement(ProgramSkillFrame f)
    { if (f.MouHxBojue is null) return false; ContinueMouHxBojue(f.Id); return true; }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuaXiongBojue)]
    private PromptChoice ChooseMouHxBojue(PendingDecision p) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "draw") ?? p.Choices.First();
}
