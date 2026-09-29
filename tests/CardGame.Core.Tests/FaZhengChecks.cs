using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FaZhengChecks
{
    private const string General = "classic:fa-zheng";
    private const string Enyuan = "classic:enyuan";
    private const string Xuanhuo = "classic:xuanhuo";
    private const string GrudgeMode = "identity:classic-fa-zheng-grudge-check-5";
    private const string XuanhuoMode = "identity:classic-fa-zheng-xuanhuo-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "shu" } general &&
                general.SkillIds.SequenceEqual([Enyuan, Xuanhuo]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Fa Zheng must be in the current Shu roster with three HP.");

        var enyuan = current.Skills[Enyuan].Program!;
        var favor = enyuan.Triggers.Single(item => item.Id == "favor-for-gained-pair");
        Require(favor.Window == SkillProgramTriggerWindow.CardsGained &&
                favor.Subject == SkillProgramTriggerSubject.Owner &&
                favor.Optional &&
                favor.DestinationZones.SequenceEqual([CardZoneKind.Hand]) &&
                favor.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                favor.Condition.Kind == SkillProgramTriggerConditionKind.GainedTwoPlusFromSingleOther,
            "Enyuan's favor must fire once per gain batch of two or more cards from one other character.");
        var favorDraw = favor.Effects.Single();
        Require(favorDraw.Op == SkillProgramEffectOp.Draw &&
                favorDraw.Target == SkillProgramEffectTarget.Owner &&
                favorDraw.Amount == 1 &&
                favorDraw.TargetReference is { Kind: ProgramParticipantRef.EventSource },
            "Enyuan's favor must make the gain source draw exactly one card.");

        var grudge = enyuan.Triggers.Single(item => item.Id == "grudge-choice-after-damage");
        Require(grudge.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                grudge.Subject == SkillProgramTriggerSubject.Owner &&
                grudge.Optional &&
                grudge.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint &&
                grudge.Condition.Kind == SkillProgramTriggerConditionKind.OtherDamageParticipantAlive,
            "Enyuan's grudge must offer the living damage source a choice per damage point.");
        Require(grudge.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChooseOption,
                SkillProgramEffectOp.SelectOwnedCards,
                SkillProgramEffectOp.MoveBoundCards,
                SkillProgramEffectOp.LoseHp]),
            "The grudge must ask, then either receive a hand card or make the source lose HP.");
        var ask = grudge.Effects[0];
        Require(ask.ChooserRef is { Kind: ProgramParticipantRef.EventSource } &&
                ask.ResultBind == "enyuan-grudge-choice" &&
                ask.Options.Select(item => item.Id).SequenceEqual(["give", "loseHp"]) &&
                ask.Options[0].Condition is { Kind: SkillProgramConditionKind.HandCountAtLeast, Value: 1 } &&
                ask.Options[1].Condition.Kind == SkillProgramConditionKind.Always,
            "The grudge choice must offer a hand-card gift only to a source holding cards.");
        Require(grudge.Effects[1] is { Op: SkillProgramEffectOp.SelectOwnedCards } gift &&
                gift.TargetReference is { Kind: ProgramParticipantRef.EventSource } &&
                gift.Zones.SequenceEqual([CardZoneKind.Hand]) &&
                gift.Amount == 1 &&
                gift.ResultBind == "enyuan-given" &&
                gift.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "give" } &&
                grudge.Effects[2] is { Op: SkillProgramEffectOp.MoveBoundCards } move &&
                move.Destination == SkillProgramCardDestination.OwnerHand &&
                move.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "give" } &&
                grudge.Effects[3] is { Op: SkillProgramEffectOp.LoseHp } loss &&
                loss.Amount == 1 &&
                loss.TargetReference is { Kind: ProgramParticipantRef.EventSource } &&
                loss.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "loseHp" },
            "Both grudge branches must stay bound to the recorded source choice.");

        var dupe = current.Skills[Xuanhuo].Program!.Triggers.Single();
        Require(dupe.Window == SkillProgramTriggerWindow.DrawPhaseStarting &&
                dupe.Subject == SkillProgramTriggerSubject.Owner &&
                dupe.Optional &&
                dupe.Priority == 100 &&
                dupe.DrawPhaseMode == SkillProgramDrawPhaseMode.Replacement,
            "Xuanhuo must replace its owner's normal draw.");
        Require(dupe.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.SelectTargets,
                SkillProgramEffectOp.Draw,
                SkillProgramEffectOp.RequestSlashByTarget,
                SkillProgramEffectOp.TakeRandomCardsFromParticipant]),
            "Xuanhuo must pick an ordered pair, draw for the first, ask the slash, then maybe take.");
        var pair = dupe.Effects[0];
        Require(pair.TargetKind == SkillProgramTargetKind.OtherLivingRangeOrderedPair &&
                pair.MinimumTargets == 2 && pair.MaximumTargets == 2,
            "Xuanhuo must select one other character plus one target inside their attack range.");
        Require(dupe.Effects[1] is { Op: SkillProgramEffectOp.Draw } feed &&
                feed.Amount == 2 &&
                feed.TargetReference is { Kind: ProgramParticipantRef.SelectedFirst },
            "Xuanhuo must make the first selected character draw two cards.");
        Require(dupe.Effects[2] is { Op: SkillProgramEffectOp.RequestSlashByTarget } forced &&
                forced.TargetReference is { Kind: ProgramParticipantRef.SelectedFirst } &&
                forced.ActorReference is { Kind: ProgramParticipantRef.SelectedSecond } &&
                forced.ResultBind == "xuanhuo-answer",
            "The second selected character must answer for the first with a slash.");
        Require(dupe.Effects[3] is { Op: SkillProgramEffectOp.TakeRandomCardsFromParticipant } take &&
                take.TargetReference is { Kind: ProgramParticipantRef.SelectedSecond } &&
                take.Amount == 2 &&
                take.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                take.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "declined" },
            "A declined answer costs the second character two random cards.");

        const string favorTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:favor","revision":1,
            "minimumRulesVersion": 190,
            "triggers":[{"id":"favor","window":"cardsGained","subject":"owner",
            "destinationZones":["hand"],"movementOccurrence":"perBatch","optional":true,"priority":0,
            "condition":{"kind":"gainedTwoPlusFromSingleOther"},
            "effects":[{"op":"draw","target":"owner","amount":1,"targetRef":{"kind":"eventSource"}}]}]}]}
            """;
        const string favorPresentation = """
            {"schemaVersion":3,"skills":{"fixture:favor":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(favorTemplate, favorPresentation)
                .Programs["fixture:favor"].Triggers.Single().Effects.Count == 1,
            "The gain-batch favor trigger must load with its frozen source condition.");
        Reject(favorTemplate.Replace("\"window\":\"cardsGained\"", "\"window\":\"turnEnding\""),
            favorPresentation, "the gain-batch condition requires the cardsGained window");
        Reject(favorTemplate.Replace("\"destinationZones\":[\"hand\"]", "\"destinationZones\":[\"equipment\"]"),
            favorPresentation, "the cardsGained window must observe exactly the hand zone");

        const string grudgeTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:grudge","revision":1,
            "minimumRulesVersion": 190,
            "triggers":[{"id":"grudge","window":"afterDamageApplied","subject":"owner",
            "damageOccurrence":"perDamagePoint","optional":true,"priority":0,
            "condition":{"kind":"otherDamageParticipantAlive"},
            "effects":[
            {"op":"chooseOption","target":"owner","chooserRef":{"kind":"eventSource"},
            "resultBind":"choice","options":[
            {"id":"give","condition":{"kind":"handCountAtLeast","value":1}},
            {"id":"loseHp","condition":{"kind":"always"}}]},
            {"op":"selectOwnedCards","target":"owner","targetRef":{"kind":"eventSource"},
            "zones":["hand"],"amount":1,"resultBind":"given",
            "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"give"}},
            {"op":"moveBoundCards","target":"owner","sourceBind":"given","destination":"ownerHand",
            "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"give"}},
            {"op":"loseHp","target":"owner","amount":1,"targetRef":{"kind":"eventSource"},
            "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"loseHp"}}]}]}]}
            """;
        const string grudgePresentation = """
            {"schemaVersion":3,"skills":{"fixture:grudge":{"name":"测试","description":"测试",
            "optionLabels":{"give":"交牌","loseHp":"失去体力"}}}}
            """;
        Require(SkillProgramCatalog.Load(grudgeTemplate, grudgePresentation)
                .Programs["fixture:grudge"].Triggers.Count == 1,
            "The grudge chain must load with its choice-gated branches.");
        const string grudgeMissingLabelPresentation = """
            {"schemaVersion":3,"skills":{"fixture:grudge":{"name":"测试","description":"测试"}}}
            """;
        Reject(grudgeTemplate, grudgeMissingLabelPresentation,
            "every chooseOption option needs a presentation label");
        Reject(grudgeTemplate.Replace("\"damageOccurrence\":\"perDamagePoint\",", ""),
            grudgePresentation, "an after-damage trigger must declare its damage occurrence");
        Reject(grudgeTemplate.Replace(
                "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1,\"targetRef\":{\"kind\":\"eventSource\"}",
                "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1,\"targetRef\":{\"kind\":\"selectedFirst\"}"),
            grudgePresentation, "an HP loss participant must be a damage event participant");

        const string dupeTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:dupe","revision":1,
            "minimumRulesVersion": 190,
            "triggers":[{"id":"dupe","window":"drawPhaseStarting","subject":"owner",
            "optional":true,"priority":100,"drawPhaseMode":"replacement",
            "effects":[
            {"op":"selectTargets","target":"owner","targetKind":"otherLivingRangeOrderedPair",
            "minimumTargets":2,"maximumTargets":2,"targetAiOrder":"stable"},
            {"op":"draw","target":"owner","amount":2,"targetRef":{"kind":"selectedFirst"}},
            {"op":"requestSlashByTarget","target":"owner",
            "responderRef":{"kind":"selectedFirst"},"victimRef":{"kind":"selectedSecond"},
            "resultBind":"answer"},
            {"op":"takeRandomCardsFromParticipant","target":"owner",
            "participantRef":{"kind":"selectedSecond"},"amount":2,"zones":["hand","equipment"],
            "condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"declined"}}]}]}]}
            """;
        const string dupePresentation = """
            {"schemaVersion":3,"skills":{"fixture:dupe":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(dupeTemplate, dupePresentation)
                .Programs["fixture:dupe"].Triggers.Single().Effects.Count == 4,
            "The full replacement chain with every new operation must load.");
        Reject(dupeTemplate.Replace("\"minimumTargets\":2,\"maximumTargets\":2",
                "\"minimumTargets\":1,\"maximumTargets\":2"),
            dupePresentation, "ordered range pairs require exactly two targets");
        Reject(dupeTemplate.Replace("\"responderRef\":{\"kind\":\"selectedFirst\"}",
                "\"target\":\"selectedTarget\",\"responderRef\":{\"kind\":\"selectedFirst\"}"),
            dupePresentation, "a bound responder requires the owner placeholder");
        Reject(dupeTemplate.Replace("\"victimRef\":{\"kind\":\"selectedSecond\"}",
                "\"victimRef\":{\"kind\":\"eventSource\"}"),
            dupePresentation, "a bound victim must be a selected participant");
        Reject(dupeTemplate.Replace("\"participantRef\":{\"kind\":\"selectedSecond\"}",
                "\"participantRef\":{\"kind\":\"eventSource\"}"),
            dupePresentation, "a participant take must read a selected slot");
        Reject(dupeTemplate.Replace("\"zones\":[\"hand\",\"equipment\"]", "\"zones\":[\"judgment\"]"),
            dupePresentation, "random participant takes support hand and equipment only");
    }

    public static void GrudgeOffersDamageSourceChoiceAndReplays()
    {
        var registry = Registry(GrudgeMode, SlashDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 200 && completed < 1; seed++)
        {
            var game = Start(registry, seed, GrudgeMode);
            DriveUntil(game, () => false, stopAtSkills: [Enyuan], budget: 2600);
            if (!IsProgramPrompt(game, Enyuan)) continue;
            var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Last(item => item.TargetSeat == 0 && item.SourceSeat != 0);
            Require(damage.Amount == 1 && game.CreateSnapshot(0, true).Players[damage.SourceSeat].IsAlive,
                "The grudge prompt must follow one damage point from a living source.");

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The grudge resolution must replay identically from the paused prompt.");
            var chosen = game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                .Single(item => item.SkillId == Enyuan && item.ResultBind == "enyuan-grudge-choice");
            Require(chosen.OwnerSeat == 0 && chosen.ChooserSeat == damage.SourceSeat,
                "The damage source must make the grudge choice.");
            var started = game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .Single(item => item.SkillId == Enyuan);
            Require(started.BindingId == "grudge-choice-after-damage" &&
                    started.Window == SkillProgramTriggerWindow.AfterDamageApplied,
                "The grudge must resolve through the after-damage program window.");
            if (chosen.OptionId == "give")
            {
                var movement = game.CardMovements.Last(item =>
                    item.To == CardLocation.Hand(0) &&
                    item.From == CardLocation.Hand(damage.SourceSeat) &&
                    item.Reason.Value.Contains(Enyuan, StringComparison.Ordinal));
                Require(game.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Id == movement.CardId),
                    "The gifted hand card must sit in Fa Zheng's hand.");
            }
            else
            {
                var loss = game.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>()
                    .Single(item => item.SkillId == Enyuan && item.TargetSeat == damage.SourceSeat);
                Require(loss.Amount == 1,
                    "A declined gift must cost the source exactly one HP.");
            }
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Enyuan && item.Activated),
                "The grudge must resolve as an activated program binding.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a grudge choice.");
    }

    public static void DeclinedXuanhuoTakesCardsAndFeedsFavor()
    {
        var registry = Registry(XuanhuoMode, DodgeDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 240 && completed < 1; seed++)
        {
            var game = Start(registry, seed, XuanhuoMode);
            DriveUntil(game, () => false, stopAtSkills: [Xuanhuo], budget: 2600);
            if (!IsProgramPrompt(game, Xuanhuo)) continue;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            if (!AnswerOrderedPair(game, 1, 2) || !AnswerOrderedPair(replay, 1, 2))
            {
                continue;
            }
            DriveUntil(game, () => false, stopAtSkills: [Enyuan], budget: 2600);
            if (!IsProgramPrompt(game, Enyuan)) continue;

            var favorPaused = RoundTrip(game.CreateCheckpoint());
            var favorReplay = GameReplay.Restore(favorPaused, registry);
            AcceptTrigger(game);
            AcceptTrigger(favorReplay);
            DriveUntilSettled(game);
            DriveUntilSettled(favorReplay);

            Require(Events(game).SequenceEqual(Events(favorReplay)) &&
                    State(game) == State(favorReplay),
                "The declined xuanhuo chain must replay identically from the paused favor prompt.");
            var taken = game.Events.Select(item => item.Payload)
                .OfType<ProgramRandomCardsTakenFromParticipantEvent>()
                .SingleOrDefault(item => item.SkillId == Xuanhuo);
            if (taken is null) continue;
            Require(taken.OwnerSeat == 0 && taken.SourceSeat == 2 && taken.CardCount == 2,
                "A declined answer must cost the second character two random cards.");
            Require(!game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                    .Any(item => item.SourceSeat == 1 && item.CardKind == CardKind.Slash),
                "The slashless responder must decline instead of using a slash.");
            var feeds = game.CardMovements.Where(item =>
                item.Reason.Value.Contains($"{Xuanhuo}.Draw", StringComparison.Ordinal) &&
                item.To == CardLocation.Hand(1)).ToArray();
            Require(feeds.Length == 2,
                "The first selected character must draw exactly two replacement cards.");
            var takenCards = game.CardMovements.Where(item =>
                item.To == CardLocation.Hand(0) &&
                item.From.OwnerSeat == 2 &&
                item.Reason.Value.Contains(Xuanhuo, StringComparison.Ordinal)).ToArray();
            Require(takenCards.Length == 2 &&
                    takenCards.All(item => game.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Id == item.CardId)),
                "Both taken cards must move from the second character into Fa Zheng's hand.");
            var favor = game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .Single(item => item.SkillId == Enyuan &&
                    item.BindingId == "favor-for-gained-pair");
            Require(favor.OwnerSeat == 0 && favor.Window == SkillProgramTriggerWindow.CardsGained,
                "The take must open the gain-batch favor window for its owner.");
            var favorDraws = game.CardMovements.Where(item =>
                item.Reason.Value.Contains($"{Enyuan}.Draw", StringComparison.Ordinal) &&
                item.To == CardLocation.Hand(2)).ToArray();
            Require(favorDraws.Length == 1,
                "The gain-batch favor must make the source draw exactly one card.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the declined xuanhuo chain.");
    }

    private static void AcceptTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The program trigger prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") != "skip") ??
            throw new InvalidOperationException("The program trigger lost its accept option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static bool AnswerOrderedPair(GameEngine game, int first, int second)
    {
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 }) return false;
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "select-targets" &&
            item.Targets.SequenceEqual([first, second]));
        if (choice is null) return false;
        Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)));
        return true;
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 900)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return;
            }
            if (stopAtDecisions is not null && stopAtDecisions.Contains(prompt.Kind) &&
                prompt.PlayerSeat == 0)
            {
                return;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                        choice.Id, game.Revision)));
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck, int bankHp) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new FaZhengScenario(mode, deck, bankHp));

    private static ContentDeckRecipe SlashDeck() =>
        DeckCore("standard:slash", Suit.Spade);

    private static ContentDeckRecipe DodgeDeck() =>
        DeckCore("standard:dodge", Suit.Heart);

    private static ContentDeckRecipe DeckCore(string kind, Suit suit) =>
        new("fixture:fa-zheng-deck", "法正测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kind, suit, index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fa Zheng fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Fa Zheng selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fa Zheng fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Fa Zheng command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Fa Zheng composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class FaZhengScenario(string modeId, ContentDeckRecipe deck, int bankHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fa-zheng-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 161, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Enyuan and Xuanhuo on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:fa-zheng-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:fa-zheng-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:fa-zheng-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:fa-zheng-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "法正测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:fa-zheng-bank-a",
                    "fixture:fa-zheng-bank-b",
                    "fixture:fa-zheng-bank-c",
                    "fixture:fa-zheng-bank-d"]));
        }
    }
}
