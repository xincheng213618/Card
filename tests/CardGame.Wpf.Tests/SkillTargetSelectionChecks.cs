using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class SkillTargetSelectionChecks
{
    public static void PortraitDraftAndConfirmation(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, zhefu: true);
        using var vm = fixture.Model;
        vm.SelectCardCommand.Execute(vm.Hand.First(card => card.IsPlayable));
        vm.ConfirmSelectedCommand.Execute(null);
        Reach(vm, prompt => prompt.SkillPrompt?.SkillId == "ol:zhefu");
        vm.SelectSkillChoiceCommand.Execute(vm.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Reach(vm, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("step") == "target"));
        var game = Program.Engine(vm);
        Program.Assert(vm.IsSkillTargetSelectionPending && vm.CanActFromHand && !vm.HasCenterChoices &&
            vm.CenterSkillChoices.Count == 0 && !vm.CanConfirmSelected,
            "Real Zhefu targets must use the portrait draft and bottom controls, without a central name-button panel.");
        var revision = game.Revision;
        var targetSeats = game.PendingDecision!.Choices.SelectMany(choice => choice.Targets).ToArray();
        Program.Assert(vm.Seats.Where(seat => seat.IsLegalTarget).Select(seat => seat.Seat).Order().SequenceEqual(targetSeats.Order()),
            "Only the engine's currently published targets may be highlighted.");
        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 0));
        Program.Assert(!vm.HasSelection && game.Revision == revision, "An invalid portrait must not create a target draft.");

        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var size in new[] { (1120, 740), (1920, 1080) })
            {
                Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"skill-target-before-{size.Item1}.png"));
                var anchors = Program.Find<Button>(root).Where(button => button.DataContext is SeatViewModel &&
                    BattleFeedbackLayer.GetSeatAnchor(button) >= 0).ToArray();
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                var before = anchors.ToDictionary(button => ((SeatViewModel)button.DataContext).Seat, button => Bounds(button));
                var selected = vm.Seats.Single(seat => seat.Seat == targetSeats[0]);
                vm.SelectTargetCommand.Execute(selected);
                Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"skill-target-selected-{size.Item1}.png"));
                Program.Assert(vm.CanConfirmSelected && selected.IsSelectedTarget && game.Revision == revision,
                    "Selecting a portrait must only preview, without submitting or paying a skill cost.");
                Program.Assert(anchors.All(button => before[((SeatViewModel)button.DataContext).Seat] == Bounds(button)),
                    "Target selection must keep every portrait's displayed position and size unchanged.");
                var outline = Program.Find<Border>(anchors.Single(button => ReferenceEquals(button.DataContext, selected)))
                    .Single(border => border.Name == "SeatSelectedOutline");
                Program.Assert(outline.Opacity == 1 && !outline.IsHitTestVisible &&
                    ((FrameworkElement)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed &&
                    ((Button)window.FindName("PlayCardButton")).IsEnabled &&
                    ((Button)window.FindName("CancelActionButton")).IsEnabled,
                    "The selection must add the existing outline and enable the bottom confirm/cancel controls.");
                vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == targetSeats[1]));
                Program.Assert(vm.Seats.Count(seat => seat.IsSelectedTarget) == 1 && !selected.IsSelectedTarget && game.Revision == revision,
                    "A single-target draft must switch portraits without committing.");
                vm.CancelActionCommand.Execute(null);
                Program.Assert(!vm.HasSelection && !vm.CanConfirmSelected && game.Revision == revision && vm.IsSkillTargetSelectionPending,
                    "Cancel must clear a mandatory target draft and retain its current prompt.");
            }
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == targetSeats[0]));
        vm.SaveGameCommand.Execute(null);
        vm.LoadManualGameCommand.Execute(null);
        game = Program.Engine(vm);
        Program.Assert(vm.IsSkillTargetSelectionPending && !vm.HasSelection && game.Revision == revision,
            "Loading a pending target prompt must clear the local draft and restore legal portrait candidates.");
        var choice = game.PendingDecision!.Choices.Single(item => item.Targets.SequenceEqual([targetSeats[1]]));
        vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == targetSeats[1]));
        var confirmWindow = new MainWindow(vm);
        try
        {
            TutorialRoundTrip(vm, confirmWindow, "Zhefu target");
            var checkpoint = game.CreateCheckpoint();
            var reference = GameReplay.Restore(checkpoint, fixture.Registry);
            Accept(reference, new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, revision));
            Click((Button)confirmWindow.FindName("PlayCardButton"));
            Program.Assert(Boundary(game) == Boundary(reference),
                "The resumed real Zhefu confirmation must execute the original published choice and match an independent native command in every safe view and journal.");
        }
        finally { confirmWindow.Content = null; confirmWindow.DataContext = null; confirmWindow.Close(); }
        Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
            answer.Choice == choice.Id && !vm.HasSelection,
            "Confirm must submit exactly the original published choice once and clear the draft.");
        StableHandResponseFixture.RecordAndReplay(game, fixture.Registry, output, "Zhefu.target-confirm");
    }

    public static void MultipleOrderedAndOptionalTargets(string output)
    {
        foreach (var kind in new[] { "set", "ordered", "optional" })
        {
            var playerCount = kind == "set" ? 8 : 4;
            var registry = ContentRegistry.Build(new StandardContentPackage(), new TargetFixture(kind, playerCount));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord,
                PlayerCount = playerCount, ModeId = TargetFixture.ModeId, UseInteractiveSetup = true,
                AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
            Accept(game, new StartGameCommand());
            Accept(game, new SelectGeneralCommand(0, TargetFixture.OwnerId, game.Revision, game.PendingDecision!.PromptId));
            for (var step = 0; step < 30 && game.PendingDecision?.SkillPrompt?.SkillId != TargetFixture.SkillId; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.PendingDecision?.SkillPrompt?.SkillId == TargetFixture.SkillId, "The fixture must reach a real published target prompt.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var vm = new MainViewModel(false, 31, true, store, useExpandedContent: false, contentRegistry: registry);
            vm.LoadManualGameCommand.Execute(null);
            game = Program.Engine(vm);
            var revision = game.Revision;
            Program.Assert(vm.IsSkillTargetSelectionPending && vm.CenterSkillChoices.Count == 0, "Shared target sets must use portraits.");
            if (playerCount == 8) CheckEightSeatSize(vm, output);
            var choice = game.PendingDecision!.Choices.First(candidate => candidate.Targets.SequenceEqual(kind == "ordered" ? [2, 1] : [1, 2]));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 2));
            Program.Assert(vm.CanConfirmSelected == (kind == "optional") && game.Revision == revision, "Required pairs and optional subsets must follow their published target bounds.");
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 1));
            Program.Assert(vm.CanConfirmSelected && game.Revision == revision && vm.Seats.Count(seat => seat.IsSelectedTarget) == 2,
                "A complete legal pair must remain a reversible local draft.");
            vm.ClearSelectionCommand.Execute(null);
            Program.Assert(!vm.HasSelection && game.Revision == revision, "Esc must clear a pair without answering its prompt.");
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 2));
            vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 1));
            var confirmWindow = new MainWindow(vm);
            try
            {
                TutorialRoundTrip(vm, confirmWindow, $"{kind} target pair");
                if (kind == "optional") choice = game.PendingDecision!.Choices.Single(candidate => candidate.Targets.Count == 0);
                var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
                Accept(reference, new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, revision));
                if (kind == "optional") Click((Button)confirmWindow.FindName("CancelActionButton"));
                else Program.Assert(TableShortcutChecks.Input(confirmWindow, Key.Enter),
                    "Enter must confirm the actual resumed complete target draft.");
                Program.Assert(Boundary(game) == Boundary(reference),
                    "The resumed ordered/set confirmation or optional cancellation must preserve its native choice, target order and every player-safe view and journal.");
            }
            finally { confirmWindow.Content = null; confirmWindow.DataContext = null; confirmWindow.Close(); }
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == choice.Id,
                "Shared confirmation must preserve ordered pairs, canonical sets and the engine's optional decline choice.");
        }
    }

    private static void TutorialRoundTrip(MainViewModel vm, MainWindow window, string label)
    {
        var game = Program.Engine(vm);
        var before = Boundary(game);
        var prompt = game.CreateSnapshot(0).PendingDecision!.PromptId;
        var selected = vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).ToArray();
        var hint = vm.ActionHint;
        Program.Assert(selected.Length > 0 && vm.IsSkillTargetSelectionPending && vm.CanConfirmSelected,
            "The tutorial regression must start with a real complete portrait draft.");
        vm.StartTutorialCommand.Execute(null);
        Program.Assert(vm.IsTutorialActive && !ReferenceEquals(Program.Engine(vm), game),
            "The real tutorial command must independently suspend the original target prompt.");
        vm.ExitTutorialCommand.Execute(null);
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        Console.WriteLine($"Tutorial resume {label}: selected={string.Join(',', vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat))}, confirm={vm.CanConfirmSelected}.");
        Program.Assert(!vm.IsTutorialActive && ReferenceEquals(Program.Engine(vm), game) && Boundary(game) == before &&
            game.CreateSnapshot(0).PendingDecision!.PromptId == prompt && vm.IsSkillTargetSelectionPending &&
            vm.Seats.Where(seat => seat.IsSelectedTarget).Select(seat => seat.Seat).SequenceEqual(selected) &&
            vm.ActionHint == hint && vm.CanConfirmSelected &&
            ((Button)window.FindName("PlayCardButton")).IsEnabled &&
            ((Button)window.FindName("CancelActionButton")).IsEnabled,
            $"Tutorial return must preserve the complete {label} draft, its exact ordered hint, enabled native controls and untouched original prompt/journals.");
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Command!.CanExecute(button.CommandParameter),
            "The resumed target route must use its actual enabled bound button.");
        typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, null);
    }

    private static void CheckEightSeatSize(MainViewModel vm, string output)
    {
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var size in new[] { (1120, 740), (1920, 1080) })
            {
                Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"skill-target-eight-before-{size.Item1}.png"));
                var anchors = Program.Find<Button>(root).Where(button => button.DataContext is SeatViewModel &&
                    BattleFeedbackLayer.GetSeatAnchor(button) >= 0).ToArray();
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                var before = anchors.ToDictionary(button => ((SeatViewModel)button.DataContext).Seat, button => Bounds(button));
                vm.SelectTargetCommand.Execute(vm.Seats.Single(seat => seat.Seat == 2));
                Program.Render(root, size.Item1, size.Item2, Path.Combine(output, $"skill-target-eight-selected-{size.Item1}.png"));
                Program.Assert(anchors.Length == 8 && anchors.All(button => before[((SeatViewModel)button.DataContext).Seat] == Bounds(button)),
                    "Eight-player target drafts must add an outline without moving or shrinking any portrait.");
                vm.ClearSelectionCommand.Execute(null);
            }
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
    }

    private static void Reach(MainViewModel vm, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 50; step++)
        {
            if (Program.Engine(vm).PendingDecision is { PlayerSeat: 0 } prompt && predicate(prompt)) return;
            Program.Assert(vm.CanStepAi, "The fixture stopped at an unexpected human prompt.");
            vm.StepAiCommand.Execute(null);
        }
        throw new InvalidOperationException("The target prompt was not reached.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected a real accepted fixture command.");
    }

    internal sealed class TargetFixture(string kind, int playerCount) : IGameContentPackage
    {
        internal const string ModeId = "identity:skill-target-ui";
        internal const string OwnerId = "fixture:skill-target-owner";
        internal const string SkillId = "fixture:skill-target";
        public PackageManifest Manifest { get; } = new("fixture-skill-target-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var targetKind = kind == "ordered" ? "livingPairDistinct" : kind == "optional" ? "otherLivingWithHand" : "anyLiving";
            var minimum = kind == "optional" ? 0 : 2;
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:skill-target","revision":1,
                "triggers":[{"id":"targets","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"selectTargets","target":"owner","targetKind":"{{targetKind}}","minimumTargets":{{minimum}},"maximumTargets":2,"targetAiOrder":"stable"},
                {"op":"draw","target":"owner","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:skill-target":{"name":"选人测试","description":"选择合法目标"}}}""");
            builder.AddSkill(new(SkillId, "选人测试", "选择合法目标") { Program = catalog.Programs[SkillId], ProgramPresentation = catalog.Presentations[SkillId] });
            builder.AddGeneral(new(OwnerId, "选人者", "supporter", SkillId, "wei", 4));
            var others = Enumerable.Range(1, playerCount - 1).Select(seat => $"fixture:skill-target-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "目标", "supporter", "standard:none", "shu", 4));
            builder.AddMode(new(ModeId, "选人测试", playerCount, playerCount,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = playerCount - 1 },
                "standard:basic-demo", GeneralCandidateCount: playerCount, GeneralPoolIds: [OwnerId, .. others]));
        }
    }
}
