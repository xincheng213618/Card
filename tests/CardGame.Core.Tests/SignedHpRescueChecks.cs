using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class SignedHpRescueChecks
{
    private const string Owner = "fixture:signed-hp-owner", Victim = "fixture:signed-hp-victim", Enemy = "fixture:signed-hp-enemy";
    private const string Driver = "fixture:signed-hp-driver", Watch = "fixture:signed-hp-completed", Quiet = "fixture:signed-hp-quiet", Deck = "fixture:signed-hp-deck", Gate = "fixture:signed-hp-source-threshold";
    private const string Dangtou = StandardShanheCampaignCatalog.CurrentDangtou2SkillId, Chunlao = "classic:chunlao", BoundaryChunlao = "boundary:chunlao";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new ShanheBattleSetup(83, 1, "fixture:signed-hp-production", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('d', 64))
        { TacticSkillIds = [Dangtou] }));

    public static void PhysicalSlashDebtNeedsSameLastResponderPeachesAndReplaysCold()
    {
        foreach (var armor in new[] { 0, 1 })
        {
            var s = Scene.Start("slash", armor); s.Play();
            if (armor != 0) s.Use("armor-one");
            var slash = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
            var peaches = s.V().Hand.Where(c => c.Kind == CardKind.Peach).Select(c => c.Id).ToArray();
            s.Slash(slash); s.Rescue(); var dying = s.Dying;
            var damage = s.E<DamageAppliedEvent>().Single();
            Need(dying is { VictimSeat: 1, ResponderIndex: 3, Continuation: DyingContinuationKind.Damage } && dying.ResponderSeats.SequenceEqual([1, 2, 3, 0]) &&
                damage is { SourceSeat: 0, TargetSeat: 1, Amount: 3 } && damage.RemainingHp == armor - 2 && s.V(1).Hp == armor - 2 && s.V(1).IsAlive &&
                s.E<DamageRequestedEvent>().Single().ResolutionId == dying.ParentFrameId && s.E<CardUseDeclaredEvent>().Count(e => e.CardId == slash && e.CardKind == CardKind.Slash) == 1,
                "One original first-round Slash really deals three, retains negative HP as rescue debt, and reaches the last living clockwise responder. " + s.Diagnostic());
            if (armor == 0) Need(damage.ArmorPayment is null, "Zero Armor keeps the ordinary damage receipt shape.");
            else Need(damage.ArmorPayment is { ArmorBefore: 1, ArmorAbsorbed: 1, ArmorAfter: 0, HpBefore: 1, HpLossAmount: 2, UnclampedHpAfter: -1, HpAfter: -1, ActualHpLost: 2 },
                "Numeric Armor pays before signed HP and leaves one fewer Peach of debt without clipping the complete three-point damage.");
            s.Privacy(); s.RejectForeign(); s.Cold();
            s.PayPeaches(dying.Id, peaches, 3 - armor, armor - 2);
            Need(s.E<DyingResponseEvent>().Count(e => e.ResolutionId == dying.Id && e.ResponderSeat == 0 && e.UsedPeach) == 3 - armor &&
                s.E<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id).Survived && s.V(1) is { Hp: 1, IsAlive: true, Armor: 0 } &&
                s.V().Hand.Count(c => c.Kind == CardKind.Peach) == armor && s.E<DamageAppliedEvent>().Length == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == slash && e.CardKind == CardKind.Slash) == 1 && s.G.ResolutionStack.All(f => f is not DyingFrame),
                "The same last responder pays exactly three Peaches, or two after Armor, before the one original Slash and Dying occurrence return. " + s.Diagnostic());
            s.AssertUseCost(slash, CardLocation.Hand(0)); s.Cold();
        }
    }

    public static void NativeLoseHpBypassesArmorAndPassAdvancesTheRescueCursorCold()
    {
        foreach (var pass in new[] { false, true })
        {
            var s = Scene.Start("loss", 2); s.Play(); s.Use("armor-two");
            var peaches = s.V().Hand.Where(c => c.Kind == CardKind.Peach).Select(c => c.Id).ToArray();
            s.Begin("lose-three"); s.Rescue(); var dying = s.Dying;
            var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
            Need(dying is { Continuation: DyingContinuationKind.ProgramSkill, ResponderIndex: 3 } && dying.ParentFrameId == root.Id && s.V(1) is { Hp: -2, Armor: 2, IsAlive: true } &&
                s.E<ProgramSkillHpLostEvent>().Single(e => e.FrameId == root.Id) is { TargetSeat: 1, Amount: 3, RemainingHp: -2 } && s.E<DamageAppliedEvent>().Length == 0,
                "A real generic LoseHp three pays all three HP through Armor and suspends on its exact program-owned Dying child. " + s.Diagnostic());
            s.Cold();
            if (!pass)
            {
                s.PayPeaches(dying.Id, peaches, 3, -2);
                Need(s.V(1) is { Hp: 1, Armor: 2, IsAlive: true } && s.E<ProgramSkillResolvedEvent>().Single(e => e.FrameId == root.Id).Completed &&
                    s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == root.Id) == 1 && s.G.ResolutionStack.All(f => f.Id != root.Id),
                    "Three actual Peaches repay signed native HP loss, then resume the existing producer once without spending Armor or losing its return. " + s.Diagnostic());
            }
            else
            {
                s.Peach(peaches[0]); s.Completed(dying.Id, peaches[0], CardKind.Peach, -1, CardLocation.Hand(0)); s.Cold(); s.Continue(); s.Rescue();
                Need(s.Dying.ResponderIndex == 3 && s.V(1).Hp == -1, "One paid but insufficient Peach keeps the same last responder before an explicit pass.");
                s.Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                s.Until(() => s.E<DyingResolvedEvent>().Any(e => e.ResolutionId == dying.Id)); s.Play();
                Need(s.E<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id) is { Survived: false } && !s.V(1).IsAlive && s.G.State.Winner == Winner.None &&
                    s.E<DyingResponseEvent>().Count(e => e.ResolutionId == dying.Id && e.ResponderSeat == 0 && e.UsedPeach) == 1 &&
                    s.E<DyingResponseEvent>().Last(e => e.ResolutionId == dying.Id) is { ResponderSeat: 0, UsedPeach: false, UsedAlcohol: false } &&
                    s.V().Hand.Count(c => c.Kind == CardKind.Peach) == 2 && peaches.Skip(1).All(id => !s.G.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0))) &&
                    s.E<ProgramSkillResolvedEvent>().Single(e => e.FrameId == root.Id).Completed && s.G.ResolutionStack.All(f => f.Id != root.Id),
                    "Passing advances the last responder and really kills the still-negative victim; unspent Peaches remain private hand entities and the paid HP-loss parent returns once. " + s.Diagnostic());
            }
            s.Privacy(); s.Cold();
        }
    }

    public static void PaidProgramAlcoholCanContinueTheSameResponderAndReplaysCold()
    {
        var s = Scene.Start("chunlao", 0); s.Play();
        var stored = s.V().Hand.Select(c => c.Id).ToArray();
        Need(stored.Length == 3 && s.V().Hand.All(c => c.Kind == CardKind.Slash), "The rescue provider starts with three real distinct Slash materials, not synthetic pile cards.");
        s.EndPlay(); s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == "store-chun-at-turn-end") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
        foreach (var id in stored) s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Cards.SequenceEqual([id]));
        s.FinishOwnedSelectionIfPublished(); s.Play();
        Need(s.G.State.TurnNumber > 1 && s.V().ChunlaoCards!.Select(c => c.Id).Order().SequenceEqual(stored.Order()) &&
            stored.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Chunlao(0)) == 1),
            "A real previous owner Ending stores the original three entities and natural peer turns return to the next real owner Play."); s.Cold();
        s.Begin("damage-three"); s.Rescue(); var dying = s.Dying;
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        Need(s.V(1).Hp == -2 && dying.ResponderIndex == 3 && s.E<DamageAppliedEvent>().Single() is { Amount: 3, RemainingHp: -2 }, "The bound-pile rescue begins at the genuine last-provider negative-HP damage window.");
        var frames = new List<long>();
        for (var i = 0; i < stored.Length; i++)
        {
            s.Answer(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Chunlao);
            s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card") == true);
            var program = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chunlao);
            frames.Add(program.Id);
            Need(program.OwnerSeat == 0 && program.WindowContext is { Window: SkillProgramTriggerWindow.DyingResponse, TargetSeat: 1, ParentFrameId: var parent } && parent == dying.Id &&
                s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card").SelectMany(c => c.Cards).Order().SequenceEqual(stored.Skip(i).Order()),
                "Each repeated genuine program binding offers only the remaining real Chun entities on the same provider's private rescue cursor. " + s.Diagnostic());
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card" && c.Cards.SequenceEqual([stored[i]]));
            s.Completed(dying.Id, stored[i], CardKind.Alcohol, i - 1, CardLocation.Chunlao(0));
            var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == stored[i]);
            Need(use.DyingResponse is null && use.SourceSeat == 1 && use.TargetSeats.SequenceEqual([1]) &&
                use.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 0, EffectiveKind: CardKind.Alcohol } action &&
                action.PhysicalCards is [var cost] && cost.CardId == stored[i] && cost.From == CardLocation.Chunlao(0) &&
                action.ConversionChain is [var conversion] && conversion.SkillId == Chunlao && conversion.OwnerSeat == 0 && conversion.SkillInstanceId == program.SkillInstanceId,
                "Every paid rescue retains the original provider/pile cost, the dying victim as Alcohol actor and the exact program-instance conversion rather than a fabricated native response. " + s.Diagnostic());
            s.Cold(); s.Continue();
            if (i < 2) s.Rescue(); else s.Until(() => s.E<DyingResolvedEvent>().Any(e => e.ResolutionId == dying.Id));
            Need(s.E<ProgramDyingRescueEvent>().Count(e => e.DyingFrameId == dying.Id && e.SkillId == Chunlao && e.OwnerSeat == 0) == i + 1 &&
                s.E<ProgramBindingResolvedEvent>().Where(e => e.FrameId == program.Id).ToArray() is [{ Activated: true, Completed: true }] &&
                (i == 2 || s.Dying.ResponderIndex == 3),
                "A real paid ProgramDyingRescue receipt permits the same last provider to spend again while HP is nonpositive; every old binding completes before the next is offered. " + s.Diagnostic());
            s.Cold();
        }
        s.Play();
        Need(s.V(1) is { Hp: 1, IsAlive: true } && s.V().ChunlaoCount == 0 && frames.Distinct().Count() == 3 &&
            s.E<ProgramDyingRescueEvent>().Where(e => e.DyingFrameId == dying.Id).Select(e => (e.CardId, e.RecoveredHp, e.VictimHp)).SequenceEqual(stored.Select((id, i) => (id, 1, i - 1))) &&
            s.E<RecoveryAppliedEvent>().Select(e => e.RemainingHp).SequenceEqual([-1, 0, 1]) && s.E<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id).Survived &&
            s.E<ProgramSkillResolvedEvent>().Single(e => e.FrameId == root.Id).Completed && s.E<DamageAppliedEvent>().Length == 1 &&
            stored.All(id => s.E<CardUseDeclaredEvent>().Count(e => e.CardId == id && e.CardKind == CardKind.Alcohol) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.CardId == id && e.CardKind == CardKind.Alcohol) == 1) &&
            s.G.ResolutionStack.All(f => f is not DyingFrame && f.Id != root.Id && !frames.Contains(f.Id)),
            "Three original public Chun materials pay three independent genuine Alcohol uses and signed recoveries, then finish the single original damage and rescue parents exactly once. " + s.Diagnostic());
        foreach (var id in stored) s.AssertUseCost(id, CardLocation.Chunlao(0)); s.Privacy(); s.Cold();
        VerifyRoundPricedBoundaryAlcohol();
        VerifyPaidAlcoholSourceLossKeepsPhysicalRescueCursor();
    }

    private static void VerifyPaidAlcoholSourceLossKeepsPhysicalRescueCursor()
    {
        var s = Scene.Start("paid-loss", 0); s.Play();
        var stock = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; var peaches = s.V().Hand.Where(c => c.Kind == CardKind.Peach).Select(c => c.Id).ToArray();
        Need(s.V().Skills?.Any(k => k.ContentId == Chunlao) == true, "The original live HP-threshold source really grants the production rescue skill.");
        s.EndPlay(); s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == "store-chun-at-turn-end") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Cards.SequenceEqual([stock]));
        s.FinishOwnedSelectionIfPublished(); s.Play(); s.Begin("damage-three"); s.Rescue(); var dying = s.Dying;
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Chunlao);
        s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card") == true);
        var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chunlao).Id;
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card" && c.Cards.SequenceEqual([stock]));
        s.Completed(dying.Id, stock, CardKind.Alcohol, -1, CardLocation.Chunlao(0));
        Need(s.V().Hp == 3 && s.V().Skills?.Any(k => k.ContentId == Chunlao) != true &&
            s.E<ProgramSkillHpLostEvent>().Single(e => e.SkillId == Watch) is { TargetSeat: 0, Amount: 1, RemainingHp: 3 },
            "The paid Alcohol's genuine completion child changes HP and withdraws its original derived skill instance before that binding can return.");
        s.Cold(); s.Continue(); s.Rescue();
        Need(s.Dying.Id == dying.Id && s.Dying.ResponderIndex == 3 && s.V(1).Hp == -1 &&
            s.E<ProgramDyingRescueEvent>().Single(e => e.DyingFrameId == dying.Id) is { SkillId: Chunlao, OwnerSeat: 0, VictimSeat: 1, RecoveredHp: 1, VictimHp: -1 } &&
            s.E<ProgramBindingResolvedEvent>().Where(e => e.FrameId == frame).ToArray() is [{ Activated: true, Completed: true }] &&
            s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("skill-id") != Chunlao),
            "The accepted HP-conditional source completes its already-paid rescue exactly once after withdrawal; the same living responder retains the remaining Peaches, and the withdrawn skill cannot be offered again. " + s.Diagnostic());
        s.PayPeaches(dying.Id, peaches, 2, -1);
        Need(s.V(1) is { Hp: 1, IsAlive: true } && s.E<ProgramDyingRescueEvent>().Length == 1 &&
            s.E<DyingResponseEvent>().Count(e => e.ResolutionId == dying.Id && e.ResponderSeat == 0 && e.UsedPeach) == 2 &&
            s.E<RecoveryAppliedEvent>().Select(e => e.RemainingHp).SequenceEqual([-1, 0, 1]) && s.E<DamageAppliedEvent>().Length == 1,
            "The two remaining original Peaches finish the same signed debt after a paid but source-lost program rescue, without refunding Alcohol or advancing past its provider."); s.Cold();
    }

    private static void VerifyRoundPricedBoundaryAlcohol()
    {
        var s = Scene.Start("boundary", 0); s.Play(); var stock = s.V().Hand.Select(c => c.Id).ToArray();
        s.Begin("stock-three", []); s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true);
        foreach (var id in stock) s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Cards.SequenceEqual([id]));
        s.FinishOwnedSelectionIfPublished(); s.Play();
        Need(stock.Length == 3 && s.V().ChunlaoCards!.Select(c => c.Id).Order().SequenceEqual(stock.Order()) &&
            s.E<ProgramAdjacentDiscardStoredEvent>().Select(e => e.CardId).Order().SequenceEqual(stock.Order()) &&
            stock.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DiscardPile && m.To == CardLocation.Chunlao(0)) == 1),
            "The genuine boundary skill stores three actually discarded Slash entities before rescue; none are injected directly into a pile."); s.Cold();
        s.Begin("damage-two"); s.Rescue(); var dying = s.Dying;
        Need(s.V(1).Hp == -1 && dying.ResponderIndex == 3, "Two-point real damage creates the boundary provider's last-responder two-recovery debt.");
        var offset = 0; var programs = new List<long>();
        for (var price = 1; price <= 2; price++)
        {
            s.Answer(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == BoundaryChunlao);
            s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "round-pile-alcohol") == true);
            var program = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == BoundaryChunlao); programs.Add(program.Id);
            Need(program.RoundPileAlcohol is { Stage: RoundPileAlcoholStage.Choosing } r && r.Price == price && r.DyingFrameId == dying.Id && r.VictimSeat == 1,
                "The same real Dying occurrence offers the live next round price, rather than advancing past a provider with payable stock. " + s.Diagnostic());
            var materials = stock.Skip(offset).Take(price).ToArray();
            foreach (var id in materials) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "material" && c.Cards.SequenceEqual([id]));
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "use");
            s.Completed(dying.Id, materials[0], CardKind.Alcohol, price - 1, CardLocation.Chunlao(0));
            var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == materials[0]);
            Need(use.RoundPileAlcoholReturn?.ProgramFrameId == program.Id && use.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 0, EffectiveKind: CardKind.Alcohol } action &&
                action.PhysicalCards.Select(c => c.CardId).SequenceEqual(materials) && action.PhysicalCards.All(c => c.From == CardLocation.Chunlao(0)) &&
                s.E<ProgramRoundPileAlcoholIssuedEvent>().Single(e => e.ProgramFrameId == program.Id) is { DyingFrameId: var parent, Price: var issuedPrice, VictimSeat: 1 } && parent == dying.Id && issuedPrice == price &&
                s.E<ProgramRoundPileAlcoholMaterialPaidEvent>().Count(e => e.ProgramFrameId == program.Id) == price && s.E<ProgramDyingRescueEvent>().Length == 0,
                "The boundary branch proves rescue payment by its exact issued native Alcohol and one/two real costs, independently of the classic ProgramDyingRescue event. " + s.Diagnostic());
            foreach (var id in materials) s.AssertUseCost(id, CardLocation.Chunlao(0)); s.Cold(); s.Continue();
            if (price == 1) s.Rescue(); else s.Until(() => s.E<DyingResolvedEvent>().Any(e => e.ResolutionId == dying.Id));
            Need(s.E<ProgramBindingResolvedEvent>().Where(e => e.FrameId == program.Id).ToArray() is [{ Activated: true, Completed: true }] &&
                (price != 1 || s.Dying.Id == dying.Id && s.Dying.ResponderIndex == 3), "Each paid boundary program returns before the same responder's next real priced rescue. " + s.Diagnostic());
            offset += price; s.Cold();
        }
        s.Play(); Need(s.V(1) is { Hp: 1, IsAlive: true } && s.V().ChunlaoCount == 0 &&
            s.E<RecoveryAppliedEvent>().Select(e => e.RemainingHp).SequenceEqual([0, 1]) && s.E<ProgramRoundPileAlcoholIssuedEvent>().Select(e => e.Price).SequenceEqual([1, 2]) &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardKind == CardKind.Alcohol) == 2 && s.E<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id).Survived &&
            s.G.ResolutionStack.All(f => f is not DyingFrame && !programs.Contains(f.Id)),
            "Two real priced Alcohol uses consume exactly three original stock cards, repay minus one through zero to one, and return the single native Dying frame without a third price or repeated cost. " + s.Diagnostic()); s.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public DyingFrame Dying => G.ResolutionStack.OfType<DyingFrame>().Single();
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real signed-HP command."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P; var c = p?.Choices.SingleOrDefault(predicate);
            Need(p is { PlayerSeat: 0 } && c is not null, "The human must answer an actually published private choice. " + Diagnostic());
            Accept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, G.Revision));
        }
        public void Begin(string activation, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [1], G.Revision, P!.PromptId));
        public void Use(string activation) { Begin(activation); Play(); }
        public void Slash(int id)
        {
            var a = G.GetHumanLegalActions().SingleOrDefault(a => a.CardId == id && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
            Need(a is not null, "The actual adjacent ally must be a legal physical Slash target. " + Diagnostic());
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Peach(int id) => Answer(c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.SequenceEqual([id]));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void FinishOwnedSelectionIfPublished()
        {
            if (P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
                Answer(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        }
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Rescue() => Until(() => P is { Kind: DecisionKind.RescueDying, PlayerSeat: 0, TargetSeat: 1 });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Until(Func<bool> done)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Kind == DecisionKind.DiscardCards) { Accept(new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision)); continue; }
                    throw new InvalidOperationException("Unexpected human signed-HP boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The small signed-HP battle must remain live. " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded signed-HP native boundary was not reached: " + Diagnostic());
        }
        public void Completed(long dying, int card, CardKind kind, int hp, CardLocation from)
        {
            Until(() => P?.SkillPrompt?.SkillId == Watch);
            var use = G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == card && f.CardKind == kind);
            var window = G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.ParentFrameId == use.Id);
            var watch = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Watch);
            Need(Dying.Id == dying && Dying is { VictimSeat: 1, ResponderIndex: 3, ResponderSeat: 0 } && V(1).Hp == hp && V(1).IsAlive &&
                use.Step == ResolutionFrameStep.Completed && window.Continuation == ProgramCardContinuation.CompletedCard &&
                watch.OwnerSeat == 0 && watch.WindowContext?.ParentFrameId == window.Id && E<RecoveryAppliedEvent>().Last() is { TargetSeat: 1, Amount: 1 } recovery && recovery.RemainingHp == hp &&
                E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == card && e.CardKind == kind) == 1 &&
                E<DyingResolvedEvent>().All(e => e.ResolutionId != dying),
                "Each genuine recovery completes its paid Use and pauses on the human observer before returning to the exact unresolved last-responder Dying frame. " + Diagnostic());
            if (kind == CardKind.Peach) Need(use.DyingResponse is { ResponderSeat: 0, UsedPeach: true, PeachCardId: var id, ResolutionId: var parent } && id == card && parent == dying,
                "The original physical Peach retains its exact native DyingResponse return ticket.");
            AssertUseCost(card, from);
        }
        public void PayPeaches(long dying, int[] cards, int count, int debt)
        {
            for (var i = 0; i < count; i++)
            {
                Peach(cards[i]); Completed(dying, cards[i], CardKind.Peach, debt + i + 1, CardLocation.Hand(0)); Privacy(); Cold(); Continue();
                if (i + 1 < count) { Rescue(); Need(Dying.Id == dying && Dying.ResponderIndex == 3 && V(1).Hp == debt + i + 1, "A paid Peach at nonpositive HP retains the same actual last responder."); }
                else Until(() => E<DyingResolvedEvent>().Any(e => e.ResolutionId == dying));
                Need(E<DyingResponseEvent>().Count(e => e.ResolutionId == dying && e.ResponderSeat == 0 && e.UsedPeach) == i + 1, "Each real native Peach response is recorded exactly once after its completed-card child returns."); Cold();
            }
            Play();
        }
        public void AssertUseCost(int id, CardLocation from) => Need(G.CardMovements.Count(m => m.CardId == id && m.From == from && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Each original rescue or damage material pays one physical source→Processing→Discard path without duplicated cost or cleanup. " + Diagnostic());
        public void RejectForeign()
        {
            var p = P!; var before = State(G); var choice = p.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach");
            Need(!G.Submit(new AnswerPromptCommand(2, p.PromptId, choice.Id, G.Revision)).Accepted && State(G) == before, "A foreign seat cannot spend the last responder's private Peach or alter its signed debt.");
        }
        public void Privacy() => Need(Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)) &&
            Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null), "Physical hand choices stay private at negative HP and inside the genuine responder's child windows.");
        public void Cold() { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold command replay preserves signed HP, exact rescue cursor, original physical payments, typed owning children and each private viewer without replaying a rescue."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { scene, G.State.TurnNumber, G.State.Winner, Players = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.Armor, p.IsAlive, p.HandCount, p.ChunlaoCount }),
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, P.TargetSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(c => new { c.Id, c.Cards, Response = c.Parameters.GetValueOrDefault("response"), Action = c.Parameters.GetValueOrDefault("program-action") }) },
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step, SkillId = (f as ProgramSkillFrame)?.SkillId,
                TriggerId = (f as ProgramSkillFrame)?.TriggerId, InstructionIndex = (f as ProgramSkillFrame)?.InstructionIndex }),
            BindingResolved = E<ProgramBindingResolvedEvent>(), Rescue = E<DyingResponseEvent>(), Program = E<ProgramDyingRescueEvent>(), Recovery = E<RecoveryAppliedEvent>() });
        public static Scene Start(string name, int armor)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(name));
            ShanheStartingCard[] cards = name is "chunlao" or "boundary" ? Enumerable.Range(0, 3).Select(i => new ShanheStartingCard(CardKind.Slash, Suit.Spade, i + 3)).ToArray() : name == "paid-loss" ?
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.Peach, Suit.Heart, 5)] :
                [new(CardKind.Slash, Suit.Spade, 7), .. Enumerable.Range(0, 3).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, i + 4))];
            var setup = new ShanheBattleSetup(83, 1, "fixture:signed-hp-" + name + "-" + armor, Owner, 4, 0, 0, 0, 0, [], cards, [],
                [new(Enemy) { StartingHand = [] }, new(Enemy) { StartingHand = [] }], [new(Victim, Hp: 1) { StartingHand = [] }], new string('e', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false,
                MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r, name); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-signed-hp-rescue", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            b.AddSkill(Production.Value.GetSkill(Dangtou)); b.AddSkill(Production.Value.GetSkill(Chunlao)); b.AddSkill(Production.Value.GetSkill(BoundaryChunlao));
            var watcherKinds = scene == "paid-loss" ? "\"peach\"" : "\"peach\",\"alcohol\"";
            var paidLossWatcher = scene == "paid-loss" ? """
                ,{"id":"real-paid-alcohol-source-loss","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"cardKinds":["alcohol"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"lost-source-child","options":[{"id":"continue"}]}]}
                """ : "";
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"armor-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":1}]},
                    {"id":"armor-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":2}]},
                    {"id":"lose-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},
                    {"id":"damage-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3}]},
                    {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                    {"id":"stock-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":3,"zones":["hand"],"resultBind":"stock"},{"op":"moveBoundCards","target":"owner","sourceBind":"stock","destination":"discardPile","awaitMovementTriggers":true}]}]},
                  {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-rescue-use-completed","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"cardKinds":[{{watcherKinds}}],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-rescue-child","options":[{"id":"continue"}]}]}{{paidLossWatcher}}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet-native-turn","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Gate}}","revision":1,"hpSkillGrants":[{"id":"real-chunlao-hp-source","skillId":"{{Chunlao}}","minimumHp":4}]}
                ]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Watch, Quiet, Gate }.ToDictionary(id => id, id => new { name = id, description = "真实负体力、多桃与醇醪付款子流程",
                    optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "真实最后救援者", "", Driver, "wu", 4, [Watch, scene == "slash" ? Dangtou : scene == "chunlao" ? Chunlao : scene == "boundary" ? BoundaryChunlao : scene == "paid-loss" ? Gate : "standard:none"]));
            b.AddGeneral(new(Victim, "真实一血受伤者", "", Quiet, "wu", 4)); b.AddGeneral(new(Enemy, "真实空手他方", "", Quiet, "wei", 4));
            b.AddDeck(new(Deck, "固定无治疗自然牌、零自然摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实负体力连续救援", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Victim, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
