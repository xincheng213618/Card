using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenLiuBeiChecks
{
    private const string Hero = "happy:shen-liu-bei", Longnu = "happy:longnu", Jieying = "happy:shen-liu-bei-jieying";
    private const string Driver = "fixture:happy-liubei-driver", Watch = "fixture:happy-liubei-watch", Quiet = "fixture:happy-liubei-quiet", Extra = "fixture:happy-liubei-extra";
    private const string Deck = "fixture:happy-liubei-deck";

    public static void FreeChoiceExtraPlayColorsAndActualDamageDrawCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry);
        Accept(g, new StartGameCommand()); Choice(g);
        var actualTurn = g.State.TurnNumber;
        var opening = V(g).HandCount;
        Need(V(g).Hp == 6 && V(g).MaxHp == 6 && V(g).IsChained &&
            P(g)!.Choices.Select(c => c.Parameters.GetValueOrDefault("option-id")).Order().SequenceEqual(new[] { "fire", "thunder" }),
            "The actual extra Play begins with six HP and permanent chain, and locked non-conversion Longnu offers both mandatory choices immediately.");
        Answer(g, c => Option(c) == "fire"); Draw(g, Longnu);
        var fire = Root(g, Longnu);
        Need(V(g).Hp == 5 && V(g).MaxHp == 6 && V(g).HandCount == opening && fire.NativeDraw is { RequestedCount: 2, Recipients: [0] },
            "Free fire choice really loses one HP and requests two native cards; the HP loss is not damage and issues no Jieying draw.");
        Continue(g); Play(g);
        Need(V(g).HandCount == opening + 2 && E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Jieying || e.BindingId != "draw-on-actual-damage"),
            "The fire cost receives exactly two entities and never masquerades as Jieying's actual-damage benefit.");
        var red = V(g).Hand.Single(c => c.Suit == Suit.Heart).Id;
        var farHp = V(g, 2).Hp;
        Native(g, Action(g, red, 2)); Play(g);
        Need(V(g, 2).Hp == farHp - 1 && E<CardActionAcceptedEvent>(g).Any(e => e.Action is { ActorSeat: 0, EffectiveKind: CardKind.FireSlash } a && a.PhysicalCards is [{ CardId: var material, CardKind: CardKind.Peach, From: var origin }] && material == red && origin == CardLocation.Hand(0) && a.TargetSeats.SequenceEqual([2])),
            "A genuine red Peach entity is forced to fire Slash and really reaches distance-two seat 2 with no weapon.");

        var hp = V(g).Hp; var hand = V(g).HandCount;
        Damage(g); Prevent(g); Answer(g, c => Key(c) == "activate"); Play(g);
        Need(V(g).Hp == hp && V(g).HandCount == hand && !E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Jieying && e.BindingId == "draw-on-actual-damage"),
            "A genuinely prevented two-point native damage has no actual loss or Jieying draw.");
        Damage(g); Prevent(g); Answer(g, c => Key(c) == "skip"); Draw(g, Jieying);
        var benefit = Root(g, Jieying); var benefitId = benefit.Id;
        Need(V(g).Hp == hp - 2 && V(g).HandCount == hand && benefit.NativeDraw is { RequestedCount: 1, Recipients: [0] } &&
            E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.Amount == 2) == 1 &&
            g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(f => f.TriggerWindow == SkillProgramTriggerWindow.DamageAppliedBeforeDying && f.TargetSeat == 0) &&
            g.ResolutionStack.All(f => f is not DyingFrame),
            "One real two-point damage is already applied when Jieying requests one card in the original before-dying damage window, rather than one card per point or before payment.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g).Hp == hp - 2 && V(g).HandCount == hand + 1 &&
            E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.ParentFrameId == benefitId && e.RecipientSeat == 0 && e.RequestedCount == 1 && !e.Replaced) == 1,
            "The cold-restored actual-damage parent gains its single real card once without reapplying damage or duplicating a benefit.");

        End(g); Choice(g);
        Need(g.State.TurnNumber == actualTurn && V(g).Hand.Any(c => c.IdentityKind == CardKind.FireSlash),
            "Completing the inserted Play returns to this same actual turn's normal Play with the fire identity still active.");
        var beforeThunder = V(g).HandCount; var beforeDraws = E<NativeDrawAttemptStartedEvent>(g).Length;
        Answer(g, c => Option(c) == "thunder"); Play(g);
        Need(g.State.TurnNumber == actualTurn && V(g).MaxHp == 5 && V(g).Hp == hp - 2 && V(g).HandCount == beforeThunder &&
            E<NativeDrawAttemptStartedEvent>(g).Length == beforeDraws &&
            V(g).Hand.Where(c => c.Suit is Suit.Heart or Suit.Diamond).All(c => c.IdentityKind == CardKind.FireSlash) &&
            V(g).Hand.Where(c => c.Suit is Suit.Spade or Suit.Club).All(c => c.IdentityKind == CardKind.ThunderSlash),
            "Independent second-Play thunder choice reduces maximum HP without drawing and preserves both red-fire and black-thunder identities for the same actual turn.");
        var blackCards = V(g).Hand.Where(c => c.Suit is Suit.Spade or Suit.Club).Take(2).Select(c => c.Id).ToArray();
        var peerHp = V(g, 1).Hp;
        foreach (var id in blackCards) { Native(g, Action(g, id, 1)); Play(g); }
        Need(blackCards.Length == 2 && V(g, 1).Hp == peerHp - 2 &&
            E<CardActionAcceptedEvent>(g).Count(e => e.Action is { ActorSeat: 0, EffectiveKind: CardKind.ThunderSlash } a && a.PhysicalCards is [{ CardId: var material, From: var origin }] && blackCards.Contains(material) && origin == CardLocation.Hand(0)) == 2,
            "Two distinct black hand entities both resolve as genuine thunder Slashes despite already using a fire Slash in this same actual turn.");
        End(g); Chain(g, 3); Choice(g);
        Need(g.State.TurnNumber != actualTurn && V(g).IsChained && V(g, 3).IsChained && Limit(g, 3) == V(g, 3).Hp + 2 && Limit(g, 2) == V(g, 2).Hp,
            "The actual ending chains a chosen other role and the mature aura adds two only to a truly chained role, then the next actual turn begins.");
        var repeatedHand = V(g).HandCount;
        Answer(g, c => Option(c) == "thunder"); Play(g);
        Need(V(g).MaxHp == 4 && V(g).HandCount == repeatedHand &&
            V(g).Hand.Where(c => c.Suit is Suit.Heart or Suit.Diamond).All(c => c.IdentityKind is null) &&
            V(g).Hand.Where(c => c.Suit is Suit.Spade or Suit.Club).All(c => c.IdentityKind == CardKind.ThunderSlash) &&
            E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Longnu && e.BindingId == "choose-fire-or-thunder") == 3,
            "Thunder can be chosen again instead of compulsory alternation, while the old turn's fire state has really reset. Actual=" + JsonSerializer.Serialize(new
            {
                V(g).Hp, V(g).MaxHp, V(g).HandCount, RepeatedHand = repeatedHand, ActualTurn = g.State.TurnNumber,
                Hand = V(g).Hand.Select(c => new { c.Id, c.Kind, c.Suit, c.IdentityKind }),
                BindingStarts = E<ProgramBindingStartedEvent>(g).Where(e => e.SkillId == Longnu).ToArray(),
                ActivationStarts = E<ProgramSkillStartedEvent>(g).Where(e => e.SkillId == Longnu).ToArray()
            }));
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "Accepted commands replay free non-conversion choices, inserted real Play return, both identities, paid damage draw cold, chaining and real turn resets.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "happy-liu-bei", Hero, 6, 0, 0, 0, 0, [Driver, Watch, Extra],
            [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Slash, Suit.Diamond, 7), new(CardKind.Dodge, Suit.Spade, 2), new(CardKind.Dismantlement, Suit.Club, 4)], [],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "shu" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int seat) => "fixture:happy-liubei-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g, string skill) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == skill);
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option-id");
    private static void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Happy Liu Bei native command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing Happy Liu Bei choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    private static LegalAction Action(GameEngine g, int card, int target) => g.GetHumanLegalActions().First(a => a.CardId == card && a.TargetSeats.SequenceEqual([target]));
    private static void Choice(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Longnu && p.Choices.Any(c => Option(c) == "fire"));
    private static void Draw(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == skill && f.NativeDraw is not null));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Continue(GameEngine g) => Answer(g, c => Option(c) == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Damage(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Driver, "damage-self", [], [], g.Revision, P(g)!.PromptId));
    private static void Prevent(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => Key(c) == "activate"));
    private static void Chain(GameEngine g, int target) { Reach(g, p => p.SkillPrompt?.SkillId == Jieying && p.Choices.Any(c => Key(c) == "select-target")); Answer(g, c => c.Targets.SequenceEqual([target])); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Happy Liu Bei boundary: " + JsonSerializer.Serialize(new { Prompt = p, Hp = V(g).Hp, MaxHp = V(g).MaxHp, Turn = g.State.TurnNumber }));
            }
            Need(g.State.Winner == Winner.None, "The bounded native Happy Liu Bei game must retain its actual next boundary.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Happy Liu Bei missed the bounded native boundary.");
    }
    private static int Limit(GameEngine g, int seat)
    {
        var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(g)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(g, [players[seat]])!;
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold restore preserves original damage parent, paid HP, one-card native request, private entities and exact return cursor."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-liu-bei", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold-real-hand","query":"handLimit","operation":"add","value":20,"priority":0}],
               "activations":[{"id":"damage-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}],
               "triggers":[{"id":"prevent-fixture-damage","window":"beforeDamageApplied","subject":"damageTarget","optional":true,"priority":100,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]},
              {"id":"{{Extra}}","revision":1,"triggers":[{"id":"extra-play","isSkillInvocation":false,"window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实伤害驱动", description = "原生两点伤害及可选择防止。" }, [Extra] = new { name = "额外实际出牌阶段", description = "正常准备前插入真实出牌阶段。" }, [Quiet] = new { name = "安静角色", description = "跳过其他角色出牌。" }, [Watch] = new { name = "原生摸牌子窗", description = "暂停实际摸牌请求。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new(Peer(s), "真实同行", "supporter", Quiet, "wei", 6));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:dodge", i % 2 == 0 ? Suit.Spade : Suit.Club, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐刘备自主龙怒与结营", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
