using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenLuMengChecks
{
    private const string Hero = "happy:shen-lu-meng", Shelie = "happy:shelie", Gongxin = "happy:gongxin";
    private const string Watch = "fixture:happy-lm-watch", Ending = "fixture:happy-lm-ending", Incoming = "fixture:happy-lm-incoming", Counter = "fixture:happy-lm-counter", Quiet = "fixture:happy-lm-quiet";
    private const string Reason = "skill-program.happy:gongxin.MoveBoundCards";

    public static void ForcedShelieSoleRedHandAndSharedActualTurnCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.SkillPrompt?.SkillId == Shelie && p.Choices.Any(c => Key(c) == "select-subset"));
        var shelie = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Shelie);
        var shown = g.CreateSnapshot(0).PublicRevealedCards.Where(c => shelie.CardSetBindings.Single(b => b.Name == "revealed").CardIds.Contains(c.Id)).ToArray();
        var suits = shown.Select(c => c.Suit).Distinct().Count();
        var openingHand = V(g).HandCount;
        var openingSources = g.CardMovements.Where(m => m.To == CardLocation.Hand(0)).Select(m => new { m.CardId, m.From, m.To, m.Reason }).ToArray();
        Need(shown.Length == 5 && P(g)!.Choices.All(c => c.Cards.Count == suits && c.Cards.Select(id => shown.Single(card => card.Id == id).Suit).Distinct().Count() == suits) &&
            P(g)!.Choices.All(c => Key(c) == "select-subset"),
            "Locked Shelie immediately reveals exactly five real deck cards and permits exactly one card of every displayed suit, with no optional replacement or partial subset.");
        var taken = P(g)!.Choices[0].Cards.ToArray(); Answer(g, c => c.Cards.SequenceEqual(taken)); Play(g);
        Need(V(g).HandCount == openingHand + suits && taken.All(id => Zone(g, id) == CardLocation.Hand(0)) &&
            shown.Where(c => !taken.Contains(c.Id)).All(c => Zone(g, c.Id) == CardLocation.DiscardPile),
            "Shelie replaces the ordinary two-card draw, obtains only the full distinct-suit subset and physically discards the remaining revealed entities. Actual=" +
            JsonSerializer.Serialize(new { HandCount = V(g).HandCount, OpeningHand = openingHand, Suits = suits, OpeningSources = openingSources,
                Taken = taken.Select(id => new { CardId = id, Location = Zone(g, id) }), Remaining = shown.Where(c => !taken.Contains(c.Id)).Select(c => new { CardId = c.Id, Location = Zone(g, c.Id) }) }));
        Native(g, Iron(g, [1, 2])); Play(g);
        Need(E<HappyGongxinTargetBoundEvent>(g).Length == 0 && V(g, 1).IsChained && V(g, 2).IsChained,
            "A genuine two-target Iron Chain resolves both targets without issuing a sole-target Gongxin candidate.");
        var diamond = V(g, 1).Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Diamond).Id;
        var before = V(g).HandCount;
        Native(g, Iron(g, [1])); Offer(g, Gongxin); Activate(g); Reveal(g);
        var root = Root(g)!; var firstId = root.Id; var witness = root.HappyGongxin!;
        Need(witness is { ActorSeat: 0, TargetSeat: 1, HandOwnerSeat: 1 } && root.SelectedTargetSeats.SequenceEqual([1]) &&
            P(g)!.ValidCardIds.Count == V(g, 1).HandCount && P(g)!.Choices.Any(c => c.Cards.SequenceEqual([diamond])) &&
            P(g)!.Choices.Any(c => Key(c) == "reveal-target-hand-card-decline"),
            "Outgoing Gongxin binds the original unique other target, privately views the entire hand, allows a Diamond red card and retains the independent reveal-decline option.");
        Answer(g, c => c.Cards.SequenceEqual([diamond])); Disposition(g, "obtain");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        Need(Root(g) is { Id: var paidId, InstructionIndex: 4, PendingMovementContinuation: not null } && paidId == firstId &&
            Zone(g, diamond) == CardLocation.Hand(0) && V(g).HandCount == before &&
            g.CardMovements.Count(m => m.CardId == diamond && m.Reason.Value == Reason) == 1,
            "The native red-card obtain is really paid once and suspends its original unique Use witness in the real gain child.");
        g = Cold(g, registry); Continue(g); Play(g);
        Native(g, Iron(g, [2])); Play(g);
        Need(E<HappyGongxinTargetBoundEvent>(g).Length == 1 && g.CardMovements.Count(m => m.CardId == diamond && m.Reason.Value == Reason) == 1,
            "A second genuine sole-target use in the same actual turn cannot spend another Gongxin or repeat the cold-restored gain.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Offer(g, Ending); Activate(g); Select(g, Ending, 1);
        Offer(g, Incoming);
        Need(E<HappyGongxinTargetBoundEvent>(g).Length == 1 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action is { Type: CardActionType.Use, ActorSeat: 1, EffectiveKind: CardKind.Dismantlement } a && a.TargetSeats.SequenceEqual([0])) &&
            E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == witness.ActualTurn),
            "A genuine foreign-actor Dismantlement targeting Lu Meng in that same actual turn shares the already-spent outgoing quota; it resolves without another hand view.");
        Activate(g); Select(g, Incoming, 0);
        Offer(g, Gongxin); Activate(g); Reveal(g);
        var incoming = Root(g)!.HappyGongxin!;
        var top = V(g, 1).Hand.First(c => c.Suit == Suit.Heart).Id;
        Need(incoming is { ActorSeat: 1, TargetSeat: 0, HandOwnerSeat: 1 } && incoming.ActualTurn != witness.ActualTurn &&
            E<HappyGongxinTargetBoundEvent>(g).Length == 2,
            "The next real foreign turn resets the shared quota and incoming sole-target Gongxin correctly views the actual other card user, rather than Lu Meng's own hand.");
        Answer(g, c => c.Cards.SequenceEqual([top])); Disposition(g, "top");
        Reach(g, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        Need(Top(g) == top && g.CardMovements.Count(m => m.CardId == top && m.From == CardLocation.Hand(1) && m.To == CardLocation.DrawPile && m.Reason.Value == Reason) == 1 &&
            E<HappyGongxinTargetBoundEvent>(g).Last().Witness == incoming,
            "The revealed Heart card is really placed at the deck top before the original native Duel begins its response, preserving the incoming Use and original actor.");
        Answer(g, c => c.Cards.Count == 0);
        Offer(g, Counter); Activate(g); Select(g, Counter, 0);
        Reach(g, p => p.SkillPrompt?.SkillId == Counter && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "done"));
        Need(E<HappyGongxinTargetBoundEvent>(g).Length == 2 &&
            E<CardActionAcceptedEvent>(g).Any(e => e.Action is { Type: CardActionType.Use, ActorSeat: 0, EffectiveKind: CardKind.Slash } a && a.TargetSeats.SequenceEqual([1])) &&
            E<TurnStartedEvent>(g).Last().ActorSeat == 1 &&
            E<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Gongxin && e.Scope == SkillUsageScope.Turn) == 2,
            "Lu Meng's real out-of-turn counter Slash in the same foreign turn shares the incoming quota: both directions together consume exactly one use per actual turn.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The full accepted command journal replays forced Shelie, native unique-target identities, shared actual-turn quotas, private reveal, paid obtain cold return and real deck-top placement.");

        var self = GameEngine.CreateStandard(Options(selfTarget: true), registry);
        Accept(self, new StartGameCommand());
        Reach(self, p => p.SkillPrompt?.SkillId == Shelie && p.Choices.Any(c => Key(c) == "select-subset"));
        Answer(self, _ => true); Play(self);
        var ownRed = V(self).Hand.First(c => c.Kind == CardKind.IronChain && c.Suit == Suit.Heart).Id;
        var peach = self.GetHumanLegalActions().First(a => a.CardId is { } id && V(self).Hand.Single(c => c.Id == id).Kind == CardKind.Peach);
        Native(self, peach); Offer(self, Gongxin); Activate(self); Reveal(self);
        var ownHand = V(self).HandCount;
        Need(Root(self)!.HappyGongxin is { ActorSeat: 0, TargetSeat: 0, HandOwnerSeat: 0 } &&
            P(self)!.ValidCardIds.Count == ownHand && P(self)!.Choices.Any(c => c.Cards.SequenceEqual([ownRed])) &&
            P(self)!.Choices.Any(c => Key(c) == "reveal-target-hand-card-decline"),
            "A genuine wounded Lu Meng's self Peach has one actual self target: outgoing Gongxin views his own hand with the native red and decline choices, without issuing a duplicate incoming branch.");
        Answer(self, c => c.Cards.SequenceEqual([ownRed])); Disposition(self, "obtain"); Play(self);
        Need(V(self).Hp == 3 && V(self).HandCount == ownHand && Zone(self, ownRed) == CardLocation.Hand(0) &&
            E<HappyGongxinTargetBoundEvent>(self) is [{ Witness.ActorSeat: 0, Witness.TargetSeat: 0, Witness.HandOwnerSeat: 0 }] &&
            self.CardMovements.All(m => m.Reason.Value != Reason),
            "Obtaining the displayed card from his own unchanged hand performs no card move or gain child, and the original self Peach still resolves its real recovery.");
        Native(self, Iron(self, [1])); Play(self);
        Need(E<HappyGongxinTargetBoundEvent>(self).Length == 1 && E<SkillUsageConsumedEvent>(self).Count(e => e.SkillId == Gongxin && e.Scope == SkillUsageScope.Turn) == 1 &&
            State(self) == State(GameReplay.Replay(Options(selfTarget: true), self.AcceptedCommands, registry)),
            "Self-target Gongxin consumes the same sole actual-turn use, so a later outgoing other-target card does not trigger it again; the exact self-view and no-movement obtain replay.");
    }

    private static GameOptions Options(bool selfTarget = false)
    {
        var setup = new ShanheBattleSetup(7, 1, "happy-lu-meng", Hero, selfTarget ? 2 : 3, 0, 0, 0, 0, [],
            [new(CardKind.IronChain, Suit.Spade, 1), new(CardKind.IronChain, Suit.Club, 2), new(CardKind.IronChain, Suit.Heart, 3), new(CardKind.Slash, Suit.Spade, 4), new(CardKind.Peach, Suit.Diamond, 5)], [],
            [new(Peer(2)) { StartingHand = [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Club, 8)] },
             new(Peer(3)) { StartingHand = [new(CardKind.Slash, Suit.Heart, 9), new(CardKind.Slash, Suit.Club, 10)] }],
            [new(Peer(1)) { StartingHand = [new(CardKind.Slash, Suit.Diamond, 7), new(CardKind.Peach, Suit.Heart, 6), new(CardKind.Dodge, Suit.Spade, 5), new(CardKind.Slash, Suit.Club, 8)] }],
            new string('f', 64)) { DefaultGodFactionId = "wu" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 };
    }
    private static string Peer(int seat) => "fixture:happy-lm-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HappyGongxin is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static int Top(GameEngine g) => g.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderByDescending(c => c.ZoneIndex).First().CardId;
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Happy Lu Meng command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(pick);
        Need(choice is not null, "Missing native Happy Lu Meng choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    private static LegalAction Iron(GameEngine g, int[] targets) => g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g).Hand.Single(c => c.Id == id).Kind == CardKind.IronChain && a.TargetSeats.SequenceEqual(targets));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate"));
    private static void Activate(GameEngine g) => Answer(g, c => Key(c) == "activate");
    private static void Select(GameEngine g, string skill, int target) { Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Targets.SequenceEqual([target]))); Answer(g, c => c.Targets.SequenceEqual([target])); }
    private static void Reveal(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Gongxin && p.Choices.Any(c => Key(c) == "reveal-target-hand-card"));
    private static void Disposition(GameEngine g, string option) { Reach(g, p => p.SkillPrompt?.SkillId == Gongxin && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option)); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == option); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 230; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.Kind == DecisionKind.Nullification || p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) { Answer(g, c => c.Cards.Count == 0); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Happy Lu Meng boundary: " + JsonSerializer.Serialize(new { Prompt = p, Witness = Root(g)?.HappyGongxin, Bound = E<HappyGongxinTargetBoundEvent>(g), Turn = g.CreateSnapshot(0).TurnNumber }));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Happy Lu Meng missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold return preserves the exact native sole Use, private reveal bindings, actual-turn usage and already-paid obtain child."); return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-lu-meng", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Reason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Ending}}","revision":1,"triggers":[{"id":"native-foreign-use-own-turn","window":"playEnding","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"selectedTarget"},"targetRef":{"kind":"owner"},"outputKind":"dismantlement"}]}]},
              {"id":"{{Incoming}}","revision":1,"triggers":[{"id":"native-duel-new-turn","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"duel"}]}]},
              {"id":"{{Counter}}","revision":1,"triggers":[{"id":"native-out-of-turn-counter","window":"playPhaseStarting","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"selectedTargetVirtualSlashAgainstOwner","target":"owner"},{"op":"chooseOption","target":"owner","resultBind":"finished","options":[{"id":"done"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Watch] = new { name = "真实获得子窗", description = "暂停已付的攻心获得。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Ending] = new { name = "实际外人用牌", description = "令他人在本回合内原生使用拆牌。" },
                [Incoming] = new { name = "实际新回合决斗", description = "在自己的实际回合原生使用决斗。" },
                [Counter] = new { name = "实际外回合反击", description = "由选择的他人原生反击。", optionLabels = new Dictionary<string, string> { ["done"] = "完成" } },
                [Quiet] = new { name = "安静角色", description = "跳过出牌阶段。" }
            };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Native Happy Lu Meng fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer(1), "实际用牌者", "supporter", Incoming, "wei", 8) { AdditionalSkillIds = [Counter] });
            for (var s = 2; s < 4; s++) b.AddGeneral(new(Peer(s), "真实目标", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:happy-lm-deck", "固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:slash", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐吕蒙实际唯一目标", 4, 4, new Dictionary<string, int>(), "fixture:happy-lm-deck",
                GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [..d.AdditionalSkillIds!, Watch, Ending] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
