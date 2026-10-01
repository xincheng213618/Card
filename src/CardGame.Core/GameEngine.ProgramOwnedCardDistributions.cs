namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome DistributeProgramOwnedCards(
        ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        SkillProgramTargetKind targetKind,
        bool allowDeclineBeforeFirst,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnedCardDistribution is not null)
            throw new InvalidOperationException("An owned-card distribution is already active.");
        var requiredCount = GetProgramCardSet(active, sourceBind).CardIds.Count;
        if (requiredCount <= 0)
            return SkillProgramStepOutcome.Continue;

        var availableCount = zones.Sum(zone => _cardZones.CardsAt(new CardLocation(zone, active.OwnerSeat)).Count);
        var targetSeats = GetProgramTargetSeats(active.OwnerSeat, targetKind);
        if (availableCount < requiredCount || targetSeats.Count == 0)
            return SkillProgramStepOutcome.Continue;

        active = active with
        {
            OwnedCardDistribution = new ProgramOwnedCardDistribution(
                active.OwnerSeat,
                sourceBind,
                requiredCount,
                Array.AsReadOnly(zones.ToArray()),
                targetKind,
                allowDeclineBeforeFirst,
                [],
                [])
        };
        ReplaceRuntimeTop(active);
        PublishProgramOwnedCardDistribution(active, reason);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramOwnedCardDistribution(ProgramSkillFrame frame, CardMoveReason reason)
    {
        var distribution = frame.OwnedCardDistribution ??
            throw new InvalidOperationException("Missing owned-card distribution state.");
        var owner = _players[distribution.CardOwnerSeat];
        var cards = distribution.Zones
            .SelectMany(zone => _cardZones.CardsAt(new CardLocation(zone, owner.Seat))
                .Select(card => (Card: card, Zone: zone)))
            .OrderBy(item => item.Zone)
            .ThenBy(item => item.Card.Id)
            .ToArray();
        var targets = GetProgramTargetSeats(owner.Seat, distribution.TargetKind)
            .Select(seat => _players[seat])
            .ToArray();
        var remaining = distribution.RequiredCount - distribution.GivenCardIds.Count;
        if (cards.Length < remaining || targets.Length == 0)
        {
            ReplaceRuntimeTop(frame with { OwnedCardDistribution = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }

        var choices = cards.SelectMany(item => targets.Select(target => new PromptChoice(
                new ChoiceId(
                    $"program-owned-distribution.frame-{frame.Id}.index-{distribution.GivenCardIds.Count}.card-{item.Card.Id}.target-{target.Seat}"),
                $"将{ProgramOwnedDistributionZoneName(item.Zone)}【{item.Card.DisplayName}】交给 {target.Name}。",
                [item.Card.Id],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["program-action"] = "distribute-owned-card",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source-bind"] = distribution.SourceBind,
                    ["source-zone"] = item.Zone.ToString(),
                    ["target-kind"] = distribution.TargetKind.ToString(),
                    ["distribution-index"] = distribution.GivenCardIds.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["move-reason"] = reason.Value
                })))
            .ToList();
        if (distribution.AllowDeclineBeforeFirst && distribution.GivenCardIds.Count == 0)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"program-owned-distribution.frame-{frame.Id}.decline"),
                "不分配卡牌，结束此项结算。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "decline-owned-card-distribution",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["source-bind"] = distribution.SourceBind,
                    ["target-kind"] = distribution.TargetKind.ToString(),
                    ["distribution-index"] = "0",
                    ["move-reason"] = reason.Value
                }));
        }

        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            owner.Seat,
            distribution.GivenCardIds.Count == 0
                ? $"你可以逐张分配 {remaining} 张自己的牌；一旦交出第一张，必须交足冻结数量。"
                : $"还须分配 {remaining} 张自己的牌。",
            cards.Select(item => item.Card.Id).ToArray(),
            targets.Select(target => target.Seat).ToArray(),
            SourceSeat: frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = owner.Seat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 分配卡牌", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
    }

    private void ResolveProgramOwnedCardDistribution(
        ProgramSkillFrame frame,
        SkillProgramEffect effect,
        PromptChoice selected)
    {
        var distribution = frame.OwnedCardDistribution ??
            throw new InvalidOperationException("Missing owned-card distribution state.");
        var action = selected.Parameters.GetValueOrDefault("program-action");
        var expectedReason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        if (effect.Op != SkillProgramEffectOp.DistributeOwnedCards ||
            effect.SourceBind != distribution.SourceBind ||
            effect.TargetKind != distribution.TargetKind ||
            effect.AllowDeclineBeforeFirst != distribution.AllowDeclineBeforeFirst ||
            !effect.Zones.SequenceEqual(distribution.Zones) ||
            selected.Parameters.GetValueOrDefault("source-bind") != distribution.SourceBind ||
            selected.Parameters.GetValueOrDefault("target-kind") != distribution.TargetKind.ToString() ||
            selected.Parameters.GetValueOrDefault("move-reason") != expectedReason.Value ||
            selected.Parameters.GetValueOrDefault("distribution-index") !=
                distribution.GivenCardIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The owned-card distribution choice does not match its suspended instruction.");

        if (action == "decline-owned-card-distribution")
        {
            if (!distribution.AllowDeclineBeforeFirst || distribution.GivenCardIds.Count != 0 ||
                selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The owned-card distribution can no longer be declined.");
            ClearPendingDecision();
            ReplaceRuntimeTop(frame with { OwnedCardDistribution = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }

        if (action != "distribute-owned-card" || selected.Cards.Count != 1 || selected.Targets.Count != 1 ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
            !distribution.Zones.Contains(zone))
            throw new InvalidOperationException("The owned-card distribution requires one legal card and target.");
        var cardId = selected.Cards.Single();
        var targetSeat = selected.Targets.Single();
        if (!GetProgramTargetSeats(distribution.CardOwnerSeat, distribution.TargetKind).Contains(targetSeat))
            throw new InvalidOperationException("The owned-card distribution target is no longer legal.");
        var from = new CardLocation(zone, distribution.CardOwnerSeat);
        if (_cardZones.GetLocation(cardId) != from)
            throw new InvalidOperationException("The distributed card is no longer in the selected owned zone.");
        var card = _cardZones.CardsAt(from).Single(item => item.Id == cardId);

        ClearPendingDecision();
        MoveCard(card, from, CardLocation.Processing, expectedReason);
        MoveProcessingCardUnlessDestroyed(card, CardLocation.Hand(targetSeat), expectedReason);
        var givenIds = distribution.GivenCardIds.Append(cardId).ToArray();
        var targetSeats = distribution.TargetSeats.Append(targetSeat).ToArray();
        AdvanceEventRulesAndQueueFact(new ProgramOwnedCardDistributedEvent(
            frame.Id,
            frame.SkillId,
            GetProgramBindingId(frame),
            distribution.CardOwnerSeat,
            targetSeat,
            cardId,
            givenIds.Length,
            distribution.RequiredCount));
        AddLog("SkillCardGiven",
            $"{_players[distribution.CardOwnerSeat].Name} 通过【{_contentRegistry!.GetSkill(frame.SkillId).Name}】交给 {_players[targetSeat].Name} 一张牌。",
            distribution.CardOwnerSeat,
            targetSeat);

        if (givenIds.Length >= distribution.RequiredCount)
        {
            ReplaceRuntimeTop(frame with { OwnedCardDistribution = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }

        frame = frame with
        {
            OwnedCardDistribution = distribution with
            {
                GivenCardIds = Array.AsReadOnly(givenIds),
                TargetSeats = Array.AsReadOnly(targetSeats)
            }
        };
        ReplaceRuntimeTop(frame);
        PublishProgramOwnedCardDistribution(frame, expectedReason);
    }

    private PromptChoice SelectAiProgramOwnedCardDistribution(PendingDecision decision, ProgramSkillFrame frame)
    {
        var distribution = frame.OwnedCardDistribution ??
            throw new InvalidOperationException("Missing AI owned-card distribution state.");
        var owner = _players[distribution.CardOwnerSeat];
        var giveChoices = decision.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "distribute-owned-card").ToArray();
        var decline = decision.Choices.SingleOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "decline-owned-card-distribution");
        var allies = giveChoices.Where(choice =>
                choice.Targets.Count == 1 && AreProgramDistributionAllies(owner, _players[choice.Targets[0]]))
            .ToArray();
        if (allies.Length == 0 && decline is not null)
            return decline;
        var candidates = allies.Length > 0 ? allies : giveChoices;
        return candidates
            .OrderBy(choice => GetKeepValue(
                distribution.Zones.SelectMany(zone =>
                        _cardZones.CardsAt(new CardLocation(zone, distribution.CardOwnerSeat)))
                    .Single(card => card.Id == choice.Cards.Single()), owner))
            .ThenBy(choice => GetHand(_players[choice.Targets.Single()]).Count)
            .ThenBy(choice => choice.Targets.Single())
            .ThenBy(choice => choice.Cards.Single())
            .First();
    }

    private static bool AreProgramDistributionAllies(CharacterState owner, CharacterState target) =>
        owner.TeamId is not null || target.TeamId is not null
            ? owner.TeamId is not null && owner.TeamId == target.TeamId
            : owner.Role switch
            {
                Role.Lord => target.Role is Role.Lord or Role.Loyalist,
                Role.Loyalist => target.Role is Role.Lord or Role.Loyalist,
                Role.Rebel => target.Role == Role.Rebel,
                _ => false
            };

    private void AssertProgramOwnedCardDistribution(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.DistributeOwnedCards)
        {
            if (frame.OwnedCardDistribution is not null)
                throw new InvalidOperationException("An owned-card distribution outlived its suspended instruction.");
            return;
        }
        if (frame.OwnedCardDistribution is not { } distribution)
            throw new InvalidOperationException("A suspended owned-card distribution lost its private progress.");
        var remainingCards = distribution.Zones.Sum(zone =>
            _cardZones.CardsAt(new CardLocation(zone, distribution.CardOwnerSeat)).Count);
        var targets = GetProgramTargetSeats(distribution.CardOwnerSeat, distribution.TargetKind);
        var binding = GetProgramCardSet(frame, distribution.SourceBind);
        var expectedReason = $"skill-program.{frame.SkillId}.{paused.Op}";
        var expectedChoiceCount = remainingCards * targets.Count +
            (distribution.AllowDeclineBeforeFirst && distribution.GivenCardIds.Count == 0 ? 1 : 0);
        if (distribution.SourceBind != paused.SourceBind ||
            distribution.CardOwnerSeat != frame.OwnerSeat ||
            distribution.RequiredCount <= 0 || distribution.RequiredCount != binding.CardIds.Count ||
            distribution.GivenCardIds.Count >= distribution.RequiredCount ||
            distribution.GivenCardIds.Count != distribution.TargetSeats.Count ||
            distribution.GivenCardIds.Distinct().Count() != distribution.GivenCardIds.Count ||
            !distribution.Zones.SequenceEqual(paused.Zones) ||
            distribution.TargetKind != paused.TargetKind ||
            distribution.AllowDeclineBeforeFirst != paused.AllowDeclineBeforeFirst ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != distribution.CardOwnerSeat ||
            decision.Choices.Count != expectedChoiceCount ||
            decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("frame-id") !=
                    frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("source-bind") != distribution.SourceBind ||
                choice.Parameters.GetValueOrDefault("target-kind") != distribution.TargetKind.ToString() ||
                choice.Parameters.GetValueOrDefault("move-reason") != expectedReason ||
                choice.Parameters.GetValueOrDefault("distribution-index") !=
                    distribution.GivenCardIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                choice.Parameters.GetValueOrDefault("program-action") switch
                {
                    "decline-owned-card-distribution" =>
                        !distribution.AllowDeclineBeforeFirst || distribution.GivenCardIds.Count != 0 ||
                        choice.Cards.Count != 0 || choice.Targets.Count != 0,
                    "distribute-owned-card" =>
                        choice.Cards.Count != 1 || choice.Targets.Count != 1 ||
                        !Enum.TryParse<CardZoneKind>(
                            choice.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
                        !distribution.Zones.Contains(zone) ||
                        _cardZones.GetLocation(choice.Cards[0]) !=
                            new CardLocation(zone, distribution.CardOwnerSeat) ||
                        !targets.Contains(choice.Targets[0]),
                    _ => true
                }))
            throw new InvalidOperationException("A private owned-card distribution lost its exact instruction or prompt.");
    }

    private static string ProgramOwnedDistributionZoneName(CardZoneKind zone) => zone switch
    {
        CardZoneKind.Hand => "手牌",
        CardZoneKind.Equipment => "装备牌",
        _ => throw new InvalidOperationException("Unsupported owned-card distribution zone.")
    };
}
