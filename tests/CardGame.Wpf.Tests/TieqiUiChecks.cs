using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TieqiUiChecks
{
    private const string Mode = "fixture:tieqi-cancel", Owner = "fixture:tieqi-target";

    public static void CancelDeclinesSuitDiscard(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Package());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "boundary:tieqi" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        var window = new MainWindow(model);
        try
        {
            foreach (var selectCard in new[] { false, true })
            {
                model.LoadManualGameCommand.Execute(null);
                Program.Assert(!model.HasSaveError, model.SaveStatus);
                game = Program.Engine(model);
                var prompt = game.PendingDecision!;
                var decline = prompt.Choices.Single(choice => choice.Cards.Count == 0);
                var hand = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).ToArray();
                var movements = game.CardMovements.Count;
                Program.Assert(model.IsCardPaymentSelectionPending && model.CanCancelAction &&
                    model.CancelActionCommand.CanExecute(null) && model.HandResponseExtraChoices.Count == 0,
                    "Tieqi must expose its published decline on the bottom cancel button before any card is selected.");
                if (selectCard)
                {
                    model.SelectCardCommand.Execute(model.Hand.First(card => card.IsPlayable));
                    Program.Assert(model.HasSelection && model.CanConfirmSelected,
                        "A real matching-suit card must remain available for explicit discard confirmation.");
                }
                Program.Render((FrameworkElement)window.Content, 1120, 740,
                    Path.Combine(output, $"tieqi-cancel-selected-{selectCard}.png"));
                var cancel = (Button)window.FindName("CancelActionButton");
                Program.Assert(cancel.Visibility == Visibility.Visible && cancel.IsEnabled &&
                    ReferenceEquals(cancel.Command, model.CancelActionCommand),
                    "The actual Tieqi cancel control must stay enabled with and without selected payment cards.");
                cancel.Command!.Execute(cancel.CommandParameter);
                Program.Assert(game.Revision == prompt.Revision + 1 &&
                    game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.PromptId == prompt.PromptId && answer.Choice == decline.Id && !model.HasSelection &&
                    game.CardMovements.Count == movements &&
                    game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).SequenceEqual(hand) &&
                    game.Events.Select(item => item.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>()
                        .Last() is { PaymentCompleted: false, PaidCardId: null },
                    "Cancel must submit the exact published decline once, keep the hand and never discard a selected card.");
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> goal)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt && goal(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"Tieqi UI fixture did not reach its expected boundary: {game.PendingDecision?.Kind}.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Tieqi fixture command rejected.");
    }

    private sealed class Package : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-tieqi-cancel", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:ma-chao", "boundary:ma-chao");
            builder.AddGeneral(new(Owner, "铁骑响应者", "supporter", "standard:none", "wei", 4));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:tieqi-attacker-{seat}").ToArray();
            foreach (var peer in peers)
                builder.AddGeneral(new(peer, "铁骑攻击者", "supporter", "boundary:tieqi", "qun", 4));
            builder.AddDeck(new("fixture:tieqi-cancel-deck", "铁骑实体牌库", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(1, 80).Select(id => new ContentDeckPhysicalCard(
                    new[] { 21, 37, 61, 80 }.Contains(id) ? "standard:slash" : "standard:dodge",
                    Suit.Diamond, id % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "铁骑取消响应", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:tieqi-cancel-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
