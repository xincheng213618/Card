using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GuanYuVariantUiChecks
{
    public static void Selection(string output)
    {
        var variants = Registry().Modes["identity:classic-8"].GeneralVariantGroups!["character:guan-yu"];
        var added = new[] { "mou:guan-yu-mobile", "mou:guan-yu-ol", "magic:guan-yu" };
        Program.Assert(added.All(variants.Contains), "The actual identity pool groups all three additions with Guan Yu.");
        var registry = Registry(new Mode(variants));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode.Id, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Start the Guan family selection.");
        for (var i = 0; i < 16 && game.PendingDecision?.Kind != DecisionKind.SelectGeneral; i++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Reach actual selection.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store, useExpandedContent: false, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        var engine = Program.Engine(model); var revision = engine.Revision;
        var window = new MainWindow(model) { Width = 1120, Height = 820, ShowActivated = false, ShowInTaskbar = false };
        var root = (FrameworkElement)window.Content;
        try
        {
            model.PreviewGeneralChoiceCommand.Execute(model.GeneralChoices.Single(c => variants.Contains(c.GeneralId)));
            Program.Render(root, 1120, 820, Path.Combine(output, "guan-variants-selection.png"));
            Program.Assert(model.SelectedGeneralVariants.Select(c => c.GeneralId).ToHashSet().SetEquals(variants),
                "The real same-person version row includes all Guan editions.");
            foreach (var id in added)
            {
                var control = (ItemsControl)window.FindName("GeneralVariantChoices");
                var button = Program.Find<Button>(control).Single(b => b.DataContext is GeneralChoiceViewModel c && c.GeneralId == id);
                Program.Assert(button.IsEnabled, "Each new edition has an enabled real button.");
                button.Command!.Execute(button.CommandParameter); root.UpdateLayout();
                Program.Assert(model.SelectedGeneralChoice!.GeneralId == id && engine.Revision == revision &&
                    model.SelectedGeneralChoice.PortraitBrush is ImageBrush { ImageSource: not null } &&
                    model.SelectedGeneralChoice.SkillDescription.Contains(registry.Skills[registry.Generals[id].SkillId].Name),
                    "Edition buttons update the correct artwork and skills without committing selection.");
                Program.Render(root, 1120, 820, Path.Combine(output, "guan-" + id.Split(':')[0] + "-" + registry.Generals[id].VariantId + ".png"));
            }
            Program.Assert(model.SelectedGeneralChoice!.SkillDescription.Contains("限定技"), "The magic edition displays the physical card's limited Weiwo.");
            model.ConfirmGeneralChoiceCommand.Execute(null);
            Program.Assert(engine.CreateSnapshot(0).Players[0].GeneralId == "magic:guan-yu", "Confirm selects the actual magic edition.");
        }
        finally { window.DataContext = null; window.Close(); }
    }

    private static ContentRegistry Registry(IGameContentPackage? extra = null)
    {
        IGameContentPackage[] packages = [new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()];
        return ContentRegistry.Build(extra is null ? packages : [.. packages, extra]);
    }
    private sealed class Mode(IReadOnlyList<string> variants) : IGameContentPackage
    {
        internal const string Id = "identity:classic-guan-variants-ui";
        public PackageManifest Manifest { get; } = new("guan-variants-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var blanks = Enumerable.Range(1, 3).Select(i => "fixture:guan-ui-" + i).ToArray();
            foreach (var id in blanks) b.AddGeneral(new(id, id, "supporter", "standard:none", "qun", 4));
            b.AddMode(new(Id, "Guan edition selection", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "classic:standard-deck", GeneralCandidateCount: 4, GeneralPoolIds: [.. variants, .. blanks]) {
                GeneralVariantGroups = new Dictionary<string, IReadOnlyList<string>> { ["character:guan-yu"] = variants }
            });
        }
    }
}
