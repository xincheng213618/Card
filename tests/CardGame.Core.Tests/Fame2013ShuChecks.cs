using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2013ShuChecks
{
    public static void NextCardDoublePeachAlcoholAndBorrowedSword()
    {
        foreach (var kind in new[] { CardKind.Peach, CardKind.Alcohol, CardKind.BorrowedSword, CardKind.Slash, CardKind.Snatch, CardKind.IronChain })
        {
            var card = kind switch { CardKind.Peach => "standard:peach", CardKind.Alcohol => "standard:alcohol", CardKind.BorrowedSword => "classic:borrowed-sword",
                CardKind.Slash => "standard:slash", CardKind.Snatch => "standard:snatch", _ => "standard:iron_chain" };
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new NextTargetFixture(card));
            GameEngine? game = null;
            LegalAction? selected = null;
            for (var seed = 1; seed <= 64 && selected is null; seed++)
            {
                var candidate = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord,
                    PlayerCount = 4, ModeId = "identity:classic-next-target-fixture", UseInteractiveSetup = true, UseInteractiveDiscard = false,
                    AdvanceAfterHumanCommands = false, MaxTurns = 80 }, registry);
                Require(candidate.Submit(new StartGameCommand()).Accepted, "Next-target fixture did not start.");
                Require(candidate.Submit(new SelectGeneralCommand(0, "fixture:next-target-owner", candidate.Revision, candidate.PendingDecision!.PromptId)).Accepted,
                    "Next-target fixture actor could not be selected.");
                for (var turn = 0; turn < 3 && selected is null; turn++)
                {
                    Reach(candidate, pending => pending.Kind == DecisionKind.PlayCard);
                    if (kind == CardKind.BorrowedSword)
                        foreach (var seat in new[] { 1, 2 })
                        {
                            var snapshot = candidate.CreateSnapshot(0, true);
                            if (snapshot.Players[seat].Equipment.Count != 0 || snapshot.Players[0].Hand.All(item => item.Kind != CardKind.Crossbow)) continue;
                            Require(candidate.Submit(new UseProgramSkillCommand(0, "fixture:next-target", "equip", [], [seat], candidate.Revision,
                                candidate.PendingDecision!.PromptId)).Accepted, "Borrowed-Sword fixture equipment gift was rejected.");
                            Answer(candidate, candidate.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1 &&
                                snapshot.Players[0].Hand.Any(item => item.Id == choice.Cards[0] && item.Kind == CardKind.Crossbow)));
                            Reach(candidate, pending => pending.Kind == DecisionKind.PlayCard);
                        }
                    Require(candidate.Submit(new UseProgramSkillCommand(0, "fixture:next-target", "grant", [], [1], candidate.Revision,
                        candidate.PendingDecision!.PromptId)).Accepted, "Next-target fixture grant was rejected.");
                    Reach(candidate, pending => pending.Kind == DecisionKind.PlayCard);
                    selected = candidate.GetHumanLegalActions().FirstOrDefault(action => action.ProgramActivationId == "next-card-target-adjustment" &&
                        (action.PlayedCardKind ?? candidate.CreateSnapshot(0, true).Players[0].Hand.FirstOrDefault(item => item.Id == action.CardId)?.Kind) == kind &&
                        (kind == CardKind.BorrowedSword ? action.TargetSeats.Count == 4 :
                            kind == CardKind.IronChain ? action.TargetSeats.Count == 3 :
                            kind is CardKind.Slash or CardKind.Snatch ? action.TargetSeats.SequenceEqual(new[] { 1, 2 }) :
                            action.TargetSeats.SequenceEqual(new[] { 0, 1 })));
                    if (selected is not null) { game = candidate; break; }
                    Require(candidate.Submit(new EndPlayPhaseCommand(0, candidate.Revision, candidate.PendingDecision!.PromptId)).Accepted,
                        "Next-target fixture play phase could not end.");
                }
            }
            Require(game is not null && selected is not null, "No bounded next-target action fixture was found for " + kind);
            var before = game!.CreateSnapshot(0, true);
            var checkpoint = game.CreateCheckpoint();
            var restored = GameReplay.Restore(checkpoint, registry);
            var physical = selected!.CardId!.Value;
            Play(game); Play(restored);
            Settle(game); Settle(restored);
            Require(game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Count(item => item.CardId == physical) == 1 &&
                game.CardMovements.Count(move => move.CardId == physical && move.To == CardLocation.Processing) == 1,
                "An adjusted basic or Borrowed Sword must declare and pay its physical card once.");
            if (kind == CardKind.Peach)
                Require(game.CreateSnapshot(0, true).Players[0].Hp == before.Players[0].Hp + 1 &&
                    game.CreateSnapshot(0, true).Players[1].Hp == before.Players[1].Hp + 1, "One Peach must recover both wounded targets once.");
            else if (kind == CardKind.Alcohol)
                Require(game.Events.Select(item => item.Payload).OfType<AlcoholAppliedEvent>().Count(item => item.ResolutionId > 0 &&
                    item.SourceSeat is 0 or 1) == 2, "One Alcohol must grant its damage effect to both targets.");
            else if (kind == CardKind.BorrowedSword)
                Require(selected.TargetSeats.Where((_, index) => index % 2 == 0).All(seat =>
                    game.CardMovements.Any(move => move.From == CardLocation.Equipment(seat) &&
                        game.CardMovements.Any(delivery => delivery.CardId == move.CardId && delivery.To == CardLocation.Hand(0)))),
                    "Both requested weapon owners must independently respond or transfer their weapons.");
            else if (kind is CardKind.Slash or CardKind.Snatch)
                Require(game.GetCombatDistance(0, 2) > 1 && selected.TargetSeats.SequenceEqual(new[] { 1, 2 }),
                    "The initial target must remain ordinarily legal while the extra target ignores distance.");
            else if (kind == CardKind.IronChain)
                Require(selected.TargetSeats.All(seat => game.CreateSnapshot(0, true).Players[seat].IsChained),
                    "One Iron Chain must apply independently to all three designated targets.");
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
                "Adjusted basic and Borrowed Sword child cursors must replay exactly.");

            void Play(GameEngine branch) => Require(branch.Submit(new PlayCardCommand(0, physical, selected.TargetSeats,
                branch.Revision, branch.PendingDecision!.PromptId, selected.PlayedCardKind, selected.TargetCardId)
                { ConversionSource = selected.ConversionSource }).Accepted, "Adjusted card was rejected: " + kind);
        }
    }

    private static void Settle(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
            if (game.PendingDecision is { PlayerSeat: 0, Kind: not DecisionKind.PlayCard } prompt && prompt.Choices.Count > 0)
                Answer(game, prompt.Choices[0]);
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Adjusted-card child resolution could not advance.");
        Require(game.ResolutionStack.Count == 0, "Adjusted card did not settle.");
    }

    public static void ForeignPublicPileSlashPaymentLimitsAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new PublicPileFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "fixture:public-pile-mode", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80 }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Pile fixture did not start.");
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:pile-actor", game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Pile fixture actor could not be selected.");
        LegalAction? action = null;
        for (var step = 0; step < 768; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } pending)
            {
                action = game.GetHumanLegalActions().FirstOrDefault(item => item.ProgramSkillOwnerSeat is not null && item.ProgramSkillId == "classic:xiansi");
                if (action is not null) break;
                Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted, "Pile fixture play phase could not end.");
            }
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Pile fixture could not advance.");
        }
        Require(action is not null, "Pile fixture never exposed a paid foreign Slash.");
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:pile-adjust", "grant", [], [], game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Foreign Slash adjustment grant was rejected.");
        Reach(game, pending => pending.Kind == DecisionKind.PlayCard);
        action = game.GetHumanLegalActions().First(item => item.ProgramSkillId == "classic:xiansi" && item.TargetSeats.Count == 2);
        var ownerSeat = action!.ProgramSkillOwnerSeat!.Value;
        var payment = action.SelectableCardIds.Take(2).ToArray();
        var before = game.CreateSnapshot(0, revealAll: true);
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Use(game); Use(restored);
        Require(game.CardMovements.Count(move => payment.Contains(move.CardId) && move.From == CardLocation.Authority(ownerSeat) &&
            move.To == CardLocation.DiscardPile) == 2, "Foreign Slash must move both real public-pile payment cards to discard.");
        var accepted = game.ResolutionStack.OfType<CardUseFrame>().Last().Action!;
        Require(accepted.ActorSeat == 0 && accepted.ProviderSeat == 0 && accepted.EffectiveKind == CardKind.Slash &&
            accepted.PhysicalCards.Count == 2 && accepted.PhysicalCards.All(cost => cost.From == CardLocation.Authority(ownerSeat)) &&
            accepted.TargetSeats.SequenceEqual(action.TargetSeats), "Paid virtual Slash must retain actor and foreign-card provenance.");
        Require(game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Select(card => card.Id)
            .SequenceEqual(before.Players[0].Hand.Select(card => card.Id)), "Public-pile payment cannot spend the actor's hand cards.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)),
            "Foreign-pile Slash must replay at the ordinary response boundary.");
        var count = (int)typeof(GameEngine).GetField("_slashCountThisTurn", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        Require(count == 1, "Foreign-pile Slash must debit the actor's ordinary Slash quota.");
        Settle(game); Settle(restored);
        Require(action.TargetSeats.All(seat => game.Events.Select(item => item.Payload).Any(payload =>
            payload is CardRespondedEvent response && response.ResponderSeat == seat && response.SourceSeat == 0 ||
            payload is DamageAppliedEvent damage && damage.TargetSeat == seat && damage.SourceSeat == 0)),
            "Both paid virtual Slash targets must independently respond or take damage.");
        Require(game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Count(item => item.CardId == 0) == 1,
            "Adjusted paid Slash must finish once after both target responses.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)),
            "Adjusted foreign Slash must replay through both targets.");

        void Use(GameEngine branch) => Require(branch.Submit(new UseProgramSkillCommand(0, action.ProgramSkillId!, action.ProgramActivationId!,
            payment, action.TargetSeats, branch.Revision, branch.PendingDecision!.PromptId) { SkillOwnerSeat = ownerSeat }).Accepted,
            "The foreign-pile Slash command was rejected.");
    }

    public static void PublicPileCollectionPrivacyAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindGeneral(registry, "classic:liu-feng");
        var activation = Reach(game, pending => pending.SkillPrompt?.SkillId == "classic:xiansi" &&
            pending.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(game, activation.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var collection = game.PendingDecision!;
        Require(collection.Choices.Any(choice => choice.Targets.Contains(0)), "Public pile collection must permit its owner's own card.");
        var selected = collection.Choices.First(choice => choice.Targets.Count == 2 && choice.Targets.Contains(0));
        var foreignSeat = selected.Targets.Single(seat => seat != 0);
        var fullBefore = game.CreateSnapshot(0, revealAll: true);
        var foreignHand = fullBefore.Players[foreignSeat].Hand.Select(card => card.Id).ToHashSet();
        Require(collection.Choices.Where(choice => choice.Targets.Contains(foreignSeat)).All(choice =>
                !choice.Cards.Any(foreignHand.Contains)), "Opposing hand ids must remain private in public-pile selection choices.");
        var checkpoint = game.CreateCheckpoint();
        var branch = GameReplay.Restore(checkpoint, registry);
        Answer(game, selected);
        Answer(branch, branch.PendingDecision!.Choices.Single(choice => choice.Id == selected.Id));
        var snapshot = game.CreateSnapshot(0, revealAll: true);
        var pile = snapshot.Players[0].AuthorityCards!;
        Require(pile.Count == 2 && pile.Select(card => card.Id).Distinct().Count() == 2,
            "Collection must retain two different physical public cards.");
        Require(game.CreateSnapshot(foreignSeat).Players[0].AuthorityCards!.Select(card => card.Id).SequenceEqual(pile.Select(card => card.Id)),
            "Every player must see every physical public-pile card.");
        Require(SnapshotJson.Serialize(snapshot) == SnapshotJson.Serialize(branch.CreateSnapshot(0, revealAll: true)),
            "Public-pile selection must replay identically from a suspended prompt.");
    }

    public static void PindianClaimsWinLossTieAndNextCardExpiry()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        foreach (var outcome in new[] { 1, -1, 0 })
        {
            var fixture = FindContest(registry, outcome);
            var game = fixture.Game;
            var claim = game.PendingDecision!;
            var taking = claim.Choices.Single(choice => choice.Parameters.GetValueOrDefault("take") == "true");
            var result = game.Events.Select(item => item.Payload).OfType<PindianResultDeterminedEvent>().Last().Result;
            var expected = result.SourceWon ? result.OpponentCardId : result.SourceCardId;
            Require(taking.Cards.SequenceEqual(new[] { expected }), "Pindian win claims the lower card; loss and tie claim one's own card.");
            var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Answer(game, taking);
            Answer(restored, restored.PendingDecision!.Choices.Single(choice => choice.Id == taking.Id));
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)),
                "Pindian claim cursor, physical movement and continuation must replay identically.");
            Reach(game, pending => pending.Kind == DecisionKind.PlayCard);
            Require(game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card => card.Id == expected), "Claimed Pindian card must enter claimant hand.");
            if (outcome <= 0)
            {
                Require(game.GetHumanLegalActions().Where(action => action.CardId is not null && action.Kind != LegalActionKind.Recast).All(action =>
                    CardCatalog.Get(action.PlayedCardKind ?? game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Single(card => card.Id == action.CardId).Kind).CategoryName != "锦囊牌"),
                    "Losing or tying Qiaoshui must prohibit all trick uses, including delayed tricks.");
            }
            else
            {
                var hasGrant = typeof(GameEngine).GetMethod("HasNextCardTargetAdjustment", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
                Require((bool)hasGrant.Invoke(game, new[] { players[0] })!, "Winning Qiaoshui must arm the next actual card.");
                var rejected = game.Submit(new PlayCardCommand(0, int.MaxValue, [], game.Revision, game.PendingDecision!.PromptId));
                Require(!rejected.Accepted && (bool)hasGrant.Invoke(game, new[] { players[0] })!, "An invalid command must not consume a next-card grant.");
                var action = game.GetHumanLegalActions().FirstOrDefault(item => item.CardId is not null && item.Kind != LegalActionKind.Recast);
                if (action is not null)
                {
                    Require(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision,
                        game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource }).Accepted,
                        "The next actual card must be accepted.");
                    Require(!(bool)hasGrant.Invoke(game, new[] { players[0] })!, "The first actual card consumes Qiaoshui even if its targets cannot be adjusted.");
                }
            }
        }
    }

    private static (GameEngine Game, int SourceRank) FindContest(ContentRegistry registry, int outcome)
    {
        for (var seed = 1; seed < 4096; seed++)
        {
            var game = Create(seed, registry);
            if (!Select(game, "classic:jian-yong")) continue;
            var activation = Reach(game, pending => pending.SkillPrompt?.SkillId == "classic:qiaoshui" &&
                pending.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, activation.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            var contest = game.PendingDecision!;
            var full = game.CreateSnapshot(0, revealAll: true);
            var pair = contest.Choices.Where(choice => choice.Targets.Count == 1)
                .SelectMany(choice => full.Players[0].Hand.Select(card => (Choice: choice, Card: card)))
                .FirstOrDefault(item => Math.Sign(item.Card.Rank - full.Players[item.Choice.Targets[0]].Hand.Max(card => card.Rank)) == outcome);
            if (pair.Choice is null) continue;
            Answer(game, pair.Choice);
            Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Cards.Contains(pair.Card.Id)));
            Reach(game, pending => pending.SkillPrompt?.SkillId == "classic:zongshi-pindian");
            return (game, pair.Card.Rank);
        }
        throw new InvalidOperationException("No bounded Qiaoshui Pindian outcome fixture was found.");
    }

    private static GameEngine FindGeneral(ContentRegistry registry, string id)
    {
        for (var seed = 1; seed < 4096; seed++)
        {
            var game = Create(seed, registry);
            if (Select(game, id)) return game;
        }
        throw new InvalidOperationException("No bounded general fixture was found.");
    }
    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5, ModeId = "identity:classic-5",
        UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
    }, registry);
    private static bool Select(GameEngine game, string id)
    {
        if (!game.Submit(new StartGameCommand()).Accepted) return false;
        var prompt = game.PendingDecision;
        return prompt is { Kind: DecisionKind.SelectGeneral } && prompt.ValidContentIds.Contains(id) &&
            game.Submit(new SelectGeneralCommand(0, id, game.Revision, prompt.PromptId)).Accepted;
    }
    private static PendingDecision Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 96; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } pending && predicate(pending)) return pending;
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Fixture could not advance.");
        }
        throw new InvalidOperationException("Fixture did not reach the requested boundary.");
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Require(game.Submit(new AnswerPromptCommand(
        game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)).Accepted, "The published fixture choice was rejected.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class PublicPileFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-public-pile", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var adjustment = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:pile-adjust","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantNextCardTargetAdjustment","target":"owner"}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:pile-adjust":{"name":"目标","description":"下一张牌调整目标"}}}""");
            builder.AddSkill(new("fixture:pile-adjust", "目标", "下一张牌调整目标") { Program = adjustment.Programs["fixture:pile-adjust"], ProgramPresentation = adjustment.Presentations["fixture:pile-adjust"] });
            builder.AddGeneral(new("fixture:pile-actor", "测试借杀者", "supporter", "fixture:pile-adjust", "shu", BaseHp: 10));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:pile-owner-{seat}", $"测试牌堆者{seat}", "supporter", "classic:xiansi", "shu", BaseHp: 10));
            builder.AddDeck(new("fixture:pile-deck", "公开牌堆测试", 8, 0, [new("standard:dodge", 160)]));
            builder.AddMode(new("fixture:public-pile-mode", "公开牌堆测试", 4, 4,
                new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:pile-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:pile-actor", "fixture:pile-owner-1", "fixture:pile-owner-2", "fixture:pile-owner-3"]));
        }
    }

    private sealed class NextTargetFixture(string cardId) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-next-target", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:next-target","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"loseHp","target":"selectedTarget","amount":1},{"op":"grantNextCardTargetAdjustment","target":"owner"}]},{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"cardCategories":["equipment"],"destination":"selectedTargetEquipment","targetRef":{"kind":"selectedTarget"}}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:next-target":{"name":"下一牌目标","description":"双方失去一点体力，下一张牌增减目标"}}}""");
            builder.AddSkill(new("fixture:next-target", "下一牌目标", "下一张牌增减一个目标")
            { Program = catalog.Programs["fixture:next-target"], ProgramPresentation = catalog.Presentations["fixture:next-target"] });
            builder.AddGeneral(new("fixture:next-target-owner", "目标测试者", "supporter", "fixture:next-target", "shu", BaseHp: 10));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:next-target-{seat}", $"目标{seat}", "supporter", "standard:none", "wei", BaseHp: 10));
            builder.AddDeck(new("fixture:next-target-deck", "下一牌目标测试", 8, 0, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(cardId == "classic:borrowed-sword" && index % 2 == 0
                ? "standard:crossbow" : cardId, Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-next-target-fixture", "下一牌目标测试", 4, 4,
                new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:next-target-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:next-target-owner", "fixture:next-target-1", "fixture:next-target-2", "fixture:next-target-3"]));
        }
    }
}
