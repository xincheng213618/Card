using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramTargetOrderChecks
{
    public static void Run()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture());
        // Seed 7 deals exactly the weapon and a Dodge to the lord in this fixed deck.
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
            ModeId = "identity:classic-target-order-5", UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture did not start.");
        var hand = game.CreateSnapshot(0, true).Players[0].Hand;
        Require(hand.Count == 2 && hand.Any(card => card.Kind == CardKind.Dodge), "Fixed fixture hand changed.");
        var weapon = hand.Single(card => card.Kind == CardKind.FangtianHalberd);
        Require(game.Submit(new PlayCardCommand(0, weapon.Id, [], game.Revision,
            game.PendingDecision!.PromptId)).Accepted, "Fixture failed to equip.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "Fixture did not resume play.");
        var selected = game.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.SequenceEqual([1, 3]));
        Require(game.Submit(new PlayCardCommand(0, selected.CardId!.Value, selected.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, selected.PlayedCardKind)
            { ConversionSource = selected.ConversionSource }).Accepted, "Converted multi-target Slash was rejected.");
        for (var step = 0; step < 10 && game.PendingDecision?.Kind != DecisionKind.ProgramTrigger; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Target prepass did not advance.");
        Require(game.PendingDecision?.Kind == DecisionKind.ProgramTrigger, "Final targets did not open a program window.");
        var redirects = game.Events.Select(item => item.Payload).OfType<LiuliRedirectedEvent>().ToArray();
        var use = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.Type == CardActionType.Use).Action;
        Require(redirects.Length == 2 && use.TargetSeats.SequenceEqual([2, 4]),
            "Both Liuli choices must finish before triggers; redirected targets must stay distinct.");
        Require(!game.Events.Any(item => item.Payload is CardRespondedEvent),
            "No target may respond before all conversion-use triggers finish.");
        var paused = game.CreateCheckpoint();
        game = GameReplay.Restore(paused, registry);
        var seenTargets = new List<int>();
        while (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger } prompt)
        {
            seenTargets.Add(prompt.TargetSeat!.Value);
            var skip = prompt.Choices.Single(choice => choice.Parameters["program-action"] == "skip");
            Require(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, skip.Id, game.Revision)).Accepted,
                "Target trigger could not be skipped.");
        }
        Require(seenTargets.SequenceEqual([2, 4]), "Triggers must bind only to the final opponents in target order.");
        for (var step = 0; step < 50 && game.ResolutionStack.Count > 0; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Prepared attack did not finish.");
        Require(game.ResolutionStack.Count == 0 &&
                game.Events.Select(item => item.Payload).OfType<LiuliRedirectedEvent>().Count() == 2,
            "Prepared targets must not run Liuli again during effect resolution.");
        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(game.State) == SnapshotJson.Serialize(replay.State),
            "Multi-target trigger and Liuli continuation did not replay exactly.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("target-order-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 30, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load("""
                {"schemaVersion":58,"skills":[
                  {"id":"target-order:swap","revision":1,"viewAs":[{"id":"slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]},
                  {"id":"target-order:draw","revision":1,"minimumRulesVersion":168,"triggers":[{"id":"after-use","window":"cardUseTargetsFinalized","ownerRelation":"conversionSource","sourceSkillId":"target-order:swap","sourceViewAsId":"slash","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
                ]}
                """, """
                {"schemaVersion":3,"skills":{
                  "target-order:swap":{"name":"转换","description":"测试转换"},
                  "target-order:draw":{"name":"摸牌","description":"测试触发"}
                }}
                """);
            foreach (var program in catalog.Programs.Values)
            {
                var text = catalog.Presentations[program.Id];
                builder.AddSkill(new ContentSkillDefinition(program.Id, text.Name, text.Description) { Program = program });
            }
            builder.AddSkill(new ContentSkillDefinition("target-order:liuli", "流离", "测试流离", SkillKind.Liuli));
            var generals = Enumerable.Range(0, 5).Select(index => $"target-order:general-{index}").ToArray();
            foreach (var id in generals)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试", "zhao_yun", "target-order:swap",
                    AdditionalSkillIds: ["target-order:draw", "target-order:liuli"]));
            builder.AddDeck(new ContentDeckRecipe("target-order:deck", "测试", 2, 0,
                [new ContentDeckCardCount("classic:fangtian-halberd", 8),
                 new ContentDeckCardCount("standard:dodge", 24), new ContentDeckCardCount("standard:peach", 28)]));
            builder.AddMode(new ContentModeDefinition("identity:classic-target-order-5", "测试", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                DeckId: "target-order:deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
