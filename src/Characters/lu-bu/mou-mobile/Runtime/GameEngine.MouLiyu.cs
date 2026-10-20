using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouLbObtainReason = "skill-program.mou-mobile:liyu.obtain", MouLbDrawReason = "skill-program.mou-mobile:liyu.draw";
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31601, SkillProgramEffectOp.ResolveMouLuBuLiyu)]
    private bool CanMouLbLiyu(int owner, ProgramSkillWindowContext c) => c.TargetSeat is { } victim && victim != owner && _players[victim].IsAlive &&
        ActualDamageTargetMountUse(owner, c) is not null && GetHand(_players[victim]).Concat(GetEquipment(_players[victim])).Concat(GetJudgment(_players[victim]))
            .Any(card => !IsForeignEquipmentDiscardPrevented(owner, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Obtain));
    private SkillProgramStepOutcome BeginMouLbLiyu(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var c = f.WindowContext!;
        if (f.MouLuBuLiyu is not null) throw new InvalidOperationException("Liyu cannot issue its accepted damage twice.");
        if (!CanMouLbLiyu(f.OwnerSeat, c)) return SkillProgramStepOutcome.Continue;
        var use = ActualDamageTargetMountUse(f.OwnerSeat, c)!;
        var r = new MouLuBuLiyuReceipt(MouLbSource(f), f.GameplayHash, c.ParentFrameId, c.DamageFrameId!.Value, use.Id, use.Action!.ActionId, c.TargetSeat!.Value, c.Amount, _turnNumber);
        ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r });
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.DamageFrameId, r.OuterUseId, r.OuterActionId, r.VictimSeat, r.DamageAmount, r.ActualTurn));
        PublishMouLb(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private static int MouLbCategory(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_bu_mou_mobile_MouLiyu.MouLbCategory(kind);
    private IReadOnlyList<PromptChoice> MouLbChoices(ProgramSkillFrame f)
    {
        var r = f.MouLuBuLiyu!; var choices = new List<PromptChoice>();
        Dictionary<string, string> Params(string branch) => new() { ["program-action"] = "mou-lu-bu-liyu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.Stage == MouLuBuLiyuStage.ChoosingCard)
        {
            if (r.Selections < r.DamageAmount && _players[f.OwnerSeat].IsAlive && _players[r.VictimSeat].IsAlive)
                choices.AddRange(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.VictimSeat, DamageTargetMountZones, OwnedCardMoveIntent.Obtain)
                    .Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-lu-bu-liyu", ["branch"] = "take" } }));
            choices.Add(new(new($"mou-liyu.{f.Id}.finish"), $"结束获得（已选{r.Selections}/{r.DamageAmount}张）", [], [], Params("finish")));
        }
        else if (r.Stage == MouLuBuLiyuStage.VictimChoice && _players[r.VictimSeat].IsAlive)
        {
            choices.Add(new(new($"mou-liyu.{f.Id}.wushuang"), "获得完整【无双】直至下个回合结束", [], [], Params("wushuang")));
            foreach (var target in _players.Where(p => CanMouLbDuel(f, p.Seat)))
                choices.Add(new(new($"mou-liyu.{f.Id}.duel.{target.Seat}"), $"令吕布视为对{target.Name}使用【决斗】", [], [target.Seat], Params("duel")));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouLb(ProgramSkillFrame f)
    {
        var r = f.MouLuBuLiyu!; var chooser = r.Stage == MouLuBuLiyuStage.VictimChoice ? r.VictimSeat : f.OwnerSeat;
        var choices = MouLbChoices(f); if (choices.Count == 0) { FinishMouLb(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouLuBuLiyuStage.VictimChoice ? "利驭：选择决斗目标或获得无双。" : "利驭：获得其区域里的至多伤害数张牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.VictimSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-lu-bu-liyu")]
    private void AnswerMouLb(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuBuLiyu: { } r } f || !ValidMouLb(f) ||
            _pendingDecision?.PlayerSeat != (r.Stage == MouLuBuLiyuStage.VictimChoice ? r.VictimSeat : f.OwnerSeat) || !MouLbChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Liyu answer changed its actual chooser or concealed slot.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (branch == "finish") { FinishMouLbTaking(f); return; }
        if (branch == "take")
        {
            if (!r.Paid && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishMouLb(f); return; }
            var from = new CardLocation(Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]), r.VictimSeat);
            var card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture)]; var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r = r with { Paid = true, Selections = r.Selections + 1, Stage = MouLuBuLiyuStage.ObtainChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(card, from, card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat), new(MouLbObtainReason), m =>
            {
                var current = GetActiveProgramFrame(f.Id); var paid = current.MouLuBuLiyu!; var gain = m.To == CardLocation.Hand(f.OwnerSeat);
                var mask = paid.CategoryMask | (gain ? MouLbCategory(m.CardKind) : 0);
                ReplaceRuntimeTop(current with { MouLuBuLiyu = paid with { ActualObtained = paid.ActualObtained + (gain ? 1 : 0), CategoryMask = mask } });
                AdvanceEventRulesAndQueueFact(new MouLuBuLiyuObtainedEvent(f.Id, paid.Selections, m.CardId, m.CardKind, m.From, m.To, m.Sequence, gain, mask));
            }, disclosure: from.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
            SealMouLb(f.Id, before); ContinueMouLb(f.Id); return;
        }
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuBranchEvent(f.Id, r.VictimSeat, branch, choice.Targets.SingleOrDefault(-1) is var t && t >= 0 ? t : null, r.CategoryMask));
        if (branch == "duel") { IssueMouLbDuel(f, choice.Targets.Single()); return; }
        if (branch == "wushuang") { GrantMouLbWushuang(f); return; }
        throw new InvalidOperationException("Unknown Liyu choice.");
    }
    private void FinishMouLbTaking(ProgramSkillFrame f)
    {
        var r = f.MouLuBuLiyu!;
        if (r.ActualObtained == 0 || !_players[r.VictimSeat].IsAlive || _winner != Winner.None) { FinishMouLb(f); return; }
        ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r with { Paid = true, Stage = MouLuBuLiyuStage.Drawing }, PendingMovementContinuation = null });
        BeginOperationProgramDraw(f, r.VictimSeat, r.ActualObtained, new(MouLbDrawReason));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouLuBuLiyu)]
    private void ReturnMouLbDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MouLuBuLiyu is not { Stage: MouLuBuLiyuStage.Drawing, Draw: null } r || o.RecipientSeat != r.VictimSeat || o.RequestedCount != r.ActualObtained)
            throw new InvalidOperationException("Liyu lost its true victim native draw receipt.");
        var mask = o.ActualDrawIds.Concat(o.ReplacementGainedIds).Aggregate(r.CategoryMask, (m, id) => m | MouLbCategory(GetAdvancedCard(id).Kind));
        ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r with { Stage = MouLuBuLiyuStage.DrawChildren, Draw = o, CategoryMask = mask, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuDrawReturnedEvent(f.Id, r.VictimSeat, o.RequestedCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, mask, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId));
        SealMouLb(f.Id, o.SequenceBefore); ContinueMouLb(f.Id);
    }
    private void ContinueMouLb(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuBuLiyu: { } r } f || f.Id != id) return;
        if (!ValidMouLb(f)) throw new InvalidOperationException("Liyu lost its accepted original damage and actual invoices.");
        if (r.Stage == MouLuBuLiyuStage.DuelIssued) return;
        if (r.Stage is MouLuBuLiyuStage.ChoosingCard or MouLuBuLiyuStage.VictimChoice) { PublishMouLb(f); return; }
        if (DrainMouLb(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouLuBuLiyu!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (_winner != Winner.None) { FinishMouLb(f); return; }
        if (r.Stage == MouLuBuLiyuStage.ObtainChildren)
        {
            if (!_players[f.OwnerSeat].IsAlive || !_players[r.VictimSeat].IsAlive || r.Selections >= r.DamageAmount ||
                BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.VictimSeat, DamageTargetMountZones, OwnedCardMoveIntent.Obtain).Count == 0) { FinishMouLbTaking(f); return; }
            ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r with { Stage = MouLuBuLiyuStage.ChoosingCard } }); PublishMouLb(f); return;
        }
        if (r.Stage == MouLuBuLiyuStage.DrawChildren && r.CategoryMask == 7 && _players[r.VictimSeat].IsAlive)
        { ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r with { Stage = MouLuBuLiyuStage.VictimChoice } }); PublishMouLb(f); return; }
        FinishMouLb(f);
    }
    private void FinishMouLb(ProgramSkillFrame f)
    {
        var r = f.MouLuBuLiyu!;
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuCompletedEvent(f.Id, r.Selections, r.ActualObtained, r.CategoryMask));
        ReplaceRuntimeTop(f = f with { MouLuBuLiyu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouLuBuLiyu)]
    private PromptChoice ChooseMouLbAi(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.MouLuBuLiyu!.Stage == MouLuBuLiyuStage.ChoosingCard)
            return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "take") ?? p.Choices.Single();
        // The actual victim chooses using public teams and actual legal targets.
        return p.Choices.Where(c => c.Parameters["branch"] == "duel").FirstOrDefault(c =>
            !AreProgramDistributionAllies(_players[p.PlayerSeat], _players[c.Targets.Single()]) && AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]))
            ?? p.Choices.Single(c => c.Parameters["branch"] == "wushuang");
    }
    private void GrantMouLbWushuang(ProgramSkillFrame f)
    {
        var r = f.MouLuBuLiyu!; var recipient = _players[r.VictimSeat]; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouLuBuLiyu = r with { Stage = MouLuBuLiyuStage.GrantChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var grantId = $"mou-liyu:{f.Id}:{recipient.Seat}";
        var instance = GetSkillBindingShard(recipient).ActiveGrants.FirstOrDefault(g => g.SkillId == MouLuBuMobileComposition.Wushuang)?.SkillInstanceId ?? $"mou-liyu-shared:{recipient.Seat}:wushuang";
        recipient.SkillGrants.Grant(new SkillGrant(grantId, MouLuBuMobileComposition.Wushuang, instance, grantId));
        AdvanceEventRulesAndQueueFact(new MouLuBuLiyuGrantEvent(f.Id, r.Source, recipient.Seat, grantId, instance, CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count()));
        ContinueMouLb(f.Id);
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 31600)]
    private void ExpireMouLbGrant(ActualTurnCursorReturnedEvent returned)
    {
        foreach (var issued in ProgramEventHistory<MouLuBuLiyuGrantEvent>().Where(e => e.RecipientSeat == returned.OwnerSeat).ToArray())
        {
            if (ProgramEventHistory<MouLuBuLiyuGrantExpiredEvent>().Any(e => e.GrantId == issued.GrantId)) continue;
            var due = CompleteProgramEventHistory().OfType<TurnStartedEvent>().Skip(issued.AfterTurnStartOrdinal).FirstOrDefault(e => e.ActorSeat == issued.RecipientSeat);
            if (due?.TurnNumber != returned.ActualTurn) continue;
            _players[issued.RecipientSeat].SkillGrants.RemoveGrant(issued.GrantId);
            AdvanceEventRulesAndQueueFact(new MouLuBuLiyuGrantExpiredEvent(issued.GrantId, issued.RecipientSeat, returned.ActualTurn));
        }
    }
}
