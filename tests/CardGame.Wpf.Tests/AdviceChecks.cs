using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class AdviceChecks
{
    private static void Require(bool condition, string message) => Program.Assert(condition, message);
    private static string Checkpoint(MainViewModel vm) => GameCheckpointJson.Serialize(Program.Engine(vm).CreateCheckpoint());

    public static void ControlsAndPrivacy(string output)
    {
        using var vm = new MainViewModel(false, 721019, true, new MemorySaveStore());
        Require(!vm.CanRequestPlayAdvice, "An unopened game must not offer play advice.");
        vm.RequestPlayAdviceCommand.Execute(null);
        Require(!vm.HasPlayAdvice && !vm.IsHelpOpen, "Disabled advice changed setup.");
        vm.StartNewGameCommand.Execute(null);
        vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        Program.AdvanceToDecision(vm);
        var slash = vm.Hand.First(card => card.Name == "杀" && card.IsPlayable);
        vm.SelectCardCommand.Execute(slash);
        vm.SelectTargetCommand.Execute(vm.Seats.First(seat => seat.IsLegalTarget));
        var checkpoint = Checkpoint(vm);
        var target = vm.Seats.Single(seat => seat.IsSelectedTarget).Seat;
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        vm.OpenContextGuideCommand.Execute(null);
        Program.Render(root, 1120, 740, Path.Combine(output, "57-advice-entry.png"));
        var button = Program.Find<Button>(root).Single(button => button.Name == "PlayAdviceButton");
        Require(button.IsEnabled && button.ActualHeight > 0, "Current guidance has no usable advice entry.");
        button.Command.Execute(null);
        var advice = vm.CurrentPlayAdvice!;
        Require(advice is not null && Program.Engine(vm).GetHumanLegalActions().Any(action => action.Description == advice.Action), "Recommendation is not a published legal action.");
        Program.Render(root, 1120, 740, Path.Combine(output, "58-advice-current.png"));
        Require(Program.Find<TextBlock>(root).Single(text => text.Name == "PlayAdviceAction").Text == advice!.Action,
            "Advice is not bound to the visible control.");
        vm.RequestPlayAdviceCommand.Execute(null);
        Require(vm.CurrentPlayAdvice == advice && Checkpoint(vm) == checkpoint && slash.IsSelected && vm.Seats.Single(seat => seat.IsSelectedTarget).Seat == target,
            "Repeated advice mutated the engine, AI state, selection or tie-break result.");
        vm.IsDeveloperView = true;
        vm.RequestPlayAdviceCommand.Execute(null);
        Require(vm.CurrentPlayAdvice == advice && Checkpoint(vm) == checkpoint, "Developer visibility affected the recommendation.");
        var rejected = false;
        try { PlayAdvisor.Recommend(Program.Engine(vm).CreateSnapshot(0, true), Program.Engine(vm).GetHumanLegalActions(), []); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "The standalone advisor accepted a trusted/debug view.");
        vm.IsDeveloperView = false;
        vm.IsHelpOpen = false;
        var shortcut = typeof(MainWindow).GetMethod("HandleShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Require((bool)shortcut.Invoke(window, [Key.Enter, ModifierKeys.None])! && Checkpoint(vm) != checkpoint && !vm.HasPlayAdvice,
            "The original selection did not submit after closing help, or stale advice survived a committed action.");
        vm.SaveGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        Require(!vm.HasPlayAdvice, "Loading a new engine retained an old recommendation.");
        vm.StartTutorialCommand.Execute(null);
        Require(!vm.CanRequestPlayAdvice && !vm.HasPlayAdvice, "Free-form advice must not override fixed tutorial objectives.");
        window.Content = null;
        window.Close();

        // Real legal action lists cover physical cards, conversions, recasts and end-play formatting.
        var checkedKinds = new HashSet<LegalActionKind>();
        foreach (var seed in Enumerable.Range(171, 24).Concat([721019, 721020, 721021]))
        {
            using var candidate = new MainViewModel(false, seed, false, new MemorySaveStore(), useExpandedContent: true);
            candidate.SelectGeneralChoiceCommand.Execute(candidate.GeneralChoices[0]);
            Program.AdvanceToDecision(candidate);
            var engine = Program.Engine(candidate);
            var view = engine.CreateSnapshot(0);
            var before = Checkpoint(candidate);
            foreach (var action in engine.GetHumanLegalActions())
            {
                var entry = PlayAdvisor.Recommend(view, [action], []);
                Require(entry is not null && entry.Action == action.Description && entry.Reason.Length > 0, "A legal action has no understandable advice.");
                Require(!entry!.Reason.Contains("策略值") && !entry.Reason.Contains("目标敌对值"), "Player advice exposed internal scoring terms.");
                checkedKinds.Add(action.Kind);
            }
            Require(Checkpoint(candidate) == before, "Formatting advisory actions changed a match.");
        }
        Require(checkedKinds.Contains(LegalActionKind.EndPlay) && checkedKinds.Contains(LegalActionKind.Slash), "Advice fixtures did not cover normal play and ending a turn.");
        Console.WriteLine($"  Advice verified against {checkedKinds.Count} actual action kinds; private/debug boundary and unchanged command history passed.");
    }

    public static void VerifyLive(MainViewModel vm)
    {
        if (!vm.CanRequestPlayAdvice) return;
        var before = Checkpoint(vm);
        vm.RequestPlayAdviceCommand.Execute(null);
        Require(vm.CurrentPlayAdvice is { } advice && Program.Engine(vm).GetHumanLegalActions().Any(action => action.Description == advice.Action),
            "Live match advice is not legal at its decision boundary.");
        vm.IsHelpOpen = false;
        Require(Checkpoint(vm) == before, "Requesting advice changed live game or AI command history.");
    }
}
