using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasFriendYangmingQuota(CharacterState owner) => _resolutionStack.LastOrDefault() is
        ProgramSkillFrame { FriendPangTong.Stage: FriendPangTongStage.Use } frame && frame.OwnerSeat == owner.Seat;
    private static CardLocation FriendYangmingLocation(ProgramSkillFrame frame) => new(CardZoneKind.PublicPersistentPile,
        frame.OwnerSeat, "friend.yangming." + frame.Id.ToString(CultureInfo.InvariantCulture));
    private int FriendActualPlayHandLossCount(int seat) => EventsSinceLastBoundary(payload =>
        payload is PhaseChangedEvent phase && phase.Phase == TurnPhase.Play && phase.ActorSeat == seat)
        .OfType<CardMovedEvent>().Count(move => move.From == CardLocation.Hand(seat) && move.To != move.From);
    private IReadOnlyList<int> FriendGainCardIds(ProgramSkillWindowContext context, int seat) => context.MovementBatch is { } batch
        ? batch.NativeDrawInvocation is { } draw && draw.OwnerSeat == seat && ValidNativeDrawInvocation(draw)
            ? draw.Materials.Select(material => material.CardId).ToArray() : batch.Movements.Where(move => move.To == CardLocation.Hand(seat) && move.From != move.To)
            .Select(move => move.CardId).Distinct().ToArray() : [];
    private int FriendManjuanMovedCount(CardMovementBatchContext batch, int seat, SkillProgramTrigger trigger, int fallback) =>
        trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ExchangeFriendGainForDiscard) &&
        batch.NativeDrawInvocation is { } draw && draw.OwnerSeat == seat && ValidNativeDrawInvocation(draw)
            ? draw.ActualCount : fallback;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 670)]
    private bool CanOfferFriendPangTong(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect]) return true;
        if (effect.Op == SkillProgramEffectOp.ExchangeFriendGainForDiscard)
            return context.MovementBatch is { } batch && batch.OriginSkillId != candidate.SkillId &&
                FriendGainCardIds(context, candidate.OwnerSeat).Count >= 2 &&
                FriendGainCardIds(context, candidate.OwnerSeat).Any(id => _cardZones.GetLocation(id) == CardLocation.Hand(candidate.OwnerSeat));
        if (effect.Op == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd)
            return context.Window == SkillProgramTriggerWindow.PlayEnding && context.TargetSeat == candidate.OwnerSeat &&
                FriendActualPlayHandLossCount(candidate.OwnerSeat) >= 3;
        return true;
    }
    private SkillProgramStepOutcome BeginFriendManjuan(ProgramSkillFrame frame)
    {
        var cards = FriendGainCardIds(frame.WindowContext!, frame.OwnerSeat)
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat)).ToArray();
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = new(frame.InstructionIndex,
            SkillProgramEffectOp.ExchangeFriendGainForDiscard, FriendPangTongStage.Select, cards, [], []) });
        PublishFriendPangTongChoice(frame); return SkillProgramStepOutcome.AwaitChoice;
    }
    private SkillProgramStepOutcome BeginFriendYangming(ProgramSkillFrame frame)
    {
        var suits = EventsSinceLastBoundary(payload => payload is TurnStartedEvent).OfType<CardMovedEvent>()
            .Where(move => move.To == CardLocation.DiscardPile && move.From != CardLocation.DiscardPile)
            .Select(move => GetAttackCard(move.CardId).Suit).Where(suit => suit != Suit.None).Distinct().Count();
        var count = suits + (HasRuntimeSkill(_players[frame.OwnerSeat], "friend:gongli-pang-tong") &&
            HasFriendTrioAlly(_players[frame.OwnerSeat], "zhuge-liang") ? 1 : 0);
        if (count > 0) EnsureDrawPile();
        var cards = _cardZones.CardsAt(CardLocation.DrawPile).TakeLast(count).Reverse().ToArray();
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = new(frame.InstructionIndex,
            SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd, FriendPangTongStage.Placement, cards.Select(card => card.Id).ToArray(), [], []) });
        _cardZones.EnsurePublicPersistentPile(FriendYangmingLocation(frame));
        AdvanceEventRulesAndQueueFact(new FriendYangmingStoredEvent(frame.Id, frame.OwnerSeat, cards.Length));
        FriendPangTongMutation(frame, FriendPangTongStage.Use, () =>
            MoveCards(cards, CardLocation.DrawPile, FriendYangmingLocation(frame), new("friend.yangming.place")));
        return SkillProgramStepOutcome.AwaitChild;
    }
    // Yangming is an original-card producer, not a view-as conversion or Shuliang access.
    // Its exact live Use-stage parent and original Deck-to-frame-pile entry authorize admission.
    private bool IsFriendYangmingOriginalUseFrame(ProgramSkillFrame frame, CharacterState owner, Card card, CardKind kind)
    {
        if (card.Id <= 0 || card.Kind != kind || frame.OwnerSeat != owner.Seat || !DeckProgramSourceValid(frame) ||
            frame.FriendPangTong is not { Operation: SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd,
                Stage: FriendPangTongStage.Use, UseId: null } receipt || receipt.InstructionIndex != frame.InstructionIndex ||
            !receipt.Cards.Contains(card.Id) || receipt.UsedSuits.Contains(card.Suit) ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            _cardZones.GetLocation(card.Id) != FriendYangmingLocation(frame)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, program).GetPausedInstruction(frame.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd &&
            ProgramEventHistory<FriendYangmingStoredEvent>().Count(e => e.ProgramFrameId == frame.Id &&
                e.OwnerSeat == owner.Seat && e.Count == receipt.Cards.Count) == 1 &&
            _cardMovements.Count(move => move.CardId == card.Id && move.CardKind == card.Kind &&
                move.From == CardLocation.DrawPile && move.To == FriendYangmingLocation(frame) &&
                move.Reason.Value == "friend.yangming.place") == 1;
    }
    private bool IsFriendYangmingOriginalUseChoice(CharacterState owner, Card card, CardKind kind) =>
        _resolutionStack.LastOrDefault() is ProgramSkillFrame frame && IsFriendYangmingOriginalUseFrame(frame, owner, card, kind);
    private bool IsFriendYangmingOriginalUseParent(CardUseFrame use)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (index != _resolutionStack.Count - 1 || index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost], ConversionChain.Count: 0 } action ||
            action.ActorSeat != parent.OwnerSeat || action.ProviderSeat != parent.OwnerSeat || action.RequesterSeat is not null ||
            use.SourceSeat != parent.OwnerSeat || use.CardId != cost.CardId || use.CardKind != cost.CardKind ||
            action.EffectiveKind != cost.CardKind || cost.From != FriendYangmingLocation(parent) ||
            use.PhysicalCardIds is not [var physicalId] || physicalId != cost.CardId) return false;
        return IsFriendYangmingOriginalUseFrame(parent, _players[parent.OwnerSeat], GetAttackCard(cost.CardId), cost.CardKind);
    }

    private IReadOnlyList<LegalAction> FriendYangmingActions(ProgramSkillFrame frame)
    {
        var receipt = frame.FriendPangTong!; var actor = _players[frame.OwnerSeat]; var actions = new List<LegalAction>();
        if (!actor.IsAlive || _winner != Winner.None) return [];
        foreach (var card in _cardZones.CardsAt(FriendYangmingLocation(frame)).Where(card =>
            !receipt.UsedSuits.Contains(card.Suit) && receipt.Cards.Contains(card.Id)))
        {
            var kind = card.Kind; var suit = EffectiveSuit(actor, card); var red = SuitColor(suit);
            if (IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) || IsResponseEntityRestricted(actor.Seat, card.Id)) continue;
            if (IsSlashCard(kind))
                foreach (var target in _players.Where(target => CanUseSlashTarget(actor, target, card, effectiveKind: kind,
                    physicalCardIds: [card.Id])))
                    actions.Add(new(LegalActionKind.Slash, card.Id, target.Seat, $"使用【{card.DisplayName}】对 {target.Name}", kind, TargetSeats: [target.Seat]));
            else if (kind == CardKind.Peach && IsRuleWounded(actor) && !HasSelfCardTargetProhibition(actor.Seat) &&
                !IsCardTargetProhibited(actor, kind, suit, red))
                actions.Add(new(LegalActionKind.Peach, card.Id, null, "使用【桃】", kind));
            else if (kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) &&
                !IsCardTargetProhibited(actor, kind, suit, red))
                actions.Add(new(LegalActionKind.Alcohol, card.Id, null, "使用【酒】（无次数限制）", kind));
            else if (EquipmentCatalog.IsEquipment(kind) && actor.EquipmentSlotCapacity(EquipmentCatalog.Get(kind).Slot) > 0 &&
                !IsSelfTargetForbiddenAction(actor, kind, [actor.Seat]))
                actions.Add(new(LegalActionKind.Equip, card.Id, null, $"使用【{card.DisplayName}】", kind));
            else if (IsOrdinaryTrick(kind) && kind != CardKind.Nullification)
                foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind, suit, enforceUsePermission: true,
                    beneficiaryShieldSuit: suit, actualEffectiveColor: red, hasActualColor: true, physicalCardIds: [card.Id], includeNextActualUseAdjustment: true))
                    actions.Add(new(option.ActionKind, card.Id, option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,
                        $"使用【{card.DisplayName}】：{option.Description}", kind, option.TargetCardId, option.TargetSeats));
            else if (kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning)
                foreach (var target in _players.Where(target => target.IsAlive && (kind == CardKind.Lightning ? target.Seat == actor.Seat : target.Seat != actor.Seat) &&
                    !target.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(target, kind) &&
                    !IsCardTargetProhibited(target, kind, suit, red) && !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) &&
                    (kind != CardKind.SupplyShortage || GetSeatDistance(actor.Seat, target.Seat) <= 1 || HasCardDistanceExemption(actor, target, kind))))
                    actions.Add(new(kind switch { CardKind.Indulgence => LegalActionKind.Indulgence,
                        CardKind.SupplyShortage => LegalActionKind.SupplyShortage, _ => LegalActionKind.Lightning },
                        card.Id, target.Seat, $"对 {target.Name} 使用【{card.DisplayName}】", kind, TargetSeats: [target.Seat]));
        }
        return actions;
    }
    private IReadOnlyList<PromptChoice> FriendPangTongChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.FriendPangTong!; var choices = new List<PromptChoice>();
        Dictionary<string,string> Parameters(string value) => new()
        { ["program-action"] = "friend-pang-tong", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["value"] = value };
        if (receipt.Stage == FriendPangTongStage.Select)
        {
            foreach (var id in receipt.Cards.Except(receipt.Selected).Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat)))
            {
                var card = GetAttackCard(id);
                choices.Add(new(new($"friend-manjuan.{frame.Id}.{receipt.Selected.Count}.{id}"),
                    $"选择【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)}{card.RankText}），按选择顺序从顶至底放置", [id], [], Parameters(id.ToString(CultureInfo.InvariantCulture))));
            }
            choices.Add(new(new($"friend-manjuan.{frame.Id}.finish"), receipt.Selected.Count == 0 ? "不放置牌" : $"放置已选 {receipt.Selected.Count} 张牌并换牌", [], [], Parameters("finish")));
        }
        else if (receipt.Stage == FriendPangTongStage.Use)
        {
            foreach (var (action, index) in FriendYangmingActions(frame).Select((action, index) => (action, index)))
                choices.Add(new(new($"friend-yangming.{frame.Id}.{receipt.UsedSuits.Count}.{index}"), action.Description,
                    [action.CardId!.Value], action.TargetSeats, Parameters(index.ToString(CultureInfo.InvariantCulture))));
            choices.Add(new(new($"friend-yangming.{frame.Id}.finish"), "结束使用，将剩余牌置入弃牌堆", [], [], Parameters("finish")));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishFriendPangTongChoice(ProgramSkillFrame frame)
    {
        var choices = FriendPangTongChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            frame.FriendPangTong!.Stage == FriendPangTongStage.Select ? "漫卷：选择本次获得的牌，按从顶至底的顺序置于牌堆顶。" : "养名：使用武将牌上的牌，每种花色只能使用一次。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("friend-pang-tong")]
    private void ResolveFriendPangTongChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Friend Pang Tong lost its program.");
        AssertFriendPangTong(frame);
        var canonical = FriendPangTongChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id);
        if (canonical is null || !AssistedChoicesEqual([canonical], [selected])) throw new InvalidOperationException("Friend Pang Tong changed its exact private cards, order or use targets.");
        ClearPendingDecision();
        var receipt = frame.FriendPangTong!; var value = selected.Parameters["value"];
        if (receipt.Stage == FriendPangTongStage.Select)
        {
            if (value != "finish")
            { ReplaceRuntimeTop(frame with { FriendPangTong = receipt with { Selected = receipt.Selected.Append(selected.Cards.Single()).ToArray() } }); PublishFriendPangTongChoice(GetActiveProgramFrame(frame.Id)); return; }
            if (receipt.Selected.Count == 0) { FinishFriendPangTong(frame); return; }
            var cards = receipt.Selected.Select(GetAttackCard).ToArray();
            FriendPangTongMutation(frame, FriendPangTongStage.Exchange, () =>
            {
                MoveCards(cards, CardLocation.Hand(frame.OwnerSeat), CardLocation.DrawPile, new("friend.manjuan.top"));
                _cardZones.PlaceDrawPileCardsAtTop(receipt.Selected);
            }); return;
        }
        if (value == "finish") { CleanupFriendYangming(frame); return; }
        var action = FriendYangmingActions(frame)[int.Parse(value, CultureInfo.InvariantCulture)];
        IssueFriendYangmingUse(frame, action);
    }
    private void IssueFriendYangmingUse(ProgramSkillFrame frame, LegalAction action)
    {
        var actor = _players[frame.OwnerSeat]; var card = GetAttackCard(action.CardId!.Value); var kind = card.Kind;
        var receipt = frame.FriendPangTong!; ProgramOrdinaryTrickUseOption? option = null;
        if (IsOrdinaryTrick(kind)) option = BuildProgramOrdinaryTrickUseOptions(actor, kind, EffectiveSuit(actor, card),
            enforceUsePermission: true, beneficiaryShieldSuit: EffectiveSuit(actor, card), actualEffectiveColor: SuitColor(EffectiveSuit(actor, card)),
            hasActualColor: true, physicalCardIds: [card.Id], includeNextActualUseAdjustment: true).Single(candidate => candidate.ActionKind == action.Kind &&
                candidate.TargetCardId == action.TargetCardId && candidate.TargetSeats.SequenceEqual(action.TargetSeats));
        if (option?.NextActualUseAdjusted == true) _selectedNextCardTargetSeats = action.TargetSeats;
        SelectUseConversion(action);
        var id = BeginCardUse(card, actor.Seat, action.TargetSeats, kind,
            designatedTargetSeats: kind == CardKind.BorrowedSword ? action.TargetSeats.Take(1).ToArray() : null);
        var use = LifecycleCardUse(id)!;
        var ret = new FriendYangmingUseReturn(frame.Id, frame.InstructionIndex, card.Id, card.Suit,
            frame.SkillInstanceId, frame.GameplayHash, use.Action!.ActionId);
        ReplaceRuntimeFrame(frame.Id, frame with { FriendPangTong = receipt with { Stage = FriendPangTongStage.Issued, UseId = id,
            UsedSuits = receipt.UsedSuits.Append(card.Suit).ToArray() } });
        UpdateLifecycleCardUse(id, current => current with { FriendYangmingReturn = ret, UnlimitedUse = true, QuotaOnlyUseLimitExemption = true });
        AdvanceEventRulesAndQueueFact(new FriendYangmingUseIssuedEvent(id, actor.Seat, ret));
        MoveCard(card, FriendYangmingLocation(frame), CardLocation.Processing, EquipmentCatalog.IsEquipment(kind) ? CardMoveReasons.EquipmentUse : CardMoveReasons.Use);
        if (EquipmentCatalog.IsEquipment(kind))
        { if (!TryBeginEquipmentTargetPrograms(actor, card, id)) CompleteEquipmentUse(actor, card, id); return; }
        if (kind == CardKind.Peach) { BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Recovery)); return; }
        if (kind == CardKind.Alcohol) { BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.Alcohol)); return; }
        if (IsSlashCard(kind))
        {
            var target = _players[action.TargetSeats[0]]; var damage = 1 + actor.AlcoholDamageBonus;
            CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
            var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card, damageAmount: damage,
                playedCardKind: kind, physicalCards: [card], programSkillCardUseFrameId: frame.Id,
                ignoresArmor: HasCardArmorBypass(actor, target, kind));
            ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind);
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id, kind, actor.Seat, target.Seat));
            if (!TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!, SkillProgramTriggerWindow.CardUseCommitted,
                action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack); return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, use.TargetSeats, action.Kind, action.TargetCardId, option?.RequiredCardKind, kind);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 30)]
    private bool CompleteFriendYangmingNativeAttackReturn()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FriendPangTong.Stage: FriendPangTongStage.Returned } parent) return false;
        AdvanceRuntimeProgram(parent.Id);
        return true;
    }
    [NativeUseReturn(310)]
    private void ReturnFriendYangmingUse(CardUseFrame use)
    {
        if (use.FriendYangmingReturn is not { } ret) return;
        var frame = GetActiveProgramFrame(ret.ProgramFrameId); var receipt = frame.FriendPangTong;
        if (receipt is not { Stage: FriendPangTongStage.Issued } || receipt.UseId != use.Id || frame.InstructionIndex != ret.InstructionIndex ||
            frame.SkillInstanceId != ret.SkillInstanceId || frame.GameplayHash != ret.GameplayHash || use.CardId != ret.CardId ||
            use.Action?.ActionId != ret.CardActionId || !receipt.UsedSuits.Contains(ret.Suit))
            throw new InvalidOperationException("Yangming lost its once-issued owning typed Use return.");
        ReplaceRuntimeFrame(frame.Id, frame = frame with { FriendPangTong = receipt with { Stage = FriendPangTongStage.Returned, UseId = null } });
        if (use.CardAttack is not { Active: true }) ContinueFriendPangTong(frame);
    }
    private void ContinueFriendPangTong(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame); var receipt = frame.FriendPangTong!;
        if (receipt.Stage == FriendPangTongStage.Complete) { FinishFriendPangTong(frame); return; }
        if (!DeckProgramSourceValid(frame) || _winner != Winner.None)
        { if (receipt.Operation == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd) CleanupFriendYangming(frame, reward: false); else FinishFriendPangTong(frame); return; }
        switch (receipt.Stage)
        {
            case FriendPangTongStage.Exchange:
                if (receipt.ExchangeIndex >= Math.Min(5, receipt.Selected.Count)) { FinishFriendPangTong(frame); return; }
                var original = GetAttackCard(receipt.Selected[receipt.ExchangeIndex]); var category = FriendCategoryValue(original.Kind);
                var pool = _cardZones.CardsAt(CardLocation.DiscardPile).Where(card => FriendCategoryValue(card.Kind) != category).OrderBy(card => card.Id).ToArray();
                ReplaceRuntimeTop(frame = frame with { FriendPangTong = receipt with { ExchangeIndex = receipt.ExchangeIndex + 1 } });
                FriendPangTongMutation(frame, FriendPangTongStage.Exchange, () =>
                { if (pool.Length > 0) MoveCard(pool[_random.Next(pool.Length)], CardLocation.DiscardPile, CardLocation.Hand(frame.OwnerSeat), new("friend.manjuan.gain")); }); return;
            case FriendPangTongStage.Returned:
                FriendPangTongMutation(frame, FriendPangTongStage.Use, () => { }); return;
            case FriendPangTongStage.Use:
                if (FriendYangmingActions(frame).Count == 0) { CleanupFriendYangming(frame); return; }
                PublishFriendPangTongChoice(frame); return;
            case FriendPangTongStage.Reward:
                var excluded = receipt.UsedSuits;
                var reward = HasRuntimeSkill(_players[frame.OwnerSeat], "friend:gongli-pang-tong") && HasFriendTrioAlly(_players[frame.OwnerSeat], "xu-shu")
                    ? _cardZones.CardsAt(CardLocation.DrawPile).Where(card => receipt.Cards.Select(GetAttackCard).Any(placed => placed.Suit == card.Suit) &&
                        !excluded.Contains(card.Suit)).OrderBy(card => card.Id).ToArray() : [];
                FriendPangTongMutation(frame, FriendPangTongStage.Complete, () =>
                { if (reward.Length > 0) MoveCard(reward[_random.Next(reward.Length)], CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), new("friend.gongli-pang.gain")); }); return;
            case FriendPangTongStage.Complete: FinishFriendPangTong(frame); return;
            default: if (_pendingDecision is null) PublishFriendPangTongChoice(frame); return;
        }
    }
    private void CleanupFriendYangming(ProgramSkillFrame frame, bool reward = true)
    {
        var cards = _cardZones.CardsAt(FriendYangmingLocation(frame)).ToArray();
        FriendPangTongMutation(frame, reward ? FriendPangTongStage.Reward : FriendPangTongStage.Complete, () =>
            MoveCards(cards, FriendYangmingLocation(frame), CardLocation.DiscardPile, new("friend.yangming.remaining")));
    }
    private void FinishFriendPangTong(ProgramSkillFrame frame)
    { ReplaceRuntimeTop(frame with { FriendPangTong = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(frame.Id); }
    private void FriendPangTongMutation(ProgramSkillFrame frame, FriendPangTongStage next, Action mutation)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = frame.FriendPangTong! with
            { Stage = FriendPangTongStage.Movement, NextStage = next, Before = before, After = before }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        mutation(); frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = frame.FriendPangTong! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new FriendPangTongMovementPaidEvent(frame.Id, frame.FriendPangTong.Operation, before, _movementSequence));
        ReturnFriendPangTongMovement(frame);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20)]
    private bool ReturnFriendPangTongMovement(ProgramSkillFrame frame)
    {
        if (frame.FriendPangTong is not { Stage: FriendPangTongStage.Movement } receipt) return false;
        if (DrainFriendTrioChildren(frame)) return true;
        ContinueFriendPangTong(frame with { PendingMovementContinuation = null, FriendPangTong = receipt with { Stage = receipt.NextStage } }); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20)]
    private bool ResumeFriendPangTong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FriendPangTong: { } receipt } frame || frame.Id != id) return false;
        if (receipt.Stage == FriendPangTongStage.Movement) return ReturnFriendPangTongMovement(frame);
        if (receipt.Stage == FriendPangTongStage.Issued) return true;
        if (_pendingDecision is null) ContinueFriendPangTong(frame); return true;
    }
    private bool IsFriendPangTongMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.FriendPangTong is { Stage: FriendPangTongStage.Movement } receipt && effect?.Op == receipt.Operation &&
        frame.PendingMovementContinuation == pending && pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 470)]
    private void AssertFriendPangTong(ProgramSkillFrame frame)
    {
        if (frame.FriendPangTong is not { } receipt) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (receipt.InstructionIndex != frame.InstructionIndex || effect.Op != receipt.Operation || !Enum.IsDefined(receipt.Stage) ||
            receipt.Cards.Distinct().Count() != receipt.Cards.Count || receipt.Selected.Distinct().Count() != receipt.Selected.Count ||
            receipt.Selected.Any(id => !receipt.Cards.Contains(id)) || receipt.UsedSuits.Distinct().Count() != receipt.UsedSuits.Count ||
            receipt.Stage == FriendPangTongStage.Issued != (receipt.UseId is not null) ||
            receipt.Stage == FriendPangTongStage.Movement && (frame.PendingMovementContinuation is null || receipt.Before > receipt.After || receipt.After > _movementSequence))
            throw new InvalidOperationException("Friend Pang Tong lost its frozen cards, ordered exchange or one-use receipt.");
    }
    private IEnumerable<CardSnapshot> FriendPangTongPublicCards() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(frame => frame.FriendPangTong?.Operation == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd)
        .SelectMany(frame => _cardZones.CardsAt(FriendYangmingLocation(frame))).Select(ToSnapshot);
}
