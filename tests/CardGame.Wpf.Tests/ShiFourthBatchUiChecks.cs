using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class ShiFourthBatchUiChecks
{
    private static readonly string[] Heroes = ["mobile:shi-guo-yuan", "mobile:shi-xin-xian-ying", "mobile:shi-huan-jie"];
    private const string Peer = "fixture:shi-fourth-ui-peer", Driver = "fixture:shi-fourth-ui-driver", Jiejie = "mobile:shi-jiejie";

    public static void GalleryProgressAndSharedActiveEntry(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var hero in Heroes)
        {
            var game = Start(hero, shared: false); ReachPlay();
            using var model = Model(game); game = Program.Engine(model);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            Program.Assert(GeneralGalleryCatalog.Classify(hero) is { SeriesId: "shi" } && GeneralRosterCatalog.Classify(hero) == "shi",
                "All three complete formal mobile editions belong to the actual Shi selection and gallery series.");
            model.OpenGeneralGalleryCommand.Execute(null); model.GeneralGallerySearchText = registry.Generals[hero].Name;
            var entry = model.GeneralGalleryEntries.Single(e => e.GeneralId == hero); model.SelectGeneralGalleryEntryCommand.Execute(entry);
            Program.Assert(entry.IsPlayable && entry.Portrait.Brush is ImageBrush { ImageSource: BitmapSource { PixelWidth: > 100 } } &&
                entry.Skills.Count == (hero == Heroes[0] ? 3 : 2),
                "Each real gallery entry decodes its own official portrait and displays every implemented skill.");
            model.CloseGeneralGalleryCommand.Execute(null);
            if (hero == Heroes[0])
            {
                Program.Assert(Chip("mobile:shi-xiugeng").ProgressText == "记录 0/2" && Chip("mobile:shi-chenshe").StateText.Contains("花色相同"),
                    "Guo Yuan exposes his current record count and the complete multi-part rescue condition after loading the actual scene.");
            }
            else if (hero == Heroes[1])
            {
                var chip = Chip(Jiejie); var original = model.Hand.Select(c => c.Id).Order().ToArray(); var commands = game.AcceptedCommands.Count;
                Program.Assert(chip.IsAvailable && chip.ProgressText == "清识 0/2", "Own-phase Jiejie is a real available rail entry with its separate round progress.");
                model.ActivateHumanSkillCommand.Execute(chip);
                Program.Assert(game.AcceptedCommands.Count == commands + 1 && game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, IsPrivate: true } &&
                    model.PrivatelyViewedCards.Select(c => c.Id).Order().SequenceEqual(original) && model.SkillChoices.Any(c => c.Parameters.GetValueOrDefault("branch") == "Spade"),
                    "Clicking the published own skill chip issues the real activation and displays only the exact owner's private hand and suit picker.");
                model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("branch") == "Spade"));
                ReachPlay(); Refresh();
                Program.Assert(!Chip(Jiejie).IsAvailable && Chip(Jiejie).ProgressText == "清识 1/2" &&
                    Chip(Jiejie).StateText.Contains("黑桃") && !model.HasPrivatelyViewedCards,
                    "After the actual suit answer the picker disappears, the phase entry becomes spent, and the granted suit plus automatic Qingshi progress refresh in the rail.");
            }
            else
            {
                var chip = Chip("mobile:shi-zhengshuo");
                Program.Assert(chip.IsAvailable && chip.ProgressText == "限定技 · 可发动" && Chip("mobile:shi-gongmou").StateText.Contains("奇策"),
                    "Huan Jie exposes the available limited action and the complete preparation-derived skill lifetime.");
                model.ActivateHumanSkillCommand.Execute(chip); ReachPlay(); Refresh();
                Program.Assert(!Chip("mobile:shi-zhengshuo").IsAvailable && Chip("mobile:shi-zhengshuo").ProgressText == "限定技 · 已使用" &&
                    game.CreateSnapshot(0).Players.All(p => p.HandCount == 4),
                    "Clicking the real limited rail entry performs the complete hand reset and updates its once-per-game state.");
                model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
                Program.Assert(!model.HasSaveError && Chip("mobile:shi-zhengshuo").ProgressText == "限定技 · 已使用",
                    "The spent limited state remains visible after a manual save and cold UI load.");
            }
            using var host = new NativeResponseStatusChecks.NativeHost(model); host.Capture(output, hero.Replace(':', '-') + "-progress", 1120);
            HumanSkillViewModel Chip(string id) => model.HumanSkillCards.Single(c => c.ContentId == id);
            void Refresh() => typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
            void ReachPlay() => Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        }

        // A foreign source publishes the same visible active chip, with its own
        // owner identity. The actor does not receive the owner's private chooser.
        var sharedGame = Start(Peer, shared: true); Reach(sharedGame, () => sharedGame.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard });
        using var shared = Model(sharedGame); sharedGame = Program.Engine(shared);
        var foreign = shared.HumanSkillCards.Single(c => c.ContentId == Jiejie && c.SkillOwnerSeat == 1);
        Program.Assert(foreign.IsAvailable && foreign.SourceText.Contains("势辛宪英"), "The real foreign skill owner appears in the actor's shared active rail.");
        var expected = shared.Hand.Select(c => c.Id).Order().ToArray(); var before = sharedGame.AcceptedCommands.Count;
        shared.ActivateHumanSkillCommand.Execute(foreign);
        Program.Assert(sharedGame.AcceptedCommands.Count == before + 1 && sharedGame.AcceptedCommands.OfType<UseProgramSkillCommand>().Last() is
                { ActorSeat: 0, SkillOwnerSeat: 1, SkillId: Jiejie } &&
            sharedGame.CreateSnapshot(0).PendingDecision is null && !shared.HasPrivatelyViewedCards &&
            sharedGame.CreateSnapshot(1).PendingDecision is { PlayerSeat: 1, IsPrivate: true } ownerPrompt &&
            sharedGame.CreateSnapshot(1).PrivateRevealedCards!.Select(c => c.Id).Order().SequenceEqual(expected),
            "The foreign chip sends the native command with its exact source owner; only Xin Xian Ying sees the real hand and suit choices while the actor waits.");
        var prompt = sharedGame.CreateSnapshot(1).PendingDecision!;
        Accept(sharedGame, new AnswerPromptCommand(1, prompt.PromptId,
            prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "Spade").Id, sharedGame.Revision));
        Reach(sharedGame, () => sharedGame.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard });
        typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shared, null);
        Program.Assert(!shared.HumanSkillCards.Single(c => c.ContentId == Jiejie && c.SkillOwnerSeat == 1).IsAvailable &&
            sharedGame.Events.Select(e => e.Payload).OfType<ShiXinXianYingSuitChosenEvent>().Single() is { Actor: 0, Suit: Suit.Spade } &&
            !shared.HasPrivatelyViewedCards, "A completed foreign-owner answer refreshes the actor's phase-spent shared entry without retaining the private hand picker.");
        using var sharedHost = new NativeResponseStatusChecks.NativeHost(shared); sharedHost.Capture(output, "mobile-shi-xin-xian-ying-shared-entry", 1120);

        MainViewModel Model(GameEngine game)
        {
            var store = new MemorySaveStore(); store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            var model = new MainViewModel(false, 98, showSetup: false, saveStore: store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
            model.LoadManualGameCommand.Execute(null); return model;
        }
        GameEngine Start(string hero, bool shared)
        {
            var setup = new ShanheBattleSetup(98, 1, "shi-fourth-ui", hero, registry.Generals[hero].BaseHp, 0, 0, 0, 0, [Driver],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Alcohol, Suit.Diamond, 9)], [],
                Enumerable.Range(1, 3).Select(i => new ShanheCombatantDefinition(shared && i == 1 ? Heroes[1] : Peer,
                    Hp: shared && i == 1 ? registry.Generals[Heroes[1]].BaseHp : 8)
                    { StartingHand = [new(CardKind.Peach, Suit.Heart, 6)] }).ToArray(), [], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
            Accept(game, new StartGameCommand()); return game;
        }
    }
    private static void Reach(GameEngine game, Func<bool> done)
    {
        for (var n = 0; n < 100; n++)
        {
            if (done()) return;
            if (game.CreateSnapshot(0).PendingDecision is { } p)
            {
                var skip = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                Program.Assert(skip is not null, "Unexpected human Shi fourth batch UI boundary: " + p.Prompt);
                Accept(game, new AnswerPromptCommand(0, p.PromptId, skip!.Id, game.Revision)); continue;
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Shi fourth batch UI missed its bounded actual native boundary.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Rejected actual Shi fourth batch UI command."); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-fourth-ui", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, [.. Heroes, "classic:xun-you", "classic:wolong-zhuge-liang"]);
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "固定原生手牌", description = "保留准备阶段与实际摸牌流程。" } } }));
            b.AddSkill(new(Driver, "固定原生手牌", "保留准备阶段与实际摸牌流程。") { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            b.AddGeneral(new(Peer, "对手", "supporter", "standard:none", "wei", 8));
            b.AddDeck(new("fixture:shi-fourth-ui-deck", "固定实际牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 8)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势将第四批界面场景", 4, 4, new Dictionary<string, int>(), "fixture:shi-fourth-ui-deck", GeneralPoolIds: [.. Heroes, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
