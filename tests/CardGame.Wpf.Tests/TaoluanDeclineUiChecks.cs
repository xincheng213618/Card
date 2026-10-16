using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class TaoluanDeclineUiChecks
{
    public static void BottomDecline(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, Fixture.Provider, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 256; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "different-action-category-decline") == true)
                break;
            if (prompt?.Kind == DecisionKind.PlayCard)
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
            else if (prompt is not null)
            {
                var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ?? prompt.Choices[0];
                Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)));
            }
            else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        Assert(game.PendingDecision?.SkillPrompt?.SkillId == "classic:taoluan" && game.PendingDecision.Choices.Any(choice => choice.Cards.Count == 1),
            "The fixed native Taoluan fixture must reach a human-controlled gift with legal cards.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 17, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(model);
        try
        {
            foreach (var select in new[] { false, true })
            {
                model.LoadManualGameCommand.Execute(null);
                game = Engine(model);
                var prompt = game.CreateSnapshot(0).PendingDecision!;
                var decline = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "different-action-category-decline");
                var boundary = GameCheckpointJson.Serialize(game.CreateCheckpoint());
                if (select)
                {
                    model.SelectCardCommand.Execute(model.Hand.First(card => card.IsPlayable));
                    Assert(model.HasSelection && model.CanConfirmSelected, "A legal gift must still be selectable before declining.");
                    model.ClearSelectionCommand.Execute(null);
                    Assert(!model.HasSelection && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == boundary,
                        "Clearing selected cards must preserve the pending gift decision.");
                    model.SelectCardCommand.Execute(model.Hand.First(card => card.IsPlayable));
                }
                var root = (FrameworkElement)window.Content;
                Render(root, 1120, 740, Path.Combine(output, $"taoluan-decline-{select}.png"));
                var cancel = (Button)window.FindName("CancelActionButton");
                Assert(cancel.IsEnabled && Equals(cancel.Content, "放 弃") && model.CanCancelAction &&
                    model.HandResponseExtraChoices.All(choice => choice.Parameters.GetValueOrDefault("program-action") != "different-action-category-decline"),
                    "Taoluan must expose its exact decline through the enabled bottom button without duplicating it in the center.");
                var moves = game.CardMovements.Count;
                var revision = game.Revision;
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cancel, null);
                Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.PromptId == prompt.PromptId && answer.Choice == decline.Id && !model.HasSelection &&
                    game.CardMovements.Count == moves && game.Events.Select(item => item.Payload).OfType<ProgramActionCategoryGiftResolvedEvent>().Last().GiftCardId is null,
                    "Bottom decline must submit the current published refusal once and retain every locally selected gift card.");
                var after = game.Revision;
                model.CancelActionCommand.Execute(null);
                Assert(game.Revision == after, "A repeated cancellation must not answer the retired gift prompt.");
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "Rejected Taoluan fixture command.");

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:classic-taoluan-decline-ui";
        internal const string Provider = "fixture:taoluan-gift-provider";
        public PackageManifest Manifest { get; } = new("fixture:taoluan-decline-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Provider, "交牌者", "supporter", "standard:none", "wei", 8));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:taoluan-owner-{seat}", "滔乱者", "supporter", "classic:taoluan", "qun", 8));
            builder.AddDeck(new("fixture:taoluan-gift-deck", "固定滔乱交牌牌库", 4, 2, [new("standard:crossbow", 96)]));
            builder.AddMode(new(Mode, "滔乱交牌界面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:taoluan-gift-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Provider, "fixture:taoluan-owner-1", "fixture:taoluan-owner-2", "fixture:taoluan-owner-3"]));
        }
    }
}
