using System.IO;
using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ShiSecondBatchUiChecks
{
    private static readonly string[] Heroes = ["mobile:shi-wang-chang", "mobile:shi-tian-feng", "mobile:shi-chen-zhi"];
    private const string Peer = "fixture:shi-second-ui-peer";

    public static void SkillProgressAndNativePreparationAndGiftDrafts(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var hero in Heroes)
        {
            var setup = new ShanheBattleSetup(97, 1, "shi-second-ui", hero, registry.Generals[hero].BaseHp, 0, 0, 0, 0, [],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 2),
                    new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Alcohol, Suit.Diamond, 9)], [],
                Enumerable.Range(0, 3).Select(_ => new ShanheCombatantDefinition(Peer) { StartingHand = [] }).ToArray(), [], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4,
                HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Accept(new StartGameCommand());
            Reach(p => p?.PlayerSeat == 0);
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, 97, showSetup: false, saveStore: store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
            model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            if (hero == Heroes[0])
            {
                Program.Assert(Chip("mobile:shi-kaiji").ProgressText == "至多1人" && Chip("mobile:shi-shepan").ProgressText == "0/1" &&
                    Chip("mobile:shi-shepan").StateText.Contains("本轮已用牌名：无"),
                    "Wang Chang must expose the live Kaiji bound and separate turn/name limits after cold loading.");
                Choose(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Program.Assert(model.IsSkillTargetSelectionPending && model.CanActFromHand,
                    "Kaiji's real native recipient choices must use selectable portraits and an explicit confirmation.");
                var target = model.Seats.First(s => s.Seat != 0 && s.IsLegalTarget);
                var before = game.AcceptedCommands.Count;
                model.SelectTargetCommand.Execute(target);
                Program.Assert(model.CanConfirmSelected && game.AcceptedCommands.Count == before,
                    "Previewing a Kaiji recipient must not draw a card before confirmation.");
                model.ConfirmSelectedCommand.Execute(null);
                Choose(c => c.Parameters.GetValueOrDefault("branch") == "done");
                Program.Assert(game.Events.Select(e => e.Payload).OfType<MobileShiKaijiDrawReturnedEvent>().Single().Target == target.Seat,
                    "The actual native finish must draw for the exact portrait recipient once.");
            }
            else if (hero == Heroes[1])
            {
                Program.Assert(Chip("mobile:shi-sijian").ProgressText == "0/2" &&
                    Chip("mobile:shi-sijian").StateText.Contains("下次背水失去 1 点体力"),
                    "Tian Feng must show the shared turn quota, backwater progression and outstanding debt count.");
                model.ActivateHumanSkillCommand.Execute(Chip("mobile:shi-ganggeng"));
                var ids = model.Hand.Take(2).Select(c => c.Id).ToArray();
                var before = game.AcceptedCommands.Count;
                foreach (var id in ids) model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
                var target = model.Seats.First(s => s.Seat != 0 && s.IsLegalTarget);
                model.SelectTargetCommand.Execute(target);
                Refresh();
                Program.Assert(model.Hand.Count(c => c.IsSelected) == 2 && model.CanConfirmSelected && game.AcceptedCommands.Count == before,
                    "Ganggeng must retain both material cards and its selected recipient without paying during refresh.");
                model.ConfirmSelectedCommand.Execute(null);
                Program.Assert(Chip("mobile:shi-ganggeng").ProgressText == "1/1" &&
                    Chip("mobile:shi-ganggeng").StateText.Contains("待结算"),
                    "The committed gift must display its real phase usage and the delayed recipient awaiting the ending.");
                model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
                Program.Assert(!model.HasSaveError && Chip("mobile:shi-ganggeng").StateText.Contains("待结算"),
                    "Cold loading must preserve Ganggeng's visible pending ending settlement.");
            }
            else
            {
                Program.Assert(Chip("mobile:shi-quanchong").ProgressText == "0/1" && Chip("mobile:shi-renxing").ProgressText == "0/2",
                    "Chen Zhi's forced extra turn and first-discard skill must show separate per-round counters.");
                Accept(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
                Reach(p => p?.SkillPrompt?.SkillId == "mobile:shi-renxing"); Refresh();
                Program.Assert(Chip("mobile:shi-quanchong").ProgressText == "1/1" &&
                    Chip("mobile:shi-renxing").StateText.Contains("机会已出现"),
                    "Quanchong's actual forced discard must update both its round quota and the real first-discard opportunity.");
            }
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, hero.Replace(':', '-') + "-progress", 1120);

            HumanSkillViewModel Chip(string id) => model.HumanSkillCards.Single(s => s.ContentId == id);
            void Choose(Func<PromptChoice, bool> predicate) => model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(predicate));
            void Refresh() => typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
            void Accept(GameCommand c) { var result = game.Submit(c); Program.Assert(result.Accepted, result.Error?.Message ?? "Shi UI fixture rejected a native command."); }
            void Reach(Func<PendingDecision?, bool> done)
            {
                for (var step = 0; step < 80; step++)
                {
                    if (done(game.CreateSnapshot(0).PendingDecision)) return;
                    Program.Assert(game.PendingDecision?.PlayerSeat != 0, "Unexpected human prompt before the requested Shi UI boundary: " + game.PendingDecision?.Prompt);
                    Accept(new AdvanceOneStepCommand(game.Revision));
                }
                throw new InvalidOperationException("The bounded Shi UI scene missed its next native boundary.");
            }
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-second-ui", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in Heroes) CharacterFixtureContent.Register(b, id);
            b.AddGeneral(new(Peer, "对手", "supporter", "standard:none", "wei", 4));
            b.AddDeck(new("fixture:shi-second-ui-deck", "固定牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 8)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势将界面场景", 4, 4, new Dictionary<string, int>(), "fixture:shi-second-ui-deck",
                GeneralPoolIds: [.. Heroes, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
