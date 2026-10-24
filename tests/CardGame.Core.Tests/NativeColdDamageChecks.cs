using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class NativeColdDamageChecks
{
    private const string Owner = "fixture:cold-owner", Driver = "fixture:cold-driver", Watch = "fixture:cold-recovery";
    private const string Deck = "fixture:cold-deck", Late = "fixture:cold-late-cap", Cap = "fixture:cold-cap-one";
    private static readonly Lazy<ContentSkillDefinition> Wushuang = new(() =>
        StandardContentRegistry.CreateWithClassicGenerals().GetSkill("classic:wushuang"));

    public static void PhysicalSlashUsesOldFamilyDoubleDodgeBonusAndElementalChain()
    {
        Need(!ProgramCardKindMatch.Contains([CardKind.Slash], CardKind.ColdSlash) &&
            !ProgramCardKindMatch.Contains([CardKind.FireSlash, CardKind.ThunderSlash], CardKind.ColdSlash) &&
            ProgramCardKindMatch.Contains([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel], CardKind.ColdSlash),
            "Compatibility extends only an explicitly complete old Slash family; a named Slash or selected elemental pair keeps its exact meaning.");
        var (g, registry) = Start();
        DriverUse(g, "armor", [2]); Play(g);
        DriverUse(g, "chain", [2]); Play(g);
        DriverUse(g, "chain", [3]); Play(g);
        var before = View(g, 2);
        var otherHp = View(g, 3).Hp;
        var coldId = View(g).Hand.First(c => c.Kind == CardKind.ColdSlash).Id;
        var action = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.CardId == coldId && a.TargetSeats.SequenceEqual([2]));
        Accept(g, new PlayCardCommand(0, coldId, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind));
        Reach(g, p => IsCold(p));
        var offer = Facts<ColdDamageOfferedEvent>(g).Single();
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == offer.AttackId);
        Need(use.CardKind == CardKind.ColdSlash && use.CardAttack is { RequiredDodgeResponses: 2, SuccessfulDodgeResponses: 1 } &&
            offer.Amount == 2 && View(g, 2).Hand.Count == 1 && before.Hand.Count == 2 &&
            Facts<CardUseDeclaredEvent>(g).Any(e => e.ResolutionId == use.Id && e.CardKind == CardKind.ColdSlash) &&
            g.CardMovements.Any(m => m.CardId == coldId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing),
            "A real physical Cold Slash uses the unchanged classic Wushuang full-family policy: one actual Dodge is insufficient, and the existing full-family damage modifier adds exactly one.");
        Need(P(g)!.IsPrivate && g.CreateSnapshot(2).PendingDecision is null,
            "Only the real Cold damage source receives its replacement choice.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("action") == "cold-damage"); Play(g);
        Need(View(g, 2).Hp == before.Hp - 1 && View(g, 2).Armor == 0 && View(g, 3).Hp == otherHp - 2 &&
            !View(g, 2).IsChained && !View(g, 3).IsChained &&
            Facts<DamageRequestedEvent>(g).Count(e => e.SourceCard == CardKind.ColdSlash && e.Nature == DamageNature.Cold) == 2 &&
            Facts<ColdDamageOfferedEvent>(g).Count() == 1 &&
            Facts<ColdDamageCompletedEvent>(g).Single() is { Prevented: false },
            "Declining applies genuine Cold damage through native armor, then propagates its elemental amount; the chain recipient never opens a second Cold replacement.");
        _ = Cold(g, registry);

        // The cap is really acquired only after the origin has paid. Starting
        // with a cap would bypass the late-scope proof guards under regression.
        var (late, lateRegistry) = Start(lateCap: true);
        DriverUse(late, "armor", [2]); Play(late);
        DriverUse(late, "chain", [2]); Play(late);
        DriverUse(late, "chain", [3]); Play(late);
        var originHp = View(late, 2).Hp; var cappedHp = View(late, 3).Hp;
        var card = View(late).Hand.First(c => c.Kind == CardKind.ColdSlash).Id;
        var native = late.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.CardId == card && a.TargetSeats.SequenceEqual([2]));
        Accept(late, new PlayCardCommand(0, card, native.TargetSeats, late.Revision, P(late)!.PromptId, native.PlayedCardKind));
        Reach(late, IsCold); Answer(late, c => c.Parameters.GetValueOrDefault("action") == "cold-damage");
        Reach(late, p => p.SkillPrompt?.SkillId == Late);
        var firstDamage = late.ResolutionStack.OfType<DamageFrame>().Single();
        Need(Facts<DamageAppliedEvent>(late).ToArray() is [{ TargetSeat: 2, Amount: 2, Nature: DamageNature.Cold }] &&
            View(late, 3).IncomingDamageCap is null && !Facts<RecipientScopedDamageBaseEvent>(late).Any(),
            "The origin has really paid two Cold damage before any recipient cap or propagation-base scope exists.");
        late = Cold(late, lateRegistry);
        Accept(late, new AdvanceOneStepCommand(late.Revision));
        Reach(late, p => p.SkillPrompt?.SkillId == Late);
        Need(View(late, 3).IncomingDamageCap == 1 && Facts<DamageAppliedEvent>(late).Count() == 1 &&
            !Facts<ProgramDamageCappedEvent>(late).Any() && !Facts<RecipientScopedDamageBaseEvent>(late).Any(),
            "A real once-only GrantSkills child gives the next chained recipient cap one after the original payment and before the late base is issued.");
        late = Cold(late, lateRegistry);
        Accept(late, new AdvanceOneStepCommand(late.Revision)); Play(late);
        var scope = Facts<RecipientScopedDamageBaseEvent>(late).Single();
        Need(scope is { FirstTargetSeat: 2, Amount: 2, FixedTargetDamageCap: true, LateFixedTargetDamageCap: { } proof } &&
            proof.Nature == DamageNature.Cold && proof.Amount == 2 && proof.PaidTargetSeat == 2 && proof.NextTargetSeat == 3 &&
            proof.DamageFrameId == firstDamage.Id && proof.AttackFrameId == firstDamage.ParentFrameId &&
            proof.Cap == 1 && proof.CapSource.OwnerSeat == 3 &&
            proof.RequestedEventIndex < proof.AppliedEventIndex && proof.AppliedEventIndex < proof.AfterDamageEventIndex &&
            Facts<ProgramDamageCappedEvent>(late).ToArray() is [{ TargetSeat: 3, BaseAmount: 2, CappedAmount: 1 }] &&
            Facts<DamageAppliedEvent>(late).Select(e => (e.TargetSeat, e.Amount, e.Nature)).SequenceEqual([(2, 2, DamageNature.Cold), (3, 1, DamageNature.Cold)]) &&
            View(late, 2).Hp == originHp - 1 && View(late, 3).Hp == cappedHp - 1 &&
            Facts<ColdDamageOfferedEvent>(late).Count() == 1,
            "The real late-cap path issues an exact Cold original-segment proof, retains base two, and pays one only at the capped recipient without repeating replacement or grant.");
        _ = Cold(late, lateRegistry);
    }

    public static void SequentialReplacementWaitsForPaidSilverLionRecoveryAndRestores()
    {
        var (g, registry) = Start();
        var lion = View(g).Hand.Single(c => c.Kind == CardKind.SilverLion).Id;
        var equipment = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Equip && a.CardId == lion);
        Accept(g, new PlayCardCommand(0, lion, equipment.TargetSeats, g.Revision, P(g)!.PromptId)); Play(g);
        var hp = View(g).Hp;
        DriverUse(g, "self-cold", []); Reach(g, IsCold);
        var offered = Facts<ColdDamageOfferedEvent>(g).Single();
        Answer(g, c => c.Parameters.GetValueOrDefault("action") == "cold-replace");
        Need(P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("target-zone") == "hand").All(c => c.Cards.Count == 0) &&
            P(g)!.Choices.Any(c => c.Cards.SequenceEqual([lion])),
            "Sequential Cold replacement publishes only opaque hand slots and actual public equipment.");
        Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = g.ResolutionStack.OfType<ColdDamageReplacementFrame>().Single();
        Need(paid is { Activated: true, ChoiceMade: true } && paid.Discards is [var first] && first.CardId == lion &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == paid.Id &&
                f.Continuation == PostEventContinuation.ColdDamageReplacement && f.Change.ParentFrameId == paid.Id) &&
            Zone(g, lion) == CardLocation.DiscardPile && View(g).Hp == hp + 1 &&
            !Facts<ColdDamageCompletedEvent>(g).Any(),
            "The first actual discard is retained on the native Cold frame while Silver Lion's genuine recovery child is pending; the second card is not charged early.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(g, IsCold);
        var handBefore = View(g).Hand.Count;
        Answer(g, c => c.Parameters.GetValueOrDefault("target-zone") == "hand"); Play(g);
        var completed = Facts<ColdDamageCompletedEvent>(g).Single();
        Need(completed.FrameId == paid.Id && completed.AttackId == offered.AttackId && completed.Prevented &&
            completed.PreventedAmount == offered.Amount && completed.DiscardedCount == 2 &&
            Facts<ColdDamageDiscardedEvent>(g).Select(e => e.Ordinal).SequenceEqual([1, 2]) &&
            g.CardMovements.Count(m => m.CardId == lion && m.Reason.Value == "damage.cold.discard") == 1 &&
            View(g).Hand.Count == handBefore - 1 && View(g).Hp == hp + 1 &&
            !Facts<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0) &&
            !g.ResolutionStack.OfType<ColdDamageReplacementFrame>().Any(),
            "The restored child return keeps the first payment, discards exactly one further live hand entity, and prevents the entire original amount without damage triggers.");
        _ = Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Start(bool lateCap = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(37, 1, "native-cold", Owner, 5, 0, 0, 0, 0, [],
            [new(CardKind.ColdSlash, Suit.Spade, 7), new(CardKind.SilverLion, Suit.Club, 1)], [],
            [new(Peer(2)) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Peach, Suit.Heart, 3)] },
             new(Peer(3), ExtraSkillIds: lateCap ? [Late] : []) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('b', 64));
        var game = GameEngine.CreateStandard(new()
        {
            Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand()); Play(game); return (game, registry);
    }
    private static string Peer(int s) => "fixture:cold-peer-" + s;
    private static PlayerSnapshot View(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, g.PlayerCount).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsCold(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "native-cold-damage");
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static CardLocation Zone(GameEngine g, int card) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location;
    private static void DriverUse(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 100; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected native Cold prompt: " + JsonSerializer.Serialize(p));
            }
            Need(g.State.Winner == Winner.None, "Native Cold fixture ended before its real boundary.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Native Cold fixture missed its bounded boundary.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    {
        var p = P(g)!; var c = p.Choices.FirstOrDefault(pick);
        Need(c is not null, "Native Cold choice missing: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand c)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Cold command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Native Cold checkpoint reconstructs all private views, typed parents, paid children, facts and physical movements.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, g.PlayerCount).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Zones = g.CreateCardZoneDiagnostics(), g.ObserverFailures
    });
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-native-cold", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            // Import the real unchanged Wushuang only. Unrelated registered
            // damage observers would pre-issue a scope and bypass the late-cap path.
            b.AddSkill(Wushuang.Value);
            b.AddCard(new("fixture:cold-slash", "冰杀", "基本牌", CardCatalog.Get(CardKind.ColdSlash).Description, CardKind.ColdSlash));
            b.AddCard(new("fixture:cold-lion", "白银狮子", "装备牌", CardCatalog.Get(CardKind.SilverLion).Description, CardKind.SilverLion));
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"attackRange","operation":"set","value":4,"priority":0}],
              "damageModifiers":[{"id":"old-full-family","cardKinds":["slash","fireSlash","thunderSlash"],"amount":1,"condition":"always","sourceScope":"ownerUsed"}],
              "activations":[
               {"id":"armor","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":1}]},
               {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
               {"id":"self-cold","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":3,"nature":"cold"}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{Cap}}","revision":1,"damageModifiers":[{"id":"incoming-one","cardKinds":[],"amount":1,"condition":"always","sourceScope":"damageParticipant","capsToAmount":true}]},
             {"id":"{{Late}}","revision":1,"triggers":[{"id":"late-acquire","window":"afterDamageApplied","subject":"any","damageOccurrence":"perDamage","optional":false,"priority":200,"usageScope":"game","usageLimit":1,
              "effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]},
                         {"op":"grantSkills","target":"owner","skillIds":["{{Cap}}"]},
                         {"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "原生冰伤", description = "真实Cold伤害与连环" },
              [Cap] = new { name = "真实伤害上限一", description = "获得后每段实际受伤至多一" },
              [Late] = new { name = "后伤害实际获得", description = "原段付款后获得上限", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
              [Watch] = new { name = "回复子窗", description = "真实装备离场回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var id in new[] { Driver, Watch, Cap, Late }) b.AddSkill(new(id, id, "原生冰伤机制") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "原生冰伤来源", "supporter", Driver, "qun", 8, [Watch, "classic:wushuang"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new(Peer(i), "原生冰伤目标", "supporter", "standard:none", "shu", 8));
            b.AddDeck(new(Deck, "真实独立牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 3)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "冰伤共用原生机制", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
