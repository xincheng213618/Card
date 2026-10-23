namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MobileShiFujiSkill = "mobile:shi-fuji";
    private const string MobileShiFujiEmpoweredState = "fuji-empowered";
    private const string MobileShiFujiSuitGainMoveReason = "skill-program.mobile:shi-fuji.suit-gain";
    private const string MobileShiFujiBonusDrawReason = "skill-program.mobile:shi-fuji.bonus-draw";
    private const string MobileShiFujiDodgeDrawReason = "skill-program.mobile:shi-fuji.dodge-draw";

    [ProgramCapability]
    private sealed class MobileShiYuJiProgramCapability(GameEngine engine) : IMobileShiYuJiProgramHost
    {
        public SkillProgramStepOutcome ResolveFujiGift(ProgramSkillFrame frame) => engine.ResolveMobileShiFujiGiftProgram(frame);
        public SkillProgramStepOutcome ResolveFujiUseEffects(ProgramSkillFrame frame) => engine.ResolveMobileShiFujiUseEffectsProgram(frame);
        public SkillProgramStepOutcome ResolveFujiDodgeDraw(ProgramSkillFrame frame) => engine.ResolveMobileShiFujiDodgeDrawProgram(frame);
    }

    // 符济: the frozen activation selection is marked on the frame's own source
    // identity; the fewest-hand bonus draws and arms the first-Slash/Dodge
    // window that expires at the owner's next turn start.
    private SkillProgramStepOutcome ResolveMobileShiFujiGiftProgram(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        var owner = _players[f.OwnerSeat];
        if (f.ActivationId != MobileShiYuJiComposition.FujiGiftActivation || f.TriggerId is not null ||
            f.WindowContext is not null || f.SelectedTargetSeats is not [var recipient] || recipient == f.OwnerSeat)
            throw new InvalidOperationException("Fuji gift requires its own activation frame and one exact other recipient.");
        if (f.SelectedCardIds.Count == 0 || _winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var program = _contentRegistry.GetSkill(f.SkillId).Program!;
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var recipientHand = CardLocation.Hand(recipient);
        var marked = 0;
        foreach (var cardId in f.SelectedCardIds)
        {
            if (_cardZones.GetLocation(cardId) != recipientHand) continue;
            var card = _cardZones.CardsAt(recipientHand).SingleOrDefault(item => item.Id == cardId);
            if (card is null) continue;
            AdvanceEventRulesAndQueueFact(new MobileShiFujiCardMarkedEvent(f.Id, source, program.GameplayHash,
                f.OwnerSeat, recipient, cardId, card.Kind, card.Suit));
            marked++;
        }
        if (marked > 0)
            AddLog("SkillEffect", $"{owner.Name} 符济：{marked} 张牌成为“符济”牌。", f.OwnerSeat);
        var minimum = _players.Where(p => p.IsAlive).Min(p => GetHand(p).Count);
        if (marked == 0 || GetHand(owner).Count > minimum) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new MobileShiFujiEmpowerIssuedEvent(f.Id, f.OwnerSeat));
        SetProgramBooleanState(f.OwnerSeat, f.SkillId, f.SkillInstanceId, MobileShiFujiEmpoweredState, true);
        AddLog("SkillEffect", $"{owner.Name} 符济：手牌数为全场最少，摸一张牌且首张【杀】和【闪】带有“符济”牌效果。", f.OwnerSeat);
        DrawCards(owner, 1, true, new(MobileShiFujiBonusDrawReason));
        if (!TryBeginCardsMovedProgramWindow()) AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool IsMobileShiFujiEmpoweredActive(ProgramSkillFrame f) =>
        GetProgramBooleanState(f.OwnerSeat, f.SkillId, f.SkillInstanceId, MobileShiFujiEmpoweredState);

    private int LastMobileShiFujiEmpowerIndex(IGameEvent[] history, int ownerSeat)
    {
        for (var i = history.Length - 1; i >= 0; i--)
            if (history[i] is MobileShiFujiEmpowerIssuedEvent { OwnerSeat: var seat } && seat == ownerSeat) return i;
        return -1;
    }

    // 获得一张与该牌花色相同的牌：与诱言一致，取牌堆顶起第一张同花色牌。
    private bool GainMobileShiFujiSuitCard(int userSeat, int usedCardId, Suit suit, long actionId)
    {
        var card = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(item => item.Suit == suit);
        if (card is null) return false;
        var sequence = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(userSeat), new(MobileShiFujiSuitGainMoveReason));
        var after = _cardMovements.LastOrDefault()?.Sequence ?? sequence;
        AdvanceEventRulesAndQueueFact(new MobileShiFujiSuitGainIssuedEvent(actionId, userSeat, usedCardId, suit, card.Id, after));
        return true;
    }

    private SkillProgramStepOutcome ResolveMobileShiFujiUseEffectsProgram(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        var owner = _players[f.OwnerSeat];
        if (f.TriggerId != MobileShiYuJiComposition.FujiUseEffectsTrigger || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } })
            throw new InvalidOperationException("Fuji use effects require their exact committed card-use window.");
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        // The committed window freezes its exact action on the owning window frame;
        // the accepted fact only enters history after that window completes.
        var action = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
            .SingleOrDefault(item => item.Id == f.WindowContext.ParentFrameId)?.Action;
        if (action is null) return SkillProgramStepOutcome.Continue;
        var history = CompleteProgramEventHistory().ToArray();
        var issued = false;
        var gained = 0;
        foreach (var cost in action.PhysicalCards)
        {
            if (!history.OfType<MobileShiFujiCardMarkedEvent>().Any(item =>
                    item.CardId == cost.CardId && item.Source.SkillId == MobileShiFujiSkill &&
                    item.Source.SkillInstanceId == f.SkillInstanceId)) continue;
            if (history.OfType<MobileShiFujiCardUsedEvent>().Any(item => item.CardId == cost.CardId && !item.Empowered)) continue;
            var card = _cardZones.CardsAt(_cardZones.GetLocation(cost.CardId))
                .SingleOrDefault(item => item.Id == cost.CardId);
            if (card is null) continue;
            AdvanceEventRulesAndQueueFact(new MobileShiFujiCardUsedEvent(action.ActionId, action.ActorSeat,
                cost.CardId, card.Kind, MobileShiYuJiComposition.Name(card.Kind), Empowered: false));
            if (GainMobileShiFujiSuitCard(action.ActorSeat, cost.CardId, card.Suit, action.ActionId)) gained++;
            issued = true;
        }
        var normalized = MobileShiYuJiComposition.Name(action.EffectiveKind);
        if (action.ActorSeat == owner.Seat && normalized is CardKind.Slash or CardKind.Dodge &&
            IsMobileShiFujiEmpoweredActive(f) &&
            !history.Skip(LastMobileShiFujiEmpowerIndex(history, owner.Seat) + 1).OfType<MobileShiFujiCardUsedEvent>()
                .Any(item => item.Empowered && item.UserSeat == owner.Seat && item.Name == normalized))
        {
            var first = action.PhysicalCards.Count > 0 ? action.PhysicalCards[0] : null;
            var firstCard = first is { } leading
                ? _cardZones.CardsAt(_cardZones.GetLocation(leading.CardId)).SingleOrDefault(item => item.Id == leading.CardId)
                : null;
            AdvanceEventRulesAndQueueFact(new MobileShiFujiCardUsedEvent(action.ActionId, owner.Seat,
                firstCard?.Id ?? 0, action.EffectiveKind, normalized, Empowered: true));
            if (firstCard is not null && GainMobileShiFujiSuitCard(owner.Seat, firstCard.Id, firstCard.Suit, action.ActionId)) gained++;
            issued = true;
        }
        if (!issued) return SkillProgramStepOutcome.Continue;
        AddLog("SkillEffect", $"{_players[action.ActorSeat].Name} 符济：使用了“符济”牌" +
            (gained > 0 ? $"，从牌堆获得 {gained} 张同花色牌。" : "。"), f.OwnerSeat);
        if (!TryBeginCardsMovedProgramWindow()) AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgramStepOutcome ResolveMobileShiFujiDodgeDrawProgram(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        var owner = _players[f.OwnerSeat];
        if (f.TriggerId != MobileShiYuJiComposition.FujiDodgeDrawTrigger || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardResponseCompleted, CardUse: { } use })
            throw new InvalidOperationException("Fuji's Dodge draw requires its exact response-completion window.");
        // The window's own completed-response fact carries the accepted action identity;
        // a finished response answers exactly one accepted action.
        var completedFact = CompleteProgramEventHistory().OfType<CardResponseCompletedEvent>()
            .SingleOrDefault(item => item.ActionId == use.CardActionId);
        if (completedFact is null || _winner != Winner.None || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var user = _players[completedFact.ActorSeat];
        var history = CompleteProgramEventHistory().ToArray();
        var used = history.OfType<MobileShiFujiCardUsedEvent>()
            .Where(item => item.ActionId == completedFact.ActionId && item.Name == CardKind.Dodge).ToArray();
        if (used.Length == 0 || !user.IsAlive || !owner.IsAlive || history.OfType<MobileShiFujiDodgeDrawIssuedEvent>()
                .Any(item => item.ActionId == completedFact.ActionId)) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new MobileShiFujiDodgeDrawIssuedEvent(completedFact.ActionId, user.Seat, used[0].CardId, used[0].Empowered));
        AddLog("SkillEffect", $"{user.Name} 符济：【闪】结算完毕后摸一张牌。", f.OwnerSeat);
        DrawCards(user, 1, true, new(MobileShiFujiDodgeDrawReason));
        if (!TryBeginCardsMovedProgramWindow()) AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 符济【杀】基础伤害值+1：已结算的符济使用事实按 ActionId 归属本次攻击。
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 67210)]
    private int MobileShiFujiSlashDamageBonus(IDamageAttempt attack)
    {
        if (attack.IsChainPropagation || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { } action } ||
            !CompleteProgramEventHistory().OfType<MobileShiFujiCardUsedEvent>()
                .Any(item => item.ActionId == action.ActionId && IsSlashCard(item.Kind)))
            return 0;
        return 1;
    }
}
