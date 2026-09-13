using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PlayerGuideChecks
{
    public static HashSet<string> ObservedPrompts { get; } = [];
    private static string State(MainViewModel vm) => SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0, true));
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
    private static bool Shortcut(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.None) =>
        (bool)typeof(MainWindow).GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [key, modifiers])!;
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement => Program.Find<T>(root).Single(item => item.Name == name);

    public static void ControlsAndSearch(string output)
    {
        using var vm = new MainViewModel(false, 721019, showSetup: true, saveStore: new MemorySaveStore());
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var beforeSetup = State(vm);
        Require(Shortcut(window, Key.F1) && vm.IsHelpOpen && vm.IsNewGameSetupOpen, "F1 must open guidance over setup.");
        Program.Render(root, 1120, 740, Path.Combine(output, "20-guide-setup.png"));
        Require(!Named<Grid>(root, "TableSurface").IsEnabled && !Named<Border>(root, "NewGameSetupPanel").IsEnabled, "Guide must isolate both setup and table input.");
        Require(vm.CurrentGuideTitle.Contains("战场") && Named<Button>(root, "CloseGuideButton").IsEnabled, "Setup context or usable modal close is missing.");
        Require(KeyboardNavigation.GetTabNavigation(Program.Find<PlayerGuidePanel>(root).Single()) == KeyboardNavigationMode.Cycle, "Guide keyboard navigation should stay in its modal.");
        Require(Shortcut(window, Key.Escape) && !vm.IsHelpOpen && vm.IsNewGameSetupOpen, "Esc must close the top guide before setup.");
        Require(State(vm) == beforeSetup, "Opening help changed the not-yet-selected game.");
        vm.StartNewGameCommand.Execute(null);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var slash = vm.Hand.First(card => card.Name == "杀" && card.IsPlayable);
        vm.SelectCardCommand.Execute(slash);
        vm.SelectTargetCommand.Execute(vm.Seats.First(seat => seat.IsLegalTarget));
        Require(vm.CanConfirmSelected, "Fixture needs a real selected attack.");
        var before = State(vm);
        var beforeRevision = Program.Engine(vm).Revision;
        var selectedTarget = vm.Seats.Single(seat => seat.IsSelectedTarget).Seat;
        vm.OpenContextGuideCommand.Execute(null);
        Program.Render(root, 1440, 860, Path.Combine(output, "21-guide-current.png"));
        Require(vm.CurrentGuideTitle.Contains("确认") && vm.GuideHand.Count == vm.Hand.Count, "Guide must describe the current selection and only your hand.");
        var greyPeach = vm.Hand.First(card => card.Name == "桃" && !card.IsPlayable);
        Require(greyPeach.CardHint.Contains("体力已满"), "Grey Peach tooltip must explain the actual restriction.");
        Require(!Shortcut(window, Key.Enter) && !Shortcut(window, Key.Enter, ModifierKeys.Control), "Guide must block game shortcuts.");
        var peachLink = Program.Find<Button>(root).First(button => button.DataContext is GuideHandEntry { Name: "桃" });
        Require(peachLink.Command is not null && peachLink.Command.CanExecute(peachLink.CommandParameter), "Hand reference button must be usable.");
        peachLink.Command!.Execute(peachLink.CommandParameter);
        PumpBinding();
        Require(vm.IsGuideCards && vm.SelectedGuideCard?.Kind == CardKind.Peach, "Clicking a hand reference must open that card's rules.");

        var nav = Named<ListBox>(root, "GuideNavigation");
        nav.SelectedItem = vm.GuideSections.Single(section => section.Key == "cards");
        var search = Named<TextBox>(root, "GuideSearchBox");
        search.Text = "无懈";
        PumpBinding();
        Require(vm.IsGuideCards && vm.FilteredGuideCards.Select(card => card.Kind).ToHashSet().SetEquals([CardKind.Nullification, CardKind.IronChain]) &&
            vm.SelectedGuideCard?.Kind == CardKind.Nullification, "Search must match both the card name and Iron Chain's current non-nullifiable recast description.");
        Program.Render(root, 1120, 740, Path.Combine(output, "22-guide-cards.png"));
        Require(Named<TextBlock>(root, "GuideCardDescription").Text == CardCatalog.Get(CardKind.Nullification).Description, "Card rules must come from the actual content definition.");
        Named<ComboBox>(root, "GuideCategoryBox").SelectedItem = "基本牌";
        PumpBinding();
        Require(vm.HasNoGuideCards && vm.SelectedGuideCard is null, "Nonmatching category and search need an empty state.");
        Program.Render(root, 1120, 740, Path.Combine(output, "23-guide-empty-search.png"));
        vm.ClearGuideSearchCommand.Execute(null);
        var allCards = vm.FilteredGuideCards.ToArray();
        Require(allCards.Length == CardCatalog.ImplementedCards.Count, "Encyclopedia omitted implemented cards.");
        foreach (var category in vm.GuideCategories.Skip(1))
        {
            Named<ComboBox>(root, "GuideCategoryBox").SelectedItem = category;
            PumpBinding();
            Require(vm.FilteredGuideCards.Count > 0 && vm.FilteredGuideCards.All(card => card.Category == category), $"Empty or inaccurate category: {category}.");
        }
        vm.ClearGuideSearchCommand.Execute(null);
        var cardList = Named<ListBox>(root, "GuideCardList");
        foreach (var card in allCards)
        {
            cardList.SelectedItem = card;
            PumpBinding();
            if (card.Kind == CardKind.IronChain)
            {
                var text = Named<TextBlock>(root, "GuideCardDescription").Text;
                Require(text == card.Description && text.Contains("可包含自己") && text.Contains("并摸一张牌"), "Current Iron Chain rules are not bound to the description control.");
                continue;
            }
            var expectedDescription = card.Kind switch
            {
                CardKind.Dismantlement => "选择一名其他角色；从其手牌的不透明牌位中选择一张弃置，或弃置其一张公开装备/判定区牌。",
                CardKind.Snatch => "选择一名距离为 1 的其他角色；从其手牌的不透明牌位中选择一张获得，或获得其一张公开装备/判定区牌。",
                _ => CardCatalog.Get(card.Kind).Description
            };
            Require(Named<TextBlock>(root, "GuideCardDescription").Text == expectedDescription, $"Wrong definition displayed for {card.Name}.");
        }
        cardList.SelectedItem = allCards.Single(card => card.Kind == CardKind.Slash);
        Program.Render(root, 1120, 740, Path.Combine(output, "26-guide-all-cards.png"));
        foreach (var section in vm.GuideSections.Where(section => section.Key is "basics" or "keys"))
        {
            nav.SelectedItem = section;
            Program.Render(root, 1120, 740, Path.Combine(output, $"25-guide-{section.Key}.png"));
        }
        Require(!Shortcut(window, Key.D1), "Typing in guide must not select a game card.");
        Require(State(vm) == before && slash.IsSelected && vm.Seats.Single(seat => seat.IsSelectedTarget).Seat == selectedTarget, "Guide browsing changed the game or selection.");
        Require(Shortcut(window, Key.Escape) && !vm.IsHelpOpen && vm.CanConfirmSelected, "Closing guide must restore the selected attack.");
        Require(Shortcut(window, Key.Enter) && Program.Engine(vm).Revision > beforeRevision, "The preserved attack must still submit after closing guidance.");

        for (var turn = 0; turn < 16 && !vm.HasChoicePrompt && !vm.HasGameOver; turn++)
        {
            if (vm.IsDiscardSelectionPending) Program.ResolveDiscard(vm);
            if (vm.CanEndTurn) vm.EndTurnCommand.Execute(null);
            Program.AdvanceToDecision(vm);
        }
        Require(vm.HasChoicePrompt, "Fixture did not reach an actual human response.");
        vm.OpenContextGuideCommand.Execute(null);
        Require(vm.CurrentGuideBody == Program.Engine(vm).PendingDecision!.Prompt, "Response guide must refer to the actual private prompt.");
        Program.Render(root, 1120, 740, Path.Combine(output, "24-guide-response.png"));
        window.Content = null;
        window.Close();
    }

    public static void ModalLifecycle()
    {
        using var vm = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore());
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Require(vm.CanStepAi, "Timer fixture needs an actual engine continuation.");
        vm.IsAutoAdvance = true;
        vm.OpenContextGuideCommand.Execute(null);
        var paused = State(vm);
        Pump(TimeSpan.FromMilliseconds(1400));
        Require(State(vm) == paused && vm.IsAutoAdvance, "Guide changed state while its original auto timer was enabled.");
        vm.ToggleHelpCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Require(State(vm) != paused && vm.IsAutoAdvance, "Closing guide failed to resume the original auto policy.");
        vm.IsAutoAdvance = false;
        var manuallyPaused = State(vm);
        vm.OpenContextGuideCommand.Execute(null);
        vm.ToggleHelpCommand.Execute(null);
        Pump(TimeSpan.FromMilliseconds(750));
        Require(State(vm) == manuallyPaused && !vm.IsAutoAdvance, "Guide silently enabled an intentionally paused timer.");
    }

    public static void CheckCurrentState(MainViewModel vm)
    {
        DecisionContextChecks.VerifyLive(vm);
        var engine = Program.Engine(vm);
        var label = vm.HasGameOver ? "Completed" :
            vm.IsGeneralSelectionPending || vm.IsDiscardSelectionPending || vm.CanEndTurn || vm.HasChoicePrompt
                ? engine.PendingDecision?.Kind.ToString() : null;
        if (label is null || !ObservedPrompts.Add(label)) return;
        var before = State(vm);
        vm.OpenContextGuideCommand.Execute(null);
        Require(vm.IsGuideCurrent && !string.IsNullOrWhiteSpace(vm.CurrentGuideTitle) && vm.CurrentGuideSteps.Count > 0, $"Missing live guidance: {label}.");
        if (vm.HasChoicePrompt) Require(vm.CurrentGuideBody == engine.PendingDecision!.Prompt, $"Incorrect live response description: {label}.");
        if (vm.IsDiscardSelectionPending) Require(vm.CurrentGuideBody.Contains($"{vm.RequiredDiscardCount} 张"), "Discard count missing from current guidance.");
        vm.ToggleHelpCommand.Execute(null);
        Require(State(vm) == before, $"Looking at {label} guidance changed the match.");
    }

    private static void PumpBinding() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
