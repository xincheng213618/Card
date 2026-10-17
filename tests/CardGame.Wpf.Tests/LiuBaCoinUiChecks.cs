using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class LiuBaCoinUiChecks
{
    public static void RealMintPrivateBottomExchangeAndReversibleChoice()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = CreateEnding(registry);
        var checkpoint = game.CreateCheckpoint();
        VerifyWindow(checkpoint, registry, exchange: false);
        VerifyWindow(checkpoint, registry, exchange: true);
    }

    private static void VerifyWindow(GameCheckpoint checkpoint, ContentRegistry registry, bool exchange)
    {
        using var model = Restore(checkpoint, registry);
        var game = Program.Engine(model);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LiuBa is { Stage: LiuBaStage.Coins });
        var coins = game.Events.Select(e => e.Payload).OfType<LiuBaCoinMintedEvent>().Select(e => e.CardId).ToArray();
        var bottom = Deck(game).Take(5).ToArray();
        Program.Assert(coins.Length == 2 && bottom.Length == 5 && frame.OwnerSeat == 0 &&
            frame.LiuBa!.ViewedBottom.SequenceEqual(bottom) && model.IsHandResponsePending &&
            model.PrivatelyViewedCards.Select(c => c.Id).SequenceEqual(bottom) &&
            model.PrivatelyViewedCards.All(c => c.IsPrivateReveal) &&
            coins.All(id => model.Hand.Single(c => c.Id == id).OriginalHandTag.Contains("币", StringComparison.Ordinal)),
            "Two actual recast rewards must retain their coin marks and expose the true bottom five only to their current holder.");
        foreach (var seat in Enumerable.Range(1, 3))
        {
            var view = game.CreateSnapshot(seat);
            var visible = view.Players.SelectMany(p => p.Hand.Concat(p.Equipment).Concat(p.Judgment))
                .Concat(view.PublicRevealedCards).Concat(view.PrivateRevealedCards ?? []);
            Program.Assert(view.PendingDecision is null && view.PrivateRevealedCards is null &&
                !visible.Any(c => bottom.Contains(c.Id)),
                "Another viewer must receive neither the private bottom entities nor the holder's exchange choices.");
        }

        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Layout(root);
            var card = model.Hand.Single(c => c.Id == coins[0]);
            var handButton = BoundButton(root, model.SelectCardCommand, card);
            Program.Assert(Program.Find<TextBlock>(handButton).Any(t => t.Visibility == Visibility.Visible &&
                    t.Text.Contains("币", StringComparison.Ordinal)) &&
                Program.Find<Button>((ItemsControl)window.FindName("PrivatelyViewedCards")).Count() == 5,
                "The real hand control must still render the coin badge beside the five authorized private card faces.");
            var commands = game.AcceptedCommands.Count;
            var revision = game.Revision;
            var movements = game.CardMovements.Count;
            var originalDeck = Deck(game);
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var coinChoice = prompt.Choices.Single(c => Branch(c, "coin") && c.Cards.SequenceEqual([coins[0]]));
            Click(handButton);
            Program.Assert(card.IsSelected && model.CanConfirmSelected && game.Revision == revision,
                "Selecting a marked physical coin must remain a local draft until confirmation.");
            Click((Button)window.FindName("CancelActionButton"));
            Program.Assert(!model.HasSelection && !model.CanConfirmSelected && game.Revision == revision &&
                game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements,
                "Returning from the local coin draft must neither answer the native prompt nor pay a coin.");

            if (!exchange)
            {
                var decline = model.HandResponseExtraChoices.Single(c => Branch(c, "done"));
                Click(BoundButton(root, model.SelectHandResponseChoiceCommand, decline));
                AssertAnswer(game, commands, prompt.PromptId, decline.Id);
                Program.Assert(Deck(game).SequenceEqual(originalDeck) && game.CardMovements.Count == movements &&
                    coins.All(id => game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id)) &&
                    !game.Events.Any(e => e.Payload is LiuBaEntityMovedEvent m && m.FrameId == frame.Id),
                    "The published zero-exchange choice must retire the private window without moving either coins or deck cards.");
                return;
            }

            Click(handButton);
            Click((Button)window.FindName("PlayCardButton"));
            AssertAnswer(game, commands, prompt.PromptId, coinChoice.Id);
            Program.Assert(game.CardMovements.Count == movements &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frame.Id).LiuBa!.Coins.SequenceEqual([coins[0]]),
                "Confirming a coin must submit its exact published entity choice while deferring the physical exchange.");
            Layout(root);
            var done = model.HandResponseExtraChoices.Single(c => Branch(c, "done"));
            Click(BoundButton(root, model.SelectHandResponseChoiceCommand, done));
            Layout(root);
            var bottomPrompt = game.CreateSnapshot(0).PendingDecision!;
            var choice = model.SkillChoices.Single(c => Branch(c, "bottom") && c.Cards.SequenceEqual([bottom[3]]));
            var button = BoundButton((ItemsControl)window.FindName("ScrollingSkillChoices"), model.SelectSkillChoiceCommand, choice);
            var face = Program.Find<ContentControl>(button).Select(c => c.Content).OfType<PromptCardChoicePresentation>()
                .SingleOrDefault(p => p.Face?.Id == bottom[3]);
            Program.Assert(!model.IsHandResponsePending && model.HasPrivatelyViewedCards &&
                model.SkillChoices.SelectMany(c => c.Cards).SequenceEqual(bottom) &&
                face is { IsCard: true, IsHidden: false, Face: not null } &&
                face.Face.Kind == game.CreateSnapshot(0).PrivateRevealedCards!.Single(c => c.Id == bottom[3]).Kind,
                "The actual skill-choice template must resolve a selectable bottom card face solely from the holder's authorized snapshot.");
            commands = game.AcceptedCommands.Count;
            Click(button);
            AssertAnswer(game, commands, bottomPrompt.PromptId, choice.Id);
            var moved = game.CardMovements.Skip(movements).ToArray();
            Program.Assert(moved.Length == 2 &&
                moved.Any(m => m.CardId == coins[0] && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile) &&
                moved.Any(m => m.CardId == bottom[3] && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) &&
                game.Events.Count(e => e.Payload is LiuBaCompletedEvent completed && completed.FrameId == frame.Id) == 1 &&
                game.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == coins[1]).SkillMarkLabel?.Contains("币", StringComparison.Ordinal) == true,
                "The WPF answer must exchange only the selected original coin and private bottom entity, preserving the unselected coin mark.");
            commands = game.AcceptedCommands.Count;
            model.SelectSkillChoiceCommand.Execute(choice);
            Program.Assert(game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements + 2,
                "A retired private bottom choice must not submit or pay the physical exchange again.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static GameEngine CreateEnding(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        for (var i = 0; i < 2; i++)
        {
            Accept(game, new UseProgramSkillCommand(0, "ol:zhubi", "recast-coin", [], [], game.Revision, game.PendingDecision!.PromptId));
            Answer(game, c => Branch(c, "target") && c.Targets.SequenceEqual([0]));
            var minted = game.Events.Select(e => e.Payload).OfType<LiuBaCoinMintedEvent>().Select(e => e.CardId).ToHashSet();
            Answer(game, c => Branch(c, "card") && !minted.Contains(c.Cards.Single()));
            Reach(game, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        }
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, () => game.PendingDecision is { PlayerSeat: 0 } p && p.Choices.Any(c => Branch(c, "coin")));
        return game;
    }

    private static bool Branch(PromptChoice choice, string branch) =>
        choice.Parameters.GetValueOrDefault("program-action") == "liu-ba" && choice.Parameters.GetValueOrDefault("branch") == branch;
    private static int[] Deck(GameEngine game) => game.CreateCardZoneDiagnostics().Where(z => z.Location == CardLocation.DrawPile)
        .OrderBy(z => z.ZoneIndex).Select(z => z.CardId).ToArray();
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Liu Ba UI fixture command rejected."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = game.PendingDecision!; Accept(game, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var i = 0; i < 80; i++)
        {
            if (reached()) return;
            if (game.PendingDecision is { PlayerSeat: 0 } p)
                throw new InvalidOperationException($"Unexpected Liu Ba UI prompt: {p.Kind}/{p.SkillPrompt?.SkillId}.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The actual Liu Ba UI fixture did not reach its expected boundary.");
    }
    private static void AssertAnswer(GameEngine game, int before, PromptId prompt, ChoiceId choice) =>
        Program.Assert(game.AcceptedCommands.Count == before + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand a &&
            a.ActorSeat == 0 && a.PromptId == prompt && a.Choice == choice,
            "The real WPF control must submit exactly its current native AnswerPrompt choice ID.");
    private static MainViewModel Restore(GameCheckpoint checkpoint, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, checkpoint));
        var model = new MainViewModel(false, checkpoint.Options.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }
    private static Button BoundButton(DependencyObject root, ICommand command, object parameter) => Program.Find<Button>(root)
        .Single(b => ReferenceEquals(b.Command, command) && ReferenceEquals(b.CommandParameter, parameter));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command is not null,
            "The actual WPF coin/bottom control must be enabled and visible.");
        button.Command!.Execute(button.CommandParameter);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 780)); root.Arrange(new Rect(0, 0, 1120, 780)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:liu-ba-coin-ui", Owner = "fixture:liu-ba-coin-ui-owner";
        public PackageManifest Manifest { get; } = new("fixture-liu-ba-coin-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:liu-ba");
            const string pick = "fixture:liu-ba-coin-ui-pick";
            b.AddSkill(new(pick, "本人", "固定公开选将评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "铸币交互", "ol-liu-ba", pick, "shu", 8, ["ol:zhubi"]));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:liu-ba-coin-ui-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "其他角色", "supporter", "standard:none", "wei", 8));
            b.AddDeck(new("fixture:liu-ba-coin-ui-deck", "真实币与底牌", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:dodge",
                    new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray()
            });
            b.AddMode(new(Mode, "铸币交互", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:liu-ba-coin-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
