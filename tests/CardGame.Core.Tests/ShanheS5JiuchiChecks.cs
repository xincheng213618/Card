using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5JiuchiChecks
{
    private const string Jiuchi = "shanhe:s5-jiuchi-14210", Benghuai = "boundary:benghuai-current";
    private const string Zaiqi = StandardShanheCampaignCatalog.S5ZaiqiSkillId, Chuanwu = "ol:chuanwu";
    private const string Owner = "fixture:s5-jiuchi-owner", Peer = "fixture:s5-jiuchi-peer", Deck = "fixture:s5-jiuchi-deck";
    private const string Driver = "fixture:s5-jiuchi-driver", Watch = "fixture:s5-jiuchi-damage";
    private const string Gate = "fixture:s5-jiuchi-hp-source", Before = "fixture:s5-jiuchi-before", Changed = "fixture:s5-jiuchi-changed";
    private const string Prevent = "fixture:s5-jiuchi-prevent";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:s5-jiuchi-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new(StandardShanheS5BossPackage.DongZhuoFengyiId)], [], new string('a', 64))));

    public static void BlackHandWineUnlimitedUsesAndRealArmorDamagePermanentlySwapCold()
    {
        var s = Scene.Start("armor"); s.Use("armor", 3);
        Need(s.V(0) is { Hp: 10, MaxHp: 15 } && s.V(3).Armor == 3 && s.Has(Jiuchi) && s.Has(Benghuai) && !s.Has(Zaiqi),
            "The real owner begins with the source form's wounded 10/15 HP, Jiuchi and Benghuai, against a genuinely granted three-point Armor pool.");
        var club = s.V().Hand.Single(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Club && card.Rank == 8).Id;
        var spade = s.V().Hand.Single(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Spade && card.Rank == 7).Id;
        var red = s.V().Hand.Single(card => card.Suit == Suit.Heart && card.Rank == 9).Id;
        Need(s.WineActions().Any(action => action.CardId == club) && s.WineActions().Any(action => action.CardId == spade) &&
            s.WineActions().All(action => action.CardId != red),
            "Production 14210 converts both genuine club and spade hand entities, while a real red hand entity is excluded.");
        s.Wine(club);
        Need(s.V().HasAlcoholEffect && !s.G.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Alcohol),
            "Unlimited use does not stack a second unconsumed Alcohol effect.");
        var first = s.BeginSlash(); s.AtDamage();
        var original = s.G.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardId == first);
        var hit = s.E<DamageAppliedEvent>().Single(); var receipt = s.Replacements().Single();
        Need(original is { ConsumedAlcoholBoost: true, ConsumedAlcoholBonus: 1 } && !s.V().HasAlcoholEffect &&
            hit is { SourceSeat: 0, TargetSeat: 3, Amount: 2, RemainingHp: 20,
                ArmorPayment: { ArmorBefore: 3, ArmorAbsorbed: 2, ArmorAfter: 1, HpLossAmount: 0 } } &&
            receipt.Source is { SkillId: Jiuchi, OwnerSeat: 0 } && receipt.CardUseFrameId == original.Id &&
            receipt.CardActionId == original.Action!.ActionId && receipt.DamageFrameId == hit.ArmorPayment!.DamageFrameId &&
            receipt.TargetSeat == 3 && receipt.DamageAmount == 2 && receipt.AlcoholBonus == 1 && receipt.GrantAdded &&
            receipt.LostSkillIds.SequenceEqual([Benghuai]) && receipt.RemovedGrants.Any(grant => grant.SkillId == Benghuai) &&
            receipt.GrantedGrant is { SkillId: Zaiqi, TurnExpiry: null, PhaseExpiry: null, NextActualTurnStartExpiry: null } &&
            !s.Has(Benghuai) && s.Has(Zaiqi),
            "A fully Armor-paid native two-point wine Slash still pays real damage and issues the exact permanent source-owned skill exchange, rather than a turn suppression.");
        AssertReceiptCollectionsAreFrozen(s, receipt);
        s.Cold(); s.Continue(); s.FinishSlash(first); s.AssertPaidOnce(club); s.AssertPaidOnce(first);
        Need(s.WineActions().Any(action => action.CardId == spade), "Consuming the first wine Slash permits a second real black-card Alcohol in the same Play.");
        s.Wine(spade); var second = s.BeginSlash(); s.AtDamage();
        Need(s.E<DamageAppliedEvent>().Last() is { Amount: 2, RemainingHp: 19,
                ArmorPayment: { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpLossAmount: 1 } } &&
            s.Replacements().Length == 1 && s.V().Skills!.Count(skill => skill.ContentId == Zaiqi) == 1 &&
            s.E<CardUseAlcoholConsumptionCapturedEvent>().Count(fact => fact.ActorSeat == 0 && fact.AlcoholBonus == 1) == 2,
            "The second paid wine consumes only the remaining Armor, loses actual HP once, and cannot duplicate the already completed permanent grant.");
        s.Cold(); s.Continue(); s.FinishSlash(second); s.AssertPaidOnce(spade); s.AssertPaidOnce(second);
        s.EndOwnerTurn();
        Need(!s.Has(Benghuai) && s.Has(Zaiqi) && s.E<ProgramBindingStartedEvent>().All(fact => fact.SkillId != Benghuai) &&
            s.E<CurrentTurnOwnSkillSuppressionIssuedEvent>().Length == 0,
            "The actual first Ending has no Benghuai choice even though another living character has lower HP; this is permanent removal, not old current-turn suppression.");
        s.Cold(); s.Until(() => s.P?.SkillPrompt?.SkillId == Zaiqi && s.P.Choices.Any(choice => Action(choice) == "activate"));
        var hand = s.V().HandCount; s.Cold(); s.Answer(choice => Action(choice) == "activate"); s.Play();
        Need(s.E<ProgramCardsRevealedEvent>().Single(fact => fact.SkillId == Zaiqi).Cards.Count == 6 &&
            s.V().HandCount == hand + 6 && !s.Has(Benghuai) && s.Has(Zaiqi) &&
            s.G.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.Draw) == 2 &&
            s.Replacements().Length == 1 && s.E<ProgramAlcoholSlashSkillReplacementCompletedEvent>().Length == 1,
            "Across an actual turn boundary the independently acquired 887 really replaces the next draw with lost HP plus one black cards; the old skill and completed exchange do not return.");
        s.Cold();
        VerifyOneWineUseAcrossRealFireChain();
        VerifyExistingZaiqiSourceSharesOneInstanceAndCanWithdrawIndependently();
    }

    private static void VerifyExistingZaiqiSourceSharesOneInstanceAndCanWithdrawIndependently()
    {
        var s = Scene.Start("already-zaiqi");
        var initial = s.E<HpConditionalSkillGrantChangedEvent>().Single(fact => fact.OwnerSeat == 0 && fact.SkillId == Zaiqi && fact.Added);
        Need(s.Has(Zaiqi) && s.Replacements().Length == 0 && s.E<ProgramCardsRevealedEvent>().All(fact => fact.SkillId != Zaiqi),
            "A genuine HP-conditioned 887 source already exists and its first actual draw offer was declined before Jiuchi earns another independent source.");
        var wine = s.ClubWineId(); s.Wine(wine); var slash = s.BeginSlash(); s.AtDamage();
        var paid = s.Replacements().Single();
        Need(paid.GrantAdded && paid.GrantedGrant.GrantId != initial.GrantId &&
            paid.SharedInstanceGrant?.GrantId == initial.GrantId && paid.GrantedGrant.SkillInstanceId == initial.SkillInstanceId &&
            s.V().Skills!.Count(skill => skill.ContentId == Zaiqi) == 1,
            "The new permanent source has its own grant identity but reuses the already active 887 instance, rather than creating duplicate real draw candidates.");
        s.Cold(); s.Continue(); s.FinishSlash(slash); s.Use("lose-one");
        Need(s.V().Hp == 9 && s.Has(Zaiqi) && !s.Has(Benghuai) &&
            s.E<HpConditionalSkillGrantChangedEvent>().Any(fact => fact.GrantId == initial.GrantId && !fact.Added && fact.OwnerHp == 9),
            "A real one-HP payment removes the old conditional source while the distinct permanent source keeps the shared 887 skill available.");
        s.Cold(); s.EndOwnerTurn(); s.Until(() => s.P?.SkillPrompt?.SkillId == Zaiqi && s.P.Choices.Any(choice => Action(choice) == "activate"));
        s.Cold(); s.Answer(choice => Action(choice) == "activate"); s.Play();
        Need(s.E<ProgramCardsRevealedEvent>().Where(fact => fact.SkillId == Zaiqi).ToArray() is [{ Cards.Count: 7 }] &&
            s.E<ProgramBindingResolvedEvent>().Count(fact => fact.SkillId == Zaiqi && fact.Activated && fact.Completed) == 1 &&
            s.V().SkillRuntimeStates!.Count(state => state.SkillId == Zaiqi) == 1 && s.Replacements().Length == 1,
            "After the old source withdraws, exactly one genuine next-turn offer reveals lost HP plus one cards and reaches Play; no second 887 candidate or runtime state survives.");
        s.AssertPaidOnce(wine); s.AssertPaidOnce(slash); s.Cold();
    }

    private static void VerifyOneWineUseAcrossRealFireChain()
    {
        var s = Scene.Start("chain"); s.Use("armor", 3); s.Use("armor", 1);
        var chainAction = s.G.GetHumanLegalActions().First(action => action.Kind != LegalActionKind.Recast && action.ConversionSource is null &&
            action.CardId is { } id && s.V().Hand.Any(card => card.Id == id && card.Kind == CardKind.IronChain) && action.TargetSeats.SequenceEqual([1, 3]));
        var chain = chainAction.CardId!.Value; s.PlayAction(chainAction); s.Play();
        Need(s.V(1).IsChained && s.V(3).IsChained && s.V(1).Armor == 3 && s.V(3).Armor == 3,
            "One genuine original Iron Chain links the ally and actual enemy before the two independent Armor payments.");
        var wine = s.ClubWineId(); s.Wine(wine); var slash = s.BeginSlash(CardKind.FireSlash); s.AtDamage();
        var receipt = s.Replacements().Single();
        Need(s.E<DamageAppliedEvent>() is [{ TargetSeat: 3, Amount: 2, Nature: DamageNature.Fire,
            ArmorPayment: { ArmorAbsorbed: 2, HpLossAmount: 0 } }], "The original real Fire Slash consumes wine and pays the first fully absorbed fire damage segment.");
        s.Cold(); s.Continue(); s.AtDamage();
        Need(s.E<DamageAppliedEvent>() is [{ TargetSeat: 3, Amount: 2, Nature: DamageNature.Fire },
                { TargetSeat: 1, Amount: 2, Nature: DamageNature.Fire, ArmorPayment: { ArmorAbsorbed: 2, HpLossAmount: 0 } }] &&
            s.E<ChainedDamagePropagatedEvent>() is [{ ResolutionId: var use, SourceSeat: 0, FromSeat: 3, TargetSeat: 1, Amount: 2, Nature: DamageNature.Fire }] &&
            use == receipt.CardUseFrameId && s.E<CardUseAlcoholConsumptionCapturedEvent>() is [{ CardUseFrameId: var wineUse, AlcoholBonus: 1 }] &&
            wineUse == use && s.Replacements().Length == 1 && s.V().Skills!.Count(skill => skill.ContentId == Zaiqi) == 1,
            "The true chain recipient shares exactly the original paid use's consumption fact; its real Armor damage cannot reissue the permanent swap or create a second acquired Zaiqi.");
        s.Cold(); s.Continue(); s.FinishSlash(slash);
        Need(s.Replacements().Length == 1 && s.E<ProgramAlcoholSlashSkillReplacementCompletedEvent>().Length == 1 &&
            s.V(1) is { Hp: 20, Armor: 1, IsChained: false } && s.V(3) is { Hp: 20, Armor: 1, IsChained: false },
            "Cold returns from both actual fire-damage children complete one independent exchange while preserving full-damage chain propagation and once-paid Armor.");
        s.AssertPaidOnce(chain, CardMoveReasons.IronChainFinished); s.AssertPaidOnce(wine); s.AssertPaidOnce(slash); s.Cold();
    }

    private static void AssertReceiptCollectionsAreFrozen(Scene scene, AlcoholSlashSkillReplacementReceipt receipt)
    {
        var before = State(scene.G);
        foreach (var list in new System.Collections.IList[]
            { (System.Collections.IList)receipt.LostSkillIds, (System.Collections.IList)receipt.RemovedGrants, (System.Collections.IList)receipt.RevokedGrants })
        {
            Need(list.IsReadOnly && list.Count > 0, "Every nested permanent-replacement receipt list must be a real nonempty read-only collection.");
            var rejected = false; try { list[0] = list[0]; } catch (NotSupportedException) { rejected = true; }
            Need(rejected, "An observer cannot mutate a nested committed receipt through IList and thereby change history or prepared player views.");
        }
        var lost = receipt.LostSkillIds.ToArray(); var removed = receipt.RemovedGrants.ToArray(); var revoked = receipt.RevokedGrants.ToArray();
        var copy = receipt with { LostSkillIds = lost, RemovedGrants = removed, RevokedGrants = revoked };
        lost[0] = "standard:none"; removed[0] = removed[0] with { SkillId = "standard:none" }; revoked[0] = revoked[0] with { SkillId = "standard:none" };
        Need(copy.LostSkillIds.SequenceEqual(receipt.LostSkillIds) && copy.RemovedGrants.SequenceEqual(receipt.RemovedGrants) &&
            copy.RevokedGrants.SequenceEqual(receipt.RevokedGrants) && State(scene.G) == before,
            "Receipt input arrays are copied before exposure; attempted observer mutation and later original-array writes alter neither the committed receipt nor any player snapshot.");
    }

    public static void OrdinaryPreventedAndHpLossSlashesCannotCreatePermanentExchangeCold()
    {
        foreach (var scene in new[] { "ordinary", "prevent", "jueqing" })
        {
            var s = Scene.Start(scene);
            if (scene == "prevent") s.Use("armor", 3);
            int? wine = null;
            if (scene != "ordinary") { wine = s.ClubWineId(); s.Wine(wine.Value); }
            var slash = s.BeginSlash(); s.FinishSlash(slash);
            Need(s.Has(Jiuchi) && s.Has(Benghuai) && !s.Has(Zaiqi) && s.Replacements().Length == 0 &&
                s.E<ProgramAlcoholSlashSkillReplacementCompletedEvent>().Length == 0,
                "Neither an ordinary unboosted Slash nor a real prevented/HP-loss-replaced wine Slash may invent a permanent exchange: " + s.Diagnostic());
            if (scene == "ordinary")
                Need(s.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 1 }] &&
                    s.E<CardUseAlcoholConsumptionCapturedEvent>().Length == 0 && s.V(3).Hp == 19,
                    "The ordinary physical Slash really damages its target but has no consumed-wine proof.");
            else if (scene == "prevent")
                Need(s.E<ProgramDamagePreventedEvent>() is [{ SkillId: Prevent, SourceSeat: 0, TargetSeat: 3, Amount: 2 }] &&
                    s.E<DamageAppliedEvent>().Length == 0 && s.V(3) is { Hp: 20, Armor: 3 } && !s.V().HasAlcoholEffect,
                    "A native before-damage prevention consumes the issued wine effect but pays neither Armor nor damage, so no damage-after exchange exists.");
            else
                Need(s.E<DamageAppliedEvent>().Length == 0 && s.V(3).Hp == 18 && !s.V().HasAlcoholEffect &&
                    s.E<CardUseAlcoholConsumptionCapturedEvent>() is [{ ActorSeat: 0, AlcoholBonus: 1 }],
                    "Original Jueqing converts the real wine Slash into two HP loss; consumption alone cannot masquerade as paid damage.");
            if (wine.HasValue) s.AssertPaidOnce(wine.Value);
            s.AssertPaidOnce(slash); s.Cold();
        }
    }

    public static void PaidSourceLifetimesAndTemporaryPrintedReturnPreservePermanentReplacementCold()
    {
        var absent = Scene.Start("loss-before"); var wine = absent.ClubWineId(); absent.Wine(wine);
        var unqualified = absent.BeginSlash(); absent.At(Before);
        Need(absent.V().Hp == 9 && !absent.Has(Jiuchi) && absent.Has(Benghuai) && absent.Replacements().Length == 0,
            "A genuine finalized-use child pays HP and withdraws the conditional source before the pending wine Slash damages its target.");
        absent.Cold(); absent.Continue(); absent.FinishSlash(unqualified);
        Need(absent.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2 }] &&
            absent.E<CardUseAlcoholConsumptionCapturedEvent>() is [{ ActorSeat: 0, AlcoholBonus: 1 }] &&
            absent.Replacements().Length == 0 && !absent.Has(Zaiqi) && absent.Has(Benghuai),
            "The old consumed-wine receipt cannot lend replacement authority to a source that is actually gone at damage time.");
        absent.AssertPaidOnce(wine); absent.AssertPaidOnce(unqualified); absent.Cold();

        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var s = Scene.Start(scene); var material = s.ClubWineId(); s.Wine(material); var slash = s.BeginSlash(); s.At(Changed);
            var receipt = s.Replacements().Single();
            Need(!s.Has(Benghuai) && s.Has(Zaiqi) && s.Has(Jiuchi) &&
                s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.AlcoholSlashSkillReplacement?.ProgramFrameId == receipt.ProgramFrameId),
                "The exact permanent exchange is already paid on its owning frame before a genuine skills-changed child can remove or kill its source.");
            s.Cold(); s.Continue(); s.FinishSlash(slash);
            Need(s.Replacements().Length == 1 && s.E<ProgramAlcoholSlashSkillReplacementCompletedEvent>() is [var completed] &&
                completed.ProgramFrameId == receipt.ProgramFrameId &&
                s.E<CardUseFinishedEvent>().Count(fact => fact.CardId == slash) == 1 &&
                (scene == "paid-loss" ? !s.Has(Jiuchi) && !s.Has(Benghuai) && s.Has(Zaiqi) : !s.V().IsAlive && s.V(1).IsAlive && s.G.State.Winner == Winner.None),
                "Losing or dying after payment drains the exact already-issued child and native use once; a surviving genuine ally keeps the death branch from hiding behind game termination.");
            s.AssertPaidOnce(material); s.AssertPaidOnce(slash); s.Cold();
        }

        var returning = Scene.Start("restore"); returning.Use("self-damage");
        var oldGrant = returning.E<OrderedPrintedSkillGrantRemovedEvent>().Single(fact => fact.Grant.SkillId == Benghuai);
        Need(!returning.Has(Benghuai) && returning.Has(Jiuchi) && returning.E<OrderedPrintedSkillLostEvent>().Any(fact => fact.SkillId == Benghuai),
            "A real native Chuanwu damage window has temporarily removed the first printed Benghuai grant while leaving the independently granted Jiuchi source available.");
        returning.Cold(); var oldMaterial = returning.ClubWineId(); returning.Wine(oldMaterial); var native = returning.BeginSlash(); returning.AtDamage();
        var paid = returning.Replacements().Single();
        Need(paid.RevokedGrants.Contains(oldGrant.Grant) && paid.RemovedGrants.All(grant => grant.SkillId != Benghuai) &&
            !returning.Has(Benghuai) && returning.Has(Zaiqi),
            "Permanent loss records the exact old grant already held by a real temporary-loss lease, rather than inspecting only currently present grants.");
        returning.Cold(); returning.Continue(); returning.FinishSlash(native); returning.EndOwnerTurn();
        returning.Until(() => returning.E<OrderedPrintedSkillGrantRestoredEvent>().Any(fact => fact.FrameId == oldGrant.FrameId && fact.Grant == oldGrant.Grant));
        var restoration = returning.E<OrderedPrintedSkillGrantRestoredEvent>().Single(fact => fact.FrameId == oldGrant.FrameId && fact.Grant == oldGrant.Grant);
        Need(!restoration.Restored && restoration.Outcome == "permanently-lost" &&
            !returning.Has(Benghuai) && returning.Has(Zaiqi) && returning.E<ProgramBindingStartedEvent>().All(fact => fact.SkillId != Benghuai),
            "The genuine actual-turn restoration processes its original typed lease but cannot resurrect the grant revoked by the later permanent exchange.");
        returning.AssertPaidOnce(oldMaterial); returning.AssertPaidOnce(native); returning.Cold();
    }

    private static string Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action") ?? "";
    private static bool ContinueChoice(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option-id") == "continue";
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Facts = game.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
        game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(game.AcceptedCommands)
    });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public bool Has(string skill) => V().Skills?.Any(item => item.ContentId == skill) == true;
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public AlcoholSlashSkillReplacementReceipt[] Replacements() => E<ProgramAlcoholSlashSkillsReplacedEvent>().Select(fact => fact.Receipt).ToArray();
        public IEnumerable<LegalAction> WineActions() => G.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.Alcohol && action.ConversionSource?.SkillId == Jiuchi);
        public int ClubWineId() => V().Hand.Single(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Club && card.Rank == 8).Id;
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "Actual S5 Jiuchi command rejected.");
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var prompt = P ?? throw new InvalidOperationException("No real Jiuchi owner prompt.");
            Accept(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, G.Revision));
        }
        public void Continue() => Answer(ContinueChoice);
        public void PlayAction(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        public void Wine(int material)
        {
            PlayAction(WineActions().Single(action => action.CardId == material)); Play();
            var declaration = E<CardUseDeclaredEvent>().Single(fact => fact.CardId == material);
            Need(declaration.CardKind == CardKind.Alcohol && E<AlcoholAppliedEvent>().Count(fact => fact.ResolutionId == declaration.ResolutionId && fact.SourceSeat == 0) == 1 &&
                E<CardUseFinishedEvent>().Count(fact => fact.ResolutionId == declaration.ResolutionId) == 1 && V().HasAlcoholEffect,
                "The original black hand card really completes a native Alcohol use and applies its actual pending boost once.");
        }
        public int BeginSlash(CardKind kind = CardKind.Slash)
        {
            var action = G.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash && item.TargetSeats.SequenceEqual([3]) &&
                item.ConversionSource is null && item.CardId is { } id && V().Hand.Any(card => card.Id == id && card.Kind == kind));
            PlayAction(action); return action.CardId!.Value;
        }
        public void Use(string activation, params int[] targets)
        {
            Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets, G.Revision, P!.PromptId));
            Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, drainDamage: true);
        }
        public void At(string skill) => Until(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(ContinueChoice));
        public void AtDamage() => At(Watch);
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void FinishSlash(int card)
        {
            Until(() => E<CardUseFinishedEvent>().Any(fact => fact.CardId == card), drainDamage: true);
            if (V().IsAlive) Play();
        }
        public void EndOwnerTurn()
        {
            var turn = G.State.TurnNumber;
            Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
            Until(() => E<TurnEndedEvent>().Any(fact => fact.ActorSeat == 0 && fact.TurnNumber == turn));
        }
        public void Until(Func<bool> done, bool drainDamage = false)
        {
            for (var step = 0; step < 260; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } prompt)
                {
                    if (prompt.Kind == DecisionKind.RescueDying)
                    { Answer(choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); continue; }
                    if (drainDamage && prompt.SkillPrompt?.SkillId == Watch && prompt.Choices.Any(ContinueChoice)) { Continue(); continue; }
                    throw new InvalidOperationException("Unexpected actual Jiuchi boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded actual Jiuchi boundary: " + Diagnostic());
        }
        public void AssertPaidOnce(int card, CardMoveReason? finish = null)
        {
            var moves = G.CardMovements.Where(move => move.CardId == card).ToArray();
            var cleanup = moves.Where(move => move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile).ToArray();
            var expected = finish ?? CardMoveReasons.UseFinished;
            Need(moves.Count(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
                cleanup is [var completed] && completed.Reason == expected &&
                E<CardUseDeclaredEvent>().Count(fact => fact.CardId == card) == 1 && E<CardUseFinishedEvent>().Count(fact => fact.CardId == card) == 1,
                "Each original entity has one actual Hand-to-Processing payment, its exact native cleanup reason, and one declared/finished use: " +
                JsonSerializer.Serialize(new { card, ExpectedFinish = expected.Value, Moves = moves,
                    Declared = E<CardUseDeclaredEvent>().Where(fact => fact.CardId == card), Finished = E<CardUseFinishedEvent>().Where(fact => fact.CardId == card) }));
        }
        public void Cold()
        {
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == State(restored), "Cold replay preserves permanent source receipts, exact original costs, leases, child cursors and all four filtered views."); G = restored;
        }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, V().Hp, V().IsAlive, Skills = V().Skills?.Select(skill => skill.ContentId),
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Replacements = Replacements(), Damage = E<DamageAppliedEvent>(), Bindings = E<ProgramBindingResolvedEvent>(),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var extra = new List<string> { Driver, Watch };
            if (scene == "restore") extra.Add(Jiuchi);
            if (scene == "loss-before") extra.Add(Before);
            if (scene is "paid-loss" or "paid-death") extra.Add(Changed);
            if (scene == "jueqing") extra.Add("classic:jueqing");
            ShanheStartingCard[] cards = [new(CardKind.Dodge, Suit.Club, 8), new(CardKind.Dodge, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 9),
                new(CardKind.Slash, Suit.Spade, 2), new(CardKind.Slash, Suit.Club, 3)];
            if (scene == "chain") cards = [.. cards, new(CardKind.IronChain, Suit.Club, 6), new(CardKind.FireSlash, Suit.Heart, 5)];
            var setup = new ShanheBattleSetup(83, 1, "fixture:s5-jiuchi-" + scene, Owner, 10, 0, 0, 0, 4, extra, cards, [],
                [new(Peer + "-low", Hp: 1) { StartingHand = [] }, new(Peer + "-target") { StartingHand = [] }],
                [new(Peer + "-ally") { StartingHand = [] }], new string('c', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand());
            if (scene == "already-zaiqi")
            {
                result.Until(() => result.P?.SkillPrompt?.SkillId == Zaiqi && result.P.Choices.Any(choice => Action(choice) == "skip"));
                result.Cold(); result.Answer(choice => Action(choice) == "skip");
            }
            result.Play(); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-jiuchi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Jiuchi, Benghuai, Zaiqi, "classic:jueqing" }) builder.AddSkill(Production.Value.GetSkill(id));
            if (scene == "restore") CharacterFixtureContent.Register(builder, ["ol:zhang-hua"]);
            object Pause() => new { op = "chooseOption", target = "owner", resultBind = "native-return", options = new[] { new { id = "continue" } } };
            object A(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets,
                maxTargets = targets, targetKind = "otherLiving", usesPerTurn = (int?)null, effects };
            var definitions = new List<object>
            {
                new { id = Driver, revision = 1, modifiers = new[] { new { id = "retain-originals", query = "handLimit", operation = "add", value = 20, priority = 0 } },
                    activations = new[] { A("armor", 1, new { op = "grantArmor", target = "selectedTarget", amount = 3 }),
                        A("self-damage", 0, new { op = "damage", target = "owner", amount = 1 }),
                        A("lose-one", 0, new { op = "loseHp", target = "owner", amount = 1 }) } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "real-damage-tail", window = "afterDamageApplied", subject = "damageSource",
                    damageOccurrence = "perDamage", optional = false, priority = -100, effects = new[] { Pause() } } } }
            };
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实酒池规则操作", description = "原生护甲赋予和受伤，不伪造伤害或酒加成" },
                [Watch] = new { name = "实际伤害后子流程", description = "原生伤害已支付后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            if (scene is "loss-before" or "paid-loss" or "already-zaiqi")
            {
                definitions.Add(new { id = Gate, revision = 1, hpSkillGrants = new[] { new { id = "live-full-source",
                    skillId = scene == "already-zaiqi" ? Zaiqi : Jiuchi, minimumHp = 10 } } });
                labels[Gate] = new { name = "真实HP技能来源", description = "HP低于10撤销独立的条件技能来源" };
            }
            if (scene == "loss-before")
            {
                definitions.Add(new { id = Before, revision = 1, triggers = new[] { new { id = "real-use-loses-source", window = "cardUseTargetsFinalized",
                    ownerRelation = "actor", cardKinds = new[] { "slash" }, optional = false, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "loseHp", target = "owner", amount = 1 }, Pause() } } } });
                labels[Before] = new { name = "原生用牌子流程", description = "真实用牌指定目标后损失HP并暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            if (scene is "paid-loss" or "paid-death")
            {
                var tail = new { op = "loseHp", target = "owner", amount = scene == "paid-death" ? 10 : 1 };
                definitions.Add(new { id = Changed, revision = 1, triggers = new[] { new { id = "paid-exchange-child", window = "skillsChanged", subject = "owner",
                    optional = false, usageScope = "game", usageLimit = 1, effects = new object[] { Pause(), tail } } } });
                labels[Changed] = new { name = "已付交换子流程", description = "真实技能变化后失去来源或死亡", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            if (scene == "prevent")
            {
                definitions.Add(new { id = Prevent, revision = 1, triggers = new[] { new { id = "real-before-damage-prevention", window = "beforeDamageApplied",
                    subject = "damageTarget", optional = false, effects = new[] { new { op = "preventCurrentDamage", target = "owner" } } } } });
                labels[Prevent] = new { name = "真实防止伤害", description = "防止原生受伤尝试而不支付护甲" };
            }
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "专属酒池实际来源", "", scene is "restore" or "loss-before" or "paid-loss" ? Benghuai : Jiuchi, "qun", 15,
                scene == "restore" ? [Chuanwu] : scene is "loss-before" or "paid-loss" ? [Gate] : scene == "already-zaiqi" ? [Benghuai, Gate] : [Benghuai]));
            builder.AddGeneral(new(Peer + "-ally", "存活的实际盟友", "", "standard:none", "wei", 20));
            builder.AddGeneral(new(Peer + "-low", "体力较低的实际角色", "", "standard:none", "wei", 20));
            builder.AddGeneral(new(Peer + "-target", "实际承受杀的角色", "", "standard:none", "wei", 20, scene == "prevent" ? [Prevent] : []));
            builder.AddDeck(new(Deck, "全黑固定实体牌堆", 0, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 9)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实专属酒池与永久交换", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer + "-ally", Peer + "-low", Peer + "-target"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
