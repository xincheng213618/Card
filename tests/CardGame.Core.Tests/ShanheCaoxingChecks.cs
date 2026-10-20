using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheCaoxingChecks
{
    private const string Liushi = "ol:liu-shi", Zhanwan = "ol:zhan-wan", Activation = "topdeck-heart-slash";
    private const string Boss = StandardShanheS5CaoxingPackage.CaoxingId;
    private const string Dangtou = "shanhe:current-dangtou-2037", Third = "shanhe:current-duoduo-yishan-2084";
    private const string Capacity = "shanhe:current-jinnangji-2011", Roulin = "shanhe:s5-roulin-14211";
    private const string Owner = "fixture:caoxing-owner", Victim = "fixture:caoxing-victim", Peer = "fixture:caoxing-peer";
    private const string Driver = "fixture:caoxing-driver", DamageWatch = "fixture:caoxing-damage", RecoveryWatch = "fixture:caoxing-recovery";
    private const string GainWatch = "fixture:caoxing-gain", EndWatch = "fixture:caoxing-discard-end", Mutation = "fixture:caoxing-mutation";
    private const string Gate = "fixture:caoxing-health-source", RegainGate = "fixture:caoxing-other-health-source", Quiet = "fixture:caoxing-quiet", Deck = "fixture:caoxing-deck";
    private static readonly CardMoveReason DrawReason = new("skill-program." + Zhanwan + ".Draw");
    private static readonly CardMoveReason ThirdReason = new("skill-program." + Third + ".Draw");
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new ShanheBattleSetup(83, 1, "fixture:caoxing-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new(StandardShanheS5BossPackage.DiaoChanBiyueId)], [], new string('c', 64)) { TacticSkillIds = [Dangtou, Third, Capacity] }));

    public static void ContinuousHeartHeTopdeckUsesRealSlashCostsResponsesAndDamageMarkersCold()
    {
        var s = Scene.Start("continuous"); s.Play();
        var invalid = s.V().Hand.Single(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Spade).Id;
        s.Reject(new UseProgramSkillCommand(0, Liushi, Activation, [invalid], [2], s.G.Revision, s.P!.PromptId));
        s.Use("armor-four", [2]);
        var wine = s.PlayCard(CardKind.Alcohol);
        VerifyPublishedAndTargetedAi(s);
        var lion = s.V().Equipment.Single(card => card.Kind == CardKind.SilverLion).Id;
        var before = s.G.CardMovements.Count;
        s.Begin(lion, 2); s.Until(() => s.At(RecoveryWatch));
        Need(s.V().Hp == 20 && s.V().Equipment.Count == 0 && s.E<RecoveryAppliedEvent>() is [{ TargetSeat: 0, Amount: 1 }] &&
            s.G.CardMovements.Skip(before).Count(move => move.CardId == lion && move.From == CardLocation.Equipment(0) && move.To == CardLocation.DrawPile) == 1 &&
            s.SlashUses().Length == 0 && s.Mark(2) == 0,
            "A genuine Heart Silver Lion pays Equipment-to-deck-top once; its actual removal recovery runs before the native virtual Slash is declared.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 2, Amount: 4, ArmorPayment: { ArmorAbsorbed: 4, HpLossAmount: 0 } }] &&
            s.V(2).Hp == 20 && s.V(2).Armor == 0 && s.Mark(2) == 1 && s.SlashUses().Length == 1 &&
            s.E<ProgramCardDamageModifiedEvent>().Single(fact => fact.Source.SkillId == Dangtou) is { } bonus && bonus.ModifiedAmount == bonus.BaseAmount + 2 &&
            !s.V().HasAlcoholEffect,
            "The first real virtual Slash consumes wine and the actual first-Slash tactic; numeric armor absorbs all four damage without preventing the one actual Liushi effect.");
        s.Cold();
        var second = s.V().Hand.First(card => card.Suit == Suit.Heart).Id;
        var responses = s.E<CardRespondedEvent>().Length;
        s.Begin(second, 1); s.Play();
        var paid = s.E<CardRespondedEvent>().Skip(responses).Where(fact => fact.ResponderSeat == 1 && fact.EffectiveCardKind == CardKind.Dodge).ToArray();
        Need(paid.Length == 2 && paid.Select(fact => fact.CardId).Distinct().Count() == 2 &&
            s.E<RequiredResponseProgressEvent>().Any(fact => fact.ResponderSeat == 1 && fact.RequiredResponseCount == 2 && fact.ResponseCount == 1) &&
            s.E<DamageAppliedEvent>().Length == 1 && s.Mark(1) == 0 && s.V(1).Hp == 18 && s.SlashUses().Length == 2,
            "A second same-Play activation is real and unrestricted by the native one-Slash quota; the native male Roulin target pays two distinct Dodges and acquires no damage effect.");
        s.Cold();
        var third = s.V().Hand.First(card => card.Suit == Suit.Heart).Id;
        s.Begin(third, 2); s.Play();
        Need(s.E<DamageAppliedEvent>().Where(fact => fact.TargetSeat == 2).Select(fact => fact.Amount).SequenceEqual([4, 1]) &&
            s.Mark(2) == 2 && s.SlashUses().Length == 3 && s.E<ProgramSkillResolvedEvent>().Count(fact => fact.SkillId == Liushi && fact.ActivationId == Activation && fact.Completed) == 3,
            "A third activation creates a third independent native use, with no repeated wine or first-Slash bonus and one further effect for the actual damage.");
        var effects = s.E<ProgramLiushiEffectAppliedEvent>();
        Need(effects.Select(fact => fact.MarkerCount).SequenceEqual([1, 2]) && effects.All(fact => fact.OwnerSeat == 0 && fact.TargetSeat == 2 &&
                fact.SkillId == Liushi && fact.ActionId > 0 && fact.DamageFrameId > 0 && fact.DamageWindowId > 0 &&
                s.E<CardUseDeclaredEvent>().Any(use => use.ResolutionId == fact.CardUseFrameId && use.SourceSeat == 0 && use.CardKind == CardKind.Slash)) &&
            effects.Select(fact => fact.ActionId).Distinct().Count() == 2,
            "Each effect is issued by its exact independent native Slash action and applied-damage window; the fully Dodged action owns none.");
        foreach (var (card, from) in new[] { (lion, CardLocation.Equipment(0)), (second, CardLocation.Hand(0)), (third, CardLocation.Hand(0)) })
            s.AssertTopCost(card, from);
        s.AssertPhysicalUse(wine); s.Cold(); s.Privacy();
        var remainingCost = s.V().Hand.First(card => card.Suit == Suit.Heart).Id;
        Need(s.G.GetHumanLegalActions().Any(action => action.ProgramSkillId == Liushi && action.SelectableCardIds.Contains(remainingCost)),
            "A real unused Heart material keeps the continuous activation genuinely available before adding a targeting prohibition.");
        s.Use("self-only", []);
        Need(s.G.GetHumanLegalActions().All(action => action.ProgramSkillId != Liushi),
            "A genuinely issued self-only card-target restriction hides the otherwise-affordable other-target virtual Slash activation.");
        s.Reject(new UseProgramSkillCommand(0, Liushi, Activation, [remainingCost], [2], s.G.Revision, s.P!.PromptId));
        Need(s.V().Hand.Any(card => card.Id == remainingCost), "The stale prohibited activation cannot pay its still-present Heart material before returning without a Slash.");
        s.Cold();
        VerifyReacquiredInstanceDoesNotClaimOriginalDamage();
    }

    public static void StackedLiushiClearsAllAtNativeDiscardEndAndDrawsExactCountCold()
    {
        var s = Scene.Start("stacked"); s.Play(); s.HitTwice();
        Need(s.V(1).Hp == 3 && s.Mark(1) == 2 && s.V(1).HandCount == 6,
            "Two independently paid native Slash hits leave two persistent Liushi effects without touching the target's six old hand entities.");
        var originalHand = s.G.CreateSnapshot(1).Players[1].Hand.Select(card => card.Id).ToHashSet();
        s.EndPlay(); s.Until(() => s.AtEnd("before"));
        var discarded = s.HandDiscards(1);
        var ending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == EndWatch).WindowContext!;
        Need(s.V(1).HandLimit == 1 && s.V(1).HandCount == 1 && discarded.Length == 5 &&
            discarded.Select(move => move.CardId).Distinct().Count() == 5 && discarded.All(move => originalHand.Contains(move.CardId)) &&
            ending is { Window: SkillProgramTriggerWindow.DiscardPhaseEnded, TargetSeat: 1, Facts.TurnOwnerDiscardPhaseHandDiscardCount: 5 } &&
            s.Draws(Zhanwan).Length == 0 && s.Mark(1) == 2,
            "The actual NPC actor's HP3 minus two persistent effects gives hand limit one and exactly five original hand-limit movements, frozen in its real discard-end context before Zhanwan starts.");
        s.Cold(); s.Continue(); s.Until(() => s.AtGain() && s.Proof is { RequestedCount: 5, ActualCount: 5 } proof && proof.LastBatchId == s.Batch!.Id);
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Zhanwan);
        var invoice = s.Draws(Zhanwan).Single();
        s.AssertDrawPayment(root, invoice);
        Need(invoice.Requested == 5 && invoice.ActualCount == 5 && invoice.ParentFrameId == root.Id && invoice.OwnerSeat == 0 &&
            invoice.ActualTurnOwnerSeat == 1 && invoice.ActualTurnDrawOrdinal == 1 && s.Mark(1) == 2 &&
            s.G.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason == DrawReason) == 5,
            "One true Draw5 is paid once, with its exact owning Zhanwan parent and first actual-draw-call ordinal; per-card gain batches do not multiply the reward.");
        s.Cold(); s.Continue(); s.Until(() => s.AtEnd("after"));
        Need(s.Mark(1) == 0 && s.V(1).HandLimit == 3 && s.V().HandCount == 7 && s.Draws(Zhanwan).Length == 1 &&
            s.Draws(Third) is [{ Requested: 2, ActualCount: 2, ActualTurnDrawOrdinal: 2 }] &&
            s.E<PlayerMarkerChangedEvent>().Where(fact => fact.PlayerSeat == 1 && fact.Marker == PlayerMarkerKind.LiuShi).Select(fact => fact.Delta).SequenceEqual([1, 1, -2]) &&
            s.E<ProgramBindingResolvedEvent>().Count(fact => fact.FrameId == root.Id && fact.SkillId == Zhanwan && fact.Completed) == 1 &&
            s.E<ProgramLiushiEffectClearedEvent>().Single() is { RemovedCount: 2, OwnerSeat: 0, TargetSeat: 1 } cleared && cleared.FrameId == root.Id &&
            s.DrawPhaseTotal() == 0 && s.V().HandLimit is null,
            "Zhanwan clears all two effects after the paid draw; its one normal Draw5 triggers one actual Duoduo III Draw2, while another actor's discard and outside-phase draws do not count for Jinnangji or reveal an inactive hand limit.");
        s.Cold(); s.Continue(); s.Until(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(s.Draws(Zhanwan).Length == 1 && s.Mark(1) == 0 && s.HandDiscards(1).Length == 5,
            "Returning through the next bounded native turn cannot drain the cleared target again or pay its five discarded entities twice.");
        s.Cold();

        var zero = Scene.Start("zero"); zero.Play();
        zero.Begin(zero.V().Hand.First(card => card.Suit == Suit.Heart).Id, 1); zero.Play(); zero.EndPlay();
        zero.Until(() => zero.E<TurnStartedEvent>().Any(fact => fact.ActorSeat == 2));
        Need(zero.Mark(1) == 1 && zero.HandDiscards(1).Length == 0 && zero.Draws(Zhanwan).Length == 0 &&
            zero.E<ProgramBindingStartedEvent>().All(fact => fact.SkillId != Zhanwan),
            "A genuinely affected target who discards zero cards gives no draw and does not prematurely erase its persistent Liushi effect.");
        zero.Cold();
    }

    public static void PaidZhanwanDrawRetainsSourceLossTailAndActualS5RecipesStayIsolatedCold()
    {
        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var s = Scene.Start(scene); s.Play(); s.HitTwice(); s.EndPlay(); s.Until(() => s.AtEnd("before")); s.Continue();
            s.Until(() => s.AtMutation("paid"));
            var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Zhanwan);
            var invoice = s.Draws(Zhanwan).Single();
            s.AssertDrawPayment(root, invoice);
            Need(invoice.ParentFrameId == root.Id && invoice.Requested == 5 && invoice.ActualCount == 5 && s.Mark(1) == 2,
                "The actual Draw5 is already paid and owned by the original Zhanwan frame before its real gain child can remove or kill the source.");
            s.Cold(); s.Continue();
            if (scene == "paid-loss")
            {
                s.Until(() => s.AtMutation("lost"));
                Need(s.V().Hp == 19 && !s.Has(Zhanwan) && s.Mark(1) == 2,
                    "A real HP change removes the derived Zhanwan grant inside the already-paid native draw child, before clearing the target.");
                s.Cold(); s.Continue(); s.Until(() => s.AtEnd("after"));
            }
            else s.Until(() => !s.V().IsAlive && s.Mark(1) == 0 && s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(frame => frame.Id != root.Id));
            Need(s.Mark(1) == 0 && s.Draws(Zhanwan).Length == 1 && s.Draws(Zhanwan).Single().InvocationId == invoice.InvocationId &&
                s.G.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason == DrawReason) == 5 &&
                s.HandDiscards(1).Length == 5 && s.E<ProgramLiushiDrawPaidEvent>().Length == 1,
                "Loss or actual death cannot repeat the issued Draw5 or five original discard payments. " + s.PaidDiagnostic(root.Id));
            Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(frame => frame.Id != root.Id) &&
                s.E<ProgramBindingResolvedEvent>().Count(fact => fact.FrameId == root.Id && fact.SkillId == Zhanwan) == 1,
                "The original paid Zhanwan frame must return and resolve exactly once after its real source-loss child. " + s.PaidDiagnostic(root.Id));
            Need(s.E<ProgramLiushiEffectClearedEvent>().Single() is { RemovedCount: 2 } cleared && cleared.FrameId == root.Id,
                "The paid Zhanwan clear instruction must execute once with the original target. " + s.PaidDiagnostic(root.Id));
            Need(s.G.State.Winner == Winner.None,
                "The source-death fixture retains a genuine live teammate and must prove paid return without a game-over stack-clear shortcut. " + s.PaidDiagnostic(root.Id));
            s.Cold();
        }
        VerifyActualRecipes();
    }

    private static void VerifyPublishedAndTargetedAi(Scene scene)
    {
        var before = State(scene.G); var view = scene.G.CreateSnapshot(0);
        var action = scene.G.GetHumanLegalActions().Single(action => action.ProgramSkillId == Liushi && action.ProgramActivationId == Activation);
        Need(action.SelectableCardIds.Contains(scene.V().Equipment.Single(card => card.Kind == CardKind.SilverLion).Id) &&
            action.SelectableTargetSeats.Contains(2) && action.ProgramAiHint is { TargetDamage: > 0, TargetHealthEffects.Count: > 0 },
            "Actual legal-action publication exposes the Heart equipment and distant target with a nonzero real-damage AI prior, without claiming a selected target yet.");
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.G)!;
        var publicFactory = typeof(GameEngine).GetMethod("CreateProgramAiPublicContext", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(CharacterState)], null)!;
        var actorFactory = typeof(GameEngine).GetMethod("CreateAiSkillContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var context = (ProgramAiPublicContext)publicFactory.Invoke(scene.G, [players[0]])!;
        var owner = (PlayerSkillContext)actorFactory.Invoke(scene.G, [players[0], 0, view])!;
        var target = (PlayerSkillContext)actorFactory.Invoke(scene.G, [players[2], 0, view])!;
        var effects = scene.Registry.GetSkill(Liushi).Program!.Activations.Single(activation => activation.Id == Activation).Effects;
        var hint = ProgramCompositionAi.Estimate(effects, owner, publicContext: context with { SelectedTarget = target }).Hint;
        var predicted = hint.PredictTarget(scene.V(2).Hp, scene.V(2).MaxHp, scene.V(2).Armor);
        Need(hint.TargetDamage == 4 && hint.TargetDirectHpLoss == 0 && predicted is { DamageAmount: 4, ArmorAbsorbed: 4, HpLossAmount: 0 } &&
            view.Players.Where(player => player.Seat != 0).All(player => player.Hand.Count == 0) && State(scene.G) == before,
            "The native public context's real virtual-Slash callback includes wine and first-Slash damage before armor; legal and targeted AI queries consume no action, material, marker or hidden hand. " + JsonSerializer.Serialize(predicted));
    }

    private static void VerifyReacquiredInstanceDoesNotClaimOriginalDamage()
    {
        var s = Scene.Start("instance"); s.Play();
        var original = s.E<HpConditionalSkillGrantChangedEvent>().Single(fact => fact.SkillId == Liushi && fact.Added);
        var cost = s.V().Hand.First(card => card.Suit == Suit.Heart).Id;
        s.Begin(cost, 1); s.Until(() => s.AtMutation("lost-instance"));
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Action?.ConversionChain.Any(source => source.SkillId == Liushi) == true);
        var conversion = use.Action!.ConversionChain.Single(source => source.SkillId == Liushi);
        Need(s.V().Hp == 19 && !s.Has(Liushi) && original.Source.CapabilitySkillId == Gate && conversion.SkillInstanceId == original.SkillInstanceId && conversion.OwnerSeat == 0 &&
            s.E<HpConditionalSkillGrantChangedEvent>().Any(fact => fact.SkillId == Liushi && !fact.Added && fact.SkillInstanceId == original.SkillInstanceId) &&
            s.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 1 }] && s.Mark(1) == 0,
            "A real HP cost removes the exact original derived Liushi instance only after its native Slash has genuinely paid damage; the action retains that original conversion source.");
        s.Cold(); s.Continue(); s.Until(() => s.AtMutation("regranted"));
        var reacquired = s.E<HpConditionalSkillGrantChangedEvent>().Last(fact => fact.SkillId == Liushi && fact.Added);
        Need(s.V().Hp == 18 && s.Has(Liushi) && reacquired.Source.CapabilitySkillId == RegainGate && reacquired.SkillInstanceId != original.SkillInstanceId &&
            s.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == use.Id).Action!.ConversionChain.Single(source => source.SkillId == Liushi).SkillInstanceId == original.SkillInstanceId &&
            s.E<ProgramLiushiEffectAppliedEvent>().Length == 0,
            "A second real HP payment qualifies a different HP-grant source and its same-ID new instance, without rewriting the already-issued native action or inheriting its old damage.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.Mark(1) == 0 && s.E<ProgramLiushiEffectAppliedEvent>().Length == 0 && s.E<DamageAppliedEvent>().Length == 1 &&
            s.E<ProgramSkillResolvedEvent>().Count(fact => fact.SkillId == Liushi && fact.ActivationId == Activation) == 1,
            "Returning past the stale after-damage candidate neither throws nor applies the old effect through the newly acquired same-ID skill; actual paid damage and the original activation still return once.");
        s.AssertTopCost(cost, CardLocation.Hand(0)); s.Cold();
    }

    private static void VerifyActualRecipes()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(node => node.Encounters ?? []).SelectMany(encounter => encounter.Enemies).All(member => member.GeneralId != Boss),
            "The S5 Cao Xing recipe is absent from the retained 2024 edition.");
        for (var difficulty = 1; difficulty <= 3; difficulty++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(difficulty, StandardShanheCampaignCatalog.Returning2026EditionId);
            var node = definition.Nodes.Single(node => node.Id == "guangcheng");
            var encounter = node.Encounters!.Single(encounter => encounter.Id == "guangcheng:caoxing-s5");
            Need(encounter.Enemies is [{ GeneralId: Boss, MaxHpBonus: 0, Hp: null, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] &&
                (encounter.Enemies[0].ExtraSkillIds?.Count ?? 0) == 0 && (encounter.Allies?.Count ?? 0) == 0,
                "Each local tier uses the source base Cao Xing rather than inventing higher-form skills, growth, fixed hand, equipment or allies.");
            var setup = new ShanheBattleSetup(83, 1, node.Id, "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies,
                encounter.Allies ?? [], ShanheCampaignContent.GetContentHash(definition));
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
            Need(registry.Generals[Boss].BaseHp == 4 && registry.Generals[Boss].SkillIds.SequenceEqual([Liushi, Zhanwan]),
                "The actual conditional S5 package reuses the two exact production OL programs and raw four maximum HP.");
            var game = StandardShanheCampaignCatalog.CreateBattle(setup); var started = game.Submit(new StartGameCommand());
            Need(started.Accepted, started.Error?.Message ?? "The actual S5 encounter did not start.");
            var view = game.CreateSnapshot(0).Players.Single(player => player.GeneralId == Boss);
            Need(view is { Hp: 4, MaxHp: 4, HandCount: 4, Armor: 0 } && view.Equipment.Count == 0 &&
                view.Skills?.Select(skill => skill.ContentId).ToHashSet().SetEquals([Liushi, Zhanwan]) == true,
                "Actual S5 enemy startup has four real opening cards, raw four HP, only the printed two skills and no fabricated equipment or armor.");
            var before = State(game); game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Need(before == State(game), "The actual S5 enemy, source identity and native setup survive cold replay.");
            var ordinary = setup with { EnemyRoster = [new("classic:cao-cao")] };
            Need(!StandardShanheCampaignCatalog.CreateBattleRegistry(ordinary).Generals.ContainsKey(Boss),
                "A warmed ordinary recipe does not import the S5 alias merely because the normal OL programs are available.");
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Facts = game.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
        game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public int Mark(int seat) => V(seat).Markers?.SingleOrDefault(marker => marker.Kind == PlayerMarkerKind.LiuShi)?.Count ?? 0;
        public bool Has(string id) => V().Skills?.Any(skill => skill.ContentId == id) == true;
        public bool At(string id) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == id;
        public bool AtEnd(string option) => At(EndWatch) && P!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == option);
        public bool AtMutation(string option) => At(Mutation) && P!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == option);
        public bool AtGain() => At(GainWatch);
        public CardMovementBatchContext? Batch => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(frame => frame.WindowContext?.MovementBatch is not null)?.WindowContext?.MovementBatch;
        public NativeDrawInvocationProof? Proof => Batch?.NativeDrawInvocation;
        public NativeDrawInvocationRecordedEvent[] Draws(string skill) => E<NativeDrawInvocationRecordedEvent>().Where(fact => fact.OwnerSeat == 0 && fact.Reason == new CardMoveReason("skill-program." + skill + ".Draw")).ToArray();
        public int DrawPhaseTotal() => E<NativeDrawInvocationRecordedEvent>().Where(fact => fact.OwnerSeat == 0 && fact.TurnNumber == G.State.TurnNumber &&
            fact.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Draw, ActorSeat: 0 }).Sum(fact => fact.ActualCount);
        public CurrentTurnCardUseKindsRecordedEvent[] SlashUses() => E<CurrentTurnCardUseKindsRecordedEvent>().Where(fact => fact.ActorSeat == 0 &&
            fact.RoundActionType == CardActionType.Use && fact.EffectiveCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash).ToArray();
        public CardMovementRecord[] HandDiscards(int seat) => G.CardMovements.Where(move => move.From == CardLocation.Hand(seat) && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.HandLimitDiscard).ToArray();
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, (result.Error?.Message ?? "Rejected actual Cao Xing command.") + " " + Diagnostic()); }
        public void Reject(GameCommand command)
        { var before = State(G); var result = G.Submit(command); Need(!result.Accepted && State(G) == before, "An invalid cost is atomically rejected without moving a card, spending a use or altering the issued prompt."); }
        public void Answer(Func<PromptChoice, bool> predicate) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(predicate).Id, G.Revision));
        public void Continue() => Answer(choice => choice.Parameters.ContainsKey("option-id"));
        public void Begin(int card, int target) => Accept(new UseProgramSkillCommand(0, Liushi, Activation, [card], [target], G.Revision, P!.PromptId));
        public void HitTwice()
        { for (var hit = 0; hit < 2; hit++) { Begin(V().Hand.First(card => card.Suit == Suit.Heart).Id, 1); Play(); Cold(); } }
        public void Use(string id, int[] targets) { Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId)); Play(); }
        public int PlayCard(CardKind kind)
        {
            var action = G.GetHumanLegalActions().First(action => action.CardId is { } id && V().Hand.Any(card => card.Id == id && card.Kind == kind) && action.ConversionSource is null);
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)); Play(); return action.CardId.Value;
        }
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Until(Func<bool> stop)
        {
            for (var step = 0; step < 240; step++)
            {
                if (stop()) return;
                if (P is { PlayerSeat: 0 } prompt)
                {
                    if (prompt.Choices.Any(choice => choice.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (prompt.Kind == DecisionKind.RescueDying) { Answer(choice => choice.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    if (prompt.Kind == DecisionKind.DiscardCards) { Accept(new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, G.Revision)); continue; }
                    throw new InvalidOperationException("Unexpected real Cao Xing boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded real Cao Xing boundary was not reached: " + Diagnostic());
        }
        public void AssertTopCost(int card, CardLocation from) => Need(G.CardMovements.Count(move => move.CardId == card && move.From == from && move.To == CardLocation.DrawPile) == 1 &&
            G.CardMovements.All(move => move.CardId != card || move.To != CardLocation.Processing), "One selected original Heart HE entity pays deck-top once; the subsequent Slash is a genuine independent zero-material use.");
        public void AssertPhysicalUse(int card) => Need(G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1, "The original wine has one real use payment and one cleanup.");
        public void AssertDrawPayment(ProgramSkillFrame root, NativeDrawInvocationRecordedEvent invoice)
        {
            Need(root.LiushiDrawPayment is { OwnerSeat: 0, TargetSeat: 1, InstructionIndex: 1, RequestedCount: 5, ActualCount: 5, Replaced: false } paid &&
                paid.FrameId == root.Id && paid.SkillId == Zhanwan && paid.SkillInstanceId == root.SkillInstanceId && paid.GameplayHash == root.GameplayHash &&
                paid.WindowId == root.WindowContext!.ParentFrameId && paid.SequenceBefore == invoice.SequenceBefore && paid.SequenceAfter == invoice.SequenceAfter &&
                E<ProgramLiushiDrawPaidEvent>().Single().Payment == paid,
                "The owning Zhanwan frame freezes the exact issued positive Draw5 interval, instruction, discard target and source hash before any gain child or source invalidation.");
        }
        public void Privacy() { for (var viewer = 0; viewer < G.PlayerCount; viewer++) Need(G.CreateSnapshot(viewer).Players.Where(player => player.Seat != viewer).All(player => player.Hand.Count == 0), "Cao Xing costs and public effects reveal no foreign private hand identities."); }
        public void Cold()
        {
            var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == before, "Cold replay preserves exact native costs, actual damage, marker stacks, draw payment and typed source-loss continuations.");
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every real Cao Xing card entity remains in exactly one zone.");
        }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, G.State.TurnNumber, Alive = V().IsAlive, V().Hp, V().HandCount,
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(choice => choice.Id) },
            Marks = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, Count = Mark(seat) }),
            Damage = E<DamageAppliedEvent>().Select(fact => new { fact.SourceSeat, fact.TargetSeat, fact.Amount }),
            Draws = E<NativeDrawInvocationRecordedEvent>().Select(fact => new { fact.InvocationId, fact.OwnerSeat, fact.Requested, fact.ActualCount, fact.ActualTurnOwnerSeat, fact.ActualTurnDrawOrdinal, fact.ParentFrameId, fact.LastBatchId }),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public string PaidDiagnostic(long root) => JsonSerializer.Serialize(new
        {
            scene, Root = root, G.State.Winner,
            Players = G.CreateSnapshot(0).Players.Select(player => new { player.Seat, player.TeamId, player.IsAlive, player.Hp, player.HandCount }),
            Draws = Draws(Zhanwan).Select(fact => new { fact.InvocationId, fact.Requested, fact.ActualCount, fact.ParentFrameId }),
            DiscardCount = HandDiscards(1).Length, Paid = E<ProgramLiushiDrawPaidEvent>().Select(fact => fact.Payment.FrameId),
            Cleared = E<ProgramLiushiEffectClearedEvent>(),
            Resolved = E<ProgramBindingResolvedEvent>().Where(fact => fact.SkillId == Zhanwan),
            MarkerChanges = E<PlayerMarkerChangedEvent>().Where(fact => fact.Marker == PlayerMarkerKind.LiuShi && fact.PlayerSeat == 1)
                .Select(fact => new { fact.ResolutionId, fact.Delta, fact.Count, fact.Reason }),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var continuous = scene == "continuous"; var death = scene == "paid-death";
            ShanheStartingCard[] own = continuous
                ? [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3), new(CardKind.Dodge, Suit.Spade, 4), new(CardKind.Alcohol, Suit.Club, 5), new(CardKind.Dodge, Suit.Heart, 7)]
                : [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Heart, 3)];
            var victim = new ShanheCombatantDefinition(continuous ? Peer : Victim, Hp: continuous ? 18 : null)
            {
                StartingHand = continuous ? [new(CardKind.Dodge, Suit.Heart, 8), new(CardKind.Dodge, Suit.Club, 9)] :
                    Enumerable.Range(0, scene == "zero" ? 1 : 6).Select(index => new ShanheStartingCard(CardKind.Nullification, Suit.Club, index + 1)).ToArray()
            };
            var enemies = death ? new ShanheCombatantDefinition[] { new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] } } :
                [victim, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }];
            var setup = new ShanheBattleSetup(83, 1, "fixture:caoxing-" + scene, Owner, continuous ? 19 : 20, 0, 0, 0, 0, [], own,
                continuous ? [new(CardKind.SilverLion, Suit.Heart, 6)] : [], enemies, death ? [victim] : [], new string('c', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-caoxing", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:cao-xing");
            foreach (var id in new[] { Dangtou, Third, Capacity, Roulin }) builder.AddSkill(Production.Value.GetSkill(id));
            var definitions = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string option) => new { op = "chooseOption", target = "owner", resultBind = option, options = new[] { new { id = option } } };
            void Add(string id, string property, object value, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = value });
                labels[id] = new { name = id, description = "真实曹性原牌成本、生命周期子流程与付款检查", optionLabels = options.ToDictionary(option => option, option => option) };
            }
            Add(Driver, "activations", new object[] { new { id = "armor-four", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                targetKind = "otherLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "grantArmor", target = "selectedTarget", amount = 4 } } },
                new { id = "self-only", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "grantTurnCardTargetRestriction", target = "owner", targetRestriction = "selfOnly" } } } });
            Add(DamageWatch, "triggers", new[] { new { id = "actual-paid-damage", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage", optional = false, priority = -100, effects = new[] { Pause("continue") } } }, "continue");
            Add(RecoveryWatch, "triggers", new[] { new { id = "real-equipment-removal-recovery", window = "afterHpRecovered", subject = "owner", optional = false, effects = new[] { Pause("continue") } } }, "continue");
            Add(GainWatch, "triggers", new[] { new { id = "real-draw-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                movementOccurrence = "perBatch", movementReasons = new[] { DrawReason.Value, ThirdReason.Value }, priority = 100, optional = false, effects = new[] { Pause("continue") } } }, "continue");
            object PositiveDiscard() => new { kind = "compare", left = new { kind = "turnOwnerDiscardPhaseHandDiscardCount" }, @operator = "greaterThanOrEqual", right = new { kind = "integerConstant", value = 1 } };
            Add(EndWatch, "triggers", new[] { new { id = "before-real-drain", window = "discardPhaseEnded", subject = "owner", turnOwnerScope = "otherLiving", optional = false, priority = 100, condition = PositiveDiscard(), effects = new[] { Pause("before") } },
                new { id = "after-real-drain", window = "discardPhaseEnded", subject = "owner", turnOwnerScope = "otherLiving", optional = false, priority = -100, condition = PositiveDiscard(), effects = new[] { Pause("after") } } }, "before", "after");
            Add(Quiet, "triggers", new[] { new { id = "skip-unrelated-native-ai-play", window = "afterNormalDraw", subject = "owner", optional = false,
                effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } });
            Add(Gate, "hpSkillGrants", new[] { new { id = "true-health-source", skillId = scene == "instance" ? Liushi : Zhanwan, minimumHp = 20 } });
            if (scene is "paid-loss" or "paid-death")
            {
                var effects = new List<object> { Pause("paid"), new { op = "loseHp", target = "owner", amount = scene == "paid-death" ? 20 : 1 } };
                if (scene == "paid-loss") effects.Add(Pause("lost"));
                Add(Mutation, "triggers", new[] { new { id = "actual-paid-source-loss", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                    movementOccurrence = "perBatch", movementReasons = new[] { DrawReason.Value }, usageScope = "game", usageLimit = 1, priority = 200,
                    optional = false, effects = effects.ToArray() } }, scene == "paid-death" ? ["paid"] : ["paid", "lost"]);
            }
            if (scene == "instance")
            {
                Add(RegainGate, "hpSkillGrants", new[] { new { id = "different-live-health-source", skillId = Liushi, maximumHp = 18 } });
                Add(Mutation, "triggers", new[] { new { id = "replace-exact-issued-source", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage",
                    optional = false, priority = 200, usageScope = "game", usageLimit = 1, effects = new object[] { new { op = "loseHp", target = "owner", amount = 1 },
                        Pause("lost-instance"), new { op = "loseHp", target = "owner", amount = 1 }, Pause("regranted") } } }, "lost-instance", "regranted");
            }
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) builder.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id] });
            var skills = new List<string> { Driver, DamageWatch, RecoveryWatch, GainWatch, EndWatch };
            if (scene is "paid-loss" or "paid-death") skills.AddRange([Gate, Mutation]); else skills.Add(Zhanwan);
            if (scene == "instance") { skills.Remove(Driver); skills.AddRange([Gate, RegainGate, Mutation]); }
            if (scene == "stacked") skills.AddRange([Third, Capacity]);
            if (scene == "continuous") skills.AddRange([Dangtou, Roulin]);
            builder.AddGeneral(new(Owner, "真实曹性来源", "", scene == "instance" ? Driver : Liushi, "qun", 20, skills));
            builder.AddGeneral(new(Victim, "实际五体力六手牌目标", "", Quiet, "wei", 5));
            builder.AddGeneral(new(Peer, "实际原生响应角色", "", Quiet, "wei", 20));
            var lion = CardCatalog.Get(CardKind.SilverLion); builder.AddCard(new("fixture:caoxing-silver-lion", lion.DisplayName, lion.CategoryName, lion.Description, CardKind.SilverLion));
            builder.AddDeck(new(Deck, "没有自然摸牌的固定牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(index => new ContentDeckPhysicalCard("standard:nullification", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实曹性与已付收据", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Victim, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = scene == "paid-death" ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = scene == "paid-death" ? 2 : 3 }));
        }
    }
}
