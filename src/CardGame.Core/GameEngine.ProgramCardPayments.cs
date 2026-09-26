namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ResolveProgramParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference)
    {
        return reference.Kind switch
        {
            ProgramParticipantRef.Owner => frame.OwnerSeat,
            ProgramParticipantRef.Actor when frame.WindowContext?.CardUse is { } context => context.ActorSeat,
            ProgramParticipantRef.EventTarget when frame.WindowContext?.TargetSeat is { } seat => seat,
            ProgramParticipantRef.EventSource when frame.WindowContext?.SourceSeat is { } seat => seat,
            ProgramParticipantRef.SelectedTarget when frame.SelectedTargetSeats is [var seat] => seat,
            ProgramParticipantRef.SelectedFirst when frame.SelectedTargetSeats.Count >= 1 => frame.SelectedTargetSeats[0],
            ProgramParticipantRef.SelectedSecond when frame.SelectedTargetSeats.Count >= 2 => frame.SelectedTargetSeats[1],
            ProgramParticipantRef.ResultSource => frame.PindianResultBindings
                .Single(item => item.Name == reference.ResultBind).SourceSeat,
            ProgramParticipantRef.ResultOpponent => frame.PindianResultBindings
                .Single(item => item.Name == reference.ResultBind).OpponentSeat,
            _ => throw new InvalidOperationException("The requested program participant is unavailable.")
        };
    }

    private SkillProgramStepOutcome SelectAndMoveProgramOwnedCard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        SkillProgramCardDestination destination,
        ProgramParticipantReference? destinationRef,
        string? resultBind,
        CardMoveReason reason,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        bool skipIfNoCards = false)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var chooserSeat = ResolveProgramParticipant(active, chooser);
        var cardOwnerSeat = ResolveProgramParticipant(active, cardOwner);
        var destinationSeat = destinationRef is null ? (int?)null : ResolveProgramParticipant(active, destinationRef);
        if (destination == SkillProgramCardDestination.SelectedTargetHand &&
            (destinationSeat is not { } seat || !_players[seat].IsAlive || seat == cardOwnerSeat))
            throw new InvalidOperationException("A selected-target transfer requires a distinct living recipient.");
        var choices = BuildOwnedCardPaymentChoices(active.Id, chooserSeat, cardOwnerSeat, zones, cardCategories);
        if (choices.Count == 0)
        {
            if (skipIfNoCards)
                return SkillProgramStepOutcome.Continue;
            CancelProgramBindingAndCleanup(active, "没有可支付的区域牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var visibleCardIds = choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray();
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, chooserSeat,
            destination == SkillProgramCardDestination.SelectedTargetHand
                ? "请选择一张区域牌交给目标。" : "请选择一张区域牌支付。", visibleCardIds, [],
            chooserSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = frame.WindowContext?.TargetSeat ?? cardOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                destination == SkillProgramCardDestination.SelectedTargetHand
                    ? $"{skill.Name} · 选择转交牌" : $"{skill.Name} · 选择支付牌", skill.Description),
            Choices = choices
        };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> BuildOwnedCardPaymentChoices(
        long frameId, int chooserSeat, int cardOwnerSeat, IReadOnlyList<CardZoneKind> zones,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null)
    {
        var result = new List<PromptChoice>();
        foreach (var zone in zones)
        {
            var cards = zone switch
            {
                CardZoneKind.Hand => GetHand(_players[cardOwnerSeat]),
                CardZoneKind.Equipment => GetEquipment(_players[cardOwnerSeat]),
                CardZoneKind.Judgment => GetJudgment(_players[cardOwnerSeat]),
                _ => throw new InvalidOperationException("Unsupported payment card zone.")
            };
            for (var slot = 0; slot < cards.Count; slot++)
            {
                if (cardCategories is { Count: > 0 } && !MatchesProgramCardCategory(cards[slot].Kind, cardCategories))
                    continue;
                var hidden = zone == CardZoneKind.Hand && chooserSeat != cardOwnerSeat;
                var parameters = new Dictionary<string, string>
                {
                    ["program-action"] = "select-and-move-owned-card",
                    ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["card-owner-seat"] = cardOwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source-zone"] = zone.ToString(),
                    ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                result.Add(new PromptChoice(
                    new ChoiceId($"program-owned-card.frame-{frameId}.zone-{zone}.slot-{slot}"),
                    hidden ? $"选择第 {slot + 1} 张手牌" : $"选择【{cards[slot].DisplayName}】",
                    hidden ? [] : [cards[slot].Id], [], parameters));
            }
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private void ResolveSelectAndMoveOwnedCardChoice(
        ProgramSkillFrame frame, SkillProgramEffect effect, PromptChoice selected)
    {
        var chooserSeat = ResolveProgramParticipant(frame, effect.ChooserRef!);
        var ownerSeat = ResolveProgramParticipant(frame, effect.CardOwnerRef!);
        if (!_players[frame.OwnerSeat].IsAlive || !_players[chooserSeat].IsAlive || !_players[ownerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "支付参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }
        if (selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("card-owner-seat") != ownerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !effect.Zones.Contains(zone) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), out var slot))
            throw new InvalidOperationException("The selected payment no longer matches its suspended instruction.");
        var cards = zone switch
        {
            CardZoneKind.Hand => GetHand(_players[ownerSeat]),
            CardZoneKind.Equipment => GetEquipment(_players[ownerSeat]),
            CardZoneKind.Judgment => GetJudgment(_players[ownerSeat]),
            _ => throw new InvalidOperationException("Unsupported payment card zone.")
        };
        if (slot < 0 || slot >= cards.Count)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的支付牌位已失效，技能结算已取消。");
            return;
        }
        var card = cards[slot];
        if (effect.CardCategories.Count > 0 &&
            !MatchesProgramCardCategory(card.Kind, effect.CardCategories))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "公布的支付牌类别已失效，技能结算已取消。");
            return;
        }
        if (!(zone == CardZoneKind.Hand && chooserSeat != ownerSeat) &&
            (selected.Cards.Count != 1 || selected.Cards[0] != card.Id))
            throw new InvalidOperationException("The visible payment card identity changed.");
        var source = zone switch
        {
            CardZoneKind.Hand => CardLocation.Hand(ownerSeat),
            CardZoneKind.Equipment => CardLocation.Equipment(ownerSeat),
            CardZoneKind.Judgment => CardLocation.Judgment(ownerSeat),
            _ => throw new InvalidOperationException()
        };
        var destination = effect.Destination switch
        {
            SkillProgramCardDestination.OwnerHand => CardLocation.Hand(frame.OwnerSeat),
            SkillProgramCardDestination.SelectedTargetHand => CardLocation.Hand(
                ResolveProgramParticipant(frame, effect.TargetReference!)),
            SkillProgramCardDestination.DiscardPile => CardLocation.DiscardPile,
            _ => throw new InvalidOperationException("Unsupported selected-card destination.")
        };
        if (destination.OwnerSeat is { } recipientSeat && !_players[recipientSeat].IsAlive)
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "受牌角色已失效，技能结算已取消。");
            return;
        }
        ClearPendingDecision();
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        if (effect.Destination == SkillProgramCardDestination.SelectedTargetHand)
        {
            MoveCard(card, source, CardLocation.Processing, reason);
            MoveCard(card, CardLocation.Processing, destination, reason);
        }
        else MoveCard(card, source, destination, reason);
        if (effect.ResultBind is { } bind)
            SetProgramCardSet(frame.Id, bind, [card.Id], SkillProgramCardSetVisibility.Private, [destination]);
        ContinueProgramSkill(frame.Id);
    }

    private static bool MatchesProgramCardCategory(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories)
    {
        return categories.Contains(GetProgramCardCategory(kind));
    }

    private static SkillProgramCardCategory GetProgramCardCategory(CardKind kind)
    {
        return EquipmentCatalog.IsEquipment(kind)
            ? SkillProgramCardCategory.Equipment
            : CardCatalog.Get(kind).CategoryName switch
            {
                "基本牌" => SkillProgramCardCategory.Basic,
                "锦囊牌" => SkillProgramCardCategory.Trick,
                _ => throw new InvalidOperationException($"Card kind '{kind}' has no supported skill-program category.")
            };
    }
}
