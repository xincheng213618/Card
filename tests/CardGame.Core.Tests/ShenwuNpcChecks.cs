using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class ShenwuNpcChecks
{
    private const string Driver = "fixture:shenwu-npc-driver", Quiet = "fixture:shenwu-npc-quiet", Mode = "fixture:shenwu-npc-mode";
    private const string NonLockedReward = "fixture:shenwu-npc-nonlocked-reward";

    public static void NativeDamagePoliciesReplacementDrawAndTurnover()
    {
        {
            var (g, registry) = Start(["shenwu:xiongshou", "shenwu:wuzang"], []);
            Require(V(g, 0).HandCount == 8 && V(g, 0).HandLimit == 0 &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawShenwuWuzangReplacement", StringComparison.Ordinal)) == 5 &&
                !g.CardMovements.Any(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
                "Wuzang replaces normal drawing with five real cards and sets the hand limit to zero.");
            Use(g, "turn-over", 0); Play(g);
            Require(!V(g, 0).IsFaceDown && !E<CharacterStateChangedEvent>(g).Any(e => e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver), "Xiongshou rejects the native turnover before a face mutation.");
            Use(g, "one-damage", 1); Play(g);
            Slash(g, 1); Play(g);
            Require(V(g, 1).Hp == 1 && E<DamageAppliedEvent>(g).Last() is { SourceSeat: 0, TargetSeat: 1, Amount: 2 },
                "An actual Slash receives the beast bonus against the now lower-HP target.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["shenwu:xiangde", "shenwu:nitai"]);
            Use(g, "equip", 0); Play(g);
            Require(V(g, 0).Equipment.Single().Kind == CardKind.QinggangSword, "The damage source has a real weapon, rather than a synthetic modifier.");
            Use(g, "fire", 1); Play(g);
            Require(V(g, 1).Hp == 1 && E<DamageAppliedEvent>(g).Last() is { Nature: DamageNature.Fire, Amount: 3 },
                "Xiangde and foreign-turn Nitai both add to native fire damage from an armed source.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start(["shenwu:nitai", "shenwu:cangmu", "shenwu:tanyu"], []);
            Require(g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawShenwuCangmuReplacement", StringComparison.Ordinal)) == 4,
                "Cangmu draws the four living characters' count through the replacement path.");
            Use(g, "damage", 0); Play(g);
            Require(V(g, 0).Hp == 4 && E<ProgramDamagePreventedEvent>(g).Single().SkillId == "shenwu:nitai", "Nitai prevents the whole own-turn damage instance.");
            var hand = V(g, 0).HandCount;
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0));
            Require(V(g, 0).HandCount == hand && V(g, 0).Hp == 3 &&
                E<ProgramSkillHpLostEvent>(g).Single(e => e.SkillId == "shenwu:tanyu").Amount == 1 &&
                !g.CardMovements.Any(m => m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard),
                "Tanyu skips actual discard and loses one HP when its hand is the greatest.");
            Cold(g, registry);
        }
    }

    public static void DamageSourceAndRecoveryParticipantsDrawExactlyOnce()
    {
        {
            var (g, registry) = Start(["shenwu:zhue", "shenwu:jicai"], []);
            var own = V(g, 0).HandCount; var other = V(g, 1).HandCount;
            Use(g, "foreign-damage", 1); Play(g);
            Require(V(g, 0).Hp == 2 && V(g, 0).HandCount == own + 1 && V(g, 1).HandCount == other + 1,
                "Zhue awards one actual card to the observer and the real other damage source.");
            own = V(g, 0).HandCount;
            Use(g, "recover", 0); Play(g);
            Require(V(g, 0).Hp == 4 && V(g, 0).HandCount == own + 2 &&
                E<RecoveryAppliedEvent>(g).Last() is { TargetSeat: 0, Amount: 2 },
                "One two-HP self recovery gives Jicai exactly two cards, without multiplying by HP or duplicating its observer.");
            Use(g, "damage", 1); Play(g);
            own = V(g, 0).HandCount; other = V(g, 1).HandCount;
            Use(g, "recover", 1); Play(g);
            Require(V(g, 0).HandCount == own + 1 && V(g, 1).HandCount == other + 1,
                "A foreign recovery gives one real card to each distinct participant.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start(["shenwu:yinzei"], []);
            while (V(g, 0).HandCount != 0)
            {
                Accept(g, new UseProgramSkillCommand(0, Driver, "discard", [V(g, 0).Hand[0].Id], [], g.Revision, P(g)!.PromptId)); Play(g);
            }
            var other = V(g, 1).HandCount; var before = g.CardMovements.Count;
            Use(g, "foreign-one-damage", 1); Play(g);
            var receipt = E<ShenwuYinzeiDiscardedEvent>(g).Single();
            Require(V(g, 1).HandCount == other - 1 && receipt is { OwnerSeat: 0, SourceSeat: 1 } &&
                g.CardMovements.Skip(before).Count(m => m.CardId == receipt.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 1,
                "Empty-handed Yinzei makes exactly one real random discard from the actual source's owned cards.");
            Cold(g, registry);
        }
    }

    public static void MingwanRestrictsLaterUsesAndYanduUsesActualEndedTurn()
    {
        {
            var (g, registry) = Start(["shenwu:mingwan"], [], "standard:peach");
            var hand = V(g, 0).HandCount;
            var slash = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Accept(g, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, g.Revision, P(g)!.PromptId, slash.PlayedCardKind, slash.TargetCardId) { ConversionSource = slash.ConversionSource }); Play(g);
            Require(V(g, 1).Hp == 3 && V(g, 0).HandCount == hand - 1 && E<ShenwuMingwanVictimRecordedEvent>(g).Single() is { OwnerSeat: 0, VictimSeat: 1 },
                "Mingwan records a real virtual-card victim, and the first damaging card earns no retrospective draw.");
            var dismantle = g.GetHumanLegalActions().First(a => a.ConversionSource is { SkillId: Driver, BindingId: "fixed-dismantlement" } && a.TargetSeats.SequenceEqual([1]));
            var rejected = g.Submit(new PlayCardCommand(0, dismantle.CardId!.Value, [2], g.Revision, P(g)!.PromptId, CardKind.Dismantlement) { ConversionSource = dismantle.ConversionSource });
            Require(!rejected.Accepted, "After qualification a later actual card cannot designate an unrecorded target.");
            Use(g, "one-damage", 0); Play(g);
            var peach = g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Peach));
            Accept(g, new PlayCardCommand(0, peach.CardId!.Value, peach.TargetSeats, g.Revision, P(g)!.PromptId, peach.PlayedCardKind, peach.TargetCardId)); Play(g);
            Require(V(g, 0).Hp == 4 && V(g, 0).HandCount == hand - 1 &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawShenwuMingwanUse", StringComparison.Ordinal)) == 1,
                "A subsequent real Peach remains legal on self, pays its entity once and earns exactly one Mingwan card.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start(["shenwu:yandu"], []);
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "shenwu:yandu" && P(g)!.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
            Require(E<TurnEndedEvent>(g).Last() is { ActorSeat: 1 } && !E<DamageAppliedEvent>(g).Any() &&
                P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0 &&
                    c.Parameters.GetValueOrDefault("card-owner-seat") == "1" && c.Parameters.GetValueOrDefault("source-zone") == "Hand"),
                "Yandu waits until the real other actual turn completed without dealing damage.");
            Cold(g, registry);
            var choice = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
            var movementStart = g.CardMovements.Count; var ownerHand = V(g, 0).HandCount; var peerHand = V(g, 1).HandCount;
            Answer(g, c => c.Id == choice.Id);
            Until(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == "shenwu:yandu" && e.Completed));
            var acquired = g.CardMovements.Skip(movementStart).Where(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)).ToArray();
            Require(acquired.Length == 1 && V(g, 0).Hand.Any(c => c.Id == acquired[0].CardId) &&
                V(g, 0).HandCount == ownerHand + 1 && V(g, 1).HandCount == peerHand - 1,
                "Yandu's opaque owned-hand slot obtains exactly one real entity, which becomes visible only after entering its new owner's hand.");
            Cold(g, registry);
        }
    }

    public static void LuanchangOwnStartAndActualEndKeepNativeAoeParents()
    {
        var (g, registry) = Start(["shenwu:luanchang", "shenwu:mingwan"], [], beginPlay: false);
        Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.VirtualOrdinaryTrickOrigin is not null));
        var start = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.VirtualOrdinaryTrickOrigin is not null);
        Require(start.CardKind == CardKind.BarbarianAssault && start.CardId == 0 &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WindowContext?.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),
            "Luanchang's material-free Nanman is a real native child of its actual turn-start binding.");
        Cold(g, registry); Play(g);
        Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == start.Id) == 1 && E<GroupResponseEvent>(g).Count(e => e.ResolutionId == start.Id && e.UsedResponse) == 3,
            "The turn-start assault consumes the three real Slash responses and returns once.");
        Require(!E<ShenwuMingwanVictimRecordedEvent>(g).Any(), "The answered assault causes no damage and cannot arm Mingwan.");
        foreach (var peer in new[] { 1, 2, 3 }) { Use(g, "recover", peer); Play(g); }
        Slash(g, 1); Play(g);
        Require(E<ShenwuMingwanVictimRecordedEvent>(g).Single().VictimSeat == 1, "An actual play-phase Slash arms Mingwan before the turn ends.");
        var damageBeforeEnd = E<DamageAppliedEvent>(g).Count();
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.VirtualOrdinaryTrickOrigin is not null));
        var end = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.VirtualOrdinaryTrickOrigin is not null);
        Require(end.CardKind == CardKind.ArrowBarrage && end.CardId == 0 && end.TargetSeats.SequenceEqual([1, 2, 3]) && E<TurnEndedEvent>(g).Last().ActorSeat == 0 &&
            g.ResolutionStack.OfType<DeferredTurnEndFrame>().Any(f => f.OwnerSeat == 0) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WindowContext?.Window == SkillProgramTriggerWindow.AfterTurnEnded),
            "Luanchang's native arrows are issued after actual turn completion, while retaining the deferred-turn parent.");
        Cold(g, registry);
        Until(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == end.Id));
        Require(E<DamageAppliedEvent>(g).Skip(damageBeforeEnd).Count(e => e.SourceSeat == 0 && e.Amount == 1) == 3 &&
            !g.CardMovements.Any(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawShenwuMingwanUse", StringComparison.Ordinal)),
            "After the actual TurnEnded, Mingwan no longer restricts the arrows' three recipients or draws for the outside-turn use.");
        Cold(g, registry);
    }

    public static void FutaiPeachPolicyAndNativeCoalitionDeathBenefits()
    {
        {
            var (g, registry) = Start([], ["shenwu:futai"], "standard:peach");
            Use(g, "one-damage", 0); Play(g);
            var peach = V(g, 0).Hand.First(c => c.Kind == CardKind.Peach);
            Require(g.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Peach) &&
                !g.Submit(new PlayCardCommand(0, peach.Id, [0], g.Revision, P(g)!.PromptId)).Accepted && V(g, 0).Hp == 3,
                "An actual other Futai owner outside its own turn prohibits the wounded actor's physical Peach.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start(["shenwu:futai"], [], "standard:peach");
            Use(g, "one-damage", 0); Play(g); Use(g, "one-damage", 1); Play(g);
            Require(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Peach), "Futai permits other characters' Peaches during its owner's actual turn.");
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => E<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0) == 2);
            Require(V(g, 0).Hp == 4 && V(g, 1).Hp == 4 && E<TurnStartedEvent>(g).Last().ActorSeat == 0,
                "The next own real turn-start recovers all wounded living characters through native recovery events.");
            Cold(g, registry);
        }
        foreach (var gods in new[] { false, true })
        {
            var (g, registry) = Start(["shenwu:coalition"], [], godPeers: gods);
            var ally = g.CreateSnapshot(0).Players.Single(p => p.Seat != 0 && p.TeamId == V(g, 0).TeamId).Seat;
            Use(g, "damage", 0); Play(g); var hand = V(g, 0).HandCount;
            Use(g, "kill", ally); Play(g);
            Require(!V(g, ally).IsAlive && V(g, 0).Hp == 3 && V(g, 0).HandCount == hand + (gods ? 3 : 1),
                $"An actual {(gods ? "god" : "ordinary")} ally death recovers one HP and draws the correct surviving-player reward.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start(["shenwu:coalition"], [], minionPeer: true);
            var minion = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "shenwu:zhuyin-left").Seat;
            Use(g, "damage", 0); Play(g); var hand = V(g, 0).HandCount;
            Use(g, "kill", minion); Play(g);
            Require(!V(g, minion).IsAlive && V(g, 0).Hp == 3 && V(g, 0).HandCount == hand + 3 &&
                E<PlayerDiedEvent>(g).Last().KillerSeat == 0,
                "The actual killer of a real Zhuyin receives one native recovery and three real cards while other enemies remain alive.");
            Cold(g, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(string[] ownerSkills, string[] peerSkills, string card = "standard:slash", bool beginPlay = true, bool godPeers = false, bool minionPeer = false, bool expanded = false, Suit suit = Suit.Heart)
    {
        var packages = new List<IGameContentPackage> { new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShenwuActivityPackage() };
        if (expanded) packages.Add(new StandardShenwu2020ActivityPackage());
        packages.Add(new Fixture(ownerSkills, peerSkills, card, godPeers, minionPeer, suit));
        var registry = ContentRegistry.Build(packages.ToArray());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:blue", ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:shenwu-npc-actor", g.Revision, P(g)!.PromptId));
        if (beginPlay) Play(g);
        return (g, registry);
    }
    private static void Use(GameEngine g, string id, int target) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [target], g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId));
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Until(GameEngine g, Func<bool> condition)
    {
        for (var i = 0; i < 220; i++)
        {
            if (condition()) return;
            if (P(g) is { PlayerSeat: 0 } human && human.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shenwu NPC fixture missed its native boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(restored.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements), "NPC rule choices, events and native movements replay from accepted commands.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "NPC command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string[] ownerSkills, string[] peerSkills, string card, bool godPeers, bool minionPeer, Suit suit) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shenwu-npc-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var woundedPrelude = ownerSkills.Contains("shenwu:luanchang")
                ? ",{\"id\":\"wounded-native-response-fixture\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}]}"
                : "";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"viewAs":[{"id":"real-material-slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"fixed-dismantlement","inputKinds":[],"inputSuits":[],"outputKind":"dismantlement","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"one-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"foreign-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2,"sourceRef":{"kind":"selectedTarget"} }]},
                {"id":"foreign-one-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"sourceRef":{"kind":"selectedTarget"} }]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]},
                {"id":"turn-over","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"discard","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}{{{woundedPrelude}}}]},
              {"id":"{{{NonLockedReward}}}","revision":1,"triggers":[{"id":"after-damage-draw","window":"afterDamageApplied","subject":"owner","optional":false,"damageOccurrence":"perDamage","effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, NonLockedReward }.ToDictionary(id => id, id => new { name = id, description = "Native Shenwu NPC rule fixture." }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Native Shenwu NPC rule fixture.") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new("fixture:shenwu-npc-actor", "NPC rule actor", "", Driver, AdditionalSkillIds: ownerSkills));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:shenwu-npc-peer-{i}", $"NPC rule peer {i}", "", Quiet, FactionId: godPeers ? "god" : "qun", AdditionalSkillIds: peerSkills));
            b.AddDeck(new("fixture:shenwu-npc-deck", "Real uniform cards and weapon", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(i < 160 ? card : "standard:qinggang_sword", suit, 7)).ToArray() });
            b.AddMode(new(Mode, "Native NPC rule fixture", 4, 4, new Dictionary<string, int>(), "fixture:shenwu-npc-deck", 4,
                ["fixture:shenwu-npc-actor", minionPeer ? "shenwu:zhuyin-left" : "fixture:shenwu-npc-peer-1", "fixture:shenwu-npc-peer-2", "fixture:shenwu-npc-peer-3"], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:blue"] = minionPeer ? 1 : 2, ["team:red"] = minionPeer ? 3 : 2 }));
        }
    }
}
