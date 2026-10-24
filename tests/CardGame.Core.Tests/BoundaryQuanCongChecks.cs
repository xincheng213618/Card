using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class BoundaryQuanCongChecks
{
    private const string Hero = "boundary:quan-cong-mobile", Skill = "boundary:yaoming-mobile",
        Driver = "fixture:boundary-quan-cong-driver", NoDraw = "fixture:boundary-quan-cong-no-draw",
        Watch = "fixture:boundary-quan-cong-watch", Peer = "fixture:boundary-quan-cong-peer";

    public static void ChargedOptionsEqualHandsDamageRechargeAndPaidDiscardCold()
    {
        var created = CreateScene(); var s = new Scene(created.Registry, created.Game); s.Play();
        s.Need(s.Remaining == 2 && s.PendingOption == 0, "Yaoming starts with two public charges and no recorded option.");
        s.Activate();
        s.Need(s.P!.Choices.Any(c => Branch(c) == "draw" && c.Targets.SequenceEqual([0])) &&
            s.P.Choices.Any(c => Branch(c) == "draw" && c.Targets.SequenceEqual([1])) &&
            s.P.Choices.Any(c => Branch(c) == "discard" && c.Targets.SequenceEqual([1])) &&
            s.P.Choices.All(c => Branch(c) != "discard" || !c.Targets.Contains(0)),
            "Equal hand counts qualify for both options; self can receive a draw but cannot be the discard victim.");
        var before = s.V(0).HandCount;
        s.Answer("draw", 0); s.Cold(); s.Play();
        s.Need(s.Remaining == 1 && s.PendingOption == 2 && s.V(0).HandCount == before + 1 &&
            s.E<BoundaryQuanCongYaomingDrawReturnedEvent>().Length == 1,
            "A genuine native self draw consumes one charge and records draw through a cold native return.");
        s.Activate(); s.Answer("draw", 0); s.Play();
        s.Need(s.Remaining == 0 && s.PendingOption == 2 && s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Skill),
            "Repeating the same option spends the second charge and removes the empty activation.");
        var hp = s.V(0).Hp; s.Driver("damage-three", [1]); s.Menu();
        s.Need(s.V(0).Hp == hp - 3 && s.Remaining == 3 &&
            s.E<BoundaryQuanCongYaomingStateEvent>().Where(e => e.Cause == "damage").ToArray() is [{ DamageAmount: 3, Remaining: 3 }],
            "One real three-point received damage adds three charges before its single optional intervention menu.");
        s.Answer("skip"); s.Play();
        s.Driver("equip-target", [1]); s.Reach(() => s.P?.Choices.Any(c => c.Cards.Count == 1 && c.Parameters.ContainsKey("source-zone")) == true);
        s.Answer(s.P!.Choices.First(c => c.Cards.Count == 1)); s.Play();
        s.Driver("draw-target", [1]); s.Play();
        s.Need(s.V(1).HandCount == s.V(0).HandCount && s.V(1).Equipment.Count == 1,
            "The fixture reaches a real equal-hand victim with a separately equipped native weapon.");
        s.Activate(); s.Answer("discard", 1); s.Cold();
        s.Need(s.Remaining == 3 && s.PendingOption == 0 &&
            s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
            "Changing from draw to discard refunds the spent charge, clears the old record and preserves anonymous foreign-hand choices.");
        var equipment = s.V(1).Equipment.Single().Id;
        s.Answer(s.P!.Choices.Single(c => c.Cards.SequenceEqual([equipment])));
        s.Reach(() => s.P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
        var paid = s.E<BoundaryQuanCongYaomingDiscardedEvent>().Single();
        s.Need(paid.CardId == equipment && s.V(1).Equipment.Count == 0 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == paid.FrameId).BoundaryQuanCongYaoming?.Stage == BoundaryQuanCongYaomingStage.Children &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == paid.FrameId &&
                e.InitiatorSeat == 0 && e.ExecutorSeat == 0 && e.SubjectSeat == 1 && e.ActualCount == 1 && e.HandCount == 0) == 1,
            "The chosen equipment is really discarded by Yaoming's owner once before the paid movement observer child.");
        s.Cold(); s.Answer(s.P!.Choices.Single()); s.Play();
        s.Need(s.Remaining == 3 && s.E<BoundaryQuanCongYaomingDiscardedEvent>().Length == 1 &&
            s.G.CardMovements.Count(m => m.CardId == equipment && m.To == CardLocation.DiscardPile) == 1,
            "Restoring the paid child neither spends another charge nor discards its material twice.");
        s.Activate(); s.Answer("draw", 0); s.Play();
        s.Need(s.Remaining == 2 && s.PendingOption == 2, "After the cleared record a new draw spends a charge without a second alternation refund.");
        s.Driver("damage-three", [1]); s.Menu(); s.Answer("skip"); s.Play();
        s.Need(s.Remaining == 4 && s.E<BoundaryQuanCongYaomingStateEvent>().Last(e => e.Cause == "damage") is { DamageAmount: 3, Remaining: 4 },
            "Further real damage recharges only up to four, and declining the optional use preserves that public cap.");
        s.Cold();
    }

    internal static (ContentRegistry Registry, GameEngine Game) CreateScene()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var starting = Enumerable.Range(0, 4).Select(i => new ShanheStartingCard(CardKind.Crossbow, Suit.Club, i + 1)).ToArray();
        var peers = Enumerable.Range(0, 4).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, i + 1)).ToArray();
        var setup = new ShanheBattleSetup(43, 1, "charged-yaoming", Hero, 8, 4, 0, 0, 0,
            [NoDraw, Driver], starting, [],
            [new(Peer, Hp: 10) { StartingHand = peers }, new(Peer, Hp: 10) { StartingHand = peers }],
            [new(Peer, Hp: 10) { StartingHand = peers }], new string('b', 64));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        return (registry, game);
    }

    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option");
    private static void Accept(CommandResult result)
    { if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message ?? "Rejected command."); }

    private sealed class Scene(ContentRegistry registry, GameEngine game)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Remaining => V(0).SkillRuntimeStates!.Single(s => s.SkillId == Skill).PublicRuleStates!.Single(s => s.StateId == "yaoming-remaining").Value;
        public int PendingOption => V(0).SkillRuntimeStates!.Single(s => s.SkillId == Skill).PublicRuleStates!.Single(s => s.StateId == "yaoming-pending-option").Value;
        public void Activate()
        { Accept(G.Submit(new UseProgramSkillCommand(0, Skill, "charged-intervention", [], [], G.Revision, P!.PromptId))); Menu(); }
        public void Driver(string activation, int[] targets) =>
            Accept(G.Submit(new UseProgramSkillCommand(0, BoundaryQuanCongChecks.Driver, activation, [], targets, G.Revision, P!.PromptId)));
        public void Menu() => Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "boundary-quan-cong-yaoming") == true);
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(string option, int target = -1) => Answer(P!.Choices.Single(c => Branch(c) == option && (target < 0 || c.Targets.SequenceEqual([target]))));
        public void Answer(PromptChoice choice) => Accept(G.Submit(new AnswerPromptCommand(P!.PlayerSeat, P.PromptId, choice.Id, G.Revision)));
        public void Reach(Func<bool> condition)
        {
            for (var i = 0; i < 180 && !condition(); i++)
            {
                if (P is { Kind: DecisionKind.ProgramTrigger } prompt)
                {
                    if (prompt.PlayerSeat != 0) { Accept(G.Submit(new AdvanceOneStepCommand(G.Revision))); continue; }
                    var skip = prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                    if (skip is not null) { Answer(skip); continue; }
                    throw new InvalidOperationException("Unexpected Yaoming fixture prompt: " + JsonSerializer.Serialize(prompt));
                }
                Accept(G.Submit(new AdvanceOneStepCommand(G.Revision)));
            }
            Need(condition(), "The bounded native charged-Yaoming fixture did not reach its expected window.");
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold command replay preserves the original charge state, option, selected victim and native child on its owning frame.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-quan-cong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":3}]},
                {"id":"draw-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]}
              ]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:yaoming-mobile.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"chooseOption","target":"selectedTarget","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [NoDraw] = new { name = "固定起手", description = "保留固定起手进行真实操作。" },
                [Driver] = new { name = "邀名原生输入", description = "真实伤害、摸牌和装备转移。" },
                [Watch] = new { name = "已付弃牌观察者", description = "原生装备弃置后的暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "邀名同桌", "supporter", NoDraw, "wei", 10, AdditionalSkillIds: [Watch]));
            const string deck = "fixture:boundary-quan-cong-deck";
            builder.AddDeck(new(deck, "邀名固定摸牌", 0, 0, [])
                { PhysicalCards = Enumerable.Range(0, 60).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "邀名原生操作", 4, 4, new Dictionary<string, int>(), deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
