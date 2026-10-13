using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FriendEventChecks
{
    private const string Driver = "fixture:friend-events-driver", Mode = "identity:friend-events-fixture";
    public static void RecastDuelSharesDiscardBudgetAndColdReplay()
    {
        var (g, r) = Create(false);
        var original = g.CreateSnapshot(0).Players[0].Hand.First().Id;
        var hand = g.CreateSnapshot(0).Players[0].HandCount;
        Use(g, "friend:qinying", "recast-duel", [original], [1]); Play(g);
        Require(g.CardMovements.Count(m => m.CardId == original && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
            Facts<CardRecastEvent>(g).Count(e => e.CardId == original) == 1, "Qinying recasts its exact original physical card once.");
        Require(Facts<FriendDuelDiscardPaidEvent>(g).ToArray() is [var payment] && payment.ResponderSeat == 1 && payment.ResponseCount == 1,
            "The opponent may discard an allowed non-Slash and consumes the same single budget used by both participants.");
        Require(Facts<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Response &&
                e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Count == 0) &&
            Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0),
            "The discarded cost is separate from the virtual Slash, and the exhausted budget returns to the ordinary Duel failure path.");
        Require(Facts<CardUseDeclaredEvent>(g).Single(e => e.CardKind == CardKind.Duel).CardId == 0 &&
            g.CreateSnapshot(0).Players[0].HandCount == hand && !g.GetLegalActions().Any(a => a.ProgramSkillId == "friend:qinying"),
            "A real zero-entity Duel pays one actual phase use after the recast redraw.");
        Cold(g, r);
        var (all, ar) = Create(false, bothFriends: true); var options = Facts<FriendEventConfiguredEvent>(all).Where(e => e.OwnerSeat == 0).ToArray();
        Require(options.Length == 2 && options.Select(e => e.Option).Distinct().Count() == 2, "Opening category removal counts both seated friend generals once.");
        Cold(all, ar);
    }
    public static void MaximumPaymentRefundAndAscendingRankRemainStrict()
    {
        var (cui, cr) = Create(true); Require(!cui.GetLegalActions().Any(a => a.ProgramSkillId == "friend:biwei"), "A tied maximum has no legal Biwei payment.");
        Use(cui, Driver, "single", [], []); Play(cui); var highest = cui.CreateSnapshot(0).Players[0].Hand.Single();
        Use(cui, "friend:biwei", "discard-highest", [highest.Id], [1]); Play(cui);
        Require(cui.CardMovements.Count(m => m.Reason.Value == "skill.friend-biwei.cost") == 1 &&
            cui.CreateSnapshot(1).Players[1].HandCount == 0 && !cui.GetLegalActions().Any(a => a.ProgramSkillId == "friend:biwei"),
            "Biwei pays one unique maximum and discards every actual target hand card at or above its rank."); Cold(cui, cr);
        var (refund, rr) = Create(true); Use(refund, Driver, "empty-target", [], [1]); Play(refund); Use(refund, Driver, "single", [], []); Play(refund);
        Use(refund, "friend:biwei", "discard-highest", [refund.CreateSnapshot(0).Players[0].Hand.Single().Id], [1]); Play(refund);
        Use(refund, Driver, "single", [], []); Play(refund);
        Require(refund.GetLegalActions().Any(a => a.ProgramSkillId == "friend:biwei"), "No actual target discard refunds the actual Play phase debit."); Cold(refund, rr);
        var (shi, sr) = Create(false); Use(shi, Driver, "hurt", [], [1]); Play(shi);
        Require(!Facts<AscendingHandRankPaidEvent>(shi).Any(), "A tied maximum cannot pay Lunxiong after real damage.");
        Use(shi, Driver, "single", [], []); Play(shi); Use(shi, Driver, "hurt", [], [1]);
        Reach(shi, p => p.SkillPrompt?.SkillId == "friend:lunxiong" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(shi, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(shi);
        Require(Facts<AscendingHandRankPaidEvent>(shi).Single().Rank == 7 && shi.CreateSnapshot(0).Players[0].HandCount == 3, "A unique ascending rank pays once and draws exactly three actual cards.");
        shi = Cold(shi, sr); Use(shi, Driver, "single", [], []); Play(shi); Use(shi, Driver, "hurt", [], [1]); Play(shi);
        Require(Facts<AscendingHandRankPaidEvent>(shi).Count() == 1, "Cold recovery preserves the game-long strictly greater rank; equality does not reset eligibility.");
    }
    public static void MinimumSuitHoldStaysPrivateAndReturnsAtActualTurnEnd()
    {
        var (g, r) = Create(true, varied: true); Use(g, Driver, "pair", [], []); Play(g);
        var hand = g.CreateSnapshot(0).Players[0].Hand.OrderBy(c => c.Rank).ToArray();
        Require(hand.Length == 2 && hand[0].Rank < hand[1].Rank && hand.All(c => c.Suit == Suit.Heart), "The verified fixed seed supplies a unique minimum and another Heart without seed searching: " + JsonSerializer.Serialize(hand));
        var action = g.GetLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo && a.CardId == hand[0].Id);
        Accept(g, new PlayCardCommand(0, hand[0].Id, [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == "friend:shunyi" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(g);
        var hold = g.CreateSnapshot(0).Players[0].PrivateTurnHolds!.Single();
        Require(hold.Count == 1 && hold.Cards!.Single().Id == hand[1].Id && g.CreateSnapshot(1).Players[0].PrivateTurnHolds!.Single().Cards is null,
            "The remaining same-suit hand is held privately; observers see only the count.");
        Require(Facts<UsedSuitHandHeldEvent>(g).Count() == 1 && g.CardMovements.Count(m => m.Reason.Value == "skill.friend-shunyi.hold") == 1,
            "Only a Use of the frozen unique minimum creates one paid hold."); g = Cold(g, r);
        var turn = g.State.TurnNumber; Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => Facts<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn));
        Require(g.CardMovements.Count(m => m.CardId == hand[1].Id && m.Reason.Value == "skill.private-turn-hold.return") == 1 &&
            (g.CreateSnapshot(0).Players[0].PrivateTurnHolds?.Count ?? 0) == 0, "The actual ending prelude returns the held entity once after cold recovery."); Cold(g, r);
        var (ordinary, _) = Create(false); Use(ordinary, Driver, "single", [], []); Play(ordinary);
        var ordinaryCard = ordinary.CreateSnapshot(0).Players[0].Hand.Single();
        Accept(ordinary, new PlayCardCommand(0, ordinaryCard.Id, [], ordinary.Revision, P(ordinary)!.PromptId)); Play(ordinary);
        Require(!Facts<FriendMinimumHandUseEvent>(ordinary).Any() && !Facts<UsedSuitHandHeldEvent>(ordinary).Any(),
            "An ordinary unique-minimum Use never captures or publishes an unauthorized remaining-hand predicate.");
    }
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Use(GameEngine g, string skill, string binding, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, skill, binding, cards, targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 100; i++) { if (P(g) is { } p && predicate(p)) return; Step(g); } throw new InvalidOperationException("Friend event boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 150; i++) { if (predicate()) return; Step(g); } throw new InvalidOperationException("Friend event native fact missing."); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "friend-configuration")) Answer(g, _ => true);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Friend event command rejected."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack) && SnapshotJson.Serialize(g.CreateSnapshot(0)) == SnapshotJson.Serialize(restored.CreateSnapshot(0)), "Cold replay preserves paid frames and private views."); return restored; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(bool cui, bool bothFriends = false, bool varied = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new Fixture(cui, bothFriends, varied));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "friend:fixture-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(bool cui, bool bothFriends, bool varied) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-friend-events", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            string Read(string suffix)
            { using var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("friend-events." + suffix + ".json", StringComparison.Ordinal)))!); return reader.ReadToEnd(); }
            var friends = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var (id, program) in friends.Programs)
                b.AddSkill(new(id, friends.Presentations[id].Name, friends.Presentations[id].Description)
                { Program = program, ProgramPresentation = friends.Presentations[id] });
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
            {"id":"single","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"draw","target":"owner","amount":1}]},
            {"id":"pair","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"draw","target":"owner","amount":2}]},
            {"id":"empty-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true}]},
            {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]}]}
            """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> { [Driver] = new { name = Driver, description = "Focused actual friend event rules fixture" } } }));
            b.AddSkill(new(Driver, Driver, "Focused actual rules") { Program = catalog.Programs[Driver] });
            b.AddSkill(new("fixture:friend-pick-owner", "owner", "fixture") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:friend-pick-other", "other", "fixture") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -100000d : 100000d) });
            var skills = cui ? new[] { "friend:shunyi", "friend:biwei", "friend:cui-jun-gongli", Driver } : new[] { "friend:qinying", "friend:lunxiong", "friend:shi-tao-gongli", Driver };
            b.AddGeneral(new("friend:fixture-owner", "friend owner", "supporter", "fixture:friend-pick-owner", "qun", 12, skills));
            for (var i = 1; i < 4; i++) b.AddGeneral(new(bothFriends && i == 1 ? "friend:fixture-other-1" : $"fixture:friend-other-{i}", "friend other", "supporter", "fixture:friend-pick-other", "qun", 12));
            // Seed 31 redraws entities 1 and 53; give that pair distinct ranks without searching seeds.
            b.AddDeck(new("fixture:friend-deck", "fixed friend rules deck", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, varied ? i == 52 ? 2 : i % 13 + 1 : 7)).ToArray() });
            // Source instance names remain the real content. The opening population uses real friend generals in its pool.
            b.AddMode(new(Mode, "Friend events", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:friend-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: bothFriends ? ["friend:fixture-owner", "friend:fixture-other-1", "fixture:friend-other-2", "fixture:friend-other-3"] : ["friend:fixture-owner", "fixture:friend-other-1", "fixture:friend-other-2", "fixture:friend-other-3"]));
        }
    }
}
