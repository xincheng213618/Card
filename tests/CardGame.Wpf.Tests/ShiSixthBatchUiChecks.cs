using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Wpf.Presentation;

internal static class ShiSixthBatchUiChecks
{
    private static readonly string[] Heroes = ["mobile:shi-sun-chen", "mobile:shi-sun-jun", "mobile:shi-cao-zhen"];
    private const string Nigu = "mobile:shi-nigu", Reason = "skill.mobile-shi-sun-chen.nigu.discard", Peer = "fixture:shi-sixth-ui-peer", Driver = "fixture:shi-sixth-ui-driver", Quiet = "fixture:shi-sixth-ui-quiet", Deck = "fixture:shi-sixth-ui-deck";
    public static void GalleryPortraitSkillCountsAndProgress(string output, IReadOnlyList<string>? heroes = null)
    {
        foreach (var hero in heroes ?? Heroes)
        {
            var (registry, game) = CreateScene(hero); ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard });
            using var model = ShiFifthBatchUiChecks.Model(game, registry);
            Program.Assert(GeneralGalleryCatalog.Classify(hero).SeriesId == "shi" && GeneralRosterCatalog.Classify(hero) == "shi", "Each formal sixth-batch Shi general belongs to the actual gallery and selection series.");
            model.OpenGeneralGalleryCommand.Execute(null); model.GeneralGallerySearchText = registry.Generals[hero].Name;
            var entry = model.GeneralGalleryEntries.Single(e => e.GeneralId == hero); model.SelectGeneralGalleryEntryCommand.Execute(entry);
            var expected = hero == "mobile:shi-cao-zhen" ? 1 : hero == "mobile:shi-deng-ai" ? 3 : 2;
            Program.Assert(entry.IsPlayable && entry.Skills.Count == expected && entry.Portrait.Brush is ImageBrush { ImageSource: BitmapSource { PixelWidth: > 100 } }, "The official portrait decodes and the real general keeps its printed skill count: Cao Zhen has one and the other released Shi generals in this check have two.");
            model.CloseGeneralGalleryCommand.Execute(null); var ids = registry.Generals[hero].SkillIds.ToHashSet(); var rail = model.HumanSkillCards.Where(s => s.ContentId is not null && ids.Contains(s.ContentId)).ToArray();
            Program.Assert(rail.Length == expected && rail.Any(s => !string.IsNullOrWhiteSpace(s.ProgressText)), "The loaded native rail shows this actual general's printed skills and live progress.");
            using var host = new NativeResponseStatusChecks.NativeHost(model); host.Capture(output, hero.Replace(':', '-') + "-progress", 1120);
        }
    }
    public static void NiguTwoDifferentSuitsRemainUnpaidUntilConfirmation(string output)
    {
        var scene = CreateScene(Heroes[0]); var game = scene.Game; ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard });
        using var model = ShiFifthBatchUiChecks.Model(game, scene.Registry); game = Program.Engine(model);
        var chip = model.HumanSkillCards.Single(s => s.ContentId == Nigu); Program.Assert(chip.IsAvailable, "The real Nigu rail exposes its once-Play activation."); model.ActivateHumanSkillCommand.Execute(chip);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c => Branch(c) == "pay") == true); ShiFifthBatchUiChecks.Refresh(model);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiSunChen is not null).Id;
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && model.IsOwnedCardSelectionPending && game.CreateSnapshot(0).PendingDecision?.MinimumCardCount == 0 && game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiSunChen is not null).Id == frame, "Cold loading restores Nigu's unpaid original multi-card collector and legal zero-card lower bound.");
        var ids = model.Hand.Where(c => c.IsPlayable).Take(2).Select(c => c.Id).ToArray();
        Program.Assert(ids.Length == 2 && game.CreateSnapshot(0).Players.Single(p => p.Seat == 0).Hand.Where(c => ids.Contains(c.Id)).Select(c => c.Suit).Distinct().Count() == 2, "The genuine hand contains two selectable original entities with different suits.");
        var commands = game.AcceptedCommands.Count; var movements = game.CardMovements.Count;
        model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == ids[0]));
        Program.Assert(model.Hand.Count(c => c.IsSelected) == 1 && Unpaid(), "The first selected suit remains a local unpaid editable draft.");
        model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == ids[1])); ShiFifthBatchUiChecks.Refresh(model);
        Program.Assert(model.CanConfirmSelected && model.Hand.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet().SetEquals(ids) && Unpaid(), "Both different suits survive a native refresh together and remain unpaid until primary confirmation.");
        using (var host = new NativeResponseStatusChecks.NativeHost(model)) host.Capture(output, "mobile-shi-sun-chen-nigu-two-suit-draft", 1120);
        model.ConfirmSelectedCommand.Execute(null); ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard }); ShiFifthBatchUiChecks.Refresh(model);
        var paid = game.CardMovements.Skip(movements).Where(m => m.Reason.Value == Reason).ToArray();
        Program.Assert(paid.Length == 2 && paid.Select(m => m.CardId).ToHashSet().SetEquals(ids) && paid.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) &&
            game.Events.Select(e => e.Payload).OfType<ShiScCostPaidEvent>().Single() is { Count: 2 } cost && cost.FrameId == frame &&
            game.Events.Select(e => e.Payload).OfType<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == frame && e.Reason.Value == Reason && e.ActualCount == 2) == 1 &&
            !model.HumanSkillCards.Single(s => s.ContentId == Nigu).IsAvailable, "One actual UI confirmation pays exactly both original hand entities as one native logical discard and spends this Play activation.");
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && game.Events.Select(e => e.Payload).OfType<ShiScCostPaidEvent>().Count() == 1 && game.CardMovements.Count(m => m.Reason.Value == Reason) == 2, "A cold UI load preserves the completed payment without discarding either entity again.");
        bool Unpaid() => game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements && !game.Events.Select(e => e.Payload).OfType<ShiScCostPaidEvent>().Any();
    }
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private static (ContentRegistry Registry, GameEngine Game) CreateScene(string hero)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(hero));
        var setup = new ShanheBattleSetup(660, 1, "shi-sixth-ui", hero, registry.Generals[hero].BaseHp, 0, 0, 0, 0, [Driver], [new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Peach, Suit.Heart, 3)], [],
            Enumerable.Range(0, 3).Select(_ => new ShanheCombatantDefinition(Peer, Hp: 8) { StartingHand = [] }).ToArray(), [], new string('f', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        ShiFifthBatchUiChecks.Accept(game, new StartGameCommand()); return (registry, game);
    }
    private sealed class Fixture(string hero) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-sixth-ui", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var heroes = Heroes.Append(hero).Distinct(StringComparer.Ordinal).ToArray();
            CharacterFixtureContent.Register(b, heroes);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "固定原生手牌", description = "保留真实阶段与不同花色实体。" }, [Quiet] = new { name = "安静对手", description = "不干扰本批原生界面。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静对手", "", Quiet, "wei", 8)); b.AddDeck(new(Deck, "固定实际牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势将第六批界面场景", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [.. heroes, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
