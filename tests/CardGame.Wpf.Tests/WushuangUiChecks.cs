using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class WushuangUiChecks
{
    public static void MoLuBuOpeningAndNormalInitiativeAreVisible(string output)
    {
        var registry = StandardWushuangContentRegistry.Create();
        foreach (var seed in new[] { 17, 1 })
        {
            var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(seed) with
                { AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false }, registry);
            Accept(game, new StartGameCommand());
            for (var step = 0; game.PendingDecision?.Kind != DecisionKind.SelectGeneral && step < 40; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, game.PendingDecision!.PromptId));
            Accept(game, new SelectGeneralCommand(0, "ol:mo-lu-bu", game.Revision, game.PendingDecision!.PromptId));
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var vm = Create(new MemorySaveStore(), store, registry);
            vm.LoadWushuangManualGameCommand.Execute(null);
            Drive(vm, () => vm.IsWushuangOpening && vm.IsWushuangShopInteractive);
            var firstSeat = Program.Engine(vm).CreateWushuangSnapshot(0)!.FirstNormalSeat;
            vm.BuyWushuangOfferCommand.Execute(vm.WushuangOffers.First());
            Drive(vm, () => Program.Engine(vm).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            Program.Assert(vm.RoundText.Contains("夺炁") && vm.RoundText.Contains("额外出牌") && vm.TurnHeadline.Contains("夺炁额外出牌阶段") &&
                vm.Seats.Single(s => s.Seat == firstSeat).SeatLabel.Contains("先手") &&
                vm.Seats.Single(s => s.Seat != firstSeat).SeatLabel.Contains("后手"),
                "The opening names Duoqi explicitly and the seat labels show actual normal initiative even while Lu Bu temporarily acts first.");
            var window = new MainWindow(vm);
            try
            {
                Program.Render((FrameworkElement)window.Content, 1440, 880, Path.Combine(output, $"mo-lu-bu-opening-{firstSeat}.png"));
                vm.EndTurnCommand.Execute(null);
                Drive(vm, () => Program.Engine(vm).Events.Count(e => e.Payload is TurnStartedEvent) >= 2);
                Program.Assert(!vm.RoundText.Contains("夺炁") && !vm.TurnHeadline.Contains("夺炁额外出牌阶段") &&
                    vm.Seats.Single(s => s.Seat == firstSeat).SeatLabel.Contains("先手"),
                    "The phase indication expires at the actual normal return while the first actor label stays stable.");
            }
            finally { window.Close(); }
        }
    }

    public static void EntryShopAndIndependentColdRestore(string output)
    {
        foreach (var tactic in WushuangTactics.All)
        {
            var offer = new WushuangShopOfferViewModel("artwork", tactic.Name, tactic.Description, tactic.Cost, null, true, 0)
                { Kind = WushuangOfferKind.Tactic, TacticId = tactic.Id, Quality = tactic.Quality };
            Program.Assert(offer.Artwork is BitmapSource { IsFrozen: true, PixelWidth: > 0, PixelHeight: > 0 } &&
                ReferenceEquals(offer.Artwork, new WushuangTacticViewModel(tactic.Id).Icon),
                $"Tactic {tactic.Id} ({tactic.Name}) has a decoded local icon shared by shop offers and table badges.");
        }
        var ordinary = new MemorySaveStore();
        var wushuang = new MemorySaveStore();
        using var vm = Create(ordinary, wushuang);
        vm.SaveGameCommand.Execute(null);
        var ordinaryManual = GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint);
        var window = new MainWindow(vm); window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            Layout(root);
            var lobby = Program.Find<HomeLobby>(root).Single();
            var entry = (Button)lobby.FindName("WushuangBattleButton");
            Program.Assert(vm.IsNewGameSetupOpen && entry.Visibility == Visibility.Visible && ReferenceEquals(entry.Command, vm.OpenWushuangModeCommand),
                "The home lobby exposes a visible, real single-player duel entry.");
            entry.Command.Execute(null);
            Program.Assert(vm.IsWushuangModeSelection && vm.IsModeConfigurationOpen && !vm.IsIdentityModeSelection &&
                !vm.IsTeamModeSelection && vm.TeamModeSetupText.Contains("两个额外技能"),
                "The duel has its own setup with one AI, a time limit and two extra skills, without an incorrect team or identity picker.");
            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            Drive(vm, () => vm.IsGeneralSelectionPending);
            vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices.First());
            Drive(vm, () => Program.Engine(vm).CreateWushuangSnapshot(0)?.CanShop == true);
            var game = Program.Engine(vm);
            Program.Assert(game.CreateSnapshot(0).Players.Count == 2 && vm.TableModeText.Contains("1v1") &&
                vm.IdentityObjective.Contains("AI") && vm.WindowTitle.Contains("单骑无双") && vm.WushuangClockText.Contains(':') &&
                game.WushuangRemainingMilliseconds is > 840_000 and <= WushuangMode.TimeLimitMilliseconds,
                "The entry starts a real two-seat engine and the table identifies its single-player duel and fifteen-minute countdown.");
            Layout(root);
            var panel = Program.Find<WushuangShopPanel>(root).Single();
            Program.Assert(vm.IsWushuangShopOpen && panel.Visibility == Visibility.Visible && vm.WushuangOffers.Count == 3 && vm.IsWushuangOpening &&
                vm.WushuangSkillSlots.Count == 2 && vm.WushuangSkillSlots.All(slot => slot.SkillId is null),
                "The selected game automatically opens a free three-choice initial tactic panel.");
            Program.Render(root, 1440, 880, Path.Combine(output, "wushuang-opening.png"));
            var originalOffers = vm.WushuangOffers.ToArray();
            try
            {
                vm.WushuangOffers.Clear();
                foreach (var id in new[] { 2295, 2045, 2209 })
                {
                    var tactic = WushuangTactics.ById[id];
                    vm.WushuangOffers.Add(new("artwork-" + id, tactic.Name, tactic.Description, 0, null, true, game.Revision)
                        { Kind = WushuangOfferKind.Tactic, TacticId = id, Quality = tactic.Quality, IsOpening = true });
                }
                Layout(root);
                var artworkGrids = Program.Find<Grid>(panel).Where(grid => grid.Name == "WushuangTacticArtwork").ToArray();
                Program.Assert(artworkGrids.Length == vm.WushuangOffers.Count && artworkGrids.All(grid =>
                    ReferenceEquals(Program.Find<Image>(grid).Last().Source, ((WushuangShopOfferViewModel)grid.DataContext).Artwork)),
                    "The previously blank opening choices bind their actual tactic icon above the quality frame.");
                Program.Render(root, 1440, 880, Path.Combine(output, "wushuang-tactic-icons.png"));
            }
            finally
            {
                vm.WushuangOffers.Clear();
                foreach (var savedOffer in originalOffers) vm.WushuangOffers.Add(savedOffer);
            }
            var initialTactic = vm.WushuangOffers.First(offer => offer.TacticId is { } id && WushuangTactics.ById[id].SkillId is not null);
            vm.BuyWushuangOfferCommand.Execute(initialTactic);
            Program.Assert(!game.IsFaulted && !vm.IsWushuangShopOpen &&
                (vm.CanStepAi || game.PendingDecision is { } next && next.SkillPrompt?.SkillId != WushuangMode.ModeId),
                "Selecting the free tactic retires the modal and leaves either a runnable engine or a genuine next human decision.");
            Drive(vm, () => game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Layout(root);
            var tacticRail = (ScrollViewer)window.FindName("HumanWushuangTactics");
            var dock = (FrameworkElement)window.FindName("HandDock");
            var badge = vm.HumanPlayer!.WushuangTactics.Single();
            Program.Assert(badge.Id == initialTactic.TacticId && badge.Icon is not null && badge.Frame.IsFrozen &&
                vm.HumanSkillCards.All(skill => skill.ContentId != WushuangTactics.ById[badge.Id].SkillId) &&
                game.CreateSnapshot(0).Players[0].Skills!.Any(skill => skill.ContentId == WushuangTactics.ById[badge.Id].SkillId) &&
                vm.WushuangSkillSlots.All(slot => slot.SkillId is null),
                "A real passive tactic keeps its executable rule but renders as an art badge, never as a skill button or an extra skill slot.");
            Program.Assert(tacticRail.Visibility == Visibility.Visible && tacticRail.ActualHeight > 40 &&
                tacticRail.TranslatePoint(new Point(), root).X > root.ActualWidth / 2 &&
                tacticRail.TranslatePoint(new Point(0, tacticRail.ActualHeight), root).Y <= dock.TranslatePoint(new Point(), root).Y &&
                vm.Seats.Single(seat => !seat.IsHuman).WushuangTactics.Count > 0,
                "Human tactic badges sit at the right above the hand dock; the opponent has a separate public tactic rail.");
            var opposingRail = Program.Find<ScrollViewer>(root).Single(view => view.Name == "OpponentWushuangTactics" &&
                view.DataContext is SeatViewModel { IsHuman: false });
            Program.Assert(opposingRail.ActualWidth > 40 && opposingRail.ActualHeight > 40 &&
                opposingRail.TranslatePoint(new Point(), root).X > 0,
                "The opponent's rail has real layout outside the portrait rather than collapsing under a large positive margin.");
            Program.Render(root, 1440, 880, Path.Combine(output, "wushuang-tactics-table.png"));
            vm.OpenWushuangShopCommand.Execute(null); Layout(root);
            var previewIds = vm.WushuangOffers.Select(item => item.Id).ToArray();
            Program.Assert(vm.IsWushuangShopOpen && !vm.IsWushuangShopInteractive && previewIds.Length == 4 &&
                vm.WushuangOffers.All(item => item.IsReadOnly && !item.IsEnabled) && vm.WushuangPromptText == "暂时无法购买商品" &&
                !vm.RerollWushuangShopCommand.CanExecute(null) && !vm.ToggleWushuangShopLockCommand.CanExecute(null),
                "During play the actual upcoming page is visible, with every transaction disabled.");
            Program.Render(root, 1440, 880, Path.Combine(output, "wushuang-preview.png"));
            vm.CloseWushuangShopCommand.Execute(null);
            Drive(vm, () => Program.Engine(vm).CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping, Tokens: >= 2 });
            Program.Assert(vm.WushuangOffers.Select(item => item.Id).SequenceEqual(previewIds),
                "The next buying window uses the previewed goods without rolling a different page.");
            Drive(vm, () => HasAffordableSkillOffer(vm));
            var offer = vm.WushuangOffers.First(item => item.IsEnabled && item.SkillId is not null);
            var beforeTokens = game.CreateWushuangSnapshot(0)!.Tokens;
            vm.BuyWushuangOfferCommand.Execute(offer);
            Program.Assert(vm.IsWushuangShopOpen && game.CreateWushuangSnapshot(0)!.Tokens == beforeTokens - offer.Cost &&
                vm.WushuangSkillSlots.Count(slot => slot.SkillId is not null) == 1 &&
                game.CreateWushuangSnapshot(0)!.CanShop && !vm.BuyWushuangOfferCommand.CanExecute(offer) &&
                vm.WushuangOffers.Count == 4 && vm.WushuangOffers.Single(item => item.Id == offer.Id).IsPurchased,
                "One purchase pays once, keeps its sold card in position and leaves the shopping prompt open.");
            vm.ToggleWushuangShopLockCommand.Execute(null);
            Program.Assert(game.CreateWushuangSnapshot(0)!.IsLocked, "The visible lock commits to the native private shop state.");
            Program.Render(root, 1440, 880, Path.Combine(output, "wushuang-shop.png"));
            var sidebar = (FrameworkElement)window.FindName("TableSidebar");
            var shopEnd = panel.TranslatePoint(new Point(panel.ActualWidth, panel.ActualHeight), root);
            Program.Assert(shopEnd.X <= sidebar.TranslatePoint(new Point(), root).X &&
                shopEnd.Y <= dock.TranslatePoint(new Point(), root).Y &&
                ((FrameworkElement)window.FindName("ActionBar")).Opacity == 0,
                "The shop scales inside the battlefield, aligned above the hand dock and to the left of the log sidebar.");
            var priceSlots = Program.Find<Grid>(panel).Where(grid => grid.Name == "WushuangPriceSlot").ToArray();
            Program.Assert(priceSlots.Length == 4 &&
                priceSlots.Select(slot => slot.TranslatePoint(new Point(), panel).Y).Distinct().Count() == 1,
                "All four price and sold-state slots share one baseline, independent of offer type and description length.");
            var footerIcons = new[] { "WushuangEndIcon", "WushuangLockIcon", "WushuangRefreshIcon" }
                .Select(name => (FrameworkElement)panel.FindName(name)).ToArray();
            Program.Assert(footerIcons.Select(icon => icon.TranslatePoint(new Point(), panel).Y).Distinct().Count() == 1,
                "Ending, lock and refresh icons share one footer baseline; the token cost has its own row.");
            var refresh = (Button)panel.FindName("WushuangRerollButton");
            var locked = (Button)panel.FindName("WushuangLockButton");
            Program.Assert(refresh.TranslatePoint(new Point(), panel).Y > panel.ActualHeight - 130 &&
                locked.TranslatePoint(new Point(), panel).X < refresh.TranslatePoint(new Point(), panel).X &&
                vm.WushuangOffers.Where(item => item.Cost == 0 && !item.IsPurchased).All(item => item.PriceText == "免费" && !item.ShowsPriceIcon) &&
                vm.WushuangOffers.Where(item => !item.IsAffordable).All(item => item.PriceForeground == "#F04436"),
                "Footer controls sit at the lower right, free goods use a label without a token icon and unaffordable prices are red.");
            var prompt = game.PendingDecision!.PromptId;
            Field("_wushuangShopOpenedAt").SetValue(vm, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 31);
            Method("OnWushuangClockTick").Invoke(vm, [null, EventArgs.Empty]);
            Program.Assert(game.PendingDecision?.PromptId != prompt,
                "The twenty-second intermission timeout commits finish and continues the native game flow.");
            vm.SaveGameCommand.Execute(null);
            Program.Assert(wushuang.GetSavedAt(GameSaveSlot.Manual) is not null && vm.HasManualSave &&
                vm.ManualSaveText.Contains("单骑无双") &&
                GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint) == ordinaryManual,
                "The shared Save action routes to the duel's independent manual file and preserves the ordinary manual game.");
            var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
            using var cold = Create(ordinary, wushuang);
            cold.LoadWushuangManualGameCommand.Execute(null);
            Program.Assert(!cold.HasSaveError && cold.IsWushuangTable &&
                SnapshotJson.Serialize(Program.Engine(cold).CreateSnapshot(0)) == saved &&
                cold.HumanPlayer!.WushuangTactics.SequenceEqual(vm.HumanPlayer!.WushuangTactics) &&
                cold.HumanSkillCards.All(skill => skill.ContentId != WushuangTactics.ById[badge.Id].SkillId) &&
                cold.WushuangSkillSlots.Single(slot => slot.SkillId is not null).SkillId == offer.SkillId,
                "A new desktop configured with ordinary content cold-restores the exact duel journal, acquired skill and remaining time.");
            vm.SelectedTableMode = vm.TableModes.First(mode => mode.ModeId != WushuangMode.ModeId);
            vm.StartNewGameCommand.Execute(null);
            Program.Assert(!vm.IsWushuangTable && wushuang.GetSavedAt(GameSaveSlot.Automatic) is not null &&
                GameCheckpointJson.Serialize(ordinary.Read(GameSaveSlot.Manual).Checkpoint) == ordinaryManual,
                "Leaving the duel flushes its pending automatic save to its own store without replacing the ordinary manual save.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void FullSkillSlotsRequireVisibleReplacement(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new QuietFixture()]);
        var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(17) with
            { UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; game.PendingDecision?.Kind != DecisionKind.SelectGeneral && step < 100; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Accept(game, new SelectGeneralCommand(0, "fixture:wushuang-ui-human", game.Revision, game.PendingDecision!.PromptId));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = Create(new MemorySaveStore(), store, registry);
        vm.LoadWushuangManualGameCommand.Execute(null);
        Drive(vm, () => HasAffordableSkillOffer(vm));
        game = Program.Engine(vm);
        vm.OpenWushuangShopCommand.Execute(null);
        for (var slotNumber = 0; slotNumber < 2; slotNumber++)
        {
            var current = vm.WushuangOffers.First(item => item.SkillId is not null && item.IsEnabled);
            vm.BuyWushuangOfferCommand.Execute(current);
            vm.CloseWushuangShopCommand.Execute(null);
            Drive(vm, () => HasAffordableSkillOffer(vm));
        }
        Program.Assert(game.CreateWushuangSnapshot(0)!.ExtraSkillIds.Count == 2,
            "The bounded native fixture buys two real skills over consecutive shopping windows.");
        var replacement = vm.WushuangOffers.First(offer => offer.SkillId is not null && offer.IsEnabled);
        Program.Assert(replacement.RequiresReplacement, "A full rack marks the skill card before a replacement purchase is selected.");
        var shop = new WushuangShopPanel { DataContext = vm };
        Program.Render(shop, 1240, 650, Path.Combine(output, "wushuang-full-slots.png"));
        var before = game.CreateWushuangSnapshot(0)!;
        vm.BuyWushuangOfferCommand.Execute(replacement);
        Program.Assert(vm.IsWushuangReplacementPending && vm.WushuangReplacementTitle.Contains(replacement.Name) &&
            game.CreateWushuangSnapshot(0)!.Tokens == before.Tokens &&
            game.CreateWushuangSnapshot(0)!.ExtraSkillIds.SequenceEqual(before.ExtraSkillIds),
            "Selecting a third skill opens the explicit replacement UI before any payment or skill removal.");
        var window = new MainWindow(vm); window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            Layout(root);
            Program.Render(root, 1440, 880, Path.Combine(output, "wushuang-replacement.png"));
            var slot = vm.WushuangSkillSlots.First();
            vm.ReplaceWushuangSkillCommand.Execute(slot);
            Program.Assert(!vm.IsWushuangReplacementPending && game.CreateWushuangSnapshot(0)!.ExtraSkillIds.Count == 2 &&
                !game.CreateWushuangSnapshot(0)!.ExtraSkillIds.Contains(slot.SkillId!) &&
                game.CreateWushuangSnapshot(0)!.ExtraSkillIds.Contains(replacement.SkillId!) &&
                game.CreateWushuangSnapshot(0)!.Tokens == before.Tokens - replacement.Cost,
                "Confirming one visible owned slot replaces only that grant and pays exactly once, keeping the two-skill limit.");
            Program.Assert(TableShortcutChecks.Input(window, Key.Escape) && !vm.IsWushuangShopOpen,
                "Escape closes the modal shop and returns keyboard ownership to the normal table.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    public static void FreeRefreshAndExpandedSlotsFollowNativeSnapshot(string output)
    {
        foreach (var (seed, tactic) in new[] { (148, 2291), (57, 2293) })
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new QuietFixture()]);
            var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(seed) with
                { UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Accept(game, new StartGameCommand());
            for (var step = 0; game.PendingDecision?.Kind != DecisionKind.SelectGeneral && step < 40; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Accept(game, new SelectGeneralCommand(0, "fixture:wushuang-ui-human", game.Revision, game.PendingDecision!.PromptId));
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var vm = Create(new MemorySaveStore(), store, registry);
            vm.LoadWushuangManualGameCommand.Execute(null);
            Drive(vm, () => vm.IsWushuangOpening && vm.IsWushuangShopInteractive);
            vm.BuyWushuangOfferCommand.Execute(vm.WushuangOffers.Single(o => o.TacticId == tactic));
            Drive(vm, () => Program.Engine(vm).CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping });
            game = Program.Engine(vm);
            if (tactic == 2291)
            {
                var tokens = game.CreateWushuangSnapshot(0)!.Tokens;
                Program.Assert(vm.WushuangRerollPriceText == "免费" && !vm.WushuangRerollShowsToken && vm.WushuangRerollText.Contains('2'),
                    "A native free refresh grant updates the visible price, token icon and remaining-count tooltip.");
                vm.RerollWushuangShopCommand.Execute(null);
                vm.RerollWushuangShopCommand.Execute(null);
                Program.Assert(vm.WushuangRerollPriceText == "1" && vm.WushuangRerollShowsToken &&
                    game.CreateWushuangSnapshot(0)!.Tokens == tokens,
                    "After two native free commands the UI returns to the paid price without spending currency.");
            }
            else Program.Assert(vm.WushuangSkillSlots.Count == 3 && vm.WushuangSkillSlotsText.Contains("/3"),
                "The expanded native capacity exposes a real third selectable skill slot.");
        }
    }

    public static void MixedShopEquipmentShowsItsNativePurchase(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new QuietFixture()]);
        var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(17) with
            { UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; game.PendingDecision?.Kind != DecisionKind.SelectGeneral && step < 40; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Accept(game, new SelectGeneralCommand(0, "fixture:wushuang-ui-human", game.Revision, game.PendingDecision!.PromptId));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = Create(new MemorySaveStore(), store, registry);
        vm.LoadWushuangManualGameCommand.Execute(null);
        Drive(vm, () => vm.IsWushuangOpening && vm.IsWushuangShopInteractive);
        vm.BuyWushuangOfferCommand.Execute(vm.WushuangOffers.First());
        Drive(vm, () => Program.Engine(vm).CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping });
        var equipment = vm.WushuangOffers.FirstOrDefault(o => o.IsEquipment && o.IsEnabled);
        Program.Assert(vm.WushuangOffers.Count(o => o.Kind == WushuangOfferKind.Skill) == 2 &&
            vm.WushuangOffers.Count(o => o.Kind == WushuangOfferKind.Card) == 1,
            "The verified mixed page contains two skills and one card, proving there is no fixed type quota.");
        Program.Assert(equipment is { CardOfferId: not null, TypeText: "装备", Artwork: not null } &&
            equipment.DetailText.Contains("立即装备") && equipment.DetailText.Contains("销毁"),
            "The real random page exposes an enabled equipment card with its source identity, artwork and immediate-equip/destruction explanation.");
        var panel = new WushuangShopPanel { DataContext = vm };
        Program.Render(panel, 1240, 650, Path.Combine(output, "wushuang-mixed-equipment-shop.png"));
        Program.Assert(Program.Find<TextBlock>(panel).Any(text => text.Text == "装备"),
            "The actual offer template labels equipment rather than presenting it as an ordinary hand-card purchase.");
        vm.BuyWushuangOfferCommand.Execute(equipment);
        Program.Assert(!Program.Engine(vm).IsFaulted && Program.Engine(vm).CreateSnapshot(0).Players[0].Equipment
            .Any(card => card.Kind == equipment!.CardKind) && vm.WushuangOffers.Single(o => o.Id == equipment!.Id).IsPurchased,
            "The visible buy command equips the native card and retains its sold position.");
        Program.Assert(vm.WushuangOffers.Where(o => o.IsEquipment).All(o => o.CardKind is { } kind &&
            WushuangCatalogue.IsCardAllowed(kind) && EquipmentCatalog.Get(kind).Slot is EquipmentSlot.Weapon or EquipmentSlot.Armor),
            "The visible equipment shop contains basic weapons and armor, without treasures or mode-exclusive equipment.");
    }

    public static void ActiveTacticKeepsAnExecutableEntry(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new QuietFixture("standard:peach_garden")]);
        var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(31) with
            { UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; game.PendingDecision?.Kind != DecisionKind.SelectGeneral && step < 40; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Accept(game, new SelectGeneralCommand(0, "fixture:wushuang-ui-human", game.Revision, game.PendingDecision!.PromptId));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = Create(new MemorySaveStore(), store, registry);
        vm.LoadWushuangManualGameCommand.Execute(null);
        Drive(vm, () => vm.IsWushuangOpening && vm.IsWushuangShopInteractive);
        vm.BuyWushuangOfferCommand.Execute(vm.WushuangOffers.Single(o => o.TacticId == 2365));
        Drive(vm, () => Program.Engine(vm).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        game = Program.Engine(vm);
        var chip = vm.HumanSkillCards.Single(s => s.ContentId == "wushuang:tactic-2365");
        Program.Assert(chip.IsAvailable && vm.HumanPlayer!.WushuangTactics.Any(t => t.Id == 2365) &&
            game.CreateWushuangSnapshot(0)!.ExtraSkillIds.Count == 0,
            "An active tactic keeps its public badge and a real executable entry while consuming no extra skill slot.");
        vm.ActivateHumanSkillCommand.Execute(chip);
        Drive(vm, () => Program.Engine(vm).ResolutionStack.OfType<ProgramSkillFrame>().Any(f =>
            f.SkillId == "wushuang:tactic-2365" && f.OwnedCardSelection is not null));
        Program.Assert(game.PendingDecision?.SkillPrompt?.SkillId == "wushuang:tactic-2365",
            "Clicking the zero-input entry reaches its native physical-card selection before any actual recast payment.");
    }

    private static MainViewModel Create(IGameSaveStore ordinary, IGameSaveStore wushuang, ContentRegistry? registry = null) =>
        new(false, 17, true, ordinary, contentRegistry: registry, wushuangSaveStore: wushuang,
            shanheSaveStore: new MemoryShanheCampaignSaveStore())
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
    private static FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static bool HasAffordableSkillOffer(MainViewModel vm) =>
        Program.Engine(vm).CreateWushuangSnapshot(0) is { CanShop: true, Phase: WushuangShopPhase.Shopping } &&
        vm.WushuangOffers.Any(offer => offer.IsEnabled && !offer.IsPurchased && offer.SkillId is not null);
    private static MethodInfo Method(string name) => typeof(MainViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static void Layout(FrameworkElement root)
    { root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); }
    private static void Drive(MainViewModel vm, Func<bool> done)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return;
            var game = Program.Engine(vm);
            GameCommand command;
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard } play)
                command = new EndPlayPhaseCommand(0, game.Revision, play.PromptId);
            else if (game.PendingDecision is { Kind: DecisionKind.DiscardCards } discard)
                command = new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision);
            else if (game.PendingDecision is { } prompt && prompt.Kind != DecisionKind.SelectGeneral)
            {
                var choice = prompt.Choices.FirstOrDefault(item => item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    prompt.Choices.FirstOrDefault(item => item.Parameters.GetValueOrDefault("response") == "pass") ?? prompt.Choices.First();
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
            else command = new AdvanceOneStepCommand(game.Revision);
            var result = (CommandResult)Method("SubmitCommand").Invoke(vm, [command])!;
            Program.Assert(result.Accepted, result.Error?.Message ?? "The bounded duel UI fixture must reach its native boundary.");
        }
        throw new InvalidOperationException("The bounded duel UI fixture missed its native play boundary.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native duel fixture rejected its command."); }

    private sealed class QuietFixture(string cardId = "standard:dodge") : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("wushuang-ui-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var skill in StandardWushuangContentRegistry.Create().Skills.Values.Where(s =>
                s.Id.StartsWith("wushuang:", StringComparison.Ordinal) || s.Id.StartsWith("shanhe:", StringComparison.Ordinal) ||
                s.Id.StartsWith("hulao:", StringComparison.Ordinal) || s.Id.StartsWith("shenwu:", StringComparison.Ordinal) ||
                s.Id.StartsWith("ghost:", StringComparison.Ordinal))) builder.AddSkill(skill);
            builder.AddGeneral(new("fixture:wushuang-ui-human", "无双主将", "", "classic:qianxun", "qun", BaseHp: 20));
            builder.AddGeneral(new("fixture:wushuang-ui-ai", "无双对手", "", "classic:qianxun", "qun", BaseHp: 20));
            builder.AddDeck(new("fixture:wushuang-ui-deck", "Bounded native duel UI", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(_ => new ContentDeckPhysicalCard(cardId, Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(WushuangMode.ModeId, "无双界面验证", 2, 2, new Dictionary<string, int>(),
                "fixture:wushuang-ui-deck", 2, ["fixture:wushuang-ui-human", "fixture:wushuang-ui-ai"], ContentModeKind.Team,
                new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 }));
        }
    }
}
