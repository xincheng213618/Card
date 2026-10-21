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

internal static class NianActivityUiChecks
{
    public static void ClickDeadAllyAndConfirmSharedRevival(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new RevivalUiFixture());
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:allies",
            ModeId = RevivalUiFixture.Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(new StartGameCommand());
        Until(() => original.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(new SelectGeneralCommand(0, RevivalUiFixture.Owner, original.Revision, original.PendingDecision!.PromptId));
        Until(() => original.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Accept(new UseProgramSkillCommand(0, RevivalUiFixture.Driver, "defeat", [], [1], original.Revision,
            original.PendingDecision!.PromptId));
        Until(() => !original.CreateSnapshot(0).Players[1].IsAlive && original.PendingDecision is
            { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false,
            original.CreateCheckpoint()));
        using var vm = new MainViewModel(false, 17, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            var game = Program.Engine(vm);
            var victim = game.CreateSnapshot(0).Players[1];
            var originalGameplay = CommandJson.Serialize(game.AcceptedCommands.Where(c => c is not TickNianClockCommand).ToArray());
            Layout();
            Program.Assert(!vm.HasSaveError && !Seat(1).IsAlive && victim.MaxHp == 6 &&
                vm.TableModeText.Contains("共享复活 3/3") && !Payments().Any(),
                "A real manual checkpoint must restore an actually dead allied general with the full shared revival quota.");
            Begin();
            var deadButton = TargetButton(1);
            var deathShade = Program.Find<Border>(deadButton).Single(border => border.Name == "DeathShade");
            Program.Assert(deadButton.IsEnabled && deadButton.IsHitTestVisible &&
                deathShade.Visibility == Visibility.Visible && !deathShade.IsHitTestVisible &&
                vm.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).SequenceEqual([1]) &&
                !vm.CanConfirmSelected && !Confirm().IsEnabled,
                "The real dead-seat template must keep its portrait clickable through the non-hit-testable death shade and highlight only the legal dead ally.");
            Click(TargetButton(2));
            Program.Assert(!vm.Seats.Any(seat => seat.IsSelectedTarget) && !vm.CanConfirmSelected,
                "Clicking a living teammate must not turn it into a revival target.");
            Click(TargetButton(3));
            Program.Assert(!vm.Seats.Any(seat => seat.IsSelectedTarget) && !vm.CanConfirmSelected,
                "Clicking the enemy must not turn it into a revival target.");
            Click(TargetButton(1));
            Program.Assert(Seat(1).IsSelectedTarget && vm.CanConfirmActiveSkill && vm.CanConfirmSelected && Confirm().IsEnabled,
                "Clicking the actual dead portrait must enable the normal bound confirmation button.");
            Click(TargetButton(1));
            Program.Assert(!Seat(1).IsSelectedTarget && !vm.CanConfirmSelected && !Confirm().IsEnabled,
                "A second click must remove the dead target without spending a shared revival.");
            Click(TargetButton(1));
            Click((Button)window.FindName("CancelActionButton"));
            Program.Assert(!vm.IsActiveSkillSelectionPending && !vm.Seats.Any(seat => seat.IsSelectedTarget) &&
                !Payments().Any() && !Seat(1).IsAlive &&
                CommandJson.Serialize(game.AcceptedCommands.Where(c => c is not TickNianClockCommand).ToArray()) == originalGameplay,
                "Target toggles and the real cancel button must remain UI drafts, with no revival command or payment.");
            Begin();
            Click(TargetButton(1));
            Program.Render(root, 1440, 880, Path.Combine(output, "165-nian-dead-ally-target.png"));
            Click(Confirm());
            for (var step = 0; step < 48 && (!game.Events.Any(e => e.Payload is NianRevivalCompletedEvent) ||
                     game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); step++)
                vm.StepAiCommand.Execute(null);
            Layout();
            var revived = game.CreateSnapshot(0).Players[1];
            var uses = game.AcceptedCommands.OfType<UseProgramSkillCommand>().Where(c => c.SkillId == "nian:revive").ToArray();
            Program.Assert(uses is [{ ActorSeat: 0, ActivationId: "revive", CardIds.Count: 0 } use] &&
                use.TargetSeats.SequenceEqual([1]) && Payments() is [{ ActorSeat: 0, TargetSeat: 1, UsedRevives: 1 }] &&
                game.Events.Count(e => e.Payload is NianRevivalCompletedEvent { TargetSeat: 1, DrawnCards: 2 }) == 1 &&
                revived.IsAlive && revived.Hp == 2 && revived.HandCount == 2 && revived.MaxHp == victim.MaxHp &&
                revived.GeneralId == victim.GeneralId && Seat(1).IsAlive && !Seat(1).IsSelectedTarget &&
                vm.TableModeText.Contains("共享复活 2/3") && vm.AliveText.Contains("联合军 3") &&
                vm.HumanActiveSkillActions.All(action => action.ProgramSkillId != "nian:revive") &&
                Program.Find<Border>(TargetButton(1)).Single(border => border.Name == "DeathShade").Visibility == Visibility.Collapsed,
                "The actual confirmation button must submit one native dead-ally revival, revive the original general with two cards and deduct exactly one shared use.");
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).CreateSnapshot(0).Players[1] is { IsAlive: true, Hp: 2, HandCount: 2 } &&
                vm.TableModeText.Contains("共享复活 2/3") &&
                Program.Engine(vm).Events.Count(e => e.Payload is NianRevivalPaidEvent) == 1,
                "Saving and loading the clicked revival must preserve the actual revived ally and shared payment ledger.");

