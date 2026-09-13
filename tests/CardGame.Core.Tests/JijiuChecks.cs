using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JijiuChecks
{
    public static void ContentContract()
    {
        var active = StandardContentRegistry.CreateWithActiveSkills();
        var rescue = StandardContentRegistry.CreateWithRescueSkills();

        Require(!active.Skills.ContainsKey("standard:jijiu"),
            "The legacy active-skill package must not silently gain Jijiu.");
        Require(!active.Generals.ContainsKey("standard:demo-jijiu"),
            "The legacy active-skill general pool must not silently gain Jijiu.");
        Require(rescue.Packages.Select(package => $"{package.Id}@{package.Version}")
            .SequenceEqual([
                "standard@1.11.0",
                "standard-active-skills@1.0.0",
                "standard-rescue-skills@1.0.0"]),
            "The rescue package signature must be explicit and dependency ordered.");
        Require(rescue.Skills.TryGetValue("standard:jijiu", out var contentSkill) &&
                contentSkill.LegacyKind == SkillKind.Jijiu &&
                contentSkill.Description.Contains("红色牌", StringComparison.Ordinal),
            "The Jijiu content definition must expose its typed legacy projection.");
        Require(rescue.Generals.TryGetValue("standard:demo-jijiu", out var contentGeneral) &&
                contentGeneral.SkillId == "standard:jijiu",
            "The demo general must point at the namespaced Jijiu definition.");
        Require(!active.Modes["identity:active-skills-5"].GeneralPoolIds!
            .Contains("standard:demo-jijiu", StringComparer.Ordinal),
            "The old active-skill mode must retain its original general pool.");
        Require(rescue.Modes["identity:active-skills-5"].GeneralPoolIds!
            .Contains("standard:demo-jijiu", StringComparer.Ordinal),
            "The rescue-enabled mode must publish the Jijiu general.");

        var skill = SkillRegistry.Get(SkillKind.Jijiu);
        var context = new PlayerSkillContext(0, 2, 4, 3, TurnPhase.Play);
        var redHeart = new Card(101, CardKind.Slash, Suit.Heart, 7);
        var redDiamond = new Card(102, CardKind.Alcohol, Suit.Diamond, 9);
        var blackCard = new Card(103, CardKind.Slash, Suit.Spade, 7);
        var peach = new Card(104, CardKind.Peach, Suit.Heart, 3);
        Require(skill.CanUseAsDyingRescue(context, redHeart) &&
                skill.CanUseAsDyingRescue(context, redDiamond),
            "Jijiu must accept red physical cards as dying Peach candidates.");
        Require(!skill.CanUseAsDyingRescue(context, blackCard) &&
                !skill.CanUseAsDyingRescue(context, peach),
            "Jijiu must reject black cards and leave native Peach to the base rule.");

        var self = new PlayerSnapshot(
            Seat: 0,
            Name: "AI",
            IsHuman: false,
            Role: Role.Lord,
            IsRoleRevealed: true,
            GeneralId: "standard:demo-jijiu",
            GeneralName: "急救者",
            PortraitKey: "hua_tuo",
            Skill: SkillKind.Jijiu,
            SkillName: "急救",
            SkillDescription: "濒死窗口可将一张红色牌当作桃使用。",
            Hp: 3,
            MaxHp: 4,
            IsAlive: true,
            HandCount: 1,
            Hand: [new CardSnapshot(redHeart.Id, redHeart.Kind, redHeart.Suit, redHeart.Rank, redHeart.DisplayName, redHeart.RankText)]);
        var victim = self with
        {
            Seat = 1,
            Name = "濒死角色",
            Role = Role.Loyalist,
            IsRoleRevealed = true,
            GeneralId = "liu-bei",
            GeneralName = "刘备",
            PortraitKey = "liu_bei",
            Skill = SkillKind.None,
            SkillName = "无",
            SkillDescription = "",
            Hp = 0,
            HandCount = 0,
            Hand = []
        };
        var view = new GameSnapshot(
            Seed: null,
            HumanSeat: -1,
            Status: EngineStatus.Running,
            Winner: Winner.None,
            TurnNumber: 1,
            CurrentSeat: 0,
            Phase: TurnPhase.Play,
            DrawPileCount: 10,
            DiscardPileCount: 0,
            Players: [self, victim],
            PendingDecision: null);
        var ai = new SimpleAiBrain(0, 41, policyVersion: 2);
        var decision = ai.ChooseDyingResponseWithAlcohol(
            view,
            victimSeat: 1,
            peaches: [redHeart],
            alcohols: [],
            thoughtSequence: 1);
        Require(decision.UsePeach && decision.PeachCardId == redHeart.Id,
            "AI must choose an available red Jijiu candidate for an allied dying seat.");
        Require(decision.Thought.Candidates.Any(candidate =>
                candidate.Action.CardId == redHeart.Id &&
                candidate.Action.Description.Contains("当桃", StringComparison.Ordinal)),
            "AI thought text must identify the converted physical card without exposing hidden state.");
    }

    public static void DyingFlow()
    {
        var registry = StandardContentRegistry.CreateWithRescueSkills();
        var (game, prompt, convertedCard) = FindConvertedPrompt(registry);
        var dyingFrame = game.ResolutionStack.OfType<DyingFrame>().Single();
        var convertedChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "peach" &&
            choice.Cards.Contains(convertedCard.Id));

        Require(prompt.Kind == DecisionKind.RescueDying &&
                prompt.PlayerSeat == 0 &&
                prompt.TargetSeat is not null,
            "Jijiu must reuse the private dying decision contract.");
        Require(convertedChoice.Description.Contains("当作【桃】", StringComparison.Ordinal) &&
                convertedChoice.Parameters["physical-card-kind"] == convertedCard.Kind.ToString(),
            "The private choice must explain the physical-to-effective card mapping.");

        var observer = game.CreateSnapshot(1);
        Require(observer.PendingDecision is null &&
                observer.Players.All(player => player.Hand.All(card => card.Id != convertedCard.Id)),
            "The converted physical card must remain hidden from an ordinary observer.");

        var beforeInvalid = game.SerializeState();
        var invalid = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            new ChoiceId("dying.jijiu.invalid"),
            game.Revision));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidChoice,
            "An unknown Jijiu dying choice must be rejected at the command boundary.");
        Require(beforeInvalid == game.SerializeState(),
            "Rejecting an invalid Jijiu choice must not mutate state.");

        var accepted = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            convertedChoice.Id,
            game.Revision));
        Require(accepted.Accepted,
            accepted.Error?.Message ?? "The converted Jijiu rescue was rejected.");

        var events = game.Events.Select(item => item.Payload).ToArray();
        var response = events.OfType<DyingResponseEvent>().Single(item =>
            item.ResolutionId == dyingFrame.Id && item.ResponderSeat == 0);
        Require(response.UsedPeach &&
                response.PeachCardId == convertedCard.Id &&
                response.UsedPeachPhysicalCardKind == convertedCard.Kind,
            "The typed dying event must retain both effective and physical card kinds.");
        Require(events.OfType<CardUseDeclaredEvent>().Any(item =>
                item.CardId == convertedCard.Id && item.CardKind == CardKind.Peach),
            "The recovery resolution must publish Peach as its effective card kind.");
        Require(events.OfType<CardUseFinishedEvent>().Any(item =>
                item.CardId == convertedCard.Id && item.CardKind == CardKind.Peach),
            "The converted recovery must finish through the normal card-use lifecycle.");
        Require(events.OfType<RecoveryAppliedEvent>().Any(item =>
                item.SourceSeat == 0 &&
                item.TargetSeat == prompt.TargetSeat &&
                item.Amount == 1 &&
                item.RemainingHp > 0),
            "Jijiu must recover the dying target through the shared recovery effect.");
        Require(game.CardMovements.Any(movement =>
                movement.CardId == convertedCard.Id &&
                movement.From == CardLocation.Hand(0) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.Use) &&
                game.CardMovements.Any(movement =>
                movement.CardId == convertedCard.Id &&
                movement.From == CardLocation.Processing &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason == CardMoveReasons.UseFinished),
            "The physical red card must follow the ordinary Hand -> Processing -> Discard path.");
        Require(game.ResolutionStack.All(frame => frame.Id != dyingFrame.Id),
            "A successful Jijiu rescue must close its dying frame.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "The converted rescue must restore with the same state and typed event stream.");
        AssertInventory(game);
        AssertInventory(restored);
    }

    private static (GameEngine Game, PendingDecision Prompt, Card ConvertedCard) FindConvertedPrompt(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(
                new GameOptions
                {
                    Seed = seed,
                    PlayerCount = 5,
                    ModeId = "identity:active-skills-5",
                    HumanSeat = 0,
                    HumanRole = Role.Lord,
                    UseInteractiveDiscard = false,
                    MaxTurns = 180
                },
                registry);
            var result = game.Submit(new StartGameCommand());
            if (!result.Accepted)
            {
                throw new InvalidOperationException(result.Error?.Message ?? "The Jijiu fixture failed to start.");
            }

            for (var step = 0; result.Status != EngineStatus.Completed && step < 3_000; step++)
            {
                if (result.Status == EngineStatus.AwaitingHumanDying &&
                    game.PendingDecision is { } prompt &&
                    game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Skill == SkillKind.Jijiu)
                {
                    var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
                    var converted = prompt.Choices
                        .Where(choice => choice.Parameters.GetValueOrDefault("response") == "peach")
                        .Select(choice => choice.Cards.SingleOrDefault(cardId =>
                            hand.Any(card => card.Id == cardId &&
                                card.Kind != CardKind.Peach &&
                                card.Suit is Suit.Heart or Suit.Diamond)))
                        .Where(cardId => cardId != 0)
                        .Select(cardId => hand.Single(card => card.Id == cardId))
                        .FirstOrDefault();
                    if (converted is not null)
                    {
                        return (game, prompt, new Card(
                            converted.Id,
                            converted.Kind,
                            converted.Suit,
                            converted.Rank));
                    }
                }

                result = Continue(game, result);
            }
        }

        throw new InvalidOperationException("No deterministic Jijiu dying prompt was found.");
    }

    private static CommandResult Continue(GameEngine game, CommandResult current)
    {
        if (current.Status == EngineStatus.AwaitingHumanPlay)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing play prompt.");
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
        }

        if (current.Status == EngineStatus.AwaitingHumanResponse ||
            current.Status == EngineStatus.AwaitingHumanDying ||
            current.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing human prompt.");
            var choice = prompt.Choices.FirstOrDefault(item =>
                             item.Parameters.GetValueOrDefault("response") == "take-damage" ||
                             item.Parameters.GetValueOrDefault("response") == "let-die") ??
                         prompt.Choices.FirstOrDefault() ??
                         throw new InvalidOperationException("The human prompt has no choices.");
            return game.Submit(new AnswerPromptCommand(
                prompt.PlayerSeat,
                prompt.PromptId,
                choice.Id,
                game.Revision));
        }

        if (current.Status == EngineStatus.AwaitingHumanDiscard)
        {
            var prompt = game.PendingDecision ?? throw new InvalidOperationException("Missing discard prompt.");
            return game.Submit(new DiscardCardsCommand(
                prompt.PlayerSeat,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision));
        }

        return game.Submit(new AdvanceOneStepCommand(game.Revision));
    }

    private static void AssertInventory(GameEngine game)
    {
        var diagnostics = game.CreateCardZoneDiagnostics();
        var snapshot = game.CreateSnapshot(0, revealAll: true);
        Require(diagnostics.Count == diagnostics.Select(card => card.CardId).Distinct().Count(),
            "The Jijiu fixture duplicated a physical card.");
        Require(snapshot.DrawPileCount == diagnostics.Count(card => card.Location == CardLocation.DrawPile) &&
                snapshot.DiscardPileCount == diagnostics.Count(card => card.Location == CardLocation.DiscardPile) &&
                snapshot.ProcessingCardCount == diagnostics.Count(card => card.Location == CardLocation.Processing),
            "The Jijiu fixture's shared zone counts diverged from its snapshot.");
        foreach (var player in snapshot.Players)
        {
            var hand = diagnostics
                .Where(card => card.Location == CardLocation.Hand(player.Seat))
                .Select(card => card.CardId)
                .OrderBy(cardId => cardId);
            Require(hand.SequenceEqual(player.Hand.Select(card => card.Id).OrderBy(cardId => cardId)),
                "The Jijiu fixture's hand projection diverged from its zone store.");
        }
    }

    private static string EventSignature(EventEnvelope eventItem) =>
        JsonSerializer.Serialize(new
        {
            eventItem.Id,
            eventItem.ParentId,
            eventItem.Sequence,
            eventItem.Revision,
            eventItem.CorrelationId,
            Payload = JsonSerializer.Serialize(eventItem.Payload, eventItem.Payload.GetType())
        });

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
