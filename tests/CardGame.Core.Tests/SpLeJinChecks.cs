using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SpLeJinChecks
{
    private const string GeneralId = "sp:le-jin";
    private const string SkillId = "sp:xiaoguo";
    private const string RulesResource = "CardGame.Content.Standard.SkillPrograms.sp-le-jin.rules.json";
    private const string PresentationResource = "CardGame.Content.Standard.SkillPrograms.sp-le-jin.presentation.json";

    public static void DefinitionAndSchemaBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var general = current.Generals[GeneralId];
        var skill = current.Skills[SkillId];
        Require(general is { Name: "SP乐进", FactionId: "wei", BaseHp: 4, PortraitKey: "sp_le_jin" } &&
            general.SkillIds.SequenceEqual([SkillId]) &&
            current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
            current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
            skill.Program is { RuntimeVersion: SkillProgramCatalog.RuntimeVersion, MinimumRulesVersion: 172 } &&
            skill.Program.Triggers.Single().TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
            (int)SkillProgramConditionKind.HasOwnedCardCategory == 23,
            "SP Le Jin must register with its schema-56 observer trigger in the current identity roster.");
        var rules = Resource(RulesResource);
        var presentation = Resource(PresentationResource);
        Reject(rules.Replace("\"window\": \"turnEnding\"", "\"window\": \"afterNormalDraw\""),
            presentation, "turnEnding, playEnding or playPhaseStarting trigger");
        Reject(rules.Replace("\"kind\": \"hasOwnedCardCategory\", \"zones\": [\"hand\", \"equipment\"]",
                "\"kind\": \"hasOwnedCardCategory\", \"zones\": [\"judgment\"]"),
            presentation, "hand or equipment only");
    }

    public static void HumanOwnerPaysBasicAndReplaysDamage()
    {
        var (game, registry) = Create();
        ReachHumanPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Any(card =>
            Card(game, card.Id) is CardKind.Slash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol),
            "The recorded fixture must deal Le Jin a natural basic card for replay: " +
            string.Join(',', game.CreateSnapshot(0, true).Players[0].Hand.Select(card =>
                $"{card.Id}:{Card(game, card.Id)}")));
        Answer(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        ReachSkill(game);
        var activation = Prompt(game);
        Require(activation.PlayerSeat == 0 && activation.IsPrivate &&
            activation.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"),
            "Xiaoguo must offer its owner a private optional trigger on another player's ending phase.");
        var turnOwner = game.State.CurrentSeat;
        Require(turnOwner != 0, "Xiaoguo must not trigger on its own turn.");
        var atActivation = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerAction(game, "activate");
        Require(InternalPrompt(game) is { Kind: DecisionKind.ProgramTrigger },
            $"Activation did not pause on a target (turn={game.State.TurnNumber}, stack={string.Join(',', game.ResolutionStack.Select(f => f.Kind))}).");
        AnswerAction(atActivation, "activate");
        Require(game.ResolutionStack.OfType<ProgramSkillFrame>().Single().WindowContext?.TargetSeat == turnOwner,
            "The observer frame must bind the actual current turn owner as event target.");
        AnswerTarget(game, turnOwner);
        AnswerTarget(atActivation, turnOwner);
        var cost = Prompt(game);
        Require(cost.PlayerSeat == 0 && cost.Choices.Count > 0 &&
            cost.Choices.All(choice => choice.Cards.Count == 1 &&
                Card(game, choice.Cards[0]) is CardKind.Slash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol),
            "Only a real basic card in Le Jin's hand/equipment may pay the cost.");
        var atCost = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        AnswerChoice(game, cost.Choices[0]);
        AnswerChoice(atActivation, Prompt(atActivation).Choices[0]);
        AnswerChoice(atCost, Prompt(atCost).Choices[0]);
        Require(game.CardMovements.Any(move => move.Reason.Value ==
            $"skill-program.{SkillId}.SelectAndMoveOwnedCard" && move.From.OwnerSeat == 0 &&
            move.To == CardLocation.DiscardPile),
            "A selected basic card must physically reach discard before the target choice.");
        var option = InternalPrompt(game);
        Require(option is { PlayerSeat: > 0, IsPrivate: true } &&
            option.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "take-damage"),
            "The target must receive a private, real option decision after payment.");
        for (var i = 0; i < 24 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(); i++)
        {
            Advance(game); Advance(atCost);
        }
        Require(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any() &&
            game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Any(item => item.SourceSeat == 0 && item.TargetSeat == turnOwner && item.Amount == 1) &&
            game.CardMovements.All(move => move.Reason.Value != $"skill-program.{SkillId}.Draw") &&
            Snapshot(game) == Snapshot(atCost) && Events(game).SequenceEqual(Events(atCost)),
            "Xiaoguo target option and damage continuation must finish identically after checkpoint replay.");
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(bool mixedDeck = false,
        int seed = 17, bool allEquipment = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Scenario(mixedDeck, allEquipment));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 16
        }, registry);
        Answer(game, new StartGameCommand());
        Answer(game, new SelectGeneralCommand(0, GeneralId, game.Revision, game.PendingDecision!.PromptId));
        return (game, registry);
    }

    private static (GameEngine Game, ContentRegistry Registry) CreateObserverScenario(int seed,
        bool lethal = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new ObserverScenario(lethal));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, HumanSeat = 0, HumanRole = lethal ? Role.Rebel : Role.Lord, PlayerCount = 4,
            ModeId = ObserverScenario.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Answer(game, new StartGameCommand());
        for (var i = 0; i < 12 && game.PendingDecision?.PlayerSeat != 0; i++) Advance(game);
        var humanGeneral = game.PendingDecision?.ValidContentIds
            .FirstOrDefault(id => id is ObserverScenario.HumanGeneralId or ObserverScenario.AlternateHumanGeneralId);
        Require(game.PendingDecision?.PlayerSeat == 0 && humanGeneral is not null,
            $"Observer fixture did not offer the human general after earlier AI selections: seat={game.PendingDecision?.PlayerSeat}, kind={game.PendingDecision?.Kind}, candidates={string.Join(',', game.PendingDecision?.ValidContentIds ?? [])}.");
        Answer(game, new SelectGeneralCommand(0, humanGeneral!,
            game.Revision, game.PendingDecision!.PromptId));
        return (game, registry);
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var i = 0; i < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "SP Le Jin fixture did not reach Play.");
    }

    private static void ReachSkill(GameEngine game)
    {
        for (var i = 0; i < 600 && game.PendingDecision?.SkillPrompt?.SkillId != SkillId; i++) Advance(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            $"SP Le Jin did not reach an observed turn ending (turn={game.State.TurnNumber}, pending={game.PendingDecision?.Kind}).");
    }

    private static CardKind Card(GameEngine game, int cardId) =>
        game.CreateCardZoneDiagnostics().Single(item => item.CardId == cardId).CardKind;
    private static PendingDecision Prompt(GameEngine game) => game.PendingDecision is
        { Kind: DecisionKind.ProgramTrigger } prompt ? prompt :
        throw new InvalidOperationException($"Expected program prompt, found {game.PendingDecision?.Kind} at turn {game.State.TurnNumber}; stack={string.Join(',', game.ResolutionStack.Select(f => f.Kind))}.");
    private static PendingDecision? InternalPrompt(GameEngine game) =>
        (PendingDecision?)typeof(GameEngine).GetField("_pendingDecision",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game);
    private static void AnswerAction(GameEngine game, string action) => AnswerChoice(game,
        Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void AnswerTarget(GameEngine game, int seat) => AnswerChoice(game,
        Prompt(game).Choices.Single(choice => choice.Targets.SequenceEqual([seat])));
    private static void AnswerChoice(GameEngine game, PromptChoice choice) => Answer(game,
        new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId,
            choice.Id, game.Revision));
    private static void Answer(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "SP Le Jin command was rejected.");
    }
    private static void Advance(GameEngine game) => Answer(game, new AdvanceOneStepCommand(game.Revision));
    private static string Snapshot(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void Reject(string rules, string presentation, string fragment)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected schema rejection containing '{fragment}'.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool mixedDeck, bool allEquipment) : IGameContentPackage
    {
        public const string ModeId = "identity:sp-le-jin-check-4";
        public PackageManifest Manifest { get; } = new("sp-le-jin-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:sp-le-jin-target-{index}").ToArray();
            foreach (var other in others)
                builder.AddGeneral(new ContentGeneralDefinition(other, "骁果测试目标", "supporter",
                    "standard:none", "shu", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:sp-le-jin-deck", "骁果牌堆", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(allEquipment || mixedDeck && index % 2 == 0
                        ? "standard:crossbow" : "standard:slash",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "骁果四人身份测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, "fixture:sp-le-jin-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. others]));
        }
    }

    private sealed class ObserverScenario(bool lethal) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-sp-le-jin-observers-4";
        public const string HumanGeneralId = "fixture:sp-le-jin-human-target";
        public const string AlternateHumanGeneralId = "fixture:sp-le-jin-human-target-alt";
        public PackageManifest Manifest { get; } = new("sp-le-jin-observers", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(HumanGeneralId, "人类回合者", "supporter",
                "standard:none", "shu", BaseHp: lethal ? 1 : 4));
            builder.AddGeneral(new ContentGeneralDefinition(AlternateHumanGeneralId, "备选人类回合者", "supporter",
                "standard:none", "shu", BaseHp: lethal ? 1 : 4));
            var observers = Enumerable.Range(1, 3).Select(index => $"fixture:sp-le-jin-observer-{index}").ToArray();
            foreach (var observer in observers)
                builder.AddGeneral(new ContentGeneralDefinition(observer, "骁果观察者", "sp_le_jin",
                    SkillId, "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:sp-le-jin-observer-deck", "多观察者牌堆", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(!lethal && index % 2 == 0 ? "standard:crossbow" : "standard:slash",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "骁果多观察者身份测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, "fixture:sp-le-jin-observer-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [HumanGeneralId, AlternateHumanGeneralId, .. observers]));
        }
    }

    private sealed class GenericChoiceScenario(bool basic) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-generic-owned-category-4";
        public const string HumanGeneralId = "fixture:category-human";
        public const string SkillId = "fixture:owned-category-choice";
        private const string Rules = """
        {"schemaVersion":62,"skills":[{"id":"fixture:owned-category-choice","revision":1,
        "minimumRulesVersion": 171,"triggers":[{"id":"other-ending","window":"turnEnding",
        "subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[
        {"op":"selectTarget","target":"owner","targetKind":"eventTarget"},
        {"op":"chooseOption","target":"selectedTarget","resultBind":"choice","options":[
        {"id":"draw","condition":{"kind":"hasOwnedCardCategory","zones":["hand"],"cardCategories":["basic"]}},
        {"id":"pass","condition":{"kind":"always"}}]},
        {"op":"draw","target":"selectedTarget","amount":1,
        "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"draw"}}
        ]}]}]}
        """;
        private const string Presentation = """
        {"schemaVersion":3,"skills":{"fixture:owned-category-choice":{
        "name":"自有牌种选择","description":"通用条件测试",
        "optionLabels":{"draw":"有基本牌，摸一张","pass":"不摸牌"}}}}
        """;
        public PackageManifest Manifest { get; } = new("owned-category-choice-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "自有牌种选择", "通用条件测试")
            {
                Program = catalog.Programs[SkillId], ExecutionForms = SkillExecutionForm.Trigger
            });
            builder.AddGeneral(new ContentGeneralDefinition(HumanGeneralId, "通用图目标", "supporter",
                "standard:none", "shu", BaseHp: 4));
            var observers = Enumerable.Range(1, 3).Select(index => $"fixture:category-observer-{index}").ToArray();
            foreach (var observer in observers)
                builder.AddGeneral(new ContentGeneralDefinition(observer, "通用图观察者", "supporter",
                    SkillId, "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:owned-category-deck", "私密牌种牌堆", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(basic ? "standard:slash" : "standard:crossbow",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "通用牌种身份测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, "fixture:owned-category-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [HumanGeneralId, .. observers]));
        }
    }
}
