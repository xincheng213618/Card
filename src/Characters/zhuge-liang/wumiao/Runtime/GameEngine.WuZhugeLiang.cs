using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuZglDraw = "skill-program.wumiao:zhuge-liang.draw", WuZglCopy = "skill-program.wumiao:zhizhe.copy";
    private bool HasWuZhugeLiangCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeWuZhugeLiangJincui) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuZhugeLiangQingshi) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWuZhugeLiangZhizhe);
    private static CardConversionSource WuZglSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool WuZglTurnOpen(int turn, int owner) => turn == _turnNumber && owner == _currentSeat && !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == turn && e.OwnerSeat == owner);
    private CardActionContext? WuZglAction(ProgramSkillWindowContext c) => c.CardUse is { } id
        ? _resolutionStack.OfType<CardUseFrame>().Select(f => f.Action).FirstOrDefault(a => a?.ActionId == id.CardActionId) ??
            ProgramEventHistory<CardActionAcceptedEvent>().LastOrDefault(e => e.Action.ActionId == id.CardActionId)?.Action : null;
    private bool CanWuZglQingshi(int owner, ProgramSkillWindowContext c)
    {
        var action = WuZglAction(c);
        return c.Window == SkillProgramTriggerWindow.CardUseCommitted && _phase == TurnPhase.Play && owner == _currentSeat && WuZglTurnOpen(_turnNumber, _currentSeat) &&
            action is not null && action.ActorSeat == owner && (action.Type == CardActionType.Use || action.ProviderSeat == owner && action.RequesterSeat is null && IsProgramResponseCardUse(_players[owner], action.EffectiveKind)) &&
            !ProgramEventHistory<WuZhugeLiangQingshiDisabledEvent>().Any(e => e.OwnerSeat == owner && e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat) &&
            !ProgramEventHistory<WuZhugeLiangQingshiAcceptedEvent>().Any(e => e.Source.OwnerSeat == owner && e.ActualTurn == _turnNumber && e.TurnOwner == _currentSeat && e.Name == ProgramBasicCardName(action.EffectiveKind)) &&
            GetHand(_players[owner]).Any(card => !action.PhysicalCards.Any(m => m.CardId == card.Id) && ProgramBasicCardName(AdvancedEffectiveHandKind(_players[owner], card)) == ProgramBasicCardName(action.EffectiveKind));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30300, SkillProgramEffectOp.InitializeWuZhugeLiangJincui, SkillProgramEffectOp.ResolveWuZhugeLiangJincui, SkillProgramEffectOp.ResolveWuZhugeLiangQingshi)]
    private bool CanOfferWuZgl(ProgramTriggerCandidate candidate, SkillProgramTrigger t, ProgramSkillWindowContext c) => t.Effects[0].Op switch
    {
        SkillProgramEffectOp.InitializeWuZhugeLiangJincui => c.Window == SkillProgramTriggerWindow.GameStarting && !ProgramEventHistory<WuZhugeLiangStartedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.Operation == SkillProgramEffectOp.InitializeWuZhugeLiangJincui),
        SkillProgramEffectOp.ResolveWuZhugeLiangJincui => c.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Preparation } p && p.ActorSeat == candidate.OwnerSeat,
        _ => CanWuZglQingshi(candidate.OwnerSeat, c)
    };
    [ProgramCapability] private sealed class WuZhugeLiangCapability(GameEngine g) : IWuZhugeLiangHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.BeginWuZgl(f, op); }
    private SkillProgramStepOutcome BeginWuZgl(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WuZhugeLiang is not null || f.InstructionIndex != 1 || GetPausedPrivateOfferEffect(f)?.Op != op) throw new InvalidOperationException("Wu Zhuge Liang lost his original operation.");
        var context = f.WindowContext; var action = context is null ? null : WuZglAction(context);
        var use = action is null ? null : _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId);
        var r = new WuZhugeLiangReceipt(WuZglSource(f), f.GameplayHash, op, _turnNumber, _currentSeat, context?.ParentFrameId ?? 0, action?.ActionId ?? 0, use?.Id ?? 0)
            { Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { WuZhugeLiang = r });
        AdvanceEventRulesAndQueueFact(new WuZhugeLiangStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ActualTurn, r.TurnOwner, r.WindowId, r.ActionId, r.UseId));
        if (op == SkillProgramEffectOp.InitializeWuZhugeLiangJincui)
        { StartWuZglDraw(f, f.OwnerSeat, Math.Max(0, 7 - GetHand(_players[f.OwnerSeat]).Count)); return SkillProgramStepOutcome.AwaitChild; }
        if (op == SkillProgramEffectOp.ResolveWuZhugeLiangJincui)
        {
            var owner = _players[f.OwnerSeat]; var sevens = _cardZones.CardsAt(CardLocation.DrawPile).Count(c => c.Rank == 7);
            var before = owner.Hp; var after = Math.Min(owner.MaxHp, Math.Max(1, sevens));
            ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Paid = true, Stage = WuZhugeLiangStage.HealthChildren, HpBefore = before, HpAfter = after, SevenCount = sevens }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            owner.Hp = after; var change = RecordHpChange(f.Id, f.OwnerSeat, f.OwnerSeat, before, after, HpChangeKind.Setting);
            ReplaceRuntimeTop(f = f with { WuZhugeLiang = f.WuZhugeLiang! with { HpChangeId = change?.Id } });
            AdvanceEventRulesAndQueueFact(new WuZhugeLiangHpSetEvent(f.Id, f.OwnerSeat, before, after, sevens, change?.Id));
            ContinueWuZgl(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.ResolveWuZhugeLiangQingshi)
        {
            if (context is null || action is null || !CanWuZglQingshi(f.OwnerSeat, context)) { FinishWuZgl(f); return SkillProgramStepOutcome.AwaitChild; }
            ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Paid = true, Name = ProgramBasicCardName(action.EffectiveKind), Stage = WuZhugeLiangStage.Choice } });
            AdvanceEventRulesAndQueueFact(new WuZhugeLiangQingshiAcceptedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwner, r.ActionId, ProgramBasicCardName(action.EffectiveKind)));
            PublishWuZgl(f); return SkillProgramStepOutcome.AwaitChoice;
        }
        if (f.SkillId != WuZhugeLiangComposition.Zhizhe || f.ActivationId != "copy-hand" || f.TriggerId is not null || f.SelectedCardIds is not [var originalId] ||
            _cardZones.GetLocation(originalId) != CardLocation.Hand(f.OwnerSeat)) throw new InvalidOperationException("Zhizhe needs one real selected Hand card.");
        var original = GetHand(_players[f.OwnerSeat]).Single(c => c.Id == originalId);
        var newId = checked(CreateCardZoneDiagnostics().Max(c => c.CardId) + 1);
        var copy = original with { Id = newId, GrantedSkillIds = Array.AsReadOnly(original.GrantedSkillIds.ToArray()) };
        _cardZones.AddGeneratedCard(copy); _generatedPhysicalCardIds.Add(newId);
        ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Paid = true, CreatedCardId = newId, Stage = WuZhugeLiangStage.CopyChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuZhugeLiangCopyCreatedEvent(f.Id, r.Source, r.GameplayHash, originalId, newId, copy.Kind, copy.Suit, copy.Rank));
        MoveCard(copy, CardLocation.OutsideGame, CardLocation.Hand(f.OwnerSeat), new(WuZglCopy), disclosure: CardMovementDisclosure.FaceDown);
        SealWuZgl(f.Id, r.Before); ContinueWuZgl(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice WuZglChoice(ProgramSkillFrame f, string key, string label, int? target = null) => new(new($"wu-zhuge-liang.{f.Id}.{key}.{target ?? -1}"), label, [], target is { } seat ? [seat] : [],
        new Dictionary<string, string> { ["program-action"] = "wu-zhuge-liang", ["branch"] = key, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> WuZglChoices(ProgramSkillFrame f)
    {
        var r = f.WuZhugeLiang!; var choices = new List<PromptChoice>();
        if (r.Stage == WuZhugeLiangStage.DrawTargets)
        {
            choices.AddRange(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => WuZglChoice(f, "toggle", (r.Targets.Contains(p.Seat) ? "取消" : "选择") + p.Name, p.Seat)));
            choices.Add(WuZglChoice(f, "finish", $"确认{r.Targets.Count}名角色各摸一张")); return Array.AsReadOnly(choices.ToArray());
        }
        if (r.UseId > 0 && LifecycleCardUse(r.UseId) is { Action: { } a } use && a.ActionId == r.ActionId)
            choices.AddRange(use.TargetSeats.Distinct().Where(i => _players[i].IsAlive).Select(i => WuZglChoice(f, "damage", $"此牌对{_players[i].Name}伤害+1", i)));
        choices.Add(WuZglChoice(f, "others", "令任意名其他角色各摸一张牌"));
        choices.Add(WuZglChoice(f, "draw-three", "摸三张牌，然后本回合情势失效")); return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishWuZgl(ProgramSkillFrame f)
    {
        var choices = WuZglChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Description, [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("wu-zhuge-liang")]
    private void AnswerWuZgl(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.WuZhugeLiang!;
        if (!ValidWuZgl(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([selected], WuZglChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Qingshi requires its exact published owning choice.");
        ClearPendingDecision(); if (!_players[f.OwnerSeat].IsAlive) { FinishWuZgl(f); return; }
        var branch = selected.Parameters["branch"];
        if (branch == "toggle")
        {
            var seat = selected.Targets.Single(); var list = r.Targets.Contains(seat) ? r.Targets.Where(t => t != seat).ToArray() : r.Targets.Append(seat).ToArray();
            ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Targets = Array.AsReadOnly(list) } }); PublishWuZgl(f); return;
        }
        if (branch == "others") { ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Stage = WuZhugeLiangStage.DrawTargets, Branch = branch } }); PublishWuZgl(f); return; }
        AdvanceEventRulesAndQueueFact(new WuZhugeLiangQingshiBranchEvent(f.Id, branch));
        if (branch == "damage")
        { AdvanceEventRulesAndQueueFact(new WuZhugeLiangQingshiDamageEvent(f.Id, r.Source, r.GameplayHash, r.ActionId, r.UseId, selected.Targets.Single())); FinishWuZgl(f); return; }
        if (branch == "draw-three")
        { ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Branch = branch } }); StartWuZglDraw(f, f.OwnerSeat, 3); return; }
        for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new WuZhugeLiangQingshiTargetEvent(f.Id, i, r.Targets[i]));
        ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Branch = "others", Stage = WuZhugeLiangStage.DrawChildren, Cursor = 0 }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueWuZgl(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuZhugeLiangQingshi)]
    private PromptChoice AiWuZgl(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.WuZhugeLiang?.Stage != WuZhugeLiangStage.DrawTargets) return p.Choices.Single(c => c.Parameters["branch"] == "draw-three");
        var selected = f.WuZhugeLiang.Targets;
        return p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "toggle" && !selected.Contains(c.Targets.Single()) &&
            _aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat), c.Targets.Single(), new(1, 0, 0, 0, 0, 0, true, false)) > 0) ?? p.Choices.Single(c => c.Parameters["branch"] == "finish");
    }
    private void StartWuZglDraw(ProgramSkillFrame f, int recipient, int count)
    {
        if (count <= 0) { FinishWuZgl(f); return; }
        ReplaceRuntimeTop(f = f with { WuZhugeLiang = f.WuZhugeLiang! with { Paid = true, Stage = WuZhugeLiangStage.Drawing, Recipient = recipient, RequestedDraw = count, Draw = null }, PendingMovementContinuation = null });
        BeginOperationProgramDraw(f, recipient, count, new(WuZglDraw));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.InitializeWuZhugeLiangJincui, SkillProgramEffectOp.ResolveWuZhugeLiangQingshi)]
    private void ReturnWuZglDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        var r = f.WuZhugeLiang!;
        if (!ValidWuZgl(f) || r.Stage != WuZhugeLiangStage.Drawing || o.RequestedCount != r.RequestedDraw || o.RecipientSeat != r.Recipient) throw new InvalidOperationException("Wu Zhuge Liang returned another native draw request.");
        ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Draw = o, Stage = WuZhugeLiangStage.DrawChildren, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuZhugeLiangDrawReturnedEvent(f.Id, o.RecipientSeat, o.RequestedCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter));
        SealWuZgl(f.Id, o.SequenceBefore); ContinueWuZgl(f.Id);
    }
    private void ContinueWuZgl(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuZhugeLiang: { } r } f || f.Id != id) return;
        if (!ValidWuZgl(f)) throw new InvalidOperationException("Wu Zhuge Liang lost his issued native receipt.");
        if (r.Stage is WuZhugeLiangStage.Choice or WuZhugeLiangStage.DrawTargets or WuZhugeLiangStage.Drawing) return;
        if (DrainWuZgl(f)) return;
        f = GetActiveProgramFrame(id); r = f.WuZhugeLiang!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive && !(r.Operation == SkillProgramEffectOp.ResolveWuZhugeLiangQingshi && r.Branch == "others")) { FinishWuZgl(f); return; }
        if (r.Stage == WuZhugeLiangStage.HealthChildren)
        {
            var count = Math.Max(0, _players[f.OwnerSeat].Hp); AdvanceEventRulesAndQueueFact(new WuZhugeLiangOrderingIssuedEvent(f.Id, f.OwnerSeat, count));
            ReplaceRuntimeTop(f = f with { WuZhugeLiang = null, PendingMovementContinuation = null });
            if (BeginProgramTopReorder(f, count, null) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (r.Operation == SkillProgramEffectOp.ResolveWuZhugeLiangQingshi && r.Branch == "others")
        {
            var cursor = r.Cursor;
            if (r.Draw is not null) cursor++;
            while (cursor < r.Targets.Count && !_players[r.Targets[cursor]].IsAlive) cursor++;
            if (cursor < r.Targets.Count)
            { ReplaceRuntimeTop(f = f with { WuZhugeLiang = r with { Cursor = cursor, Draw = null } }); StartWuZglDraw(f, r.Targets[cursor], 1); return; }
        }
        if (r.Operation == SkillProgramEffectOp.ResolveWuZhugeLiangQingshi && r.Branch == "draw-three")
            AdvanceEventRulesAndQueueFact(new WuZhugeLiangQingshiDisabledEvent(f.Id, f.OwnerSeat, r.ActualTurn, r.TurnOwner));
        FinishWuZgl(f);
    }
    private void FinishWuZgl(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new WuZhugeLiangCompletedEvent(f.Id, f.WuZhugeLiang!.Operation));
        ReplaceRuntimeTop(f = f with { WuZhugeLiang = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    private CardSnapshot[] WuZglPrivateTopCards(int viewer) => _resolutionStack.LastOrDefault() is ProgramSkillFrame { SkillId: WuZhugeLiangComposition.Jincui, TopReorder: { } order } f &&
        f.OwnerSeat == viewer && _pendingDecision is { Kind: DecisionKind.ProgramTopReorder, PlayerSeat: var seat } && seat == viewer
        ? order.ViewedCardIds.Select(id => ToSnapshot(GetAdvancedCard(id))).ToArray() : [];
    private IEnumerable<(CardUseEffectSource Source, int Amount)> WuZglQingshiDamage(IDamageAttempt attack) => attack.IsChainPropagation ? [] :
        ProgramEventHistory<WuZhugeLiangQingshiDamageEvent>().Where(e => e.UseId == attack.ResolutionId && e.TargetSeat == attack.TargetSeat &&
            LifecycleCardUse(e.UseId)?.Action is { } a && a.ActionId == e.ActionId && a.ActorSeat == e.Source.OwnerSeat && attack.CardUserSeat == a.ActorSeat)
        .Select(e => (new CardUseEffectSource(e.Source.SkillId, e.Source.BindingId, e.Source.OwnerSeat, e.Source.SkillInstanceId), 1));
}
