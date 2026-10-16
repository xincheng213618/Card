using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

// Homogeneous physical recipes remove setup-shuffle assumptions while retaining
// the existing command/runner framework and real entity payments.
internal static class RecipientContestConsequencesChecks
{
    private const string Contest = "ol:jianshu", Benefit = "ol:yongdi", Driver = "fixture:rcc-driver", Pause = "fixture:rcc-pause";
    private const string Mode = "identity:classic-recipient-consequences-check";

    public static void ActualBlackGiftSelfSecondAndTie()
    {
        var (g, r) = Start(); var before = g.State.Players.Select(p => p.Hp).ToArray();
        Gift(g); Reach(g, p => ProgramAction(p, "black-gift-contest"));
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().BlackGiftContest!;
        Require(receipt.RecipientSeat == 1 && g.CardMovements.Count(m => m.Sequence > receipt.GiftBefore && m.Sequence <= receipt.GiftAfter) == 1,
            "A genuine one-card Hand gift is paid before the second participant choice.");
        Require(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])), "The issuer is a legal distinct second participant with actual remaining Hand.");
        Require(g.CreateSnapshot(2).PendingDecision is null, "The private second-participant choice is visible only to its chooser.");
        Cold(g, r); RejectUnpublished(g); Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.Kind == DecisionKind.SkillModule && p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-card"));
        Require(g.ResolutionStack.OfType<PindianFrame>().Single().SourceSeat == 1, "The recipient chooses its own Pindian input through the mature child.");
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "pindian-card"); ReachPlay(g);
        var result = Facts<BlackGiftContestResultCapturedEvent>(g).Single().Result;
        Require(result.SourceRank == 7 && result.OpponentRank == 7 && Facts<BlackGiftContestDiscardPaidEvent>(g).Length == 0,
            "A true equal-rank result has no invented winner or discard.");
        Require(g.State.Players[0].Hp == before[0] - 1 && g.State.Players[1].Hp == before[1] - 1 && Facts<BlackGiftContestLossPaidEvent>(g).Length == 2,
            "Both original non-winning participants pay one real HP loss in a tie.");
        Require(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Contest), "The game-limited issued fact removes the second activation.");
        Cold(g, r); Conserve(g);
    }

    public static void ActualBlackGiftWinnerOwnPrivateDiscardsColdReplay()
    {
        var (g, registry) = Start(recipientWins: true);
        var hp = g.State.Players.Select(p => p.Hp).ToArray();
        Gift(g); Reach(g, p => ProgramAction(p, "black-gift-contest"));
        Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.Kind == DecisionKind.SkillModule && p.PlayerSeat == 0 &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-card"));
        Answer(g, c => c.Parameters.GetValueOrDefault("action") == "pindian-card");
        Reach(g, p => ProgramAction(p, "black-gift-contest"));
        var result = Facts<BlackGiftContestResultCapturedEvent>(g).Single().Result;
        Require(result.SourceSeat == 1 && result.SourceWon && result.SourceRank == 13 && result.OpponentRank == 7,
            "The real recipient wins against the distinct skill issuer.");
        var winnerHand = g.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).ToArray();
        var prompt = P(g)!;
        Require(prompt.PlayerSeat == 1 && prompt.TargetSeat == 1 && prompt.IsPrivate &&
            prompt.Choices.All(c => c.Cards.Count == 1 && winnerHand.Contains(c.Cards.Single())) &&
            prompt.Choices.SelectMany(c => c.Cards).Order().SequenceEqual(winnerHand.Order()),
            "The winner sees exact own cards for mandatory self-discard.");
        Require(new[] { 0, 2, 3 }.All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "The skill issuer and other viewers cannot inspect the winner's private discard choices.");
        Cold(g, registry); RejectUnpublished(g);
        var wrongActor = g.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, g.Revision));
        Require(!wrongActor.Accepted, "The issuer cannot answer the winner's private payment.");
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BlackGiftContest is not null).BlackGiftContest!;
        Require(receipt.RequiredDiscards == 2 && receipt.SelectedDiscardIds is [var first] &&
            P(g)!.PlayerSeat == 1 && P(g)!.Choices.All(c => c.Cards.Count == 1 && c.Cards.Single() != first) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == first).Location == CardLocation.Hand(1) &&
            g.State.Players[1].HandCount == winnerHand.Length && Facts<BlackGiftContestDiscardPaidEvent>(g).Length == 0,
            "The second choice excludes the first card without paying either selection early.");
        Require(new[] { 0, 2, 3 }.All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "The partial selection remains private to the winner.");
        Cold(g, registry);
        Accept(g, new AdvanceOneStepCommand(g.Revision)); ReachPlay(g);
        var paid = Facts<BlackGiftContestDiscardPaidEvent>(g).Single();
        Require(paid.WinnerSeat == 1 && paid.Count == 2 && g.State.Players[1].HandCount == winnerHand.Length - 2 &&
            g.CardMovements.Count(m => m.Sequence > paid.Before && m.Sequence <= paid.After &&
                m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 2,
            "The winner actually discards exactly two own entities once.");
        Require(g.State.Players[0].Hp == hp[0] - 1 && g.State.Players[1].Hp == hp[1] &&
            Facts<BlackGiftContestLossPaidEvent>(g) is [{ Loss.Seat: 0 }],
            "Only the original losing participant loses one HP after the discard.");
        Cold(g, registry); Conserve(g);
    }

    private static void Gift(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Contest, "limited-black-hand-recipient-contest",
        [g.CreateSnapshot(0).Players[0].Hand.First().Id], [1], g.Revision, P(g)!.PromptId));
    private static bool ProgramAction(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static List<CharacterState> Players(GameEngine g) => (List<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            var p = P(g); if (p is not null && predicate(p)) return;
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) throw new InvalidOperationException("Unexpected own Play before the focused boundary.");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException($"Fixed focused boundary not reached: {BoundaryState(g)}");
    }
    private static string BoundaryState(GameEngine g) => JsonSerializer.Serialize(new
    {
        g.Revision,
        g.State.Status,
        g.State.CurrentSeat,
        g.State.Phase,
        Prompt = P(g),
        Stack = g.ResolutionStack.Select(f => new { f.Id, Kind = f.GetType().Name }),
        Contest = g.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.BlackGiftContest is not null)?.BlackGiftContest,
        LastFacts = g.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))
    });
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected real fixture command."); }
    private static void RejectUnpublished(GameEngine g) { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice is atomically rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Stack = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "All private views, exact paid receipts, typed children and event history cold-restore through accepted commands.");
    private static void Conserve(GameEngine g) => Require(g.CreateCardZoneDiagnostics().Count == 80 && g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 80, "Every actual card entity retains one zone.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(bool winner = false, bool benefit = false, bool trick = false, bool woodenOx = false, bool protect = true, bool humanCounterspellOnly = false, bool recipientWins = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(winner, trick, woodenOx, protect, humanCounterspellOnly, recipientWins));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:rcc-owner", g.Revision, P(g)!.PromptId));
        if (benefit) Reach(g, p => p.SkillPrompt?.SkillId == Benefit && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        else ReachPlay(g);
        return (g, registry);
    }
    private sealed class Fixture(bool winner, bool trick, bool woodenOx, bool protect, bool humanCounterspellOnly, bool recipientWins) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-recipient-consequences", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"viewAs":[{"id":"real-hand-as-draw-two","inputKinds":["nullification"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"drawTwo","singleCardTrickUse":true,"forPlay":true,"forResponse":false}],"activations":[{"id":"source-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:yongdi"],"sourceBind":"standard:none"}]}]},{"id":"{{Pause}}","revision":1,"triggers":[{"id":"paid-discard-child","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementOccurrence":"perCard","movementReasons":["skill-program.ol:jianshu.GiveBlackHandAndResolveRecipientContest.discard","equipment.wooden-ox.grain-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-child","options":[{"id":"continue"}]}]}]},{"id":"fixture:rcc-rank","revision":1,"cardPolicies":[{"id":"club-rank","kind":"pindianRankBySuit","inputSuit":"club","value":13}]}]}""";
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实来源变化与用牌", description = "共享原生能力夹具" }, [Pause] = new { name = "实际弃牌孩子", description = "真实成本后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, ["fixture:rcc-rank"] = new { name = "原生拼点点数", description = "确定性 13 点" } };
            var document = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
            document["skills"]![0]!["viewAs"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"real-hand-as-iron-chain","inputKinds":["nullification"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"ironChain","singleCardTrickUse":true,"forPlay":true,"forResponse":false}"""));
            if (humanCounterspellOnly)
                document["skills"]![0]!["viewAs"]![0]!["inputKinds"] = new System.Text.Json.Nodes.JsonArray("slash");
            var catalog = SkillProgramCatalog.Load(document.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "Focused source fixture") { Program = pair.Value });
            foreach (var owner in new[] { false, true }) b.AddSkill(new(owner ? "fixture:rcc-owner-pick" : "fixture:rcc-peer-pick", "固定公开选将", "角色评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var ownerSkills = new List<string> { Contest, Benefit, Driver, Pause };
            if (protect) ownerSkills.Add("ol:zhenlue");
            if (winner) ownerSkills.Add("fixture:rcc-rank");
            b.AddGeneral(new("fixture:rcc-owner", "真实发行者", "supporter", "fixture:rcc-owner-pick", "wei", 8, ownerSkills.ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:rcc-peer-{i}", "真实其他角色", "supporter", "fixture:rcc-peer-pick", "wei", 8,
                recipientWins ? ["classic:hujia", "classic:xueyi", "fixture:rcc-rank"] : ["classic:hujia", "classic:xueyi"]));
            b.AddDeck(new("fixture:rcc-deck", "同质实体", woodenOx ? 6 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(index => new ContentDeckPhysicalCard(humanCounterspellOnly ? index == 31 ? "standard:nullification" : "standard:slash" :
                    woodenOx ? "classic:wooden-ox" : trick ? "standard:nullification" : "standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "真实赠牌与主公技资格", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:rcc-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:rcc-owner", "fixture:rcc-peer-1", "fixture:rcc-peer-2", "fixture:rcc-peer-3"]));
        }
    }
}
