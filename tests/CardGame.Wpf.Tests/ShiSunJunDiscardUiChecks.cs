using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiSunJunDiscardUiChecks
{
    private const string Hero = "mobile:shi-sun-jun", Skill = "mobile:shi-xiongtu", Reason = "skill.mobile-shi-sun-jun.discard",
        Driver = "fixture:shi-sun-jun-ui-driver", Quiet = "fixture:shi-sun-jun-ui-quiet", Peer = "fixture:shi-sun-jun-ui-peer", Deck = "fixture:shi-sun-jun-ui-deck";
    public static void XiongtuFourHandCardsRemainUnpaidUntilSingleConfirmation(string output)
    {
        var scene = CreateScene(); var game = scene.Game;
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        using var model = ShiFifthBatchUiChecks.Model(game, scene.Registry); game = Program.Engine(model);
        Program.Assert(model.HumanPlayer is { GeneralId: Hero, GeneralName: "势孙峻", Kingdom: "吴", Hp: 3, MaxHp: 3, HealthValue: "3/3" },
            "The restored real human seat is Sun Jun of Wu with 3/3 HP, rather than another model's initial seat.");
        var chip = model.HumanSkillCards.Single(s => s.ContentId == Skill);
        Program.Assert(chip.IsAvailable && chip.ProgressText == "本阶段 0/1" && model.HumanSkillCards.Single(s => s.ContentId == "mobile:shi-xianshuai").StateText.Contains("♠未用", StringComparison.Ordinal),
            "The actual native skill rail shows the initial one-use Xiongtu quota and visible unused Xianshuai suits.");
        model.ActivateHumanSkillCommand.Execute(chip);
        model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == 1));
        Program.Assert(model.CanConfirmSelected, "The actual Xiongtu activation accepts its published other Hand target.");
        model.ConfirmSelectedCommand.Execute(null);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c => Branch(c) == "hand") == true); ShiFifthBatchUiChecks.Refresh(model);
        model.DockSkillChoiceCommand.Execute(model.DockSkillChoices.First(c => Branch(c) == "hand"));
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision?.Choices.Any(c => Branch(c) == "damage") == true); ShiFifthBatchUiChecks.Refresh(model);
        model.DockSkillChoiceCommand.Execute(model.DockSkillChoices.Single(c => Branch(c) == "damage"));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiSunJun?.Stage == ShiSunJunStage.Payment).Id;
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && model.IsOwnedCardSelectionPending && !model.IsCardPaymentSelectionPending &&
            game.CreateSnapshot(0).PendingDecision is { RequiredCardCount: 4, MinimumCardCount: 4 } &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiSunJun is not null).Id == frame,
            "Cold loading restores the original unpaid four-card native owned collector and exact frozen cost.");
        var ids = model.Hand.Where(c => c.IsPlayable).Take(4).Select(c => c.Id).ToArray();
        Program.Assert(ids.Length == 4, "The original collector exposes four actual selectable Hand entities.");
        var commands = game.AcceptedCommands.Count; var movements = game.CardMovements.Count;
        Select(ids[0]); Select(ids[1]); ShiFifthBatchUiChecks.Refresh(model);
        Program.Assert(model.Hand.Count(c => c.IsSelected) == 2 && !model.CanConfirmSelected && Unpaid(),
            "Two clicks remain a local unpaid editable draft through refresh, accept no game command and cannot satisfy X4.");
        Select(ids[2]); Select(ids[3]); ShiFifthBatchUiChecks.Refresh(model);
        Program.Assert(model.CanConfirmSelected && model.Hand.Where(c => c.IsSelected).Select(c => c.Id).ToHashSet().SetEquals(ids) && Unpaid(),
            "All four actual entities survive refresh together and remain unpaid until one primary confirmation.");
        using (var host = new NativeResponseStatusChecks.NativeHost(model))
        {
            Program.Assert(host.Window.FindName("HumanPortrait") is System.Windows.Controls.ContentControl portrait && ReferenceEquals(portrait.Content, model.HumanPlayer) &&
                Program.Find<System.Windows.Controls.TextBlock>(host.Root).Any(t => t.IsVisible && t.Text == "势孙峻") &&
                Program.Find<System.Windows.FrameworkElement>(host.Root).Any(e => e.Name == "SeatHealthRail" && e.IsVisible &&
                    ReferenceEquals(e.DataContext, model.HumanPlayer) && System.Windows.Automation.AutomationProperties.GetName(e) == "3/3"),
                "The loaded visible human portrait binds the same Sun Jun seat, name and 3/3 health rail as the actual native engine.");
            host.Capture(output, "mobile-shi-sun-jun-xiongtu-four-card-draft", 1120);
        }
        model.ConfirmSelectedCommand.Execute(null);
        ShiFifthBatchUiChecks.Reach(game, () => game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); ShiFifthBatchUiChecks.Refresh(model);
        var paid = game.CardMovements.Skip(movements).Where(m => m.Reason.Value == Reason).ToArray();
        Program.Assert(paid.Length == 4 && paid.Select(m => m.CardId).ToHashSet().SetEquals(ids) && paid.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) &&
            game.Events.Select(e => e.Payload).OfType<ShiSunJunCostPaidEvent>().Single() is { Count: 4 } cost && cost.FrameId == frame &&
            game.Events.Select(e => e.Payload).OfType<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == frame && e.Reason.Value == Reason && e.ActualCount == 4) == 1 &&
            game.CreateSnapshot(0).Players.Single(p => p.Seat == 1).Hp == 7 && !model.HumanSkillCards.Single(s => s.ContentId == Skill).IsAvailable,
            "One actual UI confirmation pays precisely four selected originals in one logical discard, issues one real damage and consumes the initial phase quota.");
        model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null); game = Program.Engine(model);
        Program.Assert(!model.HasSaveError && game.Events.Select(e => e.Payload).OfType<ShiSunJunCostPaidEvent>().Count() == 1 && game.CardMovements.Count(m => m.Reason.Value == Reason) == 4,
            "Cold loading the completed native payment cannot discard any selected card again.");
        void Select(int id) => model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == id));
        bool Unpaid() => game.AcceptedCommands.Count == commands && game.CardMovements.Count == movements && !game.Events.Select(e => e.Payload).OfType<ShiSunJunCostPaidEvent>().Any();
    }
    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch");
    private static (ContentRegistry Registry, GameEngine Game) CreateScene()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(661, 1, "shi-sun-jun-ui", Hero, 3, 0, 0, 0, 0, [Driver],
            [new(CardKind.Slash, Suit.Spade, 1), new(CardKind.Slash, Suit.Heart, 2), new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Diamond, 4)], [],
            Enumerable.Range(0, 3).Select(_ => new ShanheCombatantDefinition(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Heart, 7)] }).ToArray(), [], new string('f', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        ShiFifthBatchUiChecks.Accept(game, new StartGameCommand()); return (registry, game);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-sun-jun-ui", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Driver] = new { name = "固定真实手牌", description = "保留四花色实际手牌，不改变正常次数。" }, [Quiet] = new { name = "安静对手", description = "不干扰实际界面。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静对手", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势孙峻本地多选界面场景", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
