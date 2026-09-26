using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramJudgmentReplacementChecks
{
    internal static void Definitions()
    {
        var catalog = SkillProgramCatalog.Load(ValidV4, Presentation);
        var program = catalog.Programs[ProgramId];
        var exchange = program.Triggers.Single(trigger => trigger.Id == ExchangeTriggerId);
        var discard = program.Triggers.Single(trigger => trigger.Id == DiscardTriggerId);
        var replacement = exchange.Effects[0];
        var followUp = exchange.Effects[1];
        Require(program.RuntimeVersion == "skill-program-v59" && program.MinimumRulesVersion == 169 &&
                exchange.Window == SkillProgramTriggerWindow.JudgmentReplacing &&
                exchange.Subject == SkillProgramTriggerSubject.Any && exchange.Optional &&
                exchange.ExcludedReasons.SequenceEqual([JudgmentReasons.Leiji]) &&
                replacement is
                {
                    Op: SkillProgramEffectOp.ReplaceJudgment,
                    Target: SkillProgramEffectTarget.Owner,
                    Amount: 0,
                    OldCardDestination: SkillProgramOldJudgmentCardDestination.OwnerHand
                } &&
                replacement.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                replacement.Suits.SequenceEqual([Suit.Spade, Suit.Club]) &&
                followUp is
                {
                    Op: SkillProgramEffectOp.Draw,
                    Target: SkillProgramEffectTarget.Owner,
                    Amount: 1,
                    MinimumReplacementRank: 2,
                    MaximumReplacementRank: 9
                } &&
                followUp.ReplacementSuits.SequenceEqual([Suit.Spade]) &&
                discard.Effects.Single().OldCardDestination ==
                    SkillProgramOldJudgmentCardDestination.DiscardPile,
            "The shared executor must retain typed judgment replacement and committed-card filters.");
        RequireThrows<NotSupportedException>(() =>
            ((ICollection<CardZoneKind>)replacement.Zones).Clear());
        RequireThrows<NotSupportedException>(() =>
            ((ICollection<Suit>)followUp.ReplacementSuits).Add(Suit.Heart));
        RequireThrows<ArgumentException>(() => JudgmentTriggerOrdering.Order(
            [new JudgmentTriggerCandidate(
                0, SkillKind.None, "partial-program-identity", ProgramTriggerId: "replace")],
            currentActorSeat: 0,
            playerCount: 1));

        AssertReject(ValidV4.Replace("\"schemaVersion\":59", "\"schemaVersion\":57", StringComparison.Ordinal),
            "schema version");
        AssertReject(ValidV4.Replace("\"zones\":[\"hand\",\"equipment\"]", "\"zones\":[]", StringComparison.Ordinal),
            "replacement requires hand or equipment");
        AssertReject(ValidV4.Replace("\"zones\":[\"hand\",\"equipment\"]", "\"zones\":[\"hand\",\"judgment\"]", StringComparison.Ordinal),
            "replacement requires hand or equipment");
        AssertReject(ValidV4.Replace("\"suits\":[\"spade\",\"club\"]", "\"suits\":[]", StringComparison.Ordinal),
            "at least one suit");
        AssertReject(ValidV4.Replace(",\"oldCardDestination\":\"ownerHand\"", string.Empty, StringComparison.Ordinal),
            "oldCardDestination");
        AssertReject(ValidV4.Replace("\"op\":\"replaceJudgment\",\"target\":\"owner\"",
                "\"op\":\"replaceJudgment\",\"target\":\"opponent\"", StringComparison.Ordinal),
            "unsupported SkillProgramEffectTarget");
        AssertReject(ValidV4.Replace("\"op\":\"draw\",\"target\":\"owner\"",
                "\"op\":\"draw\",\"target\":\"opponent\"", StringComparison.Ordinal),
            "opponent");
        AssertReject(ValidV4.Replace("\"replacementSuits\":[\"spade\"]",
                "\"replacementSuits\":[\"spade\",\"spade\"]", StringComparison.Ordinal),
            "duplicate");
        AssertReject(ValidV4.Replace("\"minimumReplacementRank\":2", "\"minimumReplacementRank\":14", StringComparison.Ordinal),
            "valid rank bounds");
        AssertReject(InvalidOrderV4, "requires replaceJudgment first");
        AssertReject(InvalidFollowUpV4, "obtainOpponentHandCard");
        foreach (var (effect, expectedError) in new[]
        {
            ("""{"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade"],"oldCardDestination":"discardPile"}""",
                "requires context JudgmentReplacement"),
            ("""{"op":"draw","target":"owner","amount":1,"replacementSuits":["spade"],"minimumReplacementRank":1,"maximumReplacementRank":13}""",
                "replacement filters require judgmentReplacing"),
            ("""{"op":"recover","target":"owner","amount":1,"replacementSuits":["spade"],"minimumReplacementRank":1,"maximumReplacementRank":13}""",
                "replacement filters require judgmentReplacing")
        })
        {
            var activeRules = $$"""
                {"schemaVersion":59,"skills":[{"id":"judgment-replace-test:both","revision":1,
                  "activations":[{"id":"activate","minCards":0,"maxCards":0,"minTargets":0,
                  "maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{{effect}}]}]}]}
                """;
            AssertReject(activeRules, expectedError);
        }
    }

    internal static void WindowDestinationsAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var (boundary, equipmentId) = FindBoundary(registry);
        var boundaryPrompt = boundary.PendingDecision ??
            throw new InvalidOperationException("Judgment replacement prompt was lost.");
        var boundaryFrame = boundary.ResolutionStack.OfType<JudgmentFrame>().Single();
        var oldCardId = boundaryFrame.CardId ??
            throw new InvalidOperationException("Judgment replacement frame has no current card.");
        var before = boundary.CreateSnapshot(0, revealAll: true).Players[0];
        Require(boundaryPrompt is
        {
            Kind: DecisionKind.ProgramJudgmentReplacement,
            PlayerSeat: 0,
            IsPrivate: true
        } &&
                boundaryPrompt.ValidCardIds.Contains(equipmentId) &&
                boundaryPrompt.Choices.Any(choice => choice.Cards.SequenceEqual([equipmentId])) &&
                boundary.CreateCardZoneDiagnostics().Single(card => card.CardId == equipmentId).Location ==
                    CardLocation.Equipment(0) &&
                boundary.CreateSnapshot(1, revealAll: true).PendingDecision is null,
            "The first configured replacement must privately publish the exact owned equipment card.");
        var checkpoint = boundary.CreateCheckpoint();
        var restoredBoundary = GameReplay.Restore(checkpoint, registry);
        Require(restoredBoundary.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement } restored &&
                restored.PromptId == boundaryPrompt.PromptId &&
                restored.ValidCardIds.SequenceEqual(boundaryPrompt.ValidCardIds),
            "A paused configured replacement must restore its exact prompt and candidate identities.");
        var invalidBefore = SnapshotJson.Serialize(boundary.CreateSnapshot(0, revealAll: true));
        var invalid = boundary.Submit(new AnswerPromptCommand(
            0, boundaryPrompt.PromptId, new ChoiceId("program-judgment-replace.invalid"), boundary.Revision));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(boundary.CreateSnapshot(0, revealAll: true)) == invalidBefore,
            "A forged configured replacement choice must not mutate the judgment or card zones.");

        var exchange = GameReplay.Restore(checkpoint, registry);
        AnswerWithCard(exchange, equipmentId);
        var secondExchangePrompt = exchange.PendingDecision ??
            throw new InvalidOperationException("The second replacement candidate was not resumed.");
        var exchangeEvent = exchange.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentReplacementResolvedEvent>()
            .Single(item => item.TriggerId == ExchangeTriggerId);
        Require(exchangeEvent is
        {
            SkillId: ProgramId,
            OwnerSeat: 0,
            SubjectSeat: 0,
            Activated: true,
            DrawnCards: 1,
            RecoveredHp: 0,
            OldCardDestination: SkillProgramOldJudgmentCardDestination.OwnerHand
        } &&
                exchange.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .Count(item => item.BindingId == ExchangeTriggerId &&
                        item.Window == SkillProgramTriggerWindow.JudgmentReplacing) == 1 &&
                exchange.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.BindingId == ExchangeTriggerId && item.Completed) == 1 &&
                exchangeEvent.OldCardId == oldCardId && exchangeEvent.ReplacementCardId == equipmentId &&
                secondExchangePrompt.Kind == DecisionKind.ProgramJudgmentReplacement &&
                secondExchangePrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip") &&
                exchange.ResolutionStack.OfType<JudgmentFrame>().Single() is
                { CardId: var exchangedCardId, Suit: Suit.Spade } && exchangedCardId == equipmentId &&
                exchange.CreateCardZoneDiagnostics().Single(card => card.CardId == oldCardId).Location ==
                    CardLocation.Hand(0) &&
                exchange.CardMovements.Any(move =>
                    move.CardId == equipmentId && move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.ProgramJudgmentReplace),
            "OwnerHand replacement must atomically exchange the cards, freeze Spade 5 and draw once.");
        AnswerSkip(exchange);
        var exchangeAfter = exchange.CreateSnapshot(0, revealAll: true).Players[0];
        Require(exchangeAfter.HandCount == before.HandCount + 2 &&
                exchangeAfter.Hand.Any(card => card.Id == oldCardId) &&
                exchange.CreateCardZoneDiagnostics().Single(card => card.CardId == equipmentId).Location ==
                    CardLocation.DiscardPile &&
                exchange.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
                    .Any(item => item.ResolutionId == boundaryFrame.Id && item.CardId == equipmentId),
            "After later candidates skip, the exchanged old card and extra draw must remain while judgment cleans up.");
        AssertCompletedReplay(exchange, registry, expectedActivatedTrigger: ExchangeTriggerId);

        var discard = GameReplay.Restore(checkpoint, registry);
        AnswerSkip(discard);
        Require(discard.PendingDecision is { Kind: DecisionKind.ProgramJudgmentReplacement } discardPrompt &&
                discardPrompt.ValidCardIds.Contains(equipmentId),
            "Skipping the first binding must resume the second configured replacement candidate.");
        AnswerWithCard(discard, equipmentId);
        var discardAfter = discard.CreateSnapshot(0, revealAll: true).Players[0];
        var discardEvent = discard.Events.Select(item => item.Payload)
            .OfType<ProgramJudgmentReplacementResolvedEvent>()
            .Single(item => item.TriggerId == DiscardTriggerId);
        Require(discardEvent is
        {
            Activated: true,
            DrawnCards: 0,
            RecoveredHp: 0,
            OldCardDestination: SkillProgramOldJudgmentCardDestination.DiscardPile
        } &&
                discardEvent.OldCardId == oldCardId && discardEvent.ReplacementCardId == equipmentId &&
                discardAfter.HandCount == before.HandCount &&
                discardAfter.Hand.All(card => card.Id != oldCardId) &&
                discard.CreateCardZoneDiagnostics().Single(card => card.CardId == oldCardId).Location ==
                    CardLocation.DiscardPile,
            "DiscardPile replacement must discard the old judgment card and omit unmatched follow-up effects.");
        AssertCompletedReplay(discard, registry, expectedActivatedTrigger: DiscardTriggerId);
        AssertAiReplacementAndReplay(registry);
    }

    internal static void BaguaSourceEquipmentIsExcluded()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new BaguaFixturePackage());
        var (beforeBagua, baguaId) = FindBaguaBoundary(registry);
        var current = GameReplay.Restore(RoundTrip(beforeBagua), registry);
        SubmitBagua(current);

        var currentPrompt = current.PendingDecision ??
            throw new InvalidOperationException("The current Bagua replacement prompt was lost.");
        var currentOwner = current.CreateSnapshot(0, revealAll: true).Players[0];
        var legalBlackHand = currentOwner.Hand
            .Where(card => card.Suit is Suit.Spade or Suit.Club)
            .Select(card => card.Id)
            .Order()
            .ToArray();
        Require(currentPrompt is
                {
                    Kind: DecisionKind.ProgramJudgmentReplacement,
                    PlayerSeat: 0,
                    IsPrivate: true
                } &&
                legalBlackHand.Length > 0 &&
                currentPrompt.ValidCardIds.SequenceEqual(legalBlackHand) &&
                !currentPrompt.ValidCardIds.Contains(baguaId) &&
                currentOwner.Equipment.Any(card => card.Id == baguaId),
            "The current replacement prompt must exclude the equipped Bagua that started this judgment.");

        var replay = GameReplay.Restore(RoundTrip(current.CreateCheckpoint()), registry);
        Require(replay.PendingDecision?.PromptId == currentPrompt.PromptId &&
                replay.PendingDecision.ValidCardIds.SequenceEqual(currentPrompt.ValidCardIds) &&
                SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)),
            "A paused Bagua-source exclusion prompt must replay exactly.");
    }

    private static (GameCheckpoint Checkpoint, int BaguaId) FindBaguaBoundary(ContentRegistry registry)
    {
        var baguaHands = 0;
        var equippedGames = 0;
        var baguaResponses = 0;
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = BaguaFixturePackage.ModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !game.Submit(new SelectGeneralCommand(
                    0, BaguaFixturePackage.OwnerGeneralId, game.Revision, setup.PromptId)).Accepted ||
                !game.Submit(new AdvanceCommand(game.Revision)).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.PlayCard })
                continue;

            var hand = game.CreateSnapshot(0, revealAll: true).Players[0].Hand;
            var bagua = hand.FirstOrDefault(card => card.Kind == CardKind.BaguaFormation);
            if (bagua is not null) baguaHands++;
            if (bagua is null ||
                !hand.Any(card => card.Id != bagua.Id &&
                    (card.Suit is Suit.Spade or Suit.Club)))
                continue;
            var equipped = game.Submit(new PlayCardCommand(
                0, bagua.Id, [], game.Revision, game.PendingDecision!.PromptId));
            if (!equipped.Accepted)
                continue;
            for (var resume = 0; resume < 8 &&
                 game.PendingDecision is not { Kind: DecisionKind.PlayCard }; resume++)
            {
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } resumedPlay ||
                !game.Submit(new EndPlayPhaseCommand(
                    0, game.Revision, resumedPlay.PromptId)).Accepted)
                continue;
            equippedGames++;

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } response &&
                    response.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "bagua") &&
                    game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card =>
                        card.Suit is Suit.Spade or Suit.Club))
                {
                    baguaResponses++;
                    return (game.CreateCheckpoint(), bagua.Id);
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RescueDying => new AnswerPromptCommand(
                            0, pending.PromptId,
                            pending.Choices.First(choice =>
                                choice.Parameters.GetValueOrDefault("response") == "let-die").Id,
                            game.Revision),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException(
            $"No bounded equipped Bagua response retained another black hand card " +
            $"(baguaHands={baguaHands}, equippedGames={equippedGames}, baguaResponses={baguaResponses}).");
    }

    private static void SubmitBagua(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Bagua response prompt was lost.");
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("response") == "bagua");
        var result = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Bagua response was rejected.");
    }

    private static (GameEngine Game, int EquipmentId) FindBoundary(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = FixturePackage.ModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !game.Submit(new SelectGeneralCommand(
                    0, FixturePackage.OwnerGeneralId, game.Revision, setup.PromptId)).Accepted ||
                !game.Submit(new AdvanceCommand(game.Revision)).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.PlayCard })
                continue;

            var hand = game.CreateSnapshot(0, revealAll: true).Players[0].Hand.ToDictionary(card => card.Id);
            var equipment = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Equip && action.CardId is { } id &&
                hand[id] is { Kind: CardKind.OffensiveHorse, Suit: Suit.Spade, Rank: 5 });
            if (equipment?.CardId is not { } equipmentId ||
                !game.Submit(new PlayCardCommand(
                    0, equipmentId, [], game.Revision, game.PendingDecision.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 1_200 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.ProgramJudgmentReplacement,
                        PlayerSeat: 0
                    } programPrompt &&
                    programPrompt.Choices.Any(choice => choice.Cards.SequenceEqual([equipmentId])) &&
                    programPrompt.Choices.Any(choice => choice.Id.Value.Contains(ExchangeTriggerId,
                        StringComparison.Ordinal)))
                    return (game, equipmentId);

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard =>
                            new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.Ganglie =>
                            Answer(pending, game, "response", "ganglie"),
                        DecisionKind.RespondDodge =>
                            Answer(pending, game, "response", "take-damage"),
                        DecisionKind.RescueDying =>
                            Answer(pending, game, "response", "let-die"),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException(
            "No bounded configured judgment replacement reached its equipped-card boundary.");
    }

    private static AnswerPromptCommand Answer(
        PendingDecision pending,
        GameEngine game,
        string key,
        string value) =>
        new(0, pending.PromptId,
            pending.Choices.Single(choice => choice.Parameters.GetValueOrDefault(key) == value).Id,
            game.Revision);

    private static void AnswerWithCard(GameEngine game, int cardId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Replacement prompt was lost.");
        var choice = prompt.Choices.Single(item => item.Cards.SequenceEqual([cardId]));
        var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Configured judgment replacement was rejected.");
    }

    private static void AnswerSkip(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Replacement prompt was lost.");
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Configured judgment replacement skip was rejected.");
    }

    private static void AssertCompletedReplay(
        GameEngine game,
        ContentRegistry registry,
        string expectedActivatedTrigger)
    {
        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload)
                    .OfType<ProgramJudgmentReplacementResolvedEvent>()
                    .Count(item => item.Activated && item.TriggerId == expectedActivatedTrigger) == 1,
            "A completed configured judgment replacement must replay without repeating its effects.");
    }

    private static void AssertAiReplacementAndReplay(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = FixturePackage.ModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 40
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var ownerSeat = game.CreateSnapshot(0, revealAll: true).Players
                .Single(player => player.GeneralId == FixturePackage.OwnerGeneralId).Seat;
            if (ownerSeat == 0) continue;

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var activated = game.Events.Select(item => item.Payload)
                    .OfType<ProgramJudgmentReplacementResolvedEvent>()
                    .FirstOrDefault(item => item.OwnerSeat == ownerSeat && item.Activated);
                if (activated is not null)
                {
                    var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
                    Require(replay.Events.Select(item => item.Payload)
                            .OfType<ProgramJudgmentReplacementResolvedEvent>()
                            .Count(item => item.OwnerSeat == ownerSeat && item.Activated) == 1 &&
                            SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                        "AI configured judgment replacement must choose only its private cards and replay once.");
                    return;
                }

                GameCommand command = game.PendingDecision is { PlayerSeat: 0 } pending
                    ? pending.Kind switch
                    {
                        DecisionKind.PlayCard =>
                            new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
                        DecisionKind.RespondDodge =>
                            Answer(pending, game, "response", "take-damage"),
                        DecisionKind.RescueDying =>
                            Answer(pending, game, "response", "let-die"),
                        _ => new AnswerPromptCommand(
                            0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                    }
                    : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded AI configured judgment replacement activated.");
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-program-judgment-replace-5";
        internal const string OwnerGeneralId = "judgment-replace-test:owner";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 4).Select(index => $"judgment-replace-test:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-judgment-replacement-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(ValidV4, Presentation);
            var program = catalog.Programs[ProgramId];
            var text = catalog.Presentations[ProgramId];
            builder.AddSkill(new ContentSkillDefinition(ProgramId, text.Name, text.Description)
            { Program = program });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "改判程序测试", "zhang_jiao", ProgramId, "qun", BaseHp: 4,
                AdditionalSkillIds: ["standard:ganglie"]));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "改判陪测", "cao_cao", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe(
                "judgment-replace-test:black-deck", "黑色装备改判夹具", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 24)
                    .Select(_ => new ContentDeckPhysicalCard("standard:offensive_horse", Suit.Spade, 5))
                    .Concat(Enumerable.Range(0, 28)
                        .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 5)))
                    .Concat(Enumerable.Range(0, 28)
                        .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 5)))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId, "可配置改判场景", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "judgment-replace-test:black-deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
        }
    }

    private sealed class BaguaFixturePackage : IGameContentPackage
    {
        internal const string ModeId = "identity:program-guidao-bagua-5";
        internal const string OwnerGeneralId = "program-guidao-bagua:owner";
        private const string SkillId = "program-guidao-bagua:guidao";
        private static readonly string[] OtherGeneralIds =
            Enumerable.Range(1, 4).Select(index => $"program-guidao-bagua:other-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-guidao-bagua-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(BaguaRules, BaguaPresentation);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "鬼道来源装备测试", "以黑色牌替换判定牌。")
            {
                Program = catalog.Programs[SkillId]
            });
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId, "鬼道八卦测试", "zhang_jiao", SkillId, "qun", BaseHp: 4));
            foreach (var id in OtherGeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                    id, "八卦陪测", "cao_cao", "standard:none", "wei", BaseHp: 4));
            var physicalCards = Enumerable.Range(0, 40).SelectMany(_ => new[]
            {
                new ContentDeckPhysicalCard("standard:bagua", Suit.Spade, 2),
                new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7),
                new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 9)
            }).ToArray();
            builder.AddDeck(new ContentDeckRecipe(
                "program-guidao-bagua:deck", "鬼道八卦来源装备夹具", 4, 2, [])
            {
                PhysicalCards = physicalCards
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId, "鬼道八卦来源装备测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "program-guidao-bagua:deck",
                GeneralCandidateCount: 5,
                GeneralPoolIds: [OwnerGeneralId, .. OtherGeneralIds]));
        }
    }

    private static void AssertReject(string rules, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string ProgramId = "judgment-replace-test:both";
    private const string ExchangeTriggerId = "a-exchange";
    private const string DiscardTriggerId = "b-discard";

    private const string ValidV4 = """
        {"schemaVersion":59,"skills":[
          {"id":"judgment-replace-test:both","revision":1,"triggers":[
            {"id":"a-exchange","window":"judgmentReplacing","subject":"any",
             "excludedReasons":["skill.leiji"],"optional":true,"effects":[
              {"op":"replaceJudgment","target":"owner","zones":["hand","equipment"],
               "suits":["spade","club"],"oldCardDestination":"ownerHand"},
              {"op":"draw","target":"owner","amount":1,"replacementSuits":["spade"],
               "minimumReplacementRank":2,"maximumReplacementRank":9}
             ]},
            {"id":"b-discard","window":"judgmentReplacing","subject":"any",
             "excludedReasons":[],"optional":true,"effects":[
              {"op":"replaceJudgment","target":"owner","zones":["hand","equipment"],
               "suits":["spade","club"],"oldCardDestination":"discardPile"}
             ]}
          ]}
        ]}
        """;

    private const string InvalidOrderV4 = """
        {"schemaVersion":59,"skills":[{"id":"invalid:order","revision":1,"triggers":[{
          "id":"replace","window":"judgmentReplacing","subject":"owner","excludedReasons":[],
          "optional":true,"effects":[
            {"op":"draw","target":"owner","amount":1,"replacementSuits":["spade"],
             "minimumReplacementRank":2,"maximumReplacementRank":9},
            {"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade"],
             "oldCardDestination":"discardPile"}
          ]}]}]}
        """;

    private const string InvalidFollowUpV4 = """
        {"schemaVersion":59,"skills":[{"id":"invalid:follow-up","revision":1,"triggers":[{
          "id":"replace","window":"judgmentReplacing","subject":"owner","excludedReasons":[],
          "optional":true,"effects":[
            {"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade"],
             "oldCardDestination":"discardPile"},
            {"op":"obtainOpponentHandCard","target":"owner","amount":1}
          ]}]}]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "judgment-replace-test:both":{
            "name":"改判交换测试","description":"测试旧判定牌去向与提交牌条件效果。"
          }
        }}
        """;

    private const string BaguaRules = """
        {"schemaVersion":59,"skills":[
          {"id":"program-guidao-bagua:guidao","revision":1,"triggers":[
            {"id":"replace","window":"judgmentReplacing","subject":"any","excludedReasons":[],
             "optional":true,"effects":[
              {"op":"replaceJudgment","target":"owner","zones":["hand","equipment"],
               "suits":["spade","club"],"oldCardDestination":"ownerHand"}
             ]}
          ]}
        ]}
        """;

    private const string BaguaPresentation = """
        {"schemaVersion":3,"skills":{
          "program-guidao-bagua:guidao":{
            "name":"鬼道来源装备测试","description":"以黑色牌替换判定牌。"
          }
        }}
        """;
}
