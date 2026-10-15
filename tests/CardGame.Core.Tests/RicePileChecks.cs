using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RicePileChecks
{
    private const string Yi = "ol:yishe", Bu = "ol:bushi", Mi = "ol:midao", Second = "fixture:rice-second";
    private const string Driver = "fixture:rice-driver", Owner = "fixture:rice-owner", Watch = "fixture:rice-watch", Mode = "identity:classic-rice";
    public static void ActualEndingDrawHEStoreAndColdMovementChildren()
    {
        var (g, registry) = Start(child: "pause", mixed: true);
        Use(g, "draw20"); Play(g);
        var lion = V(g, 0).Hand.First(c => c.Kind == CardKind.SilverLion);
        var gear = g.GetHumanLegalActions().Single(a => a.CardId == lion.Id && a.Kind == LegalActionKind.Equip);
        Accept(g, new PlayCardCommand(0, lion.Id, gear.TargetSeats, g.Revision, P(g)!.PromptId, gear.PlayedCardKind, gear.TargetCardId)); Play(g);
        Use(g, "lose-one"); Play(g); var hp = V(g, 0).Hp;
        End(g); Reach(g, () => Optional(P(g), Yi)); Activate(g);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RicePile is not null);
        Need(root.RicePile is { Stage: RicePileStage.DrawChildren, DrawActual: 2 } && root.PendingMovementContinuation is not null,
            "The ending pauses under its actual paid draw receipt and real gain observer.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, () => RicePrompt(P(g))); var f = g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && P(g)!.Choices.Any(c => c.Cards.SequenceEqual([lion.Id])),
            "Yishe stores actual own Hand or Equipment, including an equipped Silver Lion, using a private owner selection.");
        Answer(g, c => c.Cards.SequenceEqual([lion.Id])); Answer(g, c => c.Cards.Count == 1); g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("rice-action") == "pay"); Reach(g, () => E<RicePileCompletedEvent>(g).Any(e => e.ProgramFrameId == f.Id));
        var paid = E<RicePilePaymentIssuedEvent>(g).Single(e => e.ProgramFrameId == f.Id).Receipt;
        Need(paid.CardIds.Count == 2 && paid.From.Contains(CardLocation.Equipment(0)) && paid.From.Contains(CardLocation.Hand(0)) &&
            E<RicePileDrawIssuedEvent>(g).Count(e => e.ProgramFrameId == f.Id && e.Requested == 2 && e.Actual == 2) == 1 && V(g, 0).Hp == hp + 1 &&
            Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).Players[0].PublicPersistentPileCount == 2 &&
                g.CreateSnapshot(s).Players[0].PublicPersistentPileCards!.Select(c => c.Id).Order().SequenceEqual(paid.CardIds.Order())),
            "Two actual HE entities become publicly visible rice, the actual Silver Lion loss recovers once, and cold recovery never redraws or repays.");
        Play(g); End(g); Reach(g, () => E<TurnStartedEvent>(g).Last().ActorSeat != 0);
        Need(!Optional(P(g), Yi), "A nonempty exact rice pile does not offer the next ending draw."); Cold(g, registry);
    }
    public static void AnyJudgmentLastRiceRecoveryAndMultiplePileIdentity()
    {
        var (g, registry) = Start(secondPile: true); Fill(ref g, registry, 0); Reach(g, () => Optional(P(g), Second));
        Activate(g); Reach(g, () => RicePrompt(P(g))); Answer(g, c => c.Cards.Count == 1); Answer(g, c => c.Cards.Count == 1);
        Answer(g, c => c.Parameters.GetValueOrDefault("rice-action") == "pay"); Play(g);
        var piles = V(g, 0).PublicPersistentPiles!; Need(piles.Count == 2 && piles.All(p => p.Count == 2), "Two producer instances own two real separately named public rice piles.");
        Use(g, "incoming-one", targets: [1]); Reach(g, () => Optional(P(g), Bu)); Activate(g); Reach(g, () => RicePrompt(P(g))); Answer(g, c => c.Cards.Count == 1); Play(g);
        var primary = V(g, 0).PublicPersistentPiles!.Single(p => p.SourceSkillId == Yi); var secondary = V(g, 0).PublicPersistentPiles!.Single(p => p.SourceSkillId == Second);
        Need(primary.Count == 1 && secondary.Count == 2, "The consumer references only the matching producer domain.");
        Use(g, "judge", targets: [2]); Reach(g, () => P(g) is { Kind: DecisionKind.ProgramJudgmentReplacement } p && p.ValidCardIds.Count > 0);
        var judged = g.ResolutionStack.OfType<JudgmentFrame>().Last(); var old = judged.CardId; var rice = primary.Cards.Single().Id; var hp = V(g, 0).Hp;
        Need(P(g)!.PlayerSeat == 0 && P(g)!.ValidCardIds.SequenceEqual([rice]) && judged.TargetSeat == 2,
            "Any actual participant's judgment offers only referenced rice, excluding hand, equipment and a distinct public pile.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([rice]));
        Reach(g, () => E<JudgmentResolvedEvent>(g).Any(e => e.ResolutionId == judged.Id));
        var paid = E<RicePilePaymentIssuedEvent>(g).Single(e => e.Receipt.Operation == SkillProgramEffectOp.ReplaceJudgmentFromPublicRice).Receipt;
        var moves = g.CardMovements.Where(m => m.Sequence > paid.Before && m.Sequence <= paid.After).ToArray();
        Need(moves.Count(m => m.CardId == old && m.From == CardLocation.Judgment(2) && m.To == CardLocation.DiscardPile) == 1 &&
            moves.Count(m => m.CardId == rice && m.From == primary.Location && m.To == CardLocation.Processing) == 1 &&
            moves.Count(m => m.CardId == rice && m.From == CardLocation.Processing && m.To == CardLocation.Judgment(2)) == 1 &&
            E<JudgmentResolvedEvent>(g).Single(e => e.ResolutionId == judged.Id).CardId == rice && V(g, 0).Hp == hp + 1 &&
            E<RicePileRecoveryIssuedEvent>(g).Count(e => e.Pile.Location == primary.Location) == 1 &&
            !E<RicePileRecoveryIssuedEvent>(g).Any(e => e.Pile.Location == secondary.Location) &&
            V(g, 0).PublicPersistentPiles!.Single(p => p.SourceSkillId == Second).Count == 2,
            "One real rice replacement pays three exact movements; only its emptied source recovers before the judgment resolves, while the other pile remains intact."); Cold(g, registry);
    }
    private static string Embedded(string suffix)
    { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-zhang-lu." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool RicePrompt(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "rice-pile") == true;
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Fill(ref GameEngine g, ContentRegistry registry, int seat)
    {
        var game = g; End(game); Reach(game, () => Optional(P(game), Yi) && P(game)!.PlayerSeat == seat); Activate(game);
        Reach(game, () => RicePrompt(P(game))); game = Cold(game, registry);
        Answer(game, c => c.Cards.Count == 1); Answer(game, c => c.Cards.Count == 1); Answer(game, c => c.Parameters.GetValueOrDefault("rice-action") == "pay");
        Reach(game, () => E<RicePileCompletedEvent>(game).Any(e => e.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice &&
            E<RicePileStartedEvent>(game).Any(s => s.ProgramFrameId == e.ProgramFrameId && s.Receipt.Source.OwnerSeat == seat))); g = game;
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 300; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Rice fixture ended before actual boundary.");
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else if (p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p?.Choices.Count > 0 && (p.PlayerSeat == 0 || p.Kind == DecisionKind.ProgramTrigger))
            {
                var c = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ?? p.Choices[0];
                Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Rice fixture exceeded its bounded actual commands: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real rice command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) && JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements) &&
            JsonSerializer.Serialize(g.Events.Select(e => e.Payload)) == JsonSerializer.Serialize(copy.Events.Select(e => e.Payload)),
            "Cold command replay preserves all four viewer projections, exact owning receipts, event history and actual movement boundaries."); return copy;
    }
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static (GameEngine, ContentRegistry) Start(string child = "none", bool mixed = false, bool ownerBushi = true, bool peerRice = false, bool secondPile = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(child, mixed, ownerBushi, peerRice, secondPile));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = child == "death" ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string child, bool mixed, bool ownerBushi, bool peerRice, bool secondPile) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("rice-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Register(SkillProgramCatalog c) { foreach (var (id, program) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description)
                { Program = program, ProgramPresentation = c.Presentations[id],
                    SelectionWeights = id is Driver or "fixture:rice-quiet" ? Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Driver ? -10000d : 10000d) : null }); }
            Register(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            if (secondPile)
            {
                var n = JsonNode.Parse(Embedded("rules.json"))!; var producer = n["skills"]![0]!.DeepClone(); producer["id"] = Second;
                n["skills"] = new JsonArray(producer); var p = JsonNode.Parse(Embedded("presentation.json"))!; var label = p["skills"]![Yi]!.DeepClone();
                p["skills"] = new JsonObject { [Second] = label }; Register(SkillProgramCatalog.Load(n.ToJsonString(), p.ToJsonString()));
            }
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw20","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"incoming-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"incoming-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                {"id":"self-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"judge","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.fixture.rice-judgment","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]}]},
              {"id":"fixture:rice-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}
            ]}
            """)!;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实米机制准备", description = "真实命令准备" },
                ["fixture:rice-quiet"] = new { name = "安静其他角色", description = "实际其他角色不主动杀" } };
            if (child != "none")
            {
                var effect = child == "pause" ? "" : ", {\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}";
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-rice-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.rice.draw"],"usageScope":"game","usageLimit":1,"optional":false,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}{{effect}}]}]}
                """));
                labels[Watch] = new { name = "摸牌子窗", description = "实际已付摸牌子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
                if (child == "lose-source")
                {
                    rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""
                    {"id":"fixture:rice-loss","revision":1,"triggers":[{"id":"real-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":8,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Yi}}"],"sourceBind":"standard:none"}]}]}
                    """)); labels["fixture:rice-loss"] = new { name = "实际源丢失", description = "真实濒死回复后移除义舍" };
                }
            }
            Register(SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var extras = new List<string> { Yi, Mi }; if (ownerBushi) extras.Add(Bu); if (secondPile) extras.Add(Second);
            if (child != "none") extras.Add(Watch); if (child == "lose-source") extras.Add("fixture:rice-loss");
            b.AddGeneral(new(Owner, "真实米拥有者", "supporter", Driver, "qun", 8, extras));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:rice-peer-{i}").ToArray();
            for (var i = 0; i < 3; i++) b.AddGeneral(new(peers[i], "实际其他角色", "supporter", "fixture:rice-quiet", "wei", 8, peerRice ? [Yi, Mi, Bu] : []));
            b.AddCard(new("fixture:rice-lion", "白银狮子", "装备牌", "真实白银狮子", CardKind.SilverLion));
            var cards = mixed ? new[] { "standard:dodge", "fixture:rice-lion", "standard:dodge" } : ["standard:dodge"];
            b.AddDeck(new("fixture:rice-deck", "固定实际米牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], i % 2 == 0 ? Suit.Spade : Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "真实米机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:rice-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
