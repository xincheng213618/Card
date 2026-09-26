using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryGanNingChecks
{
    private const string GeneralId = "boundary:gan-ning";
    private const string SkillId = "boundary:fenwei";

    public static void DefinitionAndReusableSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var old = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 139, 0));
        var general = current.Generals[GeneralId];
        var skill = current.Skills[SkillId];
        var trigger = skill.Program!.Triggers.Single();
        Require(!old.Generals.ContainsKey(GeneralId) && !old.Skills.ContainsKey(SkillId) &&
            old.Generals["classic:gan-ning"].SkillIds.SequenceEqual(["classic:qixi"]) &&
            general.Name == "界甘宁" && general.BaseHp == 4 && general.FactionId == "wu" &&
            general.SkillIds.SequenceEqual(["boundary:qixi", SkillId]) &&
            current.Skills["boundary:qixi"].LegacyKind == SkillKind.Qixi &&
            current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
            current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
            !current.Modes["identity:classic-boundary-5"].GeneralPoolIds!.Contains(GeneralId) &&
            skill.Tags.HasFlag(SkillTag.Limited) &&
            trigger is { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                OwnerRelation: SkillProgramCardActionOwnerRelation.Observer,
                Optional: true, UsageScope: SkillUsageScope.Game, UsageLimit: 1,
                Effects: [{ Op: SkillProgramTriggerEffectOp.SelectTargets,
                    TargetKind: SkillProgramTargetKind.CurrentCardUseTargets },
                    { Op: SkillProgramTriggerEffectOp.NullifySelectedCardEffects }] } &&
            trigger.CardCategories.SequenceEqual([SkillProgramCardCategory.Trick]),
            "Boundary Gan Ning must have separate 1.140 skills, a limited generic trick observer and formal pools.");

        var rules = Resource("boundary-gan-ning.rules.json");
        var presentation = Resource("boundary-gan-ning.presentation.json");
        Reject(rules.Replace("\"schemaVersion\": 55", "\"schemaVersion\": 54", StringComparison.Ordinal),
            presentation);
        var generic = JsonNode.Parse(rules)!;
        generic["skills"]![0]!["id"] = "fixture:intervention";
        var gTrigger = generic["skills"]![0]!["triggers"]![0]!;
        gTrigger.AsObject().Remove("usageScope");
        gTrigger.AsObject().Remove("usageLimit");
        gTrigger["effects"]!.AsArray().Insert(1, JsonNode.Parse("""
            {"op":"draw","target":"owner","amount":1}
            """));
        var genericPresentation = """
            {"schemaVersion":1,"skills":{"fixture:intervention":{"name":"公共组合","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(generic.ToJsonString(), genericPresentation).Programs
                .ContainsKey("fixture:intervention"),
            "The card target source and nullification node must compose in a non-limited graph with another effect.");
        var emptyIntersection = JsonNode.Parse(generic.ToJsonString())!;
        emptyIntersection["skills"]![0]!["triggers"]![0]!["cardKinds"] =
            JsonNode.Parse("[\"slash\"]");
        Require(SkillProgramCatalog.Load(emptyIntersection.ToJsonString(), genericPresentation).Programs
                .ContainsKey("fixture:intervention"),
            "Independent card kind and category restrictions may have an empty runtime intersection.");
        var noSelection = JsonNode.Parse(generic.ToJsonString())!;
        noSelection["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray().RemoveAt(0);
        Reject(noSelection.ToJsonString(), genericPresentation);
        var wrongWindow = JsonNode.Parse(generic.ToJsonString())!;
        wrongWindow["skills"]![0]!["triggers"]![0]!["window"] = "turnEnding";
        Reject(wrongWindow.ToJsonString(), genericPresentation);
        var oldDelayedRules = """
            {"schemaVersion":54,"skills":[{"id":"fixture:old-delayed-observer","revision":1,
            "minimumRulesVersion":164,"triggers":[{"id":"observe-delayed",
            "window":"cardUseBeforeTargetEffects","ownerRelation":"observer",
            "cardKinds":["indulgence"],"optional":false,
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        var oldDelayedPresentation = """
            {"schemaVersion":1,"skills":{"fixture:old-delayed-observer":{"name":"旧延时观察","description":"测试"}}}
            """;
        var oldDelayedRejected = false;
        try { _ = SkillProgramCatalog.Load(oldDelayedRules, oldDelayedPresentation); }
        catch (InvalidOperationException exception)
        {
            oldDelayedRejected = exception.Message.Contains(
                "contains a card kind unsupported by cardUseBeforeTargetEffects", StringComparison.Ordinal);
        }
        Require(oldDelayedRejected,
            "Schema 54 must retain its old rejection of an exact delayed card kind in this window.");
    }

    public static void MultiTargetAssaultSubsetAndReplay()
    {
        var registry = Registry("standard:barbarian_assault");
        var game = Start(registry);
        ReachPlay(game);
        var action = game.GetHumanLegalActions().FirstOrDefault(item => item.Kind == LegalActionKind.BarbarianAssault);
        Require(action is not null,
            "Assault fixture has no legal card: " + string.Join(",",
                game.GetHumanLegalActions().Select(item => item.Kind)) + " | " +
            string.Join(",", game.CreateSnapshot(0, true).Players[0].Hand.Select(item => item.Kind)));
        Play(game, action!);
        var prompt = game.PendingDecision!;
        Require(prompt.SkillPrompt?.SkillId == SkillId && prompt.IsPrivate &&
            prompt.Choices.Select(item => item.Parameters.GetValueOrDefault("program-action"))
                .Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"]),
            "A real multi-target trick must offer the optional Fenwei observer before target effects.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var resumed = GameReplay.Restore(checkpoint, registry);
        Require(State(resumed) == State(game) && resumed.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Fenwei activation must survive checkpoint and replay.");
        Answer(game, "activate");
        var subset = game.PendingDecision!;
        Require(subset.SkillPrompt?.SkillId == SkillId && subset.IsPrivate &&
            subset.ValidTargetSeats.Count == 4 &&
            subset.Choices.Any(choice => choice.Targets.Count == 4) &&
            subset.Choices.All(choice => choice.Targets.Count is >= 1 and <= 4 &&
                choice.Parameters.GetValueOrDefault("maximum-targets") == "4"),
            "Fenwei must offer every nonempty subset up to the actual frozen target count.");
        var duringSelection = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(duringSelection) == State(game) &&
            duringSelection.PendingDecision?.Choices.Count == subset.Choices.Count,
            "The suspended public target set must replay exactly.");
        var invalidRevision = game.Revision;
        var invalid = game.Submit(new AnswerPromptCommand(0, subset.PromptId,
            new ChoiceId("program-targets.forged"), invalidRevision));
        Require(!invalid.Accepted && game.Revision == invalidRevision,
            "A forged cross-card target answer must fail atomically.");
        var selected = subset.Choices.Single(choice => choice.Targets.SequenceEqual([1, 2]));
        Answer(game, selected);
        var marked = game.Events.Select(item => item.Payload)
            .OfType<ProgramSelectedCardEffectsNullifiedEvent>().Single();
        Require(marked.TargetSeats.SequenceEqual([1, 2]),
            "Fenwei must record exactly the selected seats without deleting the other targets.");
        var afterMark = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(afterMark) == State(game), "Marked card effects must checkpoint after the original trick resumes.");
        AdvanceToResolved(game);
        Require(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Where(item => item.SourceSeat == 0).Select(item => item.TargetSeat).SequenceEqual([3, 4]) &&
            game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                .Count(item => item.SkillId == SkillId && item.Activated) == 1 &&
            game.ResolutionStack.Count == 0 && State(afterMark) == State(game),
            "The untouched assault targets must resolve in order while protected targets take no damage.");
    }

    public static void FrozenDesignationSurvivesEarlierObserverDeath()
    {
        var registry = Registry("standard:barbarian_assault", preEffectDeath: true);
        var game = Start(registry);
        ReachPlay(game);
        var doomedSeat = game.CreateSnapshot(0, true).Players.Single(player =>
            player.GeneralId == "fixture:fenwei-target-1").Seat;
        Play(game, game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.BarbarianAssault));
        for (var step = 0; step < 30 && game.PendingDecision?.SkillPrompt?.SkillId != SkillId; step++)
            Advance(game);
        var prompt = game.PendingDecision!;
        var window = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single();
        Require(!game.CreateSnapshot(0, true).Players[doomedSeat].IsAlive &&
            prompt.SkillPrompt?.SkillId == SkillId &&
            window.Action.DesignatedTargetSeats?.Count == 4 &&
            window.Candidates.Single(candidate => candidate.SkillId == SkillId)
                .FrozenContext?.Facts?.CardUseDesignatedTargetCount == 4,
            $"A prior observer killing one target must not rewrite the four targets fixed at card use: " +
            $"alive={game.CreateSnapshot(0, true).Players[doomedSeat].IsAlive}, " +
            $"prompt={prompt?.SkillPrompt?.SkillId}, designated={window.Action.DesignatedTargetSeats?.Count}, " +
            $"fact={window.Candidates.FirstOrDefault(candidate => candidate.SkillId == SkillId)?.FrozenContext?.Facts?.CardUseDesignatedTargetCount}, " +
            $"candidates={string.Join(',', window.Candidates.Select(candidate => candidate.SkillId))}.");
        Answer(game, "activate");
        var subset = game.PendingDecision!;
        Require(subset.ValidTargetSeats.Count == 3 &&
            !subset.ValidTargetSeats.Contains(doomedSeat) &&
            subset.Choices.All(choice => choice.Parameters.GetValueOrDefault("maximum-targets") == "3"),
            "After the death, selection must filter the dead seat while retaining the original trigger count.");
        Answer(game, subset.Choices.First(choice => choice.Targets.Count == 1));
        Require(game.Events.Select(item => item.Payload)
                .OfType<ProgramSelectedCardEffectsNullifiedEvent>().Count() == 1,
            "The surviving target subset must still be nullified through the real suspended card use.");
    }

    public static void FiveGrainsDeclineUseLimitAndPartialEffects()
    {
        var registry = Registry("standard:five_grains");
        var game = Start(registry);
        ReachPlay(game);
        var initialHands = game.CreateSnapshot(0, true).Players.Select(player => player.HandCount).ToArray();
        Play(game, game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.FiveGrains));
        Require(game.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Five Grains must expose Fenwei before the public harvest is revealed.");
        var declined = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Answer(declined, "skip");
        if (declined.PendingDecision is { Kind: DecisionKind.SelectHarvestCard } harvest)
            Answer(declined, harvest.Choices[0]);
        for (var step = 0; step < 50 && declined.ResolutionStack.Count > 0; step++)
            Advance(declined);
        ReachPlay(declined);
        var declinedSecond = declined.GetHumanLegalActions().FirstOrDefault(item =>
            item.Kind == LegalActionKind.FiveGrains);
        Require(declinedSecond is not null, "Decline branch needs a second Five Grains card: " +
            $"phase={declined.State.Phase}, decision={declined.PendingDecision?.Kind}, " +
            $"hand={string.Join(',', declined.CreateSnapshot(0, true).Players[0].Hand.Select(item => item.Kind))}, " +
            $"frames={declined.ResolutionStack.Count}.");
        Play(declined, declinedSecond!);
        Require(declined.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Declining Fenwei must leave its limited use unspent on the next trick.");
        Answer(game, "activate");
        var subset = game.PendingDecision!;
        Require(subset.ValidTargetSeats.Count == 5 && subset.Choices.Any(choice => choice.Targets.Count == 5),
            "Five Grains must include its user and every other living designated target.");
        Answer(game, subset.Choices.Single(choice => choice.Targets.SequenceEqual([0, 1])));
        for (var step = 0; step < 50 && game.ResolutionStack.Count > 0; step++)
            Advance(game);
        ReachPlay(game);
        var after = game.CreateSnapshot(0, true).Players;
        Require(game.Events.Select(item => item.Payload).OfType<CardsRevealedEvent>()
                .Any(item => item.Cards.Count == 5) &&
            after[0].HandCount == initialHands[0] - 1 &&
            after[1].HandCount == initialHands[1] &&
            Enumerable.Range(2, 3).All(seat => after[seat].HandCount == initialHands[seat] + 1) &&
            game.Events.Select(item => item.Payload).OfType<ProgramSelectedCardEffectsNullifiedEvent>()
                .Single().TargetSeats.SequenceEqual([0, 1]),
            "Five Grains must reveal five original cards, skip only protected picks and keep later picks in order.");

        var second = game.GetHumanLegalActions().FirstOrDefault(item => item.Kind == LegalActionKind.FiveGrains);
        Require(second is not null, "Five Grains fixture needs a second card to prove the limited use.");
        Play(game, second!);
        Require(game.PendingDecision?.SkillPrompt?.SkillId != SkillId &&
            game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .Count(item => item.SkillId == SkillId) == 1,
            "Fenwei must not be offered again after its first activation in the same game.");
    }

    public static void IronChainAndSingleTargetBoundary()
    {
        var ironRegistry = Registry("standard:iron_chain");
        var chain = Start(ironRegistry);
        ReachPlay(chain);
        var two = chain.GetHumanLegalActions().First(item =>
            item.Kind == LegalActionKind.IronChain && item.TargetSeats.Count == 2);
        Play(chain, two);
        Require(chain.PendingDecision?.SkillPrompt?.SkillId == SkillId &&
            chain.ResolutionStack.OfType<CardUseFrame>().Single().Action?.DesignatedTargetSeats?.Count == 2,
            "A two-target Iron Chain must qualify through the actual designated target set.");
        Answer(chain, "skip");

        var oneRegistry = Registry("standard:dismantlement");
        var one = Start(oneRegistry);
        ReachPlay(one);
        var dismantlement = one.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Dismantlement);
        Play(one, dismantlement);
        Require(one.PendingDecision?.SkillPrompt?.SkillId != SkillId &&
            one.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .All(item => item.SkillId != SkillId),
            "A one-target trick must not expose Fenwei.");

        var delayedRegistry = Registry("standard:indulgence", delayedTrickObserver: true);
        var delayed = Start(delayedRegistry);
        ReachPlay(delayed);
        var indulgence = delayed.GetHumanLegalActions().First(item =>
            item.Kind == LegalActionKind.Indulgence && item.TargetSeat == 2);
        Play(delayed, indulgence);
        AdvanceToResolved(delayed);
        Require(delayed.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .Any(item => item.SkillId == "fixture:delayed-trick-observer") &&
            delayed.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>()
                .Any(item => item.CardId == indulgence.CardId && item.TargetSeat == 2 &&
                    item.CardKind == CardKind.Indulgence) &&
            delayed.CreateSnapshot(0, true).Players[2].Judgment.Any(item =>
                item.Id == indulgence.CardId && item.Kind == CardKind.Indulgence),
            "A legitimate delayed trick observer must resume the card use and preserve placement.");
    }

    public static void BorrowedSwordCountsOnlyWeaponOwner()
    {
        var registry = Registry("fixture:borrowed-mix");
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            if (game.CreateSnapshot(0, true).Players[0].Hand.All(card =>
                    card.Kind != CardKind.BorrowedSword)) continue;
            // Place an ordinary weapon as fixture setup; the card use itself remains a real command.
            var weapon = game.CreateCardZoneDiagnostics().First(item => item.CardKind == CardKind.Crossbow &&
                item.Location.Zone == CardZoneKind.DrawPile);
            var store = typeof(GameEngine).GetField("_cardZones",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(game)!;
            _ = store.GetType().GetMethod("Move")!.Invoke(store,
                [weapon.CardId, weapon.Location, CardLocation.Equipment(1)]);
            var counter = game.CreateCardZoneDiagnostics().First(item =>
                item.CardKind == CardKind.Nullification && item.Location.Zone == CardZoneKind.DrawPile);
            _ = store.GetType().GetMethod("Move")!.Invoke(store,
                [counter.CardId, counter.Location, CardLocation.Hand(0)]);
            var borrowed = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.BorrowedSword && item.TargetSeats[0] == 1);
            Require(borrowed is not null, "The prepared weapon owner must be a legal Borrowed Sword target: " +
                $"actions={string.Join(',', game.GetHumanLegalActions().Select(item => item.Kind))}, " +
                $"equipment={game.CreateSnapshot(0, true).Players[1].Equipment.Count}.");
            Play(game, borrowed!);
            var frame = game.ResolutionStack.OfType<CardUseFrame>().Single(item =>
                item.CardKind == CardKind.BorrowedSword);
            Require(frame.TargetSeats.Count == 2 &&
                frame.Action?.DesignatedTargetSeats?.SequenceEqual([frame.TargetSeats[0]]) == true &&
                game.PendingDecision?.SkillPrompt?.SkillId != SkillId,
                "Borrowed Sword has one designated weapon owner; the nominated Slash victim must not offer Fenwei.");
            return;
        }
        throw new InvalidOperationException("No bounded borrowed-sword fixture equipped an opponent's weapon.");
    }

    public static void IndependentQixiUsesBlackHandAndEquipment()
    {
        var registry = Registry("standard:crossbow");
        for (var seed = 1; seed <= 64; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var black = hand.FirstOrDefault(card => card.Suit is Suit.Spade or Suit.Club);
            var red = hand.FirstOrDefault(card => card.Suit is Suit.Heart or Suit.Diamond);
            if (black is null || red is null) continue;
            var actions = game.GetHumanLegalActions();
            var converted = actions.FirstOrDefault(action => action.Kind == LegalActionKind.Dismantlement &&
                action.CardId == black.Id && action.PlayedCardKind == CardKind.Dismantlement);
            Require(converted is not null && !actions.Any(action =>
                    action.Kind == LegalActionKind.Dismantlement && action.CardId == red.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement),
                "Independent boundary Qixi must accept a black hand card and reject a red hand card.");
            var equip = actions.First(item => item.Kind == LegalActionKind.Equip && item.CardId == black.Id);
            Play(game, equip);
            ReachPlay(game);
            var fromEquipment = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Dismantlement && action.CardId == black.Id &&
                action.PlayedCardKind == CardKind.Dismantlement);
            Require(fromEquipment is not null &&
                game.CreateCardZoneDiagnostics().Single(item => item.CardId == black.Id).Location ==
                    CardLocation.Equipment(0),
                "Independent boundary Qixi must also convert its already equipped black card.");
            Play(game, fromEquipment!);
            Require(game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                .Any(item => item.CardId == black.Id && item.CardKind == CardKind.Dismantlement) &&
                game.CardMovements.Any(item => item.CardId == black.Id &&
                    item.From == CardLocation.Equipment(0) && item.To == CardLocation.Processing),
                "The black equipment must be paid through the real Qixi Dismantlement command.");
            return;
        }
        throw new InvalidOperationException("No deterministic black/red Qixi hand was found.");
    }

    public static void TwoObserversSeeOnlyRemainingEffects()
    {
        var registry = Registry("standard:barbarian_assault", secondFenweiOwner: true);
        for (var seed = 1; seed <= 64; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var secondOwner = game.CreateSnapshot(0, true).Players.Single(player =>
                player.GeneralId == "fixture:fenwei-target-1").Seat;
            Play(game, game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.BarbarianAssault));
            if (game.PendingDecision?.SkillPrompt?.SkillId != SkillId) continue;
            var before = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            Answer(game, "activate");
            var firstChoice = game.PendingDecision!.Choices.First(choice =>
                choice.Targets.Count == 1 && choice.Targets[0] != secondOwner);
            Answer(game, firstChoice);
            for (var step = 0; step < 30 && game.ResolutionStack.Count > 0; step++)
                Advance(game);
            var events = game.Events.Select(item => item.Payload)
                .OfType<ProgramSelectedCardEffectsNullifiedEvent>().ToArray();
            if (events.Length != 2) continue;
            Require(events[0].OwnerSeat == 0 && events[1].OwnerSeat == secondOwner &&
                !events[1].TargetSeats.Intersect(events[0].TargetSeats).Any() &&
                events[1].TargetSeats.All(seat => seat != events[0].TargetSeats[0]),
                "The later Fenwei owner may use only effects still valid after the first owner acts.");
            var replay = GameReplay.Restore(before, registry);
            Answer(replay, "activate");
            Answer(replay, replay.PendingDecision!.Choices.Single(choice =>
                choice.Targets.SequenceEqual(firstChoice.Targets)));
            for (var step = 0; step < 30 && replay.ResolutionStack.Count > 0; step++)
                Advance(replay);
            Require(State(replay) == State(game) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramSelectedCardEffectsNullifiedEvent>()
                    .Select(item => string.Join(',', item.TargetSeats))
                    .SequenceEqual(events.Select(item => string.Join(',', item.TargetSeats))),
                "Both observers and the remaining card effects must replay exactly.");
            return;
        }
        throw new InvalidOperationException("No bounded two-Fenwei fixture had both owners intervene.");
    }

    public static void PeachGardenAndNullificationKeepIndependentEffects()
    {
        var gardenRegistry = Registry("fixture:assault-peach-mix");
        var gardenVerified = false;
        for (var seed = 1; seed <= 128; seed++)
        {
            var garden = Start(gardenRegistry, seed);
            ReachPlay(garden);
            var assault = garden.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.BarbarianAssault);
            var peach = garden.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.PeachGarden);
            if (assault is null || peach is null) continue;
            Play(garden, assault);
            Answer(garden, "skip");
            AdvanceToResolved(garden);
            ReachPlay(garden);
            peach = garden.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.PeachGarden);
            if (peach is null) continue;
            var before = garden.CreateSnapshot(0, true).Players.Select(player => player.Hp).ToArray();
            Require(Enumerable.Range(1, 4).All(seat => before[seat] == 3),
                "The actual Assault must wound each Peach Garden target before its next card use.");
            Play(garden, peach);
            Require(garden.PendingDecision?.SkillPrompt?.SkillId == SkillId,
                "Peach Garden must qualify as a multi-target trick.");
            Answer(garden, "activate");
            Answer(garden, garden.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual([1])));
            var paused = GameReplay.Restore(GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(garden.CreateCheckpoint())), gardenRegistry);
            AdvanceToResolved(garden);
            AdvanceToResolved(paused);
            var after = garden.CreateSnapshot(0, true).Players;
            Require(after[1].Hp == before[1] &&
                Enumerable.Range(2, 3).All(seat => after[seat].Hp == before[seat] + 1) &&
                State(paused) == State(garden),
                "Fenwei must suppress only the selected Peach Garden recovery and replay exactly.");
            gardenVerified = true;
            break;
        }
        Require(gardenVerified, "No bounded fixture reached a wounded real Peach Garden after Assault.");

        var counterRegistry = Registry("fixture:assault-nullification-mix");
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = Start(counterRegistry, seed);
            ReachPlay(game);
            var assault = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.BarbarianAssault);
            if (assault is null || game.CreateSnapshot(0, true).Players[0].Hand.All(item =>
                    item.Kind != CardKind.Nullification)) continue;
            Play(game, assault);
            Answer(game, "activate");
            Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual([1])));
            for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.Nullification &&
                 game.ResolutionStack.Count > 0; step++) Advance(game);
            if (game.PendingDecision?.Kind != DecisionKind.Nullification) continue;
            Require(game.ResolutionStack.OfType<CardUseFrame>().Single().IneffectiveTargetSeats?.Contains(1) == true &&
                game.PendingDecision!.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "nullification"),
                "Fenwei's chosen effect must remain ineffective when another target opens Nullification.");
            var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            var replay = GameReplay.Restore(checkpoint, counterRegistry);
            var answer = game.PendingDecision.Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("response") == "nullification");
            Answer(game, answer);
            Answer(replay, replay.PendingDecision!.Choices.Single(choice => choice.Id == answer.Id));
            Require(State(replay) == State(game) &&
                game.Events.Select(item => item.Payload).OfType<NullificationRespondedEvent>()
                    .Any(item => item.ResponderSeat == 0),
                "The independent Nullification response must retain its own replayable response window.");
            return;
        }
        throw new InvalidOperationException("No bounded fixture reached a real Nullification response after Fenwei.");
    }

    private static ContentRegistry Registry(string cardId, bool preEffectDeath = false,
        bool secondFenweiOwner = false, bool delayedTrickObserver = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(cardId, preEffectDeath, secondFenweiOwner, delayedTrickObserver));

    private static GameEngine Start(ContentRegistry registry, int seed = 1)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = Scenario.ModeId, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fenwei fixture did not start.");
        var selection = game.PendingDecision!;
        var result = game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, selection.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei fixture general selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fenwei fixture did not reach play.");
    }

    private static void AdvanceToResolved(GameEngine game)
    {
        for (var step = 0; step < 60 && game.ResolutionStack.Count > 0; step++)
            Advance(game);
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei fixture card use failed.");
    }

    private static void Answer(GameEngine game, string action) => Answer(game,
        game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var result = game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fenwei answer failed.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(
            "CardGame.Content.Standard.SkillPrograms." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void Reject(string rules, string presentation)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected the invalid reusable card-effect graph to be rejected.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(string cardId, bool preEffectDeath, bool secondFenweiOwner,
        bool delayedTrickObserver) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-boundary-gan-ning-check-5";
        public PackageManifest Manifest { get; } = new("boundary-gan-ning-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            if (preEffectDeath)
            {
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":55,"skills":[{"id":"fixture:before-effect-death","revision":1,
                    "minimumRulesVersion":165,"triggers":[{"id":"die-first",
                    "window":"cardUseBeforeTargetEffects","ownerRelation":"target",
                    "cardKinds":["barbarianAssault"],"optional":false,"priority":100,
                    "effects":[{"op":"loseHp","target":"owner","amount":4}]}]}]}
                    """, """
                    {"schemaVersion":1,"skills":{"fixture:before-effect-death":{"name":"先行死亡","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition("fixture:before-effect-death", "先行死亡", "测试")
                { Program = catalog.Programs["fixture:before-effect-death"] });
            }
            if (delayedTrickObserver)
            {
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":55,"skills":[{"id":"fixture:delayed-trick-observer","revision":1,
                    "minimumRulesVersion":165,"triggers":[{"id":"observe-delayed",
                    "window":"cardUseBeforeTargetEffects","ownerRelation":"observer",
                    "cardKinds":["indulgence"],"cardCategories":["trick"],"optional":false,
                    "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                    """, """
                    {"schemaVersion":1,"skills":{"fixture:delayed-trick-observer":{"name":"延时观察","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition("fixture:delayed-trick-observer", "延时观察", "测试")
                { Program = catalog.Programs["fixture:delayed-trick-observer"] });
            }
            builder.AddDeck(new ContentDeckRecipe("fixture:fenwei-deck", "奋威牌堆", 4, 1,
                cardId == "fixture:borrowed-mix"
                    ? [new ContentDeckCardCount("classic:borrowed-sword", 50),
                       new ContentDeckCardCount("standard:crossbow", 50),
                       new ContentDeckCardCount("standard:nullification", 20)]
                    : cardId == "fixture:assault-peach-mix"
                        ? [new ContentDeckCardCount("standard:barbarian_assault", 50),
                           new ContentDeckCardCount("standard:peach_garden", 50)]
                    : cardId == "fixture:assault-nullification-mix"
                        ? [new ContentDeckCardCount("standard:barbarian_assault", 50),
                           new ContentDeckCardCount("standard:nullification", 50)]
                    : [new ContentDeckCardCount(cardId, 100)]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:fenwei-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "奋威目标", "supporter",
                    preEffectDeath && target == targets[0] ? "fixture:before-effect-death" :
                    delayedTrickObserver && target == targets[0] ? "fixture:delayed-trick-observer" :
                    secondFenweiOwner && target == targets[0] ? SkillId : "standard:none",
                    "wei", BaseHp: 4));
            builder.AddMode(new ContentModeDefinition(ModeId, "界甘宁测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:fenwei-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
