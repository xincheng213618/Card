using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShiBatchUiChecks
{
    private static readonly string[] Heroes = ["mobile:shi-wei-yan", "mobile:shi-chen-qun", "mobile:shi-xiahou-shang"];
    private const string Peer = "fixture:shi-batch-ui-peer", Quiet = "fixture:shi-batch-ui-quiet", Deck = "fixture:shi-batch-ui-deck";

    public static void PublicProgressAndCommittedPreparationChoices(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var hero in Heroes)
        {
            var setup = new ShanheBattleSetup(97, 1, "shi-batch-ui", hero, registry.Generals[hero].BaseHp, 0, 0, 0, 0, [],
                hero == Heroes[0]
                    ? [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 2),
                        new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Alcohol, Suit.Diamond, 9)]
                    : [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 2)], [],
                Enumerable.Range(0, 3).Select(_ => new ShanheCombatantDefinition(Peer) { StartingHand = [] }).ToArray(), [], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4,
                HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Accept(new StartGameCommand());
            for (var step = 0; step < 60 && game.CreateSnapshot(0).PendingDecision is null; step++)
                Accept(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(game.CreateSnapshot(0).PendingDecision?.PlayerSeat == 0, "The formal general must reach its first real human interaction.");
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, 97, true, store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var window = new MainWindow(model);
            try
            {
                var root = (FrameworkElement)window.Content;
                Program.Render(root, 1120, 780, Path.Combine(output, hero.Replace(':', '-') + "-status.png"));
                if (hero == Heroes[0])
                {
                    Program.Assert(Chip("mobile:shi-zhongao").ProgressText == "进行中" && Chip("mobile:shi-kuanggu").ProgressText == "一级",
                        "The mission and initial granted skill must show their actual public state on the skill rail after loading.");
                    var native = Program.Engine(model);
                    var movementCount = native.CardMovements.Count;
                    var hp = native.CreateSnapshot(0).Players[0].Hp;
                    var cards = model.Hand.Take(3).Select(card => card.Id).ToArray();
                    Program.Assert(cards.Length == 3, "The real Wei Yan prompt needs independent cards for multiselect, undo and clear.");
                    SelectCard(cards[0]); AssertDraft([cards[0]], 0);
                    SelectCard(cards[1]); AssertDraft([cards[0], cards[1]], 0);
                    Choose("hp-1");
                    AssertDraft([cards[0], cards[1]], 1);
                    SelectCard(cards[0]); AssertDraft([cards[1]], 1);
                    SelectCard(cards[2]); AssertDraft([cards[1], cards[2]], 1);
                    Click((Button)window.FindName("CancelActionButton"));
                    AssertDraft([], 0);
                    SelectCard(cards[0]); SelectCard(cards[1]); Choose("hp-1");
                    Program.Assert(TableShortcutChecks.Input(window, Key.Escape), "Escape must reach the real table cancellation route.");
                    AssertDraft([], 0);
                    SelectCard(cards[0]); SelectCard(cards[1]); Choose("hp-1");
                    AssertDraft([cards[0], cards[1]], 1);

                    var selectedBoundary = Boundary(native);
                    model.SaveGameCommand.Execute(null);
                    model.LoadManualGameCommand.Execute(null);
                    native = Program.Engine(model);
                    Program.Assert(!model.HasSaveError && Boundary(native) == selectedBoundary,
                        "Manual save/load must replay the original unpaid frame, including both selected cards and the simultaneous HP plan.");
                    AssertDraft([cards[0], cards[1]], 1);
                    Program.Render(root, 1120, 780, Path.Combine(output, "shi-wei-yan-cost-selected.png"));
                    Program.Assert(native.CardMovements.Count == movementCount && native.CreateSnapshot(0).Players[0].Hp == hp &&
                        Program.Find<TextBlock>(root).Any(text => text.Text.Contains("弃置 2 张手牌，失去 1 点体力", StringComparison.Ordinal)),
                        "Two directly selected hands and HP selection must remain visibly accumulated after cold recovery without early payment.");
                    var prompt = native.CreateSnapshot(0).PendingDecision!;
                    var pay = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("branch") == "pay");
                    var expected = new AnswerPromptCommand(0, prompt.PromptId, pay.Id, native.Revision);
                    var reference = GameReplay.Restore(native.CreateCheckpoint(), registry);
                    var expectedResult = reference.Submit(expected);
                    Program.Assert(expectedResult.Accepted, expectedResult.Error?.Message ?? "The published native combined plan must remain legal.");
                    var commands = native.AcceptedCommands.Count;
                    Click((Button)window.FindName("PlayCardButton"));
                    Program.Assert(native.AcceptedCommands.Count == commands + 1 &&
                        CommandJson.Serialize([native.AcceptedCommands.Last()]) == CommandJson.Serialize([expected]) &&
                        Boundary(native) == Boundary(reference),
                        "Only the shared real confirm button may submit the exact current native combined payment once.");
                    for (var step = 0; step < 40 && native.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                        model.StepAiCommand.Execute(null);
                    Program.Assert(native.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } &&
                        native.CreateSnapshot(0).Players[0].Hp == hp - 1 && Chip("mobile:shi-zhuangshi").ProgressText == "2／1" &&
                        native.Events.Select(item => item.Payload).OfType<MobileShiWeiYanPhaseGrantEvent>() is var grants &&
                        grants.Count() == 1 && grants.Single() is { DiscardedCards: 2, LostHp: 1 } &&
                        native.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>()
                            .Count(item => item.SkillId == "mobile:shi-zhuangshi" && item.Amount == 1) == 1 &&
                        cards.Take(2).All(id => native.CardMovements.Count(move => move.CardId == id &&
                            move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile &&
                            move.Reason.Value == "skill.mobile-shi-wei-yan.discard") == 1),
                        "The final explicit native confirmation pays the selected plan once and publishes both remaining phase allowances.");
                    var paidBoundary = Boundary(native);
                    model.SelectSkillChoiceCommand.Execute(pay);
                    model.ConfirmSelectedCommand.Execute(null);
                    Program.Assert(Boundary(native) == paidBoundary,
                        "A retired combined-payment choice and repeated empty confirmation cannot repay either cost or the phase grant.");

                    void SelectCard(int id)
                    {
                        Layout(root);
                        Click(Bound(root, model.SelectCardCommand, model.Hand.Single(card => card.Id == id)));
                    }
                    void Choose(string branch)
                    {
                        Layout(root);
                        var choice = model.SkillChoices.Single(item => item.Parameters.GetValueOrDefault("branch") == branch);
                        var button = Program.Find<Button>(root).Single(item => ReferenceEquals(item.CommandParameter, choice) &&
                            item.Command is not null && item.Command.CanExecute(choice));
                        Click(button);
                    }
                    void AssertDraft(int[] selected, int loss)
                    {
                        Layout(root);
                        var frame = native.ResolutionStack.OfType<ProgramSkillFrame>().Last();
                        var pending = native.CreateSnapshot(0).PendingDecision;
                        Program.Assert(frame.MobileShiWeiYan is { Operation: "zhuangshi", Stage: 0 } draft &&
                            draft.Cards.SequenceEqual(selected) && draft.HpLoss == loss &&
                            model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToHashSet().SetEquals(selected) &&
                            pending?.SkillPrompt?.SkillId == "mobile:shi-zhuangshi" &&
                            pending.Choices.Where(choice => choice.Cards.Count == 1).All(choice =>
                                choice.Parameters.GetValueOrDefault("selected") == (selected.Contains(choice.Cards[0]) ? "true" : "false")) &&
                            pending.Choices.Where(choice => choice.Parameters.GetValueOrDefault("branch")?.StartsWith("hp-", StringComparison.Ordinal) == true)
                                .All(choice => choice.Parameters.GetValueOrDefault("selected") ==
                                    (choice.Parameters["branch"] == "hp-" + loss ? "true" : "false")) &&
                            native.CardMovements.Count == movementCount && native.CreateSnapshot(0).Players[0].Hp == hp &&
                            native.Events.All(item => item.Payload is not MobileShiWeiYanMissionEvent and not MobileShiWeiYanPhaseGrantEvent) &&
                            Chip("mobile:shi-zhongao").ProgressText == "进行中" &&
                            model.SelectionArea.CanConfirm == (selected.Length + loss > 0),
                            "Each direct card toggle and HP click must preserve every unpaid native selection and visual highlight; clear and Escape must reset only the plan without failing the mission.");
                    }
                }
                else if (hero == Heroes[1])
                    Program.Assert(Chip("mobile:shi-dingpin").ProgressText == "0/1" && Chip("mobile:shi-faen").ProgressText == "0/2",
                        "Round and turn limits must visibly expose their distinct unused quotas from the public projection.");
                else
                    Program.Assert(Program.Find<TextBlock>(root).Any(text => text.Text.Contains("两项都选", StringComparison.Ordinal)) &&
                        Program.Engine(model).CreateSnapshot(0).PendingDecision?.Choices.Count == 4,
                        "Tanfeng must visibly preselect either item, both, or cancellation before selecting targets or paying effects.");
                HumanSkillViewModel Chip(string id) => model.HumanSkillCards.Single(skill => skill.ContentId == id);
            }
            finally { window.Content = null; window.Close(); }
            void Accept(GameCommand command) { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Rejected Shi UI scene command."); }
        }
    }

    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new { game.Revision,
        Commands = CommandJson.Serialize(game.AcceptedCommands), game.CardMovements, game.ResolutionStack,
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray() });
    private static Button Bound(FrameworkElement root, ICommand command, object parameter) => Program.Find<Button>(root)
        .Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible && button.Command!.CanExecute(button.CommandParameter),
            "The actual bound WPF control must be enabled and visible before its click.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 780)); root.Arrange(new Rect(0, 0, 1120, 780)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-batch-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Heroes);
            builder.AddSkill(new(Quiet, "安静同桌", "保留原生交互。"));
            builder.AddGeneral(new(Peer, "同桌", "", Quiet, "wei", 8));
            builder.AddDeck(new(Deck, "固定牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "势将界面", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [.. Heroes, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
