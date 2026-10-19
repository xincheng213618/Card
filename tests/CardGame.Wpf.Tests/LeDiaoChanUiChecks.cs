using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class LeDiaoChanUiChecks
{
    public static void OriginalMarksAndWholeHandSwapUseNativeChoices(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var prepared = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4,
            HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 2 }, registry);
        Accept(prepared, new StartGameCommand());
        Accept(prepared, new SelectGeneralCommand(0, Fixture.Owner, prepared.Revision,
            prepared.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var i = 0; i < 60 && prepared.CreateSnapshot(0).PendingDecision?.SkillPrompt?.SkillId != "le:tanban"; i++)
        {
            Program.Assert(prepared.CreateSnapshot(0).PendingDecision is null, "No unrelated human prompt may precede the native Draw-end offer.");
            Accept(prepared, new AdvanceOneStepCommand(prepared.Revision));
        }
        var before = prepared.CreateSnapshot(0);
        Program.Assert(before.PendingDecision?.SkillPrompt?.SkillId == "le:tanban" && before.Players[0].Hand.Count == 6,
            "The tiny actual opening must reach the native Draw-end Tanban offer with four opening cards and two drawn cards.");
        var original = before.Players[0].Hand.Where(Marked).Select(c => c.Id).ToHashSet();
        Program.Assert(original.Count == 4 && before.Players[0].Hand.Count(c => !Marked(c)) == 2,
            "Only real opening entities initially have Tanban; ordinary Draw-phase cards remain unmarked.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
        using var model = new MainViewModel(false, prepared.Seed, true, store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Layout(root);
            var game = Program.Engine(model);
            Program.Assert(model.Hand.Count == 6 && model.Hand.All(c => c.OriginalHandTag.Contains("檀板", StringComparison.Ordinal) == original.Contains(c.Id)),
                "The restored real hand must bind every original entity's authoritative Tanban badge.");
            Program.Assert(Program.Find<TextBlock>(root).Any(t => t.Visibility == Visibility.Visible && t.Text.Contains("檀板", StringComparison.Ordinal)),
                "An actual visible hand control must display its Tanban mark.");
            foreach (var seat in Enumerable.Range(1, 3))
                Program.Assert(game.CreateSnapshot(seat).Players[0].Hand.Count == 0 && game.CreateSnapshot(seat).PendingDecision is null,
                    "Other viewers receive no private original entities or Draw-end answer choices.");
            var choice = model.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            var button = Program.Find<Button>((ItemsControl)window.FindName("ScrollingSkillChoices")).Single(b => ReferenceEquals(b.Command, model.SelectSkillChoiceCommand) && ReferenceEquals(b.CommandParameter, choice));
            Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The native Tanban activation button must be visible and enabled.");
            var commands = game.AcceptedCommands.Count; var movements = game.CardMovements.Count;
            var prompt = game.CreateSnapshot(0).PendingDecision!.PromptId;
            button.Command!.Execute(button.CommandParameter);
            Layout(root);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                answer.PromptId == prompt && answer.Choice == choice.Id && game.CardMovements.Count == movements,
                "The real button must answer exactly the published prompt without paying, replacing or moving a Hand entity.");
            Program.Assert(model.Hand.Count == 6 && model.Hand.All(c => c.OriginalHandTag.Contains("檀板", StringComparison.Ordinal) != original.Contains(c.Id)),
                "The whole real Hand must refresh all six badges after the atomic mark exchange.");
            Program.Render(root, 1120, 780, Path.Combine(output, "le-diao-chan-tanban-swap.png"));
        }
        finally { window.DataContext = null; window.Close(); }
    }
    private static bool Marked(CardSnapshot c) => c.SkillMarkLabel?.Split(" / ").Contains("檀板") == true;
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Le Diao Chan UI command rejected."); }
    private static void Layout(FrameworkElement root)
    { root.Measure(new Size(1120, 780)); root.Arrange(new Rect(0, 0, 1120, 780)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout(); }
    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:le-diao-chan-ui", Owner = "fixture:le-diao-chan-ui-owner";
        public PackageManifest Manifest { get; } = new("fixture-le-diao-chan-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "le:diao-chan");
            const string pick = "fixture:le-diao-chan-ui-pick";
            b.AddSkill(new(pick, "本人", "固定公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(Owner, "檀板交互", "le-diao-chan", pick, "qun", 3, ["le:tanban"], Gender: GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:le-diao-chan-ui-peer-" + i).ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, "旁观角色", "default", "standard:none", FactionId: "wei"));
            const string deck = "fixture:le-diao-chan-ui-deck";
            b.AddDeck(new(deck, "固定小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 40).Select(i =>
                new ContentDeckPhysicalCard("standard:dodge", new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "檀板交互", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                deck, GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
