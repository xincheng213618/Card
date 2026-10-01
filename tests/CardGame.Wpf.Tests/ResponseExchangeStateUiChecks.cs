using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ResponseExchangeStateUiChecks
{
    public static void ActualEntityRestrictionAndUpgradeRestore(string output)
    {
        var (game, registry) = Create(ownerIsHuman: false);
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        int? restrictedId = null;
        for (var step = 0; step < 100; step++)
        {
            var snapshot = game.CreateSnapshot(0);
            restrictedId = snapshot.TurnProhibitedPhysicalCards?.Where(item => item.RecipientSeat == 0)
                .SelectMany(item => item.CardIds).FirstOrDefault(id => snapshot.Players[0].Hand.Any(card => card.Id == id));
            if (restrictedId is > 0) break;
            var prompt = snapshot.PendingDecision;
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.Nullification } && prompt.Choices.FirstOrDefault(choice => choice.Cards.Count > 0) is { } response)
                Answer(game, response);
            else Drive(game);
        }
        Program.Assert(restrictedId is > 0, "The actual native AI trick response must give a restricted entity to the human. " +
            string.Join("; ", game.Events.Select(item => item.Payload).OfType<ProgramResponseEntityClaimedEvent>().Select(claim =>
                $"claim {claim.OwnerSeat}->{claim.RecipientSeat}, restricted={claim.Restricted}, ids={string.Join(',', claim.CardIds)}")) +
            " actions=" + string.Join(';', game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Select(item =>
                $"{item.Action.ActionId}:{item.Action.ActorSeat}/{item.Action.Type}/{item.Action.EffectiveKind}/parent={item.Action.ParentActionId}")));
        using (var model = Restore(game, registry))
        {
            Program.Assert(model.Hand.Single(card => card.Id == restrictedId).AvailabilityText.Contains("本回合不能使用或打出") &&
                           model.Hand.Where(card => card.Id != restrictedId).All(card => !card.AvailabilityText.Contains("本回合不能使用或打出")),
                "Restored availability annotates the acquired physical entity and leaves other cards' messages unchanged.");
            Render(model, output, "243-public-physical-response-restriction.png");
        }
        var (upgraded, upgradedRegistry) = Create(ownerIsHuman: true);
        for (var turn = 0; turn < 4 && upgraded.CreateSnapshot(0).ProgramResponseExchangeStates?.Any(state => state.OwnerSeat == 0 && state.IsUpgraded) != true; turn++)
        {
            Reach(upgraded, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
            Accept(upgraded, new EndPlayPhaseCommand(0, upgraded.Revision, upgraded.PendingDecision!.PromptId));
            Reach(upgraded, prompt => prompt.PlayerSeat == 0 && prompt.SkillPrompt?.SkillId == "classic:jiexun");
            Answer(upgraded, upgraded.CreateSnapshot(0).PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(upgraded, upgraded.CreateSnapshot(0).PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual([1])));
            Reach(upgraded, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
        }
        Program.Assert(upgraded.CreateSnapshot(0).ProgramResponseExchangeStates?.Single(state => state.OwnerSeat == 0).IsUpgraded == true,
            "The UI upgrade must follow actual exhausting discards rather than a synthetic projection.");
        using var upgradedModel = Restore(upgraded, upgradedRegistry);
        var stateCard = upgradedModel.HumanSkillCards.Single(skill => skill.ContentId == "classic:funan");
        Program.Assert(stateCard.StateText.Contains("无需交出原牌，仍获得响应牌") && stateCard.Tooltip.Contains(stateCard.StateText) &&
                       upgradedModel.HumanSkillCards.All(skill => skill.ContentId != "classic:jiexun"),
            "Restored acquired skill state shows the real upgrade while the lost source skill disappears.");
        Render(upgradedModel, output, "244-public-response-exchange-upgrade.png");
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }
    private static void Render(MainViewModel model, string output, string filename)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, filename));
        window.Content = null;
        window.Close();
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 100; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Drive(game);
        }
        throw new InvalidOperationException("The verified public exchange fixture did not reach its actual rule boundary.");
    }
    private static void Drive(GameEngine game)
    {
        var prompt = game.CreateSnapshot(0).PendingDecision;
        if (prompt is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger })
            Answer(game, prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"));
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game,
        new AnswerPromptCommand(0, game.CreateSnapshot(0).PendingDecision!.PromptId, choice.Id, game.Revision));
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted public exchange command.");
    }
    private static (GameEngine Game, ContentRegistry Registry) Create(bool ownerIsHuman)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(ownerIsHuman));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-ui-response-exchange", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, ownerIsHuman ? "fixture:xue-owner" : "fixture:xue-target-1", game.Revision, game.PendingDecision!.PromptId));
        return (game, registry);
    }
    private sealed class Fixture(bool ownerIsHuman) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-response-exchange", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"fixture:ui-exchange-driver","revision":1,"viewAs":[{"id":"draw","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true}],"triggers":[{"id":"skipdraw","window":"drawPhaseStarting","drawPhaseMode":"replacement","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":0}]}]},
                {"id":"fixture:ui-exchange-response","revision":1,"viewAs":[{"id":"counter","inputKinds":[],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true,"extendedUse":true}],"triggers":[{"id":"skipdraw","window":"drawPhaseStarting","drawPhaseMode":"replacement","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":0}]}]},
                {"id":"fixture:ui-exchange-skip","revision":1,"triggers":[{"id":"skipdraw","window":"drawPhaseStarting","drawPhaseMode":"replacement","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":0}]}]},
                {"id":"fixture:ui-exchange-observer","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","movementReasons":["skill-program.response-entity-exchange"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observe","options":[{"id":"continue"}]}],"destinationZones":["hand"]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-exchange-driver":{"name":"状态驱动","description":"实际用牌"},"fixture:ui-exchange-response":{"name":"响应转换","description":"实际无懈"},"fixture:ui-exchange-skip":{"name":"固定摸牌","description":"阶段替代"},"fixture:ui-exchange-observer":{"name":"获牌观察","description":"恢复暂停","optionLabels":{"continue":"继续"}}}}""");
            foreach (var program in catalog.Programs) builder.AddSkill(new(program.Key, program.Key, "测试") { Program = program.Value });
            builder.AddGeneral(new("fixture:xue-owner", "薛综", "xue_zong", "classic:funan", "wu", 4,
                ownerIsHuman ? ["classic:jiexun", "fixture:ui-exchange-driver", "fixture:ui-exchange-response", "fixture:ui-exchange-observer"]
                    : ["classic:jiexun", "fixture:ui-exchange-driver", "fixture:ui-exchange-observer"]));
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:xue-target-{index}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "响应者", "supporter",
                ownerIsHuman || id == "fixture:xue-target-1" ? "fixture:ui-exchange-response" : "fixture:ui-exchange-skip", "shu", 4,
                ["fixture:ui-exchange-observer"]));
            builder.AddDeck(new("fixture:ui-exchange-deck", "固定实体牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 100).Select(index => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-ui-response-exchange", "公开响应状态", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-exchange-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xue-owner", .. others]));
        }
    }
}
