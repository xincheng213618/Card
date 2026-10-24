using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class KongShuUiChecks
{
    public static void FuchaoIncrementalDiscardKeepsPickUndoAndFinishVisible(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "fixture:fuchao-ui", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(new StartGameCommand());
        for (var step = 0; step < 100; step++)
        {
            var p = game.PendingDecision;
            if (p is { PlayerSeat: 0, SkillPrompt.SkillId: "ol:fuchao" }) break;
            if (p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral })
                Accept(new SelectGeneralCommand(0, "fixture:fuchao-human", game.Revision, p.PromptId));
            else Accept(new AdvanceOneStepCommand(game.Revision));
        }
        Program.Assert(game.PendingDecision is { PlayerSeat: 0, SkillPrompt.SkillId: "ol:fuchao" } &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Last().KongShu is { DiscardCount: 2 },
            "Two actually chained players must reach the real private two-card Fuchao discard.");
        var store = new MemorySaveStore();
        Save();
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            var moves = game.CardMovements.Count;
            CheckChoices();
            var choice = game.CreateSnapshot(0).PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == "pick");
            var first = choice.Cards.Single();
            Accept(new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision));
            Program.Assert(game.CardMovements.Count == moves && game.PendingDecision!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "unpick"),
                "A native partial selection retains undo without moving the unpaid cards.");
            Save(); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model); CheckChoices();
            Program.Assert(model.Hand.Single(card => card.Id == first).IsSelected,
                "Cold recovery highlights the actual selected entity from its native undo choice.");
            model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == first));
            Program.Assert(!Branches().Contains("unpick") && game.CardMovements.Count == moves,
                "Cold recovery must keep the exact selected card undoable without discarding it.");
            var ids = game.CreateSnapshot(0).PendingDecision!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "pick")
                .Select(c => c.Cards.Single()).Take(2).ToArray();
            var commands = game.AcceptedCommands.Count;
            foreach (var id in ids) model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == id));
            typeof(MainViewModel).GetMethod("RefreshCurrentView", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(model, null);
            CheckChoices();
            Program.Assert(model.Hand.Count(card => card.IsSelected) == 2 && model.CanConfirmSelected &&
                game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves,
                "Both cards survive refresh in the unpaid local draft; selecting the second never drops or pays the first.");
            Program.Render(root, 1120, 740, Path.Combine(output, "fuchao-two-card-confirm.png"));
            model.ConfirmSelectedCommand.Execute(null);
            var discarded = game.CardMovements.Skip(moves).Where(m => m.Reason.Value == "skill-program.kong-shu.fuchao.discard").ToArray();
            Program.Assert(discarded.Length == 2 && discarded.Select(m => m.CardId).ToHashSet().SetEquals(ids) &&
                discarded.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
                "The visible Finish must discard the exact two selected entities together once.");
            GameReplay.Restore(game.CreateCheckpoint(), registry);

            string[] Branches() => model.SkillChoices.Select(c => c.Parameters.GetValueOrDefault("branch") ?? "").ToArray();
            void CheckChoices()
            {
                Program.Assert(model.IsOwnedCardSelectionPending && !model.IsHandResponsePending && !model.IsCardPaymentSelectionPending,
                    "Native incremental discards must use the shared hand multi-selection rather than singleton payment.");
                root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
                Program.Assert(Program.Find<Button>(root).Any(b => b.Visibility == Visibility.Visible && b.Command == model.ConfirmSelectedCommand),
                    "The actual table must expose the common explicit confirmation control.");
            }
        }
        finally { window.Content = null; window.Close(); }
        void Accept(GameCommand c) { var result = game.Submit(c); Program.Assert(result.Accepted, result.Error?.Message ?? "Fixture command rejected."); }
        void Save() => store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-fuchao-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:kong-shu");
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:fuchao-chain","revision":1,"triggers":[
                  {"id":"chain","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
                {"id":"fixture:fuchao-quiet","revision":1,"triggers":[
                  {"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> {
                        ["fixture:fuchao-quiet"] = new { name = "固定回合", description = "真实回合末弃牌夹具" },
                        ["fixture:fuchao-chain"] = new { name = "固定横置", description = "真实回合末弃牌夹具" }
                    } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实回合末弃牌夹具") { Program = program });
            b.AddGeneral(new("fixture:fuchao-human", "弃牌者", "supporter", "fixture:fuchao-quiet", "qun", 12, ["fixture:fuchao-chain"]));
            b.AddGeneral(new("fixture:fuchao-owner", "覆巢来源", "supporter", "fixture:fuchao-quiet", "qun", 12, ["fixture:fuchao-chain", "ol:fuchao"]));
            foreach (var id in new[] { "fixture:fuchao-peer-2", "fixture:fuchao-peer-3" })
                b.AddGeneral(new(id, "静态旁观者", "supporter", "fixture:fuchao-quiet", "qun", 12));
            b.AddDeck(new("fixture:fuchao-deck", "固定牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 8)).ToArray() });
            b.AddMode(new("fixture:fuchao-ui", "覆巢交互", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:fuchao-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:fuchao-human", "fixture:fuchao-owner", "fixture:fuchao-peer-2", "fixture:fuchao-peer-3"]));
        }
    }
}
