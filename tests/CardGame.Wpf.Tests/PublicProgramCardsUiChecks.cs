using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicProgramCardsUiChecks
{
    public static void RevealedChoicesRestoreAndSubmit(string output)
    {
        RevealedChoicesRestoreAndSubmit(output, nativeHarvest: false);
        RevealedChoicesRestoreAndSubmit(output, nativeHarvest: true);
    }

    private static void RevealedChoicesRestoreAndSubmit(string output, bool nativeHarvest)
    {
        var playerCount = nativeHarvest ? 8 : 4;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(nativeHarvest));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = playerCount, ModeId = "identity:ui-public-program", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 2 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:ui-public-owner", game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && (nativeHarvest
                 ? game.PendingDecision?.Kind != DecisionKind.PlayCard : game.CreateSnapshot(0).PendingDecision?.SkillPrompt is null); step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        if (nativeHarvest)
        {
            Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The fixed all-FiveGrains deck must reach the human's native Play boundary.");
            var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.FiveGrains);
            Accept(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId)));
            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.SelectHarvestCard; step++)
                Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Program.Assert(game.PendingDecision is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0, SkillPrompt: null },
                "The physical FiveGrains card must produce the native human harvest choice without a skill-program prompt.");
        }
        var shown = game.CreateSnapshot(0).PublicRevealedCards.ToArray();
        var expectedFaces = nativeHarvest ? playerCount : 2;
        var renderName = nativeHarvest ? "238-harvest-choices" : "238-public-program-choices";
        Program.Assert(shown.Length == expectedFaces && game.CreateSnapshot(0).PendingDecision!.Choices.All(choice => choice.Cards.Count == 1) &&
                       Enumerable.Range(0, playerCount).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Select(card => card.Id)
                           .SequenceEqual(shown.Select(card => card.Id))),
            "The real public choice must reveal the expected physical cards to all observers and publish exact single-card choices.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && model.PublicRevealedCards.Count == expectedFaces &&
                       model.PublicRevealedCards.All(face => face.IsPublicChoice && face.PublicCardLabel == "点击选取" &&
                           model.SelectRevealedCardCommand.CanExecute(face)) &&
                       shown.All(card => model.PublicRevealedCards.Any(face => face.Id == card.Id && face.Kind == card.Kind &&
                           face.Rank == card.RankText)),
            "Restored public choices must make the actual public faces selectable through the shared command.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            if (nativeHarvest) VerifyHarvestLayout(model, window, root, output);
            Program.Render(root, 1120, 740, Path.Combine(output, $"{renderName}.png"));
            var decisionPanel = (Border)window.FindName("DecisionPanel");
            var harvestHeader = (FrameworkElement)window.FindName("HarvestSelectionHeader");
            if (nativeHarvest)
                Program.Assert(decisionPanel.Background is System.Windows.Media.SolidColorBrush { Color.A: > 220 } &&
                               decisionPanel.BorderBrush is System.Windows.Media.SolidColorBrush harvestBorder &&
                               harvestBorder.Color == System.Windows.Media.Color.FromRgb(216, 183, 120) &&
                               decisionPanel.BorderThickness == new Thickness(2) && decisionPanel.Padding == new Thickness(12) &&
                               harvestHeader.Visibility == Visibility.Visible &&
                               ((FrameworkElement)window.FindName("TablePlaysViewport")).Visibility == Visibility.Collapsed,
                    "A native harvest must expose a framed selection area, its instruction header and no recent-play cards.");
            else
                Program.Assert(decisionPanel.Background is System.Windows.Media.SolidColorBrush { Color.A: 0 } &&
                               decisionPanel.BorderThickness == new Thickness(0) && harvestHeader.Visibility == Visibility.Collapsed,
                    "Other public faces must retain the shared table presentation.");
            var panel = Program.Find<ItemsControl>(root).Single(control => control.Name == "PublicRevealedCards");
            var buttons = FaceButtons();
            if (nativeHarvest)
                Program.Assert(buttons.All(button => button.ActualWidth >= 104 && button.ActualHeight >= 160 &&
                                   AutomationProperties.GetHelpText(button).Contains("获得到手牌")),
                    "Harvest choices must use larger interactive faces with an explicit gain action.");
            Program.Assert(buttons.Length == expectedFaces && buttons.All(button => button.IsEnabled && button.ActualWidth > 0 &&
                               button.CommandParameter is CardViewModel face && model.PublicRevealedCards.Contains(face)),
                "The shared XAML face buttons must expose every legal choice with its current physical face.");
            var command = buttons[0].Command!;
            var retired = (CardViewModel)buttons[0].CommandParameter;
            var original = Program.Engine(model);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var selected = model.PublicRevealedCards.Single(face => face.Id == retired.Id);
            Program.Assert(!model.HasSaveError && !ReferenceEquals(original, game) && !ReferenceEquals(retired, selected) &&
                           !model.PublicRevealedCards.Contains(retired) && command.CanExecute(selected),
                "Reloading the same native public choice must retire the actual old button parameter while publishing a new face for the same card.");
            Reject(retired, "A retired public face selected the reloaded prompt and gained its physical card.");
            Reject(new CardViewModel
            {
                Id = selected.Id, Kind = selected.Kind, Name = selected.Name, KindLabel = selected.KindLabel,
                SuitGlyph = selected.SuitGlyph, Rank = selected.Rank, Description = selected.Description,
                IsPlayable = selected.IsPlayable, IsPublicChoice = selected.IsPublicChoice, IsPrivateReveal = selected.IsPrivateReveal
            }, "An unpublished copy of a current public face must not select or gain its physical card.");

            Program.Render(root, 1120, 740, Path.Combine(output, $"{renderName}-reloaded.png"));
            var currentButton = FaceButtons().Single(button => ReferenceEquals(button.CommandParameter, selected));
            Program.Assert(currentButton.IsEnabled && currentButton.Command == command,
                "The rendered current button must remain enabled after rejecting retired and copied faces.");
            var faces = model.PublicRevealedCards.ToArray();
            var faceChanges = 0;
            System.Collections.Specialized.NotifyCollectionChangedEventHandler onFaceChange = (_, _) => faceChanges++;
            model.PublicRevealedCards.CollectionChanged += onFaceChange;
            var refreshRevision = game.Revision;
            var refreshCommands = game.AcceptedCommands.Count;
            var refreshViews = Enumerable.Range(0, playerCount).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray();
            try
            {
                foreach (var developerView in new[] { true, false })
                {
                    model.IsDeveloperView = developerView;
                    Program.Render(root, 1120, 740, Path.Combine(output, $"{renderName}-stable-{developerView}.png"));
                    var refreshedButton = FaceButtons().Single(button => button.CommandParameter is CardViewModel face && face.Id == selected.Id);
                    Program.Assert(faceChanges == 0 && model.PublicRevealedCards.Count == faces.Length &&
                                   model.PublicRevealedCards.Zip(faces).All(pair => ReferenceEquals(pair.First, pair.Second)) &&
                                   ReferenceEquals(currentButton, refreshedButton) && refreshedButton.IsEnabled && command.CanExecute(selected),
                        "A same-revision public view refresh replaced the current face parameters and their rendered buttons.");
                    Program.Assert(game.Revision == refreshRevision && game.AcceptedCommands.Count == refreshCommands &&
                                   Enumerable.Range(0, playerCount).All(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer)) == refreshViews[viewer]),
                        "A public presentation refresh must not advance rules or change any player view.");
                }
            }
            finally
            {
                model.PublicRevealedCards.CollectionChanged -= onFaceChange;
                model.IsDeveloperView = false;
            }
            if (!nativeHarvest)
            {
                VerifyPublicProjection(model, root, output);
                selected = model.PublicRevealedCards.Single(face => face.Id == selected.Id);
            }
            var suspendedFace = selected;
            model.StartTutorialCommand.Execute(null);
            Program.Assert(model.IsTutorialActive && model.PublicRevealedCards.Count == 0 && !command.CanExecute(suspendedFace),
                "The tutorial must withdraw the original public faces.");
            model.ExitTutorialCommand.Execute(null);
            selected = model.PublicRevealedCards.Single(face => face.Id == suspendedFace.Id);
            Program.Assert(!model.IsTutorialActive && ReferenceEquals(game, Program.Engine(model)) &&
                           game.Revision == refreshRevision && !ReferenceEquals(suspendedFace, selected) && command.CanExecute(selected),
                "Returning to the same original revision must publish new public faces without reviving withdrawn parameters.");
            Reject(suspendedFace, "A pre-tutorial public face must not select the restored original prompt.");
            Program.Render(root, 1120, 740, Path.Combine(output, $"{renderName}-tutorial-return.png"));
            currentButton = FaceButtons().Single(button => ReferenceEquals(button.CommandParameter, selected));
            Program.Assert(currentButton.IsEnabled && currentButton.Command == command,
                "The current face button must remain enabled after returning from the tutorial.");
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var choice = prompt.Choices.Single(candidate => candidate.Cards.Count == 1 && candidate.Cards[0] == selected.Id);
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            var movements = game.CardMovements.Count;
            var events = game.Events.Count;
            currentButton.Command!.Execute(currentButton.CommandParameter);
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                           game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.ActorSeat == 0 &&
                           answer.PromptId == prompt.PromptId && answer.Choice == choice.Id && answer.ExpectedRevision == revision &&
                           game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == selected.Id) &&
                           game.CardMovements.Any(move => move.CardId == selected.Id && move.To == CardLocation.Hand(0)) &&
                           !command.CanExecute(selected),
                "Clicking the current rendered face must submit its exact native choice once, gain that physical card and retire the parameter.");
            if (nativeHarvest)
                Program.Assert(game.CardMovements.Skip(movements).Count(move => move.CardId == selected.Id && move.From == CardLocation.Processing &&
                                   move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.HarvestPick) == 1 &&
                               game.Events.Skip(events).Count(item => item.Payload is HarvestCardSelectedEvent { PlayerSeat: 0 } pick &&
                                   pick.CardId == selected.Id) == 1,
                    "The shared current face must execute one native harvest gain and its matching public event.");
            Reject(selected, "A spent public face must not answer the next prompt or gain another card.");
            var replayed = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(Enumerable.Range(0, playerCount).All(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer)) ==
                               SnapshotJson.Serialize(replayed.CreateSnapshot(viewer))) &&
                           JsonSerializer.Serialize(game.AcceptedCommands) == JsonSerializer.Serialize(replayed.AcceptedCommands) &&
                           JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(replayed.Events) &&
                           game.CardMovements.SequenceEqual(replayed.CardMovements),
                "The real public-face submission must cold-replay all player views, exact commands, events and physical movements.");

            Button[] FaceButtons() => Program.Find<Button>(panel).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();

            void Reject(CardViewModel parameter, string message)
            {
                Program.Assert(!model.PublicRevealedCards.Contains(parameter), "The rejected face must no longer be published.");
                var canExecute = command.CanExecute(parameter);
                var oldRevision = game.Revision;
                var oldCommands = game.AcceptedCommands.Count;
                var oldMoves = game.CardMovements.Count;
                var oldEvents = game.Events.Count;
                var oldViews = Enumerable.Range(0, playerCount).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray();
                var selectedCards = model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray();
                var selectedTargets = model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
                var selectionText = model.SelectedCardText;
                var promptText = model.PromptText;
                var hasSelection = model.HasSelection;
                command.Execute(parameter);
                Program.Assert(game.Revision == oldRevision && game.AcceptedCommands.Count == oldCommands &&
                               game.CardMovements.Count == oldMoves && game.Events.Count == oldEvents && Enumerable.Range(0, playerCount).All(viewer =>
                                   SnapshotJson.Serialize(game.CreateSnapshot(viewer)) == oldViews[viewer]) &&
                               model.Hand.Where(card => card.IsSelected).Select(card => card.Id).SequenceEqual(selectedCards) &&
                               model.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(selectedTargets) &&
                               model.SelectedCardText == selectionText && model.PromptText == promptText && model.HasSelection == hasSelection,
                    message);
                Program.Assert(!canExecute, "Retired and copied public faces must also be disabled by CanExecute.");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void VerifyHarvestLayout(MainViewModel model, MainWindow window, FrameworkElement root, string output)
    {
        var game = Program.Engine(model);
        var snapshot = game.CreateSnapshot(0);
        var refresh = typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var viewport = (ScrollViewer)window.FindName("ResponseViewport");
        try
        {
            Program.Render(root, 1920, 1080, Path.Combine(output, "harvest-eight-card-selection.png"));
            Program.Assert(viewport.ScrollableHeight < 1 && Buttons().Length == 8 && Buttons().All(IsWithinViewport),
                "The eight-player harvest must show every complete choice in a roomy window.");
            Program.Render(root, 1120, 740, null);
            viewport.ScrollToBottom();
            Program.Render(root, 1120, 740, null);
            Program.Assert(IsWithinViewport(Buttons().Last()),
                "A compact harvest must let the player scroll to and select the last complete card.");
            var remaining = snapshot.PublicRevealedCards.Take(3).ToArray();
            refresh.Invoke(model, [snapshot with { PublicRevealedCards = remaining }]);
            viewport.ScrollToTop();
            Program.Render(root, 1920, 1080, Path.Combine(output, "harvest-three-card-selection.png"));
            Program.Assert(Buttons().Length == 3 && Buttons().All(IsWithinViewport) &&
                           Program.Find<TextBlock>((FrameworkElement)window.FindName("HarvestSelectionHeader"))
                               .Any(text => text.Text == "剩余 3 张"),
                "The remaining three cards and their count must stay together in the selection area.");
        }
        finally
        {
            refresh.Invoke(model, [snapshot]);
            viewport.ScrollToTop();
        }

        Button[] Buttons() => Program.Find<Button>((FrameworkElement)window.FindName("PublicRevealedCards"))
            .Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        bool IsWithinViewport(FrameworkElement element)
        {
            var bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
            return bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1;
        }
    }

    private static void VerifyPublicProjection(MainViewModel model, FrameworkElement root, string output)
    {
        // Exercise only the shared UI projection; never submit artificial card data to Core.
        var game = Program.Engine(model);
        var snapshot = game.CreateSnapshot(0);
        var card = snapshot.PublicRevealedCards[0];
        var original = model.PublicRevealedCards.Single(face => face.Id == card.Id);
        var refresh = typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var revision = game.Revision;
        var commands = game.AcceptedCommands.Count;
        var events = game.Events.Count;
        var movements = game.CardMovements.Count;
        var views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray();
        try
        {
            var projected = snapshot with { PublicRevealedCards = snapshot.PublicRevealedCards.Select(face => face.Id == card.Id
                ? face with { DisplayName = "投影公开牌", RankText = "Q", SkillMarkLabel = "闾" } : face).ToArray(),
                Players = snapshot.Players.Select(player => player.Seat == snapshot.HumanSeat
                    ? player with { Hand = player.Hand.Select(face => face with { SkillMarkLabel = "闾" }).ToArray() } : player).ToArray() };
            refresh.Invoke(model, [projected]);
            Program.Render(root, 1120, 740, Path.Combine(output, "238-public-face-projection.png"));
            var changed = model.PublicRevealedCards.Single(face => face.Id == card.Id);
            var button = FaceButton();
            Program.Assert(!ReferenceEquals(original, changed) && changed.Name == "投影公开牌" && changed.Rank == "Q" &&
                           changed.OriginalHandTag == "闾" && changed.CardHint.Contains("闾") &&
                           Program.Find<TextBlock>(button).Any(text => text.Text == "闾" && text.Visibility == Visibility.Visible && text.ActualWidth > 0) &&
                           Program.Find<TextBlock>(root).Any(text => text.Name == "ChoiceSkillMark" && text.Text == "闾" &&
                               text.Visibility == Visibility.Visible && text.ActualWidth > 0 &&
                               text.DataContext is PromptCardChoicePresentation { Face: { } choiceFace } && choiceFace.Id == card.Id) &&
                           model.Hand.Count > 0 && model.Hand.All(face => face.OriginalHandTag.Contains("闾") && face.CardHint.Contains("闾")) &&
                           AutomationProperties.GetName(button).Contains("投影公开牌") && AutomationProperties.GetName(button).Contains("Q") &&
                           !model.SelectRevealedCardCommand.CanExecute(original) && button.IsEnabled,
                "Changing visible face metadata at the same revision must update public and own-hand marks, hints and accessible text.");
            refresh.Invoke(model, [projected with { PendingDecision = null }]);
            Program.Render(root, 1120, 740, Path.Combine(output, "238-public-face-readonly.png"));
            var readOnly = model.PublicRevealedCards.Single(face => face.Id == card.Id);
            Program.Assert(!ReferenceEquals(changed, readOnly) && !readOnly.IsPublicChoice && readOnly.PublicCardLabel == "公开牌" &&
                           !FaceButton().IsEnabled && !model.SelectRevealedCardCommand.CanExecute(changed),
                "Withdrawing the public choice while retaining its physical cards must replace the faces and disable the real button.");
            var owner = snapshot.Players.Single(player => player.Seat == 1);
            var handReveal = projected with { PendingDecision = null, PublicRevealedCards = projected.PublicRevealedCards
                .Select(face => face.Id == card.Id ? face with { PublicHandOwnerSeat = owner.Seat } : face).ToArray() };
            refresh.Invoke(model, [handReveal]);
            Program.Render(root, 1120, 740, Path.Combine(output, "238-public-hand-owner.png"));
            var handFace = model.PublicRevealedCards.Single(face => face.Id == card.Id);
            var handButton = FaceButton();
            Program.Assert(!ReferenceEquals(readOnly, handFace) && handFace.PublicHandOwnerLabel == owner.Name &&
                           handFace.PublicCardLabel == owner.Name + "·展示手牌" && handFace.CardHint.Contains(owner.Name + "的展示手牌") &&
                           AutomationProperties.GetName(handButton).Contains(owner.Name + "的展示手牌") &&
                           !handButton.IsEnabled && !model.SelectRevealedCardCommand.CanExecute(handFace),
                "A persistent public hand face must identify its actual owner visually and accessibly without acquiring choice authority.");
            refresh.Invoke(model, [handReveal with { PublicRevealedCards = handReveal.PublicRevealedCards.Where(face => face.Id != card.Id).ToArray() }]);
            Program.Assert(model.PublicRevealedCards.All(face => face.Id != card.Id) && !model.SelectRevealedCardCommand.CanExecute(handFace),
                "Removing the current hand-residency face must remove its owner-labelled view and leave old parameters inert.");
            refresh.Invoke(model, [snapshot]);
            Program.Render(root, 1120, 740, Path.Combine(output, "238-public-face-projection-restored.png"));
            var restored = model.PublicRevealedCards.Single(face => face.Id == card.Id);
            Program.Assert(!ReferenceEquals(restored, readOnly) && restored.Name == card.DisplayName && restored.Rank == card.RankText &&
                           restored.IsPublicChoice && FaceButton().IsEnabled && !model.SelectRevealedCardCommand.CanExecute(readOnly),
                "Restoring the public choice must publish its actual faces without reviving the read-only parameters.");
        }
        finally { refresh.Invoke(model, [snapshot]); }
        Program.Assert(ReferenceEquals(game, Program.Engine(model)) && game.Revision == revision && game.AcceptedCommands.Count == commands &&
                       game.Events.Count == events && game.CardMovements.Count == movements && Enumerable.Range(0, 4).All(viewer =>
                           SnapshotJson.Serialize(game.CreateSnapshot(viewer)) == views[viewer]),
            "Public metadata and choice-availability projections must leave the real game and every player view unchanged.");

        Button FaceButton() => Program.Find<Button>(root).Single(button => button.Command == model.SelectRevealedCardCommand &&
            button.CommandParameter is CardViewModel face && face.Id == card.Id);
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted fixture command.");

    private sealed class Fixture(bool nativeHarvest = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-public-program", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-public-program","revision":1,
                "triggers":[{"id":"reveal-and-choose","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"shown","visibility":"public"},
                {"op":"selectCardSubset","target":"owner","sourceBind":"shown","resultBind":"chosen","minimumCards":1,"maximumCards":1,"maximumRankSum":208,"aiOrder":"mostCardsThenRankSum"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"ownerHand"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"shown","exceptBind":"chosen","destination":"discardPile"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-public-program":{"name":"公开选牌","description":"通过共享牌面选择实际展示牌"}}}""");
            builder.AddSkill(new("fixture:ui-public-program", "公开选牌", "通过共享牌面选择实际展示牌")
            { Program = catalog.Programs["fixture:ui-public-program"], ProgramPresentation = catalog.Presentations["fixture:ui-public-program"] });
            builder.AddGeneral(new("fixture:ui-public-owner", "选牌者", "supporter", nativeHarvest ? "standard:none" : "fixture:ui-public-program", "wei", 9));
            var playerCount = nativeHarvest ? 8 : 4;
            var others = Enumerable.Range(1, playerCount - 1).Select(seat => $"fixture:ui-public-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "旁观者", "supporter", "standard:none", "wu", 9));
            builder.AddDeck(new("fixture:ui-public-deck", "公开选择实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(nativeHarvest ? "standard:five_grains" : "standard:dodge", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:ui-public-program", "公开选择界面测试", playerCount, playerCount,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = playerCount - 2 },
                "fixture:ui-public-deck", GeneralCandidateCount: playerCount, GeneralPoolIds: ["fixture:ui-public-owner", .. others]));
        }
    }
}
