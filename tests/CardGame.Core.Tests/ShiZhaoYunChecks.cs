using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiZhaoYunChecks
{
    private const string Hero = "mobile:shi-zhao-yun", Wuyi = "mobile:shi-wuyi", Cuifeng = "mobile:shi-cuifeng",
        Driver = "fixture:shi-zhao-yun-driver", Quiet = "fixture:shi-zhao-yun-quiet", Watch = "fixture:shi-zhao-yun-watch",
        Practice = "fixture:shi-zhao-yun-practice", Counter = "fixture:shi-zhao-yun-counter", Peer = "fixture:shi-zhao-yun-peer", Deck = "fixture:shi-zhao-yun-deck";

    public static void LifetimeHandNamesCardlessRoundQuotaUnlimitedBasicsAndFixedFourCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V(0).Hp == 2 && s.V(0).MaxHp == 4 && !s.ZeroSlash(),
            "The printed four-HP general starts without an unearned cardless allowance.");
        s.Native(CardKind.Alcohol); s.Play();
        s.Need(s.ZeroSlash() && !s.HasSlashTarget(2), "The first genuine hand basic unlocks one round use while ordinary distance still applies.");
        s.Need(WuyiQuota(s) is { Value: 1, Used: 0 }, "The public skill state exposes the unused shared round allowance once for both view-as bindings.");
        var hand = s.V(0).HandCount;
        s.NativeAction(s.G.GetHumanLegalActions().First(a => a.CardId == 0 && a.ConversionSource?.SkillId == Wuyi && a.TargetSeats.SequenceEqual([1]))); s.Play();
        s.Need(s.V(0).HandCount == hand && !s.ZeroSlash() && !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The real zero-entity Slash pays the shared round allowance and ordinary Slash quota without inventing a second hand name.");
        s.Need(WuyiQuota(s) is { Value: 1, Used: 1 }, "The public skill state reports the paid round allowance immediately after its native use.");

        s.Driver("foreign-duel", [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Answer(c => c.Cards.Count == 1 && s.V(0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Slash)); s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol) && s.HasSlashTarget(1) && !s.HasSlashTarget(2),
            "An actual played hand Slash supplies the second lifetime name, reopening both Slash and Alcohol counts without changing distance.");
        s.Native(CardKind.Alcohol); s.Play(); s.Native(CardKind.FireSlash, 1); s.Play(); s.Native(CardKind.ThunderSlash, 1); s.Play();
        s.Need(!s.HasSlashTarget(2), "Fire and Thunder Slashes share the Slash name and never manufacture the third basic name.");
        s.Native(CardKind.Peach); s.Play();
        s.Need(s.V(0).Hp == 3 && s.HasSlashTarget(2), "The real hand Peach supplies name three and removes basic-card distance limits.");
        s.Native(CardKind.Slash, 2); s.Play();

        s.Driver("poke", [3]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != Wuyi),
            "The spent cardless Slash also closes the same round's zero-card Dodge option.");
        var beforeFourth = s.V(0).HandCount;
        s.Answer(c => c.Cards.Count == 1 && s.V(0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Dodge));
        s.Play();
        s.Need(beforeFourth > 5 && s.V(0).HandCount == 4 && s.E<CardActionAcceptedEvent>().Any(e =>
                e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Dodge && e.Action.PhysicalCards.Count == 1),
            "A real hand Dodge reaches four distinct lifetime names and immediately discards actual excess hand entities down to four.");

        var paid = s.V(0).Hand[0].Id; s.Driver("discard-one", [], [paid]);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.V(0).HandCount == 3 && s.Zone(paid) == CardLocation.DiscardPile,
            "The fixed-four refill waits on its real native draw child after one actual hand entity has left.");
        var movements = s.G.CardMovements.Count(m => m.CardId == paid && m.To == CardLocation.DiscardPile);
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.V(0).HandCount == 4 && s.G.CardMovements.Count(m => m.CardId == paid && m.To == CardLocation.DiscardPile) == movements,
            "Cold recovery returns the already-paid refill child once without repeating the original discard.");
        s.Driver("draw-three"); s.Play();
        s.Need(s.V(0).HandCount == 4, "A later real three-card gain also settles to exactly four hand entities.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.State.TurnNumber > turn && s.ZeroSlash() && s.V(0).HandCount == 4,
            "The next genuine round restores the cardless quota and keeps all four lifetime unlocks and the fixed hand.");
        s.Need(WuyiQuota(s) is { Value: 1, Used: 0 }, "The next round's public allowance resets while lifetime progress remains unlocked.");
        s.Replay();
    }

    private static ProgramPublicRuleStateSnapshot WuyiQuota(Scene scene) => scene.V(0).SkillRuntimeStates!
        .Single(skill => skill.SkillId == Wuyi).PublicRuleStates!
        .Single(state => state.StateId == "shi-wuyi" && state.Kind == ProgramPublicRuleStateKind.ActivationLimit && state.Scope == SkillUsageScope.Round);

    public static void ActualTurnAllPlayersPlayedSlashesDynamicBidirectionalDuelAndRoundLimit()
    {
        var s = Scene.Start(duels: true); s.Play();
        var startingHp = s.V(0).Hp; s.End();
        PracticeDuel(s, 1);
        s.OfferCuifeng(1); var firstTurn = s.G.State.TurnNumber;
        s.Need(s.E<CardActionAcceptedEvent>().Count(e => e.Action.Type == CardActionType.Response &&
                e.Action.EffectiveKind == CardKind.Slash && e.Action.ActorSeat is 0 or 1) == 2,
            "Both participants really played their own hand Slash in the first foreign actual turn.");
        var firstHp = s.V(1).Hp; s.ActivateCuifeng(1);
        var firstUse = s.E<CardUseDeclaredEvent>().Last(e => e.CardKind == CardKind.Duel && e.SourceSeat == 0);
        s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == firstUse.ResolutionId));
        s.Need(firstUse.CardId == 0 && s.E<CardActionAcceptedEvent>().Any(e => e.Action is
                { ActorSeat: 0, Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0 } && e.Action.TargetSeats.SequenceEqual([1])),
            "The accepted turn-ending skill owns a genuine native zero-entity Duel against that exact turn's other living owner.");
        s.Need(s.V(1).Hp == firstHp - 3 && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 3),
            "The outgoing native Duel adds both actors' actual played Slashes, for one plus two damage.");

        PracticeDuel(s, 2); s.OfferCuifeng(2); s.ActivateCuifeng(2);
        var secondUse = s.E<CardUseDeclaredEvent>().Last(e => e.CardKind == CardKind.Duel && e.SourceSeat == 0);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash } && s.P.IncomingCard == CardKind.Duel);
        var hp = s.V(0).Hp; s.Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null);
        s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == secondUse.ResolutionId));
        s.Need(s.V(0).Hp == hp - 4 && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 2 && e.TargetSeat == 0 && e.Amount == 4) &&
                s.V(0).Hp == startingHp - 6,
            "The second native Duel also increases incoming damage and includes the Slash just played inside this very Duel, while prior actual-turn Slashes do not leak in.");
        s.Play();
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 0 && e.CardKind == CardKind.Duel) == 2 &&
                s.G.State.TurnNumber > firstTurn,
            "The third other living turn offers no third activation in that same real round, and the owner's own ending never creates a self-Duel.");
        s.Replay();
    }

    private static void PracticeDuel(Scene s, int source)
    {
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Answer(c => c.Cards.Count == 1 && s.V(0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Slash));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Need(s.E<DuelResponseEvent>().Any(e => e.ResponderSeat == source && e.UsedSlash), "The actual peer spent its own real Slash before the next human response.");
        s.Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null);
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public bool ZeroSlash() => G.GetHumanLegalActions().Any(a => a.CardId == 0 && a.ConversionSource?.SkillId == Wuyi);
        public bool HasSlashTarget(int target) => G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        public void Driver(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, ShiZhaoYunChecks.Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int? target = null)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is > 0 && a.ConversionSource is null &&
                V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind) && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(action is not null, "The expected actual hand-card action must be legal: " + kind);
            NativeAction(action!);
        }
        public void NativeAction(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected native prompt choice must be published.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void OfferCuifeng(int turnOwner) => Reach(() => G.State.CurrentSeat == turnOwner &&
            P?.SkillPrompt?.SkillId == Cuifeng && P.Choices.Any(c => Key(c) == "activate"));
        public void ActivateCuifeng(int turnOwner)
        {
            Answer(c => Key(c) == "activate");
            Reach(() => P?.SkillPrompt?.SkillId == Cuifeng && P.Choices.Any(c => c.Targets.SequenceEqual([turnOwner])));
            Need(P!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.SequenceEqual([turnOwner])),
                "The turn-ending counterpart menu contains only this actual other living turn's owner.");
            Answer(c => c.Targets.SequenceEqual([turnOwner]));
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 220; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.SkillPrompt?.SkillId == Wuyi && p.Choices.Any(c => Key(c) == "finish-owned-cards")) { Answer(c => Key(c) == "finish-owned-cards"); continue; }
                    if (p.SkillPrompt?.SkillId == Wuyi && p.Choices.Any(c => Key(c) == "select-owned-cards")) { Answer(c => Key(c) == "select-owned-cards"); continue; }
                    if (p.SkillPrompt?.SkillId == Cuifeng && p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.PlayerSeat == 0 && p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Zhao Yun boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain all participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Zhao Yun did not reach the expected bounded native boundary.");
        }
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "The actual prompt, nested frames, all views, journal and card entities cold-restore exactly.");
        }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces lifetime names, round allowances and all fixed-hand children exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Zhao Yun command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Wuyi = E<MobileShiWuyiBasicNameRecordedEvent>(), Actions = E<CardActionAcceptedEvent>().Select(e => e.Action),
            States = E<ProgramBooleanStateChangedEvent>(), SkillRuntimeStates = V(0).SkillRuntimeStates,
            Last = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(bool duels = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = duels
                ? [new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Slash, Suit.Diamond, 10),
                    new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Heart, 9)]
                : [new(CardKind.Alcohol, Suit.Heart, 1), new(CardKind.Alcohol, Suit.Club, 2), new(CardKind.Slash, Suit.Club, 3),
                    new(CardKind.FireSlash, Suit.Diamond, 4), new(CardKind.ThunderSlash, Suit.Spade, 5), new(CardKind.Peach, Suit.Heart, 6),
                    new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.DrawTwo, Suit.Diamond, 9),
                    new(CardKind.Slash, Suit.Heart, 10), new(CardKind.Dodge, Suit.Heart, 11), new(CardKind.Slash, Suit.Spade, 12),
                    new(CardKind.Dodge, Suit.Club, 1), new(CardKind.Dodge, Suit.Diamond, 2), new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Heart, 5)];
            ShanheCombatantDefinition Combatant(int seat) => new(Peer, Hp: duels && seat is 1 or 2 ? 8 : null,
                ExtraSkillIds: duels && seat is 1 or 2 ? [Practice] : !duels && seat == 3 ? [Counter] : [])
            { StartingHand = duels && seat is 1 or 2 ? Enumerable.Range(0, seat).Select(i => new ShanheStartingCard(CardKind.Slash, Suit.Spade, 7 + i)).ToArray() : [] };
            var setup = new ShanheBattleSetup(83, 1, duels ? "shi-zhao-yun-duels" : "shi-zhao-yun-wuyi", Hero, duels ? 10 : 2,
                duels ? 6 : 0, 0, 0, 0, [ShiZhaoYunChecks.Driver, Watch], opening, [], [Combatant(1), Combatant(2), Combatant(3)], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-zhao-yun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"preserve-materials","query":"handLimit","operation":"add","value":20,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],
                "activations":[{"id":"foreign-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"discard-one","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-refill","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}]}]},
              {"id":"{{Practice}}","revision":1,"triggers":[{"id":"real-peer-duel","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"outputKind":"duel"}]}]},
              {"id":"{{Counter}}","revision":1,"triggers":[{"id":"actual-counter","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Driver] = new { name = Driver, description = "固定真实牌与实际回合的小场景。" },
                [Quiet] = new { name = Quiet, description = "保留真实回合并跳过普通摸牌出牌。" },
                [Watch] = new { name = Watch, description = "暂停实际原生补牌子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Practice] = new { name = Practice, description = "实际回合中的真实双方杀响应。" },
                [Counter] = new { name = Counter, description = "真实受伤后的原生虚拟杀。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定真实基本牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势赵云实际解锁与原生决斗", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
