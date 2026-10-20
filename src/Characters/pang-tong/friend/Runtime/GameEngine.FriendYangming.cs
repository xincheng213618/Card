using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasFriendYangmingQuota(CharacterState owner) => _resolutionStack.LastOrDefault() is
        ProgramSkillFrame { FriendPangTong.Stage: FriendPangTongStage.Use } frame && frame.OwnerSeat == owner.Seat;
    private static CardLocation FriendYangmingLocation(ProgramSkillFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pang_tong_friend_FriendYangming.FriendYangmingLocation(frame);
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
    private void CleanupFriendYangming(ProgramSkillFrame frame, bool reward = true)
    {
        var cards = _cardZones.CardsAt(FriendYangmingLocation(frame)).ToArray();
        FriendPangTongMutation(frame, reward ? FriendPangTongStage.Reward : FriendPangTongStage.Complete, () =>
            MoveCards(cards, FriendYangmingLocation(frame), CardLocation.DiscardPile, new("friend.yangming.remaining")));
    }
}
