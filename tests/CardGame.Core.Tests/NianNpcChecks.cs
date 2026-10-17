using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NianNpcChecks
{
    private const string Driver = "fixture:nian-npc-driver", Quiet = "fixture:nian-npc-quiet", Mode = "fixture:nian-npc-mode";

    public static void BaseDrawRandomReplacementDormancyAndNativeSlash()
    {
        {
            var (g, registry) = Start(["nian:yingzi"], []);
            Require(g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 4,
                "Yingzi adds two actual cards to the normal two-card draw."); Cold(g, registry);
        }
        {
            var (g, registry) = Start(["nian:yingzi", "nian:baonu", "nian:shouyi"], []);
            var roll = E<NianBaonuDrawRolledEvent>(g).Single();
            Require(roll.HpAtRoll == 12 && roll.DrawCount is >= 4 and <= 12 &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawNianBaonuReplacement", StringComparison.Ordinal)) == roll.DrawCount &&
                !g.CardMovements.Any(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
                "Baonu rolls both inclusive endpoints once and replaces the whole normal draw, including Yingzi's addition.");
            Use(g, "lose-eight", 0); Play(g);
            Require(V(g, 0).Hp == 4, "The real native HP-loss command crosses Baonu's low-HP threshold.");
            var hp = V(g, 2).Hp; var first = Slash(g, 2); Play(g); var second = Slash(g, 2); Play(g);
            Require(first != second && V(g, 2).Hp == hp - 4 && E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) == 2 &&
                new[] { first, second }.All(id => g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing)),
                "Shouyi permits distance-two Slash targets; low-HP Baonu issues two native physical Slashes with two damage each and separate paid materials.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start(["nian:shouyi"], ["nian:dongmian"]); var boss = Peer(g);
            Use(g, "lose-six-target", boss); Play(g);
            var card = V(g, 0).Hand.First(c => c.Kind == CardKind.Slash).Id;
            Require(V(g, boss).Hp == 6 && !g.GetHumanLegalActions().Any(a => a.CardId == card && a.TargetSeats.Contains(boss)),
                "In actual round one, six-HP Dongmian removes the boss from real card targets.");
            var before = SnapshotJson.Serialize(g.CreateSnapshot(0));
            Require(!g.Submit(new PlayCardCommand(0, card, [boss], g.Revision, P(g)!.PromptId)).Accepted && SnapshotJson.Serialize(g.CreateSnapshot(0)) == before,
                "A forged dormant-boss card target rejects atomically."); Cold(g, registry);
            End(g); Play(g);
            Require(E<RoundStartedEvent>(g).Last().RoundNumber == 2 && g.GetHumanLegalActions().Any(a => a.TargetSeats.SequenceEqual([boss]) && a.Kind == LegalActionKind.Slash),
                "The same living six-HP target becomes legal after the full normal round, rather than after actual turn one."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["nian:dongmian"], card: "standard:arrow_barrage"); var boss = Peer(g);
            Use(g, "lose-six-target", boss); Play(g);
            var before = g.CreateSnapshot(0).Players.ToDictionary(p => p.Seat, p => p.Hp); var eventIndex = g.Events.Count;
            var arrow = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.ArrowBarrage);
            Accept(g, new PlayCardCommand(0, arrow.CardId!.Value, arrow.TargetSeats, g.Revision, P(g)!.PromptId, arrow.PlayedCardKind)); Play(g);
            var declared = E<CardUseDeclaredEvent>(g).Last(e => e.CardKind == CardKind.ArrowBarrage);
            var targets = E<TargetsConfirmedEvent>(g).Single(e => e.ResolutionId == declared.ResolutionId);
            Require(!targets.TargetSeats.Contains(boss) && V(g, boss).Hp == before[boss] &&
                Enumerable.Range(1, 3).Where(s => s != boss).All(s => V(g, s).Hp == before[s] - 1) &&
                !g.Events.Skip(eventIndex).Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.TargetSeat == boss),
                "A real paid mass attack omits the dormant boss but still issues and resolves normally against both other characters."); Cold(g, registry);
        }
        {
            var (g, registry) = Start(["nian:shouyi"], ["nian:jingjue"], card: "standard:snatch");
            var before = V(g, 2).HandCount; var moves = g.CardMovements.Count;
            var snatch = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([2]) && a.TargetCardId is null);
            Accept(g, new PlayCardCommand(0, snatch.CardId!.Value, snatch.TargetSeats, g.Revision, P(g)!.PromptId, snatch.PlayedCardKind));
            Until(g, () => P(g)?.Kind == DecisionKind.SelectTargetCard); Cold(g, registry);
            Require(P(g)!.Choices.All(c => c.Cards.Count == 0), "A distance-two native Snatch exposes only opaque foreign hand slots.");
            Answer(g, _ => true); Play(g);
            Require(V(g, 2).HandCount == before - 1 && g.CardMovements.Skip(moves).Any(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.SnatchFinished),
                "Shouyi's general card-distance rule permits and completes a real distance-two Snatch, beyond its separate Slash distance modifier."); Cold(g, registry);
            var alert = Peer(g); Use(g, "damage", alert); Play(g); var hp = V(g, alert).Hp;
            var receipts = E<NianJingjueLossRecoveredEvent>(g).Count(); var start = g.CardMovements.Count;
            snatch = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([alert]) && a.TargetCardId is null);
            Accept(g, new PlayCardCommand(0, snatch.CardId!.Value, snatch.TargetSeats, g.Revision, P(g)!.PromptId, snatch.PlayedCardKind));
            Until(g, () => P(g)?.Kind == DecisionKind.SelectTargetCard); Cold(g, registry); Answer(g, _ => true); Play(g);
            var transfer = g.CardMovements.Skip(start).Single(m => m.From == CardLocation.Hand(alert) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Snatch);
            Require(V(g, alert).Hp == hp + 1 && E<NianJingjueLossRecoveredEvent>(g).Count() == receipts + 1 &&
                g.CardMovements.Skip(start).Any(m => m.CardId == transfer.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.SnatchFinished),
                "Native Snatch's real card-use and TargetCardTaken invoices attribute its hidden two-stage hand transfer to the alert owner, healing that actual loss once."); Cold(g, registry);
        }
    }

    public static void RuizhiPrivateKeepsAndJingjueRealMixedLossBatches()
    {
        {
            var (g, registry) = Start([], ["nian:ruizhi"], beginPlay: false);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "nian:ruizhi");
            var boss = Peer(g);
            var hand = V(g, 0).Hand.Select(c => c.Id).ToArray(); var keep = hand[0];
            Require(P(g) is { PlayerSeat: 0, IsPrivate: true } p && p.Choices.All(c => c.Cards.Count == 1) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "The affected character privately chooses its mandatory kept hand entity, without exposing cards or an optional decline to the boss.");
            Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([keep])); Play(g);
            Require(hand.Where(id => id != keep).All(id => g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "program.nian.ruizhi.discard")) &&
                E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e => e.InitiatorSeat == boss && e.ExecutorSeat == 0 && e.SubjectSeat == 0 && e.ActualCount == hand.Length - 1),
                "Ruizhi discards the remaining original hand as one native attributed instruction before the ordinary draw.");
            Use(g, "equip", 0); Play(g); var equipment = V(g, 0).Equipment.Single().Id;
            End(g); Until(g, () => P(g)?.SkillPrompt?.SkillId == "nian:ruizhi" && P(g)?.PlayerSeat == 0);
            var retained = V(g, 0).Hand[0].Id; Answer(g, c => c.Cards.SequenceEqual([retained]));
            Require(P(g) is { PlayerSeat: 0, IsPrivate: true } equipmentPrompt && equipmentPrompt.Choices is [var sole] && sole.Cards.SequenceEqual([equipment]),
                "An existing equipment zone requires its separate exact kept-equipment choice, even when it holds only one card.");
            Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([equipment])); Play(g);
            Require(V(g, 0).Equipment.Single().Id == equipment && V(g, 0).Hand.Any(c => c.Id == retained), "The two privately chosen originals stay in their own zones."); Cold(g, registry);
        }
        {
            var (g, registry) = Start(["nian:jingjue"], []);
            Use(g, "damage", 0); Play(g); Use(g, "equip", 0); Play(g);
            var hp = V(g, 0).Hp; var equipment = V(g, 0).Equipment.Single().Id; var hand = V(g, 0).Hand[0].Id;
            var recoveryCount = E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0);
            Use(g, "discard-mixed", 0); Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
            Answer(g, c => c.Cards.SequenceEqual([equipment])); Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([hand])); Play(g);
            Require(V(g, 0).Hp == hp + 1 && E<NianJingjueLossRecoveredEvent>(g).Count() == 1 &&
                E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0) == recoveryCount + 1 &&
                E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e => e.SubjectSeat == 0 && e.ActualCount == 2 && e.HandCount == 1),
                "A real mixed equipment/hand discard batch heals once despite two source zones.");
            Use(g, "give", 1); Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
            var gift = V(g, 0).Hand[0].Id; Answer(g, c => c.Cards.SequenceEqual([gift])); Play(g);
            Require(V(g, 0).Hp == hp + 2 && V(g, 1).Hand.Any(c => c.Id == gift) && E<NianJingjueLossRecoveredEvent>(g).Count() == 2,
                "Another character really acquiring an owned hand card forms a separate healing batch.");
            Use(g, "damage", 0); Play(g); var recipientHand = V(g, 1).HandCount; var giftHp = V(g, 0).Hp;
            Use(g, "give-two", 1); Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "owned-hand-rank-sum") == true);
            Answer(g, c => c.Cards.Count == 1); Cold(g, registry); Answer(g, c => c.Cards.Count == 1);
            Require(P(g)!.Choices is [var finish] && finish.Cards.Count == 0 && finish.Id.Value.Contains("finish-2", StringComparison.Ordinal),
                "The exact two-card rank sum still requires its real final confirmation before moving either gift entity.");
            Cold(g, registry); Answer(g, c => c.Cards.Count == 0); Play(g);
            Require(V(g, 1).HandCount == recipientHand + 2 && V(g, 0).Hp == giftHp + 1 && E<NianJingjueLossRecoveredEvent>(g).Count() == 3,
                "A genuine two-entity awaited gift pays both original hand cards as one loss batch and heals once, rather than per card.");
            Use(g, "damage", 0); Play(g); hp = V(g, 0).Hp; Slash(g, 1); Play(g);
            Require(V(g, 0).Hp == hp && E<NianJingjueLossRecoveredEvent>(g).Count() == 3,
                "Using and finishing an actual card does not become Jingjue's discard or other-character acquisition."); Cold(g, registry);
        }
    }

    public static void ActualSkipLedgerAndOutsideTurnPerPointDamageRecovery()
    {
        {
            var (g, registry) = Start(["nian:mengtai"], [], beginPlay: false, skipBoth: true);
            Until(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0));
            Require(E<NianActualPhaseSkippedEvent>(g).Where(e => e.OwnerSeat == 0 && e.ActualTurnNumber == 1).Select(e => e.Phase).Order().SequenceEqual(new[] { SkillProgramTurnPhase.Draw, SkillProgramTurnPhase.Play }.Order()) &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawNianMengtaiEnding", StringComparison.Ordinal)) == 3 &&
                !g.CardMovements.Any(m => m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard),
                "Two truly skipped phases in the same actual turn award the three ending cards and skip real discard, rather than reading a future schedule."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["nian:renxing"]); var boss = Peer(g); var hand = V(g, boss).HandCount;
            Use(g, "damage", 2); Play(g);
            Require(V(g, boss).HandCount == hand + 2, "One native two-point damage to another participant awards Renxing two real cards outside its actual turn.");
            hand = V(g, boss).HandCount; Use(g, "recover", 2); Play(g);
            Require(V(g, boss).HandCount == hand + 2 && E<RecoveryAppliedEvent>(g).Last().Amount == 2,
                "One real two-HP foreign recovery awards exactly two cards, without duplicating owner-only and observer candidates.");
            hand = V(g, boss).HandCount; Use(g, "source-less"); Play(g);
            Require(V(g, boss).HandCount == hand + 1 && E<DamageAppliedEvent>(g).Last() is { SourceLess: true, TargetSeat: 0, Amount: 1 },
                "Source-less native damage still awards its single Renxing point; HP loss is a separate rule."); Cold(g, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(string[] ownerSkills, string[] peerSkills, bool beginPlay = true, bool skipBoth = false, string card = "standard:slash")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardNianActivityPackage(), new Fixture(ownerSkills, peerSkills, skipBoth, card));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:blue", ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:nian-npc-actor", g.Revision, P(g)!.PromptId));
        if (beginPlay) Play(g); return (g, registry);
    }
    private static void Use(GameEngine g, string id, int? target = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    private static int Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)); return a.CardId.Value;
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static int Peer(GameEngine g) => g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:nian-npc-peer-1").Seat;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Until(GameEngine g, Func<bool> condition)
    {
        for (var i = 0; i < 260; i++)
        {
            if (condition()) return;
            if (P(g) is { PlayerSeat: 0 } human && human.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Nian NPC fixture missed its real boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(restored.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements), "Nian native movements and all viewer-specific private choices reproduce from the accepted command journal.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Nian command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string[] ownerSkills, string[] peerSkills, bool skipBoth, string card) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("nian-npc-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var growth = "{\"id\":\"native-health-fixture\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"changeMaximumHp\",\"target\":\"owner\",\"amount\":8},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":8}]}";
            var skipTriggers = skipBoth ? ",\"triggers\":[{\"id\":\"skip-draw\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"draw\"]}]},{\"id\":\"skip-play\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]" : ",\"triggers\":[" + growth + "]";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"lose-eight","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":8}]},
                {"id":"lose-six-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":6}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"discard-mixed","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","amount":2,"zones":["hand","equipment"],"resultBind":"mixed"},{"op":"moveBoundCards","target":"owner","sourceBind":"mixed","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"give","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true}]},
                {"id":"give-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedHandRankSum","target":"owner","exactRankSum":14,"resultBind":"two-gift"},{"op":"moveBoundCards","target":"owner","sourceBind":"two-gift","destination":"selectedTargetHand","awaitMovementTriggers":true}]}
              ]{{{skipTriggers}}}},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{{{growth}}}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "Small real Nian rule fixture." }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Small real Nian rule fixture.") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new("fixture:nian-npc-actor", "Nian rule actor", "", Driver, "qun", BaseHp: 12, AdditionalSkillIds: ownerSkills));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:nian-npc-peer-{i}", $"Nian rule peer {i}", "", Quiet, "qun", BaseHp: 12, AdditionalSkillIds: i == 1 ? peerSkills : []));
            b.AddDeck(new("fixture:nian-npc-deck", "Real Slash materials and equipment", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(i < 160 ? card : "standard:qinggang_sword", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "Actual Nian NPC fixture", 4, 4, new Dictionary<string, int>(), "fixture:nian-npc-deck", 4,
                ["fixture:nian-npc-actor", "fixture:nian-npc-peer-1", "fixture:nian-npc-peer-2", "fixture:nian-npc-peer-3"], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:blue"] = 2, ["team:red"] = 2 }));
        }
    }
}