            SeatViewModel Seat(int seat) => vm.Seats.Single(item => item.Seat == seat);
            Button TargetButton(int seat) => Program.Find<Button>(root).Single(button =>
                ReferenceEquals(button.Command, vm.SelectTargetCommand) && ReferenceEquals(button.CommandParameter, Seat(seat)));
            Button Confirm() => (Button)window.FindName("PlayCardButton");
            NianRevivalPaidEvent[] Payments() => game.Events.Select(entry => entry.Payload).OfType<NianRevivalPaidEvent>().ToArray();
            void Begin()
            {
                vm.SelectActiveSkillCommand.Execute(vm.HumanActiveSkillActions.Single(action => action.ProgramSkillId == "nian:revive"));
                Layout();
            }
            void Click(Button button)
            {
                Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter),
                    "The actual revival UI button must be enabled before the routed click.");
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Layout();
            }
            void Layout()
            {
                root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
            }
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void Accept(GameCommand command)
        {
            var result = original.Submit(command);
            Program.Assert(result.Accepted, result.Error?.ToString() ?? "Native dead-ally UI fixture command rejected.");
        }
        void Until(Func<bool> ready)
        {
            for (var step = 0; step < 80; step++) { if (ready()) return; Accept(new AdvanceOneStepCommand(original.Revision)); }
            throw new InvalidOperationException("Native dead-ally UI fixture did not reach its bounded decision boundary.");
        }
    }

    public static void SetupFourSeatsFormPortraitsAndCrossConfigurationRestore(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
            includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true,
            includeNianActivities: true);
        var mode = registry.Modes["activity:nian-2016"];
        var activity = mode.NianActivity!;
        var coalition = mode.BossActivity!;
        var store = new MemorySaveStore();
        using var vm = new MainViewModel(false, 17, showSetup: true, saveStore: store,
            useExpandedContent: true) { IsMotionEnabled = false };
        var window = new MainWindow(vm);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            vm.OpenLobbyCategoryCommand.Execute("activity");
            vm.SelectedTableMode = vm.VisibleTableModes.Single(option => option.ModeId == mode.Id);
            Program.Assert(vm.IsActivityModeSelection && vm.IsTeamModeSelection && vm.SelectedTableMode.PlayerCount == 4 &&
                vm.StartingTeams.Count == 1 && vm.SelectedStartingTeam.TeamId == coalition.PlayerTeamId &&
                vm.TeamModeSetupText.Contains("12体力") && vm.TeamModeSetupText.Contains("两名人机") &&
                vm.TeamModeSetupText.Contains("五阶") && vm.TeamModeSetupText.Contains("第二轮") &&
                vm.TeamModeSetupText.Contains("三次") && vm.CurrentGuideTitle.Contains("年兽") &&
                vm.TeamModeSetupText.Contains("30分钟") && !Program.Engine(vm).IsNianClockRunning &&
                vm.CurrentGuideBody.Contains("四种用尽") && !vm.CurrentGuideBody.Contains("觉醒") &&
                !vm.TeamModeSetupText.Contains("生肖") && !vm.TeamModeSetupText.Contains("随从"),
                "The Nian lobby must explain the complete 2016 three-versus-one encounter and its four-state round cycle.");
            Program.Assert(coalition.PlayerPoolIds.Contains("ol:shen-zhao-yun") &&
                coalition.PlayerPoolIds.All(id => id is not ("classic:cao-pi" or "classic:man-chong" or
                    "classic:cai-wen-ji" or "ol:sp-cai-wen-ji" or "classic:zuo-ci" or "classic:yu-ji" or
                    "classic:shen-zhao-yun")) &&
                activity.FormGeneralIds.All(id => registry.Generals[id].BaseHp == 12 &&
                    registry.Generals[id].FactionId == "god" && GeneralArt.HasPortrait(id) &&
                    GeneralArt.GetSkin(id) is { Name: "本地兽符" } && new GeneralPortraitViewModel(id).Image is not null),
                "All five Nian states must use readable local emblems; the player roster must preserve legal gods and exclude the exact historical bans.");
            Program.Render(root, 1440, 880, Path.Combine(output, "162-nian-setup.png"));

            vm.StartNewGameFromLobbyCommand.Execute(null);
            vm.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !vm.IsGeneralSelectionPending; step++) vm.StepAiCommand.Execute(null);
            Program.Assert(vm.IsGeneralSelectionPending && vm.GeneralSelectionContextTitle.Contains("联合军") &&
                vm.GeneralSelectionContextDetail.Contains("五阶") && vm.GeneralSelectionContextDetail.Contains("三次复活") &&
                vm.GeneralSelectionSubtitle.Contains("两名队友") && vm.IdentityObjective.Contains("击败年兽") &&
                vm.TableArenaTitle == "挑 战 年 兽" && !vm.IdentityObjective.Contains("鬼将") &&
                vm.GeneralChoices.All(choice => choice.HealthText.Contains("五阶")),
                "The real Nian draft must retain its local tier-five health, two AI teammates and 2016 objective.");
            Program.Render(root, 1440, 880, Path.Combine(output, "163-nian-selection.png"));
            vm.ToggleGeneralRosterCommand.Execute(null);
            Program.Assert(vm.CustomGeneralChoices.Count > 0 && vm.CustomGeneralChoices.All(choice =>
                choice.HealthText == $"五阶体力上限 {registry.Generals[choice.GeneralId].BaseHp + 2}"),
                "Every real expanded-roster entry must reuse the activity's tier-five health preview.");
            var chosen = vm.CustomGeneralChoices.Single(choice => choice.GeneralId == "ol:shen-zhao-yun");
            Program.Assert(chosen.Kingdom == "神" && chosen.HealthText.Contains("五阶"),
                "The legitimate expanded roster must expose ordinary OL God Zhao Yun with his god faction and tier-five health.");
            vm.PreviewGeneralChoiceCommand.Execute(chosen);
            vm.ConfirmGeneralChoiceCommand.Execute(null);
            if (vm.RunToHumanCommand.CanExecute(null)) vm.RunToHumanCommand.Execute(null);
            var game = Program.Engine(vm);
            var snapshot = game.CreateSnapshot(0);
            var beast = snapshot.Players.Single(player => player.TeamId == coalition.EnemyTeamId);
            Program.Assert(snapshot.Players.Count == 4 && snapshot.Players.Count(player => player.IsHuman) == 1 &&
                snapshot.Players.Count(player => player.TeamId == coalition.PlayerTeamId && !player.IsHuman) == 2 &&
                beast.GeneralId == "nian:initial" && beast.Hp == 12 && beast.MaxHp == 12 &&
                snapshot.Players.Single(player => player.IsHuman).FactionId == "god" &&
                snapshot.Players.Single(player => player.IsHuman).MaxHp == registry.Generals[chosen.GeneralId].BaseHp + 2,
                "The live Nian table must begin with one twelve-health initial beast and three independently tiered coalition players.");
            Program.Assert(vm.Seats.Count(seat => seat.RoleLabel == "年兽") == 1 &&
                vm.Seats.Count(seat => seat.IsTeammate) == 2 &&
                vm.Seats.Single(seat => seat.RoleLabel == "年兽") is { HasPortrait: true, Portrait.Image: not null } &&
                vm.TableModeText.Contains("年兽·初始") && vm.TableModeText.Contains("共享复活 3/3") &&
                vm.TableModeText.Contains("剩余") &&
                vm.AliveText.Contains("联合军 3") && vm.AliveText.Contains("年兽 1") &&
                !vm.TableModeText.Contains("第 1/1") && !vm.TableModeText.Contains("万夫莫开") &&
                !vm.AliveText.Contains("鬼军"),
                "The live four-seat UI must show the actual Nian state and shared revival balance without the generic Ghost stage label.");
            Program.Render(root, 1440, 880, Path.Combine(output, "164-nian-table.png"));
            Program.Assert(!vm.IsAutoAdvance && game.IsNianClockRunning,
                "Pausing automatic AI progression must not pause the active thirty-minute Nian clock.");
            var submit = typeof(MainViewModel).GetMethod("SubmitCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            CommandResult Submit(GameCommand command) => (CommandResult)submit.Invoke(vm, [command])!;
            // Expanded rosters can place an ally before the human and offer Longhun counters.
            // Decline only the published optional counterspells before testing the play clock.
            for (var step = 0; step < 24 && game.PendingDecision is
                { Kind: DecisionKind.Nullification, PlayerSeat: 0 }; step++)
            {
                Program.Assert(vm.CancelActionCommand.CanExecute(null),
                    "An optional counterspell before the first human turn must expose its real decline command.");
                vm.CancelActionCommand.Execute(null);
                if (vm.RunToHumanCommand.CanExecute(null)) vm.RunToHumanCommand.Execute(null);
            }
            var beforeNative = game.CreateSnapshot(0);
            Program.Assert(beforeNative.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The clock input regression needs the real first human play prompt.");
            var originalRevision = game.Revision;
            var originalJournalCount = game.CreateCheckpoint().Commands.Count;
            var originalRemaining = game.NianRemainingMilliseconds;
            System.Threading.Thread.Sleep(25);
            var stale = Submit(new EndPlayPhaseCommand(0, originalRevision - 1, beforeNative.PendingDecision!.PromptId));
            Program.Assert(!stale.Accepted && stale.Error?.Code == CommandErrorCode.StaleRevision &&
                game.Revision == originalRevision && game.CreateCheckpoint().Commands.Count == originalJournalCount &&
                game.NianRemainingMilliseconds == originalRemaining &&
                SnapshotJson.Serialize(game.CreateSnapshot(0)) == SnapshotJson.Serialize(beforeNative),
                "A genuinely stale UI command must remain rejected without flushing time, rebasing input or appending a journal entry.");
            var ended = Submit(new EndPlayPhaseCommand(0, originalRevision, beforeNative.PendingDecision!.PromptId));
            var submitted = game.CreateCheckpoint().Commands.Skip(originalJournalCount).ToArray();
            Program.Assert(ended.Accepted && submitted is [TickNianClockCommand { ElapsedMilliseconds: > 0 } tick,
                    EndPlayPhaseCommand native] && tick.ExpectedRevision == originalRevision &&
                native.ExpectedRevision == originalRevision + 1 && game.Revision == originalRevision + 2 &&
                game.NianRemainingMilliseconds!.Value < originalRemaining!.Value,
                "Valid native input must first commit the elapsed clock tail and then rebase only its originally current revision.");
            var remainingBeforeSave = game.NianRemainingMilliseconds!.Value;
            // Stay below the five-second timer interval; saving itself must commit this elapsed tail.
            System.Threading.Thread.Sleep(25);
            vm.SaveGameCommand.Execute(null);
            var savedRemaining = game.NianRemainingMilliseconds!.Value;
            Program.Assert(!vm.HasSaveError && savedRemaining < remainingBeforeSave &&
                store.Read(GameSaveSlot.Manual).Checkpoint.Commands.OfType<TickNianClockCommand>().Any(),
                "Saving must flush even a partial timer interval through a real replayable clock command.");
            var saved = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.SelectedTableMode = vm.TableModes.Single(option => option.ModeId == "team:standard-2v2");
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && Program.Engine(vm).ModeId == mode.Id &&
                vm.SelectedStartingTeam.TeamId == coalition.PlayerTeamId && vm.WindowTitle.Contains("年兽") &&
                vm.TableArenaTitle == "挑 战 年 兽" && vm.TableModeText.Contains("共享复活 3/3") &&
                Program.Engine(vm).NianRemainingMilliseconds == savedRemaining &&
                SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == saved,
                "Restoring Nian from another lobby selector must preserve its exact state, shared revivals and source snapshot.");

            var configuredWithoutNian = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
                includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true);
            using var reopened = new MainViewModel(false, 17, showSetup: true, saveStore: store,
                useExpandedContent: true, contentRegistry: configuredWithoutNian) { IsMotionEnabled = false };
            Program.Assert(!configuredWithoutNian.Modes.ContainsKey(mode.Id) && reopened.TableModes.All(option => option.ModeId != mode.Id),
                "The second desktop must genuinely be configured without the Nian activity package.");
            reopened.LoadManualGameCommand.Execute(null);
            Program.Assert(!reopened.HasSaveError && Program.Engine(reopened).ModeId == mode.Id &&
                reopened.SelectedTableMode.ModeId == mode.Id && reopened.SelectedStartingTeam.TeamId == coalition.PlayerTeamId &&
                reopened.Seats.Count == 4 && reopened.TableArenaTitle == "挑 战 年 兽" &&
                reopened.TableModeText.Contains("共享复活 3/3") &&
                Program.Engine(reopened).NianRemainingMilliseconds == savedRemaining &&
                SnapshotJson.Serialize(Program.Engine(reopened).CreateSnapshot(0)) == saved,
                "Cross-configuration restore must recover the checkpoint's Nian metadata and state without substituting a configured mode.");
            reopened.StartNewGameFromLobbyCommand.Execute(null);
            reopened.ContinueFromIdentityRevealCommand.Execute(null);
            for (var step = 0; step < 24 && !reopened.IsGeneralSelectionPending; step++) reopened.StepAiCommand.Execute(null);
            Program.Assert(!reopened.HasSaveError && Program.Engine(reopened).ModeId == mode.Id &&
                reopened.IsGeneralSelectionPending && Program.Engine(reopened).PendingDecision is
                    { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } && reopened.Seats.Count == 4 &&
                reopened.GeneralSelectionContextDetail.Contains("三次复活") && reopened.TableArenaTitle == "挑 战 年 兽",
                "Starting again after cross-configuration restore must reach a real Nian draft using its recovered registry.");
        }
        finally
        {
            window.Content = null;
            window.DataContext = null;
            window.Close();
        }
    }
    private sealed class RevivalUiFixture : IGameContentPackage
    {
        internal const string Mode = "fixture:nian-revival-ui", Owner = "fixture:nian-revival-ui-owner",
            Driver = "fixture:nian-revival-ui-driver";
        public PackageManifest Manifest { get; } = new("nian-revival-ui-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            const string prefix = "CardGame.Content.Standard.SkillPrograms.nian-activity";
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rulesReader = new StreamReader(assembly.GetManifestResourceStream(prefix + ".rules.json")!);
            using var presentationReader = new StreamReader(assembly.GetManifestResourceStream(prefix + ".presentation.json")!);
            var actual = SkillProgramCatalog.Load(rulesReader.ReadToEnd(), presentationReader.ReadToEnd());
            foreach (var (id, program) in actual.Programs)
                builder.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                {
                    Program = program, ProgramPresentation = actual.Presentations[id],
                    Tags = id == "nian:niepan" ? SkillTag.Limited : SkillTag.None
                });
            var driver = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}]}
                ]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "实际阵亡夹具", description = "通过原生体力流失建立真实阵亡队友。" } }
                }));
            builder.AddSkill(new(Driver, "实际阵亡夹具", "通过原生体力流失建立真实阵亡队友。")
            { Program = driver.Programs[Driver], ProgramPresentation = driver.Presentations[Driver] });
            const string quiet = "fixture:nian-revival-ui-quiet";
            builder.AddSkill(new(quiet, "安静夹具", "无额外行为。") { ExecutionForms = SkillExecutionForm.State });
            string[] players = [Owner, "fixture:nian-revival-ui-ally-1", "fixture:nian-revival-ui-ally-2"];
            foreach (var id in players) builder.AddGeneral(new(id, id, "", id == Owner ? Driver : quiet, "wei", 4));
            string[] forms = ["nian:initial", "nian:wise", "nian:alert", "nian:capricious", "nian:furious"];
            foreach (var id in forms) builder.AddGeneral(new(id, "年兽·原生复活夹具", "", quiet, "god", 12));
            const string deck = "fixture:nian-revival-ui-deck";
            builder.AddDeck(new(deck, "无回复的实际牌堆", 4, 2, [new("standard:dodge", 80)]));
            builder.AddMode(new(Mode, "挑战年兽·原生复活界面", 4, 4, new Dictionary<string, int>(), deck, 3,
                [.. players, .. forms], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:nian"] = 1 })
            {
                BossActivity = new(players, [new("年兽", forms, 4)], 5, ["nian:revive"], EnemyTeamId: "team:nian"),
                NianActivity = new(forms, SharedRevives: 3)
            });
        }
    }
}
