using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DockSkillChoiceUiChecks
{
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

            void ResumePlay()
            {
                Reach(game, DecisionKind.PlayCard);
                Refresh();
            }

            void AssertDock(string stage, int count)
            {
                Program.Render(root, 1120, 740, Path.Combine(output, $"skill-dock-{stage}.png"));
                var dock = (ItemsControl)window.FindName("DockSkillChoices");
                var actionBar = (FrameworkElement)window.FindName("ActionBar");
                var buttons = Program.Find<Button>(dock).Where(button => button.Command == model.SelectSkillChoiceCommand).ToArray();
                Program.Assert(model.HasDockSkillChoices && !model.HasCenterChoices && model.DockSkillChoices.Count == count &&
                    ((FrameworkElement)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed &&
                    ((FrameworkElement)window.FindName("PrimaryActionButtons")).Visibility == Visibility.Collapsed &&
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
