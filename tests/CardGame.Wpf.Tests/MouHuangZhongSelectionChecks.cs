using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Content.Standard;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MouHuangZhongSelectionChecks
{
    public static void RelatedVariant(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new SelectionMode());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "identity:classic-mou-huang-zhong-ui", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The formal Huang Zhong selection fixture starts through a real command.");
        for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.SelectGeneral; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "The fixture reaches a real private selection prompt.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = new MainViewModel(autoAdvance: false, seed: 31, showSetup: true, saveStore: store,
            useExpandedContent: false, contentRegistry: registry) { IsMotionEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, Left = -30000, Top = -30000,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 1500, Height = 960 };
        try
        {
            window.Show();
            var root = (FrameworkElement)window.Content;
            var candidate = vm.GeneralChoices.Single(c => c.GeneralId is "classic:huang-zhong" or "boundary:huang-zhong" or "ol:shen-huang-zhong" or "mou:huang-zhong");
            vm.PreviewGeneralChoiceCommand.Execute(candidate);
            Program.Render(root, 1500, 960, Path.Combine(output, "mou-huang-zhong-versions.png"));
            var versions = vm.SelectedGeneralVariants.Select(c => c.GeneralId).ToHashSet();
            Program.Assert(versions.SetEquals(new[] { "classic:huang-zhong", "boundary:huang-zhong", "ol:shen-huang-zhong", "mou:huang-zhong" }),
                "The same-character panel exposes ordinary, boundary, God and Mou Huang Zhong.");
            var buttons = Program.Find<Button>((ItemsControl)window.FindName("GeneralVariantChoices"));
            var button = buttons.Single(b => b.DataContext is GeneralChoiceViewModel c && c.GeneralId == "mou:huang-zhong");
            var revision = Program.Engine(vm).Revision;
            button.Command!.Execute(button.CommandParameter);
            Program.Render(root, 1500, 960, Path.Combine(output, "mou-huang-zhong-selected.png"));
            Program.Assert(vm.GeneralChoices.Count == 3 && vm.SelectedGeneralChoice is { GeneralId: "mou:huang-zhong" } choice &&
                choice.SkillDescription.Contains("记录") && choice.SkillDescription.Contains("未装备武器") && Program.Engine(vm).Revision == revision,
                "Selecting the real Mou version preserves candidate slots and previews its actual skill without committing selection.");
            Program.Assert(vm.SelectedGeneralChoice!.HealthText.Contains("5"), "The real Lord selection previews four base HP plus the one-HP Lord bonus.");
            var portrait = new BitmapImage(new Uri("pack://application:,,,/CardGame.Wpf;component/Assets/official-mou-huang-zhong.jpg"));
            Program.Assert(portrait.PixelWidth == 530 && portrait.PixelHeight == 750, "The official mobile portrait is embedded and decodes.");
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            Program.Assert(Program.Engine(vm).CreateSnapshot(0).Players[0].GeneralId == "mou:huang-zhong",
                "The same-character Mou button confirms a real playable formal general.");
        }
        finally { window.Close(); }
    }

    private sealed class SelectionMode : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("mou-huang-zhong-ui-check", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b) => b.AddMode(new("identity:classic-mou-huang-zhong-ui", "谋黄忠选将", 5, 5,
            new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
            "classic:standard-deck", GeneralCandidateCount: 3, GeneralPoolIds:
            ["classic:huang-zhong", "boundary:huang-zhong", "ol:shen-huang-zhong", "mou:huang-zhong", "classic:cao-cao", "classic:ling-tong"])
        {
            GeneralVariantGroups = new Dictionary<string, IReadOnlyList<string>>
            {
                ["character:huang-zhong"] = ["classic:huang-zhong", "boundary:huang-zhong", "ol:shen-huang-zhong", "mou:huang-zhong"]
            }
        });
    }
}
