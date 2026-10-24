using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ShiThirdBatchUiChecks
{
    private static readonly string[] Heroes = ["mobile:shi-dong-zhao", "mobile:shi-chen-dao", "mobile:shi-huang-zu"];
    private const string Peer = "fixture:shi-third-ui-peer";
    public static void GalleryProgressOriginalCardsAndMultiCardCost(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var hero in Heroes)
        {
            var setup = new ShanheBattleSetup(99, 1, "shi-third-ui", hero, registry.Generals[hero].BaseHp, 0, 0, 0, 0, [],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Alcohol, Suit.Diamond, 9)], [],
                Enumerable.Range(0, 3).Select(_ => new ShanheCombatantDefinition(Peer) { StartingHand = [new(CardKind.Peach, Suit.Heart, 6)] }).ToArray(), [], new string('b', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Accept(new StartGameCommand()); Reach(p => p?.PlayerSeat == 0);
            var store = new MemorySaveStore(); store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, 99, showSetup: false, saveStore: store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
            model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            Program.Assert(GeneralGalleryCatalog.Classify(hero) is { SeriesId: "shi" } && GeneralRosterCatalog.Classify(hero) == "shi",
                "The real selection roster and gallery must both classify this complete formal mobile edition under Shi.");
            model.OpenGeneralGalleryCommand.Execute(null);
            model.GeneralGallerySearchText = registry.Generals[hero].Name;
            var entry = model.GeneralGalleryEntries.Single(e => e.GeneralId == hero);
            model.SelectGeneralGalleryEntryCommand.Execute(entry);
            Program.Assert(entry.IsPlayable && entry.Portrait.Brush is ImageBrush { ImageSource: BitmapSource { PixelWidth: > 100 } } && entry.Skills.Count == 2,
                "Searching and opening each added Shi edition must decode its own attributed portrait and show both full native skill descriptions.");
            model.CloseGeneralGalleryCommand.Execute(null);
            if (hero == Heroes[0])
            {
                Reach(p => p?.Kind == DecisionKind.PlayCard); Refresh();
                var derived = model.Hand.Where(c => c.Kind == CardKind.MantianGuohai).ToArray();
                Program.Assert(derived.Length == 2 && derived.All(c => c.HasArtwork && c.HasDynamicWeaponName) &&
                    CardArt.Get(CardKind.MantianGuohai) is BitmapSource { PixelWidth: 186, PixelHeight: 260, IsFrozen: true } &&
                    Chip("mobile:shi-yingjia").ProgressText == "0/2" && Chip("mobile:shi-miaolue").StateText.Contains("不计手牌上限"),
                    "The opening really produces two visible Mantian Guohai entities with source-backed art, runtime names and the unused Yingjia progress.");
            }
            else if (hero == Heroes[1])
            {
                Program.Assert(Chip("mobile:shi-hongyi").ProgressText == "毅 2/4", "Hongyi's actual opening marks must be visible while its real preparation order is pending.");
                Choose(c => c.Parameters.GetValueOrDefault("branch") == "clear");
                Reach(p => p?.SkillPrompt?.SkillId == "mobile:shi-wanglie"); Refresh();
                Choose(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Choose(c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Cards.Count == 1);
                Reach(p => p?.Kind == DecisionKind.PlayCard); Refresh();
                Program.Assert(Chip("mobile:shi-wanglie").ProgressText == "已选手牌" &&
                    Chip("mobile:shi-hongyi").ProgressText == "毅 0/4" && Chip("mobile:shi-hongyi").StateText.Contains("本回合 X=2 已固定"),
                    "The selected original hand entity and the pending frozen preparation X must remain distinguishable in the skill state rail.");
                model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
                Program.Assert(!model.HasSaveError && Chip("mobile:shi-wanglie").ProgressText == "已选手牌" &&
                    Chip("mobile:shi-hongyi").StateText.Contains("本回合 X=2 已固定"), "A cold UI refresh retains the actual selected material and frozen ending reward.");
            }
            else
            {
                Reach(p => p?.Kind == DecisionKind.PlayCard); Refresh();
                Program.Assert(Chip("mobile:shi-duanyang").ProgressText == "0/1 · 储杀0" && Chip("mobile:shi-chizhang").StateText.Contains("多选"),
                    "Duanyang's live turn allowance, pending stored entity count and Chizhang's multiple-card operation must be visible.");
                var slash = game.GetHumanLegalActions().First(a => a.CardId is > 0 && a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
                Accept(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind));
                Reach(p => p?.SkillPrompt?.SkillId == "mobile:shi-chizhang"); Refresh();
                Choose(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                var materials = model.Hand.Where(c => c.Kind is CardKind.Peach or CardKind.Dodge).Take(2).Select(c => c.Id).ToArray();
                Program.Assert(materials.Length == 2, "The real original hand cost contains two selectable entities.");
                var before = game.AcceptedCommands.Count;
                foreach (var id in materials) model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
                Refresh();
                Program.Assert(model.Hand.Count(c => c.IsSelected) == 2 && model.CanConfirmSelected && game.AcceptedCommands.Count == before,
                    "Chizhang must retain a genuine two-card draft across refresh and pay neither card before confirmation.");
                model.ConfirmSelectedCommand.Execute(null);
                Program.Assert(materials.All(id => game.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile)),
                    "Confirming the shared multi-card draft pays both original Chizhang materials together.");
            }
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, hero.Replace(':', '-') + "-progress", 1120);
            HumanSkillViewModel Chip(string id) => model.HumanSkillCards.Single(s => s.ContentId == id);
            void Choose(Func<PromptChoice,bool> predicate) => model.SelectSkillChoiceCommand.Execute(model.SkillChoices.First(predicate));
            void Refresh() => typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
            void Accept(GameCommand c) { var result = game.Submit(c); Program.Assert(result.Accepted, result.Error?.Message ?? "Shi third batch UI command rejected."); }
            void Reach(Func<PendingDecision?,bool> ready)
            {
                for (var n = 0; n < 80; n++)
                {
                    if (ready(game.CreateSnapshot(0).PendingDecision)) return;
                    Program.Assert(game.PendingDecision?.PlayerSeat != 0, "Unexpected human boundary in Shi third batch UI: " + game.PendingDecision?.Prompt);
                    Accept(new AdvanceOneStepCommand(game.Revision));
                }
                throw new InvalidOperationException("Shi third batch UI missed its bounded native boundary.");
            }
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-third-ui", new(1,0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in Heroes) CharacterFixtureContent.Register(b, id);
            b.AddGeneral(new(Peer, "对手", "supporter", "standard:none", "wei", 8));
            b.AddDeck(new("fixture:shi-third-ui-deck", "固定牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 8)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势将第三批界面场景", 4,4,new Dictionary<string,int>(),"fixture:shi-third-ui-deck", GeneralPoolIds: [..Heroes,Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string,int> { [ShanheBattleSetup.PlayerTeamId]=1, [ShanheBattleSetup.EnemyTeamId]=3 }));
        }
    }
}
