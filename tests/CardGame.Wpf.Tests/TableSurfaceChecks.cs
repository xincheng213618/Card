using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
