using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheChengxiongChecks
{
    private const string Skill = "shanhe:s5-chengxiong-14055", Boss = "shanhe:s5-gaoshun-chengxiong", BaseBoss = "shanhe:s5-gaoshun-xianzhen";
    private const string Owner = "fixture:chengxiong-owner", Peer = "fixture:chengxiong-peer", Driver = "fixture:chengxiong-driver";
    private const string Watch = "fixture:chengxiong-paid", Gate = "fixture:chengxiong-hp-grant", Loss = "fixture:chengxiong-source-loss";
    private const string Startup = "fixture:chengxiong-startup", Quiet = "fixture:chengxiong-quiet", Deck = "fixture:chengxiong-deck";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-chengxiong-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new(Boss)], [], new string('c', 64))));

    public static void RealRoundParityExtraTurnsProgramDamageAndZeroPaymentsCold()
    {
        VerifyLoaderBoundaries();
        var startup = Scene.Start("startup");
        Need(startup.Round == 0 && startup.Events<RoundStartedEvent>().Length == 0 &&
            startup.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 0, SourceLess: false, Amount: 1 }] && startup.Modifiers().Length == 0,
            "An actual GameStarting program damage at native round zero receives neither odd nor even modification.");
        startup.Cold(); startup.Continue(); startup.Play();
        Need(startup.Round == 1 && startup.Events<RoundStartedEvent>().Length == 1,
            "The paid startup child returns to the genuine first-round boundary, without inventing a prior positive round."); startup.Cold();

        var s = Scene.Start("rounds"); var round = s.Round; var turn = s.G.State.TurnNumber;
        VerifyNativeProgramAiEstimate(s, 2);
        var slash = s.PlayCard(CardKind.Slash, [2]); s.Use("one", [2]); s.Use("source-less");
        Need(round == 1 && s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(2, 2), (2, 2), (0, 1)]) &&
            s.Modifiers().Length == 2 && s.Modifiers().All(e => e.Source.OwnerSeat == 0 && e.BaseAmount == 1 && e.ModifiedAmount == 2) &&
            s.Modifiers().Any(e => e.CardKind == CardKind.Slash) && s.Modifiers().Any(e => e.CardKind is null) &&
            s.Events<DamageAppliedEvent>().Last().SourceLess,
            "Odd-round source damage adds one to a genuine physical Slash and an actual cardless Damage instruction, while a real source-less received hit has no source to borrow.");
        s.AssertPaidOnce(slash); s.Cold(); s.Use("extra-turn"); s.EndPlay(); s.Play();
        Need(s.G.State.TurnNumber > turn && s.Round == round && s.Events<TurnStartedEvent>().Last().ActorSeat == 0,
            "A genuine extra actual turn changes the turn number without changing the round parity.");
        s.PlayCard(CardKind.ThunderSlash, [2]);
        Need(s.Events<DamageAppliedEvent>().Last() is { Amount: 2, Nature: DamageNature.Thunder },
            "The extra turn still uses the odd-round bonus, including elemental damage."); s.Cold();
        s.EndPlay(); s.Play(); Need(s.Round == 2, "The bounded quiet opponents reach the real second normal round.");
        s.Use("armor-two", [2]); VerifyNativeProgramAiEstimate(s, 0);
        var beforeHits = s.Events<DamageAppliedEvent>().Length; var beforeAfter = s.Events<AfterDamageEvent>().Length;
        var hp = s.V(2).Hp; var action = s.Action(CardKind.Slash, [2]); var before = s.State();
        var prediction = s.G.PredictPublicActionDamage(0, action, 2);
        Need(prediction is { DamageAmount: 0, ArmorAbsorbed: 0, HpLossAmount: 0, ArmorAfter: 2 } && prediction.HpAfter == hp && s.State() == before,
            "A pure public second-round estimate reduces the actual one-point Slash to zero before Armor, without advancing rules: " + JsonSerializer.Serialize(prediction));
        var zero = s.Begin(action); s.Play();
        Need(s.V(2).Hp == hp && s.V(2).Armor == 2 && s.Events<DamageAppliedEvent>().Length == beforeHits &&
            s.Events<AfterDamageEvent>().Length == beforeAfter && s.Modifiers().Last() is { BaseAmount: 1, ModifiedAmount: 0, CardKind: CardKind.Slash },
            "The real even-round subtraction to zero consumes no Armor or HP and creates neither a zero-valued damage payment nor an after-damage trigger.");
        s.AssertPaidOnce(zero); s.Cold(); s.Use("two", [2]);
        Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 2, Amount: 1,
                ArmorPayment: { ArmorBefore: 2, ArmorAbsorbed: 1, ArmorAfter: 1, HpLossAmount: 0 } } && s.V(2).Hp == hp &&
            s.Modifiers().Last() is { CardKind: null, BaseAmount: 2, ModifiedAmount: 1 },
            "The same signed rule subtracts from actual multi-point program damage once, retaining the one-point damage fact while Armor absorbs its HP payment."); s.Cold();
    }

    public static void NativeElementalPropagationRechecksEachRecipientAndLiveSourceLossCold()
    {
        // These amounts intentionally retain the existing local weak/far-source
        // propagation model, not an undocumented original-game Chengxiong FAQ.
        foreach (var scene in new[] { "chain", "chain-loss", "chain-unowned" })
        {
            var s = Scene.Start(scene); var a = s.PlayCard(CardKind.IronChain, [1, 2]); var b = s.PlayCard(CardKind.IronChain, [3]);
            Need(new[] { 1, 2, 3 }.All(seat => s.V(seat).IsChained), "Two original Iron Chain cards link the three real recipients.");
            var card = s.BeginCard(CardKind.ThunderSlash, [1]);
            if (scene == "chain-loss")
            {
                s.AtSkill(Loss);
                Need(s.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 2 }] && s.V().Hp == 19 &&
                    s.V().Skills?.Any(skill => skill.ContentId == Skill) != true &&
                    s.Events<HpConditionalSkillGrantChangedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == Skill && !e.Added) == 1,
                    "The real origin pays its odd-round damage before a legitimate HP child withdraws the exact derived source.");
                s.Cold(); s.Continue();
            }
            int[] amounts = scene == "chain" ? [2, 3, 4] : scene == "chain-loss" ? [2, 2, 2] : [1, 1, 1];
            for (var index = 0; index < 3; index++)
            {
                s.AtDamage();
                Need(s.Events<DamageAppliedEvent>().Length == index + 1 && s.Events<DamageAppliedEvent>().Last() is
                        { SourceSeat: 0, SourceLess: false, Nature: DamageNature.Thunder } hit && hit.TargetSeat == index + 1 && hit.Amount == amounts[index],
                    "Native propagation inherits the preceding finalized segment and rechecks only the currently live source: " + s.Diagnostic());
                s.Cold(); s.Continue();
            }
            s.Play(); var modifiers = s.Modifiers();
            Need(s.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual(amounts) &&
                s.Events<AfterDamageEvent>().Select(e => e.Amount).SequenceEqual(amounts) &&
                s.Events<ChainedDamagePropagatedEvent>().Length == 2 && s.Events<RecipientScopedDamageBaseEvent>().Length == 0 &&
                s.Events<RecipientScopedDamageAdvancedEvent>().Length == 0 &&
                modifiers.Length == (scene == "chain" ? 3 : scene == "chain-loss" ? 1 : 0) &&
                modifiers.All(e => e.ModifiedAmount == e.BaseAmount + 1) &&
                modifiers.GroupBy(e => (e.ResolutionId, e.TargetSeat)).All(group => group.Count() == 1),
                "There is no new parity-specific propagation scope: odd active source uses 2/3/4, loss retains already-paid 2/2/2, and mere registration leaves 1/1/1.");
            foreach (var id in new[] { a, b, card }) s.AssertPaidOnce(id); s.Cold();
            if (scene == "chain-loss")
            {
                s.Use("recover-one"); s.Use("one", [2]);
                Need(s.V().Hp == 20 && s.Events<DamageAppliedEvent>().Last().Amount == 2 && s.Modifiers().Length == 2 &&
                    s.Events<HpConditionalSkillGrantChangedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == Skill && e.Added) == 2,
                    "A real later recovery restores the conditional source for future damage, without replaying an already-paid chain segment."); s.Cold();
            }
        }
        var even = Scene.Start("chain-even"); even.EndPlay(); even.Play(); Need(even.Round == 2, "The zero-origin chain uses a genuine even round.");
        even.PlayCard(CardKind.IronChain, [1, 2]); even.PlayCard(CardKind.IronChain, [3]); even.Use("armor-two", [1]);
        var paid = even.PlayCard(CardKind.FireSlash, [1]);
        Need(even.Modifiers() is [{ BaseAmount: 1, ModifiedAmount: 0 }] && even.Events<DamageAppliedEvent>().Length == 0 &&
            even.Events<AfterDamageEvent>().Length == 0 && even.Events<ChainedDamagePropagatedEvent>().Length == 0 &&
            even.Events<RecipientScopedDamageBaseEvent>().Length == 0 && even.V(1) is { Hp: 20, Armor: 2 } &&
            new[] { 1, 2, 3 }.All(seat => even.V(seat).IsChained),
            "A true first elemental recipient reduced to zero neither starts propagation nor pays Armor or falsely unchains the table.");
        even.AssertPaidOnce(paid); even.Cold();

        var middle = Scene.Start("chain-even-middle"); middle.EndPlay(); middle.Play();
        Need(middle.Round == 2, "The unscoped middle-zero regression reaches a real even round through native turn progression.");
        middle.PlayCard(CardKind.IronChain, [1, 2]); middle.PlayCard(CardKind.IronChain, [3]); middle.Use("armor-two", [2]);
        var wine = middle.PlayCard(CardKind.Alcohol); var origin = middle.BeginCard(CardKind.FireSlash, [1]); middle.AtDamage();
        Need(middle.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 1, ArmorPayment: null }] &&
            middle.Modifiers() is [{ TargetSeat: 1, BaseAmount: 2, ModifiedAmount: 1 }] && middle.Events<RecipientScopedDamageBaseEvent>().Length == 0 &&
            new[] { 1, 2, 3 }.All(seat => !middle.V(seat).IsChained) &&
            middle.Events<IronChainStateChangedEvent>().Where(e => !e.IsChained).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]),
            "Actual Alcohol supplies two damage before even-round subtraction; the positive origin resets the real three-person chain immediately, without creating a recipient scope that could restore a positive base.");
        middle.Cold(); middle.Continue(); middle.Play();
        Need(middle.Modifiers().Select(e => (e.TargetSeat, e.BaseAmount, e.ModifiedAmount)).SequenceEqual([(1, 2, 1), (2, 1, 0)]) &&
            middle.Events<DamageAppliedEvent>() is [{ TargetSeat: 1, Amount: 1, ArmorPayment: null }] &&
            middle.Events<AfterDamageEvent>() is [{ TargetSeat: 1, Amount: 1 }] &&
            middle.Events<ChainedDamagePropagatedEvent>() is [{ SourceSeat: 0, FromSeat: 1, TargetSeat: 2, Amount: 1 }] &&
            middle.Events<RecipientScopedDamageBaseEvent>().Length == 0 && middle.Events<RecipientScopedDamageAdvancedEvent>().Length == 0 &&
            middle.V(2) is { Hp: 20, Armor: 2 } && middle.V(3) is { Hp: 20, Armor: 0, IsChained: false } && !middle.V().HasAlcoholEffect,
            "The second unscoped recipient finalizes to zero without paying its real Armor or publishing damage/after-damage; the third recipient loses no HP and receives no zero propagation, while its chain reset was already paid at the positive origin.");
        middle.AssertPaidOnce(wine); middle.AssertPaidOnce(origin); middle.Cold();
    }

    public static void ActualChengxiongGaoshunRecipesAndEarlyReplacementsRemainIsolatedCold()
    {
        VerifyActualRecipes();
        foreach (var scene in new[] { "jueqing", "maximum" })
        {
            var s = Scene.Start(scene); s.Use("armor-four", [2]); s.PlayCard(CardKind.IronChain, [1, 2]); s.PlayCard(CardKind.Alcohol);
            var action = s.Action(CardKind.FireSlash, [2]); var before = s.State();
            var prediction = s.G.PredictPublicActionDamage(0, action, 2);
            Need(prediction is { DamageAmount: 0, ArmorAbsorbed: 0, HpLossAmount: 2, ArmorAfter: 4 } && s.State() == before,
                "The actual public replacement estimate preserves the Alcohol card layer of two, before the general additional +1 and Armor: " + JsonSerializer.Serialize(prediction));
            var card = s.Begin(action); s.Play();
            Need(s.V(2) is { Hp: 18, Armor: 4, IsChained: true } && s.V(1).IsChained &&
                s.Events<DamageAppliedEvent>().Length == 0 && s.Events<AfterDamageEvent>().Length == 0 && s.Modifiers().Length == 0 &&
                s.Events<ChainedDamagePropagatedEvent>().Length == 0 &&
                (scene == "jueqing" ? s.V(2).MaxHp == 20 && s.Events<DamageReplacedWithHpLossEvent>() is [{ TargetSeat: 2, Amount: 2 }] :
                    s.V(2).MaxHp == 18 && s.Events<CardDamageReplacedWithMaximumHpLossEvent>() is [{ Receipt.TargetSeat: 2, Receipt.Amount: 2 }]),
                "Real Jueqing or maximum-HP replacement exits before general source-parity damage, preserving the original two-point card layer without fake damage, chain or Armor receipts.");
            s.AssertPaidOnce(card); s.Cold();
        }
    }

    private static void VerifyNativeProgramAiEstimate(Scene scene, int expectedDamage)
    {
        // Invoke the existing pure context factories, as ArmorAiChecks does;
        // source ownership and visibility come from the real engine, never a
        // manufactured bonus delegate or a mutated snapshot/runtime state.
        var before = scene.State(); var view = scene.G.CreateSnapshot(0);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.G)!;
        var contextFactory = typeof(GameEngine).GetMethod("CreateProgramAiPublicContext", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(CharacterState)], null)!;
        var actorFactory = typeof(GameEngine).GetMethod("CreateAiSkillContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var publicContext = (ProgramAiPublicContext)contextFactory.Invoke(scene.G, [players[0]])!;
        var source = (PlayerSkillContext)actorFactory.Invoke(scene.G, [players[0], 0, view])!;
        var target = (PlayerSkillContext)actorFactory.Invoke(scene.G, [players[2], 0, view])!;
        var program = scene.Registry.GetSkill(Driver).Program!;
        var effects = program.Activations.Single(activation => activation.Id == "one").Effects;
        var hint = ProgramCompositionAi.Estimate(effects, source, publicContext: publicContext with { SelectedTarget = target }).Hint;
        var result = hint.PredictTarget(scene.V(2).Hp, scene.V(2).MaxHp, scene.V(2).Armor);
        var received = ProgramCompositionAi.Estimate(program.Activations.Single(activation => activation.Id == "source-less").Effects,
            source, publicContext: publicContext).Hint;
        Need(hint.TargetDamage == expectedDamage && hint.TargetDirectHpLoss == 0 && result.DamageAmount == expectedDamage &&
            result.HpLossAmount == Math.Max(0, expectedDamage - scene.V(2).Armor) && received.OwnerDamage == 1 &&
            view.Players.Where(player => player.Seat != 0).All(player => player.Hand.Count == 0) && scene.State() == before,
            "The native program AI's actual public source context estimates odd Damage2 or even Damage0, preserves true source-less Damage1 and never reads foreign hands or changes the game: " +
            JsonSerializer.Serialize(new { scene.Round, Expected = expectedDamage, hint.TargetDamage, hint.TargetDirectHpLoss, Prediction = result, received.OwnerDamage }));
    }

    private static void VerifyActualRecipes()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(3, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(node => node.Encounters ?? []).All(encounter => encounter.Enemies.All(enemy => enemy.GeneralId != Boss && enemy.GeneralId != BaseBoss)),
            "The prior edition does not import either newly configured S5 Gaoshun encounter.");
        foreach (var difficulty in new[] { 1, 2, 3 })
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(difficulty, StandardShanheCampaignCatalog.Returning2026EditionId);
            var node = definition.Nodes.Single(value => value.Id == "hulao"); var hard = difficulty == 3;
            var encounter = node.Encounters!.Single(value => value.Id == (hard ? "hulao:gaoshun-chengxiong-s5" : "hulao:gaoshun-s5"));
            Need(encounter.Enemies is [{ MaxHpBonus: 0, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] &&
                (encounter.Allies?.Count ?? 0) == 0,
                "The actual locally selected base or Chengxiong form retains raw 4/4, with no invented growth, ally or fixed starting gear.");
            var setup = new ShanheBattleSetup(83, 1, node.Id, "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, encounter.Allies ?? [], new string('c', 64));
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            Need(registry.Generals.ContainsKey(hard ? Boss : BaseBoss) && !registry.Generals.ContainsKey(hard ? BaseBoss : Boss) &&
                registry.Skills.ContainsKey(Skill) == hard, "Only the actual required Gaoshun package is imported by this encounter recipe.");
            var result = game.Submit(new StartGameCommand()); Need(result.Accepted, result.Error?.Message ?? "The actual parity Gaoshun battle did not start.");
            var own = game.CreateSnapshot(0).Players[1];
            Need(own.GeneralId == (hard ? Boss : BaseBoss) && own is { Hp: 4, MaxHp: 4, Armor: 0, HandCount: 4 } && own.Equipment.Count == 0 &&
                own.Skills?.Any(skill => skill.ContentId == "boundary:xianzhen-current") == true &&
                own.Skills?.Any(skill => skill.ContentId == "boundary:jinjiu-current") == true &&
                (own.Skills?.Any(skill => skill.ContentId == Skill) == true) == hard &&
                !game.Events.Any(item => item.Payload is DamageAppliedEvent),
                "Actual production startup displays the selected printed skills and raw healthy four-point NPC, without initialization masquerading as damage.");
            var before = State(game); game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Need(State(game) == before, "The actual production encounter, independently selected alias, printed programs and initial material replay cold.");
        }
        var bare = new ShanheBattleSetup(83, 1, "fixture-parity-cache", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new("classic:gao-shun")], [], new string('c', 64));
        foreach (var newFirst in new[] { false, true })
        {
            var ordinary = bare with { PlayerGeneralId = newFirst ? "classic:gao-shun" : "classic:guan-yu" };
            var enabled = ordinary with { ExtraSkillIds = [Skill] };
            var first = StandardShanheCampaignCatalog.CreateBattleRegistry(newFirst ? enabled : ordinary);
            var second = StandardShanheCampaignCatalog.CreateBattleRegistry(newFirst ? ordinary : enabled);
            var normal = newFirst ? second : first; var extended = newFirst ? first : second;
            Need(!normal.Skills.ContainsKey(Skill) && !normal.Generals.ContainsKey(Boss) && extended.Skills.ContainsKey(Skill) &&
                normal.ContentHash != extended.ContentHash &&
                ReferenceEquals(normal, StandardShanheCampaignCatalog.CreateBattleRegistry(ordinary)) &&
                ReferenceEquals(extended, StandardShanheCampaignCatalog.CreateBattleRegistry(enabled)),
                "Even the same ordinary roster and campaign hash distinguishes actual parity-source recipes in both cache-warming orders.");
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        var rules = Resource("shanhe-s5-chengxiong.rules.json"); var presentation = Resource("shanhe-s5-chengxiong.presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        void Reject(Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(rules)!; change(copy["skills"]![0]!["damageModifiers"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Signed source-round modifiers reject zero/out-of-range amounts and unrelated scopes, card families or damage layers.");
        }
        Reject(m => m["amount"] = 0); Reject(m => m["amount"] = 21); Reject(m => m["amount"] = -21);
        Reject(m => m["sourceScope"] = "ownerUsed"); Reject(m => m["cardKinds"] = new JsonArray("slash")); Reject(m => m["damageLayer"] = "cardDamage");
        var boundary = JsonNode.Parse(rules)!;
        boundary["skills"]![0]!["damageModifiers"]![0]!["amount"] = -20;
        _ = SkillProgramCatalog.Load(boundary.ToJsonString(), presentation);
        var previous = JsonNode.Parse(rules)!; var ordinary = previous["skills"]![0]!["damageModifiers"]![0]!.AsObject();
        ordinary["condition"] = "always"; ordinary["sourceScope"] = "ownerUsed"; ordinary["cardKinds"] = new JsonArray("slash"); ordinary["amount"] = 1;
        _ = SkillProgramCatalog.Load(previous.ToJsonString(), presentation); ordinary["amount"] = -1; var oldRejected = false;
        try { _ = SkillProgramCatalog.Load(previous.ToJsonString(), presentation); } catch (InvalidOperationException) { oldRejected = true; }
        Need(oldRejected, "A positive old OwnerUsed modifier remains legal, while the additive signed capability cannot leak negative amounts into its old contract.");
    }
    private static string Resource(string suffix)
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Facts = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
        game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(item => item.Payload).OfType<T>().ToArray();
        public int Round => Events<RoundStartedEvent>().LastOrDefault()?.RoundNumber ?? 0;
        public ProgramCardDamageModifiedEvent[] Modifiers() => Events<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId == Skill).ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "The native parity command was rejected."); }
        public void Continue() => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue").Id, G.Revision));
        public void Use(string id, int[]? targets = null) { Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId)); Play(); }
        public LegalAction Action(CardKind kind, int[] targets) => G.GetHumanLegalActions().First(action => action.CardId is { } id &&
            V().Hand.Any(card => card.Id == id && card.Kind == kind) && action.TargetSeats.SequenceEqual(targets) && action.Kind != LegalActionKind.Recast && action.ConversionSource is null);
        public int Begin(LegalAction action)
        {
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources }); return action.CardId.Value;
        }
        public int BeginCard(CardKind kind, int[] targets) => Begin(Action(kind, targets));
        public int PlayCard(CardKind kind, int[]? targets = null) { var card = Begin(Action(kind, targets ?? [])); Play(); return card; }
        public void AtSkill(string skill) => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill);
        public void AtDamage() => AtSkill(Watch);
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        private void Reach(Func<bool> done)
        {
            for (var step = 0; step < 200; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                Need(P?.PlayerSeat != 0 && G.State.Winner == Winner.None, "Unexpected parity boundary: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded source-parity continuation was not reached: " + Diagnostic());
        }
        public string Diagnostic() => JsonSerializer.Serialize(new { Round, G.State.TurnNumber,
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Damage = Events<DamageAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount, e.Nature }), Modifiers = Modifiers(),
            Source = new { V().Hp, Skills = V().Skills?.Select(skill => skill.ContentId) }, Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) });
        public void AssertPaidOnce(int card) => Need(G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
            "The exact original card pays once and performs one native finished-use cleanup.");
        public string State() => ShanheChengxiongChecks.State(G);
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves real round parity, signed per-recipient modifiers, live source grants, Armor and original physical payments.");
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Each real card keeps one native zone.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            string[] extras = scene == "chain-loss" ? [Watch, Gate, Loss] : scene == "startup" ? [Watch, Startup] :
                scene == "jueqing" ? [Watch, Skill, "classic:jueqing"] : scene == "maximum" ? [Watch, Skill, "shanhe:xiaoming"] :
                scene == "chain-unowned" ? [Watch] : [Watch, Skill];
            if (scene == "startup") extras = extras.Append(Skill).ToArray();
            ShanheStartingCard[] cards = [new(CardKind.Slash, Suit.Spade, 2), new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Spade, 4),
                new(CardKind.ThunderSlash, Suit.Spade, 5), new(CardKind.ThunderSlash, Suit.Club, 6), new(CardKind.FireSlash, Suit.Heart, 7),
                new(CardKind.IronChain, Suit.Club, 8), new(CardKind.IronChain, Suit.Spade, 9), new(CardKind.Alcohol, Suit.Club, 10)];
            var setup = new ShanheBattleSetup(83, 1, "fixture-chengxiong-" + scene, Owner, 20, 0, 0, 0, 0, extras, cards, [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [], new string('c', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry); result.Accept(new StartGameCommand());
            if (scene == "startup") result.AtDamage(); else result.Play(); return result;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-chengxiong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Skill, "classic:jueqing", "shanhe:xiaoming" }) builder.AddSkill(Production.Value.GetSkill(id));
            object Pause(string name) => new { op = "chooseOption", target = "owner", resultBind = name, options = new[] { new { id = "continue" } } };
            object A(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets, maxTargets = targets,
                targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            var definitions = new object[] {
                new { id = Driver, revision = 1, modifiers = new[] { new { id = "range", query = "attackRange", operation = "add", value = 4, priority = 0 },
                    new { id = "uses", query = "slashLimit", operation = "add", value = 10, priority = 0 },
                    new { id = "keep", query = "handLimit", operation = "add", value = 20, priority = 0 } }, activations = new[] {
                        A("one", 1, new { op = "damage", target = "selectedTarget", amount = 1 }), A("two", 1, new { op = "damage", target = "selectedTarget", amount = 2 }),
                        A("source-less", 0, new { op = "receiveOwnerDamage", target = "owner", amount = 1 }),
                        A("armor-two", 1, new { op = "grantArmor", target = "selectedTarget", amount = 2 }),
                        A("armor-four", 1, new { op = "grantArmor", target = "selectedTarget", amount = 4 }),
                        A("extra-turn", 0, new { op = "pendExtraTurn", target = "owner" }), A("recover-one", 0, new { op = "recover", target = "owner", amount = 1 }) } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "real-paid-child", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage",
                    optional = false, effects = new[] { Pause("paid") } } } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "skip-unrelated-play", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Gate, revision = 1, hpSkillGrants = new[] { new { id = "actual-full-hp-parity", skillId = Skill, minimumHp = 20 } } },
                new { id = Loss, revision = 1, triggers = new[] { new { id = "withdraw-after-origin", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage",
                    optional = false, priority = 200, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "loseHp", target = "owner", amount = 1 }, Pause("lost") } } } },
                new { id = Startup, revision = 1, triggers = new[] { new { id = "true-round-zero-damage", window = "gameStarting", subject = "owner", optional = false,
                    effects = new[] { new { op = "damage", target = "owner", amount = 1 } } } } }
            };
            var labels = new[] { Driver, Watch, Quiet, Gate, Loss, Startup }.ToDictionary(id => id, id => new
            { name = id, description = "真实轮次、原生伤害和来源生命周期", optionLabels = id is Watch or Loss ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实逞凶来源", "", Driver, "qun", 20));
            builder.AddGeneral(new(Peer, "真实伤害受害者", "", Quiet, "wei", 20));
            builder.AddDeck(new(Deck, "没有自然抽牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实轮数伤害", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
