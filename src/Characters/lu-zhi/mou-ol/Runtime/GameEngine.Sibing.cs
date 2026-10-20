using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouLzDiscard = "skill-program.mou-ol:sibing.discard", MouLzDraw = "skill-program.mou-ol:liance.draw";
    [ProgramCapability] private sealed class MouLuZhiOlCapability(GameEngine g) : IMouLuZhiOlHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.BeginMouLz(f, op); }
    private CardConversionSource MouLzSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static bool IsMouLzDamageCard(CardKind k) => IsSlashCard(k) || k is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault or CardKind.AgainstCurrent or CardKind.WumiaoFlood or CardKind.Lightning;
    private bool MouLzPressAvailable(int owner) => !ProgramEventHistory<MouLuZhiOlPressUsedEvent>().Any(e => e.Owner == owner && e.Turn == _turnNumber);
    private CardUseFrame? MouLzContextUse(ProgramSkillWindowContext? context) => context?.CardUse is { } c && LifecycleCardUse(c.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } a } use && a.ActionId == c.CardActionId ? use : null;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32900, SkillProgramEffectOp.ResolveMouLuZhiSibingPress, SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation)]
    private bool CanOfferMouLzSibing(ProgramTriggerLegalityContext g)
    {
        if (g.WindowContext is { CardUse: null } context && IsMouLzDirectCandidate(g.Candidate, context)) return true;
        if (MouLzContextUse(g.WindowContext) is not { Action: { } a } use || !IsMouLzDamageCard(a.EffectiveKind)) return false;
        if (g.Trigger.Id == "red-press") return a.ActorSeat == g.OwnerSeat && a.EffectiveDesignatedTargetSeats is [var target] && _players[target].IsAlive && MouLzPressAvailable(g.OwnerSeat) &&
            !ProgramEventHistory<MouLuZhiOlStartedEvent>().Any(e => e.OriginalId == use.Id && e.Op == SkillProgramEffectOp.ResolveMouLuZhiSibingPress && e.Source.OwnerSeat == g.OwnerSeat);
        return use.Step == ResolutionFrameStep.Completed && use.TargetSeats.Contains(g.OwnerSeat) &&
            !ProgramEventHistory<MouLuZhiOlUseDamageEvent>().Any(e => e.OriginalId == use.Id && e.Target == g.OwnerSeat && e.Amount > 0) &&
            !ProgramEventHistory<MouLuZhiOlStartedEvent>().Any(e => e.OriginalId == use.Id && e.Op == SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation && e.Source.OwnerSeat == g.OwnerSeat) && MouLzCards(g.OwnerSeat, false, false).Count > 0 && MouLzSlashTargets(g.OwnerSeat).Length > 0;
    }
    private SkillProgramStepOutcome BeginMouLz(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouLuZhiOl is not null || f.InstructionIndex != 1 || f.WindowContext is not { } w) throw new InvalidOperationException("Lu Zhi cannot repeat its accepted instruction.");
        long original = 0, action = 0; var instruction = 0; var actor = f.OwnerSeat; var target = f.OwnerSeat;
        if (op != SkillProgramEffectOp.ResolveMouLuZhiLiance)
        {
            if (MouLzContextUse(w) is { Action: { } a } use)
            { original = use.Id; action = a.ActionId; actor = a.ActorSeat; target = op == SkillProgramEffectOp.ResolveMouLuZhiSibingPress ? a.EffectiveDesignatedTargetSeats.Single() : f.OwnerSeat; }
            else if (_resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is MouLuZhiOlDirectWindowFrame dw && ExactMouLzDirectWindow(dw) && _resolutionStack.SingleOrDefault(x => x.Id == dw.ParentFrameId) is ProgramSkillFrame { MouLuZhiOlDirectDuel: { } direct })
            { original = direct.FrameId; instruction = direct.Instruction; actor = direct.Actor; target = direct.Target; }
            else throw new InvalidOperationException("Sibing requires its genuine current card Use or exact native direct-Duel instruction.");
        }
        var r = new MouLuZhiOlReceipt(MouLzSource(f), f.GameplayHash, w.ParentFrameId, original, action, actor, target, _turnNumber, op)
        { Stage = op == SkillProgramEffectOp.ResolveMouLuZhiSibingPress ? MouLuZhiOlStage.Red : op == SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation ? MouLuZhiOlStage.Black : MouLuZhiOlStage.Drawing, Before = _movementSequence, After = _movementSequence, BatchId = w.MovementBatch?.Id ?? 0, Paid = op == SkillProgramEffectOp.ResolveMouLuZhiLiance, OriginalInstruction = instruction };
        ReplaceRuntimeTop(f = f with { MouLuZhiOl = r });
        AdvanceEventRulesAndQueueFact(new MouLuZhiOlStartedEvent(f.Id, r.Source, r.GameplayHash, op, original, action, actor, target, r.Turn, r.BatchId));
        if (op == SkillProgramEffectOp.ResolveMouLuZhiLiance) { StartMouLzDraw(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishMouLz(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<Card> MouLzCards(int seat, bool red, bool handOnly) => _cardZones.CardsAt(CardLocation.Hand(seat)).Concat(handOnly ? [] : _cardZones.CardsAt(CardLocation.Equipment(seat)))
        .Where(c => (EffectiveSuit(_players[seat], c) is Suit.Heart or Suit.Diamond) == red && EffectiveSuit(_players[seat], c) != Suit.None && !IsSelfHandCategoryDiscardForbidden(seat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private int[] MouLzSlashTargets(int actor) => !IsValidPlayerSeat(actor) || !_players[actor].IsAlive || HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) ? [] : _players.Where(p => p.IsAlive && p.Seat != actor &&
        CanUseVirtualSlashTarget(_players[actor], p, CardKind.Slash, Suit.None, effectiveColor: null, effectiveRank: 0, physicalCardIds: []) && !IsDirectedCardTargetProhibited(actor, p.Seat, CardKind.Slash) && !IsCardTargetProhibited(p, CardKind.Slash, Suit.None, null)).Select(p => p.Seat).ToArray();
    private PromptChoice MouLzChoice(ProgramSkillFrame f, string branch, string label, int? card = null, int? target = null) => new(new($"mou-lu-zhi.{f.Id}.{branch}.{card ?? target ?? -1}"), label, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = "mou-lu-zhi-ol", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouLzChoices(ProgramSkillFrame f)
    {
        var r = f.MouLuZhiOl!; var list = new List<PromptChoice>();
        if (r.Stage == MouLuZhiOlStage.SlashTarget) list.AddRange(MouLzSlashTargets(f.OwnerSeat).Select(t => MouLzChoice(f, "slash", "对" + _players[t].Name + "使用【杀】", target: t)));
        else if (r.Stage is MouLuZhiOlStage.Red or MouLuZhiOlStage.TargetRed or MouLuZhiOlStage.Black)
        {
            var chooser = r.Stage == MouLuZhiOlStage.TargetRed ? r.TargetSeat : f.OwnerSeat;
            var cards = MouLzCards(chooser, r.Stage != MouLuZhiOlStage.Black, r.Stage == MouLuZhiOlStage.TargetRed);
            list.AddRange(cards.Where(c => r.Stage != MouLuZhiOlStage.TargetRed || r.Selected.Count < r.Required || r.Selected.Contains(c.Id)).Select(c => MouLzChoice(f, "card", (r.Selected.Contains(c.Id) ? "取消：" : "弃置：") + c.DisplayName, c.Id)));
            if (r.Stage == MouLuZhiOlStage.Red || r.Selected.Count == (r.Stage == MouLuZhiOlStage.Black ? 1 : r.Required)) list.Add(MouLzChoice(f, "confirm", "确认弃置" + r.Selected.Count + "张牌"));
            list.Add(MouLzChoice(f, "decline", r.Stage == MouLuZhiOlStage.TargetRed ? "不弃红色手牌，不能响应该牌" : "不发动司兵"));
        }
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishMouLz(ProgramSkillFrame f)
    {
        var r = f.MouLuZhiOl!; var chooser = r.Stage == MouLuZhiOlStage.TargetRed ? r.TargetSeat : f.OwnerSeat;
        if (_winner != Winner.None || !_players[chooser].IsAlive) { FinishMouLz(f); return; }
        var choices = MouLzChoices(f); if (choices.Count == 0) { FinishMouLz(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouLuZhiOlStage.TargetRed ? $"司兵：弃置{r.Required}张红色手牌，或不能响应该牌。" : skill.Description, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-lu-zhi-ol")]
    private void AnswerMouLz(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuZhiOl: { } r } f || !ValidMouLz(f) || _pendingDecision?.PlayerSeat != (r.Stage == MouLuZhiOlStage.TargetRed ? r.TargetSeat : f.OwnerSeat) || !MouLzChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Lu Zhi requires the exact current private chooser, cost and native target.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (branch == "card")
        { var id = choice.Cards.Single(); var next = r.Selected.Contains(id) ? r.Selected.Where(x => x != id).ToArray() : r.Stage == MouLuZhiOlStage.Black ? [id] : r.Selected.Append(id).ToArray(); if (r.Stage == MouLuZhiOlStage.TargetRed && next.Length > r.Required) throw new InvalidOperationException("Sibing does not accept an excessive response cost."); ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { Selected = next } }); PublishMouLz(f); return; }
        if (branch == "decline") { if (r.Stage == MouLuZhiOlStage.TargetRed) BanMouLzResponse(f); FinishMouLz(f); return; }
        if (branch == "slash") { ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { CounterTarget = choice.Targets.Single() } }); IssueMouLzCounter(f); return; }
        var chooser = r.Stage == MouLuZhiOlStage.TargetRed ? r.TargetSeat : f.OwnerSeat;
        var purpose = r.Stage == MouLuZhiOlStage.Red ? "owner-red" : r.Stage == MouLuZhiOlStage.TargetRed ? "target-red" : "black";
        if (r.Stage == MouLuZhiOlStage.Red)
        { if (!MouLzPressAvailable(f.OwnerSeat)) throw new InvalidOperationException("Sibing red pressure is limited once per actual turn."); AdvanceEventRulesAndQueueFact(new MouLuZhiOlPressUsedEvent(f.Id, f.OwnerSeat, r.Turn, r.Selected.Count)); }
        var nextStage = r.Stage == MouLuZhiOlStage.Red ? MouLuZhiOlStage.RedChildren : r.Stage == MouLuZhiOlStage.TargetRed ? MouLuZhiOlStage.TargetRedChildren : MouLuZhiOlStage.BlackChildren;
        ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { Stage = nextStage, Paid = true, Required = r.Stage == MouLuZhiOlStage.Red ? r.Selected.Count : r.Required, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(chooser, 0, null) });
        var before = _movementSequence;
        if (r.Selected.Count > 0) MoveProgramLogicalDiscard(f, chooser, r.Selected, new(MouLzDiscard));
        f = GetActiveProgramFrame(f.Id); r = f.MouLuZhiOl!; var actual = _cardMovements.Count(m => m.Sequence > before && m.Sequence <= _movementSequence && r.Selected.Contains(m.CardId) && m.From.OwnerSeat == chooser && m.Reason.Value == MouLzDiscard);
        if (actual != r.Selected.Count) throw new InvalidOperationException("Sibing must pay exactly its selected physical primary materials.");
        ReplaceRuntimeTop(f with { MouLuZhiOl = r with { After = _movementSequence } }); AdvanceEventRulesAndQueueFact(new MouLuZhiOlDiscardedEvent(f.Id, chooser, purpose, before, _movementSequence, actual)); ContinueMouLz(f.Id);
    }
    private void ContinueMouLz(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuZhiOl: { } r } f || f.Id != id) return;
        if (r.Stage is MouLuZhiOlStage.Drawing or MouLuZhiOlStage.Issuing or MouLuZhiOlStage.NativeUse) return;
        if (r.Stage is MouLuZhiOlStage.RedChildren or MouLuZhiOlStage.TargetRedChildren or MouLuZhiOlStage.BlackChildren or MouLuZhiOlStage.DrawChildren)
        {
            if (DrainMouLz(f)) return; f = GetActiveProgramFrame(id); r = f.MouLuZhiOl!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage == MouLuZhiOlStage.DrawChildren) { if (_winner == Winner.None && HasOngoingWholeActualUseTurn && r.Turn == _turnNumber) AdvanceEventRulesAndQueueFact(new MouLuZhiOlLianceArmedEvent(f.Id, r.Source, r.GameplayHash, r.Turn)); FinishMouLz(f); return; }
            if (r.Stage == MouLuZhiOlStage.TargetRedChildren) { FinishMouLz(f); return; }
            if (r.Stage == MouLuZhiOlStage.RedChildren)
            {
                if (r.Required == 0) { FinishMouLz(f); return; }
                if (!_players[r.TargetSeat].IsAlive) { FinishMouLz(f); return; }
                ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { Stage = MouLuZhiOlStage.TargetRed, Selected = [] } }); PublishMouLz(f); return;
            }
            if (r.Stage == MouLuZhiOlStage.BlackChildren)
            { if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishMouLz(f); return; } ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { Stage = MouLuZhiOlStage.SlashTarget, Selected = [] } }); PublishMouLz(f); return; }
        }
        if (r.Stage == MouLuZhiOlStage.NativeReturned) { FinishMouLz(f); return; }
        PublishMouLz(f);
    }
    private void BanMouLzResponse(ProgramSkillFrame f)
    {
        var r = f.MouLuZhiOl!; var ban = new MouLuZhiOlResponseBan(f.Id, r.Source, r.GameplayHash, r.OriginalId, r.OriginalActionId, r.TargetSeat, r.OriginalInstruction);
        if (r.OriginalActionId > 0) UpdateLifecycleCardUse(r.OriginalId, u => u with { MouLuZhiOlBans = (u.MouLuZhiOlBans ?? []).Append(ban).ToArray() });
        AdvanceEventRulesAndQueueFact(new MouLuZhiOlResponseBannedEvent(ban));
    }
    private void FinishMouLz(ProgramSkillFrame f)
    { var r = f.MouLuZhiOl!; AdvanceEventRulesAndQueueFact(new MouLuZhiOlCompletedEvent(f.Id, r.Op)); ReplaceRuntimeTop(f = f with { MouLuZhiOl = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouLuZhiSibingPress, SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation)]
    private PromptChoice ChooseMouLzAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouLuZhiOl!; var actor = p.PlayerSeat;
        if (r.Stage == MouLuZhiOlStage.SlashTarget) { var view = CreateSnapshot(actor); return p.Choices.OrderByDescending(c => _aiBrains[actor].ScoreProgramTarget(view, c.Targets.Single(), new(0,0,0,0,0,1,false,false))).First(); }
        var need = r.Stage == MouLuZhiOlStage.Red ? 1 : r.Stage == MouLuZhiOlStage.Black ? 1 : r.Required;
        if (r.Stage == MouLuZhiOlStage.TargetRed && MouLzCards(actor, true, true).Count < need || r.Stage == MouLuZhiOlStage.Red && AreProgramDistributionAllies(_players[actor], _players[r.TargetSeat])) return p.Choices.Single(c => c.Parameters["branch"] == "decline");
        if (r.Selected.Count >= need) return p.Choices.First(c => c.Parameters["branch"] == "confirm");
        return p.Choices.Where(c => c.Parameters["branch"] == "card" && !r.Selected.Contains(c.Cards.Single())).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[actor])).FirstOrDefault() ?? p.Choices.Single(c => c.Parameters["branch"] == "decline");
    }
}
