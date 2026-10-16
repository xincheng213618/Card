using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableStatePresentationChecks
{
    public static void FactionsChainsAndMandatoryIdentities(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var general in new[] { "ol:shen-liu-bei", "classic:shen-guan-yu" })
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 19, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "identity:table-state-" + general, UseInteractiveSetup = true,
                AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 12
            }, registry);
            Accept(game, new StartGameCommand());
            Accept(game, new SelectGeneralCommand(0, general, game.Revision, game.PendingDecision!.PromptId));
            for (var i = 0; i < 16 && game.PendingDecision?.Kind != DecisionKind.SelectFaction; i++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.PendingDecision?.Kind == DecisionKind.SelectFaction, "The real god general must request its faction.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, 19, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsAutoAdvance = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                Program.Render(root, 1440, 860, Path.Combine(output, "table-state-faction-" + general.Replace(':', '-') + ".png"));
                var factions = (ItemsControl)window.FindName("GodFactionButtons");
                var buttons = Program.Find<Button>(factions).ToArray();
                Program.Assert(model.IsGodFactionSelectionPending && buttons.Length == 4 &&
                    buttons.All(button => button.IsEnabled && button.CommandParameter is PromptChoice) &&
                    ((ScrollViewer)window.FindName("ResponseViewport")).Visibility == Visibility.Collapsed,
                    "The faction panel must expose only the current engine choices with native commands and no duplicate prompt controls.");
                var retired = model.GodFactionChoices[0].Choice;
                buttons[2].Command!.Execute(buttons[2].CommandParameter);
                Program.Assert(game.CreateSnapshot(0).Players[0].FactionId == "wu" && !model.IsGodFactionSelectionPending,
                    "Clicking Wu must commit the current real setup prompt and close its panel.");
                var revision = game.Revision;
                model.SelectSkillChoiceCommand.Execute(retired);
                Program.Assert(game.Revision == revision, "A retired faction button must not choose again.");
                AdvanceToPlay(game);
                Refresh(model, game);
                var longnu = general == "ol:shen-liu-bei";
                VerifyHand(game, model, longnu ? CardKind.FireSlash : CardKind.Slash);
                foreach (var (width, height) in new[] { (1120, 740), (1440, 860) })
                {
                    Program.Render(root, width, height, Path.Combine(output, $"table-state-{general.Replace(':', '-')}-{width}.png"));
                    var portrait = (ContentControl)window.FindName("HumanPortrait");
                    var chain = Program.Find<ChainOverlay>(portrait).Single();
                    Program.Assert(chain.Visibility == (longnu ? Visibility.Visible : Visibility.Collapsed) && !chain.IsHitTestVisible,
                        "The portrait chain must follow the committed chained state without intercepting target input.");
                    var badge = Program.Find<Border>(portrait).Single(element => element.Name == "KingdomBadge");
                    Program.Assert(model.HumanPlayer!.Kingdom == "吴" && badge.ActualWidth == 29 && badge.ActualHeight == 29,
                        "The chosen faction must use the same fixed badge as ordinary generals.");
                    foreach (var card in model.Hand)
                    {
                        var hand = (ItemsControl)window.FindName("HandCards");
                        var container = (FrameworkElement)hand.ItemContainerGenerator.ContainerFromItem(card);
                        var ribbon = Program.Find<Border>(container).Single(element => element.Name == "CardIdentityRibbon");
                        var face = Program.Find<Image>(container).Single(element => element.Name == "CardArtwork");
                        Program.Assert(ribbon.Visibility == (card.HasIdentity ? Visibility.Visible : Visibility.Collapsed) &&
                            ReferenceEquals(face.Source, CardArt.Get(card.Kind)), "Identity ribbons must preserve the original face, suit and rank.");
                    }
                    var rail = (ItemsControl)window.FindName("HumanSkillCards");
                    var skillButtons = Program.Find<Button>(rail).ToArray();
                    Program.Assert(model.HumanSkillColumns == 1 && skillButtons.Length == model.HumanSkillCards.Count &&
                        skillButtons.All(button => button.ActualHeight == 32 && button.ToolTip is string tooltip &&
                            tooltip.Contains(((HumanSkillViewModel)button.CommandParameter).StateText)) &&
                        Program.Find<TextBlock>(rail).Count() == model.HumanSkillCards.Count,
                        "The compact rail must show only skill names and icons, with full state details retained in tooltips.");
                    Program.Assert(Program.Find<SkillTypeIcon>(rail).All(icon =>
                        icon.Visibility == (((HumanSkillViewModel)icon.DataContext).IsConversion ? Visibility.Visible : Visibility.Collapsed)) &&
                        model.HumanSkillCards.Where(skill => skill.IsLocked && !skill.IsConversion)
                            .All(skill => skill.ButtonArtwork.Contains("skillSuoDing", StringComparison.Ordinal)),
                        "Only conversion skills add an overlay; locked skills use the single lock in their original artwork.");
                    foreach (var seatButton in Program.Find<Button>(root).Where(button => button.Name == "SeatButton"))
                    {
                        var seat = (SeatViewModel)seatButton.DataContext;
                        Program.Assert(!Program.Find<HealthMeter>(seatButton).Any() &&
                            !Program.Find<TextBlock>(seatButton).Any(text => text.Text == "回合中" || text.Name == "SeatEquipmentText" ||
                                System.Windows.Data.BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path is "SkillName" or "DistanceText") &&
                            !Program.Find<Border>(seatButton).Any(border => border.Name == "TurnLine") &&
                            seatButton.ToolTip is string details && details.Contains(seat.SkillText) &&
                            details.Contains(seat.EquipmentText) && details.Contains(seat.DistanceText),
                            "Portraits must omit duplicate skill/equipment/turn summaries and horizontal HP tracks while retaining hover details.");
                        Program.Assert(Program.Find<ItemsControl>(seatButton).Single(control => control.Name == "SeatHealthIcons").Items.Count ==
                            seat.HealthImages.Count() && (!seat.IsCurrent ||
                            Program.Find<Border>(seatButton).Single(border => border.Name == "SeatFrame").BorderBrush.ToString() == "#FF78CCB4"),
                            "Public HP icons and the active-turn frame must remain visible on the clean portrait.");
                    }
                }
                Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
                for (var i = 0; i < 8 && game.State.Phase == TurnPhase.Play; i++) Accept(game, new AdvanceOneStepCommand(game.Revision));
                Refresh(model, game);
                Program.Assert(game.State.Phase != TurnPhase.Play, "The fixture must leave the first play phase.");
                VerifyHand(game, model, longnu ? null : CardKind.Slash);
                if (longnu)
                {
                    AdvanceToPlay(game);
                    Refresh(model, game);
                    Program.Assert(model.Hand.All(card => card.IdentityKind == CardKind.ThunderSlash),
                        "The next real Longnu phase must replace red Fire Slash ribbons with trick Thunder Slash ribbons.");
                    Program.Render(root, 1440, 860, Path.Combine(output, "table-state-longnu-yin.png"));
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static void VerifyHand(GameEngine game, MainViewModel model, CardKind? expected)
    {
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        Program.Assert(hand.Any(card => card.Suit == Suit.Heart) && hand.All(card => card.IdentityKind ==
                (card.Suit == Suit.Heart ? expected : null)) &&
            game.CreateSnapshot(1).Players[0].Hand.Count == 0 &&
            model.Hand.All(card => card.IdentityKind == hand.Single(item => item.Id == card.Id).IdentityKind),
            "Owner-only hand identities must follow real phase conditions independently of legal action availability.");
    }

    private static void Refresh(MainViewModel model, GameEngine game) => typeof(MainViewModel)
        .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [game.CreateSnapshot(0)]);

    private static void AdvanceToPlay(GameEngine game)
    {
        for (var i = 0; i < 120 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; i++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
                Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The small fixed fixture must reach human play.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted fixture command.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("table-state-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:shen-liu-bei", "classic:shen-guan-yu");
            var others = Enumerable.Range(0, 3).Select(i => "table-state:other-" + i).ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "陪测", "cao_cao", "standard:none", "wei", 8));
            builder.AddDeck(new("table-state:deck", "固定红黑无懈", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard("standard:nullification", i % 3 == 0 ? Suit.Spade : Suit.Heart, 9)).ToArray()
            });
            foreach (var general in new[] { "ol:shen-liu-bei", "classic:shen-guan-yu" })
                builder.AddMode(new("identity:table-state-" + general, "牌桌状态", 4, 4,
                    new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                    "table-state:deck", GeneralCandidateCount: 4, GeneralPoolIds: [general, .. others]));
        }
    }
}
