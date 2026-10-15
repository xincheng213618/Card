using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<int, int> _mouZhitianCounts = [];
    private readonly HashSet<int> _mouWujingAllowances = [];
    private const string MouWujingUsage = "first-nonhand-discard";

    private bool HasMouOperation(CharacterState owner, SkillProgramEffectOp op) =>
        EnabledSkillPrograms(owner).Any(program => program.Activations.Any(a =>
            ProgramInstructionResolver.Default.Features(a).HasOperation(op)) || program.Triggers.Any(t =>
            ProgramInstructionResolver.Default.Features(t).HasOperation(op)));
    private bool MouZhijueYang(int seat) => GetProgramBooleanState(seat, "mou:zhijue", GetRuntimeSkillInstanceId(_players[seat], "mou:zhijue"), "yang");
    private int MouZhitianCount(int seat) => _mouZhitianCounts.GetValueOrDefault(seat, 7);
    private IReadOnlyList<Card> MouZhitianTop(int seat) => _cardZones.CardsAt(CardLocation.DrawPile)
        .TakeLast(MouZhitianCount(seat)).Reverse().ToArray();
    private IEnumerable<CardSnapshot> MouZhitianPrivateCards(int viewerSeat) =>
        IsValidPlayerSeat(viewerSeat) && _players[viewerSeat].IsAlive &&
        HasMouOperation(_players[viewerSeat], SkillProgramEffectOp.MouZhitianView) &&
        (_phase == TurnPhase.Play && _currentSeat == viewerSeat ||
         ActiveFireAttack is { SourceSeat: var source, FireAttackSelection.RevealedCardId: not null } && source == viewerSeat)
        ? MouZhitianTop(viewerSeat).Select(ToSnapshot) : [];
    private bool HasMouWujingAllowance(CharacterState owner) => owner.Seat == _turnProgression.OwnerSeat &&
        _mouWujingAllowances.Contains(owner.Seat) && HasMouOperation(owner, SkillProgramEffectOp.MouWujingDraw);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 180)]
    private void ObserveMouZhugeEvent(IGameEvent payload)
    {
        if (payload is TurnStartedEvent) _mouWujingAllowances.Clear();
        if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } responded &&
            responded.Action.ActorSeat == responded.Action.ProviderSeat && responded.Action.RequesterSeat is null &&
            (responded.Action.EffectiveKind == CardKind.Nullification ||
             IsProgramResponseCardUse(_players[responded.Action.ActorSeat], responded.Action.EffectiveKind)))
            _mouWujingAllowances.Remove(responded.Action.ActorSeat);
        if (payload is CardUseDeclaredEvent declared && HasMouWujingAllowance(_players[declared.SourceSeat]))
        { _mouWujingAllowances.Remove(declared.SourceSeat); TryMarkUnlimitedUse(declared.ResolutionId);
            UpdateLifecycleCardUse(declared.ResolutionId, use => use with { QuotaOnlyUseLimitExemption = true }); }
    }
    private bool MouDiscardNotFromUse(CardMovementRecord move)
    {
        if (move.Reason == CardMoveReasons.UseFinished || move.Reason == CardMoveReasons.FireAttackFinished ||
            move.Reason == CardMoveReasons.IronChainFinished || move.Reason == CardMoveReasons.DelayedCardFinish ||
            move.Reason == CardMoveReasons.NullificationFinished) return false;
        if (move.Reason == CardMoveReasons.ResponseFinished && SettledActionContext(move.CardId) is { } action &&
            (action.Type == CardActionType.Use || action.Type == CardActionType.Response &&
                (action.EffectiveKind == CardKind.Nullification || IsProgramResponseCardUse(_players[action.ActorSeat], action.EffectiveKind)))) return false;
        return true;
    }
    private void CaptureMouWujingAllowance(long batchId, IReadOnlyList<CardMovementRecord> movements)
    {
        var seat = _turnProgression.OwnerSeat;
        if (!IsValidPlayerSeat(seat) || !_players[seat].IsAlive ||
            !HasMouOperation(_players[seat], SkillProgramEffectOp.MouWujingDraw) ||
            !movements.Any(move => move.To == CardLocation.DiscardPile && MouDiscardNotFromUse(move))) return;
        _mouWujingAllowances.Add(seat);
        AdvanceEventRulesAndQueueFact(new MouWujingAllowanceGrantedEvent(seat, batchId));
    }
    private int[] MatchingMouWujingIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate) =>
        _skillRuntimeState.GetUsage(candidate.OwnerSeat, candidate.SkillId, MouWujingUsage, SkillUsageScope.Turn) != 0 ? [] :
        batch.Movements.Select((move, index) => (move, index))
            .Where(item => item.move.To == CardLocation.DiscardPile && item.move.From.Zone != CardZoneKind.Hand)
            .Select(item => item.index).ToArray();

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 30)]
    private bool CanStartMouZhuge(CharacterState owner, ProgramExecutionPlan plan)
    {
        if (plan.Features.HasOperation(SkillProgramEffectOp.MouZhitianView)) return owner.IsHuman && MouZhitianTop(owner.Seat).Count > 0;
        if (!plan.Features.HasOperation(SkillProgramEffectOp.MouZhijueUse)) return true;
        if (MouZhijueYang(owner.Seat)) return MouYangOptions(owner).Count > 0;
        return new[] { true, false }.Any(red => GetHand(owner).Any(card => IsRedSuit(EffectiveSuit(owner, card)) == red) &&
            _skillRuntimeState.GetUsage(owner.Seat, "mou:zhijue", "color:" + red, SkillUsageScope.Turn) == 0);
    }
    [ProgramCapability]
    private sealed class MouZhugeProgramCapability(GameEngine engine) : IMouZhugeProgramHost
    {
        public SkillProgramStepOutcome ExecuteMouZhuge(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ExecuteMouZhuge(frame, effect);
    }
    private SkillProgramStepOutcome ExecuteMouZhuge(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (effect.Op == SkillProgramEffectOp.MouWujingDraw)
        {
            if (!_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat, frame.SkillId, MouWujingUsage, SkillUsageScope.Turn, 1))
                return SkillProgramStepOutcome.Continue;
            DrawCards(_players[frame.OwnerSeat], 1, log: true, reason: new("mou.wujing.draw"));
            return SkillProgramStepOutcome.Continue;
        }
        if (effect.Op == SkillProgramEffectOp.MouZhitianView)
        {
            var cards = MouZhitianTop(frame.OwnerSeat);
            PublishMouPrompt(frame, [new(new($"mou-top.{frame.Id}.confirm"), "已观看牌堆顶，继续出牌", [], [],
                new Dictionary<string, string> { ["program-action"] = "mou-zhitian-confirm" })],
                "牌堆顶按从顶至底显示：" + string.Join("、", cards.Select(card =>
                    $"{GetSuitDisplayName(card.Suit)}{card.RankText}【{card.DisplayName}】")));
            return SkillProgramStepOutcome.AwaitChoice;
        }
        ReplaceRuntimeTop(frame = frame with { MouZhijue = new(MouZhijueYang(frame.OwnerSeat) ? "yang" : "color",
            MouZhijueYang(frame.OwnerSeat), [], []) });
        ResumeMouZhijue(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<ProgramOrdinaryTrickUseOption> MouYangOptions(CharacterState owner)
    {
        var top = _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault();
        return top is null ? [] : BuildProgramOrdinaryTrickUseOptions(owner, CardKind.FireAttack, EffectiveSuit(owner, top),
            enforceUsePermission: true, beneficiaryShieldSuit: EffectiveSuit(owner, top),
            actualEffectiveColor: IsRedSuit(EffectiveSuit(owner, top)), hasActualColor: true, physicalCardIds: [top.Id], includeNextActualUseAdjustment: true);
    }
    private IReadOnlyList<LegalAction> MouYinActions(ProgramSkillFrame frame)
    {
        var actor = _players[frame.OwnerSeat]; var result = new List<LegalAction>();
        foreach (var kind in frame.MouZhijue!.CopiedKinds)
        {
            if (IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) continue;
            if (IsSlashCard(kind))
                foreach (var target in _players.Where(target => CanUseVirtualSlashTarget(actor, target, kind,
                    Suit.None, effectiveColor: null, physicalCardIds: [])))
                    result.Add(new(LegalActionKind.Slash, 0, target.Seat, $"视为对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】",
                        kind, TargetSeats: [target.Seat]));
            else if (kind == CardKind.Peach && actor.Hp < actor.MaxHp && !HasSelfCardTargetProhibition(actor.Seat) &&
                !IsCardTargetProhibited(actor, kind, Suit.None))
                result.Add(new(LegalActionKind.Peach, 0, null, "视为使用【桃】", kind));
            else if (kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) &&
                (!actor.UsedPlayPhaseAlcoholThisTurn || HasMouWujingAllowance(actor) || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) ||
                 HasNextUnlimitedCard(actor) || HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, kind)) &&
                !IsCardTargetProhibited(actor, kind, Suit.None))
                result.Add(new(LegalActionKind.Alcohol, 0, null, "视为使用【酒】", kind));
            else if (IsOrdinaryTrick(kind) && kind != CardKind.Nullification)
                foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind, Suit.None,
                    enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null,
                    hasActualColor: true, physicalCardIds: [], includeNextActualUseAdjustment: true))
                    result.Add(new(option.ActionKind, 0, option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,
                        option.Description, kind, option.TargetCardId, option.TargetSeats));
        }
        return result;
    }
    private IReadOnlyList<PromptChoice> MouZhijueChoices(ProgramSkillFrame frame)
    {
        var draft = frame.MouZhijue!; var actor = _players[frame.OwnerSeat]; var choices = new List<PromptChoice>();
        Dictionary<string, string> Params(string option) => new()
        { ["program-action"] = "mou-zhijue", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option };
        if (draft.Stage == "color")
        {
            foreach (var red in new[] { true, false })
            {
                var ids = GetHand(actor).Where(card => IsRedSuit(EffectiveSuit(actor, card)) == red).Select(card => card.Id).Order().ToArray();
                if (ids.Length > 0 && _skillRuntimeState.GetUsage(actor.Seat, frame.SkillId, "color:" + red, SkillUsageScope.Turn) == 0)
                    choices.Add(new(new($"mou-color.{frame.Id}.{red}"), $"将所有{(red ? "红" : "黑")}色手牌（{ids.Length}张）置入弃牌堆", ids, [], Params(red.ToString())));
            }
        }
        else if (draft.Stage == "yang")
            foreach (var (option, index) in MouYangOptions(actor).Select((option, index) => (option, index)))
                choices.Add(new(new($"mou-yang.{frame.Id}.{index}"), option.Description,
                    [_cardZones.CardsAt(CardLocation.DrawPile)[^1].Id], option.TargetSeats, Params(index.ToString(CultureInfo.InvariantCulture))));
        else if (draft.Stage == "copy")
        {
            foreach (var (action, index) in MouYinActions(frame).Select((action, index) => (action, index)))
                choices.Add(new(new($"mou-copy.{frame.Id}.{index}"), action.Description, [], action.TargetSeats,
                    Params(index.ToString(CultureInfo.InvariantCulture))));
            choices.Add(new(new($"mou-copy.{frame.Id}.decline"), "不视为使用牌", [], [], Params("decline")));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouPrompt(ProgramSkillFrame frame, IReadOnlyList<PromptChoice> choices, string text)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, text,
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40)]
    private bool ResumeMouZhijue(ProgramSkillFrame frame)
    {
        if (frame.MouZhijue is not { } draft) return false;
        if (frame.PendingMovementContinuation is not null || _pendingDecision is not null || draft.Stage == "use") return true;
        if (!DeckProgramSourceValid(frame) || _winner != Winner.None)
        { FinishProgramSkill(frame, true); return true; }
        if (draft.Stage == "returned")
        {
            ReplaceRuntimeTop(frame = frame with { MouZhijue = draft with { Stage = "done" } });
            if (!draft.CausedDamage)
            {
                _mouZhitianCounts[frame.OwnerSeat] = Math.Max(1, MouZhitianCount(frame.OwnerSeat) - 1);
                AdvanceEventRulesAndQueueFact(new MouZhijueUndamagedEvent(frame.OwnerSeat, MouZhitianCount(frame.OwnerSeat)));
                DrawCards(_players[frame.OwnerSeat], 2, log: true, reason: new("mou.zhijue.undamaged"));
                AwaitDeckProgramMovement(frame.Id); return true;
            }
        }
        if (frame.MouZhijue!.Stage == "done")
        { ReplaceRuntimeTop(frame with { MouZhijue = null }); AdvanceRuntimeProgram(frame.Id); return true; }
        var choices = MouZhijueChoices(frame);
        if (choices.Count == 0) { FinishProgramSkill(frame, true); return true; }
        PublishMouPrompt(frame, choices, draft.Stage switch
        { "yang" => "智绝·阳：将牌堆顶的一张牌当【火攻】使用，选择目标。", "color" => "智绝·阴：选择一种颜色，置入该颜色的全部手牌。", _ => "智绝·阴：可视为使用刚置入弃牌堆的一张基本或普通锦囊牌。" });
        return true;
    }
    [ProgramAnswer("mou-zhijue", "mou-zhitian-confirm")]
    private void ResolveMouZhugeChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Mou Zhuge lost its program.");
        if (!DeckProgramSourceValid(frame)) throw new InvalidOperationException("Mou Zhuge lost its source instance.");
        if (choice.Parameters.GetValueOrDefault("program-action") == "mou-zhitian-confirm")
        { ClearPendingDecision(); AdvanceRuntimeProgram(frame.Id); return; }
        var draft = frame.MouZhijue ?? throw new InvalidOperationException("Zhijue lost its typed draft.");
        var canonical = MouZhijueChoices(frame).SingleOrDefault(item => item.Id == choice.Id);
        if (canonical is null || !AssistedChoicesEqual([canonical], [choice]))
            throw new InvalidOperationException("Zhijue changed its exact cards or legal targets.");
        var option = choice.Parameters["option"];
        ClearPendingDecision();
        if (draft.Stage == "color")
        {
            var red = bool.Parse(option); var actor = _players[frame.OwnerSeat]; var cards = GetHand(actor).Where(card => IsRedSuit(EffectiveSuit(actor, card)) == red).OrderBy(c => c.Id).ToArray();
            if (!_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat, frame.SkillId, "color:" + red, SkillUsageScope.Turn, 1))
                throw new InvalidOperationException("Zhijue repeated its color this actual turn.");
            SetProgramBooleanState(frame, "yang", true);
            AdvanceEventRulesAndQueueFact(new MouZhijuePolarityChangedEvent(frame.OwnerSeat, true));
            ReplaceRuntimeTop(frame with { MouZhijue = draft with { Stage = "copy",
                PaidCardIds = Array.AsReadOnly(cards.Select(c => c.Id).ToArray()),
                CopiedKinds = Array.AsReadOnly(cards.Where(c => IsOrdinaryTrick(c.Kind) ||
                    MatchesProgramCardCategory(c.Kind, [SkillProgramCardCategory.Basic])).Select(c => c.Kind).Distinct().Order().ToArray()) } });
            MoveCards(cards, CardLocation.Hand(frame.OwnerSeat), CardLocation.DiscardPile, new("mou.zhijue.place"));
            AwaitDeckProgramMovement(frame.Id); return;
        }
        if (option == "decline")
        { ReplaceRuntimeTop(frame with { MouZhijue = draft with { Stage = "returned" } }); AdvanceRuntimeProgram(frame.Id); return; }
        var index = int.Parse(option, CultureInfo.InvariantCulture);
        LegalAction action; ProgramOrdinaryTrickUseOption? trick;
        Card card; IReadOnlyList<int> physicalIds;
        if (draft.Yang)
        {
            trick = MouYangOptions(_players[frame.OwnerSeat])[index]; card = _cardZones.CardsAt(CardLocation.DrawPile)[^1];
            action = new(trick.ActionKind, card.Id, null, trick.Description, trick.EffectiveCardKind, trick.TargetCardId, trick.TargetSeats);
            physicalIds = [card.Id]; SetProgramBooleanState(frame, "yang", false);
            AdvanceEventRulesAndQueueFact(new MouZhijuePolarityChangedEvent(frame.OwnerSeat, false));
        }
        else
        {
            action = MouYinActions(frame)[index]; card = new(0, action.PlayedCardKind!.Value, Suit.None, 0); physicalIds = [];
            trick = IsOrdinaryTrick(card.Kind) ? BuildProgramOrdinaryTrickUseOptions(_players[frame.OwnerSeat], card.Kind, Suit.None,
                enforceUsePermission: true, beneficiaryShieldSuit: Suit.None, actualEffectiveColor: null, hasActualColor: true,
                physicalCardIds: [], includeNextActualUseAdjustment: true).Single(item => item.ActionKind == action.Kind &&
                    item.TargetCardId == action.TargetCardId && item.TargetSeats.SequenceEqual(action.TargetSeats)) : null;
        }
        IssueMouZhijueUse(frame, action, trick, card, physicalIds);
    }
    private void IssueMouZhijueUse(ProgramSkillFrame frame, LegalAction action, ProgramOrdinaryTrickUseOption? trick,
        Card card, IReadOnlyList<int> physicalIds)
    {
        var actor = _players[frame.OwnerSeat]; var kind = action.PlayedCardKind!.Value;
        if (trick?.NextActualUseAdjusted == true) _selectedNextCardTargetSeats = action.TargetSeats;
        var id = BeginCardUse(card, actor.Seat, action.TargetSeats, kind, physicalCardIds: physicalIds,
            conversionSource: frame.MouZhijue!.Yang ? new(frame.SkillId, frame.ActivationId, frame.OwnerSeat, frame.SkillInstanceId) : null,
            designatedTargetSeats: kind == CardKind.BorrowedSword ? action.TargetSeats.Take(1).ToArray() : null);
        var use = LifecycleCardUse(id)!;
        var ret = new MouZhijueUseReturn(frame.Id, frame.InstructionIndex, frame.OwnerSeat, frame.SkillId,
            frame.SkillInstanceId, frame.GameplayHash, _turnNumber, frame.MouZhijue!.Yang, kind, card.Id, use.Action!.ActionId);
        ReplaceRuntimeFrame(frame.Id, frame with { MouZhijue = frame.MouZhijue with { Stage = "use", UseId = id } });
        UpdateLifecycleCardUse(id, current => current with { MouZhijueReturn = ret });
        if (physicalIds.Count > 0) MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, CardMoveReasons.Use);
        if (kind == CardKind.Peach) { BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Recovery)); return; }
        if (kind == CardKind.Alcohol) { BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return; }
        if (IsSlashCard(kind))
        {
            var target = _players[action.TargetSeats[0]]; var damage = 1 + actor.AlcoholDamageBonus;
            CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
            if (LifecycleCardUse(id)?.UnlimitedUse != true) RecordSlashUseDebit(id, actor.Seat);
            var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card: null,
                damageAmount: damage, playedCardKind: kind, programSkillCardUseFrameId: frame.Id,
                ignoresArmor: HasCardArmorBypass(actor, target, kind));
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind);
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actor.Seat, target.Seat));
            if (!TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted,
                action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
            return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, use.TargetSeats, action.Kind,
            action.TargetCardId, trick?.RequiredCardKind, kind);
    }
    private bool IsMouZhijueUse(long id) => LifecycleCardUse(id) is { MouZhijueReturn: { } ret } use && ValidMouZhijueUse(use, ret);
    private bool IsMouZhijueZeroUse(long id) => LifecycleCardUse(id) is { CardId: 0, MouZhijueReturn: { Yang: false } } && IsMouZhijueUse(id);
    private bool ValidMouZhijueUse(CardUseFrame use, MouZhijueUseReturn ret)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == use.Id);
        return index > 0 && _resolutionStack[index - 1] is ProgramSkillFrame frame &&
            frame.Id == ret.ProgramFrameId && frame.InstructionIndex == ret.InstructionIndex &&
            frame.OwnerSeat == ret.OwnerSeat && frame.SkillId == ret.SkillId && frame.SkillInstanceId == ret.SkillInstanceId &&
            frame.GameplayHash == ret.GameplayHash && frame.MouZhijue is { Stage: "use" } draft &&
            draft.UseId == use.Id && draft.Yang == ret.Yang && use.CardId == ret.PhysicalCardId && use.CardKind == ret.Kind &&
            use.Action is { Type: CardActionType.Use } action && action.ActionId == ret.ActionId &&
            action.ActorSeat == ret.OwnerSeat && action.ProviderSeat == ret.OwnerSeat &&
            (ret.Yang ? use.PhysicalCardIds is [var paid] && paid == ret.PhysicalCardId &&
                action.PhysicalCards is [var cost] && cost.CardId == paid && cost.From == CardLocation.DrawPile && ret.Kind == CardKind.FireAttack :
                use.PhysicalCardIds is { Count: 0 } && action.PhysicalCards.Count == 0 && draft.CopiedKinds.Contains(ret.Kind));
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 20)]
    private bool CompleteMouZhijueNativeAttackReturn()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhijue.Stage: "returned" } parent) return false;
        AdvanceRuntimeProgram(parent.Id);
        return true;
    }
    [NativeUseReturn(300)]
    private void ReturnMouZhijueUse(CardUseFrame use)
    {
        if (use.MouZhijueReturn is not { } ret) return;
        var frame = GetActiveProgramFrame(ret.ProgramFrameId);
        if (frame.MouZhijue is not { Stage: "use" } draft || draft.UseId != use.Id || frame.InstructionIndex != ret.InstructionIndex)
            throw new InvalidOperationException("Zhijue lost its one-shot typed card-use return.");
        ReplaceRuntimeFrame(frame.Id, frame with { MouZhijue = draft with { Stage = "returned", CausedDamage = use.CausedDamage } });
        if (use.CardAttack is not { Active: true }) AdvanceRuntimeProgram(frame.Id);
    }
    private bool ContinueMouZhijueSimpleUse(long id, ProgramSimpleCardContinuation continuation)
    {
        if (!IsMouZhijueZeroUse(id)) return false;
        var use = LifecycleCardUse(id)!; var actor = _players[use.SourceSeat]; var card = new Card(0, use.CardKind, Suit.None, 0);
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (continuation.Effect == SimpleCardUseEffect.Recovery)
            CompleteRecoveryCardUse(actor, actor, card, id, use.CardKind, continuation.RecoveryAmount, continuation.RecoveryPolicySources ?? []);
        else CompleteAlcoholUse(actor, card, id);
        return true;
    }
    private void AssertMouZhijueProgram(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.MouZhijue is not { } draft) return;
        if (paused.Op != SkillProgramEffectOp.MouZhijueUse || draft.Stage is not ("yang" or "color" or "copy" or "use" or "returned" or "done") ||
            draft.Stage == "use" != (draft.UseId is not null && LifecycleCardUse(draft.UseId.Value) is not null) ||
            draft.PaidCardIds.Distinct().Count() != draft.PaidCardIds.Count)
            throw new InvalidOperationException("Zhijue lost its exact staged payment/use draft.");
        if (_resolutionStack.LastOrDefault()?.Id == frame.Id && frame.PendingMovementContinuation is null &&
            draft.Stage is "yang" or "color" or "copy" && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
                prompt.PlayerSeat != frame.OwnerSeat || !AssistedChoicesEqual(prompt.Choices, MouZhijueChoices(frame))))
            throw new InvalidOperationException("Zhijue lost its exact private choice.");
    }
}
