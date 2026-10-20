using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouDaQiaoMobileChecks
{
    private const string Guose = "mou-mobile:guose", Liuli = "mou-mobile:liuli", Driver = "fixture:mdqm-driver",
        Quiet = "fixture:mdqm-quiet", Owner = "fixture:mdqm-owner", Ally = "fixture:mdqm-ally",
        Enemy = "fixture:mdqm-enemy", Far = "fixture:mdqm-far", Deck = "fixture:mdqm-deck", Identity = "identity:mdqm";

    public static void NativeGuoseModeBudgetRemovalAndHeartLiuliIssuedPlay()
    {
        var s = Scene.Start();
        GuosePair(s, 2);
        s.Need(s.E<MouDaQiaoGuoseReservedEvent>().Length == 2 && s.E<MouDaQiaoGuoseReservedEvent>().All(e => e.PhaseLimit == 2) &&
            !s.G.GetHumanLegalActions().Any(a => a.SkillId == Guose), "The two native Guose branches share the non-Identity actual-Play allowance of two.");

        var hearts = s.V().Hand.Where(c => c.Suit == Suit.Heart).Select(c => c.Id).ToArray();
        s.Need(hearts.Length == 2, "The fixed native fixture retains two real Heart costs.");
        for (var i = 0; i < 2; i++)
        {
            s.Use(Driver, "request-slash", [], [2]);
            s.Reach(() => s.P?.SkillPrompt?.SkillId == Liuli && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
            var redirect = i == 0 ? 1 : 3;
            s.Answer(c => c.Targets.SequenceEqual([redirect]));
            s.Reach(() => s.P?.Choices.Any(c => c.Cards.SequenceEqual([hearts[i]])) == true);
            s.Answer(c => c.Cards.SequenceEqual([hearts[i]]));
            if (i == 0)
            {
                s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-da-qiao-liuli") == true);
                s.Need(s.E<MouDaQiaoLiuliMarkGrantedEvent>().Length == 0 && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ActualCount == 1 && e.ExecutorSeat == 0 && e.Reason.Value == "skill-program.mou-mobile:liuli.SelectAndMoveOwnedCard"),
                    "The Heart decision follows one real native HE discard and precedes the actual mark grant.");
                s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1]));
            }
            s.Play();
            s.Need(s.V().Hp == 3 && s.V(redirect).Hp == 7 && s.G.CardMovements.Count(m => m.CardId == hearts[i] && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
                "Each accepted native Liuli redirects the real incoming Slash and pays its selected Heart entity once.");
        }
        var grant = s.E<MouDaQiaoLiuliMarkGrantedEvent>().Single();
        s.Need(s.E<MouDaQiaoLiuliCostFrozenEvent>().Length == 2 && grant.RecipientSeat == 1 &&
            Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[1].Markers.Single(m => m.Kind == PlayerMarkerKind.MouDaQiaoLiuli).Count == 1),
            "Ordinary Liuli remains usable; only its Heart reward is once this actual turn, and its unique mark is publicly visible.");
        s.Use(Driver, "lose-liuli", [], []); s.Play(); s.Cold();
        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.E<MouDaQiaoLiuliPhaseReturnedEvent>().Any(e => e.GrantId == grant.GrantId));
        var issued = s.E<MouDaQiaoLiuliPhaseIssuedEvent>().Single(e => e.GrantId == grant.GrantId);
        s.Need(issued.RecipientSeat == 1 && s.E<MouDaQiaoLiuliPhaseReturnedEvent>().Count(e => e.GrantId == grant.GrantId) == 1 &&
            s.E<ProgramPhaseScheduledEvent>().Count(e => e.FrameId == issued.FrameId && e.Phase == TurnPhase.Play && e.Started) == 1 &&
            s.E<ProgramPhaseScheduledEvent>().Count(e => e.FrameId == issued.FrameId && e.Phase == TurnPhase.Play && !e.Started) == 1 &&
            s.E<MouDaQiaoLiuliMarkConsumedEvent>().Count(e => e.GrantId == grant.GrantId) == 1 &&
            s.G.CreateSnapshot(0).Players.All(p => (p.Markers ?? []).All(m => m.Kind != PlayerMarkerKind.MouDaQiaoLiuli)),
            "The paid grant survives original source loss, runs one native recipient Play inside its next actual turn, returns once, and consumes only that issued mark.");
        s.Cold();
    }

    public static void IdentityGuoseSharesFourNativeUses()
    {
        var s = Scene.Start(identity: true); GuosePair(s, 1); GuosePair(s, 1);
        s.Need(s.E<MouDaQiaoGuoseReservedEvent>().Length == 4 && s.E<MouDaQiaoGuoseReservedEvent>().All(e => e.PhaseLimit == 4) &&
            s.E<MouDaQiaoGuoseDrawReturnedEvent>().Length == 4 && !s.G.GetHumanLegalActions().Any(a => a.SkillId == Guose),
            "The actual Identity mode shares four paid uses across both complete Guose branches, then closes both entries.");
    }

    private static void GuosePair(Scene s, int target)
    {
        var diamond = s.V().Hand.First(c => c.Suit == Suit.Diamond).Id;
        var handBefore = s.V().HandCount;
        s.Use(Guose, "diamond-use", [diamond], [target]); s.Play();
        s.Need(s.V(target).Judgment.Any(c => c.Id == diamond && c.Kind == CardKind.Indulgence) &&
            s.G.CardMovements.Count(m => m.CardId == diamond && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.V().HandCount == handBefore, "Guose truly uses and settles the Diamond original as Indulgence, then returns its native Draw1 once.");
        var beforeRemove = s.G.CardMovements.Count;
        s.Use(Guose, "remove-delayed", [], [target]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-da-qiao-guose") == true);
        s.Answer(c => c.Cards.SequenceEqual([diamond])); s.Play();
        var paid = s.E<MouDaQiaoGuosePaidEvent>().Last();
        s.Need(paid.Branch == "remove-delayed" && paid.CardId == diamond && paid.From == CardLocation.Judgment(target) && paid.To == CardLocation.DiscardPile &&
            s.G.CardMovements.Skip(beforeRemove).Count(m => m.CardId == diamond && m.From == CardLocation.Judgment(target) && m.To == CardLocation.DiscardPile) == 1 &&
            !s.G.CardMovements.Skip(beforeRemove).Any(m => m.From == CardLocation.Hand(0) && m.To.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile) &&
            s.V().HandCount == handBefore + 1 && s.E<MouDaQiaoGuoseCompletedEvent>().All(e => e.Paid),
            "Removing the existing field Indulgence costs no extra Diamond or Hand entity, uses a real logical discard, and genuinely draws one.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-da-qiao-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:da-qiao-mobile");
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"request-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},
                  {"id":"lose-liuli","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Liuli}}"],"sourceBind":"standard:none"}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实请求及失源", description = "公开原生操作" }, [Quiet] = new { name = "静默", description = "跳过无关常规出牌" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实原生小局") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "移动谋大乔本人", "", "standard:none", "wu", 3, [Guose, Liuli, Driver]));
            foreach (var id in new[] { Ally, Enemy, Far }) b.AddGeneral(new(id, "真实其他角色", "", "standard:none", "qun", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:indulgence", Suit.Diamond, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动谋大乔原生团队小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, Enemy, Far], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            b.AddMode(new(Identity, "移动谋大乔原生身份小局", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, Ally, Enemy, Far]));
        }
    }

    private sealed class Scene(GameEngine g, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = g;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string skill, string activation, int[] cards, int[] targets) => Accept(new UseProgramSkillCommand(0, skill, activation, cards, targets, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> condition)
        {
            for (var i = 0; i < 160; i++)
            {
                if (condition()) return;
                Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff.");
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                else throw new InvalidOperationException("Unexpected native Da Qiao boundary: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Missing bounded native Da Qiao boundary: " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var view = SnapshotJson.Serialize(G.CreateSnapshot(0)); var movement = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(view == SnapshotJson.Serialize(G.CreateSnapshot(0)) && movement == JsonSerializer.Serialize(G.CardMovements), "The issued paid choice or marked phase survives one representative cold restoration without repeated cost.");
        }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, G.State.Winner, P, Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.HandCount, p.Markers }) })); }
        public void Accept(GameCommand c) { var result = G.Submit(c); Need(result.Accepted, "Rejected native " + c.GetType().Name + ": " + result.Error?.Message); }
        public static Scene Start(bool identity = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            if (identity)
            {
                var gi = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Identity,
                    UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
                var si = new Scene(gi, r); si.Accept(new StartGameCommand()); si.Reach(() => si.P?.Kind == DecisionKind.SelectGeneral);
                si.Accept(new SelectGeneralCommand(0, Owner, gi.Revision, si.P!.PromptId)); si.Play(); return si;
            }
            static ShanheStartingCard C(CardKind k, Suit suit = Suit.Spade) => new(k, suit, 7);
            var setup = new ShanheBattleSetup(83, 1, "mdqm-main", Owner, 3, 0, 0, 0, 0, [],
                [C(CardKind.Slash, Suit.Diamond), C(CardKind.Indulgence, Suit.Diamond), C(CardKind.Peach, Suit.Heart), C(CardKind.Peach, Suit.Heart)], [],
                [new(Enemy) { StartingHand = [C(CardKind.Slash), C(CardKind.Slash)] }, new(Far) { StartingHand = [C(CardKind.Indulgence)] }],
                [new(Ally) { StartingHand = [C(CardKind.Indulgence)] }], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
