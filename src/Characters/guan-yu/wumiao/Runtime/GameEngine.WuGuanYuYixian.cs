using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuYixianObtainReason = "skill-program.wu-guan-yu.yixian.obtain";
    private const string WuYixianDrawReason = "skill-program.wu-guan-yu.yixian.draw";
    private const string WuYixianUsage = "all-equipment";
    private static CardConversionSource WuYixianSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_guan_yu_wumiao_WuGuanYuYixian.WuYixianSource(f);
    [ProgramCapability]
    private sealed class WuYixianCapability(GameEngine engine) : IWuYixianHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginWuYixian(frame); }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28530,
        SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate)]
    private bool CanActivateWuYixian(ProgramActivationLegalityContext c) => c.Owner.IsAlive &&
        _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat && _winner == Winner.None &&
        _skillRuntimeState.GetUsage(c.OwnerSeat, c.Program.Id, WuYixianUsage, SkillUsageScope.Game) == 0;

    private SkillProgramStepOutcome BeginWuYixian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Yixian is not null) { ContinueWuYixian(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != "wumiao:yixian" || f.ActivationId != "all-equipment" || f.TriggerId is not null ||
            f.WindowContext is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !_players[f.OwnerSeat].IsAlive || _phase != TurnPhase.Play || f.OwnerSeat != _currentSeat ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, WuYixianUsage, SkillUsageScope.Game) != 0)
            throw new InvalidOperationException("Yixian requires its owner's current unused limited Play activation.");
        ReplaceRuntimeTop(f = f with { Yixian = new(WuYixianSource(f), f.GameplayHash, f.InstructionIndex,
            _turnNumber, _turnProgression.OwnerSeat, YixianStage.BranchChoice) });
        PublishWuYixian(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> WuYixianChoices(ProgramSkillFrame f)
    {
        if (f.Yixian is not { } r || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return [];
        PromptChoice Choice(string branch, string label, int? target = null) => new(
            new($"wu-yixian.{f.Id}.{r.Cursor}.{branch}"), label, [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "wu-yixian", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
        if (r.Stage == YixianStage.BranchChoice)
            return Array.AsReadOnly(new[] { Choice("field", "获得场上所有装备牌"), Choice("discard", "获得弃牌堆中所有装备牌"), Choice("cancel", "取消") });
        if (r.Stage != YixianStage.RecipientChoice || r.Cursor >= r.Recipients.Count) return [];
        var recipient = r.Recipients[r.Cursor];
        return Array.AsReadOnly(new[] { Choice("benefit", $"令{_players[recipient.Seat].Name}摸{recipient.ObtainedCount}张牌并回复1点体力", recipient.Seat), Choice("skip", "不令其摸牌及回复体力", recipient.Seat) });
    }
    private void PublishWuYixian(ProgramSkillFrame f)
    {
        AssertWuYixian(f); var choices = WuYixianChoices(f);
        if (choices.Count == 0) { CompleteWuYixian(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.Yixian!.Stage == YixianStage.BranchChoice ? "义贤：选择获得装备牌的范围。" : "义贤：可令原装备持有者摸牌并回复体力。", [], [], SourceSeat: f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wu-yixian")]
    private void AnswerWuYixian(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Yixian: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !WuYixianChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Yixian requires its original private branch or recipient choice.");
        AssertWuYixian(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == YixianStage.BranchChoice)
        {
            if (branch == "cancel") { CompleteWuYixian(f); return; }
            CommitWuYixianEquipment(f, branch == "field" ? YixianBranch.Field : YixianBranch.DiscardPile); return;
        }
        var recipient = r.Recipients[r.Cursor]; var accepted = branch == "benefit";
        AdvanceEventRulesAndQueueFact(new YixianRecipientDecisionEvent(f.Id, r.Cursor, recipient.Seat, recipient.ObtainedCount, accepted));
        ReplaceRuntimeTop(f = f with { Yixian = r with { BenefitAccepted = accepted,
            Stage = accepted ? YixianStage.Drawing : YixianStage.RecipientChoice,
            Cursor = accepted ? r.Cursor : r.Cursor + 1 } });
        ContinueWuYixian(f.Id);
    }
    private void CommitWuYixianEquipment(ProgramSkillFrame f, YixianBranch branch)
    {
        if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !_players[f.OwnerSeat].IsAlive || !_skillRuntimeState.TryConsumeUsage(f.OwnerSeat, f.SkillId, WuYixianUsage, SkillUsageScope.Game, 1))
            throw new InvalidOperationException("Yixian cannot pay an expired or already-consumed limited activation.");
        var ids = branch == YixianBranch.Field
            ? _players.SelectMany(p => GetEquipment(p)).Select(c => c.Id).ToArray()
            : _cardZones.CardsAt(CardLocation.DiscardPile).Where(c => EquipmentCatalog.IsEquipment(c.Kind)).Select(c => c.Id).ToArray();
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Yixian = f.Yixian! with { Paid = true, Branch = branch,
            Stage = YixianStage.EquipmentChildren, SequenceBefore = before, SequenceAfter = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (ids.Length > 0)
            MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(f.OwnerSeat), new CardMoveReason(WuYixianObtainReason),
                (batchId, movements) =>
                {
                    var current = GetActiveProgramFrame(f.Id);
                    ReplaceRuntimeTop(current with { Yixian = current.Yixian! with { BatchId = batchId,
                        Entities = movements.Select(m => new YixianEntity(m.CardId, m.CardKind, m.From, m.To, m.Sequence)).ToArray() } });
                });
        f = GetActiveProgramFrame(f.Id); var r = f.Yixian!;
        var recipients = r.Entities.Where(e => e.From.Zone == CardZoneKind.Equipment && e.From.OwnerSeat is not null && e.To == CardLocation.Hand(f.OwnerSeat))
            .GroupBy(e => e.From.OwnerSeat!.Value).OrderBy(g => RelativeTableOrder(r.TurnOwner, g.Key))
            .Select(g => new YixianRecipient(g.Key, g.Count())).ToArray();
        ReplaceRuntimeTop(f = f with { Yixian = r = r with { SequenceAfter = _movementSequence, Recipients = recipients } });
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(f.OwnerSeat, f.SkillId, WuYixianUsage, SkillUsageScope.Game, 1));
        AdvanceEventRulesAndQueueFact(new YixianPaidEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner,
            branch, r.BatchId, r.Entities.Count, r.Recipients.Count, r.SequenceBefore, r.SequenceAfter));
        for (var i = 0; i < r.Entities.Count; i++) { var e = r.Entities[i]; AdvanceEventRulesAndQueueFact(new YixianEntityObtainedEvent(f.Id, i, e.CardId, e.PrintedKind, e.From, e.To, e.MovementSequence)); }
        for (var i = 0; i < recipients.Length; i++) AdvanceEventRulesAndQueueFact(new YixianRecipientFrozenEvent(f.Id, i, recipients[i].Seat, recipients[i].ObtainedCount));
        ContinueWuYixian(f.Id);
    }
    private bool DrainWuYixian(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginAdvancedSkillsChanged(f.Id) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueWuYixian(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { Yixian: { } r } f && f.Id == id && _pendingDecision is null)
        {
            AssertWuYixian(f);
            if (f.NativeDraw is not null) return;
            if (r.Stage is YixianStage.EquipmentChildren or YixianStage.DrawChildren or YixianStage.RecoveryChildren)
            {
                if (DrainWuYixian(f)) return;
                f = GetActiveProgramFrame(id); r = f.Yixian!;
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage == YixianStage.DrawChildren)
                {
                    var seat = r.Recipients[r.Cursor].Seat;
                    ReplaceRuntimeTop(f = f with { Yixian = r = r with { Stage = YixianStage.RecoveryChildren },
                        PendingMovementContinuation = new(seat, 0, null) });
                    AdvanceEventRulesAndQueueFact(new YixianRecoveryIssuedEvent(id, r.Cursor, seat, _players[seat].IsAlive));
                    if (_players[seat].IsAlive && _winner == Winner.None) new ProgramSkillHost(this).Recover(id, f.OwnerSeat, seat, 1, null, null);
                    continue;
                }
                ReplaceRuntimeTop(f = f with { Yixian = r = r with { Stage = YixianStage.RecipientChoice,
                    Cursor = r.Cursor + (r.Stage == YixianStage.RecoveryChildren ? 1 : 0), BenefitAccepted = false, DrawOutcome = null } });
            }
            if (_winner != Winner.None) { CompleteWuYixian(f); return; }
            if (r.Stage == YixianStage.Drawing)
            {
                var peer = r.Recipients[r.Cursor];
                BeginOperationProgramDraw(f, peer.Seat, peer.ObtainedCount, new(WuYixianDrawReason)); return;
            }
            if (r.Stage == YixianStage.RecipientChoice)
            {
                while (r.Cursor < r.Recipients.Count && !_players[r.Recipients[r.Cursor].Seat].IsAlive)
                {
                    var peer = r.Recipients[r.Cursor];
                    AdvanceEventRulesAndQueueFact(new YixianRecipientDecisionEvent(id, r.Cursor, peer.Seat, peer.ObtainedCount, false));
                    ReplaceRuntimeTop(f = f with { Yixian = r = r with { Cursor = r.Cursor + 1 } });
                }
                if (r.Cursor >= r.Recipients.Count || !_players[f.OwnerSeat].IsAlive) { CompleteWuYixian(f); return; }
                PublishWuYixian(f); return;
            }
            if (r.Stage == YixianStage.BranchChoice) { PublishWuYixian(f); return; }
            throw new InvalidOperationException("Yixian returned to an unsupported native stage.");
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate)]
    private void ReturnWuYixianDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.Yixian is not { Paid: true, Stage: YixianStage.Drawing, BenefitAccepted: true } r ||
            r.Cursor >= r.Recipients.Count || outcome.RecipientSeat != r.Recipients[r.Cursor].Seat ||
            outcome.RequestedCount != r.Recipients[r.Cursor].ObtainedCount ||
            ProgramEventHistory<YixianDrawReturnedEvent>().Any(e => e.FrameId == f.Id && e.Index == r.Cursor))
            throw new InvalidOperationException("Yixian's native draw returned to a different or already-consumed original owner.");
        ReplaceRuntimeTop(f = f with { Yixian = r with { Stage = YixianStage.DrawChildren, DrawOutcome = outcome },
            PendingMovementContinuation = new(outcome.RecipientSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new YixianDrawReturnedEvent(f.Id, r.Cursor, outcome.RecipientSeat, outcome.RequestedCount,
            outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueWuYixian(f.Id);
    }
    private void CompleteWuYixian(ProgramSkillFrame f)
    {
        var r = f.Yixian!;
        AdvanceEventRulesAndQueueFact(new YixianCompletedEvent(f.Id, r.Paid, r.Branch, r.Entities.Count,
            ProgramEventHistory<YixianRecipientDecisionEvent>().Count(e => e.FrameId == f.Id && e.Accepted)));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28530)]
    private bool ResumeWuYixian(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Yixian: not null } f || f.Id != id) return false; if (f.NativeDraw is null) ContinueWuYixian(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28530)]
    private bool ReturnWuYixianMovement(ProgramSkillFrame f)
    { if (f.Yixian is null) return false; ContinueWuYixian(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28530)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28530)]
    private bool PermitPaidWuYixian(ProgramSkillFrame f) => CanContinueIssuedWuYixian(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveYixianAllEquipmentObtainAndCompensate)]
    private PromptChoice ChooseWuYixianAi(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.Yixian!;
        if (r.Stage == YixianStage.BranchChoice)
        {
            var field = _players.Sum(p => GetEquipment(p).Count); var discard = _cardZones.CardsAt(CardLocation.DiscardPile).Count(c => EquipmentCatalog.IsEquipment(c.Kind));
            return decision.Choices.Single(c => c.Parameters["branch"] == (field >= discard ? "field" : "discard"));
        }
        var recipient = r.Recipients[r.Cursor]; var view = CreateSnapshot(f.OwnerSeat);
        var value = _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, recipient.Seat, new(recipient.ObtainedCount, 0, 0, 0, 1, 0, false, false));
        return decision.Choices.Single(c => c.Parameters["branch"] == (value > 0 ? "benefit" : "skip"));
    }
}
