using CardGame.Content.Standard;
using CardGame.Core;
using System.Text.Json;

internal static class Fame2016DeferredChecks
{
    public static void PrivateTopGainOrderAndReplay()
    {
        var registry = Registry("li"); var game = Start(registry);
        Activate(game, "classic:duliang", "take-hand-and-benefit", 1);
        var opaque = Prompt(game)!.Choices.First();
        Require(opaque.Cards.Count == 0, "Taking another hand must offer opaque physical slots.");
        Atomic(game, new AnswerPromptCommand(0, Prompt(game)!.PromptId, new("unpublished"), game.Revision));
        Replay(game, registry); Answer(game, opaque); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
        Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "view"));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "participant-top-view"));
        var viewing = Prompt(game)!; var ids = viewing.ValidCardIds.ToArray();
        Require(viewing.PlayerSeat == 1 && viewing.IsPrivate && ids.Length == 2, "The selected participant alone must privately view two real top cards.");
        Require(game.CreateSnapshot(0, false).PendingDecision is null && game.CreateSnapshot(2, false).PendingDecision is null &&
                game.CreateSnapshot(1, false).PendingDecision?.ValidCardIds.SequenceEqual(ids) == true &&
                Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat, false).PublicRevealedCards.Count == 0) && game.CreateSnapshot(1, false).PrivateRevealedCards?.Select(card => card.Id).SequenceEqual(ids) == true && new[] { 0, 2, 3 }.All(seat => game.CreateSnapshot(seat, false).PrivateRevealedCards is null),
            "Private top-card faces must not enter public or owner views.");
        Replay(game, registry); var movesBefore = game.CardMovements.Count; Answer(game, viewing.Choices.Single()); Settle(game);
        var moved = game.CardMovements.Skip(movesBefore).ToArray();
        Require(moved.Where(m => m.To == CardLocation.Hand(1)).All(m => m.CardKind is CardKind.Dodge or CardKind.Slash or CardKind.Peach or CardKind.Alcohol), "Only basic top cards may be obtained.");
        var returned = moved.Where(m => m.To == CardLocation.DrawPile).Select(m => m.CardId).ToArray();
        var top = game.CreateCardZoneDiagnostics().Where(z => z.Location == CardLocation.DrawPile).OrderByDescending(z => z.ZoneIndex).Take(returned.Length).Select(z => z.CardId);
        Require(top.SequenceEqual(returned), "Non-basic cards must return to the top in the original viewed order.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:duliang"), "The per-play-phase activation must be spent.");
        Replay(game, registry);
    }

    public static void ObtainedEntityDiscardExclusionAndNextTurnDraw()
    {
        var registry = Registry("li"); var game = Start(registry);
        Activate(game, "classic:duliang", "take-hand-and-benefit", 1); Answer(game, Prompt(game)!.Choices.First());
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
        Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "next-turn-draw")); Settle(game);
        var obtained = game.Events.Select(e => e.Payload).SkipWhile(e => e is not TurnStartedEvent).OfType<CardMovedEvent>()
            .Where(e => e.To == CardLocation.Hand(0)).Select(e => e.CardId).ToHashSet();
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)));
        Reach(game, p => p.Kind == DecisionKind.DiscardCards); var prompt = Prompt(game)!;
        var hand = game.CreateSnapshot(0, true).Players[0].Hand;
        var excluded = hand.Where(c => obtained.Contains(c.Id)).Select(c => c.Id).ToArray();
        Require(excluded.Length > 0 && prompt.ValidCardIds.All(id => !excluded.Contains(id)) && prompt.RequiredCardCount == prompt.ValidCardIds.Count - game.CreateSnapshot(0, true).Players[0].Hp,
            "Fulin must exclude actual obtained entities still in hand both from the count and from legal discard choices.");
        Replay(game, registry);
        var bad = prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(); bad[0] = excluded[0];
        Atomic(game, new DiscardCardsCommand(0, bad, prompt.PromptId, game.Revision));
        Accept(game.Submit(new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision)));
        for (var i = 0; i < 120 && !game.Events.Any(e => e.Payload is PhaseChangedEvent { ActorSeat: 1, Phase: TurnPhase.Play }); i++) Advance(game);
        var turn = game.Events.Select(e => e.Payload).ToArray(); var start = Array.FindLastIndex(turn, e => e is TurnStartedEvent { ActorSeat: 1 });
        Require(turn.Skip(start + 1).OfType<CardMovedEvent>().Count(e => e.To == CardLocation.Hand(1) && e.Reason == CardMoveReasons.Draw) == 3,
            "The target's next ordinary draw phase must receive exactly one extra real card.");
        Replay(game, registry);
    }

    public static void ProviderPileZeroThreeAndNextTurnReplay()
    {
        foreach (var count in new[] { 0, 3 })
        {
            var registry = Registry("sun"); var game = Start(registry); Activate(game, "classic:kuangbi", "deferred-provider-pile", 1);
            var before = game.CardMovements.Count;
            Require(Prompt(game)!.PlayerSeat == 1 && game.CreateSnapshot(0, false).PendingDecision is null, "The original provider must privately choose its own actual cards.");
            for (var index = 0; index < count; index++)
            {
                Replay(game, registry); Atomic(game, new AnswerPromptCommand(1, Prompt(game)!.PromptId, new("invalid-provider"), game.Revision));
                Answer(game, Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
                if (index < count - 1) Require(game.CardMovements.Count == before, "A partial provider selection must not move any card.");
            }
            if (count == 0) Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));
            Settle(game); var pile = game.CreateSnapshot(0, true).Players[0].PublicDeferredPileCards;
            Require((pile?.Count ?? 0) == count && Enumerable.Range(0, 4).All(seat => (game.CreateSnapshot(seat, false).Players[0].PublicDeferredPileCards?.Count ?? 0) == count), "The owner's real deferred pile must be public to every viewer.");
            Replay(game, registry);
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Require((game.CreateSnapshot(0, true).Players[0].PublicDeferredPileCards?.Count ?? 0) == 0, "The next owner turn must obtain its deferred pile before ordinary play.");
            Require(game.CardMovements.Count(m => m.Reason.Value == "skill-program.deferred-pile.obtain") == count &&
                    game.CardMovements.Count(m => m.Reason.Value == "skill-program.deferred-pile.provider-reward") == count,
                "The original provider must draw exactly the number of actual obtained cards; zero must create no deferred reward.");
            var obtained = game.CardMovements.Where(m => m.Reason.Value == "skill-program.deferred-pile.obtain").Select(m => m.Sequence).ToArray();
            var rewarded = game.CardMovements.Where(m => m.Reason.Value == "skill-program.deferred-pile.provider-reward").Select(m => m.Sequence).ToArray();
            Require(count == 0 || obtained.Max() < rewarded.Min(), "All pile cards must be obtained before the provider's reward draws."); Replay(game, registry);
        }
    }

    public static void StrictProviderSourceContracts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Read(string suffix)
        { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("classic-sun-deng.rules.json"); var presentation = Read("classic-sun-deng.presentation.json");
        var root = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
        foreach (var change in new Action<System.Text.Json.Nodes.JsonNode>[]
        {
            node => node["skills"]![0]!["activations"]![0]!["effects"]![0]!["target"] = "owner",
            node => node["skills"]![0]!["activations"]![0]!["effects"]![0]!["maximumCards"] = 4,
            node => node["skills"]![0]!["activations"]![0]!["effects"]![0]!["zones"]![0] = "judgment",
            node => node["skills"]![0]!["activations"]![0]!["effects"]![1]!["sourceBind"] = "missing"
        })
        {
            var malformed = root.DeepClone(); change(malformed); var rejected = false;
            try { SkillProgramCatalog.Load(malformed.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "A wrong provider, oversized set, wrong source zone or unknown binding must fail at the shared loader before any runtime payment.");
        }
    }

    public static void ObtainMovementInterruptionAndReplay()
    {
        var registry = Registry("sun-observer"); var game = Start(registry);
        Activate(game, "classic:kuangbi", "deferred-provider-pile", 1);
        Answer(game, Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards")); Settle(game);
        var id = game.CreateSnapshot(0, true).Players[0].PublicDeferredPileCards!.Single().Id;
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "observe-obtain"));
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Any(c => c.Id == id) &&
            !game.CardMovements.Any(m => m.Reason.Value == "skill-program.deferred-pile.provider-reward"),
            "A nested actual gain observer must pause after owner obtain and before the original provider reward.");
        Replay(game, registry); Answer(game, Prompt(game)!.Choices.Single());
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(game.CardMovements.Count(m => m.Reason.Value == "skill-program.deferred-pile.provider-reward") == 1,
            "The interrupted deferred continuation must reward the original provider once."); Replay(game, registry);
    }

    public static void DeferredPileDeathLossAndFaceDownSkip()
    {
        foreach (var boundary in new[] { "provider-death", "owner-death", "skill-loss" })
        {
            var registry = Registry("sun"); var game = Start(registry);
            Activate(game, "classic:kuangbi", "deferred-provider-pile", 1);
            Answer(game, Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
            Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards")); Settle(game);
            var card = game.CreateSnapshot(0, true).Players[0].PublicDeferredPileCards!.Single().Id;
            Replay(game, registry);
            Activate(game, "fixture:deferred-driver", boundary, boundary == "provider-death" ? 1 : -1);
            for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
                if (Prompt(game) is { Kind: DecisionKind.RescueDying } rescue) Answer(game, rescue.Choices.First(c => c.Cards.Count == 0)); else Advance(game);
            Require(game.ResolutionStack.Count == 0, "Death or skill replacement must finish its original continuation.");
            Replay(game, registry);
            if (boundary != "provider-death")
                Require((game.CreateSnapshot(0, true).Players[0].PublicDeferredPileCards?.Count ?? 0) == 0 && game.CardMovements.Any(m => m.CardId == card && m.To == CardLocation.DiscardPile), "Dead owners and genuinely lost sources must discard the physical pile with no deferred reward.");
            else
            {
                Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)));
                Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
                Require(game.CardMovements.Any(m => m.CardId == card && m.To == CardLocation.Hand(0)) && !game.CardMovements.Any(m => m.Reason.Value == "skill-program.deferred-pile.provider-reward"), "A dead provider must not prevent the owner's next-turn obtain and must receive no draw reward."); Replay(game, registry);
            }
        }
        var liRegistry = Registry("li"); var li = Start(liRegistry);
        Activate(li, "classic:duliang", "take-hand-and-benefit", 1); Answer(li, Prompt(li)!.Choices.First());
        Reach(li, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
        Answer(li, Prompt(li)!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "next-turn-draw")); Settle(li);
        Activate(li, "fixture:deferred-driver", "face-down", 1); Settle(li);
        Accept(li.Submit(new EndPlayPhaseCommand(0, li.Revision, Prompt(li)!.PromptId)));
        Reach(li, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Accept(li.Submit(new EndPlayPhaseCommand(0, li.Revision, Prompt(li)!.PromptId)));
        for (var step = 0; step < 500; step++)
        {
            var history = li.Events.Select(e => e.Payload).ToArray();
            var start = Array.FindLastIndex(history, e => e is TurnStartedEvent { ActorSeat: 1 });
            if (history.Count(e => e is TurnStartedEvent { ActorSeat: 1 }) >= 2 &&
                history.Skip(start + 1).Any(e => e is PhaseChangedEvent { ActorSeat: 1, Phase: TurnPhase.Play })) break;
            if (Prompt(li) is { Kind: DecisionKind.DiscardCards } discard)
                Accept(li.Submit(new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, li.Revision)));
            else Advance(li);
        }
        var events = li.Events.Select(e => e.Payload).ToArray(); var targetTurn = Array.FindLastIndex(events, e => e is TurnStartedEvent { ActorSeat: 1 });
        Require(events.Skip(targetTurn + 1).OfType<CardMovedEvent>().Count(e => e.To == CardLocation.Hand(1) && e.Reason == CardMoveReasons.Draw) == 2,
            "A facedown skipped next turn must consume the current-edition promised draw benefit instead of accumulating it across the skip. actual=" + events.Skip(targetTurn + 1).OfType<CardMovedEvent>().Count(e => e.To == CardLocation.Hand(1) && e.Reason == CardMoveReasons.Draw)); Replay(li, liRegistry);
    }

    private static ContentRegistry Registry(string kind) => ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(kind));
    private static GameEngine Start(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:deferred-check", UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 30 }, registry);
        Accept(game.Submit(new StartGameCommand())); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:deferred-owner", game.Revision, Prompt(game)!.PromptId))); Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); return game;
    }
    private static void Activate(GameEngine game, string skill, string activation, int seat) => Accept(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new UseProgramSkillCommand(0, skill, activation, [], seat < 0 ? [] : [seat], game.Revision, Prompt(game)!.PromptId)])).Single()));
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new AnswerPromptCommand(Prompt(game)!.PlayerSeat, Prompt(game)!.PromptId, choice.Id, game.Revision)])).Single()));
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    { for (var step = 0; step < 500; step++) { if (Prompt(game) is { } p && predicate(p)) return; if (Prompt(game) is { Kind: DecisionKind.DiscardCards } discard) Accept(game.Submit(new DiscardCardsCommand(discard.PlayerSeat, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision))); else Advance(game); } throw new InvalidOperationException("Required fixture boundary was not reached: " + Prompt(game)?.Kind + " state=" + SnapshotJson.Serialize(game.CreateSnapshot(0, true))); }
    private static void Settle(GameEngine game)
    { for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++) Advance(game); Require(game.ResolutionStack.Count == 0, "Program must settle."); Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat, false)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat, false))) &&
                game.CardMovements.SequenceEqual(copy.CardMovements) && game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
            "Checkpoint JSON replay must reconstruct private prompts, every physical movement, public piles and exact typed events.");
    }
    private static void Atomic(GameEngine game, GameCommand command)
    { var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint()); var moves = game.CardMovements.Count; Require(!game.Submit(command).Accepted && checkpoint == GameCheckpointJson.Serialize(game.CreateCheckpoint()) && moves == game.CardMovements.Count, "Unpublished input must reject atomically."); }
    private static void Accept(CommandResult result) => Require(result.Accepted, "Fixture command rejected: " + result.Error?.Message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-deferred", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:deferred-driver","revision":1,"activations":[
              {"id":"provider-death","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
              {"id":"owner-death","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20},{"op":"loseHp","target":"owner","amount":20}]},
              {"id":"skill-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:kuangbi"],"sourceBind":"classic:fulin"}]},
              {"id":"face-down","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"selectedTarget","faceDown":true}]}
            ]}]}
            """, """{"schemaVersion":3,"skills":{"fixture:deferred-driver":{"name":"边界驱动","description":"真实死亡、技能替换和覆面"}}}""");
            b.AddSkill(new("fixture:deferred-driver", "边界驱动", "真实死亡、技能替换和覆面") { Program = catalog.Programs["fixture:deferred-driver"] });
            if (kind == "sun-observer")
            {
                var observer = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:deferred-observer","revision":1,"triggers":[
                  {"id":"observe","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch",
                   "movementReasons":["skill-program.deferred-pile.obtain"],"optional":false,
                   "effects":[{"op":"chooseOption","target":"owner","resultBind":"observe-obtain","options":[{"id":"continue"}]}]}
                ]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:deferred-observer":{"name":"获得观察","description":"获得后暂停","optionLabels":{"continue":"继续"}}}}""");
                b.AddSkill(new("fixture:deferred-observer", "获得观察", "获得后暂停") { Program = observer.Programs["fixture:deferred-observer"] });
            }
            b.AddGeneral(new("fixture:deferred-owner", "测试延迟将", "supporter", kind == "li" ? "classic:duliang" : "classic:kuangbi", "shu", kind == "li" ? 3 : 20, kind == "li" ? ["classic:fulin", "fixture:deferred-driver"] : kind == "sun-observer" ? ["fixture:deferred-driver", "fixture:deferred-observer"] : ["fixture:deferred-driver"]));
            for (var index = 1; index < 4; index++) b.AddGeneral(new($"fixture:deferred-{index}", $"目标{index}", "supporter", "standard:none", "wei", 20));
            b.AddDeck(new("fixture:deferred-deck", "延迟牌堆", 8, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(kind.StartsWith("sun") || i % 2 == 0 ? "standard:dodge" : "standard:bagua", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:deferred-check", "延迟机制测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:deferred-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:deferred-owner", "fixture:deferred-1", "fixture:deferred-2", "fixture:deferred-3"]));
        }
    }
}
