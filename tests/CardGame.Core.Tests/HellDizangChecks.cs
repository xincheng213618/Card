using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HellDizangChecks
{
    private const string Owner = "fixture:hell-dizang-owner", Driver = "fixture:hell-dizang-driver",
        Quiet = "fixture:hell-dizang-quiet", Watch = "fixture:hell-dizang-watch", Mode = "fixture:hell-dizang";
    private const string WuliangDraw = "skill-program.hell:wuliang.Draw";

    public static void ActualDistanceFireDamageThresholdAndWuliangDrawChildrenCold()
    {
        var (game, registry) = Start(["hell:bufo", "hell:wuliang", "hell:diting"], protectedPeers: true, damageWatch: true);
        Reach(game, () => NativeDraw(game, WuliangDraw, 3));
        Need(V(game, 0).HandCount == 4 && Draws(game, WuliangDraw) == 0 &&
            V(game, 0).EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.OffensiveHorse) == 0 &&
            V(game, 0).EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.DefensiveHorse) == 0,
            "Actual appearance must abolish both mount slots and request exactly three extra cards before their physical draw.");
        game = Cold(game, registry);
        Continue(game);
        Reach(game, () => P(game)?.PlayerSeat == 0 && game.ResolutionStack.OfType<ProgramSkillFrame>()
            .Any(frame => frame.HellBufo is { NextTargetIndex: 1 }));
        var paid = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.HellBufo is not null).HellBufo!;
        Need(paid.Targets.SequenceEqual([1, 4]) && paid.Targets is IList<int> { IsReadOnly: true } &&
            E<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.Nature == DamageNature.Fire) == 1,
            "Bufo freezes only actual combat-distance-one seats and suspends after its first real damage for the complete child observer.");
        game = Cold(game, registry);
        Continue(game);
        Play(game);
        Need(V(game, 0).HandCount == 9 && Draws(game, WuliangDraw) == 3 &&
            E<HellBufoTargetFrozenEvent>(game).Select(e => e.TargetSeat).SequenceEqual([1, 4]) &&
            new[] { 1, 4 }.All(seat => V(game, seat).Hp == 19 && game.GetCombatDistance(0, seat) == 1) &&
            new[] { 2, 3 }.All(seat => V(game, seat).Hp == 20 && game.GetCombatDistance(0, seat) == 2),
            "The distance fire must affect both eligible others exactly once, excluding farther seats, while appearance draws three in addition to the initial four and ordinary two.");
        foreach (var amount in new[] { 1, 2, 3 })
        {
            var before = V(game, 0).Hp;
            Use(game, "self-" + amount, []);
            Play(game);
            Need(V(game, 0).Hp == before - (amount < 2 ? amount : amount - 1),
                "Bufo reduces exactly one point only for an actual received damage amount of at least two.");
        }
        Need(V(game, 0).Hp == 4, "The native one/two/three-damage threshold sequence must leave four health.");
        Use(game, "lose-two", []);
        Play(game);
        Need(V(game, 0).Hp == 2, "Health loss must bypass the damage-only Bufo reduction.");
        var hand = V(game, 0).HandCount;
        End(game);
        Reach(game, () => NativeDraw(game, WuliangDraw, 2));
        Need(V(game, 0).HandCount == hand && Draws(game, WuliangDraw) == 3,
            "Wuliang's ending draw must pause at the real two-card request before receiving any ending cards.");
        game = Cold(game, registry);
        Continue(game);
        Play(game);
        Need(V(game, 0).Hp == 3 && Draws(game, WuliangDraw) == 5 &&
            E<HellBufoTargetFrozenEvent>(game).GroupBy(e => e.ProgramFrameId).Count() == 2 &&
            E<NativeDrawAttemptDecidedEvent>(game).Count(e => e.RecipientSeat == 0 && e.RequestedCount == 3) == 1,
            "The next real own turn must heal to exactly three, repeat its distance fire and never repeat the original appearance draw.");
        _ = Cold(game, registry);
    }

    public static void FinalJudgmentAnySuitRankChoiceAndColdReplay()
    {
        var (game, registry) = Start(["hell:dayuan"]);
        Play(game);
        var cases = new[] { (Suit.Spade, 1), (Suit.Heart, 7), (Suit.Club, 12), (Suit.Diamond, 13) };
        foreach (var (suit, rank) in cases)
        {
            var oldResults = E<JudgmentResolvedEvent>(game).Count();
            Use(game, "judge", [2]);
            Reach(game, () => P(game)?.Kind == DecisionKind.ProgramJudgmentTrigger &&
                P(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate"));
            var judgment = game.ResolutionStack.OfType<JudgmentFrame>().Single();
            var printed = V(game, 2).Judgment.Single(card => card.Id == judgment.CardId);
            Need(printed is { Kind: CardKind.Dodge, Suit: Suit.Club, Rank: 7 } &&
                E<JudgmentResolvedEvent>(game).Count() == oldResults,
                "Dayuan must be offered before the original physical judgment has any final result.");
            Answer(game, choice => choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
            Reach(game, () => P(game)?.Choices.Count == 4 && P(game)!.Choices.All(choice => choice.Parameters.ContainsKey("suit")));
            game = Cold(game, registry);
            Answer(game, choice => choice.Parameters.GetValueOrDefault("suit") == suit.ToString());
            Need(P(game)?.Choices.Count == 13 && P(game)!.Choices.All(choice => choice.Parameters.ContainsKey("rank")) &&
                E<JudgmentResolvedEvent>(game).Count() == oldResults && !E<HellDayuanJudgmentOverriddenEvent>(game)
                    .Any(e => e.Receipt.JudgmentFrameId == judgment.Id),
                "Choosing a suit only must retain all thirteen rank choices without prematurely changing or finalizing the judgment.");
            game = Cold(game, registry);
            var before = State(game);
            var rejected = game.Submit(new AnswerPromptCommand(2, P(game)!.PromptId, P(game)!.Choices[0].Id, game.Revision));
            Need(!rejected.Accepted && State(game) == before,
                "Another actual seat cannot answer Dizang's still-owned final-rank choice.");
            Answer(game, choice => choice.Parameters.GetValueOrDefault("rank") == rank.ToString());
            Play(game);
            var receipt = E<HellDayuanJudgmentOverriddenEvent>(game).Single(e => e.Receipt.JudgmentFrameId == judgment.Id).Receipt;
            var result = E<JudgmentResolvedEvent>(game).Single(e => e.ResolutionId == judgment.Id);
            Need(receipt.CardId == printed.Id && receipt.Suit == suit && receipt.Rank == rank &&
                result.CardId == printed.Id && result.CardKind == CardKind.Dodge && result.Suit == suit && result.Rank == rank &&
                result.Succeeded == (suit is Suit.Heart or Suit.Diamond) &&
                game.CreateCardZoneDiagnostics().Single(card => card.CardId == printed.Id).Location == CardLocation.DiscardPile &&
                E<JudgmentReplacementResolvedEvent>(game).All(e => e.JudgmentResolutionId != judgment.Id),
                "All four suits and arbitrary legal ranks must become the native result of the same physical card, without creating a replacement entity.");
            game = Cold(game, registry);
        }
        var (zero, zeroRegistry) = Start(["hell:dayuan"], zeroJudgmentDeck: true);
        Play(zero);
        Need(zero.CreateCardZoneDiagnostics().All(card => card.Location != CardLocation.DrawPile),
            "The small legal physical deck must be exhausted by real initial dealing and the first normal draw.");
        var mengpo = zero.CreateSnapshot(0).Players.Single(player => player.GeneralId == "fixture:hell-zero-mengpo").Seat;
        Use(zero, "kill-for-zero-judgment", [mengpo]);
        Play(zero);
        Need(E<HellReturnSoulInjectedEvent>(zero).Length == 2 && zero.CreateCardZoneDiagnostics()
                .Where(card => card.Location == CardLocation.DrawPile).All(card => card.CardKind == CardKind.ReturnSoul),
            "A real Wanghun death must inject the only two draw-pile entities, carrying their native None/zero faces.");
        Use(zero, "judge", [Enumerable.Range(1, 4).First(seat => V(zero, seat).IsAlive)]);
        Reach(zero, () => P(zero)?.Kind == DecisionKind.ProgramJudgmentTrigger && P(zero)!.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate"));
        var nativeZero = zero.ResolutionStack.OfType<JudgmentFrame>().Single();
        var zeroFace = V(zero, nativeZero.TargetSeat).Judgment.Single(card => card.Id == nativeZero.CardId);
        Need(zeroFace is { Kind: CardKind.ReturnSoul, Suit: Suit.None, Rank: 0 },
            "The None/zero judgment must be a genuinely injected Soul entity rather than an illegal printed fixture recipe.");
        Answer(zero, choice => choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
        Reach(zero, () => P(zero)?.Choices.Count == 4);
        Answer(zero, choice => choice.Parameters.GetValueOrDefault("suit") == Suit.Diamond.ToString());
        zero = Cold(zero, zeroRegistry);
        Answer(zero, choice => choice.Parameters.GetValueOrDefault("rank") == "13");
        Play(zero);
        Need(E<JudgmentResolvedEvent>(zero).Single(e => e.ResolutionId == nativeZero.Id) is
                { CardKind: CardKind.ReturnSoul, Suit: Suit.Diamond, Rank: 13, Succeeded: true } &&
            E<HellDayuanJudgmentOverriddenEvent>(zero).Single(e => e.Receipt.JudgmentFrameId == nativeZero.Id).Receipt.CardId == zeroFace.Id,
            "Dayuan must also convert the native None/zero Soul judgment to a legal chosen suit/rank on the same physical entity.");
        _ = Cold(zero, zeroRegistry);
    }

    public static void AbolishedMountSlotsNativeRecastPaymentDrawAndCold()
    {
        foreach (var kind in new[] { CardKind.OffensiveHorse, CardKind.DefensiveHorse })
        {
            var (game, registry) = Start(["hell:diting"], mountDeck: kind);
            Play(game);
            var owner = V(game, 0);
            var card = owner.Hand.First();
            Need(card.Kind == kind && owner.EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.OffensiveHorse) == 0 &&
                owner.EquipmentSlotCapacities.GetValueOrDefault(EquipmentSlot.DefensiveHorse) == 0 &&
                game.GetCombatDistance(0, 2) == 1 && game.GetCombatDistance(2, 0) == 3 &&
                game.GetHumanLegalActions().Where(action => action.CardId == card.Id).All(action => action.Kind == LegalActionKind.Recast) &&
                game.GetHumanLegalActions().Any(action => action.CardId == card.Id && action.Kind == LegalActionKind.Recast),
                "Both real mount slots must remain abolished while actual outgoing/incoming distance modifiers and the native hand-mount recast action are enabled.");
            var hand = owner.HandCount;
            Accept(game, new RecastCardCommand(0, card.Id, game.Revision, P(game)!.PromptId));
            Reach(game, () => P(game)?.SkillPrompt?.SkillId == Watch && game.ResolutionStack.OfType<EquipmentRecastFrame>().Any() &&
                !game.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any());
            Need(V(game, 0).HandCount == hand - 1 && E<CardRecastEvent>(game).Count() == 0 &&
                game.CardMovements.Count(move => move.CardId == card.Id && move.Reason == CardMoveReasons.RecastDiscard) == 1,
                "A real mount recast must pay the sole original entity before suspending in its movement child, without drawing early.");
            game = Cold(game, registry);
            Continue(game);
            Reach(game, () => NativeDraw(game, CardMoveReasons.RecastDraw.Value, 1));
            Need(V(game, 0).HandCount == hand - 1 && game.ResolutionStack.OfType<EquipmentRecastFrame>().Single() is
                    { DrawApplied: false, NativeDraw.ReturnKind: NativeDrawAttemptReturnKind.EquipmentRecast } &&
                E<CardRecastEvent>(game).Count() == 0,
                "The paid native recast must next own a typed one-card draw request, before any replacement decision or physical draw.");
            game = Cold(game, registry);
            Continue(game);
            Play(game);
            Need(V(game, 0).HandCount == hand && E<CardRecastEvent>(game) is [{ ActorSeat: 0, DrawCount: 1 } recast] &&
                recast.CardId == card.Id && recast.CardKind == kind &&
                game.CardMovements.Count(move => move.CardId == card.Id && move.Reason == CardMoveReasons.RecastDiscard) == 1 &&
                game.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.RecastDraw) == 1 &&
                E<NativeDrawAttemptDecidedEvent>(game).Count(e => e.RequestedCount == 1 && e.RecipientSeat == 0) == 1,
                "Cold returns must complete one genuine recast and one actual draw without repeating its paid mount or request.");
            var before = State(game);
            var rejected = game.Submit(new RecastCardCommand(0, card.Id, game.Revision, P(game)!.PromptId));
            Need(!rejected.Accepted && State(game) == before, "The original discarded mount cannot be recast twice.");
            Use(game, "lose-diting", []);
            Play(game);
            Need(V(game, 0).EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.OffensiveHorse) == 0 &&
                V(game, 0).EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.DefensiveHorse) == 0 &&
                game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Recast),
                "Actual skill loss removes the mount-recast permission without silently restoring abolished native equipment slots.");
            _ = Cold(game, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(string[] skills, bool protectedPeers = false,
        bool damageWatch = false, CardKind? mountDeck = null, bool zeroJudgmentDeck = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(skills, protectedPeers, damageWatch, mountDeck, zeroJudgmentDeck));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 5, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:boss",
            ModeId = Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 16
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, () => P(game) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        return (game, registry);
    }
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat).PendingDecision)
        .FirstOrDefault(prompt => prompt is not null);
    private static T[] E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(entry => entry.Payload).OfType<T>().ToArray();
    private static int Draws(GameEngine game, string reason) => game.CardMovements.Count(move =>
        move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) && move.Reason.Value == reason);
    private static bool NativeDraw(GameEngine game, string reason, int count) => P(game)?.PlayerSeat == 0 &&
        game.ResolutionStack.OfType<NativeDrawAttemptFrame>().LastOrDefault() is { } frame &&
        frame.Context.Reason.Value == reason && frame.Context.RequestedCount == count;
    private static void Answer(GameEngine game, Func<PromptChoice, bool> matches)
    {
        var prompt = P(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(matches).Id, game.Revision));
    }
    private static void Continue(GameEngine game) => Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue" ||
        choice.Parameters.GetValueOrDefault("option") == "continue" || choice.Id.Value.Contains("continue", StringComparison.Ordinal));
    private static void Use(GameEngine game, string activation, IReadOnlyList<int> targets) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], targets, game.Revision, P(game)!.PromptId));
    private static void End(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game) => Reach(game, () => P(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> done)
    {
        for (var step = 0; step < 200; step++)
        {
            if (done()) return;
            if (P(game) is { PlayerSeat: 0 } prompt)
            {
                var choice = prompt.Choices.FirstOrDefault(choice => choice.Id.Value.Contains("continue", StringComparison.Ordinal)) ??
                    prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-skip") ??
                    prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                if (choice is not null)
                { Accept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)); continue; }
                if (prompt.Kind == DecisionKind.PlayCard) { End(game); continue; }
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Dizang's bounded native boundary was not reached: " + SnapshotJson.Serialize(game.CreateSnapshot(0)));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "Native Dizang command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 5).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(State(restored) == State(game), "Cold JSON replay preserves all private views, exact physical payments and typed pending returns.");
        return restored;
    }

    private sealed class Fixture(string[] skills, bool protectedPeers, bool damageWatch, CardKind? mountDeck,
        bool zeroJudgmentDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("hell-dizang-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            string Read(string suffix, string bundle = "hell-dizang")
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix)!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            void Add(SkillProgramCatalog catalog)
            {
                foreach (var (id, program) in catalog.Programs)
                    builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id],
                        Tags = zeroJudgmentDeck && id is Driver or Watch ? SkillTag.Awakening :
                            id.StartsWith("hell:") && id != "hell:dayuan" ? SkillTag.Locked : SkillTag.None });
            }
            Add(SkillProgramCatalog.Load(Read("rules.json"), Read("presentation.json")));
            if (zeroJudgmentDeck)
            {
                var mengpo = SkillProgramCatalog.Load(Read("rules.json", "hell-mengpo"), Read("presentation.json", "hell-mengpo"));
                const string wanghun = "hell:wanghun";
                builder.AddSkill(new(wanghun, mengpo.Presentations[wanghun].Name, mengpo.Presentations[wanghun].Description)
                { Program = mengpo.Programs[wanghun], ProgramPresentation = mengpo.Presentations[wanghun], Tags = SkillTag.Locked });
                builder.AddCard(new("fixture:hell-zero-soul", "回魂", "锦囊牌", "Native zero-face Soul entity.", CardKind.ReturnSoul));
            }
            object Act(string id, int targets, object[] effects) => new
            { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            object[] acts = [
                .. Enumerable.Range(1, 3).Select(amount => Act("self-" + amount, 0, [new { op = "damage", target = "owner", amount }])),
                Act("lose-two", 0, [new { op = "loseHp", target = "owner", amount = 2 }]),
                Act("judge", 1, [new { op = "startJudgment", target = "selectedTarget", judgmentReason = "fixture.hell-dayuan", resultBind = "judge", visibility = "public" },
                    new { op = "moveBoundCards", target = "owner", sourceBind = "judge", destination = "discardPile" }]),
                Act("kill-for-zero-judgment", 1, [new { op = "loseHp", target = "selectedTarget", amount = 20 },
                    new { op = "grantSkills", target = "owner", skillIds = new[] { "hell:dayuan" } }]),
                Act("lose-diting", 0, [new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { "hell:diting" }, sourceBind = Quiet }])
            ];
            var watches = new List<object>
            {
                new { id = "before-native-draw", window = "nativeDrawStarting", subject = "owner", optional = false,
                    effects = new object[] { new { op = "chooseOption", target = "owner", resultBind = "draw-continue", options = new[] { new { id = "continue" } } } } },
                new { id = "after-paid-recast", window = "cardsMoved", subject = "owner", sourceZones = new[] { "hand" },
                    movementReasons = new[] { CardMoveReasons.RecastDiscard.Value }, movementOccurrence = "perOwnerBatch", optional = false,
                    usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "chooseOption", target = "owner", resultBind = "cost-continue", options = new[] { new { id = "continue" } } } } }
            };
            if (damageWatch) watches.Add(new { id = "after-first-native-fire", window = "afterDamageApplied", subject = "any",
                damageOccurrence = "perDamage", optional = false, usageScope = "game", usageLimit = 1,
                effects = new object[] { new { op = "chooseOption", target = "owner", resultBind = "fire-continue", options = new[] { new { id = "continue" } } } } });
            // The generic team adapter deliberately initializes four health.
            // Prepare the intended stress-fixture health with real rule operations.
            object PrepareHealth(int bonus, int hp) => new
            {
                id = "prepare-native-fixture-health", window = "gameStarting", subject = "owner", optional = false,
                priority = 20, usageScope = "game", usageLimit = 1,
                effects = new object[]
                {
                    new { op = "changeMaximumHp", target = "owner", amount = bonus },
                    new { op = "recoverTo", target = "owner", numberExpression = "integerConstant", minimumValue = hp, clampToMaxHp = true }
                }
            };
            var definitions = new object[]
            {
                new { id = Driver, revision = 1, activations = acts,
                    triggers = new[] { PrepareHealth(4, 8) },
                    modifiers = new[]
                    {
                        new { id = "retain-fixture-hand", query = "handLimit", operation = "add", value = 30, priority = 0 }
                    } },
                new { id = Quiet, revision = 1,
                    modifiers = protectedPeers ? new[] { new { id = "protect-farther-fixture-seat", query = "incomingDistance", operation = "add", value = 1, priority = 0 } } : [],
                    triggers = new object[] { PrepareHealth(16, 20), new { id = "quiet-play", window = "afterNormalDraw", subject = "owner", optional = false,
                        effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Watch, revision = 1, triggers = watches }
            };
            Add(SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, Quiet, Watch }.ToDictionary(id => id, id => id == Watch
                        ? (object)new { name = id, description = "Small native Dizang fixture.", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                        : new { name = id, description = "Small native Dizang fixture." }) })));
            builder.AddGeneral(new(Owner, "地藏王原生夹具", "", Driver, "qun", 8,
                [.. skills.Where(id => !zeroJudgmentDeck || id != "hell:dayuan"), Watch]));
            var peers = Enumerable.Range(1, 4).Select(index => zeroJudgmentDeck && index == 4
                ? "fixture:hell-zero-mengpo" : "fixture:hell-dizang-peer-" + index).ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, peer, "",
                peer == "fixture:hell-zero-mengpo" ? "hell:wanghun" : Quiet, "wei", 20));
            var cardId = mountDeck == CardKind.OffensiveHorse ? "standard:offensive_horse" :
                mountDeck == CardKind.DefensiveHorse ? "standard:defensive_horse" : "standard:dodge";
            const string deck = "fixture:hell-dizang-deck";
            builder.AddDeck(new(deck, "Homogeneous physical fixture deck", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, zeroJudgmentDeck ? 22 : 100).Select(_ => new ContentDeckPhysicalCard(cardId, Suit.Club, 7)).ToArray() });
            builder.AddMode(new(Mode, "原生地藏王规则", 5, 5, new Dictionary<string, int>(), deck, 5,
                [Owner, .. peers], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:boss"] = 1, ["team:allies"] = 4 }));
        }
    }
}
