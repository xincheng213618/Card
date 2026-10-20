using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouTaishiCiOlChecks
{
    private const string Dulie = "mou-ol:dulie", Douchan = "mou-ol:douchan",
        Driver = "fixture:mtco-driver", Gain = "fixture:mtco-gain", Gate = "fixture:mtco-gate", Quiet = "fixture:mtco-quiet",
        Owner = "fixture:mtco-owner", Ally = "fixture:mtco-ally", Enemy = "fixture:mtco-enemy", Far = "fixture:mtco-far", Deck = "fixture:mtco-deck";

    public static void OneActualDuelOrInitialPopulationCappedPermanentGrowth()
    {
        var s = Scene.Start();
        s.Reach(() => s.Option("obtained"));
        var first = s.E<SequentialDeckKindGrowthStartedEvent>().Single();
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SequentialDeckKindGrowth is not null);
        var paid = s.E<SequentialDeckKindGrowthStepPaidEvent>().Single();
        s.Need(first.RequestedCount == 1 && s.V().MaxHp == 12 && root.SequentialDeckKindGrowth is
            { RequestedCount: 1, ActualObtained: 1, Stage: SequentialDeckKindGrowthStage.GainChildren } &&
            paid.Step is { Index: 0, ActualObtained: 1 } && paid.Step.Movements is
            [{ From.Zone: CardZoneKind.DrawPile, To: { Zone: CardZoneKind.Hand, OwnerSeat: 0 }, CardKind: CardKind.Duel }] &&
            s.E<SequentialDeckKindGrowthGrantedEvent>().Length == 0,
            "OL preparation requests exactly one real Duel even at twelve maximum HP, and waits for that entity's actual gain child before any growth or completion.");
        s.Cold(); s.Continue(); s.Reach(() => s.Option("lost"));
        s.Need(s.V().Hp == 11 && !s.Has(Douchan) && s.E<SequentialDeckKindGrowthCompletedEvent>().Length == 0,
            "The actual obtained-card child withdraws the HP-dependent source after payment, while the original fixed-one preparation remains pending.");
        s.Continue(); s.Play();
        s.Need(s.E<SequentialDeckKindGrowthCompletedEvent>() is [{ Attempts: 1, ActualObtained: 1, Shortfall: 0, Cancelled: false }] &&
            s.Grants().Select(e => e.Amount).SequenceEqual([0]) && s.V().Hand.Any(c => c.Id == paid.Step.CardId && c.Kind == CardKind.Duel) &&
            s.G.CardMovements.Count(m => m.CardId == paid.Step.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1,
            "The paid native gain returns once after source loss; fixed one succeeds without inventing a maximum-HP shortfall.");
        s.Use("recover"); s.Play();
        s.Need(s.Has(Douchan) && s.G.GetAttackRange(0) == 1 && !s.CanSlash(2),
            "A genuinely regranted source starts with no permanent bonus, and the distance-two opponent is outside ordinary range one.");
        s.Slash(3); s.Play();
        s.Need(!s.CanSlash(3), "Before any shortfall, one real paid Slash consumes the ordinary one-use quota.");

        s.NextPlay();
        s.Need(s.Grants().Select(e => e.Amount).SequenceEqual([0, 1]) && s.G.GetAttackRange(0) == 2 && s.CanSlash(2),
            "With no Duel left in the real stock, the next preparation adds exactly one permanent range and opens an actually distant Slash target.");
        s.Slash(2); s.Play(); s.Slash(2); s.Play();
        s.Need(!s.CanSlash(2) && s.E<CardUseDeclaredEvent>().Count(e => e.CardKind == CardKind.Slash && e.SourceSeat == 0) == 3 &&
            s.E<CardUseDeclaredEvent>().Where(e => e.CardKind == CardKind.Slash && e.SourceSeat == 0).All(e =>
                s.E<CardUseFinishedEvent>().Count(f => f.ResolutionId == e.ResolutionId) == 1),
            "The same one-unit issued growth permits exactly two real Slashes in the new Play, rather than only changing a displayed number.");
        s.Use("lose-hp"); s.Play();
        s.Need(!s.Has(Douchan) && s.G.GetAttackRange(0) == 2,
            "Losing the source leaves its already issued permanent range contribution intact.");
        s.Use("recover"); s.Play();
        s.Use("remove-ally", [1]); s.Play();
        s.Need(!s.V(1).IsAlive && s.G.State.Winner == Winner.None && s.G.PlayerCount == 4 && s.Has(Douchan),
            "A real nonterminal death leaves three living characters and four original player slots, with the regranted skill still active.");
        for (var i = 0; i < 3; i++) s.NextPlay();
        s.Need(s.Grants().Select(e => e.Amount).SequenceEqual([0, 1, 1, 1, 1]) && s.G.GetAttackRange(0) == 5,
            "Permanent growth reaches the original four-player budget despite a death and source replacement; neither live population nor a new source instance resets that budget.");
        s.NextPlay();
        s.Need(s.Grants().Select(e => e.Amount).SequenceEqual([0, 1, 1, 1, 1, 0]) && s.G.GetAttackRange(0) == 5 &&
            s.E<SequentialDeckKindGrowthStartedEvent>().All(e => e.RequestedCount == 1) &&
            s.E<SequentialDeckKindGrowthCompletedEvent>().TakeLast(5).All(e => e is { Attempts: 0, ActualObtained: 0, Shortfall: 1, Cancelled: false }) &&
            s.E<SequentialDeckKindGrowthStepPaidEvent>().Length == 1,
            "At the growth cap, the actual missing Duel and shortfall remain one while only the granted increment is zero; no phantom obtain, bonus, or paid material is fabricated.");
        s.Cold();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-taishi-ci-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.mou-taishi-ci-ol." + suffix + ".json")
                    ?? throw new InvalidOperationException("Missing actual OL Taishi Ci bundle.");
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Dulie, Douchan })
                b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = production.Programs[id], ProgramPresentation = production.Presentations[id] });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]},
                    {"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                    {"id":"remove-ally","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":20}]}]},
                  {"id":"{{Gain}}","revision":1,"triggers":[{"id":"first-real-obtain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"obtained"}]},{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"source-loss","options":[{"id":"lost"}]}]}]},
                  {"id":"{{Gate}}","revision":1,"hpSkillGrants":[{"id":"actual-source","skillId":"{{Douchan}}","minimumHp":12}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "原生体力与伤害", description = "固定小局" },
                [Gain] = new { name = "真实取得子窗", description = "取得后失源", optionLabels = new { obtained = "继续", lost = "继续" } },
                [Gate] = new { name = "真实体力授源", description = "固定小局" },
                [Quiet] = new { name = "静默", description = "跳过无关出牌" }
            };
            var fixture = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in fixture.Programs)
                b.AddSkill(new(id, id, "原生固定小局") { Program = program, ProgramPresentation = fixture.Presentations[id] });
            b.AddGeneral(new(Owner, "斗缠实际拥有者", "", Driver, "wu", 12, [Dulie, Gain, Gate]));
            b.AddGeneral(new(Ally, "真实同队角色", "", Quiet, "wu", 3));
            foreach (var id in new[] { Enemy, Far }) b.AddGeneral(new(id, id, "", Quiet, "wei", 20));
            b.AddDeck(new(Deck, "仅一张决斗的固定牌堆", 0, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7))
                    .Append(new("standard:duel", Suit.Heart, 1)).ToArray()
            });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "OL斗缠原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public SequentialDeckKindGrowthGrantedEvent[] Grants() => E<SequentialDeckKindGrowthGrantedEvent>().Where(e => e.Source.SkillId == Douchan).ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public bool Option(string id) => P?.SkillPrompt?.SkillId == Gain && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public bool CanSlash(int seat) => G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([seat]));
        public void Slash(int target)
        {
            var action = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void NextPlay()
        {
            var turn = G.State.TurnNumber;
            Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
            Reach(() => G.State.TurnNumber > turn && P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        }
        public void Reach(Func<bool> reached)
        {
            for (var step = 0; step < 180; step++)
            {
                if (reached()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff.");
                if (P is null || P.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected OL preparation prompt: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Missing bounded OL preparation boundary: " + JsonSerializer.Serialize(P));
        }
        public void Answer(Func<PromptChoice, bool> choose)
        {
            var prompt = P!; var choice = prompt.Choices.First(choose);
            Accept(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, G.Revision));
        }
        public void Cold()
        {
            var view = SnapshotJson.Serialize(G.CreateSnapshot(0)); var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(view == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements),
                "The existing owning physical-gain continuation preserves its fixed-one request and paid movement through cold restoration.");
        }
        public void Accept(GameCommand command)
        {
            var result = G.Submit(command);
            Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Message);
        }
        public void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new
            {
                G.State.TurnNumber, G.State.Phase, G.State.Winner, P,
                Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).IsAlive, V(i).HandCount }),
                Grants = Grants(), Completed = E<SequentialDeckKindGrowthCompletedEvent>(), Range = G.GetAttackRange(0)
            }));
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var hand = Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Spade, 7)).ToArray();
            var setup = new ShanheBattleSetup(83, 1, "mtco-main", Owner, 12, 0, 0, 0, 0, [], hand, [],
                [new(Enemy) { StartingHand = [] }, new(Far) { StartingHand = [] }], [new(Ally) { StartingHand = [] }], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 26, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
