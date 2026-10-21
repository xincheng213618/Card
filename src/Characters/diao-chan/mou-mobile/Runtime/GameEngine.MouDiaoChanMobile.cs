using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouDiaoChanLijian = "mou-mobile:lijian", MouDiaoChanBiyue = "mou-mobile:biyue";
    private const string MouDiaoChanCostReason = "skill-program.mou-mobile:lijian.discard";
    private static CardConversionSource MouDiaoChanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_diao_chan_mou_mobile_MouDiaoChanMobile.MouDiaoChanSource(f);
    [ProgramCapability] private sealed class MouDiaoChanMobileCapability(GameEngine engine) : IMouDiaoChanMobileHost
    { public SkillProgramStepOutcome Start(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.StartMouDiaoChanMobile(f, op); }
    private Card[] MouDiaoChanCosts(int owner) => GetHand(_players[owner]).Concat(GetEquipment(_players[owner])).Where(c =>
        !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
        !IsForeignEquipmentDiscardPrevented(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private int MouDiaoChanPhaseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouDiaoChanMobileStartedEvent>().Count(e =>
        e.Source.OwnerSeat == owner && e.Op == SkillProgramEffectOp.ResolveMouDiaoChanLijian && e.Phase == phase);
    [ProgramPhaseUsedProjection(31700)]
    private int ProjectMouDiaoChanPhaseUsed(int seat, string skill, int fallback) => skill == MouDiaoChanLijian && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p
        ? MouDiaoChanPhaseUses(seat, p) : fallback;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 31700, SkillProgramEffectOp.ResolveMouDiaoChanLijian)]
    private bool CanStartMouDiaoChanLijian(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner.Seat &&
        IsRecordedActualDiscardRecoveryPhase(p) && MouDiaoChanPhaseUses(owner.Seat, p) == 0 && MouDiaoChanCosts(owner.Seat).Length > 0 && _players.Count(x => x.IsAlive && x.Seat != owner.Seat) >= 2;
    private SkillProgramStepOutcome StartMouDiaoChanMobile(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouDiaoChanMobile is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            CurrentActualDiscardRecoveryPhase() is not { } phase || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("Mou Diao Chan requires her exact original native phase entry.");
        if (op == SkillProgramEffectOp.ResolveMouDiaoChanLijian ? f.SkillId != MouDiaoChanLijian || f.ActivationId != "ring-duels" || f.TriggerId is not null || !CanStartMouDiaoChanLijian(_players[f.OwnerSeat]) :
            f.SkillId != MouDiaoChanBiyue || f.TriggerId != "ending-damaged-roles" || f.ActivationId != f.TriggerId || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } || phase.Kind != ActualDiscardRecoveryPhaseKind.Ending)
            throw new InvalidOperationException("Mou Diao Chan cannot borrow another phase or binding.");
        var r = new MouDiaoChanMobileReceipt(MouDiaoChanSource(f), f.GameplayHash, op, phase)
            { Stage = op == SkillProgramEffectOp.ResolveMouDiaoChanLijian ? MouDiaoChanMobileStage.Targets : MouDiaoChanMobileStage.Drawing };
        ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r });
        AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileStartedEvent(f.Id, r.Source, r.GameplayHash, op, phase));
        if (op == SkillProgramEffectOp.ResolveMouDiaoChanLijian) { PublishMouDiaoChan(f); return SkillProgramStepOutcome.AwaitChoice; }
        var prefix = CompleteProgramEventHistory().Reverse().TakeWhile(e => e is not TurnStartedEvent).OfType<DamageAppliedEvent>();
        var damaged = prefix.Where(e => e.Amount > 0).Select(e => e.TargetSeat).Distinct().Count();
        var draw = Math.Min(4, damaged + 1);
        ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Paid = true, DrawCount = draw } });
        AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileBiyueEvent(f.Id, _turnNumber, damaged, draw));
        ContinueMouDiaoChan(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice MouDiaoChanChoice(ProgramSkillFrame f, string branch, string text, int? target = null, int? card = null) => new(
        new($"mou-diao-chan.{f.Id}.{branch}.{target ?? card ?? 0}"), text, card is { } id ? [id] : [], target is { } seat ? [seat] : [],
        new Dictionary<string, string> { ["program-action"] = "mou-diao-chan-mobile", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouDiaoChanChoices(ProgramSkillFrame f)
    {
        var r = f.MouDiaoChanMobile!; var choices = new List<PromptChoice>();
        if (r.Stage == MouDiaoChanMobileStage.Targets)
        {
            foreach (var p in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat))
                if (r.Targets.Contains(p.Seat) || r.Targets.Count < MouDiaoChanCosts(f.OwnerSeat).Length + 1)
                    choices.Add(MouDiaoChanChoice(f, "target", (r.Targets.Contains(p.Seat) ? "取消：" : "选择：") + p.Name, target: p.Seat));
            if (r.Targets.Count >= 2) choices.Add(MouDiaoChanChoice(f, "targets-done", $"确定{r.Targets.Count}名角色"));
        }
        if (r.Stage == MouDiaoChanMobileStage.Cards)
        {
            foreach (var c in MouDiaoChanCosts(f.OwnerSeat))
                if (r.Cards.Contains(c.Id) || r.Cards.Count < r.Targets.Count - 1)
                    choices.Add(MouDiaoChanChoice(f, "card", (r.Cards.Contains(c.Id) ? "取消：" : "弃置：") + c.DisplayName, card: c.Id));
            if (r.Cards.Count == r.Targets.Count - 1) choices.Add(MouDiaoChanChoice(f, "pay", $"弃置{r.Cards.Count}张牌并结算全部离间"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouDiaoChan(ProgramSkillFrame f)
    {
        var choices = MouDiaoChanChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-diao-chan-mobile")]
    private void AnswerMouDiaoChan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDiaoChanMobile: { } r } f || !ValidMouDiaoChan(f) ||
            !MouDiaoChanChoices(f).Any(c => c.Id == choice.Id && AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Mou Diao Chan lost her exact private selection.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "target")
        { var seat = choice.Targets.Single(); ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Targets = r.Targets.Contains(seat) ? r.Targets.Where(x => x != seat).ToArray() : r.Targets.Append(seat).ToArray() } }); PublishMouDiaoChan(f); return; }
        if (branch == "targets-done")
        { ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Stage = MouDiaoChanMobileStage.Cards } }); PublishMouDiaoChan(f); return; }
        if (branch == "card")
        { var id = choice.Cards.Single(); ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Cards = r.Cards.Contains(id) ? r.Cards.Where(x => x != id).ToArray() : r.Cards.Append(id).ToArray() } }); PublishMouDiaoChan(f); return; }
        if (branch != "pay" || r.Cards.Count != r.Targets.Count - 1 || !r.Cards.All(id => MouDiaoChanCosts(f.OwnerSeat).Any(c => c.Id == id)) || r.Targets.Any(s => !_players[s].IsAlive))
            throw new InvalidOperationException("Lijian must pay exactly its complete selected roster minus one.");
        var roster = r.Targets.OrderBy(s => RelativeTableOrder(f.OwnerSeat, s)).ToArray(); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r = r with { Paid = true, Targets = roster, Stage = MouDiaoChanMobileStage.CostChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < roster.Length; i++) AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileRosterEvent(f.Id, i, roster[i]));
        foreach (var id in r.Cards)
        {
            var card = GetAdvancedCard(id); var from = _cardZones.GetLocation(id);
            MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, from, new(MouDiaoChanCostReason), parentFrameId: f.Id);
            var moved = _cardMovements.LastOrDefault(m => m.CardId == id && m.From == from && m.Sequence > before);
            if (moved is not null) AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileCostEvent(f.Id, id, card.Kind, from, moved.To, moved.Sequence));
        }
        SealMouDiaoChanMovement(f.Id, before); ContinueMouDiaoChan(f.Id);
    }
    private bool ValidMouDiaoChan(ProgramSkillFrame f) => f.MouDiaoChanMobile is { } r && r.Source == MouDiaoChanSource(f) && r.GameplayHash == f.GameplayHash &&
        _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash && IsRecordedActualDiscardRecoveryPhase(r.Phase) && r.Phase.ActorSeat == f.OwnerSeat &&
        r.Targets.Distinct().Count() == r.Targets.Count && r.Cards.Distinct().Count() == r.Cards.Count && r.Index >= 0 && r.Index <= r.Targets.Count &&
        ProgramEventHistory<MouDiaoChanMobileStartedEvent>().Count(e => e == new MouDiaoChanMobileStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.Phase)) == 1 &&
        (!r.Paid || r.Op != SkillProgramEffectOp.ResolveMouDiaoChanLijian || r.Targets.Count >= 2 && r.Cards.Count == r.Targets.Count - 1 &&
            r.Targets.Select((seat, index) => new MouDiaoChanMobileRosterEvent(f.Id, index, seat)).All(e => ProgramEventHistory<MouDiaoChanMobileRosterEvent>().Count(p => p == e) == 1));
    private void ContinueMouDiaoChan(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDiaoChanMobile: { } r } f || f.Id != id) return;
        if (!ValidMouDiaoChan(f)) throw new InvalidOperationException("Mou Diao Chan lost her original accepted cursor.");
        if (r.Stage is MouDiaoChanMobileStage.Targets or MouDiaoChanMobileStage.Cards) { PublishMouDiaoChan(f); return; }
        if (r.Stage == MouDiaoChanMobileStage.Drawing)
        { if (f.NativeDraw is null) BeginOperationProgramDraw(f, f.OwnerSeat, r.DrawCount, new("skill-program.mou-mobile:biyue.draw")); return; }
        if (r.Stage == MouDiaoChanMobileStage.Duels) { if (r.UseId == 0) IssueMouDiaoChanDuel(f); return; }
        if (DrainMouDiaoChanChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouDiaoChanMobile!;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == MouDiaoChanMobileStage.DrawChildren || _winner != Winner.None) { FinishMouDiaoChan(f); return; }
        ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Stage = MouDiaoChanMobileStage.Duels } }); IssueMouDiaoChanDuel(f);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouDiaoChanBiyue)]
    private void ReturnMouDiaoChanBiyue(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!ValidMouDiaoChan(f) || f.MouDiaoChanMobile is not { Stage: MouDiaoChanMobileStage.Drawing } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != r.DrawCount)
            throw new InvalidOperationException("Biyue must return its original native Ending draw.");
        ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = r with { Stage = MouDiaoChanMobileStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouDiaoChanMobileDrawReturnedEvent(f.Id, outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId));
        SealMouDiaoChanMovement(f.Id, outcome.SequenceBefore); ContinueMouDiaoChan(f.Id);
    }
    private void FinishMouDiaoChan(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { MouDiaoChanMobile = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouDiaoChanLijian)]
    private PromptChoice AiMouDiaoChan(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouDiaoChanMobile!;
        if (r.Stage == MouDiaoChanMobileStage.Targets)
        {
            if (r.Targets.Count >= 2) return p.Choices.Single(c => c.Parameters["branch"] == "targets-done");
            return p.Choices.Where(c => c.Targets.Count == 1 && !r.Targets.Contains(c.Targets[0])).OrderBy(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]]) ? 1 : 0).ThenBy(c => c.Targets[0]).First();
        }
        if (r.Cards.Count == r.Targets.Count - 1) return p.Choices.Single(c => c.Parameters["branch"] == "pay");
        return p.Choices.Where(c => c.Cards.Count == 1 && !r.Cards.Contains(c.Cards[0])).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[f.OwnerSeat])).ThenBy(c => c.Cards[0]).First();
    }
}
