using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DuanWeiChecks
{
    private const string Skill = "ol:taohuai", Gain = "fixture:dw-gain", Cost = "fixture:dw-cost";
    private const string Mode = "identity:classic-duan-wei-fixture";

    public static void ActualHandExtremaAlternateAtPaidUseAndColdReplay()
    {
        foreach (var tied in new[] { false, true })
        {
            var (g, registry) = Create(tied);
            var hand = V(g).Hand.ToArray();
            var maximum = hand.Max(c => c.Rank);
            var first = hand.First(c => c.Rank == maximum);
            NativeSlash(g, first.Id);
            Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            Need(States(g).Select(e => e.Value).SequenceEqual([true]), "The actual maximum (including tied maxima) flips Yang to Yin before its real draw child.");
            Need(g.CardMovements.Count(m => m.Reason.Value == "skill-program.ol:taohuai.Draw") == 1 &&
                g.CardMovements.Count(m => m.CardId == first.Id && m.Reason == CardMoveReasons.Use) == 1,
                "The native physical hand card is paid once and the extreme-use branch draws exactly one entity.");
            g = Cold(g, registry); Continue(g); Play(g);
            Need(!Facts<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Skill && e.BindingId == "non-extreme-hand-use-discard"),
                "The frozen original Yang mode prevents a second branch after the same Use changes mode and hand.");
            var second = V(g).Hand.MinBy(c => c.Rank)!;
            NativeSlash(g, second.Id);
            Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            Need(States(g).Select(e => e.Value).SequenceEqual([true, false]) &&
                g.CardMovements.Count(m => m.Reason.Value == "skill-program.ol:taohuai.Draw") == 2,
                "The following actual minimum uses the retained Yin mode, draws once and returns to Yang.");
            g = Cold(g, registry); Continue(g); Play(g); _ = Cold(g, registry);
            Need(!Facts<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Skill && e.BindingId == "non-extreme-hand-use-discard"),
                "Neither successful native Use offers the otherwise-discard branch after a cold gain-child return.");
        }
    }

    public static void ActualNonExtremeOptionalDiscardDeclinesAndPaysOnceCold()
    {
        var (g, registry) = Create(false);
        var hand = V(g).Hand.ToArray();
        Need(hand.Min(c => c.Rank) < hand.Max(c => c.Rank), "The fixed seed provides distinct native hand ranks.");
        NativeSlash(g, hand.MinBy(c => c.Rank)!.Id);
        Reach(g, p => HasOptional(p));
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
        Need(!States(g).Any() && !g.CardMovements.Any(m => m.Reason.Value == "skill-program.ol:taohuai.Draw"),
            "Declining the otherwise branch keeps Yang and never invents a draw.");
        var secondHand = V(g).Hand.ToArray();
        Need(secondHand.Min(c => c.Rank) < secondHand.Max(c => c.Rank), "The same fixed fixture retains a second non-maximum Use.");
        NativeSlash(g, secondHand.MinBy(c => c.Rank)!.Id);
        Reach(g, p => HasOptional(p));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Cards.Count == 1));
        Need(States(g).Select(e => e.Value).SequenceEqual([true]), "Accepting the conversion flips exactly once before its mandatory physical discard selection.");
        g = Cold(g, registry);
        var paidCard = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([paidCard]));
        Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        Need(g.CardMovements.Count(m => m.CardId == paidCard && m.Reason.Value == "skill-program.ol:taohuai.SelectAndMoveOwnedCard") == 1,
            "The selected native entity has actually left hand before its real discard child.");
        g = Cold(g, registry); Continue(g); Play(g); _ = Cold(g, registry);
        Need(States(g).Count() == 1 && g.CardMovements.Count(m => m.CardId == paidCard &&
            m.Reason.Value == "skill-program.ol:taohuai.SelectAndMoveOwnedCard") == 1 &&
            !g.CardMovements.Any(m => m.Reason.Value == "skill-program.ol:taohuai.Draw"),
            "A cold paid movement return repeats neither the conversion nor the physical discard and supplies no draw.");
    }

    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static IEnumerable<ProgramBooleanStateChangedEvent> States(GameEngine g) => Facts<ProgramBooleanStateChangedEvent>(g).Where(e => e.SkillId == Skill && e.OwnerSeat == 0);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool HasOptional(PendingDecision p) => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Skill && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void NativeSlash(GameEngine g, int cardId)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == cardId && a.TargetSeats.Count == 1);
        Accept(g, new PlayCardCommand(0, cardId, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 240; step++)
        {
            if (P(g) is { } prompt && predicate(prompt)) return;
            if (P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (P(g) is { PlayerSeat: 0 } optional && optional.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The actual Taohuai boundary was not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Private views, owning frames, native movements and accepted commands restore exactly at the real pending boundary.");
        return restored;
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool tied)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(tied));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:dw-owner", g.Revision, P(g)!.PromptId)); Play(g);
        return (g, registry);
    }
    private sealed class Fixture(bool tied) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-duan-wei", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = """
            {"schemaVersion":$SCHEMA$,"skills":[
              {"id":"fixture:dw-quota","revision":1,"modifiers":[{"id":"unlimited-native-slash","query":"slashLimit","operation":"unlimited","value":0,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:dw-gain","revision":1,"triggers":[{"id":"native-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:taohuai.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:dw-cost","revision":1,"triggers":[{"id":"native-discard-child","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementReasons":["skill-program.ol:taohuai.SelectAndMoveOwnedCard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
            {
                ["fixture:dw-quota"] = new { name = "无限出杀夹具", description = "固定真实用牌测试" },
                [Gain] = new { name = "真实摸牌子帧", description = "固定真实用牌测试", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Cost] = new { name = "真实弃牌子帧", description = "固定真实用牌测试", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            }});
            var programs = SkillProgramCatalog.Load(rules, presentation);
            foreach (var pair in programs.Programs) b.AddSkill(new(pair.Key, pair.Key, "固定真实用牌测试") { Program = pair.Value });
            b.AddSkill(new("fixture:dw-owner-pick", "固定主公", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:dw-other-pick", "固定其他", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            b.AddGeneral(new("fixture:dw-owner", "段煨真实用牌", "ol-duan-wei", "fixture:dw-owner-pick", "qun", 4, [Skill, "fixture:dw-quota", Gain, Cost]));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:dw-other-{seat}", "固定其他", "supporter", "fixture:dw-other-pick", "wei", 12));
            b.AddDeck(new("fixture:dw-deck", "固定点数实体杀", 8, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Club, tied ? 7 : i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "段煨真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:dw-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:dw-owner", "fixture:dw-other-1", "fixture:dw-other-2", "fixture:dw-other-3"]));
        }
    }
}
