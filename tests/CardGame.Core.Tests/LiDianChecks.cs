using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiDianChecks
{
    private const string Mode = "identity:li-dian-check-5";
    private const string FragileMode = "identity:classic-li-dian-lethal-check-5";

    public static void ClassicDefinitionAndPrivateReplacementReplay()
    {
        var registry = Registry();
        var general = registry.Generals["classic:li-dian"];
        Require(general.BaseHp == 3 && general.FactionId == "wei" &&
                general.SkillIds.SequenceEqual(["classic:xunxun", "classic:wangxi"]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains("classic:li-dian"),
            "Classic Li Dian must be an independent three-HP Wei identity general in the formal pool.");
        var xunxun = registry.Skills["classic:xunxun"].Program!;
        var wangxi = registry.Skills["classic:wangxi"].Program!;
        Require(xunxun.Triggers.Single().DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement &&
                wangxi.Triggers.Count == 2 &&
                wangxi.Triggers.All(trigger => trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint),
            "Both skills must have composable original-edition timing and per-point damage semantics.");
        Require(xunxun.Triggers.Single().Effects[1].AllowFewerWhenInsufficient &&
                !registry.Skills["classic:chengxiang"].Program!.Triggers.Single().Effects[1].AllowFewerWhenInsufficient &&
                CardSubsetSelector.Enumerate([new CardSubsetCandidate(1, 1)],
                    new CardSubsetConstraint(2, 2, 208)).Count == 0,
            "Depleted-card relaxation must be opt-in; existing exact-count selection remains strict.");
        RejectInvalidWangxiTargetReferences();

        var game = Start(registry);
        Reach(game, "classic:xunxun");
        Require(game.CreateSnapshot(1).PendingDecision is null, "Xunxun offer must be private.");
        var skipped = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(skipped, "skip");
        Require(skipped.State.Phase == TurnPhase.Play &&
                skipped.CardMovements.Count(move => move.To == CardLocation.Hand(0) &&
                    move.Reason.Value.Contains("classic:xunxun", StringComparison.Ordinal)) == 0,
            "Declining Xunxun must keep the ordinary draw path.");

        AnswerAction(game, "activate");
        var subset = Prompt(game);
        Require(subset.IsPrivate && subset.Choices.Count == 6 &&
                subset.Choices.All(choice => choice.Cards.Count == 2) &&
                game.CreateSnapshot(1).PendingDecision is null &&
                game.CreateSnapshot(1).PublicRevealedCards.Count == 0,
            "Xunxun must privately expose all six two-of-four choices without publishing card faces.");
        var subsetPaused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var pick = subset.Choices[1];
        Answer(game, pick);
        Answer(subsetPaused, Prompt(subsetPaused).Choices.Single(choice => choice.Id == pick.Id));
        var order = Prompt(game);
        Require(order.IsPrivate && order.Choices.Count == 2 &&
                order.Choices.All(choice => choice.Cards.Count == 2) &&
                order.Choices[0].Cards.SequenceEqual(order.Choices[1].Cards.Reverse()) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Remaining cards must offer both private bottom orders.");
        var orderPaused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        var chosenOrder = order.Choices[1];
        Answer(game, chosenOrder);
        Answer(subsetPaused, Prompt(subsetPaused).Choices.Single(choice => choice.Id == chosenOrder.Id));
        Answer(orderPaused, Prompt(orderPaused).Choices.Single(choice => choice.Id == chosenOrder.Id));
        var deck = game.CreateCardZoneDiagnostics().Where(card => card.Location == CardLocation.DrawPile)
            .OrderBy(card => card.ZoneIndex).ToArray();
        Require(game.State.Phase == TurnPhase.Play &&
                pick.Cards.All(id => game.CreateCardZoneDiagnostics().Any(card =>
                    card.CardId == id && card.Location == CardLocation.Hand(0))) &&
                deck.Take(2).Select(card => card.CardId).SequenceEqual(chosenOrder.Cards) &&
                game.ResolutionStack.Count == 0 &&
                State(game) == State(subsetPaused) && State(game) == State(orderPaused) &&
                Events(game).SequenceEqual(Events(subsetPaused)) &&
                Events(game).SequenceEqual(Events(orderPaused)),
            "Chosen two cards must enter hand, remaining two must retain selected bottom order, and both pauses must replay.");
    }

    public static void WangxiDamageCanBeAcceptedOrDeclinedAndReplayed()
    {
        var registry = Registry();
        var game = Start(registry);
        Reach(game, "classic:xunxun");
        AnswerAction(game, "skip");
        for (var step = 0; step < 20 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not reach Li Dian Play after declining Xunxun.");
        var play = Prompt(game);
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var target = slash.TargetSeats.Single();
        var before = game.CreateSnapshot(0, true).Players;
        var result = game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
            game.Revision, play.PromptId, slash.PlayedCardKind, slash.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Li Dian Slash failed.");
        for (var step = 0; step < 40 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi"; step++)
        {
            Require(game.PendingDecision is null, "Unexpected prompt before Wangxi.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not advance damage to Wangxi.");
        }
        var offer = Prompt(game);
        Require(offer.SkillPrompt?.SkillId == "classic:wangxi" && offer.IsPrivate &&
                game.CreateSnapshot(1).PendingDecision is null &&
                game.CreateSnapshot(0, true).Players[target].Hp == before[target].Hp - 1,
            "Dealing damage to a living other character must offer private Wangxi once.");
        var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(game, "activate");
        AnswerAction(paused, "activate");
        var after = game.CreateSnapshot(0, true).Players;
        Require(after[0].HandCount == before[0].HandCount &&
                after[target].HandCount == before[target].HandCount + 1 &&
                State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
            "Accepted Wangxi must draw one for each participant after one spent Slash and replay exactly.");
        WangxiDoesNotOfferAfterSelfLightningDamage();
        WangxiDoesNotOfferAfterLethalDamage();
        WangxiAfterNestedGanglieDamageReplays();
    }

    private static void WangxiAfterNestedGanglieDamageReplays()
    {
        var registry = Registry(ganglieTarget: true);
        GameEngine? selected = null;
        var ganglieSeat = -1;
        for (var seed = 1; seed <= 512 && selected is null; seed++)
        {
            var candidate = Start(registry, seed);
            Reach(candidate, "classic:xunxun");
            AnswerAction(candidate, "skip");
            ReachPlay(candidate);
            var xiahouDun = candidate.CreateSnapshot(0, true).Players.SingleOrDefault(player =>
                player.GeneralId == "classic:xiahou-dun");
            if (xiahouDun is null) continue;
            var slash = candidate.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeats.SequenceEqual([xiahouDun.Seat]));
            if (slash is null) continue;
            Play(candidate, slash);
            for (var step = 0; step < 80; step++)
            {
                if (candidate.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 })
                {
                    selected = candidate;
                    ganglieSeat = xiahouDun.Seat;
                    break;
                }
                if (candidate.PendingDecision?.SkillPrompt?.SkillId == "classic:wangxi")
                    AnswerAction(candidate, "skip");
                else if (candidate.PendingDecision is null)
                    Require(candidate.Submit(new AdvanceOneStepCommand(candidate.Revision)).Accepted,
                        "Could not advance to nested Ganglie punishment.");
                else break;
            }
        }
        Require(selected is not null, "No deterministic Li Dian Slash to successful Ganglie punishment was found.");
        var game = selected!;
        var punishment = Prompt(game);
        var loseHp = punishment.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "ganglie-lose-hp");
        Answer(game, loseHp);
        Require(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                item.SourceSeat == ganglieSeat && item.TargetSeat == 0 && item.Amount == 1),
            "The Ganglie punishment must apply real damage from Xiahou Dun to Li Dian.");
        for (var step = 0; step < 40 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi"; step++)
        {
            Require(game.PendingDecision is null,
                $"Unexpected {game.PendingDecision?.Kind} prompt before Wangxi after nested Ganglie damage.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not advance nested Ganglie damage to Wangxi.");
        }
        Require(Prompt(game).SkillPrompt?.SkillId == "classic:wangxi" &&
                game.CreateSnapshot(ganglieSeat).PendingDecision is null,
            "Surviving Li Dian must privately receive Wangxi after nested Ganglie damage.");
        var before = game.CreateSnapshot(0, true).Players;
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(game, "activate");
        AnswerAction(replay, "activate");
        var after = game.CreateSnapshot(0, true).Players;
        Require(after[0].HandCount == before[0].HandCount + 1 &&
                after[ganglieSeat].HandCount == before[ganglieSeat].HandCount + 1 &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Nested Ganglie damage must offer one Wangxi draw to each living participant and replay exactly.");
        static void FinishNestedDamage(GameEngine current)
        {
            for (var step = 0; current.ResolutionStack.Count > 0 && step < 40; step++)
            {
                Require(current.PendingDecision is null,
                    "Nested Ganglie damage exposed an extra prompt while closing its frames.");
                Require(current.Submit(new AdvanceOneStepCommand(current.Revision)).Accepted,
                    "Could not close nested Ganglie damage after Wangxi.");
            }
            Require(current.ResolutionStack.Count == 0 && current.State.ProcessingCardCount == 0,
                "Nested Ganglie damage left a live frame or processing card.");
        }
        FinishNestedDamage(game);
        FinishNestedDamage(replay);
        var nestedFrameId = game.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>()
            .Single(item => item.SourceSeat == ganglieSeat && item.TargetSeat == 0 &&
                item.Amount == 1 && item.SourceCard is null).ResolutionId;
        Require(game.Events.Select(item => item.Payload).OfType<AfterDamageEvent>()
                    .Count(item => item.ResolutionId == nestedFrameId) == 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.OwnerSeat == ganglieSeat && item.SkillId == "classic:ganglie" &&
                        item.Completed) == 1 &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Nested Ganglie damage and its outer punishment must each finish exactly once and replay.");
    }

    private static void RejectInvalidWangxiTargetReferences()
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string Resource(string suffix)
        {
            var name = assembly.GetManifestResourceNames().Single(item =>
                item.EndsWith(suffix, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var rules = Resource("classic-li-dian.rules.json");
        var presentation = Resource("classic-li-dian.presentation.json");
        void Reject(Action<JsonObject> mutate, string message)
        {
            var document = JsonNode.Parse(rules)!.AsObject();
            var wangxi = document["skills"]!.AsArray().Single(item =>
                item!["id"]!.GetValue<string>() == "classic:wangxi")!.AsObject();
            var draw = wangxi["triggers"]!.AsArray()[0]!["effects"]!.AsArray()[1]!.AsObject();
            mutate(draw);
            try { _ = SkillProgramCatalog.Load(document.ToJsonString(), presentation); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(message);
        }
        Reject(draw => draw["resultBind"] = "invalid-binding",
            "A targetRef draw must reject a result binding instead of silently binding the wrong participant.");
        Reject(draw => draw["target"] = "selectedTarget",
            "A targetRef draw must reject an additional selected target instead of overriding its recipient.");
    }

    public static void XunxunTakesOnlyAvailableCardFromDepletedDeck()
    {
        var registry = Registry(deckSize: 21);
        var game = Start(registry);
        Reach(game, "classic:xunxun");
        AnswerAction(game, "activate");
        Require(game.PendingDecision is not null,
            $"Depleted Xunxun made no choice: deck={game.CreateCardZoneDiagnostics().Count(card => card.Location == CardLocation.DrawPile)}, " +
            $"processing={game.CreateCardZoneDiagnostics().Count(card => card.Location == CardLocation.Processing)}, phase={game.State.Phase}.");
        var choice = Prompt(game);
        Require(choice.Choices.Count == 1 && choice.Choices[0].Cards.Count == 1 &&
                choice.IsPrivate && game.CreateSnapshot(1).PendingDecision is null,
            $"When only one card remains, Xunxun must privately select that available card: choices={choice.Choices.Count}, firstCount={choice.Choices.FirstOrDefault()?.Cards.Count}, processing={game.CreateCardZoneDiagnostics().Count(card => card.Location == CardLocation.Processing)}.");
        var cardId = choice.Choices[0].Cards.Single();
        Answer(game, choice.Choices[0]);
        Require(game.CreateCardZoneDiagnostics().Any(card =>
                card.CardId == cardId && card.Location == CardLocation.Hand(0)) &&
                game.State.Phase == TurnPhase.Play,
            "A depleted Xunxun must grant the available card and finish its replacement draw.");
    }

    public static void WangxiTwoPointDamageOffersTwoIndependentChoices()
    {
        var registry = Registry();
        GameEngine? game = null;
        for (var seed = 1; seed <= 128; seed++)
        {
            var candidate = Start(registry, seed);
            Reach(candidate, "classic:xunxun");
            AnswerAction(candidate, "skip");
            ReachPlay(candidate);
            if (candidate.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Alcohol) &&
                candidate.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash))
            {
                game = candidate;
                break;
            }
        }
        Require(game is not null, "No bounded fixture had Alcohol and Slash together.");
        var before = game!.CreateSnapshot(0, true).Players;
        var alcohol = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Alcohol);
        Play(game, alcohol);
        ReachPlay(game);
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var target = slash.TargetSeats.Single();
        Play(game, slash);
        ReachWangxi(game);
        var declined = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        for (var index = 0; index < 2; index++)
        {
            Require(Prompt(game).SkillPrompt?.SkillId == "classic:wangxi", "Each damage point must offer Wangxi.");
            AnswerAction(game, "activate");
            ReachWangxiIfPending(game, index);
            AnswerAction(declined, "skip");
            ReachWangxiIfPending(declined, index);
        }
        var used = game.CreateSnapshot(0, true).Players;
        var skipped = declined.CreateSnapshot(0, true).Players;
        Require(used[target].Hp == before[target].Hp - 2 &&
                used[0].HandCount == before[0].HandCount &&
                used[target].HandCount == before[target].HandCount + 2 &&
                skipped[0].HandCount == before[0].HandCount - 2 &&
                skipped[target].HandCount == before[target].HandCount &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == "classic:wangxi" && item.Activated) == 2,
            "Two damage points must offer two independently accepted or declined draw pairs.");
    }

    public static void WangxiTakenDamageOffersPrivateChoice()
    {
        var registry = Registry();
        var game = Start(registry);
        for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt?.SkillPrompt?.SkillId == "classic:wangxi")
            {
                var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .LastOrDefault();
                Require(damage is not null && damage.TargetSeat == 0 && damage.SourceSeat != 0 &&
                        prompt.IsPrivate && game.CreateSnapshot(1).PendingDecision is null,
                    "Taking damage from another living player must offer Li Dian a private Wangxi choice.");
                var sourceSeat = damage!.SourceSeat;
                var before = game.CreateSnapshot(0, true).Players;
                var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                AnswerAction(game, "activate");
                AnswerAction(paused, "activate");
                var after = game.CreateSnapshot(0, true).Players;
                Require(after[0].HandCount == before[0].HandCount + 1 &&
                        after[sourceSeat].HandCount == before[sourceSeat].HandCount + 1 &&
                        State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
                    "Taking-damage Wangxi must grant exactly one card to each participant and replay.");
                return;
            }
            if (prompt is { PlayerSeat: 0 })
            {
                if (prompt.Kind == DecisionKind.PlayCard)
                {
                    Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted,
                        "Could not end Li Dian Play while waiting for incoming damage.");
                    continue;
                }
                if (prompt.Kind == DecisionKind.ProgramTrigger &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                {
                    AnswerAction(game, "skip");
                    continue;
                }
                if (prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) is { } decline)
                {
                    Answer(game, decline);
                    continue;
                }
                throw new InvalidOperationException($"Unexpected human prompt {prompt.Kind} while waiting for damage.");
            }
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not advance AI turns toward incoming damage.");
        }
        throw new InvalidOperationException("No bounded AI turn dealt damage to Li Dian.");
    }

    private static void WangxiDoesNotOfferAfterLethalDamage()
    {
        var registry = Registry(fragileTargets: true);
        var game = Start(registry);
        Reach(game, "classic:xunxun");
        AnswerAction(game, "skip");
        ReachPlay(game);
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var targetSeat = slash.TargetSeats.Single();
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Play(game, slash);
        Play(replay, replay.GetHumanLegalActions().Single(action =>
            action.Kind == slash.Kind && action.CardId == slash.CardId &&
            action.TargetSeats.SequenceEqual(slash.TargetSeats)));

        static void Finish(GameEngine current, int victim)
        {
            for (var step = 0; step < 80; step++)
            {
                Require(current.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi",
                    "Lethal damage must finish dying rescue before any Wangxi choice; an unrescued dead participant cannot draw.");
                if (current.Events.Any(item => item.Payload is PlayerDiedEvent died && died.VictimSeat == victim))
                {
                    Require(!current.CreateSnapshot(0, true).Players[victim].IsAlive,
                        "The lethal-damage victim must really be dead before the fixture finishes.");
                    return;
                }
                if (current.PendingDecision is { PlayerSeat: 0 } pending)
                {
                    var decline = pending.Choices.FirstOrDefault(choice => choice.Cards.Count == 0);
                    Require(decline is not null, "Lethal-damage fixture needs a legal decline while rescuing.");
                    Answer(current, decline!);
                }
                else
                {
                    Require(current.Submit(new AdvanceOneStepCommand(current.Revision)).Accepted,
                        "Could not finish the lethal-damage window.");
                }
            }
            throw new InvalidOperationException("Lethal-damage fixture did not finish the victim's death.");
        }
        Finish(game, targetSeat);
        Finish(replay, targetSeat);
        Require(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == targetSeat && item.RemainingHp == 0) &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "The real lethal damage, dying-window choice and completed death must replay exactly.");
    }

    private static void WangxiDoesNotOfferAfterSelfLightningDamage()
    {
        var registry = Registry(lightningOnly: true);
        var game = Start(registry);
        Reach(game, "classic:xunxun");
        AnswerAction(game, "skip");
        ReachPlay(game);
        var lightning = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Lightning && action.TargetSeats.SequenceEqual([0]));
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Play(game, lightning);
        Play(replay, replay.GetHumanLegalActions().Single(action =>
            action.Kind == lightning.Kind && action.CardId == lightning.CardId &&
            action.TargetSeats.SequenceEqual(lightning.TargetSeats)));

        static void Finish(GameEngine current)
        {
            for (var step = 0; step < 600; step++)
            {
                Require(current.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi",
                    "Self-inflicted Lightning damage must not offer Wangxi.");
                if (current.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == 0 && item.Amount == 3))
                    return;
                if (current.PendingDecision is { PlayerSeat: 0 } pending)
                {
                    if (pending.Kind == DecisionKind.PlayCard)
                        Require(current.Submit(new EndPlayPhaseCommand(0, current.Revision, pending.PromptId)).Accepted,
                            "Could not end Li Dian's Play before Lightning judgment.");
                    else if (pending.Choices.FirstOrDefault(choice =>
                                 choice.Parameters.GetValueOrDefault("program-action") == "skip" ||
                                 choice.Cards.Count == 0) is { } decline)
                        Answer(current, decline);
                    else
                        throw new InvalidOperationException($"Unexpected Lightning fixture prompt {pending.Kind}.");
                }
                else
                {
                    Require(current.Submit(new AdvanceOneStepCommand(current.Revision)).Accepted,
                        "Could not advance to Li Dian's Lightning judgment.");
                }
            }
            throw new InvalidOperationException("Lightning fixture did not inflict self damage within 600 steps.");
        }
        Finish(game);
        Finish(replay);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Real self-inflicted Lightning damage without Wangxi must replay exactly.");
    }

    private static void ReachWangxiIfPending(GameEngine game, int resolvedIndex)
    {
        if (resolvedIndex == 1) return;
        ReachWangxi(game);
    }

    private static void ReachWangxi(GameEngine game)
    {
        for (var step = 0; step < 40 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi"; step++)
        {
            Require(game.PendingDecision is null, "Unexpected prompt before Wangxi.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not advance damage to Wangxi.");
        }
        Require(Prompt(game).SkillPrompt?.SkillId == "classic:wangxi", "Wangxi offer was not reached.");
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 20 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not reach Li Dian Play.");
        Require(Prompt(game).Kind == DecisionKind.PlayCard, "Li Dian Play was not reached.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, Prompt(game).PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Li Dian card play failed.");
    }

    private static ContentRegistry Registry(int deckSize = 160, bool fragileTargets = false,
        bool lightningOnly = false, bool ganglieTarget = false) => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new Scenario(deckSize, fragileTargets, lightningOnly, ganglieTarget));

    private static GameEngine Start(ContentRegistry registry, int seed = 142)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5,
            ModeId = registry.Modes.ContainsKey(FragileMode) ? FragileMode : Mode,
            HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Li Dian fixture start failed.");
        var select = Prompt(game);
        Require(select.ValidContentIds.Contains("classic:li-dian"), "Li Dian was not offered.");
        Require(game.Submit(new SelectGeneralCommand(0, "classic:li-dian", game.Revision,
            select.PromptId)).Accepted, "Li Dian selection failed.");
        return game;
    }

    private static void Reach(GameEngine game, string skillId)
    {
        for (var step = 0; step < 30 && game.PendingDecision?.SkillPrompt?.SkillId != skillId; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not reach Li Dian draw offer.");
        Require(game.PendingDecision?.SkillPrompt?.SkillId == skillId,
            "Li Dian draw offer was not reached.");
    }

    private static PendingDecision Prompt(GameEngine game) =>
        game.PendingDecision ?? throw new InvalidOperationException("Li Dian fixture lost its prompt.");

    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = Prompt(game);
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Li Dian answer failed.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(int deckSize, bool fragileTargets, bool lightningOnly,
        bool ganglieTarget) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("li-dian-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:li-dian-deck", "Li Dian Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, deckSize).Select(index =>
                    lightningOnly
                        ? new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 2)
                        : new ContentDeckPhysicalCard(index % 7 == 0 ? "standard:alcohol" : "standard:slash",
                            (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            if (fragileTargets)
                foreach (var index in Enumerable.Range(1, 4))
                    builder.AddGeneral(new ContentGeneralDefinition(
                        $"fixture:li-dian-target-{index}", "忘隙击杀目标", "supporter",
                        "standard:none", "wei", BaseHp: 1));
            builder.AddMode(new ContentModeDefinition(fragileTargets ? FragileMode : Mode, "Li Dian", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:li-dian-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: fragileTargets
                    ? ["classic:li-dian", "fixture:li-dian-target-1", "fixture:li-dian-target-2",
                        "fixture:li-dian-target-3", "fixture:li-dian-target-4"]
                    : ganglieTarget
                        ? ["classic:li-dian", "classic:xiahou-dun", "classic:guan-yu",
                            "classic:zhang-fei", "classic:sun-quan"]
                        : ["classic:li-dian", "classic:liu-bei", "classic:guan-yu",
                            "classic:zhang-fei", "classic:sun-quan"]));
        }
    }
}
