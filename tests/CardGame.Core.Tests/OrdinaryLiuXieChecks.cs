using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

// Real-command drafts. No compiler, loader, game or check was run for this delivery.
internal static class OrdinaryLiuXieChecks
{
    private const string Tianming = "ol:tianming", Mizhao = "ol:mizhao", Driver = "fixture:lx-driver";
    private const string Hp = "fixture:lx-hp", Cost = "fixture:lx-cost", Gain = "fixture:lx-gain", Gift = "fixture:lx-gift", Done = "fixture:lx-done", Dying = "fixture:lx-dying";
    private const string GiftSuppression = "fixture:lx-noop", TargetSuppression = "fixture:lx-target-suppression";
    private const string Mode = "identity:classic-liu-xie-fixture";
    private const string CostReason = "skill-program." + Tianming + ".DiscardDrawAndOfferUniqueHpPeer";

    public static void TianmingSilverLionPaymentAndUniqueCurrentHpPeerOwnColdChildren()
    {
        var (g, r) = Create(equipment: true); Play(g); Use(g, "equip", [0]); Play(g);
        var armor = g.CreateCardZoneDiagnostics().Single(c => c.Location == CardLocation.Equipment(0)).CardId;
        Require(V(g, 0).Hp == 2, "The classic identity Lord starts at actual 2 HP before the real injured SilverLion payment.");
        Incoming(g); Activate(g, Tianming); Reach(g, p => Branch(p, "card"));
        var root = PeerRoot(g); var original = root.UniqueHpPeer!;
        Require(original.RequiredCount == 2 && original.TargetIdentity.TargetSeat == 0 && original.CardActionId is not null,
            "The real foreign actual Slash target freezes its exact owner, current use/action and two actual HE cost cards.");
        Private(g); Reject(g); g = ColdRestore(g, r); Answer(g, c => c.Cards.SequenceEqual([armor]));
        Reach(g, p => Branch(p, "card")); Answer(g, c => c.Cards.Count == 1);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var paid = PeerRoot(g).UniqueHpPeer!.OwnerInvoice!; var id = PeerRoot(g).Id;
        Require(paid.CardIds.Count == 2 && paid.SourceLocations.Contains(CardLocation.Equipment(0)) && !paid.DrawIssued &&
            V(g, 0).Hp == 3 && F<UniqueHpPeerCostPaidEvent>(g).Count(e => e.FrameId == id) == 1 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.ResumeFrameId == id && h.Change.ParentFrameId == id && h.Continuation == PostEventContinuation.AwaitedProgramMovement),
            "The genuine equipment loss recovers first and pauses under the exact already-paid parent, before any Draw2 invoice.");
        Freeze(paid.CardIds); Freeze(paid.SourceLocations); g = ColdRestore(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == id &&
            (w.ResumeProgramFrameId == id || w.ResumeProgramFrameId is null && w.Batch.AwaitingProgramFrameId == id));
        Require(movement.Batch.ParentFrameId == id && (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == id) &&
            movement.Batch.Movements.Count == 2 && movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(paid.CardIds.Order()) &&
            movement.Batch.Movements.All(m => m.Reason.Value == CostReason && g.CardMovements.Contains(m)),
            "Both actual HE entities form one atomic cost batch; no HP or movement child pays them again.");
        g = ColdRestore(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0);
        Require(PeerRoot(g).UniqueHpPeer!.OwnerInvoice is { DrawIssued: true, ActualDrawCount: 2 } && !F<UniqueHpPeerOfferedEvent>(g).Any(),
            "Both real per-card draws are issued once, but current unique maximum HP is not queried while gain children remain.");
        g = ColdRestore(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Tianming && Branch(p, "accept"));
        var peer = Seat(g, "fixture:lx-target-1");
        Require(P(g)!.PlayerSeat == peer && F<UniqueHpPeerOfferedEvent>(g).Single().PeerSeat == peer &&
            F<UniqueHpPeerOfferedEvent>(g).Single().Hp == V(g, peer).Hp, "Only the unique current maximum HP living peer receives its own optional same-action choice after all owner children.");
        Private(g); g = ColdRestore(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision)); Play(g);
        Require(F<UniqueHpPeerCostPaidEvent>(g).Count(e => e.FrameId == id && e.ActorSeat == 0) == 1 &&
            F<UniqueHpPeerDrawIssuedEvent>(g).Count(e => e.FrameId == id && e.ActorSeat == 0) == 1 &&
            F<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == original.CardActionId) == 1 &&
            g.CardMovements.Count(m => m.CardId == armor && m.Reason.Value == CostReason) == 1,
            "The original Slash continues after the native peer choice with its real accepted action, exact payment and owner Draw2 retained once.");
        ColdRestore(g, r);
    }

    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool tied = false, bool multi = false, bool legacy = false, bool tie = false, bool gainDying = false, bool removeGiftSource = false, bool removeTargetSource = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(equipment, tied, multi, legacy, tie, gainDying, removeGiftSource, removeTargetSource));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Require(P(g)!.ValidContentIds.Contains("fixture:lx-owner"), "The fixed published setup retains its actual owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:lx-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void Incoming(GameEngine g)
    {
        Use(g, "incoming", [1]); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-", StringComparison.Ordinal) == true)); Answer(g, c => c.Targets.SequenceEqual([0]));
    }
    private static ProgramSkillFrame PeerRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.UniqueHpPeer is not null);
    private static int Seat(GameEngine g, string id) => g.CreateSnapshot(0).Players.Single(p => p.GeneralId == id).Seat;
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Has(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static bool Branch(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == id);
    private static void Activate(GameEngine g, string id) { Reach(g, p => p.SkillPrompt?.SkillId == id && Has(p, "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> test) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(test).Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (P(g)!.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> test)
    {
        for (var i = 0; i < 240; i++)
        {
            if (P(g) is { } p && test(p)) return;
            if (P(g) is { PlayerSeat: 0 } human)
            {
                if (Has(human, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
                else if (human.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(0, human.ValidCardIds.Take(human.RequiredCardCount).ToArray(), human.PromptId, g.Revision));
                else throw new InvalidOperationException("Unexpected real human boundary: " + JsonSerializer.Serialize(human));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed Liu Xie boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static GameEngine ColdRestore(GameEngine g, ContentRegistry r)
    {
        static string Prefix(GameEngine e) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(e.CreateSnapshot(s))).ToArray(),
            Frames = JsonSerializer.Serialize(e.ResolutionStack), Facts = e.Events.Select(x => JsonSerializer.Serialize(x.Payload, x.Payload.GetType())).ToArray(),
            e.CardMovements, Commands = CommandJson.Serialize(e.AcceptedCommands), Zones = e.CreateCardZoneDiagnostics() });
        var expected = Prefix(g);
        var rebuilt = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(Prefix(rebuilt) == expected, "All four accepted-journal cold views, owning frames, private data, real payments/facts and commands match the genuine paused prefix.");
        return rebuilt;
    }
    private static void Private(GameEngine g)
    {
        var p = P(g)!; if (!p.IsPrivate) return;
        for (var s = 0; s < 4; s++) if (s != p.PlayerSeat) Require(g.CreateSnapshot(s).PendingDecision is not { } other || other.Choices.All(c => c.Cards.Count == 0), "Foreign private entity choices stay absent or opaque.");
    }
    private static void Reject(GameEngine g) { var p = P(g)!; var n = g.Revision; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("wrong-real-choice"), n)).Accepted && n == g.Revision, "A rejected private answer does not pay or mutate the published prefix."); }
    private static void Freeze<T>(IReadOnlyList<T> list) { if (list is IList<T> mutable) { try { mutable[0] = list[0]; throw new InvalidOperationException("Mutable prepared collection."); } catch (NotSupportedException) { } } }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool equipment, bool tied, bool multi, bool legacy, bool tie, bool gainDying, bool removeGiftSource, bool removeTargetSource) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:ordinary-liu-xie", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:lx-driver","revision":1,"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
                {"id":"clear","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand","equipment"],"numberExpression":"allOwnedZoneCards","resultBind":"all-he"},{"op":"moveBoundCards","target":"owner","sourceBind":"all-he","destination":"discardPile"},{"op":"awaitBoundCardMovements","target":"owner"}]},
                {"id":"draw-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"fixture:lx-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:lx-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:lx-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.ol:tianming.DiscardDrawAndOfferUniqueHpPeer"],"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:lx-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:tianming.unique-hp-peer.draw.0"],"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:lx-gift","revision":1,"triggers":[{"id":"gift","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:mizhao.GiveAllHandAndStartRecipientPindian"],"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:lx-done","revision":1,"triggers":[{"id":"done","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:lx-dying","revision":1,"triggers":[{"id":"dying","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:lx-gift-source-loss","revision":1,"triggers":[{"id":"loss","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:mizhao.GiveAllHandAndStartRecipientPindian"],"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:lx-noop"]}]}]},
              {"id":"fixture:lx-multi","revision":1,"modifiers":[{"id":"two","query":"cardTargetCount","operation":"add","value":1,"priority":0,"cardKinds":["slash"]}]},
              {"id":"fixture:lx-legacy","revision":1,"triggers":[{"id":"legacy","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"skipTurnPhases","target":"owner","phases":["judgment","draw"]},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (gainDying) ((JsonArray)rules["skills"]![5]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":6}"""));
            if (removeTargetSource) ((JsonArray)rules["skills"]![3]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:lx-target-suppression"]}"""));
            var names = rules["skills"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();
            var presentation = names.ToDictionary(id => id, id => id is Driver or "fixture:lx-quiet" or "fixture:lx-multi" or "fixture:lx-legacy"
                ? (object)new { name = id, description = "Real fixed commands and mature policies" }
                : new { name = id, description = "Real child pause", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var id in names) b.AddSkill(new(id, id, "Real command fixture") { Program = catalog.Programs[id] });
            b.AddSkill(new(GiftSuppression, "已付赠牌来源资格抑制", "真实Lord的2HP使原赠牌来源失去资格") { SuppressionRule = new(2), Tags = SkillTag.Locked });
            b.AddSkill(new(TargetSuppression, "已付目标来源资格抑制", "真实白银狮子回复至3HP后使原目标来源失去资格") { SuppressionRule = new(3), Tags = SkillTag.Locked });
            b.AddSkill(new("fixture:lx-selection", "Fixed published setup", "Passive selection preference") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var owner = new List<string> { Mizhao, Driver, Hp, Cost, Gain };
            if (legacy) owner.Add("fixture:lx-legacy"); else owner.Add(Tianming);
            if (multi) owner.Add("fixture:lx-multi"); if (removeGiftSource) owner.Add("fixture:lx-gift-source-loss");
            b.AddGeneral(new("fixture:lx-owner", "刘协共享机制", "supporter", owner[0], "qun", 3, owner.Skip(1).ToArray(), Gender: GeneralGender.Male) { InitialHp = 1 });
            for (var i = 1; i < 4; i++)
            {
                var skills = new List<string> { "fixture:lx-quiet", Gift, Done, Dying };
                if (multi || legacy) skills.Add(Tianming); if (i == 1 && !tie) skills.Add("classic:tianbian");
                b.AddGeneral(new($"fixture:lx-target-{i}", "Fixed participant", "supporter", "fixture:lx-selection", "qun", 6, skills, Gender: GeneralGender.Male) { InitialHp = tied || i == 1 ? 5 : 4 });
            }
            b.AddDeck(new("fixture:lx-deck", "Fixed physical materials", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ =>
                new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : multi || legacy ? "standard:slash" : "standard:peach", equipment || multi || legacy ? Suit.Spade : Suit.Heart, 1)).ToArray() });
            b.AddMode(new(Mode, "Ordinary Liu Xie real-command draft", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lx-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:lx-owner", "fixture:lx-target-1", "fixture:lx-target-2", "fixture:lx-target-3"]));
        }
    }
}
