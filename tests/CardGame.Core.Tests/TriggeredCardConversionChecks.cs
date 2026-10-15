using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TriggeredCardConversionChecks
{
    private const string Niluan = "ol:niluan", Xiaoxi = "ol:xiaoxi", Driver = "fixture:trigger-conversion-driver";
    private const string Pause = "fixture:trigger-conversion-recovery", Opponent = "fixture:trigger-conversion-opponent";
    private const string Mode = "identity:classic-trigger-conversion";

    public static void BlackHandAndEquipmentUseRealMaterialsAndColdReplay()
    {
        foreach (var equipment in new[] { false, true })
        {
            var (game, registry) = Create(equipment ? "fixture:silver-lion" : "standard:crossbow");
            var material = game.CreateSnapshot(0).Players[0].Hand[0];
            if (equipment)
            {
                Accept(game, new PlayCardCommand(0, material.Id, [], game.Revision, Prompt(game)!.PromptId));
                Reach(game, IsPlay);
                Use(game, "lose", [0]);
            }
            End(game);
            Reach(game, p => Offer(p, Niluan));
            var ending = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
            Require(ending.OwnerSeat == 1 && ending.Items.Any(item => item.Candidate?.SkillId == Niluan &&
                    item.Facts is { TurnOwnerHp: 20, TurnOwnerSlashUseCount: 1 }),
                "The real ending boundary freezes the actual turn owner's HP and Slash-use ledger.");
            game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
            var choicePrompt = Prompt(game)!;
            Require(choicePrompt.Choices.Any(c => c.Cards.SequenceEqual([material.Id])), "The appropriate black hand/equipment entity must be offered.");
            for (var seat = 1; seat < 4; seat++)
                Require(game.CreateSnapshot(seat).PendingDecision is null, "An opponent cannot inspect the owner's private material options.");
            var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            Require(!game.Submit(new AnswerPromptCommand(1, choicePrompt.PromptId, choicePrompt.Choices[0].Id, game.Revision)).Accepted &&
                before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "An incorrect actor is rejected before any payment.");
            game = Cold(game, registry);
            var hpBefore = game.State.Players[0].Hp;
            var targetHp = game.State.Players[1].Hp;
            Answer(game, c => c.Cards.SequenceEqual([material.Id]));
            if (equipment)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == Pause);
                Require(game.State.Players[0].Hp == hpBefore + 1 && game.State.Players[1].Hp == targetHp,
                    "Silver Lion's actual equipment departure recovers before the Slash target resolves.");
                var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == material.Id);
                Require(use.Action is { EffectiveKind: CardKind.Slash, ActorSeat: 0, ProviderSeat: 0 } action &&
                    action.PhysicalCards.Single().From == CardLocation.Equipment(0) &&
                    action.ConversionChain.Single().SkillId == Niluan && action.TargetSeats.SequenceEqual([1]),
                    "The accepted Use retains its equipment source, actor, physical entity, conversion and sole ending target.");
                game = Cold(game, registry);
                Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            Reach(game, p => IsPlay(p) || Offer(p, Niluan));
            Require(Facts<CardUsedEvent>(game).Count(e => e.SourceSeat == 0 && e.CardId == material.Id && e.CardKind == CardKind.Slash) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.CardId == material.Id && e.CardKind == CardKind.Slash) == 1 &&
                game.CardMovements.Count(m => m.CardId == material.Id && m.Reason == CardMoveReasons.Use &&
                    m.From == (equipment ? CardLocation.Equipment(0) : CardLocation.Hand(0)) && m.To == CardLocation.Processing) == 1 &&
                game.State.Players[1].Hp == targetHp - 1,
                "The true converted Slash pays its one selected entity once and resolves one ordinary Slash.");
            Cold(game, registry);
        }
    }

    public static void RoundFirstOpportunityDoesNotMoveAfterDeclineOrExtraTurn()
    {
        var (restricted, restrictedRegistry) = Create("standard:crossbow", forceOpponentSlash: false, selfOnlyRound: true);
        Require(!Facts<ProgramBindingStartedEvent>(restricted).Any(e => e.SkillId == Xiaoxi) &&
            !Facts<CardUsedEvent>(restricted).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash),
            "A round-first SelfOnly restriction prevents both the Xiaoxi offer and any foreign-target Slash.");
        Cold(restricted, restrictedRegistry);
        var (game, registry) = Create("standard:crossbow", forceOpponentSlash: false, stopAtRound: true);
        Require(Offer(Prompt(game)!, Xiaoxi), "The first actual normal turn opens Xiaoxi for its owner.");
        game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Reach(game, IsPlay);
        Use(game, "extra", [0]); Use(game, "face", [1]);
        End(game);
        var repeated = false;
        Reach(game, IsPlay, onXiaoxi: () => repeated = true);
        Require(game.State.CurrentSeat == 0 && Facts<RoundStartedEvent>(game).Count() == 1 && !repeated,
            "The inserted extra turn never reopens the declined round-first choice or advances the round.");
        End(game);
        Reach(game, p => Offer(p, Xiaoxi), failOnNiluan: true);
        Require(Facts<RoundStartedEvent>(game).Count() == 2 && !game.State.Players[1].IsFaceDown &&
            Facts<CardUsedEvent>(game).All(e => e.SourceSeat != 0 || e.CardKind != CardKind.Slash),
            "The skipped face-down seat does not reopen Xiaoxi; only the next actual round does.");
        game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Require(Prompt(game)!.Choices.Any(c => c.Targets.SequenceEqual([2])), "Xiaoxi's real zero-entity Slash ignores ordinary range.");
        Answer(game, c => c.Targets.SequenceEqual([2])); Reach(game, IsPlay);
        Require(Facts<CardUseFinishedEvent>(game).Count(e => e.CardId == 0 && e.CardKind == CardKind.Slash) == 1,
            "The accepted second-round opportunity produces exactly one real virtual Slash.");
        Cold(game, registry);
    }

    private static (GameEngine, ContentRegistry) Create(string card, bool forceOpponentSlash = true, int ownerHp = 8,
        bool red = false, bool forbid = false, bool stopAtRound = false, bool selfOnlyRound = false, int opponentHp = 20)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(card, forceOpponentSlash, ownerHp, red, forbid, selfOnlyRound, opponentHp));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:trigger-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => stopAtRound ? Offer(p, Xiaoxi) : IsPlay(p),
            onXiaoxi: () => Require(!selfOnlyRound, "A SelfOnly actor must not be offered a round-first foreign-target Slash."));
        return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s, false).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision p) => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0;
    private static bool Offer(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Use(GameEngine g, string activation, int[] targets)
    { Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, Prompt(g)!.PromptId)); Reach(g, IsPlay); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate, Action? onNiluan = null, Action? onXiaoxi = null, bool failOnNiluan = false)
    {
        for (var step = 0; step < 250; step++)
        {
            var p = Prompt(g);
            if (p is not null && predicate(p)) return;
            if (p is { PlayerSeat: 0 })
            {
                if (Offer(p, Niluan)) { Require(!failOnNiluan, "Unexpected retaliation."); onNiluan?.Invoke(); }
                if (Offer(p, Xiaoxi)) onXiaoxi?.Invoke();
                if (IsPlay(p)) End(g);
                else if (p.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                else
                {
                    var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ??
                        p.Choices.FirstOrDefault(c => c.Cards.Count == 0 && c.Targets.Count == 0) ??
                        throw new InvalidOperationException("Unexpected conversion fixture prompt: " + JsonSerializer.Serialize(p));
                    Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
                }
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Conversion fixture exceeded its bounded next-prompt steps: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected fixture command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s, false)) == SnapshotJson.Serialize(copy.CreateSnapshot(s, false))) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements) &&
            g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))) &&
            g.CreateCardZoneDiagnostics().Count == 80 && g.ObserverFailures.Count == 0,
            "Frozen facts, private options, physical payments, frame continuations and events must survive cold replay.");
        return copy;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string ReadResource(string kind)
    { var assembly = typeof(StandardContentPackage).Assembly; using var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("ol-han-sui." + kind + ".json", StringComparison.Ordinal)))!); return reader.ReadToEnd(); }
    private static SkillProgramCatalog ReadHanSui() => SkillProgramCatalog.Load(ReadResource("rules"), ReadResource("presentation"));

    private sealed class Fixture(string card, bool opponentSlash, int ownerHp, bool red, bool forbid, bool selfOnlyRound, int opponentHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-trigger-conversion", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddCard(new("fixture:silver-lion", "白银狮子", "装备牌", "原生银狮装备", CardKind.SilverLion));
            builder.AddCard(new("fixture:halberd", "方天画戟", "装备牌", "原生四距离武器", CardKind.FangtianHalberd));
            var han = ReadHanSui();
            foreach (var id in new[] { Niluan, Xiaoxi }) builder.AddSkill(new(id, han.Presentations[id].Name, han.Presentations[id].Description)
            { Program = han.Programs[id], ProgramPresentation = han.Presentations[id] });
            void Add(string id, string body)
            {
                var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,{{body}}}]}""",
                    JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
                    { [id] = new { name = id, description = id, optionLabels = id == Pause
                        ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() } } }));
                builder.AddSkill(new(id, id, id) { Program = catalog.Programs[id] });
            }
            Add(Driver, """
            "activations":[
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
              {"id":"extra","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner","targetRef":{"kind":"selectedTarget"}}]},
              {"id":"face","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setFaceState","target":"selectedTarget","faceDown":true}]}]
            """);
            Add(Pause, """
            "triggers":[{"id":"pause-recovery","window":"afterHpRecovered","subject":"owner","optional":false,
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]}]}]
            """);
            Add(Opponent, """
            "triggers":[{"id":"real-preparation-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
            "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},
            {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","useCardActionWindows":true,"targetRestriction":"distanceUnlimitedAgainstTarget"}]}]
            """);
            if (forbid) Add("fixture:trigger-ban", """
            "triggers":[{"id":"ban-foreign-turn","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","optional":false,
            "effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash"],"actionTypes":["use"]}]}]
            """);
            if (selfOnlyRound) Add("fixture:trigger-round-self", """
            "triggers":[{"id":"restrict-before-xiaoxi","window":"roundStarting","subject":"owner","optional":false,"priority":100,
            "effects":[{"op":"grantTurnCardTargetRestriction","target":"owner","targetRestriction":"selfOnly"}]}]
            """);
            builder.AddGeneral(new("fixture:trigger-owner", "触发转化", "supporter", Niluan, "qun", ownerHp,
                [Xiaoxi, Driver, Pause, .. (forbid ? new[] { "fixture:trigger-ban" } : []),
                    .. (selfOnlyRound ? new[] { "fixture:trigger-round-self" } : [])]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:trigger-other-{seat}", "固定目标", "supporter", opponentSlash ? Opponent : "standard:none", "qun", opponentHp));
            builder.AddDeck(new("fixture:trigger-deck", "单一黑色实体", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(card, red ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "触发转化机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:trigger-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:trigger-owner", "fixture:trigger-other-1", "fixture:trigger-other-2", "fixture:trigger-other-3"]));
        }
    }
}
