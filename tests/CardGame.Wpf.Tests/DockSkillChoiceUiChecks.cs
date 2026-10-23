using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DockSkillChoiceUiChecks
{
    public static void DinghanNamesWrapAndCommit(string output)
    {
        SharedCardNamePresentation();
        var registry = ContentRegistry.Build(new StandardContentPackage(), new DinghanFixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = DinghanFixture.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game.Submit(new SelectGeneralCommand(0, "mobile:shen-xun-yu", game.Revision, game.PendingDecision!.PromptId)));
        ReachEdit();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            AssertChoices(1120, 740);
            AssertChoices(1600, 900);
            Click(nameof(CardKind.Duel));
            Program.Assert(Changes().Select(change => change.Added).SequenceEqual([true]),
                "The actual Dinghan button must add exactly one selected name.");
            ReachEdit(); Refresh(); AssertChoices(1120, 740);
            Program.Assert(model.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Duel))
                .Description.StartsWith("移除", StringComparison.Ordinal), "The next native turn must offer removal of the recorded name.");
            Click(nameof(CardKind.Duel));
            Program.Assert(Changes().Select(change => change.Added).SequenceEqual([true, false]),
                "The next actual Dinghan button must remove that name exactly once.");
            ReachEdit(); Refresh(); AssertChoices(1120, 740); Click("cancel");
            Program.Assert(Changes().Count() == 2 && !model.HasCenterChoices && model.SkillChoices.Count == 0,
                "Skipping must finish the decision without changing Dinghan records or leaving stale buttons.");

            IEnumerable<DinghanNameChangedEvent> Changes() => game.Events.Select(entry => entry.Payload)
                .OfType<DinghanNameChangedEvent>().Where(change => change.Owner == 0 && change.Kind == CardKind.Duel);

            void Refresh() => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [game.CreateSnapshot(0)]);

            void AssertChoices(int width, int height)
            {
                var viewport = (ScrollViewer)window.FindName("CardNameSelectionViewport");
                viewport.ScrollToTop();
                Program.Render(root, width, height, Path.Combine(output, $"dinghan-choices-{width}.png"));
                var choices = (ItemsControl)window.FindName("CardNameSelectionChoices");
                var buttons = Program.Find<Button>(choices).Where(button => button.Command == model.SelectSkillChoiceCommand).ToArray();
                Program.Assert(model.SkillChoices.Count > 12 && !model.HasDockSkillChoices && !model.HasCenterChoices &&
                    model.HasCardNameSelection && model.UngroupedCenterSkillChoices.Count == 0 &&
                    model.CardNameSelectionChoices.SequenceEqual(model.SkillChoices, ReferenceEqualityComparer.Instance) &&
                    buttons.Length == model.SkillChoices.Count,
                    "Dinghan's complete catalog must use the lower name selection area with its exact original choices.");
                var names = buttons.SelectMany(button => Program.Find<ContentControl>(button))
                    .Select(control => control.Content).OfType<PromptCardChoicePresentation>()
                    .Where(presentation => presentation.IsCardName).ToArray();
                Program.Assert(names.Length == model.SkillChoices.Count - 1 && names.All(name => !name.IsCard && name.Face is null) &&
                    names.Single(name => name.NamedCard!.Kind == CardKind.Duel).NamedCard!.IsRecorded ==
                        model.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Duel))
                            .Description.StartsWith("移除", StringComparison.Ordinal),
                    "Dinghan must show the shared card-name tiles with the current recorded state and no physical suit or rank.");
                var bounds = buttons.Select(button => button.TransformToAncestor(root)
                    .TransformBounds(new Rect(button.RenderSize))).ToArray();
                Program.Assert(buttons.All(button => button.IsEnabled) && bounds.All(rect => rect.Width >= 70 && rect.Height >= 28) &&
                    bounds.Select(rect => Math.Round(rect.Top)).Distinct().Count() > 1 && viewport.ViewportHeight > 28 &&
                    viewport.TranslatePoint(new Point(), root).Y >=
                        ((FrameworkElement)window.FindName("ActionBar")).TranslatePoint(new Point(), root).Y,
                    "The real name buttons must retain readable screen dimensions and wrap at both supported window sizes.");
                viewport.ScrollToBottom(); root.UpdateLayout();
                var cancel = buttons.Single(button => ((PromptChoice)button.CommandParameter).Parameters.GetValueOrDefault("kind") == "cancel");
                var point = cancel.TranslatePoint(new Point(cancel.ActualWidth / 2, cancel.ActualHeight / 2), viewport);
                Program.Assert(point.X >= 0 && point.X < viewport.ViewportWidth && point.Y >= 0 && point.Y < viewport.ViewportHeight,
                    "The final skip choice must remain reachable by scrolling the name list.");
            }

            void Click(string kind)
            {
                var choice = model.SkillChoices.Single(candidate => candidate.Parameters.GetValueOrDefault("kind") == kind);
                var button = Program.Find<Button>((ItemsControl)window.FindName("CardNameSelectionChoices"))
                    .Single(candidate => ReferenceEquals(candidate.CommandParameter, choice));
                var revision = game.Revision;
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.Choice == choice.Id, "Each real name button must submit its exact published choice once.");
            }
        }
        finally { window.Content = null; window.Close(); }

        void ReachEdit()
        {
            for (var step = 0; step < 200; step++)
            {
                var prompt = game.CreateSnapshot(0).PendingDecision;
                if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "dinghan-edit")) return;
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "select-god-faction"))
                    Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                        prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("faction-id") == "wei").Id, game.Revision)));
                else
                {
                    Program.Assert(prompt is null || prompt.PlayerSeat != 0, "The Dinghan fixture must preserve other human decisions.");
                    Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
                }
            }
            throw new InvalidOperationException("The bounded fixture did not reach Dinghan's next turn-start edit.");
        }
    }

    private static void SharedCardNamePresentation()
    {
        foreach (var (key, action) in new[] { ("recorded-kind", "named-ambush"), ("card-name", "declare-name"),
            ("played-kind", "select-play-conversion-mode"), ("card-kind", "use-all-hand-as-ordinary-trick") })
        {
            var choice = new PromptChoice(new("shared-name"), "选择【决斗】", [], [],
                new Dictionary<string, string> { ["program-action"] = action, [key] = nameof(CardKind.Duel) });
            var name = CardNameChoicePresentation.From(choice);
            Program.Assert(name is { Kind: CardKind.Duel, Name: "决斗", IsRecorded: false } &&
                CardNameChoicePresentation.From(choice with { Cards = [42] }) is null &&
                CardNameChoicePresentation.From(choice with { Parameters = new Dictionary<string, string> { [key] = "cancel" } }) is null,
                "Record, declaration and view-as choices must share named-card presentation while real materials and cancel retain their own surfaces.");
        }
    }

    public static void QuantityActivationAndEffectsUseSameDock(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game.Submit(new SelectGeneralCommand(0, Fixture.GeneralId, game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, DecisionKind.PlayCard);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var qimou = model.HumanActiveSkillActions.Single(action => action.ProgramSkillId == "boundary:qimou-current");
            model.SelectActiveSkillCommand.Execute(qimou);
            Program.Assert(model.SkillChoices.Count > 3, "The quantity fixture must cover more than three choices.");
            AssertDock("quantity", model.SkillChoices.Count);
            var nativeView = model.ChoiceSnapshot;
            var revisionBeforeProjection = game.Revision;
            Project(nativeView with { PublicRevealedCards = [nativeView.Players[0].Hand[0]] });
            AssertDock("public-cards", model.SkillChoices.Count, publicCards: true);
            Program.Assert(model.UngroupedCenterSkillChoices.Count == 0 && !model.HasPinnedPublicModuleChoices,
                "Public faces may stay central, but their small text alternatives must appear only below.");
            Project(nativeView with { PendingDecision = nativeView.PendingDecision! with
                { Kind = DecisionKind.ProgramJudgmentTrigger, SkillPrompt = null } });
            AssertDock("legacy-prompt", model.SkillChoices.Count);
            Project(nativeView with { PendingDecision = nativeView.PendingDecision! with { Kind = DecisionKind.RespondDodge } });
            AssertDock("response-alternatives", model.HandResponseExtraChoices.Count, primaryActions: true);
            Program.Assert(model.DockSkillChoiceCommand == model.SelectHandResponseChoiceCommand &&
                model.CenterHandResponseExtraChoices.Count == 0,
                "Small response alternatives must use the native response command and leave no duplicate central buttons.");
            // Route a larger published view through the scroll area, then restore the native prompt before any command.
            var catalogChoices = Enumerable.Range(1, 9).Select(index => new PromptChoice(new($"layout-option-{index}"),
                $"选择效果 {index}", [], [], new Dictionary<string, string>
                    { ["program-action"] = "choose-option", ["option-id"] = $"effect-{index}" })).ToArray();
            Project(nativeView with { PendingDecision = nativeView.PendingDecision! with { Choices = catalogChoices } });
            Program.Render(root, 1120, 740, Path.Combine(output, "skill-dock-long-catalog.png"));
            var catalog = (ItemsControl)window.FindName("CardNameSelectionChoices");
            Program.Assert(model.HasExpandedSelection && !model.HasCardNameSelection && !model.HasCenterChoices &&
                !model.HasDockSkillChoices && model.SelectionArea.CatalogChoices.SequenceEqual(catalogChoices, ReferenceEqualityComparer.Instance) &&
                catalog.Items.Count == catalogChoices.Length && Program.Find<Button>(catalog).All(button => button.IsEnabled && button.ActualHeight >= 24),
                "Larger effect catalogs must use the common lower scroll area without hiding or rescaling their published choices.");
            Project(nativeView);
            AssertDock("quantity-restored", model.SkillChoices.Count);
            Program.Assert(game.Revision == revisionBeforeProjection,
                "Routing checks over player-view shapes must not change the native pending decision.");
            Click(choice => choice.Parameters.GetValueOrDefault("quantity") == "1");
            ResumePlay();

            foreach (var benefit in new[] { "draw", "recover" })
            {
                var slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
                Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId)));
                Refresh();
                AssertDock(benefit + "-activation", 2);
                Click(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
                AssertDock(benefit + "-effects", 2);
                Program.Assert(model.DockSkillChoices.Select(choice => choice.Description).SequenceEqual(["回复1点体力", "摸一张牌"]),
                    "Kuanggu effects must use the same lower action buttons as its activation prompt.");
                var before = game.CreateSnapshot(0).Players[0];
                Click(choice => choice.Parameters.GetValueOrDefault("option-id") == benefit);
                ResumePlay();
                var after = game.CreateSnapshot(0).Players[0];
                Program.Assert(benefit == "recover" ? after.Hp == before.Hp + 1 : after.HandCount == before.HandCount + 1,
                    "The dock button must commit the selected native Kuanggu effect exactly once.");
            }
            Program.Assert(!model.HasDockSkillChoices && model.DockSkillChoices.Count == 0,
                "Finishing skill resolution must retire the dock choices.");

            void Refresh() => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [game.CreateSnapshot(0)]);
            void Project(GameSnapshot view) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [view]);

            void ResumePlay()
            {
                Reach(game, DecisionKind.PlayCard);
                Refresh();
            }

            void AssertDock(string stage, int count, bool publicCards = false, bool primaryActions = false)
            {
                Program.Render(root, 1120, 740, Path.Combine(output, $"skill-dock-{stage}.png"));
                var dock = (ItemsControl)window.FindName("DockSkillChoices");
                var actionBar = (FrameworkElement)window.FindName("ActionBar");
                var buttons = Program.Find<Button>(dock).Where(button => button.Command == model.DockSkillChoiceCommand).ToArray();
                Program.Assert(model.HasDockSkillChoices && model.HasCenterChoices == publicCards && model.DockSkillChoices.Count == count &&
                    model.SelectionArea.Choices.SequenceEqual(model.DockSkillChoices, ReferenceEqualityComparer.Instance) &&
                    model.SelectionArea.ChoiceCommand == model.DockSkillChoiceCommand &&
                    ((FrameworkElement)window.FindName("DecisionPanel")).Visibility == (publicCards ? Visibility.Visible : Visibility.Collapsed) &&
                    ((FrameworkElement)window.FindName("PrimaryActionButtons")).Visibility == (primaryActions ? Visibility.Visible : Visibility.Collapsed) &&
                    buttons.Length == count && buttons.All(button => button.Visibility == Visibility.Visible && button.IsEnabled &&
                        button.ActualWidth > 0 && button.ActualHeight > 0 &&
                        model.SkillChoices.Any(choice => ReferenceEquals(choice, button.CommandParameter))) &&
                    dock.TranslatePoint(new Point(), actionBar).Y >= 0 &&
                    dock.TranslatePoint(new Point(0, dock.ActualHeight), actionBar).Y <= actionBar.ActualHeight,
                    $"Quantity, activation and effect choices must remain usable inside the lower dock ({stage}): " +
                    $"dock={model.HasDockSkillChoices}, center={model.HasCenterChoices}, choices={model.DockSkillChoices.Count}/{count}, " +
                    $"buttons={buttons.Length}, decision={((FrameworkElement)window.FindName("DecisionPanel")).Visibility}, " +
                    $"primary={((FrameworkElement)window.FindName("PrimaryActionButtons")).Visibility}, " +
                    $"bounds={dock.TranslatePoint(new Point(), actionBar).Y}..{dock.TranslatePoint(new Point(0, dock.ActualHeight), actionBar).Y}/{actionBar.ActualHeight}.");
            }

            void Click(Func<PromptChoice, bool> predicate)
            {
                var choice = model.DockSkillChoices.Single(predicate);
                var button = Program.Find<Button>((ItemsControl)window.FindName("DockSkillChoices"))
                    .Single(candidate => ReferenceEquals(candidate.CommandParameter, choice));
                var revision = game.Revision;
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.Choice == choice.Id, "Each actual dock button must submit its current published choice once.");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 48; step++)
        {
            if (game.PendingDecision?.Kind == kind) return;
            Program.Assert(game.PendingDecision is null || game.PendingDecision.PlayerSeat != 0,
                "The bounded fixture must not advance past another human prompt.");
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException($"The skill dock fixture did not reach {kind}.");
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Command rejected.");

    private sealed class DinghanFixture : IGameContentPackage
    {
        internal const string ModeId = "identity:dinghan-ui";
        public PackageManifest Manifest { get; } = new("fixture-dinghan-ui", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "mobile:shen-xun-yu");
            var generals = new List<string> { "mobile:shen-xun-yu" };
            for (var seat = 1; seat < 4; seat++)
            {
                var id = $"fixture:dinghan-ui-{seat}";
                builder.AddGeneral(new(id, $"对手{seat}", "supporter", "standard:none", "wei", 12));
                generals.Add(id);
            }
            builder.AddDeck(new("fixture:dinghan-ui-deck", "固定闪牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 96).Select(index =>
                    new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "定汉选项", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:dinghan-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: generals));
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string ModeId = "identity:skill-dock";
        internal const string GeneralId = "fixture:skill-dock";
        public PackageManifest Manifest { get; } = new("fixture-skill-dock", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "boundary:wei-yan");
            builder.AddGeneral(new(GeneralId, "魏延", "supporter", "boundary:qimou-current", "shu", 4,
                ["boundary:kuanggu"], GeneralGender.Male) { InitialHp = 3 });
            var generals = new List<string> { GeneralId };
            for (var seat = 1; seat < 4; seat++)
            {
                var id = $"fixture:skill-dock-{seat}";
                builder.AddGeneral(new(id, $"对手{seat}", "supporter", "standard:none", "wei", 4));
                generals.Add(id);
            }
            builder.AddDeck(new("fixture:skill-dock-deck", "固定杀牌堆", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 96).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "技能选项", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:skill-dock-deck", GeneralCandidateCount: 4, GeneralPoolIds: generals));
        }
    }
}
