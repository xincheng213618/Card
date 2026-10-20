using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5RoulinChecks
{
    private const string Skill = "shanhe:s5-roulin-14211", Wushuang = "classic:wushuang";
    private const string Driver = "fixture:s5-roulin-driver", Watch = "fixture:s5-roulin-response", DamageWatch = "fixture:s5-roulin-damage";
    private const string HpGate = "fixture:s5-roulin-hp", Owner = "fixture:s5-roulin-owner", Peer = "fixture:s5-roulin-peer";
    private const string Female = "fixture:s5-roulin-female", Target = "fixture:s5-roulin-target", Deck = "fixture:s5-roulin-deck";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-s5-roulin-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new(StandardShanheS5BossPackage.DiaoChanBiyueId)], [], new string('a', 64))));

    public static void AnyGenderBidirectionalSlashResponsesUseTwoDistinctDodgesAndMaximumPolicyCold()
    {
        foreach (var scene in new[] { "incoming-male", "incoming-female", "outgoing-male", "outgoing-female" })
        {
            var s = Scene.Create(scene); var incoming = scene.StartsWith("incoming", StringComparison.Ordinal);
            Need(s.G.ContentRegistry.Generals[s.V(0).GeneralId!].Gender == GeneralGender.Male &&
                s.G.ContentRegistry.Generals[s.V(1).GeneralId!].Gender == (scene.EndsWith("female", StringComparison.Ordinal) ? GeneralGender.Female : GeneralGender.Male),
                "The real printed genders establish the male-male expansion and the ordinary female directions without changing a runtime snapshot.");
            Need(s.V(1) is { Hp: 18, MaxHp: 20 }, "The real NPC has two missing HP, providing actual native response pressure rather than assuming it will spend a Dodge at full HP.");
            var card = incoming ? s.BeginRequested() : s.BeginPhysical(scene == "outgoing-female" ? CardKind.ThunderSlash : CardKind.Slash);
            var responder = incoming ? 0 : 1; var use = s.Attack(card);
            Need(use.CardAttack is { RequiredDodgeResponses: 2, SuccessfulDodgeResponses: 0 },
                "The production two-way policy freezes two Dodges for either printed gender; attack-side Roulin, defender-side Roulin and ordinary Wushuang aggregate by maximum, never four or six.");
            s.Cold(); var paid = s.PayTwo(card, responder);
            Need(paid.Length == 2 && paid.Distinct().Count() == 2 &&
                s.E<RequiredResponseProgressEvent>().Where(item => item.ResolutionId == use.Id && item.ResponderSeat == responder)
                    .Select(item => (item.ResponseCount, item.RequiredResponseCount)).SequenceEqual([(1, 2), (2, 2)]) &&
                s.E<DamageAppliedEvent>().Length == 0 && s.E<CardUseFinishedEvent>().Count(item => item.ResolutionId == use.Id) == 1 &&
                s.E<CardUseDeclaredEvent>().Count(item => IsSlash(item.CardKind)) == 1,
                "Two distinct original Dodge entities really finish the sole physical Slash; cold response children never turn responses into uses or apply prevented damage: " + s.Diagnostic());
            foreach (var id in paid) s.AssertPaidOnce(id, responder, CardMoveReasons.ResponseFinished);
            s.AssertPaidOnce(card, incoming ? 1 : 0, CardMoveReasons.UseFinished); s.Cold();
        }

        var unrelated = Scene.Create("unrelated");
        unrelated.BeginAssisted(); unrelated.Reach(() => unrelated.P?.PlayerSeat == 0 && unrelated.P.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("request-option") == "target" && choice.Targets.SequenceEqual([2])));
        unrelated.Cold(); unrelated.Answer(choice => choice.Parameters.GetValueOrDefault("request-option") == "target" && choice.Targets.SequenceEqual([2]));
        unrelated.AtResponse(2); var third = unrelated.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardAttack is not null);
        Need(unrelated.Has(Skill) && third.Action is { ActorSeat: 1 } && third.TargetSeats.SequenceEqual([2]) &&
            third.CardAttack is { RequiredDodgeResponses: 1, SuccessfulDodgeResponses: 0 },
            "A live holder outside the actual source-target pair does not make an unrelated third-party physical Slash require two Dodges.");
        unrelated.Cold(); unrelated.Reach(() => unrelated.E<CardUseFinishedEvent>().Any(item => item.ResolutionId == third.Id)); unrelated.Play();
        var sole = unrelated.E<CardRespondedEvent>().Where(item => item.ResponderSeat == 2 && item.EffectiveCardKind == CardKind.Dodge).ToArray();
        Need(sole is [{ SourceSeat: 1 }] && unrelated.E<RequiredResponseProgressEvent>().All(item => item.ResolutionId != third.Id) &&
            unrelated.E<DamageAppliedEvent>().Length == 0 && unrelated.V(2).HandCount == 1,
            "The unrelated defender truly spends one Dodge and retains its second original entity; mere catalogue presence or a third holder creates no double-response progress.");
        unrelated.AssertPaidOnce(sole.Single().CardId, 2, CardMoveReasons.ResponseFinished); unrelated.Cold();
    }

    public static void FrozenIssuedResponseSurvivesSourceLossButNewSlashRechecksArmorCold()
    {
        var s = Scene.Create("loss"); s.Use("armor");
        Need(s.V(0) is { Hp: 4, Armor: 1 } && s.Has(Skill), "The actual target starts with conditional production Roulin and one real numerical Armor grant.");
        var first = s.BeginRequested(); var firstUse = s.Attack(first).Id; s.Cold();
        var firstDodge = s.Respond(); s.At(Watch);
        Need(s.V(0).Hp == 3 && !s.Has(Skill) && s.Attack(first).CardAttack?.RequiredDodgeResponses == 2 &&
            s.E<CardRespondedEvent>().Any(item => item.CardId == firstDodge && item.ResponderSeat == 0) &&
            s.G.CreateCardZoneDiagnostics().Single(item => item.CardId == firstDodge).Location == CardLocation.DiscardPile,
            "The real first-response completion child loses HP and withdraws the original conditional source after physical payment, while the issued Slash retains its frozen two-Dodge requirement.");
        s.Cold(); s.Continue(); s.AtResponse(0);
        Need(s.Attack(first).CardAttack is { RequiredDodgeResponses: 2, SuccessfulDodgeResponses: 1 } &&
            s.E<RequiredResponseProgressEvent>().Any(item => item.ResolutionId == firstUse && item.ResponseCount == 1 && item.RequiredResponseCount == 2),
            "Returning from the already-paid loss child must still request the second actual Dodge, rather than recomputing the old response set as one.");
        s.Cold(); var secondDodge = s.Respond(); s.Reach(() => s.E<CardUseFinishedEvent>().Any(item => item.ResolutionId == firstUse)); s.Play();
        Need(firstDodge != secondDodge && s.E<DamageAppliedEvent>().Length == 0 && s.V().Armor == 1,
            "Both original response costs finish the pre-loss defense without spending Armor or applying damage.");

        var next = s.BeginRequested(); var nextUse = s.Attack(next).Id;
        Need(s.Attack(next).CardAttack is { RequiredDodgeResponses: 1, SuccessfulDodgeResponses: 0 } && !s.Has(Skill),
            "A new actual Slash rechecks the now-missing skill and starts with the ordinary one-Dodge requirement.");
        s.Cold(); var ordinary = s.Respond(); s.Reach(() => s.E<CardUseFinishedEvent>().Any(item => item.ResolutionId == nextUse)); s.Play();
        Need(s.E<RequiredResponseProgressEvent>().All(item => item.ResolutionId != nextUse) && s.E<DamageAppliedEvent>().Length == 0,
            "One real new response suffices after source loss; the old frozen requirement cannot leak into another use.");

        s.Use("recover"); Need(s.V().Hp == 4 && s.Has(Skill), "An actual recovery restores the conditional production skill for future actions.");
        var third = s.BeginRequested(); var thirdUse = s.Attack(third).Id;
        Need(s.Attack(third).CardAttack?.RequiredDodgeResponses == 2, "The recovered source again requires two for a newly issued Slash.");
        var partial = s.Respond(); s.AtResponse(0); s.Cold();
        Need(s.Attack(third).CardAttack is { RequiredDodgeResponses: 2, SuccessfulDodgeResponses: 1 }, "The first original Dodge alone does not complete the recovered double defense.");
        s.Answer(choice => choice.Parameters.GetValueOrDefault("response") == "take-damage"); s.At(DamageWatch);
        Need(s.E<DamageAppliedEvent>() is [{ SourceSeat: 1, TargetSeat: 0, Amount: 1, ArmorPayment: { ArmorBefore: 1, ArmorAbsorbed: 1, HpLossAmount: 0 } }] &&
            s.V() is { Hp: 4, Armor: 0 } && s.E<RequiredResponseProgressEvent>().Where(item => item.ResolutionId == thirdUse)
                .Select(item => (item.ResponseCount, item.RequiredResponseCount)).SequenceEqual([(1, 2)]),
            "Declining the still-required second Dodge applies one complete native damage fact through Armor; the prior paid Dodge is neither refunded nor counted as two.");
        s.Cold(); s.Continue(); s.Reach(() => s.E<CardUseFinishedEvent>().Any(item => item.ResolutionId == thirdUse)); s.Play();
        foreach (var card in new[] { firstDodge, secondDodge, ordinary, partial }) s.AssertPaidOnce(card, 0, CardMoveReasons.ResponseFinished);
        foreach (var card in new[] { first, next, third }) s.AssertPaidOnce(card, 1, CardMoveReasons.UseFinished);
        Need(s.E<CardRespondedEvent>().Count(item => item.ResponderSeat == 0 && item.EffectiveCardKind == CardKind.Dodge) == 4 &&
            s.E<CardUseFinishedEvent>().Count(item => IsSlash(item.CardKind)) == 3,
            "Cold replay preserves all four separate original responses, three distinct physical Slash uses and one Armor payment exactly once."); s.Cold();
    }

    private static bool IsSlash(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Stack = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements, Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())), Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(item => item is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(item => item.Payload).OfType<T>().ToArray();
        public bool Has(string id) => V().Skills?.Any(item => item.ContentId == id) == true;
        public CardUseFrame Attack(int card) => G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardId == card && frame.CardAttack is not null);
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual S5 Roulin command rejected."); }
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Continue() => Answer(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Use(string id) { Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId)); Play(); }
        public void At(string id) => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == id && P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void AtResponse(int seat) => Reach(() => P is { Kind: DecisionKind.RespondDodge } prompt && prompt.PlayerSeat == seat);
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public int BeginRequested()
        {
            Accept(new UseProgramSkillCommand(0, Driver, "request", [], [1], G.Revision, P!.PromptId)); AtResponse(0);
            return G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardAttack is not null).CardId;
        }
        public int BeginPhysical(CardKind kind)
        {
            var action = G.GetHumanLegalActions().First(item => item.CardId is { } id && V().Hand.Any(card => card.Id == id && card.Kind == kind) && item.ConversionSource is null && item.TargetSeats.SequenceEqual([1]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, [1], G.Revision, P!.PromptId, action.PlayedCardKind)); AtResponse(1); return action.CardId.Value;
        }
        public void BeginAssisted() => Accept(new UseProgramSkillCommand(0, Driver, "third-party", [], [1], G.Revision, P!.PromptId));
        public int Respond()
        {
            Need(P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }, "Only the actual human defender answers a native response prompt.");
            var choice = P!.Choices.First(item => item.Cards.Count == 1 && item.Parameters.GetValueOrDefault("response") != "take-damage"); var card = choice.Cards.Single();
            Answer(item => item.Id == choice.Id); return card;
        }
        public int[] PayTwo(int card, int responder)
        {
            var use = Attack(card).Id; var before = E<CardRespondedEvent>().Count(item => item.ResponderSeat == responder && item.EffectiveCardKind == CardKind.Dodge);
            if (responder == 0) { Respond(); At(Watch); Cold(); Continue(); }
            Reach(() => E<RequiredResponseProgressEvent>().Any(item => item.ResolutionId == use && item.ResponseCount == 1 && item.RequiredResponseCount == 2));
            Need(Attack(card).CardAttack is { SuccessfulDodgeResponses: 1, RequiredDodgeResponses: 2 }, "The first actual paid response pauses with the second physical Dodge still required: " + Diagnostic());
            Cold(); AtResponse(responder); if (responder == 0) Respond();
            Reach(() => E<CardUseFinishedEvent>().Any(item => item.ResolutionId == use)); Play();
            return E<CardRespondedEvent>().Where(item => item.ResponderSeat == responder && item.EffectiveCardKind == CardKind.Dodge).Skip(before).Select(item => item.CardId).ToArray();
        }
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                Need(P?.PlayerSeat != 0 && G.State.Winner == Winner.None, "Unexpected actual S5 Roulin boundary: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded S5 Roulin boundary: " + Diagnostic());
        }
        public void AssertPaidOnce(int card, int seat, CardMoveReason finish) => Need(G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(seat) && move.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && move.Reason == finish) == 1,
            "Every real Slash or Dodge entity is paid and cleaned up once, including a cold-restored incomplete response.");
        public void Cold()
        {
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == State(restored), "Cold native replay preserves exact response counts, private choices, original materials, source lifetime and Armor receipts."); G = restored;
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(item => item.CardId).Distinct().Count() == zones.Count, "Every real entity remains in exactly one native zone.");
        }
        public string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Responses = E<CardRespondedEvent>(), Progress = E<RequiredResponseProgressEvent>(), Damage = E<DamageAppliedEvent>(),
            Attacks = G.ResolutionStack.OfType<CardUseFrame>().Select(item => new { item.Id, item.CardId, item.CardAttack }), LastThought = G.AiThoughts.LastOrDefault() });
        public static Scene Create(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            ShanheStartingCard[] baseCards = [new(CardKind.Slash, Suit.Spade, 2), new(CardKind.ThunderSlash, Suit.Club, 3)];
            var ownCards = baseCards.Concat(Enumerable.Range(0, 5).Select(i => new ShanheStartingCard(CardKind.Dodge, i % 2 == 0 ? Suit.Heart : Suit.Club, i + 4))).ToArray();
            var first = new ShanheCombatantDefinition(scene.EndsWith("female", StringComparison.Ordinal) ? Female : Peer, Hp: 18,
                ExtraSkillIds: scene == "outgoing-male" ? [Skill] : []) { StartingHand = scene.StartsWith("outgoing", StringComparison.Ordinal)
                    ? [new(CardKind.Dodge, Suit.Heart, 8), new(CardKind.Dodge, Suit.Club, 9)]
                    : [new(scene == "incoming-female" ? CardKind.FireSlash : CardKind.Slash, Suit.Spade, 8), new(CardKind.Slash, Suit.Club, 9), new(CardKind.Slash, Suit.Spade, 10)] };
            var setup = new ShanheBattleSetup(83, 1, "fixture-s5-roulin-" + scene, Owner, 4, 0, 0, 0, 0, [], ownCards, [],
                [first, new(Target, Hp: 18) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 11), new(CardKind.Dodge, Suit.Club, 12)] }, new(Target) { StartingHand = [] }], [], new string('a', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(game, registry, scene); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-roulin", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Skill, Wushuang }) b.AddSkill(Production.Value.GetSkill(id));
            object Pause() => new { op = "chooseOption", target = "owner", resultBind = "paid-response", options = new[] { new { id = "continue" } } };
            object A(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets, targetKind = "otherLiving", usesPerTurn = (int?)null, effects };
            var definitions = new object[] {
                new { id = Driver, revision = 1, modifiers = new[] { new { id = "native-range", query = "attackRange", operation = "add", value = 3, priority = 0 } },
                    activations = new[] { A("request", 1, new { op = "requestSlashByTarget", target = "selectedTarget", resultBind = "physical-request" }),
                        A("third-party", 1, new { op = "requestSlashAgainstChosenTarget", target = "selectedTarget", resultBind = "third-request" }),
                        A("armor", 0, new { op = "grantArmor", target = "owner", amount = 1 }), A("recover", 0, new { op = "recover", target = "owner", amount = 1 }) } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "first-native-dodge-completed", window = "cardResponseCompleted", ownerRelation = "actor", cardKinds = new[] { "dodge" }, optional = false,
                    usageScope = "game", usageLimit = 1, effects = scene == "loss" ? new object[] { new { op = "loseHp", target = "owner", amount = 1 }, Pause() } : [Pause()] } } },
                new { id = DamageWatch, revision = 1, triggers = new[] { new { id = "native-paid-armor", window = "afterDamageApplied", subject = "owner", damageOccurrence = "perDamage", optional = false, effects = new[] { Pause() } } } },
                new { id = HpGate, revision = 1, hpSkillGrants = new[] { new { id = "actual-full-health-source", skillId = Skill, minimumHp = 4 } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实肉林操作", description = "原生实体杀、请求、回复与护甲" }, [HpGate] = new { name = "真实HP来源", description = "实际体力改变生产肉林的有效来源" } };
            foreach (var id in new[] { Watch, DamageWatch }) labels[id] = new { name = "真实已付子窗口", description = "原生响应或伤害付款后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Watch, DamageWatch, scene == "loss" ? HpGate : Skill }; if (scene == "outgoing-male") own.Add(Wushuang);
            b.AddGeneral(new(Owner, "真实男性肉林本人", "", Driver, "qun", 4, own, GeneralGender.Male));
            b.AddGeneral(new(Peer, "真实男性对手", "", "standard:none", "wei", 20, [], GeneralGender.Male));
            b.AddGeneral(new(Female, "真实女性对手", "", "standard:none", "wei", 20, [], GeneralGender.Female));
            b.AddGeneral(new(Target, "真实第三角色", "", "standard:none", "wei", 20, [], GeneralGender.Male));
            b.AddDeck(new(Deck, "不补响应材料的小真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实双向肉林", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Peer, Female, Target], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
