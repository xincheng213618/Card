using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableSurfaceChecks
{
    public static void ActionDockSelection(string output)
    {
        using var vm = FindEquipmentHand();
        var engine = Program.Engine(vm);
        var before = engine.Revision;
        var action = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        var card = vm.Hand.Single(card => card.Id == action.CardId);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var snapshotField = typeof(MainViewModel).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalSnapshot = (GameSnapshot)snapshotField.GetValue(vm)!;
        try
        {
            var synthetic = originalSnapshot with
            {
                Players = originalSnapshot.Players.Select(player => player.IsHuman ? player with
                {
                    Equipment = [new CardSnapshot(900001, CardKind.GeneralWeapon, Suit.None, 0, "张飞", string.Empty),
                        new CardSnapshot(900002, CardKind.RedBloodBlade, Suit.None, 0, "赤血刃", string.Empty)],
                    EquipmentSlotCapacities = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.Weapon] = 2 }
                } : player).ToArray()
            };
            snapshotField.SetValue(vm, synthetic);
            var weapons = vm.HumanEquipmentSlots.Where(item => item.Slot == EquipmentSlot.Weapon).ToArray();
            Program.Assert(weapons.Length == 2 && weapons.All(item => item.IsOccupied) &&
                           weapons[0].Name == "张飞" && weapons[0].HasDynamicWeaponName &&
                           weapons.All(item => item.RankArtwork is null && item.SuitArtwork is null),
                "Multiple weapon slots must retain generated names and omit absent rank/suit art.");
            Program.Render(root, 1440, 860, Path.Combine(output, "table-multiple-weapons.png"));
        }
        finally { snapshotField.SetValue(vm, originalSnapshot); }
        Program.Render(root, 1440, 860, Path.Combine(output, "action-dock-idle.png"));
        var confirm = (Button)window.FindName("PlayCardButton");
        var cancel = (Button)window.FindName("CancelActionButton");
        var end = (Button)window.FindName("EndTurnButton");
        var sort = (Button)window.FindName("SortHandButton");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        var positions = new[] { Bounds(confirm), Bounds(cancel), Bounds(end) };
        Program.Assert(new[] { confirm, cancel, end }.All(button => button.Visibility == Visibility.Visible && button.ActualHeight >= 32) &&
            !confirm.IsEnabled && !cancel.IsEnabled && end.IsEnabled && Equals(confirm.Content, "确 定"),
            "The idle action dock must retain all three controls with truthful enabled states.");
        Program.Assert(positions[0].Right < positions[1].Left && positions[1].Right < positions[2].Left &&
            Bounds(sort).Left >= Bounds((FrameworkElement)window.FindName("HandViewport")).Right,
            "Primary controls are out of order or sorting still occupies the equipment rail.");
        vm.SelectCardCommand.Execute(card);
        Program.Render(root, 1440, 860, Path.Combine(output, "action-dock-selected.png"));
        Program.Assert(confirm.IsEnabled && cancel.IsEnabled && positions.SequenceEqual(new[] { Bounds(confirm), Bounds(cancel), Bounds(end) }),
            "Selecting an equipment card moved the action buttons or left confirmation inaccessible.");
        cancel.Command!.Execute(cancel.CommandParameter);
        Program.Assert(!vm.HasSelection && engine.Revision == before, "Cancel submitted a command or left the card selected.");
        vm.SelectCardCommand.Execute(card);
        confirm.Command!.Execute(confirm.CommandParameter);
        Program.Assert(engine.Revision == before + 1 && engine.CreateSnapshot(0, false).Players[0].Equipment.Any(equipment => equipment.Id == card.Id),
            "The new confirm button did not equip the exact selected card once.");
        window.Content = null;
        window.Close();
    }

    public static void EquipmentAndSkillControls(string output)
    {
        foreach (var kind in CardCatalog.ImplementedCards.Select(card => card.Kind).Where(EquipmentCatalog.IsEquipment))
            Program.Assert(CardArt.GetEquipment(kind) is BitmapSource { PixelWidth: 284, PixelHeight: 50 }, $"Missing equipment strip: {kind}.");
        foreach (var suit in new[] { "♥", "♦", "♠", "♣" })
        foreach (var rank in new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" })
            Program.Assert(CardArt.GetRank(rank, suit) is BitmapSource { PixelWidth: 38 } && CardArt.GetSuit(suit) is BitmapSource { PixelHeight: 34 },
                $"Missing original rank or suit sprite: {suit}{rank}.");

        using var vm = FindEquipmentHand();
        var engine = Program.Engine(vm);
        var action = engine.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        var card = vm.Hand.Single(card => card.Id == action.CardId);
        vm.SelectCardCommand.Execute(card);
        vm.PlaySelectedCardCommand.Execute(null);
        Program.AdvanceToDecision(vm);
        var actual = engine.CreateSnapshot(0, false).Players[0].Equipment.Single(equipment => equipment.Id == card.Id);
        var slot = vm.HumanEquipmentSlots.Single(slot => slot.Card?.Id == actual.Id);
        Program.Assert(slot.Tooltip.Contains(actual.DisplayName) && ReferenceEquals(slot.Artwork, CardArt.GetEquipment(actual.Kind)) &&
            ReferenceEquals(slot.RankArtwork, card.RankArtwork) && ReferenceEquals(slot.SuitArtwork, card.SuitArtwork),
            "An equipped physical card lost its identity or original rank/suit mapping.");
        Program.Assert(vm.HumanEquipmentSlots.Count == 5 && vm.HumanEquipmentSlots.Count(slot => slot.IsOccupied) == 1,
            "Empty slots must not show abolished or occupied equipment.");
        var revision = engine.Revision;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        foreach (var size in new[] { (1120, 740), (1440, 860), (1920, 1080) })
        {
            Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"table-equipment-{size.Item1}.png"));
            Rect Bounds(string name)
            {
                var element = (FrameworkElement)window.FindName(name);
                return element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            }
            var equip = Bounds("EquipmentRail");
            var hand = Bounds("HandViewport");
            var skill = Bounds("HumanSkillPanel");
            var portrait = Bounds("HumanPortrait");
            var sidebar = Bounds("TableSidebar");
            Program.Assert(equip.Right <= hand.Left + 1 && hand.Right <= skill.Left + 1 && skill.Right <= portrait.Left + 1,
                "Equipment, hand, skill buttons and portrait overlap.");
            Program.Assert(portrait.Right <= size.Item1 && hand.Bottom <= size.Item2 && sidebar.Bottom <= Bounds("HandDock").Top,
                "The new table layout clips persistent controls.");
        }
        Program.Assert(engine.Revision == revision, "Resizing or inspecting equipment changed the game.");
        window.Content = null;
        window.Close();

        using var active = ActiveSkillChecks.CreateShowcase(721019);
        active.SelectGeneralChoiceCommand.Execute(active.GeneralChoices.Single(choice => choice.SkillName == "苦肉"));
        Program.AdvanceToDecision(active);
        var activeWindow = new MainWindow(active);
        var activeRoot = (FrameworkElement)activeWindow.Content;
        try
        {
            Program.Render(activeRoot, 1120, 740, Path.Combine(output, "table-skill-available.png"));
            var originalButton = SkillButton();
            var original = (HumanSkillViewModel)originalButton.CommandParameter;
            var command = originalButton.Command ?? throw new InvalidOperationException("The skill chip has no command.");
            Program.Assert(originalButton.IsEnabled,
                "The skill chip is not an actionable control.");
            active.SaveGameCommand.Execute(null);
            Program.Assert(active.HasManualSave && !active.HasSaveError, active.SaveStatus);
            var originalGame = Program.Engine(active);
            active.LoadManualGameCommand.Execute(null);
            var loaded = Program.Engine(active);
            Program.Assert(!active.HasSaveError && !ReferenceEquals(originalGame, loaded) &&
                active.HumanSkillCards.Any(skill => ReferenceEquals(skill, original)),
                "An unchanged skill chip must remain published across an equal-state load.");

            active.StartTutorialCommand.Execute(null);
            Program.Render(activeRoot, 1120, 740, Path.Combine(output, "table-skill-tutorial.png"));
            Program.Assert(active.IsTutorialActive && !ReferenceEquals(Program.Engine(active), loaded) &&
                !active.HumanSkillCards.Any(skill => ReferenceEquals(skill, original)),
                "The tutorial must publish its own skill rail before restoring the suspended match.");
            active.ExitTutorialCommand.Execute(null);
            Program.Assert(!active.IsTutorialActive && ReferenceEquals(Program.Engine(active), loaded),
                "The tutorial must restore the original engine without using its skill.");
            active.LoadManualGameCommand.Execute(null);
            var currentGame = Program.Engine(active);
            var current = active.HumanSkillCards.Single(skill => skill.ContentId == original.ContentId);
            Program.Assert(!active.HasSaveError && !ReferenceEquals(currentGame, loaded) && current == original &&
                current.IsAvailable && !current.IsDisabled && !ReferenceEquals(current, original),
                "The restored rail must expose an equal available chip while retiring the pre-tutorial parameter.");

            var beforeRevision = currentGame.Revision;
            var beforeCommands = currentGame.AcceptedCommands.Count;
            var beforeMoves = currentGame.CardMovements.Count;
            var beforeState = SnapshotJson.Serialize(currentGame.CreateSnapshot(0));
            var before = currentGame.CreateSnapshot(0).Players[0];
            var text = active.SelectedCardText;
            var prompt = active.PromptText;
            Reject(original, "A retired skill chip rebound to the loaded game and paid its HP cost.");
            Reject(current with { }, "An equal copied skill chip must not execute the current action.");

            Program.Render(activeRoot, 1120, 740, Path.Combine(output, "table-skill-after-reload.png"));
            var button = SkillButton();
            var skillAction = active.HumanActiveSkillActions.Single(action => action.ProgramSkillId == current.ContentId);
            Program.Assert(button.IsEnabled && ReferenceEquals(button.CommandParameter, current),
                "The rendered skill chip must carry the current published parameter after reload.");
            button.Command!.Execute(button.CommandParameter);
            var after = currentGame.CreateSnapshot(0).Players[0];
            Program.Assert(currentGame.Revision == beforeRevision + 1 &&
                currentGame.AcceptedCommands.Count == beforeCommands + 1 &&
                currentGame.AcceptedCommands.Last() is UseProgramSkillCommand submitted &&
                submitted.SkillId == skillAction.ProgramSkillId && submitted.ActivationId == skillAction.ProgramActivationId &&
                submitted.CardIds.Count == 0 && submitted.TargetSeats.Count == 0 &&
                after.Hp == before.Hp - 1 && after.Hand.Count == before.Hand.Count + 2,
                "Clicking the current skill chip did not use the exact native action once.");

            Button SkillButton() => Program.Find<Button>(activeRoot).Single(button =>
                button.DataContext is HumanSkillViewModel { Name: "苦肉" });

            void Reject(HumanSkillViewModel parameter, string message)
            {
                Program.Assert(!active.HumanSkillCards.Any(skill => ReferenceEquals(skill, parameter)),
                    "The rejected skill parameter must have left the currently published rail.");
                command.Execute(parameter);
                Program.Assert(currentGame.Revision == beforeRevision && currentGame.AcceptedCommands.Count == beforeCommands &&
                    currentGame.CardMovements.Count == beforeMoves && SnapshotJson.Serialize(currentGame.CreateSnapshot(0)) == beforeState &&
                    active.SelectedCardText == text && active.PromptText == prompt &&
                    !active.HasSelection && !active.IsActiveSkillSelectionPending && active.CanEndTurn, message);
            }
        }
        finally
        {
            activeWindow.Content = null;
            activeWindow.Close();
        }
        EquipmentOnlyLonghun(output);
    }

    private static void EquipmentOnlyLonghun(string output)
    {
        using var vm = FindEquipmentHand(longhun: true, out var registry);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            var game = Program.Engine(vm);
            var ids = new List<int>();
            Program.Assert(new[] { CardKind.Crossbow, CardKind.BaguaFormation }.All(kind => vm.Hand.Any(card => card.Kind == kind)),
                "The fixed Longhun deck must deal both distinct equipment-slot materials before any rule assertion.");
            foreach (var kind in new[] { CardKind.Crossbow, CardKind.BaguaFormation })
            {
                var card = vm.Hand.First(item => item.Kind == kind);
                var nativeEquip = game.GetHumanLegalActions().Single(action =>
                    action.Kind == LegalActionKind.Equip && action.CardId == card.Id);
                var revision = game.Revision;
                var commands = game.AcceptedCommands.Count;
                var movements = game.CardMovements.Count;
                vm.SelectCardCommand.Execute(card);
                Program.Assert(vm.CanConfirmSelected && card.IsSelected,
                    "The Longhun fixture must prepare each real native equipment card without using its conversion.");
                vm.ConfirmSelectedCommand.Execute(null);
                var equipmentMoves = game.CardMovements.Skip(movements).Where(move => move.CardId == card.Id).ToArray();
                Console.WriteLine($"Longhun fixture equipment {kind}/{card.Id}: " +
                    string.Join("; ", equipmentMoves.Select(move => $"{move.From} -> {move.To} ({move.Reason})")));
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                    game.AcceptedCommands.Last() is PlayCardCommand equip && equip.CardId == nativeEquip.CardId &&
                    game.CreateSnapshot(0).Players[0].Equipment.Any(item => item.Id == card.Id && item.Suit == Suit.Diamond) &&
                    !vm.Hand.Any(item => item.Id == card.Id) && equipmentMoves is [var use, var enter] &&
                    use.From == CardLocation.Hand(0) && use.To == CardLocation.Processing && use.Reason == CardMoveReasons.EquipmentUse &&
                    enter.From == CardLocation.Processing && enter.To == CardLocation.Equipment(0) && enter.Reason == CardMoveReasons.EquipmentEnter,
                    "The Longhun fixture must move each exact Diamond entity through one accepted native equipment command.");
                ids.Add(card.Id);
                Program.AdvanceToDecision(vm);
            }
            var costs = ids.Order().ToArray();
            Program.Assert(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                game.CreateSnapshot(0).Players[0].Equipment.Where(card => costs.Contains(card.Id)).Select(card => card.Suit)
                    .SequenceEqual([Suit.Diamond, Suit.Diamond]) && costs.All(id => !vm.Hand.Any(card => card.Id == id)),
                "The native Longhun regression requires two same-suit materials in distinct real equipment slots, outside Hand.");
            vm.SaveGameCommand.Execute(null);
            Program.Assert(vm.HasManualSave && !vm.HasSaveError, vm.SaveStatus);
            foreach (var route in new[] { "button", "Enter" })
            {
                vm.LoadManualGameCommand.Execute(null);
                game = Program.Engine(vm);
                Program.Assert(!vm.HasSaveError && !vm.HasSelection && game.CreateSnapshot(0).PendingDecision is
                    { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "Both Longhun controls must start from the same real saved Play boundary.");
                var action = vm.HumanActiveSkillActions.Single(item => item.ProgramSkillId == "ol:longhun" &&
                    item.ProgramActivationId == "two-diamond");
                Program.Assert(action.SelectedCardsSameSuit && action.MinCardCount == 2 && action.MaxCardCount == 2 &&
                    costs.All(action.SelectableCardIds.Contains) && action.SelectableTargetSeats.Count > 0,
                    "The current native two-Diamond activation must publish both equipped physical materials and a legal target.");
                var views = Views();
                var beforeRevision = game.Revision;
                var beforeCommands = game.AcceptedCommands.Count;
                var beforeMoves = game.CardMovements.Count;
                var beforeEvents = game.Events.Count;
                Program.Render(root, 1120, 740, Path.Combine(output, $"longhun-equipment-{route}-before.png"));
                var chip = Program.Find<Button>(root).Single(button =>
                    button.DataContext is HumanSkillViewModel { ContentId: "ol:longhun" });
                Program.Assert(chip.Visibility == Visibility.Visible && chip.IsEnabled &&
                    ReferenceEquals(chip.Command, vm.ActivateHumanSkillCommand), "The native Longhun rail chip must be actionable.");
                Click(chip);
                foreach (var id in costs)
                {
                    Layout();
                    var choice = vm.ActiveSkillEquipmentChoices.Single(item => item.Cards.SequenceEqual([id]));
                    var button = Program.Find<Button>(root).Single(item =>
                        ReferenceEquals(item.Command, vm.SelectActiveSkillEquipmentChoiceCommand) &&
                        ReferenceEquals(item.CommandParameter, choice));
                    Program.Assert(button.Visibility == Visibility.Visible && button.IsEnabled &&
                        button.Command!.CanExecute(button.CommandParameter), "A current equipped Longhun cost must have its own enabled button.");
                    Click(button);
                }
                var target = vm.Seats.Single(seat => seat.Seat == action.SelectableTargetSeats.First());
                Program.Assert(target.IsLegalTarget, "The native Longhun target must be legal after selecting both equipment costs.");
                vm.SelectTargetCommand.Execute(target);
                var selectedCosts = (HashSet<int>)typeof(MainViewModel)
                    .GetField("_selectedActiveSkillCardIds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
                var selectedTargets = (List<int>)typeof(MainViewModel)
                    .GetField("_selectedActiveSkillTargetSeats", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;
                Program.Assert(selectedCosts.SetEquals(costs) && selectedTargets.SequenceEqual([target.Seat]) &&
                    vm.Hand.All(card => !card.IsSelected) && vm.ActiveSkillEquipmentChoices
                        .Where(choice => choice.Description.StartsWith("✓", StringComparison.Ordinal))
                        .SelectMany(choice => choice.Cards).Order().SequenceEqual(costs) &&
                    ReferenceEquals(Program.Engine(vm), game) && Views().SequenceEqual(views) &&
                    game.Revision == beforeRevision && game.AcceptedCommands.Count == beforeCommands &&
                    game.CardMovements.Count == beforeMoves && game.Events.Count == beforeEvents,
                    "Selecting Longhun costs and target must retain the exact equipment-only reversible draft and every player-safe view.");
                Program.Render(root, 1120, 740, Path.Combine(output, $"longhun-equipment-{route}-ready.png"));
                var confirm = (Button)window.FindName("PlayCardButton");
                Program.Assert(vm.CanConfirmActiveSkill && vm.CanConfirmSelected && confirm.Visibility == Visibility.Visible &&
                    confirm.IsEnabled && confirm.Command!.CanExecute(confirm.CommandParameter),
                    "Two real same-Diamond equipment materials must enable native Longhun confirmation outside the Hand collection.");
                var promptId = game.CreateSnapshot(0).PendingDecision!.PromptId;
                if (route == "button") Click(confirm);
                else Program.Assert(TableShortcutChecks.Input(window, System.Windows.Input.Key.Enter),
                    "Enter must confirm the equipment-only native Longhun draft.");
                Program.Assert(game.Revision == beforeRevision + 1 && game.AcceptedCommands.Count == beforeCommands + 1 &&
                    game.AcceptedCommands.Last() is UseProgramSkillCommand submitted && submitted.ActorSeat == 0 &&
                    submitted.SkillId == action.ProgramSkillId && submitted.ActivationId == action.ProgramActivationId &&
                    submitted.CardIds.SequenceEqual(costs) && submitted.TargetSeats.SequenceEqual([target.Seat]) &&
                    submitted.PromptId == promptId && submitted.ExpectedRevision == beforeRevision && !vm.HasSelection,
                    $"Longhun {route} must submit one exact native activation with the two equipment costs and original target.");
                Program.AdvanceToDecision(vm);
                var accepted = game.Events.Skip(beforeEvents).Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
                    .Single(item => item.Action.Type == CardActionType.Use && item.Action.EffectiveKind == CardKind.FireSlash);
                Program.Assert(accepted.Action.ActorSeat == 0 && accepted.Action.TargetSeats.SequenceEqual([target.Seat]) &&
                    accepted.Action.PhysicalCards.Select(card => card.CardId).Order().SequenceEqual(costs) &&
                    accepted.Action.PhysicalCards.All(card => card.From == CardLocation.Equipment(0)) &&
                    accepted.Action.ConversionChain is [var source] && source.SkillId == "ol:longhun" && source.BindingId == "2-diamond" &&
                    costs.All(id => game.CardMovements.Skip(beforeMoves).Count(move => move.CardId == id &&
                        move.From == CardLocation.Equipment(0) && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) == 1 &&
                        game.CardMovements.Skip(beforeMoves).Count(move => move.CardId == id && move.From == CardLocation.Processing &&
                            move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.UseFinished) == 1) &&
                    game.AcceptedCommands.Skip(beforeCommands).OfType<UseProgramSkillCommand>().Count() == 1 &&
                    costs.All(id => !game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == id)),
                    $"Longhun {route} must accept one real Fire Slash and pay/finish each equipped material exactly once.");
                var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                Program.Assert(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(restored.CreateSnapshot(seat)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(seat))), "Cold replay must retain the exact native equipment-only Longhun use and every player-safe view.");
            }

            string[] Views() => Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
            void Layout()
            {
                root.Measure(new Size(1120, 740));
                root.Arrange(new Rect(0, 0, 1120, 740));
                root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                root.UpdateLayout();
            }
            static void Click(Button button) => typeof(System.Windows.Controls.Primitives.ButtonBase)
                .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static MainViewModel FindEquipmentHand() => FindEquipmentHand(longhun: false, out _);

    private static MainViewModel FindEquipmentHand(bool longhun, out ContentRegistry registry)
    {
        // The action-dock contract is a native equipment selection. An arbitrary
        // expanded-pool general can add competing interpretations of the same card.
        registry = longhun
            ? ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new ActionDockFixture(longhun: true))
            : ContentRegistry.Build(new StandardContentPackage(), new ActionDockFixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = ActionDockFixture.ModeId, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(new StartGameCommand());
        Accept(new SelectGeneralCommand(0, "fixture:action-dock-0", game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                       game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Equip),
            "The fixed native-equipment fixture must reach a real playable equipment card.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var vm = new MainViewModel(false, game.Seed, false, store, contentRegistry: registry) { IsMotionEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        Program.Assert(!vm.HasSaveError, vm.SaveStatus);
        return vm;

        void Accept(GameCommand command)
        {
            var result = game.Submit(command);
            Program.Assert(result.Accepted, result.Error?.Message ?? "Expected a real native-equipment fixture command.");
        }
    }

    private sealed class ActionDockFixture(bool longhun = false) : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-action-dock";
        public PackageManifest Manifest { get; } = new("fixture-action-dock", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var ids = Enumerable.Range(0, 4).Select(seat => $"fixture:action-dock-{seat}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new(id, "装备操作栏", "supporter", longhun && id == ids[0] ? "ol:longhun" : "standard:none", "wei", 4));
            builder.AddDeck(longhun
                ? new ContentDeckRecipe("fixture:action-dock-deck", "装备龙魂", 10, 0, [])
                {
                    PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(
                        index % 2 == 0 ? "standard:crossbow" : "standard:bagua", Suit.Diamond, index % 13 + 1)).ToArray()
                }
                : new("fixture:action-dock-deck", "装备操作栏", 4, 2, [new ContentDeckCardCount("standard:crossbow", 40)]));
            builder.AddMode(new(ModeId, "装备操作栏", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:action-dock-deck", GeneralCandidateCount: 4, GeneralPoolIds: ids));
        }
    }
}
