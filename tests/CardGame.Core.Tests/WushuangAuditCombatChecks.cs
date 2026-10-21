using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushuangAuditCombatChecks
{
    private const string Driver = "fixture:wushuang-audit-combat-driver", Quiet = "fixture:wushuang-audit-combat-quiet", Human = "fixture:wushuang-audit-combat-human";
    private static readonly Lazy<ContentRegistry> CampRegistry = new(() => ContentRegistry.Build(new Fixture(true)));
    private static readonly Lazy<ContentRegistry> WangongRegistry = new(() => ContentRegistry.Build(new Fixture(false)));

    public static void JieyingClaimsOnlyAfterNativeTurnEndedAndResumesGainChildCold()
    {
        var g = Start(true);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "wushuang:skill-3230");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(g, () => P(g)?.Choices.Any(c => c.Targets.Contains(1)) == true);
        Answer(g, c => c.Targets.Contains(1));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Driver &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WushuangCampClaim is not null));
        var claimFrame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.WushuangCampClaim is not null);
        var receipt = claimFrame.WushuangCampClaim!;
        var facts = g.Events.Select(e => e.Payload).ToArray();
        var endedIndex = Array.FindIndex(facts, e => e is TurnEndedEvent ended && ended.ActorSeat == 1 && ended.TurnNumber == receipt.ActualTurn);
        var claimIndex = Array.FindIndex(facts, e => e is WushuangCampClaimedEvent claim && claim.ProgramFrameId == claimFrame.Id);
        Require(endedIndex >= 0 && claimIndex > endedIndex && receipt.EndedSeat == 1 && receipt.Cards.Count >= 5 &&
            Player(g, 1).HandCount == 0 && receipt.Cards.Count == g.CardMovements.Count(m => m.Sequence > receipt.SequenceBefore &&
                m.Sequence <= receipt.SequenceAfter && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)),
            "Camp collection includes the recipient's real ending-stage draw and occurs only after that recipient's native TurnEnded fact.");
        Cold(g, true); Play(g);
        Require(E<WushuangCampClaimedEvent>(g).Count(e => e.ProgramFrameId == claimFrame.Id) == 1 &&
            receipt.Cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(1) &&
                m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.wushuang.jieying.ended-hand") == 1),
            "Returning the gained-card child does not collect the paid hand or transfer its Camp twice.");
        Cold(g, true);
    }

    public static void WangongKeepsLastActualBasicAcrossTurnsAndCapturesAcceptedSlash()
    {
        var g = Start(false);
        Use(g, "obtain-slash"); Play(g); Use(g, "obtain-alcohol"); Play(g); Use(g, "obtain-draw"); Play(g);
        var alcohol = Card(g, CardKind.Alcohol); Play(g);
        var alcoholUse = E<CardUseDeclaredEvent>(g).Single(e => e.ResolutionId == alcohol);
        var oldTurn = V(g).TurnNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => V(g).TurnNumber > oldTurn && P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(!Player(g, 0).HasAlcoholEffect, "A new actual turn expires the old Alcohol damage boost.");
        var slash = Card(g, CardKind.Slash);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Driver &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == slash && f.WushuangWangong is not null));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == slash);
        var receipt = use.WushuangWangong!;
        Require(receipt.PreviousOriginFrameId == alcoholUse.ResolutionId && receipt.UseFrameId == slash &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2),
            "The previous actual Alcohol use survives phase and turn boundaries; an accepted out-of-range Slash captures that exact previous use and deals the bonus damage.");
        Cold(g, false); Play(g);
        var next = Card(g, CardKind.Slash); Play(g);
        Require(E<WushuangWangongUseCapturedEvent>(g).Count(e => e.Receipt.UseFrameId == slash || e.Receipt.UseFrameId == next) == 2,
            "The second actual Slash remains legal after the ordinary one-Slash quota and captures the preceding actual Slash.");
        Card(g, CardKind.DrawTwo); Play(g);
        Require(!g.GetHumanLegalActions().Any(a => a.CardId is { } id && Player(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash)),
            "An actual non-basic use clears Wangong's future unlimited-distance and unlimited-quota privilege.");
        Cold(g, false);
    }

    private static GameEngine Start(bool camp)
    {
        var registry = camp ? CampRegistry.Value : WangongRegistry.Value;
        var g = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(1) with
        { AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Human, g.Revision, P(g)!.PromptId)); Play(g); return g;
    }
    private static GameSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat);
    private static PlayerSnapshot Player(GameEngine g, int seat) => V(g).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => V(g).PendingDecision;
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Use(GameEngine g, string binding) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], [], g.Revision, P(g)!.PromptId));
    private static long Card(GameEngine g, CardKind kind)
    {
        var action = g.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && Player(g, 0).Hand.Any(c => c.Id == id && c.Kind == kind) &&
            (kind is CardKind.DrawTwo or CardKind.Alcohol || a.TargetSeats.Contains(1)));
        Require(action is not null, "The actual " + kind + " must be legal at its native quota and distance gates.");
        var before = E<CardUseDeclaredEvent>(g).Select(e => e.ResolutionId).ToHashSet();
        Accept(g, new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId));
        return E<CardUseDeclaredEvent>(g).Last(e => !before.Contains(e.ResolutionId)).ResolutionId;
    }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 350; i++)
        {
            if (ready()) return;
            if (V(g).Wushuang is { CanShop: true } shop)
            {
                if (shop.Phase == WushuangShopPhase.OpeningTactic)
                {
                    var offer = shop.Offers.SingleOrDefault(o => o.TacticId == 2030);
                    Require(offer is not null, "The fixed audit seed retains the harmless DrawTwo opening tactic.");
                    Accept(g, new BuyWushuangOfferCommand(0, offer!.Id, null, g.Revision));
                }
                else Accept(g, new FinishWushuangShopCommand(0, g.Revision));
            }
            else if (P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, play.PromptId));
            else if (P(g) is { PlayerSeat: 0, Choices.Count: > 0 })
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip" ||
                    c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The audit fixture did not reach its exact native boundary: " + SnapshotJson.Serialize(V(g)));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate) ?? p.Choices[0]; Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision)); }
    private static void Cold(GameEngine g, bool camp)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), camp ? CampRegistry.Value : WangongRegistry.Value);
        Require(Enumerable.Range(0, 2).All(s => SnapshotJson.Serialize(V(g, s)) == SnapshotJson.Serialize(V(restored, s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Cold replay preserves both private views and exact paid movement at the accepted-use or completed-turn child.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Audit command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool camp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:wushuang-audit-combat", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var source = StandardWushuangContentRegistry.Create();
            foreach (var x in source.Cards.Values) b.AddCard(x);
            foreach (var x in source.Skills.Values) b.AddSkill(x);
            foreach (var x in source.Generals.Values) b.AddGeneral(x);
            foreach (var x in source.Decks.Values) b.AddDeck(x);
            foreach (var x in source.Modes.Values.Where(m => m.Id != WushuangMode.ModeId)) b.AddMode(x);
            var activations = new[] { ("obtain-slash", "slash", 4), ("obtain-alcohol", "alcohol", 1), ("obtain-draw", "drawTwo", 1) }
                .Select(x => new { id = x.Item1, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "obtainWushuangDeckCards", target = "owner", amount = x.Item3, outputKind = x.Item2 } } }).ToArray();
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new object[] {
                new { id = Driver, revision = 1, activations,
                    cardPolicies = new[] { new { id = "no-dodge", kind = "prohibitNearbyTargetResponse", cardKinds = new[] { "slash", "fireSlash", "thunderSlash" }, value = 20 } },
                    triggers = new object[] {
                        new { id = "gain-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perCard", optional = true, effects = new[] { new { op = "draw", target = "owner", amount = 1 } } },
                        new { id = "damage-child", window = "afterDamageApplied", subject = "damageSource", damageOccurrence = "perDamage", optional = true, effects = new[] { new { op = "draw", target = "owner", amount = 1 } } } } },
                new { id = Quiet, revision = 1,
                    modifiers = camp ? Array.Empty<object>() : new object[] { new { id = "far-away", query = "incomingDistance", operation = "add", value = 2, priority = 0 } },
                    triggers = new[] { new { id = "true-ending-draw", window = "turnEnding", subject = "owner", optional = false, effects = new[] { new { op = "draw", target = "owner", amount = 1 } } } } }
            } });
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Driver] = new { name = "官方版本主流程", description = "实际牌、实际伤害与暂停子项。" }, [Quiet] = new { name = "时点对手", description = "真实结束阶段摸牌和距离。" } } });
            var programs = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, id, "Audit fixture") { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Human, "官方版本测试", "", Driver, "qun", BaseHp: 30, AdditionalSkillIds: [camp ? "wushuang:skill-3230" : "wushuang:skill-3263"]));
            b.AddGeneral(new("fixture:wushuang-audit-combat-peer", "对手", "", Quiet, "qun", BaseHp: 30));
            b.AddDeck(new("fixture:wushuang-audit-combat-deck", "固定实际牌", 4, 0, []) { PhysicalCards =
                Enumerable.Range(0, 4).SelectMany(_ => new[] { "standard:draw_two", "standard:alcohol", "standard:slash" }).Select(id => new ContentDeckPhysicalCard(id, Suit.Spade, 2))
                    .Concat(Enumerable.Range(0, 503).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 2))).ToArray() });
            b.AddMode(new(WushuangMode.ModeId, "官方版本主流程验证", 2, 2, new Dictionary<string, int>(), "fixture:wushuang-audit-combat-deck", 2,
                [Human, "fixture:wushuang-audit-combat-peer"], ContentModeKind.Team,
                new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 }));
        }
    }
}
