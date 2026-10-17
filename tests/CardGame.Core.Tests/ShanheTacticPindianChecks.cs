using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShanheTacticPindianChecks
{
    private const string Bonus = "fixture:shanhe-pindian-bonus", Extra = "fixture:shanhe-pindian-extra";
    private const string Driver = "fixture:shanhe-pindian-driver", Quiet = "fixture:shanhe-pindian-quiet";
    private const string Claim = "fixture:shanhe-pindian-claim", Owner = "fixture:shanhe-pindian-owner";
    private const string Mode = "identity:classic-shanhe-pindian";

    public static void EffectiveBonusInstancesCapsPhysicalClaimsAndColdReplay()
    {
        foreach (var scene in new[] { "single", "stacked", "suit" })
        {
            var s = Scenario.Start(scene);
            s.Stock();
            var source = s.Card(7);
            var opponent = s.Give(9, 1);
            s.Accept(new UseProgramSkillCommand(0, "classic:tianyi", "contest", [source], [1],
                s.G.Revision, s.P!.PromptId));
            s.Reach(() => s.P is { PlayerSeat: 1 });
            s.Need(s.G.CreateSnapshot(0).Players[1].Hand.Count == 0 &&
                s.P!.IsPrivate && s.G.CreateSnapshot(2).PendingDecision is null,
                "The additive policy cannot expose the opponent's genuinely private Pindian selection.");
            s.Cold();
            s.Accept(new AdvanceOneStepCommand(s.G.Revision));
            s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-claim") == true);
            var result = s.Events<PindianResultDeterminedEvent>().Single().Result;
            var expected = scene == "single" ? 10 : scene == "stacked" ? 12 : 13;
            s.Need(result.SourceCardId == source && result.OpponentCardId == opponent &&
                result.SourceRank == expected && result.OpponentRank == 9 && result.SourceWon,
                "One enabled instance contributes once despite duplicate nodes; independent skills stack, after existing suit-rank substitution and capped at K.");
            s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("take") == "true" &&
                c.Cards.Order().SequenceEqual(new[] { source, opponent }.Order())),
                "The native claim must reference the two original paid physical entities using the already frozen final result.");
            s.Cold();
            s.Answer(c => c.Parameters.GetValueOrDefault("take") == "true");
            s.Play();
            s.Need(s.V().Hand.Single(c => c.Id == source).Rank == 7 &&
                s.V().Hand.Single(c => c.Id == opponent).Rank == 9 &&
                s.Events<PindianResultDeterminedEvent>().Single().Result == result &&
                new[] { source, opponent }.All(id => s.G.CardMovements.Count(m => m.CardId == id &&
                    m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) &&
                    m.Reason.Value == "program.pindian.claim") == 1),
                "Claim and cold replay preserve printed ranks and never apply the rank bonus a second time or obtain a substituted entity.");
            s.Cold();
        }

        foreach (var value in new[] { 0, 13 }) InvalidPolicy(value);
        InvalidPolicy(3, "\"cardKinds\":[\"slash\"]");
        InvalidPolicy(3, "\"inputSuit\":\"spade\"");
        InvalidPolicy(3, "\"discardCost\":0");
    }

    public static void SimultaneousRanksAndOpponentAiUseTheSameBonusCold()
    {
        var s = Scenario.Start("group");
        s.Stock();
        var source = s.Card(11);
        var first = s.Give(10, 1);
        var second = s.Give(12, 1);
        var third = s.Give(12, 2);
        s.Need(first < second, "The fixed physical deck gives the lower printed card the lower stable id, without a seed search.");
        s.Accept(new UseProgramSkillCommand(0, "ol:gushe", "simultaneous-pindian", [source], [1, 2],
            s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 1 });
        s.Need(s.P!.ValidCardIds.Contains(first) && s.P.ValidCardIds.Contains(second) &&
            s.G.CreateSnapshot(0).Players[1].Hand.Count == 0,
            "The real opposing AI chooses between its two private physical cards.");
        s.Cold();
        s.Reach(() => s.P is { PlayerSeat: 0 } p &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "simultaneous-pindian" &&
                c.Parameters.GetValueOrDefault("branch") == "draw"));
        s.Need(s.V(1).Hand.Any(c => c.Id == second && c.Rank == 12),
            "At the first comparison's consequence boundary, the AI's unchosen printed 12 must still be in its original hand. " +
            JsonSerializer.Serialize(new { Unchosen = second, Hand = s.V(1).Hand, Moves = s.G.CardMovements.Where(m => m.CardId == second) }));
        var revealed = s.Events<PindianCardsRevealedEvent>();
        s.Need(revealed.Length == 1 && revealed[0].Simultaneous &&
            revealed[0].Cards.Select(c => c.CardId).Order().SequenceEqual(new[] { source, first, third }.Order()),
            "The common reveal must contain exactly the three true paid entities, excluding the unchosen hand card. " + JsonSerializer.Serialize(revealed));
        foreach (var id in new[] { source, first, third })
            s.Need(s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing &&
                m.Reason == CardMoveReasons.PindianReveal) == 1,
                "Each common material must pay exactly once before the first consequence. " +
                JsonSerializer.Serialize(new { CardId = id, Moves = s.G.CardMovements.Where(m => m.CardId == id) }));
        s.Cold();
        s.Play();
        var results = s.Events<PindianResultDeterminedEvent>().Select(e => e.Result).ToArray();
        s.Need(results.Length == 2 && results[0] is { SourceRank: 13, OpponentRank: 13, SourceWon: false } &&
            results[0].SourceCardId == source && results[0].OpponentCardId == first &&
            results[1] is { SourceRank: 13, OpponentRank: 12, SourceWon: true } && results[1].OpponentCardId == third,
            "Common-group comparisons share the capped source bonus; the opponent AI treats printed 10 and 12 as equally capped K and chooses the lower stable id.");
        var consequenceDiscards = s.Events<SimultaneousPindianConsequencePaidEvent>().Where(e =>
            e.ParticipantSeat == 1 && e.CardId == second && e.Branch == "discard").ToArray();
        s.Need(!s.G.CardMovements.Any(m => m.CardId == second && m.Reason == CardMoveReasons.PindianReveal) &&
            (s.V(1).Hand.Any(c => c.Id == second && c.Rank == 12) ||
                consequenceDiscards.Length == 1 && s.G.CardMovements.Count(m => m.CardId == second &&
                    m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "skill-program.simultaneous-pindian.discard") == 1),
            "The unchosen card cannot pay for Pindian; any subsequent loss must be the actual native nonwinner's discard consequence. " +
            JsonSerializer.Serialize(new { Unchosen = second, Hand = s.V(1).Hand, Consequences = consequenceDiscards,
                Moves = s.G.CardMovements.Where(m => m.CardId == second) }));
        s.Cold();
    }

    private static SkillProgram BonusProgram(string id, int value, bool duplicate = false, bool suit = false)
    {
        var policies = new List<object> { new { id = "rank", kind = "pindianRankBonus", value } };
        if (duplicate) policies.Add(new { id = "same-instance", kind = "pindianRankBonus", value });
        if (suit) policies.Add(new { id = "suit-base", kind = "pindianRankBySuit", inputSuit = "spade", value = 11 });
        return SkillProgramCatalog.Load(JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
            skills = new[] { new { id, revision = 1, cardPolicies = policies } }
        }), Presentation(id)).Programs[id];
    }

    private static string Presentation(string id) => JsonSerializer.Serialize(new
    {
        schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new Dictionary<string, object> { [id] = new { name = id, description = "真实拼点点数加成" } }
    });

    private static void InvalidPolicy(int value, string qualifier = "")
    {
        var suffix = qualifier.Length == 0 ? "" : "," + qualifier;
        try
        {
            _ = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Bonus}}","revision":1,"cardPolicies":[{"id":"invalid","kind":"pindianRankBonus","value":{{value}}{{suffix}}}]}]}
                """, Presentation(Bonus));
        }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid or qualified additive Pindian rank policy was accepted.");
    }

    private sealed class Scenario(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Card(int rank) => V().Hand.First(c => c.Rank == rank).Id;
        public void Stock() { Use("stock"); Play(); }
        public int Give(int rank, int target) { var id = Card(rank); Use("give", [id], [target]); Play(); return id; }
        private void Use(string action, int[]? cards = null, int[]? targets = null) => Accept(
            new UseProgramSkillCommand(0, Driver, action, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, G.Revision)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw"))
                    Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "claim"))
                    Answer(c => c.Parameters.GetValueOrDefault("branch") == "claim");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("take") == "true"))
                    Answer(c => c.Parameters.GetValueOrDefault("take") == "true");
                else throw new InvalidOperationException("Unexpected Pindian bonus boundary: " + JsonSerializer.Serialize(p));
            }
            throw new InvalidOperationException("Missing bounded Pindian bonus boundary: " + JsonSerializer.Serialize(P));
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Pindian bonus command rejected."); }
        public void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves effective ranks, private choices, frozen results, claims and unchanged physical faces.");
        }
        public static Scenario Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
                HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scenario(game, registry);
            s.Accept(new StartGameCommand());
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Owner, s.G.Revision, s.P!.PromptId));
            s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-pindian-bonus", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:taishi-ci", "ol:wang-lang");
            builder.AddSkill(new(Bonus, "赌术", "拼点点数增加三，至多K。") { Program = BonusProgram(Bonus, 3, duplicate: true, suit: scene == "suit") });
            builder.AddSkill(new(Extra, "独立加成", "另一个真实技能实例增加两点。") { Program = BonusProgram(Extra, 2) });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12},{"op":"draw","target":"owner","amount":12}]},
                    {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]}],
                    "modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Claim}}","revision":1,"cardPolicies":[{"id":"actual-pair-claim","kind":"pindianClaimAllWhenSourceWins"}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, Claim }.ToDictionary(id => id, id => new { name = id, description = "真实拼点加成小夹具" })
            }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, id) { Program = program });
            const string ownerPick = "fixture:shanhe-pindian-pick";
            builder.AddSkill(new(ownerPick, "固定玩家", "固定公开候选")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            var own = new List<string> { Driver, "classic:tianyi", "ol:gushe", Bonus };
            if (scene == "stacked") own.Add(Extra);
            if (scene != "group") own.Add(Claim);
            builder.AddGeneral(new(Owner, "真实赌术使用者", "supporter", ownerPick, "qun", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:shanhe-pindian-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:shanhe-pindian-peer-pick-" + i;
                builder.AddSkill(new(pick, "固定对手", "固定公开候选")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (4 - i)) });
                builder.AddGeneral(new(peers[i - 1], "真实拼点对手", "supporter", pick, "shu", 12,
                    scene == "group" && i == 1 ? [Quiet, Bonus] : [Quiet]));
            }
            builder.AddDeck(new("fixture:shanhe-pindian-deck", "六种真实点数小牌堆", 0, 0, [])
            { PhysicalCards = new[] { 7, 9, 10, 11, 12, 13 }.SelectMany(rank =>
                Enumerable.Range(0, 4).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, rank))).ToArray() });
            builder.AddMode(new(Mode, "真实赌术机制边界", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:shanhe-pindian-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
