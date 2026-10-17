using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class LeDaQiaoJudgmentUiChecks
{
    public static void QinSubslotsKeepOriginalCardsAndNativeChoices(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var prepared = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 3
        }, registry);
        Accept(prepared, new StartGameCommand());
        Accept(prepared, new SelectGeneralCommand(0, Fixture.Owner, prepared.Revision,
            prepared.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var step = 0; step < 60 && !IsPlacementOffer(prepared); step++)
        {
            Program.Assert(prepared.CreateSnapshot(0).PendingDecision is null,
                "The small real fixture must reach its first native Play-start offer without another human decision.");
            Accept(prepared, new AdvanceOneStepCommand(prepared.Revision));
        }
        Program.Assert(IsPlacementOffer(prepared), "The real opening must lead to Zixi's Play-start offer.");
        var original = prepared.CreateSnapshot(0).Players[0].Hand.OrderBy(card => card.Id).Take(2).ToArray();
        Program.Assert(original.Length == 2 && original.All(card => card.Kind == CardKind.Lightning &&
                card.QinJudgmentSlot is null && card.SkillMarkLabel?.Split(" / ").Contains("琴") == true) &&
            original.All(card => Events<QiqinInitialEntityMarkedEvent>(prepared).Count(mark =>
                mark.OriginalOwnerSeat == 0 && mark.CardId == card.Id && mark.PrintedKind == card.Kind) == 1),
            "The native initializer must mark the actual original Lightning entities; no fabricated card or judgment face is used.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && Views(Program.Engine(model)).SequenceEqual(Views(prepared)), model.SaveStatus);
            ClickChoice(model, root, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var game = Program.Engine(model);
            AssertPrivateDraft(game);
            var initialMoves = game.CardMovements.Count;
            ClickChoice(model, root, choice => Selects(choice, original[0].Id, CardKind.Indulgence));
            ClickChoice(model, root, choice => Branch(choice) == "undo" && choice.Cards.SequenceEqual([original[0].Id]));
            Program.Assert(game.ResolutionStack.OfType<ProgramSkillFrame>().Last().ZixiPlacement is
                { Paid: false, TargetSeat: null, Entities.Count: 0 }, "The actual undo command clears the unpaid native draft.");
            ClickChoice(model, root, choice => Selects(choice, original[1].Id, CardKind.Lightning));
            ClickChoice(model, root, choice => Branch(choice) == "cancel");
            Program.Assert(game.CardMovements.Count == initialMoves && Events<ZixiPlacementPaidEvent>(game).Length == 0 &&
                Events<ZixiPlacementCommittedEvent>(game).Length == 0 &&
                Events<ZixiPlacementCompletedEvent>(game).Count(done => !done.Paid) == 1,
                "Selecting, undoing and cancelling through actual UI choices cannot move a card or issue a paid placement.");
            Reach(model, () => Program.Engine(model).CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard);
            Program.Assert(model.CanEndTurn, "Cancellation must return to the real native Play menu.");
            model.EndTurnCommand.Execute(null);
            Reach(model, () => IsPlacementOffer(Program.Engine(model)));
            ClickChoice(model, root, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            ClickChoice(model, root, choice => Selects(choice, original[0].Id, CardKind.Indulgence));
            ClickChoice(model, root, choice => Selects(choice, original[1].Id, CardKind.Lightning));
            game = Program.Engine(model);
            AssertPrivateDraft(game);
            var draft = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
            Program.Assert(draft.ZixiPlacement is { Paid: false, TargetSeat: 1, Entities.Count: 2 } selected &&
                selected.Entities.Select(entity => entity.CardId).SequenceEqual(original.Select(card => card.Id)) &&
                selected.Entities.Select(entity => entity.SlotKind).SequenceEqual([CardKind.Indulgence, CardKind.Lightning]) &&
                game.CardMovements.Count == initialMoves,
                "The native Ending draft retains two real entities in different free slots of one selected recipient before payment.");
            var retired = model.SkillChoices.Single(choice => Branch(choice) == "confirm");
            var checkpointViews = Views(game);
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            model.SaveGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            Program.Assert(!model.HasSaveError && game.Revision == revision && Views(game).SequenceEqual(checkpointViews) &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Last().ZixiPlacement?.Entities.Count == 2,
                "Actual manual save/load replays the original private placement cursor and both independent slots.");
            var current = model.SkillChoices.Single(choice => Branch(choice) == "confirm");
            Program.Assert(current.Id == retired.Id && !ReferenceEquals(current, retired),
                "Reload must republish a new native choice object, even when its deterministic choice ID is unchanged.");
            model.SelectSkillChoiceCommand.Execute(retired);
            Program.Assert(game.Revision == revision && game.AcceptedCommands.Count == commands &&
                game.CardMovements.Count == initialMoves && Views(game).SequenceEqual(checkpointViews),
                "A retired private choice cannot pay or complete the loaded placement.");
            Layout(root);
            Program.Assert(model.IsHandResponsePending && !model.IsSkillTargetSelectionPending &&
                current.Targets.SequenceEqual([1]) &&
                model.HandResponseExtraChoices.Any(choice => ReferenceEquals(choice, current)),
                "The native confirm already carries the recipient fixed by prior selections; the existing payment UI exposes it as an exact extra response choice.");
            var confirmButton = BoundButton(root, model.SelectHandResponseChoiceCommand, current);
            Program.Render(root, 1120, 740, Path.Combine(output, "le-da-qiao-qin-placement-draft.png"));
            var expected = new AnswerPromptCommand(0, game.CreateSnapshot(0).PendingDecision!.PromptId, current.Id, revision);
            var reference = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Accept(reference, expected);
            Click(confirmButton);
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Count == commands + 1 &&
                CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
                Views(game).SequenceEqual(Views(reference)),
                "The real confirm button must submit precisely the current published native choice once.");
            AssertPaid(game, original, draft.Id, registry);
            AssertPublicFacesAndBadges(model, root, original);
            var finishedViews = Views(game);
            model.SelectSkillChoiceCommand.Execute(current);
            Program.Assert(game.Revision == revision + 1 && Views(game).SequenceEqual(finishedViews),
                "The completed prompt cannot answer a consecutive decision or pay the same original entities again.");
            model.SaveGameCommand.Execute(null);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            Program.Assert(!model.HasSaveError && Views(game).SequenceEqual(finishedViews), model.SaveStatus);
            AssertPaid(game, original, draft.Id, registry);
            AssertPublicFacesAndBadges(model, root, original);
            Program.Render(root, 1120, 740, Path.Combine(output, "le-da-qiao-qin-judgment-subslots.png"));
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void AssertPaid(GameEngine game, IReadOnlyList<CardSnapshot> original, long frameId, ContentRegistry registry)
    {
        var paid = Events<ZixiPlacementPaidEvent>(game);
        var placed = Events<ZixiQinPlacedEvent>(game).Where(entity => entity.FrameId == frameId).OrderBy(entity => entity.Index).ToArray();
        Program.Assert(paid.Length == 1 && paid[0].FrameId == frameId && paid[0].EntityCount == 2 &&
            placed.Length == 2 && placed.Select(entity => entity.CardId).SequenceEqual(original.Select(card => card.Id)) &&
            placed.Select(entity => entity.SlotKind).SequenceEqual([CardKind.Indulgence, CardKind.Lightning]) &&
            placed.All(entity => entity.Source.SkillId == "le:zixi" && entity.Source.BindingId == "end-place-qin" &&
                entity.Source.OwnerSeat == 0 && entity.GameplayHash == registry.Skills["le:zixi"].Program!.GameplayHash &&
                entity.TargetSeat == 1 && entity.BatchId == paid[0].BatchId && entity.PrintedKind == CardKind.Lightning &&
                game.CardMovements.Count(move => move.CardId == entity.CardId && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Judgment(1) && move.Sequence == entity.MovementSequence &&
                    move.Reason.Value == "skill-program.le-da-qiao.zixi.place") == 1) &&
            Events<ZixiPlacementCompletedEvent>(game).Count(done => done.FrameId == frameId && done.Paid && done.EntityCount == 2) == 1 &&
            Events<CardUseDeclaredEvent>(game).Length == 0,
            "Two Qin placements retain the original signed source/hash, one whole batch, one payment per entity and no delayed-trick Use.");
    }

    private static void AssertPublicFacesAndBadges(MainViewModel model, FrameworkElement root, IReadOnlyList<CardSnapshot> original)
    {
        var game = Program.Engine(model);
        var slots = new[] { CardKind.Indulgence, CardKind.Lightning };
        for (var viewer = 0; viewer < 4; viewer++)
        {
            var judgment = game.CreateSnapshot(viewer).Players[1].Judgment;
            Program.Assert(judgment.Count == 2, "The actual recipient exposes exactly two public judgment entities to every viewer.");
            for (var i = 0; i < original.Count; i++)
            {
                var card = judgment.Single(entity => entity.Id == original[i].Id);
                Program.Assert(card.Kind == original[i].Kind && card.Suit == original[i].Suit && card.Rank == original[i].Rank &&
                    card.DisplayName == original[i].DisplayName && card.RankText == original[i].RankText &&
                    card.QinJudgmentSlot == slots[i] && card.IdentityKind is null,
                    "An attached Qin retains its original ID, printed Lightning kind, suit and rank; its independent slot does not forge a delayed identity.");
            }
        }
        var seat = model.Seats.Single(player => player.Seat == 1);
        Program.Assert(seat.JudgmentText == "判定区 琴（乐） · 琴（闪）" && seat.JudgmentStatuses.Count == 2,
            "The actual recipient's public judgment summary must distinguish both Qin subslots.");
        Layout(root);
        for (var i = 0; i < original.Count; i++)
        {
            var entity = original[i];
            var status = seat.JudgmentStatuses.Single(item => item.CardId == entity.Id);
            var slotGlyph = i == 0 ? "乐" : "闪";
            Program.Assert(status.Kind == entity.Kind && status.QinJudgmentSlot == slots[i] && status.IsQin &&
                !status.IsLightning && status.Glyph == "琴" && status.SlotGlyph == slotGlyph &&
                status.Name == $"琴（{slotGlyph}）" && status.Tooltip.Contains("原牌：闪电", StringComparison.Ordinal) &&
                status.Tooltip.Contains(entity.RankText, StringComparison.Ordinal) && status.Tooltip.Contains("♠", StringComparison.Ordinal) &&
                status.Tooltip.Contains("不执行判定", StringComparison.Ordinal),
                "The displayed badge and tooltip expose the Qin slot while preserving the actual original face and non-judgment behavior.");
            var glyphs = Program.Find<TextBlock>(root).Where(text => text.Name == "JudgmentGlyph" &&
                text.DataContext is JudgmentStatusViewModel item && item.CardId == entity.Id).ToArray();
            var badges = Program.Find<Border>(root).Where(border => border.Name == "QinSlotBadge" &&
                border.DataContext is JudgmentStatusViewModel item && item.CardId == entity.Id).ToArray();
            var lightningPaths = Program.Find<System.Windows.Shapes.Path>(root).Where(path => path.Name == "LightningGlyph" &&
                path.DataContext is JudgmentStatusViewModel item && item.CardId == entity.Id).ToArray();
            Program.Assert(glyphs.Length > 0 && glyphs.All(text => text.Text == "琴" && text.Visibility == Visibility.Visible) &&
                badges.Length > 0 && badges.All(border => border.Visibility == Visibility.Visible &&
                    Program.Find<TextBlock>(border).Single().Text == slotGlyph) &&
                lightningPaths.Length > 0 && lightningPaths.All(path => path.Visibility == Visibility.Collapsed),
                "The real table template must render its Qin glyph plus slot corner badge and suppress the true Lightning bolt for a printed Lightning Qin.");
        }
    }

    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch");
    private static bool Selects(PromptChoice choice, int id, CardKind slot) => Branch(choice) == "select" &&
        choice.Cards.SequenceEqual([id]) && choice.Targets.SequenceEqual([1]) &&
        choice.Parameters.GetValueOrDefault("slot") == (slot == CardKind.Indulgence ? "indulgence" : "lightning");
    private static bool IsPlacementOffer(GameEngine game) => game.CreateSnapshot(0).PendingDecision is
        { PlayerSeat: 0, SkillPrompt.SkillId: "le:zixi" } prompt &&
        prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void AssertPrivateDraft(GameEngine game) => Program.Assert(game.CreateSnapshot(0).PendingDecision is
        { PlayerSeat: 0, IsPrivate: true } prompt && prompt.Choices.All(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "zixi-placement") &&
        Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).PendingDecision is null),
        "Only the actual chooser sees the unpublished Qin/slot selection before the real placement is paid.");
    private static void Reach(MainViewModel model, Func<bool> done)
    {
        for (var step = 0; step < 40; step++)
        {
            if (done()) return;
            Program.Assert(model.CanStepAi && model.StepAiCommand.CanExecute(null),
                "The bounded native continuation must not skip an unhandled human choice.");
            model.StepAiCommand.Execute(null);
        }
        throw new InvalidOperationException("The small native Zixi fixture did not reach its next real boundary.");
    }
    private static void ClickChoice(MainViewModel model, FrameworkElement root, Func<PromptChoice, bool> predicate)
    {
        var game = Program.Engine(model);
        var choice = model.SkillChoices.Single(predicate);
        var before = game.Revision;
        Layout(root);
        var panelName = model.HasDockSkillChoices ? "DockSkillChoices" : "ScrollingSkillChoices";
        var panel = Program.Find<ItemsControl>(root).Single(control => control.Name == panelName);
        Click(BoundButton(panel, model.SelectSkillChoiceCommand, choice));
        Program.Assert(game.Revision == before + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.Choice == choice.Id,
            "The actual enabled choice button must submit exactly its current native prompt parameter.");
    }
    private static Button BoundButton(FrameworkElement root, ICommand command, object parameter) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The actual bound control must be visible and enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }
    private static T[] Events<T>(GameEngine game) => game.Events.Select(entry => entry.Payload).OfType<T>().ToArray();
    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray();
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "A native Qin fixture command was rejected."); }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Owner = "fixture:zixi-ui-owner";
        internal const string Mode = "identity:classic-zixi-ui";
        public PackageManifest Manifest { get; } = new("fixture-zixi-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (bundle, id) in new[] { ("le-xiao-qiao", "le:qiqin"), ("le-da-qiao", "le:zixi") })
            {
                var assembly = typeof(StandardContentPackage).Assembly;
                using var rules = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.rules.json")!);
                using var presentation = new StreamReader(assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.presentation.json")!);
                var catalog = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            }
            builder.AddGeneral(new(Owner, "姊希副槽本人", "supporter", "le:qiqin", "wu", 4, ["le:zixi"], GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(seat => "fixture:zixi-ui-peer-" + seat).ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "副槽目标", "supporter", "standard:none", "shu", 4));
            builder.AddDeck(new("fixture:zixi-ui-deck", "真实闪电琴实体", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 40).Select(index => new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实姊希副槽显示", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:zixi-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
