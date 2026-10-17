using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class WuGuanYuRankConsumerChecks
{
    private const string RcDriver = "fixture:wjr-driver", RcWatch = "fixture:wjr-watch", RcGrant = "fixture:wjr-grant";
    private const string RcOwner = "fixture:wjr-owner", RcMode = "identity:classic-wu-rank-consumers";
    private const string RcJuewu = "wumiao:juewu", RcZiyuan = "ol:ziyuan", RcGift = "skill-program.ol:ziyuan.MoveBoundCards";

    public static void ForeignHandExactRankGiftKeepsPrintedEntitiesAndPaidSourceRightsCold()
    {
        var s = RcScenario.Start("gift"); s.Stock();
        var foreign = s.Hand().First(c => c.Rank == 13).Id;
        var eleven = s.Hand().Single(c => c.Rank == 11).Id;
        s.Give(foreign, 1); s.Take(1); s.Keep(foreign, eleven);
        s.Use("hurt", targets: [1]); s.Play();
        var acquisition = s.G.CardMovements.Last(m => m.CardId == foreign && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0));
        s.Need(acquisition.CardKind == CardKind.Crossbow && s.View().Skills?.Any(skill => skill.ContentId == RcJuewu) == true && s.Hand().Count == 2,
            "A real other Hand entry makes the original printed-K equipment an effective rank-two Hand entity.");
        var activation = s.G.GetHumanLegalActions().Single(a => a.ProgramSkillId == RcZiyuan);
        s.Need(activation.MinCardCount == 0 && activation.MaxCardCount == 0, "The real native exact-sum selector is available without preselecting physical costs.");
        s.Accept(new UseProgramSkillCommand(0, RcZiyuan, "gift-exact-thirteen-and-recover", [], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.RankPrompt()); s.Private();
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == RcZiyuan);
        var parentId = parent.Id;
        var draft = parent.OwnedHandRankSumSelection!;
        s.Need(draft.CandidateCardIds.Zip(draft.CandidateRanks).ToDictionary(x => x.First, x => x.Second) is var ranks &&
                ranks[foreign] == 2 && ranks[eleven] == 11 && s.P!.Choices.Count == 2 &&
                s.P.Choices.Single(c => c.Cards.SequenceEqual([foreign])).Description.Contains("（2）", StringComparison.Ordinal) &&
                s.P.Choices.All(c => c.Cards.Count == 1),
            "The private draft uses two plus eleven, displays effective two, and never publishes the printed-K one-card completion.");
        s.Reject(new AnswerPromptCommand(1, s.P!.PromptId, s.P.Choices[0].Id, s.G.Revision));
        s.Reject(new AnswerPromptCommand(0, s.P.PromptId, new ChoiceId($"owned-hand-rank-sum.frame-{parentId}.finish-0"), s.G.Revision));
        var retained = s.G.CreateSnapshot(0); var retainedJson = SnapshotJson.Serialize(retained);
        s.Cold(); var old = s.P!; var pick = old.Choices.Single(c => c.Cards.SequenceEqual([foreign]));
        s.Answer(pick); s.Reject(new AnswerPromptCommand(0, old.PromptId, pick.Id, s.G.Revision));
        s.Need(s.P!.Choices is [var remaining] && remaining.Cards.SequenceEqual([eleven]) &&
                !s.G.CardMovements.Any(m => m.Reason.Value == RcGift) && SnapshotJson.Serialize(retained) == retainedJson,
            "Selecting the effective two is unpaid, preserves prepared views, and still requires the actual eleven entity.");
        s.Answer(s.P.Choices.Single()); s.Cold();
        s.Need(s.P!.Choices is [var finish] && finish.Cards.Count == 0, "Only the complete effective thirteen has a published confirmation.");
        s.Answer(s.P.Choices.Single()); s.Reach(() => s.Hold("gift-paid")); s.Private();
        parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        var paid = parent.BoundParticipantGift!.RankSum!;
        var movements = s.G.CardMovements.Where(m => m.Reason.Value == RcGift).ToArray();
        s.Need(parent.InstructionIndex == 2 && paid.OwnerFrameId == parentId && paid.RequiredSum == 13 &&
                paid.Entities is IList { IsReadOnly: true } && paid.Entities.Count == 2 &&
                paid.Entities.Single(e => e.CardId == foreign) is { PrintedKind: CardKind.Crossbow, PrintedRank: 13, EffectiveRank: 2 } &&
                paid.Entities.Single(e => e.CardId == eleven) is { PrintedRank: 11, EffectiveRank: 11 } &&
                paid.Entities.All(e => e.From == CardLocation.Hand(0)) && movements.Length == 2 &&
                movements.Select(m => m.CardId).ToHashSet().SetEquals([foreign, eleven]) &&
                movements.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1) && m.CardKind == CardKind.Crossbow) &&
                s.Events<OwnedHandRankSumGiftIssuedEvent>().Count(e => e.FrameId == parentId) == 1 &&
                s.Events<OwnedHandRankSumGiftPaidEvent>().Single(e => e.FrameId == parentId).BatchId == paid.BatchId &&
                !s.Events<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
            "One paid typed gift freezes effective ranks independently of the unchanged original entities and drains its real movement child before recovery.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.View().Skills?.Any(skill => skill.ContentId == RcJuewu) != true && s.G.CardMovements.Count(m => m.Reason.Value == RcGift) == 2 &&
                s.Events<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
                s.Events<ProgramSkillResolvedEvent>().Count(e => e.FrameId == parentId && e.SkillId == RcZiyuan && e.Completed) == 1 &&
                s.Events<OwnedHandRankSumGiftPaidEvent>().Count(e => e.FrameId == parentId) == 1 &&
                !s.G.ResolutionStack.Any(f => f.Id == parentId),
            "Losing the passive after the actual two-entity payment cannot recompute printed twenty-four, repay, or suppress the recipient's native recovery."); s.Cold();
    }

    public static void ForeignHandMaximumAscendingAndHandComparisonUseEffectiveRankCold()
    {
        var s = RcScenario.Start("maximum"); s.Stock();
        var kings = s.Hand().Where(c => c.Rank == 13).Select(c => c.Id).ToArray();
        var three = s.Hand().Single(c => c.Rank == 3).Id;
        s.Give(kings[0], 1); s.Give(kings[1], 2); s.Take(2); s.Keep(three, kings[1]);
        var action = s.G.GetHumanLegalActions().Single(a => a.ProgramSkillId == "friend:biwei");
        s.Need(action.SelectableCardIds.SequenceEqual([three]), "The unique current Hand maximum is native three, rather than the foreign printed K now worth two.");
        s.Reject(new UseProgramSkillCommand(0, "friend:biwei", "discard-highest", [kings[1]], [1], s.G.Revision, s.P!.PromptId));
        s.Accept(new UseProgramSkillCommand(0, "friend:biwei", "discard-highest", [three], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.Hold("friend-paid")); s.Private();
        var maximum = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "friend:biwei");
        s.Need(maximum.FriendEventFlow!.Rank == 3 && maximum.FriendEventFlow.Stage == FriendEventFlowStage.MaximumCost,
            "The accepted maximum cost freezes its pre-departure value on its original program frame.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.View(1).HandCount == 1 && !s.G.CardMovements.Any(m => m.Reason.Value == "skill.friend-biwei.target") &&
                s.G.CardMovements.Count(m => m.CardId == three && m.From == CardLocation.Hand(0) && m.Reason.Value == "skill.friend-biwei.cost") == 1,
            "The target's own live Juewu also makes its foreign K two, so it does not meet the paid-three discard threshold."); s.Cold();

        s = RcScenario.Start("ascending"); s.Stock();
        kings = s.Hand().Where(c => c.Rank == 13).Select(c => c.Id).ToArray();
        s.Give(kings[0], 1); s.Take(1); s.Give(kings[1], 2); s.Keep(kings[0]);
        s.Use("damage", targets: [1]); s.Reach(() => s.Optional("friend:lunxiong")); s.Private(); s.Cold();
        s.Answer(s.P!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Reach(() => s.Hold("friend-paid")); s.Private();
        var ascending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "friend:lunxiong");
        var fact = s.Events<AscendingHandRankPaidEvent>().Single();
        s.Need(ascending.FriendEventFlow is { Rank: 2, Stage: FriendEventFlowStage.AscendingCost } &&
                fact.CardId == kings[0] && fact.Rank == 2 && s.G.CardMovements.Count(m => m.CardId == kings[0] &&
                    m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.CardKind == CardKind.Crossbow && m.Reason.Value == "skill.friend-lunxiong.cost") == 1,
            "The foreign printed K pays effective two before leaving Hand, and the permanent ascending-history fact freezes two rather than thirteen.");
        var ascId = ascending.Id; var drawCutoff = s.G.CardMovements.Max(m => m.Sequence); s.Cold(); s.Continue(); s.Play();
        s.Need(s.View().Skills?.Any(skill => skill.ContentId == RcJuewu) != true && s.Events<ProgramBindingResolvedEvent>().Count(e => e.FrameId == ascId && e.Activated && e.Completed) == 1 &&
                s.G.CardMovements.Count(m => m.Sequence > drawCutoff && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 3,
            "A paid damage-triggered three-card draw completes once after the real cost child removes the separate passive source.");
        s.Keep(); s.Use("heal"); s.Play(); s.Take(2);
        s.Need(s.View().Skills?.Any(skill => skill.ContentId == RcJuewu) == true && s.Hand().Single().Id == kings[1], "A real recovery reacquires Juewu and the second real foreign Hand entry remains effective two.");
        s.Use("damage", targets: [1]); s.Reach(() => s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } || s.Optional("friend:lunxiong"));
        s.Need(!s.Optional("friend:lunxiong") && s.Events<AscendingHandRankPaidEvent>().Count() == 1 &&
                s.G.CardMovements.Count(m => m.Reason.Value == "skill.friend-lunxiong.cost") == 1,
            "A second effective two cannot exceed the previously paid two, even when its printed K and the passive source instance are newly obtained."); s.Cold();

        s = RcScenario.Start("comparison"); s.Stock();
        var ownKing = s.Hand().First(c => c.Rank == 13).Id; var peerEleven = s.Hand().Single(c => c.Rank == 11).Id;
        s.Give(peerEleven, 1); s.Give(ownKing, 2); s.Take(2); s.Keep(ownKing);
        s.Accept(new UseProgramSkillCommand(0, "classic:zhongjian", "compare", [ownKing], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "hand-comparison")); s.Cold();
        s.Answer(s.P!.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "reveal"));
        var compared = s.Events<ProgramHandComparisonResolvedEvent>().Single();
        s.Need(compared.RankMatched && compared.SourceCardId == ownKing && compared.RevealedIds.SequenceEqual([peerEleven]) &&
                s.Hand().Any(c => c.Id == ownKing) && s.G.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == peerEleven),
            "Two still-owned foreign Hand entities compare as two versus two despite their distinct original K and J ranks, without moving either revealed entity.");
        s.Cold(); s.Answer(s.P!.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "draw")); s.Play();
        s.Need(s.Events<ProgramHandComparisonResolvedEvent>().Count() == 1 && s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:zhongjian"),
            "The real rank-match result grants the existing second comparison allowance, rather than a test-only definition shortcut."); s.Cold();
    }

    private sealed class RcScenario(string scene, ContentRegistry registry, GameEngine game)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot View(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public IReadOnlyList<CardSnapshot> Hand() => View().Hand;
        public IEnumerable<T> Events<T>() => G.Events.Select(e => e.Payload).OfType<T>();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == RcWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == id);
        public bool RankPrompt() => P is { PlayerSeat: 0, IsPrivate: true } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "owned-hand-rank-sum");
        public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException("scene=" + scene + " " + message + " " + JsonSerializer.Serialize(new { P, Hp = View().Hp, Skills = View().Skills, Frames = G.ResolutionStack, Paid = Events<AscendingHandRankPaidEvent>().ToArray() })); }
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Command rejected."); }
        public void Answer(PromptChoice choice) { Need(P!.PlayerSeat == 0, "Only the real human owner answers."); Accept(new AnswerPromptCommand(0, P.PromptId, choice.Id, G.Revision)); }
        public void Continue() => Answer(P!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, RcDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Stock() { Use("stock"); Play(); Need(Hand().Count == 8, "The small real eight-card deck is actually drawn, with no seed search."); }
        public void Give(int card, int seat) { Use("give", [card], [seat]); Play(); }
        public void Take(int seat) { Use("take", targets: [seat]); Play(); }
        public void Keep(params int[] keep) { var discard = Hand().Where(c => !keep.Contains(c.Id)).Select(c => c.Id).ToArray(); if (discard.Length > 0) { Use("discard", discard); Play(); } }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> condition)
        {
            for (var i = 0; i < 160; i++)
            {
                if (condition()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue();
                    else throw new InvalidOperationException("scene=" + scene + " Unexpected real human boundary " + JsonSerializer.Serialize(p));
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("scene=" + scene + " Bounded native boundary missing.");
        }
        public void Private() => Need(P is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null), "Private owner choices remain absent from all other player views.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack, G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restore keeps effective rank drafts, exact original entities, paid sources and private children unchanged."); }
        public void Reject(GameCommand command) { var before = State(); var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(!result.Accepted && result.Error is not null && before == State(), "An unpublished, stale or wrong-actor choice rejects without any rank, cost or quota mutation."); }
        public static RcScenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new RcFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = RcMode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
            var s = new RcScenario(scene, r, g); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
            s.Accept(new SelectGeneralCommand(0, RcOwner, s.G.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class RcFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu-rank-consumers", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static SkillProgramCatalog Catalog(string bundle)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                return SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            }
            void Add(SkillProgramCatalog c, string id) => b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            Add(Catalog("wumiao-guan-yu"), RcJuewu); Add(Catalog("ordinary-mi-zhu"), RcZiyuan);
            var friends = Catalog("friend-events"); foreach (var id in new[] { "friend:biwei", "friend:lunxiong" }) Add(friends, id);
            Add(Catalog("classic-xin-xianying"), "classic:zhongjian");
            static JsonNode N(string x) => JsonNode.Parse(x)!;
            static JsonNode Act(string id, string effects, int min = 0, string max = "0", int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + min + ",\"maxCards\":" + max + ",\"sourceZones\":[\"hand\"],\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            var acts = new JsonArray(
                Act("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":8}"),
                Act("give", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gift\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"gift\",\"destination\":\"selectedTargetHand\"}", 1, "1", 1),
                Act("take", "{\"op\":\"selectOwnedCards\",\"target\":\"selectedTarget\",\"numberExpression\":\"allOwnedZoneCards\",\"zones\":[\"hand\"],\"resultBind\":\"foreign\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"foreign\",\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                Act("discard", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"discard\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"discard\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}", 1, "null"),
                Act("hurt", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1),
                Act("heal", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":3}"),
                Act("damage", "{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":1}", targets: 1));
            var reasons = scene == "gift" ? new[] { RcGift } : new[] { "skill.friend-biwei.cost", "skill.friend-lunxiong.cost" };
            var loss = scene == "gift" ? 1 : scene == "ascending" ? 2 : 0;
            var watchEffects = new JsonArray(N("{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + (scene == "gift" ? "gift-paid" : "friend-paid") + "\",\"options\":[{\"id\":\"continue\"}]}"));
            if (loss > 0) watchEffects.Add(N("{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + loss + "}"));
            var defs = new JsonArray(new JsonObject { ["id"] = RcDriver, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"" + RcGrant + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"rank-two\",\"skillId\":\"wumiao:juewu\",\"minimumHp\":" + (scene == "gift" ? 13 : 11) + "}]}"),
                new JsonObject { ["id"] = RcWatch, ["revision"] = 1, ["triggers"] = new JsonArray(new JsonObject { ["id"] = "real-paid-hand", ["window"] = "cardsMoved", ["subject"] = "owner", ["sourceZones"] = new JsonArray("hand"), ["movementOccurrence"] = "perBatch", ["movementReasons"] = JsonSerializer.SerializeToNode(reasons), ["optional"] = false, ["effects"] = watchEffects }) });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new
            {
                name = n!["id"]!.GetValue<string>(), description = "Actual native effective Hand rank fixture",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray())
                    .SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray())
                    .Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id)
            });
            var fixtures = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in fixtures.Programs.Keys) Add(fixtures, id);
            var own = new[] { RcDriver, RcGrant, RcWatch, scene == "gift" ? RcZiyuan : scene == "maximum" ? "friend:biwei" : scene == "ascending" ? "friend:lunxiong" : "classic:zhongjian" };
            const string pick = "fixture:wjr-pick"; b.AddSkill(new(pick, "Owner", "Fixed real human candidate") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -10000000d) });
            b.AddGeneral(new(RcOwner, "Real effective Hand rank owner", "supporter", pick, "shu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:wjr-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "Other real Hand owner", "supporter", RcJuewu, "wei", 12));
            b.AddCard(new("fixture:wjr-crossbow", CardCatalog.Get(CardKind.Crossbow).DisplayName, CardCatalog.Get(CardKind.Crossbow).CategoryName, "Unmodified original printed entity", CardKind.Crossbow));
            b.AddDeck(new("fixture:wjr-deck", "Eight exact printed entities", 0, 0, []) { PhysicalCards = new[] { 13, 13, 11, 3, 7, 5, 4, 1 }.Select(rank => new ContentDeckPhysicalCard("fixture:wjr-crossbow", Suit.Heart, rank)).ToArray() });
            b.AddMode(new(RcMode, "Actual native Hand rank consumers", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:wjr-deck", GeneralCandidateCount: 4, GeneralPoolIds: [RcOwner, .. peers]));
        }
    }
}
