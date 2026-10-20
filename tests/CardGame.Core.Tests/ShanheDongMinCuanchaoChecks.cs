using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheDongMinCuanchaoChecks
{
    private const string Skill = "shanhe:current-cuanchao-14069", Cap = "shanhe:current-dungong-3387";
    private const string Boss = "shanhe:current-dongmin-dungong-cuanchao", Jizhi = "classic:jizhi", Fengzi = "ol:fengzi";
    private const string Owner = "fixture:cuanchao-owner", Peer = "fixture:cuanchao-peer", Source = "fixture:cuanchao-source";
    private const string Driver = "fixture:cuanchao-driver", Quiet = "fixture:cuanchao-quiet", Attack = "fixture:cuanchao-real-source";
    private const string DamageWatch = "fixture:cuanchao-paid-damage", RecoverWatch = "fixture:cuanchao-paid-recovery";
    private const string Suppress = "fixture:cuanchao-suppression", Deck = "fixture:cuanchao-deck";
    private const string Independent = "fixture:cuanchao-independent-conversion", HpGate = "fixture:cuanchao-hp-gate", PaidLoss = "fixture:cuanchao-paid-loss";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture-cuanchao-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new(Boss)], [], new string('f', 64))));

    public static void WholeUseCancellationCommitsNativePolarityOnlyWhenAcceptedCold()
    {
        VerifyWholeUsePolarityLoaderBoundaries();
        VerifyActualForm();
        var s = Scene.Start("polarity"); Need(s.Polarity == SkillPolarity.Yang, "The real production conversion starts in native Yang state.");
        s.RequestSlash(); s.AtConversion(); var first = s.NativeUse(CardKind.Slash); var firstId = first.Id; var firstCard = first.CardId;
        Need(first.SourceSeat == 3 && first.TargetSeats.SequenceEqual([0]) && first.Action is { PhysicalCards.Count: 1 },
            "The existing request operation makes the real enemy pay its original Hand Slash against the requesting human, rather than manufacturing an incoming damage frame.");
        s.Cold(); s.ChooseConversion(false); s.AtDamage();
        Need(s.Polarity == SkillPolarity.Yang && s.Commits().Length == 0 && s.Nullifications().Length == 0 &&
            s.Events<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 3 }] && s.Events<ProgramDamageCappedEvent>() is [{ BaseAmount: 5, CappedAmount: 3 }],
            "Declining the real Yang offer neither commits polarity nor invalidates the Use; the remaining native five damage really reaches Dungong and is capped to three.");
        s.Cold(); s.Continue(); s.Play(); s.AssertFinished(firstId, firstCard);
        s.RequestSlash(); s.AtConversion(); var second = s.NativeUse(CardKind.Slash); var secondId = second.Id; var secondCard = second.CardId;
        s.Cold(); s.ChooseConversion(true); s.Play();
        Need(s.Polarity == SkillPolarity.Yin && s.Commits() is [{ PreviousState: SkillPolarity.Yang, CurrentState: SkillPolarity.Yin }] &&
            s.Nullifications() is [{ ParentUseFrameId: var cancelled, OwnerSeat: 0, ActorSeat: 3, EffectiveKind: CardKind.Slash }] && cancelled == secondId &&
            s.Events<DamageAppliedEvent>().Length == 1 && s.Events<ProgramDamageCappedEvent>().Length == 1,
            "Accepting the next real basic Use commits the actual conversion exactly once and records whole-use invalidity before any damage or Armor payment.");
        s.AssertFinished(secondId, secondCard); s.Cold();
        var draw = s.BeginCard(CardKind.DrawTwo); s.AtConversion(); var drawUse = s.NativeUse(CardKind.DrawTwo).Id;
        var hand = s.V().HandCount; s.Cold(); s.ChooseConversion(true); s.Play();
        Need(s.Polarity == SkillPolarity.Yang && s.Commits().Select(e => (e.PreviousState, e.CurrentState))
                .SequenceEqual([(SkillPolarity.Yang, SkillPolarity.Yin), (SkillPolarity.Yin, SkillPolarity.Yang)]) &&
            s.Nullifications().Select(e => e.ParentUseFrameId).SequenceEqual([secondId, drawUse]) && s.V().HandCount == hand &&
            s.Events<ProgramBooleanStateChangedEvent>().All(e => e.SkillId != Skill),
            "The next real self-targeted trick accepts only native Yin, returns to Yang and cancels DrawTwo without substituting a boolean state or drawing a hidden second cost.");
        s.AssertFinished(drawUse, draw); s.Cold();
        s.RequestSlash(); s.AtConversion(); var next = s.NativeUse(CardKind.Slash); s.ChooseConversion(false); s.Play();
        Need(s.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual([3, 3]) && s.Nullifications().Length == 2 &&
            s.Commits().Length == 2 && s.V().Hp == 2, "An accepted whole-use status cannot leak into a new Slash; declining that new Use leaves its genuine capped damage intact.");
        s.AssertFinished(next.Id, next.CardId); s.Cold();
        VerifyIndependentConversionSourcesKeepTheFirstWholeUseProof();
    }

    public static void WholeUseStateCancelsAllRecipientsRoundsAndDelayedPlacementCold()
    {
        var s = Scene.Start("group"); s.BeginCard(CardKind.Peach); s.AtConversion(); s.ChooseConversion(true); s.Play();
        Need(s.Polarity == SkillPolarity.Yin, "A real self Peach acceptance, rather than a state injection, establishes Yin before the global trick.");
        var cost = s.V().Hand.Single(card => card.Kind == CardKind.Duel).Id;
        var garden = s.BeginCard(CardKind.PeachGarden); s.Reach(() => s.Optional(Fengzi));
        s.Answer(c => Action(c) == "activate"); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.Contains(cost)) == true);
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.Contains(cost)); s.AtConversion();
        var use = s.NativeUse(CardKind.PeachGarden); var id = use.Id;
        Need(use.TargetSeats.Order().SequenceEqual([0, 1, 2, 3]) && s.Events<ExtraCardEffectIssuedEvent>() is [{ CardUseFrameId: var extraId, PaidCardId: var paid }] &&
            extraId == id && paid == cost && use.ExtraCardEffect is not null && s.JizhiDraws() == 1,
            "The genuine global Use first retains all four recipients, one actual Jizhi gain and the real Fengzi same-category Hand cost/extra-effect receipt.");
        s.Cold(); s.ChooseConversion(true); s.Play();
        Need(Enumerable.Range(0, 4).All(seat => s.V(seat).Hp == 8) && s.Events<RecoveryAppliedEvent>().Length == 0 &&
            s.Nullifications().Count(e => e.ParentUseFrameId == id) == 1 && s.Events<ExtraCardEffectStartedEvent>().Count(e => e.CardUseFrameId == id) == 1 &&
            s.JizhiDraws() == 1 && s.Polarity == SkillPolarity.Yang && s.Commits().Length == 2 &&
            s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
            "Whole-use invalidity prevents every target and the genuine extra effect from recovering, while keeping the already-paid on-Use Jizhi and Fengzi results and exactly one conversion commitment.");
        s.AssertFinished(id, garden); s.Cold();
        var nextGarden = s.BeginCard(CardKind.PeachGarden); s.Play();
        Need(Enumerable.Range(0, 4).All(seat => s.V(seat).Hp == 9) && s.Events<RecoveryAppliedEvent>().Length == 4 && s.JizhiDraws() == 2 &&
            s.Commits().Length == 2 && s.Nullifications().Length == 2,
            "A new trick Use in Yang is genuinely effective for all four recipients, retains its separate on-Use draw and cannot inherit the earlier whole-use status or consumed extra-effect payment."); s.Cold();
        Need(s.G.CardMovements.Count(m => m.CardId == nextGarden && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "The next real global card pays its own original Hand material once.");

        var delayed = Scene.Start("delayed"); delayed.BeginCard(CardKind.Peach); delayed.AtConversion(); delayed.ChooseConversion(true); delayed.Play();
        var lightning = delayed.BeginCard(CardKind.Lightning); delayed.AtConversion(); var delayedId = delayed.NativeUse(CardKind.Lightning).Id;
        delayed.Cold(); delayed.ChooseConversion(true); delayed.Play();
        Need(delayed.Nullifications().Single(e => e.ParentUseFrameId == delayedId).EffectiveKind == CardKind.Lightning &&
            delayed.V().Judgment.All(card => card.Id != lightning) && delayed.G.CreateCardZoneDiagnostics().Single(c => c.CardId == lightning).Location == CardLocation.DiscardPile &&
            delayed.Events<JudgmentRequestedEvent>().Length == 0,
            "A real self Lightning is invalidated at its native placement Use, pays and finishes without a counterfeit judgment-zone card or later judgment window.");
        delayed.AssertFinished(delayedId, lightning); delayed.Cold(); var turn = delayed.G.State.TurnNumber; delayed.End(); delayed.Play();
        Need(delayed.G.State.TurnNumber > turn && delayed.Events<JudgmentRequestedEvent>().Length == 0 && delayed.Commits().Length == 2,
            "The next real actual turn cannot resurrect the canceled delayed placement or trigger another polarity payment."); delayed.Cold();
    }

    public static void SelfTargetedUsesAndLiveSkillChangesPreserveNativeReturnsAndCapCold()
    {
        var self = Scene.Start("self");
        foreach (var kind in new[] { CardKind.Peach, CardKind.DrawTwo, CardKind.Alcohol })
        {
            var card = self.BeginCard(kind); self.AtConversion(); var id = self.NativeUse(kind).Id; var hand = self.V().HandCount;
            self.Cold(); self.ChooseConversion(true); self.Play();
            Need(self.V() is { Hp: 8, HasAlcoholEffect: false } && self.V().HandCount == hand && self.Events<RecoveryAppliedEvent>().Length == 0,
                "Each genuinely self-targeted Peach/DrawTwo/Alcohol traverses the typed before-target bridge and suppresses only the card's effect, without recovery, extra drawing or a Wine enhancement.");
            self.AssertFinished(id, card); self.Cold();
        }
        Need(self.Commits().Length == 3 && self.Nullifications().Length == 3 && self.Polarity == SkillPolarity.Yin,
            "Three real accepted native self uses commit Yang/Yin/Yang exactly once each.");
        self.Use("self-five"); Need(self.Events<DamageAppliedEvent>() is [{ TargetSeat: 0, Amount: 3 }] && self.Commits().Length == 3,
            "A true program damage attempt has no card target window: it bypasses Cuanchao and reaches the still-live Dungong cap."); self.Cold();

        var live = Scene.Start("live"); live.Use("armor-five"); live.Use("lose-one");
        Need(live.V() is { Hp: 11, Armor: 5, IncomingDamageCap: null }, "A real HP payment activates the existing shared suppression without rewriting any template or skill state.");
        live.End(); live.Reach(() => live.P?.SkillPrompt?.SkillId == RecoverWatch);
        Need(live.Events<DamageAppliedEvent>() is [{ SourceSeat: 3, TargetSeat: 0, Amount: 5,
                ArmorPayment: { ArmorBefore: 5, ArmorAbsorbed: 5, HpLossAmount: 0 } }] && live.Commits().Length == 0 && live.Nullifications().Length == 0 &&
            live.V() is { Hp: 12, Armor: 0, IncomingDamageCap: 3 },
            "The actual enemy's normal Play Slash encounters the truly suppressed skills and spends full five Armor; its mandatory native ending recovery then restores the original skills before a genuine recovery child.");
        live.Cold(); live.Continue(); live.Play(); Need(live.Polarity == SkillPolarity.Yang, "Recovery enables the preserved initial polarity rather than toggling a disabled skill.");
        live.RequestSlash(); live.AtConversion(); var cancelled = live.NativeUse(CardKind.Slash); live.Cold(); live.ChooseConversion(true); live.Play();
        Need(live.Commits().Length == 1 && live.Nullifications().Single().ParentUseFrameId == cancelled.Id && live.Events<DamageAppliedEvent>().Length == 1,
            "After actual recovery, a new real Slash can be accepted and invalidated exactly once."); live.Cold();
        live.Use("lose-conversion"); Need(live.Events<ProgramOwnerSkillsReplacedEvent>() is [{ LostSkillIds: [Skill] }], "The existing legal owner replacement really removes the conversion skill.");
        live.RequestSlash(); live.AtDamage();
        Need(live.Events<DamageAppliedEvent>().Select(e => e.Amount).SequenceEqual([5, 3]) && live.Commits().Length == 1 && live.Nullifications().Length == 1 &&
            live.Events<ProgramDamageCappedEvent>() is [{ BaseAmount: 5, CappedAmount: 3 }] && live.V().Hp == 9,
            "Actual conversion-skill loss suppresses future offers, while the independent live Dungong still caps the new native Slash and never reuses an old whole-use status.");
        live.Cold(); live.Continue(); live.Play(); live.Cold();
        VerifyIssuedWholeUseStateSurvivesActualSourceLoss();
        VerifyRealDyingPeachReturnsWithoutRecoveryOrDuplicatePayment();
    }

    private static void VerifyRealDyingPeachReturnsWithoutRecoveryOrDuplicatePayment()
    {
        var s = Scene.Start("rescue"); s.RequestSlash(); s.AtConversion(); s.ChooseConversion(false);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"));
        var dyingId = s.G.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 0).Id;
        var material = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        Need(s.V().Hp == -2 && s.Commits().Length == 0 && s.Events<DamageAppliedEvent>() is [{ Amount: 3, TargetSeat: 0, RemainingHp: -2 }] &&
            s.Events<ProgramDamageCappedEvent>() is [{ BaseAmount: 5, CappedAmount: 3 }],
            "Declining the original physical Slash caps five damage to three: the actual one-HP recipient reaches minus two and enters the native dying frame before any rescue material is paid.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.SequenceEqual([material])); s.AtConversion();
        var peach = s.NativeUse(CardKind.Peach); var useId = peach.Id;
        Need(peach.Action is { Type: CardActionType.Use, ActorSeat: 0, PhysicalCards.Count: 1 } &&
            peach.DyingResponse is { ResolutionId: var originalDying, ResponderSeat: 0, UsedPeach: true, PeachCardId: var paid } &&
            originalDying == dyingId && paid == material && s.G.ResolutionStack.OfType<DyingFrame>().Any(f => f.Id == dyingId),
            "The real entity rescue is a targeted Use with the original DyingResponse return, not a fabricated Response action or a Play-phase-only stand-in.");
        s.Cold(); s.ChooseConversion(true);
        s.Reach(() => s.Events<CardUseFinishedEvent>().Any(e => e.ResolutionId == useId));
        Need(s.V().Hp == -2 && s.Events<RecoveryAppliedEvent>().Length == 0 && s.Nullifications() is
                [{ ParentUseFrameId: var cancelled, ActorSeat: 0, EffectiveKind: CardKind.Peach }] && cancelled == useId && s.Commits().Length == 1,
            "Accepting the rescue Peach commits Yang once and invalidates its recovery: the minus-two debt remains while the typed dying return and actual already-paid material are preserved.");
        s.AssertFinished(useId, material); s.Cold();
        s.Reach(() => !s.V().IsAlive && s.G.ResolutionStack.All(f => f is not DyingFrame));
        Need(s.Events<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 && s.Events<RecoveryAppliedEvent>().Length == 0 &&
            s.Commits().Length == 1 && s.Events<DyingResponseEvent>().Count(e => e.ResolutionId == dyingId && e.UsedPeach && e.PeachCardId == material) == 1 &&
            s.Events<DyingResolvedEvent>().Single(e => e.ResolutionId == dyingId) is { VictimSeat: 0, Survived: false } &&
            s.Events<DamageAppliedEvent>().Length == 1 && s.G.State.Winner == Winner.None && s.V(1).IsAlive,
            "With no second rescue material, the original dying process completes once; the live ally keeps the match nonterminal, and the canceled Peach never heals or repays itself."); s.Cold();
    }

    private static void VerifyIndependentConversionSourcesKeepTheFirstWholeUseProof()
    {
        var s = Scene.Start("dual"); s.RequestSlash(); s.AtConversion(); var use = s.NativeUse(CardKind.Slash); var id = use.Id; var card = use.CardId;
        s.ChooseConversion(true); s.Reach(() => s.Optional(Independent)); var first = s.NativeUse(CardKind.Slash).ProgramUseNullification;
        Need(first is { SkillId: Skill, InstructionIndex: 2 } && s.Commits().Length == 1 &&
            s.V().SkillRuntimeStates!.Single(state => state.SkillId == Independent).Polarity == SkillPolarity.Yang,
            "The first production skill has issued its exact whole-use proof while a different real conversion still has a legal lower-priority opportunity.");
        s.Cold(); s.Answer(c => Action(c) == "activate"); s.Play();
        Need(s.Events<ProgramCardUseNullifiedEvent>() is [var issued] && issued == first && s.Commits().Length == 1 &&
            s.Events<ProgramConversionPolarityCommittedEvent>().Single(e => e.SkillId == Independent) is
                { PreviousState: SkillPolarity.Yang, CurrentState: SkillPolarity.Yin } &&
            s.Events<ProgramBindingResolvedEvent>().Count(e => (e.SkillId is Skill or Independent) && e.Activated && e.Completed) == 2 &&
            s.Events<DamageAppliedEvent>().Length == 0,
            "Two distinct native conversions may each accept and flip in the same Use; the new whole-use operation is idempotent and retains the first source proof rather than overwriting it or throwing.");
        s.AssertFinished(id, card); s.Cold();
    }

    private static void VerifyIssuedWholeUseStateSurvivesActualSourceLoss()
    {
        var s = Scene.Start("paid-loss");
        Need(s.Events<HpConditionalSkillGrantChangedEvent>().Any(e => e.SkillId == Skill && e.Added && e.OwnerHp == 8),
            "The production conversion is truly granted by the existing healthy-HP capability, not inserted into a snapshot.");
        s.RequestSlash(); s.AtConversion(); var use = s.NativeUse(CardKind.Slash); var id = use.Id; var card = use.CardId;
        s.ChooseConversion(true); s.Reach(() => s.P?.SkillPrompt?.SkillId == PaidLoss);
        Need(s.V().Hp == 1 && s.Commits().Length == 1 && s.Events<HpConditionalSkillGrantChangedEvent>()
                .Count(e => e.SkillId == Skill && !e.Added && e.OwnerHp == 1) == 1 &&
            s.NativeUse(CardKind.Slash).ProgramUseNullification == s.Nullifications().Single() && s.Events<DamageAppliedEvent>().Length == 0,
            "After real acceptance, a lower-priority legal target child pays seven HP and withdraws the actual derived conversion; the already-issued native whole-use state still belongs to the original card.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.Nullifications().Length == 1 && s.Commits().Length == 1 && s.Events<DamageAppliedEvent>().Length == 0 && s.Events<ProgramDamageCappedEvent>().Length == 0,
            "Losing the issuing skill during a real paid child cannot revoke the accepted whole-use state, start damage, or duplicate the native commitment on cold return.");
        s.AssertFinished(id, card); s.Cold();
    }

    private static void VerifyActualForm()
    {
        var actual = Production.Value.Generals[Boss];
        Need(actual.BaseHp == 5 && actual.SkillIds.SequenceEqual([Cap, Skill]), "The production advanced form prints only the real fixed cap and conversion at raw five HP.");
        foreach (var difficulty in new[] { 1, 2, 3 })
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(difficulty, StandardShanheCampaignCatalog.Returning2026EditionId);
            var min = definition.Nodes.Single(n => n.Id == "luoyang").Encounters!.Single(e => e.Id == (difficulty == 1 ? "luoyang:dongmin-current-base" : "luoyang:dongmin-current-cuanchao"));
            Need(min.Enemies.Single().GeneralId == (difficulty == 1 ? StandardShanheS5BossPackage.DongMinBaseId : Boss),
                "The actual local three-tier recipe explicitly chooses base or advanced Dong Min without inventing the source's phase transition.");
        }
        Need(StandardShanheCampaignCatalog.CreateDefinition(1).Nodes.SelectMany(n => n.Encounters ?? []).SelectMany(e => e.Enemies).All(n => n.GeneralId != Boss),
            "The 2024 campaign does not gain this current conversion form.");
    }

    private static void VerifyWholeUsePolarityLoaderBoundaries()
    {
        string Resource(string suffix)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Resource("shanhe-current-cuanchao.rules.json"); var presentation = Resource("shanhe-current-cuanchao.presentation.json");
        void Reject(string boundary, Action<JsonObject> mutate)
        {
            var copy = JsonNode.Parse(rules)!;
            var trigger = copy["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == Skill)!["triggers"]![0]!.AsObject();
            mutate(trigger); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The shared whole-use conversion loader must reject " + boundary + " rather than weakening the exact target-owner acceptance/commit contract.");
        }
        Reject("an unknown native polarity", trigger => trigger["requiredPolarity"] = "unknown");
        Reject("a required polarity without its real commitment", trigger => trigger["effects"]!.AsArray().RemoveAt(0));
        Reject("a whole-use operation outside its real before-target window", trigger => trigger["window"] = "cardUseCommitted");
        Reject("an actor relation masquerading as the target owner", trigger => trigger["ownerRelation"] = "actor");
        Reject("response-use inclusion on the target-use operation", trigger => trigger["includeResponseUses"] = true);
        Reject("a non-owner whole-use operation target", trigger => trigger["effects"]![1]!["target"] = "selectedTarget");
        Reject("a conditional whole-use operation", trigger => trigger["effects"]![1]!["condition"] = new JsonObject { ["kind"] = "wounded" });
    }
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game; public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public SkillPolarity? Polarity => V().SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == Skill)?.Polarity;
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramConversionPolarityCommittedEvent[] Commits() => Events<ProgramConversionPolarityCommittedEvent>().Where(e => e.OwnerSeat == 0 && e.SkillId == Skill).ToArray();
        public ProgramCardUseNullifiedEvent[] Nullifications() => Events<ProgramCardUseNullifiedEvent>().Where(e => e.SkillId == Skill && e.OwnerSeat == 0).ToArray();
        public int JizhiDraws() => G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.classic:jizhi.Draw");
        public void Accept(GameCommand command) { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "The real Cuanchao command was rejected."); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public bool Optional(string skill) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill && P.Choices.Any(c => Action(c) == "activate");
        public void AtConversion() => Reach(() => Optional(Skill));
        public void ChooseConversion(bool accept) => Answer(c => Action(c) == (accept ? "activate" : "skip"));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void AtDamage() => Reach(() => P?.SkillPrompt?.SkillId == DamageWatch);
        public CardUseFrame NativeUse(CardKind kind) => G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == kind && f.TargetSeats.Contains(0));
        public void RequestSlash() => Accept(new UseProgramSkillCommand(0, Driver, "request", [], [3], G.Revision, P!.PromptId));
        public void Use(string id) { Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId)); Play(); }
        public int BeginCard(CardKind kind)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast);
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)); return a.CardId.Value;
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId is DamageWatch or RecoverWatch) { Continue(); continue; }
                    if (Optional(Jizhi)) { Answer(c => Action(c) == "activate"); continue; }
                    if (Optional(Fengzi)) { Answer(c => Action(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected actual Cuanchao boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The bounded native Cuanchao fixture unexpectedly ended: " + Diagnostic()); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing native Cuanchao boundary: " + Diagnostic());
        }
        public void AssertFinished(long useId, int card)
        {
            Need(Events<CardUseDeclaredEvent>().Count(e => e.ResolutionId == useId && e.CardId == card) == 1 &&
                Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId && e.CardId == card) == 1 &&
                Events<CardActionAcceptedEvent>().Count(e => e.Action.Type == CardActionType.Use &&
                    e.Action.PhysicalCards.Count == 1 && e.Action.PhysicalCards[0].CardId == card) == 1 &&
                G.CardMovements.Count(m => m.CardId == card && m.From.Zone == CardZoneKind.Hand && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
                "An effective or ineffective card retains one genuine declared Use, one original Hand payment and one whole native completion, rather than an early-pop shortcut.");
        }
        private string Diagnostic() => JsonSerializer.Serialize(new { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, P.Prompt, Skill = P.SkillPrompt?.SkillId },
            Polarity, Commits = Commits(), Nullified = Nullifications(), Damage = Events<DamageAppliedEvent>(),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves native polarity, whole-use invalidity, exact source/target/cost, additional effects and typed self-card returns without repeating a commitment or payment."); }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            ShanheStartingCard[] cards = scene switch {
                "polarity" => [new(CardKind.DrawTwo, Suit.Heart, 7)],
                "group" => [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.PeachGarden, Suit.Heart, 4), new(CardKind.PeachGarden, Suit.Heart, 5), new(CardKind.Duel, Suit.Spade, 6)],
                "delayed" => [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Lightning, Suit.Spade, 1)],
                "self" => [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.DrawTwo, Suit.Heart, 4), new(CardKind.Alcohol, Suit.Spade, 5)], _ => [] };
            var own = new List<string> { Cap, DamageWatch }; if (scene == "paid-loss") own.AddRange([HpGate, PaidLoss]); else own.Add(Skill);
            if (scene == "dual") own.Add(Independent); if (scene == "group") own.AddRange([Jizhi, Fengzi]);
            if (scene == "live") own.AddRange([Suppress, RecoverWatch]);
            if (scene == "rescue") cards = [new(CardKind.Peach, Suit.Heart, 3)];
            var sourceMember = new ShanheCombatantDefinition(Source, Hp: scene == "group" ? 8 : 12) { StartingHand = scene is "polarity" or "live" or "dual" or "paid-loss" or "rescue" ? Enumerable.Range(0, 6)
                .Select(i => new ShanheStartingCard(CardKind.Slash, Suit.Spade, i + 1)).ToArray() : [] };
            var setup = new ShanheBattleSetup(83, 1, "fixture-cuanchao-" + scene, Owner, scene == "live" ? 12 : scene == "rescue" ? 1 : 8, 0, 0, 0, 0, own, cards, [],
                scene == "rescue" ? [new(Peer) { StartingHand = [] }, sourceMember] :
                    [new(Peer, Hp: scene == "group" ? 8 : 12) { StartingHand = [] }, new(Peer, Hp: scene == "group" ? 8 : 12) { StartingHand = [] }, sourceMember],
                scene == "rescue" ? [new(Peer) { StartingHand = [] }] : [], new string('a', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-cuanchao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Skill, Cap, Jizhi, Fengzi }) b.AddSkill(Production.Value.GetSkill(id));
            object A(string id, int targets, params object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = targets,
                maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            object Pause() => new { op = "chooseOption", target = "owner", resultBind = "paid-child", options = new[] { new { id = "continue" } } };
            var source = new Dictionary<string, object> { ["id"] = Attack, ["revision"] = 1,
                ["damageModifiers"] = new[] { new { id = "real-five-point-slash", cardKinds = new[] { "slash" }, amount = 4, condition = "always", sourceScope = "ownerUsed" } } };
            if (scene == "live") source["triggers"] = new[] { new { id = "actual-ending-recovery", window = "playEnding", subject = "owner", optional = false,
                usageScope = "game", usageLimit = 1, effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "anyWounded" }, new { op = "recover", target = "selectedTarget", amount = 1 } } } };
            var definitions = new object[] {
                new { id = Driver, revision = 1, activations = new[] { A("request", 1, new { op = "requestSlashByTarget", target = "selectedTarget", resultBind = "native-request" }),
                    A("self-five", 0, new { op = "damage", target = "owner", amount = 5 }), A("armor-five", 0, new { op = "grantArmor", target = "owner", amount = 5 }),
                    A("lose-one", 0, new { op = "loseHp", target = "owner", amount = 1 }),
                    A("lose-conversion", 0, new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Skill }, sourceBind = "standard:none" }) } },
                source,
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "bounded-native-quiet-turn", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = DamageWatch, revision = 1, triggers = new[] { new { id = "actual-payment", window = "afterDamageApplied", subject = "owner", damageOccurrence = "perDamage", optional = false, effects = new[] { Pause() } } } },
                new { id = RecoverWatch, revision = 1, triggers = new[] { new { id = "actual-recovery", window = "afterHpRecovered", subject = "owner", optional = false, effects = new[] { Pause() } } } }
                ,new { id = Independent, revision = 1, triggers = new[] { new { id = "independent-whole-use", window = "cardUseBeforeTargetEffects", ownerRelation = "target",
                    cardCategories = new[] { "basic" }, optional = true, requiredPolarity = "yang", priority = -10,
                    effects = new object[] { new { op = "commitConversionPolarity", target = "owner" }, new { op = "nullifyCurrentCardUse", target = "owner" } } } } }
                ,new { id = HpGate, revision = 1, hpSkillGrants = new[] { new { id = "healthy-production-conversion", skillId = Skill, minimumHp = 2 } } }
                ,new { id = PaidLoss, revision = 1, triggers = new[] { new { id = "paid-after-whole-use", window = "cardUseBeforeTargetEffects", ownerRelation = "target",
                    cardCategories = new[] { "basic" }, optional = false, priority = -100, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "loseHp", target = "owner", amount = 7 }, Pause() } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实篡朝测试操作", description = "原生实体杀请求、护甲和真实失血；不注入极性" },
                [Quiet] = new { name = "安静原生同行", description = "跳过非测试角色的出牌阶段" }, [Attack] = new { name = "真实五伤来源", description = "实际杀附加四伤和真实结束回复" } };
            labels[Independent] = new { name = "独立转换机制对照", description = "不同ID的通用whole-use幂等来源，不是生产技能第二份" };
            labels[HpGate] = new { name = "实际健康HP授予", description = "只在至少两点体力时授予真实生产转换" };
            foreach (var id in new[] { DamageWatch, RecoverWatch, PaidLoss }) labels[id] = new { name = "真实已支付子窗口", description = "在原生伤害/回复付款后暂停",
                optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var actual = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in actual.Programs) b.AddSkill(new(id, actual.Presentations[id].Name, actual.Presentations[id].Description)
                { Program = program, ProgramPresentation = actual.Presentations[id], Tags = id == Independent ? SkillTag.Conversion : SkillTag.None });
            b.AddSkill(new(Suppress, "HP11实际失效", "既有共享抑制规则") { SuppressionRule = new(11) });
            b.AddGeneral(new(Owner, "真实篡朝使用者", "", Driver, "qun", 12)); b.AddGeneral(new(Peer, "真实安静同行", "", Quiet, "qun", 12));
            b.AddGeneral(new(Source, "实际杀请求来源", "", Attack, "wei", 12));
            var delayed = CardCatalog.Get(CardKind.Lightning); b.AddCard(new("fixture:cuanchao-lightning", delayed.DisplayName, delayed.CategoryName, delayed.Description, CardKind.Lightning));
            b.AddDeck(new(Deck, "固定无自然摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实整牌无效与转换", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer, Source], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = scene == "rescue" ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = scene == "rescue" ? 2 : 3 }));
        }
    }
}
