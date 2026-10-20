using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouMhDiscardReason = "skill-program.mou-mobile:zaiqi.discard", MouMhDrawReason = "skill-program.mou-mobile:zaiqi.draw";
    private PromptChoice MouMhChoice(ProgramSkillFrame f, string branch, string label, int? seat = null, int? card = null) =>
        new(new($"mou-meng-huo.{f.Id}.{branch}.{seat}.{card}"), label, card is { } id ? [id] : [], seat is { } target ? [target] : [],
            new Dictionary<string, string> { ["program-action"] = "mou-meng-huo-zaiqi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouMhChoices(ProgramSkillFrame f)
    {
        var r = f.MouMengHuo!; var choices = new List<PromptChoice>();
        if (r.Stage == MouMengHuoStage.Selecting)
        {
            foreach (var target in _players.Where(p => p.IsAlive && (r.Targets.Contains(p.Seat) || r.Targets.Count < Math.Min(r.Budget, MouMhCharge(f.OwnerSeat)))))
                choices.Add(MouMhChoice(f, "toggle", (r.Targets.Contains(target.Seat) ? "取消" : "选择") + target.Name, target.Seat));
            if (r.Targets.Count > 0) choices.Add(MouMhChoice(f, "confirm", $"消耗{r.Targets.Count}点蓄力，依次结算"));
            choices.Add(MouMhChoice(f, "skip", "不发动再起"));
        }
        else if (r.Stage == MouMengHuoStage.RecipientChoice && r.Cursor < r.Targets.Count)
        {
            var seat = r.Targets[r.Cursor]; choices.Add(MouMhChoice(f, "draw", $"令{_players[f.OwnerSeat].Name}摸一张牌"));
            foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment })
            foreach (var card in _cardZones.CardsAt(new(zone, seat)).Where(card => !IsSelfHandCategoryDiscardForbidden(seat, card, new(zone, seat), OwnedCardMoveIntent.Discard)))
                choices.Add(MouMhChoice(f, "discard", $"弃置【{CardCatalog.Get(card.Kind).DisplayName}】，令{_players[f.OwnerSeat].Name}回复1点体力", card: card.Id));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouMh(ProgramSkillFrame f)
    {
        var r = f.MouMengHuo!; var chooser = r.Stage == MouMengHuoStage.Selecting ? f.OwnerSeat : r.Targets[r.Cursor]; var choices = MouMhChoices(f);
        if (choices.Count == 0) throw new InvalidOperationException("Zaiqi cannot publish an empty recipient choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouMengHuoStage.Selecting ? $"再起：拥有{MouMhCharge(f.OwnerSeat)}点蓄力，选择角色。" : "再起：选择令其摸牌，或实际弃置自己的一张手牌/装备令其回复体力。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-meng-huo-zaiqi")]
    private void AnswerMouMh(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouMengHuo;
        if (r is null || !ValidMouMh(f) || _pendingDecision?.PlayerSeat != (r.Stage == MouMengHuoStage.Selecting ? f.OwnerSeat : r.Targets[r.Cursor]) ||
            !AssistedChoicesEqual([selected], MouMhChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Zaiqi accepts only the actual chooser's published private choice.");
        ClearPendingDecision(); var branch = selected.Parameters["branch"];
        if (r.Stage == MouMengHuoStage.Selecting)
        {
            if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishMouMh(f); return; }
            if (branch == "skip") { FinishMouMh(f); return; }
            if (branch == "toggle")
            {
                var target = selected.Targets.Single(); var targets = r.Targets.Contains(target) ? r.Targets.Where(t => t != target).ToArray() : r.Targets.Append(target).ToArray();
                ReplaceRuntimeTop(f = f with { MouMengHuo = r with { Targets = targets } }); PublishMouMh(f); return;
            }
            if (branch != "confirm" || r.Targets.Count == 0 || r.Targets.Count > MouMhCharge(f.OwnerSeat) || r.Targets.Any(s => !_players[s].IsAlive)) throw new InvalidOperationException("Zaiqi cannot debit unpayable recipient count.");
            var before = MouMhCharge(f.OwnerSeat); ReplaceRuntimeTop(f = f with { MouMengHuo = r with { Accepted = true, Stage = MouMengHuoStage.RecipientChoice } });
            ChangeMouMhCharge(f, -r.Targets.Count);
            for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new MouMengHuoTargetSelectedEvent(f.Id, i, r.Targets[i]));
            AdvanceEventRulesAndQueueFact(new MouMengHuoChargeSpentEvent(f.Id, f.OwnerSeat, r.Targets.Count, before, before - r.Targets.Count)); ContinueMouMh(f.Id); return;
        }
        var recipient = r.Targets[r.Cursor];
        AdvanceEventRulesAndQueueFact(new MouMengHuoRecipientDecidedEvent(f.Id, r.Cursor, recipient, branch));
        if (branch == "draw")
        {
            ReplaceRuntimeTop(f = f with { MouMengHuo = r with { Stage = MouMengHuoStage.Drawing, Draw = null, Payment = null }, PendingMovementContinuation = null });
            BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(MouMhDrawReason)); return;
        }
        if (branch != "discard" || selected.Cards is not [var id]) throw new InvalidOperationException("Zaiqi requires one actual recipient-owned HE payment.");
        var card = GetAdvancedCard(id); var from = _cardZones.GetLocation(id); var beforeMove = _movementSequence;
        if (from.OwnerSeat != recipient || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || IsSelfHandCategoryDiscardForbidden(recipient, card, from, OwnedCardMoveIntent.Discard)) throw new InvalidOperationException("Zaiqi cannot discard another chooser's entity.");
        ReplaceRuntimeTop(f = f with { MouMengHuo = r with { Stage = MouMengHuoStage.DiscardChildren, Before = beforeMove, After = beforeMove, Draw = null, Payment = null }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var invocation = CaptureNativeLogicalDiscardInvocation(recipient, recipient, [id], new(MouMhDiscardReason), f.Id);
        MoveCard(card, from, CardLocation.DiscardPile, new(MouMhDiscardReason)); CompleteNativeLogicalDiscardInvocation(invocation);
        var move = _cardMovements.Single(m => m.Sequence > beforeMove && m.CardId == id && m.From == from && m.Reason.Value == MouMhDiscardReason);
        var payment = new MouMengHuoPayment(id, card.Kind, from, move.To, move.Sequence, invocation?.InvocationId ?? 0);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouMengHuo = f.MouMengHuo! with { Payment = payment } });
        AdvanceEventRulesAndQueueFact(new MouMengHuoDiscardPaidEvent(f.Id, r.Cursor, recipient, payment)); SealMouMh(f.Id, beforeMove); ContinueMouMh(f.Id);
    }
    private void ContinueMouMh(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouMengHuo: { } r } f || f.Id != id) return;
        if (!ValidMouMh(f)) throw new InvalidOperationException("Mou Meng Huo lost its owned phase and paid recipient receipt.");
        if (r.Stage is MouMengHuoStage.Drawing or MouMengHuoStage.Selecting) return;
        if (DrainMouMh(f)) return; f = GetActiveProgramFrame(id); r = f.MouMengHuo!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || r.Obtain) { FinishMouMh(f); return; }
        if (r.Stage == MouMengHuoStage.DiscardChildren)
        {
            if (r.Payment is not { } p || !ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.InvocationId == p.InvocationId && e.ParentFrameId == f.Id && e.ActualCount == 1)) throw new InvalidOperationException("Zaiqi's recovery needs its actual logical recipient discard.");
            var recipient = r.Targets[r.Cursor]; var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MouMengHuo = r with { Stage = MouMengHuoStage.RecoveryChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new MouMengHuoRecoveryIssuedEvent(f.Id, r.Cursor, recipient, f.OwnerSeat, 1, _players[f.OwnerSeat].Hp));
            new ProgramSkillHost(this).Recover(f.Id, recipient, f.OwnerSeat, 1, null, null); SealMouMh(f.Id, before); ContinueMouMh(id); return;
        }
        if (r.Stage is MouMengHuoStage.RecoveryChildren or MouMengHuoStage.DrawChildren)
        {
            r = r with { Cursor = r.Cursor + 1, Stage = MouMengHuoStage.RecipientChoice, Payment = null, Draw = null };
            ReplaceRuntimeTop(f = f with { MouMengHuo = r, PendingMovementContinuation = null });
        }
        while (r.Cursor < r.Targets.Count && !_players[r.Targets[r.Cursor]].IsAlive)
        { r = r with { Cursor = r.Cursor + 1 }; ReplaceRuntimeTop(f = f with { MouMengHuo = r }); }
        if (r.Cursor == r.Targets.Count) { FinishMouMh(f); return; }
        PublishMouMh(f);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouMengHuoZaiqi)]
    private void ReturnMouMhDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!ValidMouMh(f) || f.MouMengHuo is not { Stage: MouMengHuoStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1) throw new InvalidOperationException("Zaiqi cannot return a different or repeated native Draw1.");
        ReplaceRuntimeTop(f = f with { MouMengHuo = r with { Stage = MouMengHuoStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouMengHuoDrawReturnedEvent(f.Id, r.Cursor, 1, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        SealMouMh(f.Id, outcome.SequenceBefore); ContinueMouMh(f.Id);
    }
    private void FinishMouMh(ProgramSkillFrame f)
    {
        var r = f.MouMengHuo!; AdvanceEventRulesAndQueueFact(new MouMengHuoCompletedEvent(f.Id, r.Obtain, r.Accepted, r.Cursor));
        ReplaceRuntimeTop(f = f with { MouMengHuo = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouMengHuoZaiqi)]
    private PromptChoice AiMouMh(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouMengHuo!;
        if (r.Stage == MouMengHuoStage.Selecting)
        {
            var next = p.Choices.Where(c => c.Parameters["branch"] == "toggle" && !r.Targets.Contains(c.Targets.Single()) && AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()])).OrderBy(c => c.Targets.Single()).FirstOrDefault();
            return next ?? p.Choices.Single(c => c.Parameters["branch"] == (r.Targets.Count > 0 ? "confirm" : "skip"));
        }
        var cheapest = p.Choices.Where(c => c.Parameters["branch"] == "discard").OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat])).FirstOrDefault();
        return cheapest is not null && _players[f.OwnerSeat].Hp < _players[f.OwnerSeat].MaxHp && AreProgramDistributionAllies(_players[p.PlayerSeat], _players[f.OwnerSeat]) && GetKeepValue(GetAdvancedCard(cheapest.Cards.Single()), _players[p.PlayerSeat]) <= 4
            ? cheapest : p.Choices.Single(c => c.Parameters["branch"] == "draw");
    }
}
