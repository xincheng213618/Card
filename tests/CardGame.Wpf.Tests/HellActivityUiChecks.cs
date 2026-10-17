using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HellActivityUiChecks
{
    public static void SingleCardConversionSoulAppearsOnRailAndUsesAllTargets(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new SoulUiFixture(conversion: true));
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 5, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:allies",
            ModeId = SoulUiFixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 4
        }, registry);
        Accept(new StartGameCommand());
        Until(() => original.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(new SelectGeneralCommand(0, SoulUiFixture.Owner, original.Revision, original.PendingDecision!.PromptId));
        Play();
        Accept(new UseProgramSkillCommand(0, SoulUiFixture.Driver, "defeat", [], [3], original.Revision,
            original.PendingDecision!.PromptId));
        Play();
        var losses = original.Events.Select(entry => entry.Payload).OfType<HellWanghunGrantLostEvent>().ToArray();
        var initial = original.CreateSnapshot(0).Players[0].Hand;
        Program.Assert(losses.Length == 2 && losses.All(fact => fact.Grant.SkillId == SoulUiFixture.Lost) &&
            initial.Count == 7 && initial.All(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Club),
            "The reused real Wanghun fixture must create two actual lost grants while the Hell-mode human retains seven original black Dodge cards.");
        Accept(new UseProgramSkillCommand(0, SoulUiFixture.Driver, "prepare-single-conversion",
            initial.Skip(1).Select(card => card.Id).ToArray(), [], original.Revision, original.PendingDecision!.PromptId));
        Play();
        var action = original.GetHumanLegalActions().Single(action => action.Kind == LegalActionKind.ReturnSoul &&
            action.ConversionSource?.SkillId == "ol:miaoxian");
        var source = action.ConversionSource!;
        Program.Assert(action.TargetSeats.Count == 2 && original.CreateSnapshot(0).Players[0].Hand is [{ Kind: CardKind.Dodge }] &&
            registry.GetSkill(source.SkillId).Program!.ViewAs.All(rule => rule.Id != source.BindingId) &&
            original.GetCardConversionDefinition(source) is { OutputKind: CardKind.ReturnSoul, ForPlay: true, InputCount: 1 } definition &&
            definition.Id == source.BindingId,
            "The native conversion must use the true mode-derived ReturnSoul binding absent from the original registry, paid with the sole black Dodge entity.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false,
            original.CreateCheckpoint()));
        using var vm = new MainViewModel(false, 7, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            var game = Program.Engine(vm);
            var commands = game.AcceptedCommands.Count;
            var skill = vm.HumanSkillCards.Single(skill => skill.ContentId == "ol:miaoxian");
            Program.Assert(skill.IsAvailable && !skill.IsDisabled, "The actual Miaoxian rail chip must remain available with its unique black material.");
            Layout();
            Click(Bound(vm.ActivateHumanSkillCommand, skill));
            var choice = vm.EquipmentPlayChoices.Single(choice => choice.Parameters.GetValueOrDefault("played-kind") == CardKind.ReturnSoul.ToString());
            Click(Bound(vm.SelectEquipmentPlayChoiceCommand, choice));
            var material = vm.Hand.Single(card => card.Id == action.CardId);
            Click(Bound(vm.SelectCardCommand, material));
            var confirm = (Button)window.FindName("PlayCardButton");
            Program.Assert(material.Kind == CardKind.Dodge && material.IsSelected && !vm.CanPlaySelected &&
                vm.CanPlaySelectedAsSlash && vm.CanConfirmSelected && confirm.IsEnabled &&
                !vm.IsMultiTargetCardSelected && !vm.HasTargetCombinationChoices && !vm.Seats.Any(seat => seat.IsLegalTarget) &&
                vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(action.TargetSeats) &&
                vm.SelectedCardText.Contains("妙弦") && vm.SelectedCardText.Contains("回魂") &&
                vm.ActionHint.Contains("恢复 2 名角色") && vm.CurrentGuideTitle.Contains("回魂") &&
                game.AcceptedCommands.Count == commands,
                "The real rail-to-mode-to-material flow must offer derived ReturnSoul and enable direct converted confirmation with both automatic recipients, preserving the printed Dodge and reversible draft.");
            Program.Render(root, 1440, 880, Path.Combine(output, "169-hell-converted-return-soul.png"));
            Click(confirm);
            for (var step = 0; step < 80 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
                vm.StepAiCommand.Execute(null);
            var commandsAfter = game.AcceptedCommands.Skip(commands).OfType<PlayCardCommand>().ToArray();
            var converted = game.Events.Select(entry => entry.Payload).OfType<ProgramViewAsConvertedEvent>()
                .Single(fact => fact.SkillId == source.SkillId && fact.OutputKind == CardKind.ReturnSoul);
            var restored = game.Events.Select(entry => entry.Payload).OfType<HellSoulGrantRestoredEvent>().Where(fact => fact.Outcome == "restored").ToArray();
            Program.Assert(commandsAfter is [{ PlayedCardKind: CardKind.ReturnSoul } command] &&
                command.CardId == material.Id && command.ConversionSource == source && command.TargetSeats.SequenceEqual(action.TargetSeats) &&
                converted.BindingId == source.BindingId && converted.PhysicalCardIds.SequenceEqual([material.Id]) &&
                converted.TargetSeats.SequenceEqual(action.TargetSeats) && restored.Length == 2 &&
                restored.All(fact => losses.Any(loss => loss.FrameId == fact.LossFrameId &&
                    loss.TargetSeat == fact.TargetSeat && loss.Grant.GrantId == fact.GrantId)) &&
                game.CreateCardZoneDiagnostics().Single(entity => entity.CardId == material.Id) is
                    { CardKind: CardKind.Dodge, Location.Zone: CardZoneKind.DiscardPile } &&
                !game.Events.Any(entry => entry.Payload is HellReturnSoulDestroyedEvent) &&
                game.Events.Count(entry => entry.Payload is MiaoxianBlackConsumedEvent) == 1,
                "One actual converted confirmation must preserve exact derived provenance, restore every recipient and discard its genuine Dodge cost without destroying it as a physical Soul.");

            Button Bound(System.Windows.Input.ICommand command, object parameter) => Program.Find<Button>(root).Single(button =>
                ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
            void Layout()
            {
                root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
            }
            void Click(Button button)
            {
                Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter), "The real converted Soul button must be enabled before its routed click.");
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Layout();
            }
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void Accept(GameCommand command)
        {
            var result = original.Submit(command);
            Program.Assert(result.Accepted, result.Error?.Message ?? "Native converted Soul UI command rejected.");
        }
        void Until(Func<bool> ready)
        {
            for (var step = 0; step < 100; step++) { if (ready()) return; Accept(new AdvanceOneStepCommand(original.Revision)); }
            throw new InvalidOperationException("The native converted Soul fixture did not reach its bounded decision.");
        }
        void Play() => Until(() => original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    }

    public static void ClickPhysicalSoulRestoresAutomaticPublicTargets(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new SoulUiFixture());
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:allies",
            ModeId = SoulUiFixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 4
        }, registry);
        Accept(new StartGameCommand());
        Until(() => original.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(new SelectGeneralCommand(0, SoulUiFixture.Owner, original.Revision, original.PendingDecision!.PromptId));
        Play();
        Program.Assert(original.CreateCardZoneDiagnostics().All(card => card.Location != CardLocation.DrawPile),
            "The small native soul UI fixture must exhaust its twelve physical cards through actual initial dealing.");
        Accept(new UseProgramSkillCommand(0, SoulUiFixture.Driver, "defeat", [], [3], original.Revision,
            original.PendingDecision!.PromptId));
        Play();
        Accept(new UseProgramSkillCommand(0, SoulUiFixture.Driver, "draw", [], [], original.Revision,
            original.PendingDecision!.PromptId));
        Play();
        var losses = original.Events.Select(entry => entry.Payload).OfType<HellWanghunGrantLostEvent>().ToArray();
        Program.Assert(losses.Length == 2 && losses.All(fact => fact.Grant.SkillId == SoulUiFixture.Lost) &&
            original.Events.Count(entry => entry.Payload is HellReturnSoulInjectedEvent) == 2 &&
            original.CreateSnapshot(0).Players[0].Hand.Count(card => card.Kind == CardKind.ReturnSoul) == 2,
            "A real Mengpo death must lose two original skills and generate the actual two physical soul cards drawn for the UI test.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false,
            original.CreateCheckpoint()));
        using var vm = new MainViewModel(false, 7, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            var game = Program.Engine(vm);
            var action = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.ReturnSoul);
            var card = vm.Hand.Single(card => card.Id == action.CardId);
            var commands = game.AcceptedCommands.Count;
            Layout();
            Click(Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, vm.SelectCardCommand) &&
                ReferenceEquals(button.CommandParameter, card)));
            var confirm = (Button)window.FindName("PlayCardButton");
            Program.Assert(!vm.HasSaveError && card.IsSelected && vm.CanPlaySelected && vm.CanConfirmSelected && confirm.IsEnabled &&
                !vm.IsMultiTargetCardSelected && !vm.HasTargetCombinationChoices && !vm.HasPublicTargetChoices &&
                !vm.Seats.Any(seat => seat.IsLegalTarget) && vm.CurrentGuideTitle.Contains("回魂") &&
                vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToHashSet().SetEquals(action.TargetSeats) &&
                vm.ActionHint.Contains("恢复 2 名角色") && !vm.ActionHint.Contains("选择目标") &&
                game.AcceptedCommands.Count == commands &&
                typeof(MainViewModel).GetMethod("CanAimCardWithoutTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, [card]) is true,
                "One actual hand-card click must preview both public restoration targets and enable confirmation and open-table aim without any manual target selection or rule command.");
            foreach (var target in action.TargetSeats) vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == target));
            Program.Assert(vm.CanConfirmSelected && vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat)
                .ToHashSet().SetEquals(action.TargetSeats) && game.AcceptedCommands.Count == commands,
                "Clicking an automatically included recipient must not remove it or turn a global soul effect into a partial-target draft.");
            Program.Render(root, 1440, 880, Path.Combine(output, "168-hell-return-soul.png"));
            Click(confirm);
            for (var step = 0; step < 80 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
                vm.StepAiCommand.Execute(null);
            var played = game.AcceptedCommands.Skip(commands).OfType<PlayCardCommand>().ToArray();
            var restored = game.Events.Select(entry => entry.Payload).OfType<HellSoulGrantRestoredEvent>()
                .Where(fact => fact.Outcome == "restored").ToArray();
            Program.Assert(played is [{ CardId: var paid }] && paid == card.Id &&
                played[0].TargetSeats!.SequenceEqual(action.TargetSeats) && restored.Length == 2 &&
                restored.All(fact => losses.Any(loss => loss.FrameId == fact.LossFrameId &&
                    loss.TargetSeat == fact.TargetSeat && loss.Grant.GrantId == fact.GrantId)) &&
                game.Events.Count(entry => entry.Payload is HellReturnSoulDestroyedEvent fact && fact.CardId == card.Id) == 1 &&
                game.CreateCardZoneDiagnostics().Single(entity => entity.CardId == card.Id).Location == CardLocation.OutsideGame &&
                vm.Hand.Count(item => item.Kind == CardKind.ReturnSoul) == 1 &&
                game.GetHumanLegalActions().All(item => item.Kind != LegalActionKind.ReturnSoul),
                "The actual confirm button must submit one physical soul with every canonical recipient, restore both real lost grants and destroy only its paid entity.");

            void Layout()
            {
                root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
            }
            void Click(Button button)
            {
                Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter),
                    "The real soul UI button must be enabled before its routed click.");
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Layout();
            }
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void Accept(GameCommand command)
        {
            var result = original.Submit(command);
            Program.Assert(result.Accepted, result.Error?.Message ?? "Native soul UI fixture command rejected.");
        }
        void Until(Func<bool> ready)
        {
            for (var step = 0; step < 80; step++) { if (ready()) return; Accept(new AdvanceOneStepCommand(original.Revision)); }
            throw new InvalidOperationException("The native soul UI fixture did not reach its bounded decision.");
        }
        void Play() => Until(() => original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    }

    public static void FiveTierLobbyRealFiveSeatsDormantAuxiliaryAndRestore(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
            includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true,
            includeNianActivities: true, includeHellActivities: true);
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, showSetup: true, saveStore: store,
            useExpandedContent: true)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            var options = vm.VisibleTableModes.Where(option => option.ModeId.StartsWith("activity:hell-2020-tier-", StringComparison.Ordinal)).ToArray();
            Program.Assert(options.Length == 5, "The real Hell lobby must expose one entry for each of the five enemy tiers.");
            Program.Assert(registry.Generals.Values.Where(general => general.Id.StartsWith("hell:", StringComparison.Ordinal))
                .All(general => GeneralArt.HasPortrait(general.Id) && new GeneralPortraitViewModel(general.Id).Image is not null) &&
                GeneralArt.NormalizeKey("hell:king-pingdeng") == "hell-pingdeng",
                "Every actual Hell NPC, including the distinct Pingdeng general ID, must load its readable local emblem.");
            for (var tier = 1; tier <= 5; tier++)
            {
                vm.SelectedTableMode = options.Single(option => option.ModeId == $"activity:hell-2020-tier-{tier}");
                Program.Assert(vm.IsActivityModeSelection && vm.IsTeamModeSelection && !vm.IsIdentityModeSelection &&
                    vm.SelectedTableMode.PlayerCount == 5 && vm.StartingTeams.Count == 1 &&
                    vm.SelectedStartingTeam.TeamId == "team:allies" && vm.SelectedStartingTeam.Name == "联合军" &&
                    vm.SelectedTableMode.Description.Contains($"敌方{tier}阶") &&
                    vm.TeamModeSetupText.Contains($"敌方为{tier}阶") && vm.TeamModeSetupText.Contains("玩家固定五阶") &&
                    vm.TeamModeSetupText.Contains("起手牌+2") && vm.TeamModeSetupText.Contains("重生") &&
                    vm.TeamModeSetupText.Contains("两名人机") && vm.CurrentGuideTitle.Contains("十殿阎罗") &&
                    vm.CurrentGuideBody.Contains("地藏王") && !vm.CurrentGuideBody.Contains("涅槃") &&
                    !vm.CurrentGuideBody.Contains("小鬼") && !vm.CurrentGuideBody.Contains("牛头马面"),
                    "Every Hell enemy-tier selection must retain the real five-seat coalition and its own fixed player tier-five and campaign rules.");
            }
            vm.SelectedTableMode = options.Single(option => option.ModeId == "activity:hell-2020-tier-3");
            Program.Render(root, 1440, 880, Path.Combine(output, "166-hell-setup.png"));
            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            Until(() => vm.IsGeneralSelectionPending);
            Program.Assert(vm.GeneralSelectionContextTitle.Contains("联合军") &&
                vm.GeneralSelectionContextDetail.Contains("起手六张") && vm.GeneralSelectionSubtitle.Contains("两名队友") &&
                vm.GeneralChoices.All(choice => choice.HealthText.Contains("五阶")) &&
                vm.IdentityRevealRoster.Contains("孟婆") && vm.IdentityObjective.Contains("四殿") &&
                vm.IdentityObjective.Contains("前50回合") && vm.TableArenaTitle == "十 殿 阎 罗",
                "The real private draft must explain the three players, two enemy seats and conditional final boss without Ghost rules.");
            vm.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(vm.CustomGeneralChoices.Count > 0 && vm.CustomGeneralChoices.All(choice =>
                !choice.GeneralId.StartsWith("hell:", StringComparison.Ordinal) &&
                choice.HealthText == $"五阶体力上限 {registry.Generals[choice.GeneralId].BaseHp + 2}"),
                "The full player roster must exclude NPCs and preview the actual fixed tier-five maximum health.");
            vm.PreviewGeneralChoiceCommand.Execute(vm.CustomGeneralChoices.Single(choice => choice.GeneralId == "classic:guan-yu"));
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            Until(() => Program.Engine(vm).Events.Any(entry => entry.Payload is SetupCompletedEvent));
            var game = Program.Engine(vm);
            var snapshot = game.CreateSnapshot(0);
            var mode = registry.Modes[game.ModeId];
            var auxiliary = snapshot.Players.Single(player => player.GeneralId == mode.HellActivity!.AuxiliaryGeneralId);
            var seat = vm.Seats.Single(player => player.Seat == auxiliary.Seat);
            Program.Assert(snapshot.Players.Count == 5 && snapshot.Players.Count(player => player.IsHuman) == 1 &&
                snapshot.Players.Count(player => player.TeamId == "team:allies") == 3 &&
                snapshot.Players.Count(player => player.TeamId == "team:hell") == 2 &&
                snapshot.Players.Where(player => player.TeamId == "team:allies").All(player =>
                    player.MaxHp == registry.Generals[player.GeneralId].BaseHp + 2 && player.HandCount == 6) &&
                vm.Seats.Count(player => player.IsTeammate && !player.IsHuman) == 2 &&
                vm.Seats.Where(player => player.TeamId == "team:allies").Select(player => player.RoleLabel)
                    .SequenceEqual(["先锋", "中坚", "大将"]) &&
                vm.Seats.Count(player => player.RoleLabel == "首领") == 1 &&
                !auxiliary.IsAlive && !seat.IsAlive && seat.RoleLabel == "孟婆·未登场" &&
                seat.UnavailableStatusText == "未登场" && seat.HpText.Contains("尚未登场") && !seat.HealthImages.Any() &&
                vm.AliveText.Contains("联合军 3") && vm.AliveText.Contains("孟婆未登场") &&
                vm.TableModeText.Contains("第 1/4 殿") && vm.TableModeText.Contains("敌方3阶") &&
                vm.WindowTitle.Contains("十殿阎罗") && !vm.AliveText.Contains("鬼军") &&
                !game.Events.Any(entry => entry.Payload is PlayerDiedEvent died && died.VictimSeat == auxiliary.Seat),
                "The native five-seat table must show two allied AI generals, six-card player hands and a dormant Mengpo rather than a falsely dead character.");
            Program.Render(root, 1440, 880, Path.Combine(output, "167-hell-table.png"));
            var auxiliaryButton = Program.Find<Button>(root).Single(button =>
                ReferenceEquals(button.Command, vm.SelectTargetCommand) && ReferenceEquals(button.CommandParameter, seat));
            var shade = Program.Find<Border>(auxiliaryButton).Single(border => border.Name == "DeathShade");
            Program.Assert(shade.Visibility == Visibility.Visible && !shade.IsHitTestVisible &&
                Program.Find<TextBlock>(shade).Single().Text == "未登场",
                "The actual portrait template must label the dormant seat as not yet appeared, preserving its native dead/non-targetable state.");
            vm.SaveGameCommand.Execute(null);
            var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.SelectedTableMode = vm.TableModes.Single(option => option.ModeId == "team:standard-2v2");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == "activity:hell-2020-tier-3" &&
                vm.SelectedStartingTeam.TeamId == "team:allies" && vm.Seats.Count == 5 &&
                vm.TableModeText.Contains("敌方3阶") && vm.AliveText.Contains("孟婆未登场") &&
                SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
                "A manual Hell checkpoint must restore its selected enemy tier, five seats, dormant auxiliary and exact private snapshot from another setup mode.");

            using var other = new MainViewModel(false, 17, true, store, useExpandedContent: true,
                contentRegistry: ComposedSkillContentRegistry.CreateShowcase(includeNianActivities: true))
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            other.LoadManualGameCommand.Execute(null);
            Program.Assert(!other.HasSaveError && other.SelectedTableMode.ModeId == game.ModeId &&
                other.StartingTeams.Count == 1 && other.SelectedStartingTeam.TeamId == "team:allies" &&
                other.Seats.Count == 5 && other.TableArenaTitle == "十 殿 阎 罗" &&
                SnapshotJson.Serialize(Program.Engine(other).CreateSnapshot(0)) == saved,
                "Checkpoint package recovery must restore Hell even when the receiving desktop's configured registry has no Hell package.");
            other.StartNewGameFromLobbyCommand.Execute(null);
            other.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !other.IsGeneralSelectionPending; step++) other.StepAiCommand.Execute(null);
            Program.Assert(!other.HasSaveError && other.IsGeneralSelectionPending && Program.Engine(other).ModeId == game.ModeId &&
                other.GeneralSelectionContextTitle.Contains("联合军") && other.GeneralSelectionContextDetail.Contains("起手六张"),
                "Rematching a recovered Hell save must use its restored registry and reach the native five-seat player draft.");
        }
        finally
        {
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }

        void Until(Func<bool> ready)
        {
            for (var step = 0; step < 64; step++)
            {
                if (ready()) return;
                Program.Assert(vm.StepAiCommand.CanExecute(null), "The real Hell UI must remain able to advance to the bounded setup boundary.");
                vm.StepAiCommand.Execute(null);
            }
            throw new InvalidOperationException("The real Hell UI did not reach its bounded setup boundary.");
        }
    }

    private sealed class SoulUiFixture(bool conversion = false) : IGameContentPackage
    {
        internal const string Mode = "fixture:hell-soul-ui", Owner = "fixture:hell-soul-owner",
            Driver = "fixture:hell-soul-driver", Lost = "fixture:hell-soul-lost", Quiet = "fixture:hell-soul-quiet";
        public PackageManifest Manifest { get; } = new("hell-soul-ui-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.hell-mengpo.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.hell-mengpo.presentation.json")!);
            var actual = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in actual.Programs) builder.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
            { Program = program, ProgramPresentation = actual.Presentations[id], Tags = id == "hell:wanghun" ? SkillTag.Locked : SkillTag.None });
            if (conversion)
            {
                using var singleRules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-ruan-yu.rules.json")!);
                using var singlePresentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-ruan-yu.presentation.json")!);
                var single = SkillProgramCatalog.Load(singleRules.ReadToEnd(), singlePresentation.ReadToEnd());
                builder.AddSkill(new("ol:miaoxian", single.Presentations["ol:miaoxian"].Name, single.Presentations["ol:miaoxian"].Description)
                { Program = single.Programs["ol:miaoxian"], ProgramPresentation = single.Presentations["ol:miaoxian"] });
            }
            builder.AddCard(new("fixture:hell-soul", "回魂", "锦囊牌", "真实忘魂生成的实体牌。", CardKind.ReturnSoul));
            var definitions = new object[]
            {
                new { id = Driver, revision = 1, activations = new object[]
                {
                    new { id = "defeat", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                        targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "loseHp", target = "selectedTarget", amount = 20 } } },
                    new { id = "draw", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                        targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "draw", target = "owner", amount = 2 } } },
                }.Concat(conversion ? new object[] { new
                    {
                        id = "prepare-single-conversion", minCards = 6, maxCards = 6, minTargets = 0, maxTargets = 0,
                        targetKind = "anyLiving", sourceZones = new[] { "hand" }, usesPerTurn = (int?)null, effects = new object[]
                        {
                            new { op = "discardSelected", target = "owner", amount = 6 },
                            new { op = "grantSkills", target = "owner", skillIds = new[] { "ol:miaoxian" } }
                        }
                    } } : []).ToArray() },
                new { id = Quiet, revision = 1, modifiers = new[]
                    { new { id = "quiet-retention", query = "handLimit", operation = "add", value = 30, priority = 0 } } },
                new { id = Lost, revision = 1, modifiers = new[]
                    { new { id = "actual-lost-skill", query = "slashLimit", operation = "add", value = 1, priority = 0 } } }
            };
            var fixture = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, Quiet, Lost }.ToDictionary(id => id, id => new { name = id, description = "Small native soul UI fixture." }) }));
            foreach (var (id, program) in fixture.Programs) builder.AddSkill(new(id, id, "Native restoration UI fixture.")
            { Program = program, ProgramPresentation = fixture.Presentations[id], Tags = id == Lost ? SkillTag.None : SkillTag.Awakening });
            builder.AddGeneral(new(Owner, "回魂操作者", "", Driver, "wei", 12, [Lost]));
            string[] peers = ["fixture:hell-soul-peer-1", "fixture:hell-soul-peer-2"];
            foreach (var peer in peers) builder.AddGeneral(new(peer, peer, "", Quiet, "wei", 12, [Lost]));
            const string mengpo = "fixture:hell-soul-mengpo", next = "fixture:hell-soul-next",
                auxiliary = "fixture:hell-soul-auxiliary", hidden = "fixture:hell-soul-hidden";
            builder.AddGeneral(new(mengpo, "孟婆", "", "hell:wanghun", "qun", 3));
            builder.AddGeneral(new(next, "后续首领", "", Quiet, "qun", 12));
            if (conversion)
            {
                builder.AddGeneral(new(auxiliary, "辅助席位", "", Quiet, "qun", 3));
                builder.AddGeneral(new(hidden, "隐藏首领", "", Quiet, "qun", 12));
            }
            const string deck = "fixture:hell-soul-deck";
            builder.AddDeck(new(deck, "Twelve real initial cards", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, conversion ? 80 : 12).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(Mode, "Native soul UI fixture", conversion ? 5 : 4, conversion ? 5 : 4,
                new Dictionary<string, int>(), deck, 3,
                conversion ? [Owner, .. peers, mengpo, next, auxiliary, hidden] : [Owner, .. peers, mengpo, next], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:hell"] = conversion ? 2 : 1 })
            {
                BossActivity = new([Owner, .. peers], conversion
                    ? [new("孟婆", [mengpo]), new("后续", [next]), new("后续", [next]), new("终殿", [next, hidden])]
                    : [new("孟婆", [mengpo], 0), new("后续", [next], 0)], conversion ? 5 : 1, [], EnemyTeamId: "team:hell"),
                HellActivity = conversion ? new(1, auxiliary, hidden) : null
            });
        }
    }
}
