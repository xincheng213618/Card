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

internal static class QizhengUiChecks
{
    public static void NativeChoicesRemainReachable(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 6
        }, registry);
        Accept(new StartGameCommand());
        Reach(() => game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard);
        Use();
        Reach(() => game.CreateSnapshot(0).PendingDecision?.IncomingCard == CardKind.QizhengXiangsheng);
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
            AssertChoices("designation", 2);
            Program.Assert(model.CurrentDecisionContext!.Title.Contains("奇正相生", StringComparison.Ordinal),
                "The native card prompt must identify Qizheng instead of an unnamed skill.");
            Click(c => c.Parameters.GetValueOrDefault("value") == "regular");
            Reach(() => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "hand") == true);
            Refresh();
            AssertChoices("gain", game.CreateSnapshot(1).Players[1].HandCount);
            var before = game.CreateSnapshot(1).Players[1].HandCount;
            Click(c => c.Parameters.GetValueOrDefault("branch") == "hand");
            Reach(() => game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard);
            Program.Assert(game.CreateSnapshot(1).Players[1].HandCount == before - 1 &&
                game.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) &&
                    m.Reason.Value == "card.effect.qizheng.obtain") == 1, "The visible gain choice must obtain exactly one actual card.");
            Use();
            Reach(() => game.CreateSnapshot(0).PendingDecision?.IncomingCard == CardKind.QizhengXiangsheng);
            Refresh();
            var hp = game.CreateSnapshot(1).Players[1].Hp;
            AssertChoices("designation-ambush", 2);
            Click(c => c.Parameters.GetValueOrDefault("value") == "ambush");
            Reach(() => game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard);
            Program.Assert(game.CreateSnapshot(1).Players[1].Hp == hp - 1, "The other designation must continue to native damage.");
            Accept(new EndPlayPhaseCommand(0, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
            Reach(() => game.CreateSnapshot(0).PendingDecision is { IncomingCard: CardKind.QizhengXiangsheng } p &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "pass"));
            Refresh();
            AssertChoices("response", 1);
            Program.Assert(model.SkillChoices.All(c => !c.Parameters.Values.Any(value => value is "ambush" or "regular")),
                "The response surface must retain the private designation boundary.");
            var use = game.ResolutionStack.OfType<CardUseFrame>().Last(f => f.Qizheng is not null).Id;
            Click(c => c.Parameters.GetValueOrDefault("branch") == "pass");
            Reach(() => game.ResolutionStack.OfType<CardUseFrame>().All(f => f.Id != use));
        }
        finally { window.Content = null; window.Close(); }

        void AssertChoices(string stage, int count)
        {
            Program.Render(root, 1120, 780, Path.Combine(output, $"qizheng-{stage}.png"));
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var buttons = Program.Find<Button>(root)
                .Where(b => b.Command == model.SelectSkillChoiceCommand && b.Visibility == Visibility.Visible && b.IsEnabled &&
                    b.ActualWidth >= 40 && b.ActualHeight >= 24).ToArray();
            Program.Assert(prompt is { Kind: DecisionKind.ProgramTrigger, SkillPrompt: null } &&
                model.IsSkillSelectionPending && (model.HasDockSkillChoices || model.HasTargetCardPicker) && model.SkillChoices.Count == count &&
                buttons.Length == count, "Each native Qizheng choice must have an actual reachable button after restore.");
            if (stage == "gain")
                Program.Assert(model.HasTargetCardPicker && !model.HasCardNameSelection && model.TargetCardRegions is [{ Zone: CardZoneKind.Hand }] &&
                    buttons.All(button => Program.Find<ContentControl>(button).Any(control =>
                        control.Content is CardGame.Wpf.Controls.PromptCardChoicePresentation { IsHidden: true, Face: null })),
                    "Qizheng's opaque obtain choices must reuse the same hand region and private backs as Snatch.");
        }
        void Click(Func<PromptChoice, bool> predicate)
        {
            var choice = model.SkillChoices.First(predicate);
            var button = Program.Find<Button>(root)
                .Single(b => b.Command == model.SelectSkillChoiceCommand && ReferenceEquals(b.CommandParameter, choice));
            var revision = game.Revision;
            typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                answer.Choice == choice.Id, "The actual Qizheng button must commit its published choice once.");
        }
        void Refresh() => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [game.CreateSnapshot(0)]);
        void Accept(GameCommand command)
        { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var step = 0; step < 200 && !done(); step++) Accept(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(done(), "The bounded Qizheng fixture did not reach the expected native boundary.");
        }
        void Use()
        {
            var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.QizhengXiangsheng && a.TargetSeat == 1);
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public const string Mode = "identity:qizheng-ui";
        public PackageManifest Manifest { get; } = new("fixture-qizheng-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddCard(new("fixture:qizheng-ui", "奇正相生", "锦囊牌", "原生奇正相生", CardKind.QizhengXiangsheng));
            var generals = Enumerable.Range(0, 4).Select(i => $"fixture:qizheng-ui-{i}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new(id, "奇正测试", "supporter", "standard:none", "wei", 12));
            builder.AddDeck(new("fixture:qizheng-ui-deck", "奇正牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("fixture:qizheng-ui", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(Mode, "奇正界面", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:qizheng-ui-deck", GeneralPoolIds: generals));
        }
    }
}
