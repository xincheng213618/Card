using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class QuotaTopUiChecks
{
    public static void NativePrivateViewAndOrder(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-ui-quota-top", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 6
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "classic:zhuge-zhan", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zuilun" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        using var model = Restore(game, registry);
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Reach(model, prompt => prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "quota-top-gain"));
        game = Program.Engine(model);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        var draft = frame.QuotaTop!;
        var viewed = draft.ViewedIds.ToArray();
        Program.Assert(draft is { Quota: 1, Stage: "gain" } && viewed.Length == 3 &&
            model.PrivatelyViewedCards.Select(card => card.Id).ToHashSet().SetEquals(viewed) &&
            model.PrivatelyViewedCards.All(card => card.IsPrivateReveal && !card.IsPlayable) &&
            model.SkillChoices.Count == 3 && model.SkillChoices.All(choice => choice.Cards.Count == 1) &&
            model.PrivateRevealTitle.Contains("仅你可见") &&
            Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).PrivateRevealedCards is null &&
                game.CreateSnapshot(viewer).PendingDecision is null),
            "The real native private quota offers exact faces and choices only to its human viewer.");
        Render(model, output, "247-private-top-quota-gain.png");

        using var restored = Restore(game, registry);
        game = Program.Engine(restored);
        var beforeMoves = game.CardMovements.Count;
        restored.SelectSkillChoiceCommand.Execute(restored.SkillChoices.Single(choice => choice.Cards.Contains(viewed[1])));
        Reach(restored, prompt => prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "quota-top-order"));
        game = Program.Engine(restored);
        var obtained = game.CardMovements.Skip(beforeMoves)
            .Where(move => move.Reason.Value == "program.quota-top.obtain").ToArray();
        Program.Assert(obtained is [{ CardId: var obtainedId }] && obtainedId == viewed[1] &&
            obtained[0].From == CardLocation.DrawPile && obtained[0].To == CardLocation.Hand(0) &&
            restored.SkillChoices.SelectMany(choice => choice.Cards).ToHashSet().SetEquals([viewed[0], viewed[2]]) &&
            restored.PrivatelyViewedCards.All(card => card.Id != viewed[1]),
            "An actual shared UI gain moves its one entity exactly once before offering only remaining top entities.");
        restored.SelectSkillChoiceCommand.Execute(restored.SkillChoices.Single(choice => choice.Cards.Contains(viewed[2])));
        game = Program.Engine(restored);
        Program.Assert(game.ResolutionStack.OfType<ProgramSkillFrame>().Single(child => child.Id == frame.Id)
            .QuotaTop?.OrderedTopIds.SequenceEqual([viewed[2]]) == true,
            "The first top-order click commits only the actual draft prefix.");

        using var partial = Restore(game, registry);
        game = Program.Engine(partial);
        Program.Assert(partial.SkillChoices is [{ Cards: var cards }] && cards.SequenceEqual([viewed[0]]),
            "A restored order draft offers its exact last entity without reoffering the selected prefix.");
        Render(partial, output, "248-private-top-quota-restored-order.png");
        partial.SelectSkillChoiceCommand.Execute(partial.SkillChoices[0]);
        for (var step = 0; step < 32 && Program.Engine(partial).ResolutionStack.OfType<ProgramSkillFrame>()
            .Any(child => child.Id == frame.Id); step++)
        {
            Program.Assert(partial.CanStepAi, "The completed shared order must resume its original parent.");
            partial.StepAiCommand.Execute(null);
        }
        game = Program.Engine(partial);
        Program.Assert(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(child => child.Id == frame.Id) &&
            !partial.HasPrivatelyViewedCards && game.CardMovements.Skip(beforeMoves)
                .Count(move => move.Reason.Value == "program.quota-top.obtain" && move.CardId == viewed[1]) == 1,
            "Completion clears the private panel and preserves the one real acquisition after restoration.");
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 96; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed native quota fixture did not reach its UI boundary.");
    }

    private static void Reach(MainViewModel model, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 96; step++)
        {
            if (Program.Engine(model).CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Program.Assert(model.CanStepAi, "The real private quota parent must remain resumable.");
            model.StepAiCommand.Execute(null);
        }
        throw new InvalidOperationException("The shared quota controls did not reach the expected choice.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted native quota command.");
    }

    private static void Render(MainViewModel model, string output, string file)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, file));
        Program.Assert(Program.Find<TextBlock>(root).Any(text => text.Text.Contains("仅你可见") &&
            text.Visibility == Visibility.Visible && text.ActualWidth > 0 && text.ActualHeight > 0),
            "The native private panel renders its privacy label.");
        window.Content = null;
        window.Close();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-top-quota", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:quota-target-{seat}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new(id, "目标", "supporter", "classic:fuyin", "shu", 20,
                    AdditionalSkillIds: ["classic:zuilun"]));
            builder.AddDeck(new("fixture:quota-ui-deck", "固定实体", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 100).Select(index =>
                new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-ui-quota-top", "私看选择", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:quota-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["classic:zhuge-zhan", ..others]));
        }
    }
}
