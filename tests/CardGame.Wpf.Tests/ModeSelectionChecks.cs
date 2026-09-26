using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;

internal static class ModeSelectionChecks
{
    public static void RefreshBoundSelection(MainViewModel vm, ListBox choices)
    {
        var failures = new List<string>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is NullReferenceException &&
                args.Exception.StackTrace?.Contains("MainViewModel", StringComparison.Ordinal) == true)
                failures.Add(args.Exception.ToString());
        }

        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            var previous = vm.SelectedTableMode;
            // Exercise the real two-way binding, including exceptions WPF may catch.
            choices.SetCurrentValue(Selector.SelectedItemProperty, null);
            choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            Program.Assert(vm.SelectedTableMode == previous,
                "A temporary empty ListBox selection erased the configured mode.");

            vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == "identity");
            var fivePlayer = vm.VisibleTableModes.Single(mode => mode.ModeId == "identity:classic-5");
            choices.SetCurrentValue(Selector.SelectedItemProperty, fivePlayer);
            choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            foreach (var categoryId in new[] { "all", "identity", "all", "team", "national", "identity", "all" })
            {
                var retained = vm.SelectedTableMode;
                vm.SelectedModeCategory = vm.ModeCategories.Single(category => category.Id == categoryId);
                choices.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Program.Assert(vm.SelectedTableMode is not null && vm.VisibleTableModes.Contains(vm.SelectedTableMode) &&
                               Equals(choices.SelectedItem, vm.SelectedTableMode),
                    "Refreshing mode cards lost a valid selection or left the view and setup out of sync.");
                Program.Assert(!vm.VisibleTableModes.Contains(retained) || vm.SelectedTableMode == retained,
                    "Switching to a category containing the selected mode reset the user's choice.");
                Program.Assert(vm.GuideIdentity.Contains(vm.SelectedTableMode!.Name, StringComparison.Ordinal),
                    "The setup guide did not follow the selected mode.");
            }
            Program.Assert(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnException;
        }
    }
}
