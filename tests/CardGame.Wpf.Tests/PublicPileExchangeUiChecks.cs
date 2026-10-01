using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicPileExchangeUiChecks
{
    public static void MultipleSourcesRestoreSeparatePublicPiles(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(true));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-ui-pile-exchange", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:pile-ui-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.ArrowBarrage);
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zhengrong");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "public-pile-flow"));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:bizhuan");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var piles = game.CreateSnapshot(0).Players[0].PublicPersistentPiles!;
        var book = piles.Single(pile => pile.SourceSkillId == "classic:bizhuan");
        var honor = piles.Single(pile => pile.SourceSkillId == "classic:zhengrong");
        using var model = Restore(game, registry);
        var rendered = model.DeferredPublicPiles.Where(pile => pile.OwnerSeat == 0).ToArray();
        Program.Assert(rendered.Length == 2 && rendered.Single(pile => pile.Title.Contains("书")).Cards.Single().Id == book.Cards.Single().Id &&
            rendered.Single(pile => pile.Title.Contains("荣")).Cards.Single().Id == honor.Cards.Single().Id,
            "Two real source piles restore as separately named panels with their own physical faces.");
        Program.Assert(model.Seats.Single(seat => seat.Seat == 0).DeferredPileText.Contains("书 ×1") &&
            model.Seats.Single(seat => seat.Seat == 0).DeferredPileText.Contains("荣 ×1"),
            "The generic public badge shows both exact source counts.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "253-two-source-public-piles.png"));
        Program.Assert(Program.Find<TextBlock>(root).Any(block => block.Text.Contains("书") && block.ActualWidth > 0) &&
            Program.Find<TextBlock>(root).Any(block => block.Text.Contains("荣") && block.ActualWidth > 0),
            "Both public names render through existing shared native controls.");
        window.Content = null;
        window.Close();

        game = Program.Engine(model);
        Accept(game, new UseProgramSkillCommand(0, "fixture:pile-ui-supply", "lose-honor", [], [], game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        using var remaining = Restore(game, registry);
        Program.Assert(remaining.DeferredPublicPiles.Where(pile => pile.OwnerSeat == 0).Single().Cards.Single().Id == book.Cards.Single().Id &&
            remaining.Seats.Single(seat => seat.Seat == 0).DeferredPileText.Contains("书 ×1") &&
            !remaining.Seats.Single(seat => seat.Seat == 0).DeferredPileText.Contains("荣 ×1") &&
            Program.Engine(remaining).CreateCardZoneDiagnostics().Single(card => card.CardId == honor.Cards.Single().Id).Location == CardLocation.DiscardPile,
            "Actual honor source loss clears only honor and restores the surviving book through the legacy single-pile view.");
    }

    public static void OptionalHandCountAndPublicFacesRestore(string output)
    {
        foreach (var count in new[] { 0, 2 })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
                ModeId = "identity:classic-ui-pile-exchange", UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            Accept(game, new StartGameCommand());
            Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
            Accept(game, new SelectGeneralCommand(0, "fixture:pile-ui-owner", game.Revision, game.PendingDecision!.PromptId));
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            for (var index = 0; index < 3; index++)
            {
                Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
                var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.ArrowBarrage);
                Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId));
                Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zhengrong");
                Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "public-pile-flow"));
                Answer(game, choice => choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            }
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            Accept(game, new UseProgramSkillCommand(0, "fixture:pile-ui-supply", "draw", [], [], game.Revision,
                game.PendingDecision!.PromptId));
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
            var original = game.CreateSnapshot(0).Players[0].PublicPersistentPileCards!.Select(card => card.Id).ToArray();
            var initialMaxHp = game.CreateSnapshot(0).Players[0].MaxHp;
            Program.Assert(original.Length == 3, "Three genuine uses created exactly three public pile entities.");
            Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
            Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("stage") == "owned"));

            using var model = Restore(game, registry);
            Program.Assert(model.DeferredPublicPiles.Single(pile => pile.OwnerSeat == 0).Cards
                    .Select(card => card.Id).ToHashSet().SetEquals(original) &&
                Enumerable.Range(1, 3).All(viewer => Program.Engine(model).CreateSnapshot(viewer).PendingDecision is null),
                "Private hand payment coexists with the real public pile without exposing the private prompt.");
            var entering = new List<int>();
            for (var index = 0; index < count; index++)
            {
                var choice = model.SkillChoices.First(candidate => candidate.Cards.Count == 1);
                entering.Add(choice.Cards.Single());
                model.SelectSkillChoiceCommand.Execute(choice);
            }
            var finish = model.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("token") == "finish");
            ClickChoice(model, finish, output, $"251-pile-exchange-finish-{count}.png");
            if (count > 0)
            {
                Program.Assert(model.PublicRevealedCards.Select(card => card.Id).ToHashSet().SetEquals(original) &&
                    model.PublicRevealedCards.All(card => model.SelectRevealedCardCommand.CanExecute(card)),
                    "Equal outgoing public costs map to the actual shared clickable faces.");
                using var resumed = Restore(Program.Engine(model), registry);
                var selected = resumed.PublicRevealedCards.First();
                ClickFace(resumed, selected, output, "252-pile-exchange-public-order.png");
                Program.Assert(!resumed.SelectRevealedCardCommand.CanExecute(selected),
                    "One selected public entity cannot be chosen again after the restored command.");
                using var final = Restore(Program.Engine(resumed), registry);
                final.SelectRevealedCardCommand.Execute(final.PublicRevealedCards.First(card =>
                    final.SelectRevealedCardCommand.CanExecute(card)));
                Reach(final, prompt => prompt.Kind == DecisionKind.PlayCard);
                var owner = Program.Engine(final).CreateSnapshot(0).Players[0];
                Program.Assert(owner.MaxHp == initialMaxHp - 1 && owner.PublicPersistentPileCount == 3 &&
                    entering.All(id => owner.PublicPersistentPileCards!.Any(card => card.Id == id)),
                    "Exact equal-side exchange survives both UI restores before the awakening completes.");
            }
            else
            {
                Reach(model, prompt => prompt.Kind == DecisionKind.PlayCard);
                var owner = Program.Engine(model).CreateSnapshot(0).Players[0];
                Program.Assert(owner.MaxHp == initialMaxHp - 1 && owner.PublicPersistentPileCards!.Select(card => card.Id).ToHashSet().SetEquals(original),
                    $"The zero-count native button completes awakening without moving a pile entity. Initial max {initialMaxHp}, actual max {owner.MaxHp}, pile {owner.PublicPersistentPileCount}.");
            }
        }
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void ClickChoice(MainViewModel model, PromptChoice choice, string output, string filename)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, filename));
        var button = Program.Find<Button>(root).First(candidate => candidate.Command == model.SelectSkillChoiceCommand &&
            candidate.CommandParameter is PromptChoice parameter && parameter.Id == choice.Id && candidate.ActualWidth > 0);
        Program.Assert(button.IsEnabled, "The optional-count finish command must be reachable as a real rendered button.");
        button.Command!.Execute(button.CommandParameter);
        window.Content = null;
        window.Close();
    }

    private static void ClickFace(MainViewModel model, CardViewModel card, string output, string filename)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, filename));
        var panel = Program.Find<ItemsControl>(root).Single(control => control.Name == "PublicRevealedCards");
        var button = Program.Find<Button>(panel).Single(candidate => candidate.CommandParameter is CardViewModel face && face.Id == card.Id);
        Program.Assert(button.IsEnabled && button.ActualWidth > 0, "The exact restored public face must have a native enabled control.");
        button.Command!.Execute(button.CommandParameter);
        window.Content = null;
        window.Close();
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted UI fixture command.");
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision));
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 640; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed public-pile UI fixture did not reach its real boundary.");
    }

    private static void Reach(MainViewModel model, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 80; step++)
        {
            if (Program.Engine(model).CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Program.Assert(model.CanStepAi, "The restored public-pile parent must remain resumable.");
            model.StepAiCommand.Execute(null);
        }
        throw new InvalidOperationException("The restored public-pile UI did not reach its real boundary.");
    }

    private sealed class Fixture(bool multiple = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pile-ui", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:pile-ui-supply","revision":1,
                "activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                "effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"lose-honor","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                "effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:zhengrong"],"sourceBind":"standard:none"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:pile-ui-supply":{"name":"补牌","description":"固定真实手牌"}}}""");
            builder.AddSkill(new("fixture:pile-ui-supply", "补牌", "固定真实手牌") { Program = catalog.Programs["fixture:pile-ui-supply"] });
            builder.AddGeneral(new("fixture:pile-ui-owner", "公开交换者", "supporter", "classic:zhengrong", "wei", 20,
                multiple ? ["classic:bizhuan", "fixture:pile-ui-supply"] : ["classic:hongju", "fixture:pile-ui-supply"]));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:pile-ui-target-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "旁观者", "supporter", "standard:none", "wu", 20));
            builder.AddDeck(new("fixture:pile-ui-deck", "固定公开交换实体", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 100).Select(index =>
                    new ContentDeckPhysicalCard("standard:arrow_barrage", multiple ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-ui-pile-exchange", "公开牌堆交换", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:pile-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:pile-ui-owner", .. others]));
        }
    }
}
