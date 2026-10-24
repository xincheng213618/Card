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

internal static class ShiFifthBatchUiChecks
{
    private static readonly string[] Heroes = ["mobile:shi-lu-yu-sheng", "mobile:shi-lu-su", "mobile:shi-he-qi"];
    private const string Peer = "fixture:shi-fifth-ui-peer", Actor = "fixture:shi-fifth-ui-actor",
        Driver = "fixture:shi-fifth-ui-driver", Quiet = "fixture:shi-fifth-ui-quiet", Deck = "fixture:shi-fifth-ui-deck";

    public static void GalleryProgressAndForeignHandUseResponse(string output)
    {
        foreach (var hero in Heroes)
        {
            var (registry, game) = CreateScene(hero);
            Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard });
            using var model = Model(game, registry);
            Program.Assert(GeneralGalleryCatalog.Classify(hero).SeriesId == "shi" && GeneralRosterCatalog.Classify(hero) == "shi",
                "The three formal Shi editions belong to their actual gallery and selection series.");
            model.OpenGeneralGalleryCommand.Execute(null);
            model.GeneralGallerySearchText = registry.Generals[hero].Name;
            var entry = model.GeneralGalleryEntries.Single(e => e.GeneralId == hero);
            model.SelectGeneralGalleryEntryCommand.Execute(entry);
            Program.Assert(entry.IsPlayable && entry.Skills.Count == 2 &&
                entry.Portrait.Brush is ImageBrush { ImageSource: BitmapSource { PixelWidth: > 100 } },
                "Every real gallery entry decodes its official portrait and both skills.");
            model.CloseGeneralGalleryCommand.Execute(null);
            var skillIds = registry.Generals[hero].SkillIds.ToHashSet();
            var progress = model.HumanSkillCards.Where(c => c.ContentId is not null && skillIds.Contains(c.ContentId)).ToArray();
            Program.Assert(progress.Length == 2 && progress.Any(c => !string.IsNullOrWhiteSpace(c.ProgressText)),
                "The actual loaded rail exposes the new general's current progress.");
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, hero.Replace(':', '-') + "-progress", 1120);
        }

        CheckBorrowed(false);
        CheckBorrowed(true);
        void CheckBorrowed(bool response)
        {
            var (registry, game) = CreateScene(Actor, shared: true, response: response);
            Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard });
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            Accept(game, new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
            Reach(game, () => game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0 } p &&
                p.Kind == (response ? DecisionKind.RespondDodge : DecisionKind.PlayCard));
            var provider = game.CreateSnapshot(0).Players.Single(p => p.Seat == 1);
            var id = provider.Hand.Single().Id;
            using var model = Model(game, registry);
            game = Program.Engine(model);
            var card = model.Hand.Single(c => c.Id == id);
            Program.Assert(provider.ViewerCanUseHand && card.IsPlayable && card.OriginalHandTag.Contains("好施") &&
                game.CreateSnapshot(2).Players.Single(p => p.Seat == 1).Hand.Count == 0 &&
                game.CreateSnapshot(0).Players.Single(p => p.Seat == 0).Hand.All(c => c.Id != id),
                "The beneficiary can click the real foreign card with its source label, while other viewers cannot see it.");
            var accepted = game.AcceptedCommands.Count;
            model.SelectCardCommand.Execute(card);
            Program.Assert(game.AcceptedCommands.Count == accepted, "Selecting a shared material remains an editable UI draft.");
            if (response)
            {
                Program.Assert(model.IsHandResponsePending && model.ConfirmSelectedCommand.CanExecute(null),
                    "The shared Dodge is selectable through the ordinary hand-response surface.");
                model.ConfirmSelectedCommand.Execute(null);
            }
            else
            {
                Program.Assert(model.PlaySelectedCardCommand.CanExecute(null), "The shared Draw Two has a real legal play command.");
                model.PlaySelectedCardCommand.Execute(null);
            }
            Reach(game, () => game.Events.Select(e => e.Payload).OfType<ShiLsRefillIssuedEvent>().Any() &&
                NativeActions().Any(action => action.PhysicalCards.Any(c => c.CardId == id)) &&
                game.CreateSnapshot(0).Players.Single(p => p.Seat == 1).HandCount == 3);
            var action = NativeActions().Single(a => a.PhysicalCards.Any(c => c.CardId == id));
            Program.Assert(action.ActorSeat == 0 && action.ProviderSeat == 1 &&
                action.PhysicalCards.Single().From == CardLocation.Hand(1) &&
                action.Type == (response ? CardActionType.Response : CardActionType.Use),
                "The actual UI command preserves actor, provider, original hand and native use/response semantics.");
            Refresh(model);
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, response ? "shi-lu-su-shared-dodge" : "shi-lu-su-shared-use", 1120);
            IEnumerable<CardActionContext> NativeActions() => response
                ? game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action)
                : game.Events.Select(e => e.Payload).OfType<CardUseAppearanceCapturedEvent>().Select(e => e.Action);
        }
    }

    internal static (ContentRegistry Registry, GameEngine Game) CreateScene(string hero, bool shared = false,
        bool response = false, bool wounded = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var enemies = Enumerable.Range(1, 3).Select(i =>
            new ShanheCombatantDefinition(shared && i == 1 ? Heroes[1] : response && i == 3 ? Actor : Peer,
                Hp: shared && i == 1 ? 3 : 8, ExtraSkillIds: shared && i == 1 ? [Quiet] : [])
            { StartingHand = shared && i == 1 ? [new(response ? CardKind.Dodge : CardKind.DrawTwo, Suit.Heart, 7)]
                : response && i == 3 ? [new(CardKind.Slash, Suit.Spade, 7)]
                : wounded && i == 1 ? [new(CardKind.Dodge), new(CardKind.Dodge), new(CardKind.Peach), new(CardKind.Alcohol)]
                : [] }).ToArray();
        var setup = new ShanheBattleSetup(638, 1, "shi-fifth-ui", hero,
            wounded ? 1 : registry.Generals[hero].BaseHp, 0, 0, 0, 0, [Driver],
            shared ? [] : [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Peach, Suit.Heart, 3)], [], enemies, [], new string('e', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        return (registry, game);
    }

    internal static MainViewModel Model(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 638, showSetup: false, saveStore: store, contentRegistry: registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }
    internal static void Refresh(MainViewModel model) =>
        typeof(MainViewModel).GetMethod("RefreshCurrentView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, null);
    internal static void Reach(GameEngine game, Func<bool> done)
    {
        for (var n = 0; n < 500; n++)
        {
            if (done()) return;
            if (game.CreateSnapshot(0).PendingDecision is { } p)
            {
                var answer = p.Kind == DecisionKind.SelectFaction ? p.Choices.FirstOrDefault()
                    : p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                Program.Assert(answer is not null, "Unexpected fifth Shi batch UI boundary: " + p.Prompt);
                Accept(game, new AnswerPromptCommand(0, p.PromptId, answer!.Id, game.Revision));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fifth Shi batch UI missed its bounded native boundary.");
    }
    internal static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Rejected actual fifth Shi batch UI command.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-fifth-ui", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Heroes);
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],
                 "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[
                 {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                 {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "固定原生手牌", description = "保留真实阶段。" },
                        [Quiet] = new { name = "安静同桌", description = "保留真实结束阶段。" } } }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静对手", "", Quiet, "wei", 8));
            b.AddGeneral(new(Actor, "真实进攻者", "", Driver, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, [])
                { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势将第五批界面场景", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [.. Heroes, Peer, Actor], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
