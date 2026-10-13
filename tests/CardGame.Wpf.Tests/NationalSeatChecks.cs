using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

internal static class NationalSeatChecks
{
    private static void Require(bool value, string message) => Program.Assert(value, message);
    private static string State(MainViewModel vm) => GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint());

    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(false, 721019, true, new MemorySaveStore(), useExpandedContent: true) { IsMotionEnabled = false };
        vm.SelectedTableMode = new TableModeOption(4, "双将旧规则测试", string.Empty, "national:lite-4");
        vm.StartNewGameCommand.Execute(null);
        for (var i = 0; i < 100 && !vm.CanEndTurn; i++) PersistenceChecks.Step(vm);
        Require(vm.CanEndTurn, "National portrait fixture failed to reach play.");
        return vm;
    }

    public static void PrivacyAndReveal(string output)
    {
        using var vm = Ready();
        var before = State(vm);
        var own = Program.Engine(vm).CreateSnapshot(0, true).Players[0];
        var primary = GeneralSlotViewModel.FromPlayer(own, false);
        var secondary = GeneralSlotViewModel.FromPlayer(own, true);
        Require(primary.HasPortrait && secondary.HasPortrait && !primary.IsSkillEnabled && !secondary.IsSkillEnabled, "Own dark slots lost portraits or enabled skills.");
        var other = own with { IsHuman = false };
        foreach (var second in new[] { false, true })
        {
            var hidden = GeneralSlotViewModel.FromPlayer(other, second);
            Require(!hidden.IsKnown && !hidden.HasPortrait && !hidden.IsSkillEnabled && hidden.GeneralId.Length == 0 && hidden.Name == "暗将" &&
                !hidden.DetailText.Contains(own.GeneralName) && !hidden.DetailText.Contains(own.SecondaryGeneralName!), "Trusted input leaked an unrevealed general through its portrait or tooltip.");
        }
        other = other with { IsSecondaryGeneralPublic = true };
        Require(!GeneralSlotViewModel.FromPlayer(other, false).HasPortrait && GeneralSlotViewModel.FromPlayer(other, true) is { HasPortrait: true, IsSkillEnabled: true },
            "Revealing the secondary slot also exposed the primary or left the revealed skill disabled.");
        vm.IsDeveloperView = true;
        Require(vm.Seats.Where(seat => !seat.IsHuman).All(seat => seat.RelationshipLabel == "未明" && !seat.IsTeammate && !seat.PrimaryGeneral!.HasPortrait && !seat.SecondaryGeneral!.HasPortrait &&
            seat.Kingdom == "未明势力" && seat.SkillName == "未知 / 未知"),
            "Developer visibility assigned undisclosed allies or revealed portrait slots.");
        Require(State(vm) == before, "Portrait projection or developer toggling changed the game.");
        DeadSeatComponent(vm, output);
        RevealControls(vm, output);
    }

    private static void DeadSeatComponent(MainViewModel vm, string output)
    {
        // These clones exercise only the real template's dead-state layout;
        // no death command, Core HP change or general reveal is simulated.
        var game = Program.Engine(vm);
        var before = State(vm);
        var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var developer = vm.IsDeveloperView;
        vm.IsDeveloperView = false;
        var playerView = game.CreateSnapshot(0);
        var own = playerView.Players.Single(player => player.IsHuman);
        var hidden = playerView.Players.First(player => !player.IsHuman && !player.IsGeneralPublic && !player.IsSecondaryGeneralPublic);
        var privatePlayer = game.CreateSnapshot(0, true).Players.Single(player => player.Seat == hidden.Seat);
        var root = new Canvas { Background = new SolidColorBrush(Color.FromRgb(15, 25, 30)) };
        var window = new MainWindow(vm);
        try
        {
            var template = (DataTemplate)window.FindResource("SeatTemplate");
            window.Content = root;
            var hosts = new List<(ContentControl Host, SeatViewModel Seat)>();
            foreach (var player in new[] { own, hidden })
            {
                var source = vm.Seats.Single(seat => seat.Seat == player.Seat);
                var dead = new SeatViewModel
                {
                    Seat = player.Seat, Name = source.Name, Kingdom = source.Kingdom, RoleLabel = source.RoleLabel,
                    HpText = source.HpText, HandText = source.HandText, EquipmentText = source.EquipmentText,
                    JudgmentText = source.JudgmentText, DistanceText = source.DistanceText, SkillText = source.SkillText,
                    IsAlive = false, IsCurrent = false, IsHuman = player.IsHuman,
                    GeneralId = player.GeneralId, GeneralName = player.GeneralName, Portrait = source.Portrait,
                    IsNationalSeat = true, DisplaySeatNumber = source.DisplaySeatNumber,
                    PrimaryGeneral = GeneralSlotViewModel.FromPlayer(player, false),
                    SecondaryGeneral = GeneralSlotViewModel.FromPlayer(player, true),
                    RelationshipLabel = source.RelationshipLabel, SkillName = source.SkillName,
                    Hp = 0, MaxHp = player.MaxHp, HandCount = player.HandCount
                };
                var host = new ContentControl { Content = dead, ContentTemplate = template };
                Canvas.SetLeft(host, 50 + hosts.Count * 210); Canvas.SetTop(host, 80);
                root.Children.Add(host); hosts.Add((host, dead));
            }
            foreach (var width in new[] { 1120, 1920 })
            {
                Program.Render(root, width, width == 1120 ? 740 : 1080,
                    Path.Combine(output, $"national-dead-seat-component-{width}.png"));
                foreach (var (host, seat) in hosts)
                {
                    var button = Program.Find<Button>(host).Single(control => control.Name == "SeatButton");
                    var shade = Program.Find<Border>(button).Single(border => border.Name == "DeathShade");
                    var names = Program.Find<TextBlock>(button).Where(text => text.Name == "SlotGeneralName").ToArray();
                    var states = Program.Find<TextBlock>(button).Where(text => text.Name == "SlotRevealState").ToArray();
                    var meter = Program.Find<HealthMeter>(button).Single(control => control.Name == "SeatHealthMeter");
                    var role = Program.Find<TextBlock>(button).Single(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == "RoleBadgeText");
                    Require(button.ActualWidth == 154 && button.ActualHeight == 204 &&
                        shade.Visibility == Visibility.Visible && !shade.IsHitTestVisible && names.Length == 2 && states.Length == 2,
                        "The national dead-state component must use both real slots and the actual passive 154x204 seat template.");
                    var badge = Bounds(shade);
                    var readouts = names.Concat(states).Cast<FrameworkElement>().Concat(new FrameworkElement[] { meter, role });
                    Require(badge.Width > 0 && badge.Height > 0 && Bounds(button).Contains(badge) &&
                        readouts.All(element => element.Visibility == Visibility.Visible && element.ActualWidth > 0 && element.ActualHeight > 0 &&
                            Bounds(button).Contains(Bounds(element)) && !badge.IntersectsWith(Bounds(element))),
                        "The national death badge overlapped/cropped a public slot name, reveal state, HP meter or relationship badge.");
                    Require(meter.Value == 0 && meter.Maximum == seat.MaxHp && role.Text == seat.RoleBadgeText &&
                        names.All(text => text.Text == ((GeneralSlotViewModel)text.DataContext).Name) &&
                        states.All(text => text.Text == ((GeneralSlotViewModel)text.DataContext).SlotState),
                        "Dead-state presentation changed a published national slot or detached its public HP/relationship readout.");
                    if (!seat.IsHuman)
                    {
                        var slots = new[] { seat.PrimaryGeneral!, seat.SecondaryGeneral! };
                        Require(slots.All(slot => !slot.IsKnown && !slot.HasPortrait && slot.GeneralId.Length == 0 && slot.Name == "暗将" &&
                            !slot.DetailText.Contains(privatePlayer.GeneralName) && !slot.DetailText.Contains(privatePlayer.SecondaryGeneralName!)) &&
                            Program.Find<TextBlock>(button).Count(text => text.Name == "HiddenSlotGlyph" && text.Visibility == Visibility.Visible) == 2 &&
                            names.All(text => text.Text == "暗将") &&
                            Program.Find<Rectangle>(button).Where(rectangle => rectangle.DataContext is GeneralSlotViewModel &&
                                BindingOperations.GetBinding(rectangle, Shape.FillProperty)?.Path?.Path == "Portrait.Brush") is var portraits &&
                            portraits.Count() == 2 && portraits.All(rectangle =>
                                ReferenceEquals(rectangle.Fill, ((GeneralSlotViewModel)rectangle.DataContext).Portrait.Brush)),
                            "Setting only the component's dead state revealed an undisclosed portrait, slot name or tooltip.");
                    }
                    Rect Bounds(FrameworkElement element) => element.TransformToVisual(root).TransformBounds(new Rect(element.RenderSize));
                }
            }
        }
        finally
        {
            window.Content = null; window.Close(); vm.IsDeveloperView = developer;
        }
        Require(State(vm) == before && Enumerable.Range(0, 4).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))) &&
            vm.Seats.All(seat => seat.IsAlive), "The dead-state component changed the live game or any player-safe snapshot.");
        Console.WriteLine("National dead-seat component: own/hidden public slots, passive badge clear of names/state/HP/role at 1120/1920; no Core death or reveal.");
    }

    private static void RevealControls(MainViewModel vm, string output)
    {
        vm.ContinueFromIdentityRevealCommand.Execute(null);
        Require(!vm.IsIdentityRevealOpen && !vm.IsOpeningDealVisible && !vm.IsGeneralSelectionPending,
            "The real reveal controls must be reached after the startup overlays.");
        vm.SaveGameCommand.Execute(null);
        Require(vm.HasManualSave && !vm.HasSaveError, vm.SaveStatus);
        var originalGame = Program.Engine(vm);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 740, Path.Combine(output, "national-reveal-before-reload.png"));
            var originalButton = ButtonFor(GeneralSelectionSlot.Secondary);
            var retired = (NationalRevealChoice)originalButton.CommandParameter;
            var command = originalButton.Command ?? throw new InvalidOperationException("The reveal button has no command.");
            Require(originalButton.IsEnabled && originalButton.Visibility == Visibility.Visible && originalButton.ActualHeight > 0,
                "The secondary reveal parameter must come from a visible enabled button.");
            vm.LoadManualGameCommand.Execute(null);
            var game = Program.Engine(vm);
            var current = vm.NationalRevealChoices.Single(choice => choice.Slot == retired.Slot);
            Require(!vm.HasSaveError && !ReferenceEquals(game, originalGame) && current == retired && !ReferenceEquals(current, retired),
                "A real reload must retire the old reveal parameter even when its slot and description are equal.");
            var card = vm.Hand.First(card => card.IsPlayable);
            vm.SelectCardCommand.Execute(card);
            if (vm.Seats.FirstOrDefault(seat => seat.IsLegalTarget) is { } target) vm.SelectTargetCommand.Execute(target);
            var selectedTargets = vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
            Require(vm.HasSelection && card.IsSelected && selectedTargets.Length > 0,
                "The reveal regression must protect a current private hand and nonempty target draft.");
            Reject(retired, "A retired reveal button revealed the loaded general and cleared the current hand draft.");
            Reject(current with { }, "An equal copied reveal choice must not execute the current native reveal.");

            Program.Render(root, 1120, 740, Path.Combine(output, "national-reveal-loaded-draft.png"));
            var unchangedButton = ButtonFor(current.Slot);
            var unchangedChoices = vm.NationalRevealChoices.ToArray();
            var changes = 0;
            vm.NationalRevealChoices.CollectionChanged += (_, _) => changes++;
            vm.IsDeveloperView = false;
            Program.Render(root, 1120, 740, Path.Combine(output, "national-reveal-player-view.png"));
            vm.IsDeveloperView = true;
            Program.Render(root, 1120, 740, Path.Combine(output, "national-reveal-developer-view.png"));
            Require(changes == 0 && vm.NationalRevealChoices.SequenceEqual(unchangedChoices, ReferenceEqualityComparer.Instance) &&
                ReferenceEquals(ButtonFor(current.Slot), unchangedButton) && card.IsSelected &&
                vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(selectedTargets),
                "Same-revision view refreshes must preserve reveal parameters, button containers and the hand/target draft.");

            vm.StartTutorialCommand.Execute(null);
            Program.Render(root, 1120, 740, Path.Combine(output, "national-reveal-tutorial.png"));
            Require(vm.IsTutorialActive && vm.NationalRevealChoices.Count == 0,
                "The tutorial must withdraw the suspended game's reveal controls.");
            vm.ExitTutorialCommand.Execute(null);
            Program.Render(root, 1120, 740, Path.Combine(output, "national-reveal-after-tutorial.png"));
            Require(!vm.IsTutorialActive && ReferenceEquals(Program.Engine(vm), game) &&
                vm.NationalRevealChoices.Count == 2 && vm.Hand.Single(item => item.Id == card.Id).IsSelected,
                "Returning to the original revision must restore both reveal controls and the private hand draft.");
            Require(vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(selectedTargets),
                "Returning from the tutorial must restore the exact nonempty target draft alongside its selected hand card.");
            Reject(current, "A pre-tutorial reveal choice must not replace the resumed game's current button.");

            foreach (var slot in new[] { GeneralSelectionSlot.Secondary, GeneralSelectionSlot.Primary })
            {
                var pendingChoices = vm.NationalRevealChoices.ToArray();
                var beforeRevision = game.Revision;
                var beforeCommands = game.AcceptedCommands.Count;
                var beforeEvents = game.Events.Count;
                var beforeMoves = game.CardMovements.Count;
                var prompt = game.PendingDecision!.PromptId;
                var button = ButtonFor(slot);
                var parameter = (NationalRevealChoice)button.CommandParameter;
                Require(vm.NationalRevealChoices.Any(choice => ReferenceEquals(choice, parameter)),
                    "The current reveal button must bind to the currently published parameter.");
                button.Command!.Execute(parameter);
                Require(game.Revision == beforeRevision + 1 && game.AcceptedCommands.Count == beforeCommands + 1 &&
                    game.AcceptedCommands.Last() is RevealGeneralCommand submitted && submitted.ActorSeat == 0 &&
                    submitted.Slot == slot && submitted.PromptId == prompt && submitted.ExpectedRevision == beforeRevision &&
                    game.CardMovements.Count == beforeMoves && !vm.HasSelection &&
                    game.Events.Skip(beforeEvents).Count(envelope => envelope.Payload is NationalGeneralRevealedEvent reveal &&
                        reveal.Seat == 0 && reveal.Slot == slot) == 1,
                    "A current reveal button must submit exactly its own native slot once and clear the completed draft.");
                var own = game.CreateSnapshot(0).Players[0];
                var foreign = game.CreateSnapshot(1).Players[0];
                Require(own.IsSecondaryGeneralPublic && foreign.IsSecondaryGeneralPublic &&
                    own.IsGeneralPublic == (slot == GeneralSelectionSlot.Primary) &&
                    foreign.IsGeneralPublic == own.IsGeneralPublic &&
                    (own.IsGeneralPublic || foreign.GeneralId.Length == 0),
                    "Revealing the secondary slot must leave the primary private until its own button is confirmed.");
                // A reveal leaves the native human prompt before it publishes the
                // next Play boundary. Resume that flow before testing the next button.
                for (var step = 0; step < 8 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                {
                    Require(vm.CanStepAi, "The native reveal must expose its own continuation before returning to Play.");
                    vm.StepAiCommand.Execute(null);
                }
                Require(game.PendingDecision?.Kind == DecisionKind.PlayCard && vm.CanEndTurn,
                    "The native reveal must return to the human Play boundary without ending the turn.");
                foreach (var old in pendingChoices) Reject(old, "A retired reveal choice must not reveal a remaining slot after another slot commits.");
                Program.Render(root, 1120, 740, Path.Combine(output, $"national-reveal-{slot}.png"));
            }
            Require(vm.NationalRevealChoices.Count == 0, "Revealing both slots must withdraw both controls.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), game.ContentRegistry!);
            Require(Enumerable.Range(0, 4).All(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer)) ==
                    SnapshotJson.Serialize(restored.CreateSnapshot(viewer))) &&
                JsonSerializer.Serialize(game.AcceptedCommands) == JsonSerializer.Serialize(restored.AcceptedCommands) &&
                JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(restored.Events) && game.CardMovements.SequenceEqual(restored.CardMovements),
                "The two UI reveals must cold-replay their private views, exact commands, events and physical movements.");

            Button ButtonFor(GeneralSelectionSlot slot) => Program.Find<Button>((FrameworkElement)window.FindName("NationalRevealButtons"))
                .Single(button => button.CommandParameter is NationalRevealChoice choice && choice.Slot == slot);

            void Reject(NationalRevealChoice parameter, string message)
            {
                Require(!vm.NationalRevealChoices.Any(choice => ReferenceEquals(choice, parameter)),
                    "The rejected reveal parameter must no longer be published.");
                var revision = game.Revision;
                var commands = game.AcceptedCommands.Count;
                var moves = game.CardMovements.Count;
                var state = State(vm);
                var selected = vm.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToArray();
                var targets = vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
                var text = vm.SelectedCardText;
                var prompt = vm.PromptText;
                var hasSelection = vm.HasSelection;
                command.Execute(parameter);
                Require(game.Revision == revision && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves &&
                    State(vm) == state && vm.Hand.Where(card => card.IsSelected).Select(card => card.Id).SequenceEqual(selected) &&
                    vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(targets) &&
                    vm.SelectedCardText == text && vm.PromptText == prompt && vm.HasSelection == hasSelection, message);
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

}
