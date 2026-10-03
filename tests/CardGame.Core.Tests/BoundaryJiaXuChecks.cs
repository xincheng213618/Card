using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryJiaXuChecks
{
    private const string Driver = "fixture:jx-driver";
    private const string Gain = "fixture:jx-gain";
    private const string Entry = "fixture:jx-entry";
    private const string Loss = "fixture:jx-source-loss";
    private const string Ordinary = "fixture:jx-ordinary";
    private const string Locked = "fixture:jx-locked";
    private const string Limited = "fixture:jx-limited";
    private const string Later = "fixture:jx-later";
    private const string ForeignGrant = "fixture:jx-foreign-grant";
    private const string Mode = "identity:classic-boundary-jia-xu-fixture";
    private const string Luanwu = "nearest-slashes-and-unlimited-final-slash";

    public static void PreventionUsesExactAmountAndReturnsThroughRealDrawChild()
    {
        var invalid = $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid-prevention-subject","revision":1,
        "triggers":[{"id":"invalid","window":"beforeDamageApplied","subject":"owner","optional":false,
        "effects":[{"op":"preventCurrentDamageAndDrawMultiple","target":"owner","amount":2}]}]}]}
        """;
        var rejected = false;
        try { SkillProgramCatalog.Load(invalid, "{\"schemaVersion\":3,\"skills\":{\"fixture:invalid-prevention-subject\":{\"name\":\"invalid\",\"description\":\"invalid\"}}}"); }
        catch (InvalidOperationException e) when (e.Message.Contains("damage-target window", StringComparison.Ordinal)) { rejected = true; }
        Require(rejected, "The strict production loader rejects an owner subject that cannot own the exact prevented damage target.");
        foreach (var effect in new[]
        {
            "{\"op\":\"requestLegalSlashByNearest\",\"target\":\"owner\",\"targetKind\":\"otherLiving\",\"amount\":1}",
            "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"offerUnlimitedVirtualSlash\",\"target\":\"owner\"}"
        })
        {
            var invalidTrigger = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid-slash-trigger","revision":1,
                "triggers":[{"id":"invalid","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{{effect}}]}]}]}
                """;
            rejected = false;
            try { SkillProgramCatalog.Load(invalidTrigger, "{\"schemaVersion\":3,\"skills\":{\"fixture:invalid-slash-trigger\":{\"name\":\"invalid\",\"description\":\"invalid\"}}}"); }
            catch (InvalidOperationException e) when (e.Message.Contains("active play program", StringComparison.Ordinal)) { rejected = true; }
            Require(rejected, "An activation-only Slash sequence is rejected before an unsupported trigger can execute.");
        }
        var (g, r) = Create(outside: true); var hp = g.State.Players[0].Hp;
        var hand = g.CreateSnapshot(0).Players[0].Hand.Count;
        Use(g, "incoming", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var prevention = Events<ProgramDamagePreventedEvent>(g).Single(e => e.SkillId == "boundary:weimu");
        var parent = g.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(f => f.Id == prevention.FrameId);
        Require(parent.Prevented && parent.Amount == 2 && prevention is { OwnerSeat: 0, SourceSeat: 1, TargetSeat: 0, Amount: 2 } &&
            g.State.Players[0].Hp == hp && g.CreateSnapshot(0).Players[0].Hand.Count == hand + 4,
            "The original exact two-damage parent is prevented before four real cards enter the owner's hand.");
        var paid = g.CardMovements.Where(m => m.To == CardLocation.Hand(0) &&
            m.Reason.Value == "skill-program.boundary:weimu.prevented-damage-draw").Select(m => m.CardId).ToArray();
        Require(paid.Length == 4 && paid.Distinct().Count() == 4 && parent.Id ==
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:weimu").WindowContext!.ParentFrameId,
            "The draw observer suspends the original prevention instruction after its exact payment, with no parallel pending state.");
        Private(g); Reject(g); Cold(g, r); Continue(g); Finish(g);
        Require(Events<ProgramDamagePreventedEvent>(g).Count(e => e.SkillId == "boundary:weimu") == 1 &&
            paid.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Hand(0)) == 1) &&
            !Events<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0),
            "The real draw child returns without preventing or drawing twice, and no original HP damage is applied.");
        Use(g, "lose-one"); Finish(g);
        Require(g.State.Players[0].Hp == hp - 1 && Events<ProgramDamagePreventedEvent>(g).Length == 1,
            "Actual HP loss is not a damage attempt and is never prevented by Curtain.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:jx-outside" && Action(p, "select-target"));
        Answer(g, c => c.Targets.SequenceEqual([0]));
        ReachUntil(g, () => Events<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0));
        Require(g.State.Players[0].Hp == hp - 2 && Events<ProgramDamagePreventedEvent>(g).Length == 1,
            "A real damage attempt from the next actor during that actor's draw phase damages the Curtain owner outside its own turn.");
        Cold(g, r);

        var (nested, nr) = Create(cardId: "standard:peach", gainDying: true);
        LuanwuUse(nested); Reach(nested, Nearest);
        var slash = P(nested)!.Choices.First(c => c.Cards.Count == 2 && c.Targets.SequenceEqual([0])); var costs = slash.Cards.ToArray();
        Answer(nested, c => c.Id == slash.Id); Reach(nested, p => p.SkillPrompt?.SkillId == Entry && p.PlayerSeat == 0);
        var original = Events<CardActionAcceptedEvent>(nested).Single(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(original.PhysicalCards.Select(c => c.CardId).SequenceEqual(costs) && nested.State.Players[0].Hp == 0 &&
            nested.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single() is { Prevented: true, Amount: 1, TargetSeat: 0 } &&
            nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:weimu").PreventionDrawReceipt is { ActualDrawCount: 2, PreventedAmount: 1 },
            "An actual converted Slash is already prevented and its two-card draw receipt stays on the original before-damage parent while a gain observer causes Dying.");
        Cold(nested, nr); Continue(nested); Reach(nested, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0);
        Cold(nested, nr); Answer(nested, c => c.Parameters.GetValueOrDefault("response") == "peach"); Reach(nested, p => p.SkillPrompt?.SkillId == Gain);
        Cold(nested, nr); Continue(nested); Reach(nested, p => Nearest(p) && p.PlayerSeat == 2);
        Require(nested.State.Players[0].Hp == 1 && Events<DyingResponseEvent>(nested).Any(e => e.ResponderSeat == 0 && e.UsedPeach) &&
            Events<ProgramDamagePreventedEvent>(nested).Count(e => e.SkillId == "boundary:weimu") == 1 &&
            !Events<DamageAppliedEvent>(nested).Any(e => e.TargetSeat == 0) &&
            costs.All(id => nested.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.Use) == 1 &&
                nested.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished) == 1) &&
            nested.CardMovements.Count(m => m.Reason.Value == "skill-program.boundary:weimu.prevented-damage-draw") == 2,
            "Entry, real Peach and gain children return through the exact physical Slash parent without applying its prevented damage, redrawing or repaying either material.");
        DeclineRemaining(nested); Finish(nested); Cold(nested, nr);
    }

    public static void BlackTricksUseActualColorAndLightningTransferContext()
    {
        var (black, br) = Create(cardId: "standard:duel", suit: Suit.Spade, curtain: true);
        Require(black.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([2])) &&
            !black.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.Contains(1)),
            "A physical black Duel cannot target Curtain, while the same actual card can target an unprotected actor."); Cold(black, br);
        var (rewrite, rr) = Create(cardId: "standard:duel", suit: Suit.Spade, curtain: true, rewrite: true);
        Play(rewrite, LegalActionKind.Duel, [1]); Finish(rewrite);
        Require(Events<CardActionAcceptedEvent>(rewrite).Any(e => e.Action is
            { Type: CardActionType.Use, ActorSeat: 0, EffectiveKind: CardKind.Duel, EffectiveIsRed: true } &&
            e.Action.TargetSeats.SequenceEqual([1])),
            "The actual source's suit rewrite produces a red Duel and passes the new color gate."); Cold(rewrite, rr);
        foreach (var id in new[] { "standard:draw_two", "standard:lightning" })
        {
            var (self, sr) = Create(cardId: id, suit: Suit.Club);
            var kind = id == "standard:draw_two" ? LegalActionKind.DrawTwo : LegalActionKind.Lightning;
            Require(self.CreateSnapshot(0).Players[0].Hand.Count > 0 && !self.GetHumanLegalActions().Any(a => a.Kind == kind),
                "Black self-targeted tricks also obey the universal target policy before any physical cost is paid."); Cold(self, sr);
        }
        var (global, gr) = Create(cardId: "standard:arrow_barrage", suit: Suit.Spade, curtain: true);
        Play(global, LegalActionKind.ArrowBarrage); Finish(global);
        Require(Events<CardActionAcceptedEvent>(global).Any(e => e.Action is
                { Type: CardActionType.Use, ActorSeat: 0, EffectiveKind: CardKind.ArrowBarrage, EffectiveIsRed: false } &&
                e.Action.TargetSeats.SequenceEqual([2, 3])) && !Events<DamageAppliedEvent>(global).Any(e => e.TargetSeat == 1),
            "A global black trick removes only its protected target and still resolves against its other declared actors."); Cold(global, gr);
        foreach (var mixedRed in new[] { false, true })
        {
            var (qice, qr) = Create(cardId: "standard:duel", suit: Suit.Spade, curtain: true, mixed: true, mixedRed: mixedRed);
            Use(qice, "draw-many"); Finish(qice); var cards = qice.CreateSnapshot(0).Players[0].Hand;
            Require(cards.Select(c => c.Suit).Distinct().Count() == 2, "The fixed real draw has more cards than either printed suit population.");
            Accept(qice, new UseProgramSkillCommand(0, "classic:qice", "all-hand-as-ordinary-trick", cards.Select(c => c.Id).ToArray(), [], qice.Revision, P(qice)!.PromptId));
            Reach(qice, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "use-all-hand-as-ordinary-trick"));
            var toCurtain = P(qice)!.Choices.Any(c => c.Targets.SequenceEqual([1]) && c.Parameters.GetValueOrDefault("card-kind") == CardKind.Duel.ToString());
            Require(toCurtain == mixedRed, "Mixed black suits retain actual black color; mixed red and black material is colorless.");
            Cold(qice, qr); Answer(qice, c => c.Targets.SequenceEqual([mixedRed ? 1 : 2]) && c.Parameters.GetValueOrDefault("card-kind") == CardKind.Duel.ToString()); Finish(qice);
            var use = Events<CardActionAcceptedEvent>(qice).Last(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Duel).Action;
            Require(use.EffectiveSuit is null && use.EffectiveIsRed == (mixedRed ? null : false) &&
                use.PhysicalCards.Count == cards.Count && use.ConversionChain.Single().SkillId == "classic:qice",
                "The accepted virtual trick freezes its actual all-hand material, absent suit and independent effective color.");
            Frozen(use.PhysicalCards); Frozen(use.TargetSeats); Frozen(use.ConversionChain); Cold(qice, qr);
        }
        var (lightning, lr) = Create(cardId: "standard:lightning", suit: Suit.Club, curtain: true);
        Use(lightning, "shed-curtain"); Finish(lightning); var delayed = Play(lightning, LegalActionKind.Lightning);
        Finish(lightning); Use(lightning, "gain-curtain"); Finish(lightning);
        Accept(lightning, new EndPlayPhaseCommand(0, lightning.Revision, P(lightning)!.PromptId));
        ReachUntil(lightning, () => Events<LightningResolvedEvent>(lightning).Any(e => e.CardId == delayed));
        var transfer = Events<LightningResolvedEvent>(lightning).Single(e => e.CardId == delayed);
        Require(!transfer.Hit && transfer.NextTargetSeat == 2 && lightning.CardMovements.Any(m =>
            m.CardId == delayed && m.From == CardLocation.Judgment(0) && m.To == CardLocation.Judgment(2) && m.Reason == CardMoveReasons.DelayedCardTransfer),
            "The actual held black delayed card skips the next Curtain target on transfer; a miss judgment's card is not its targeting color."); Cold(lightning, lr);
    }

    public static void DyingQualificationChangesOnlyCurrentForeignNonLockedGrants()
    {
        var (g, r) = Create(cardId: "standard:peach", grantAtEntry: true);
        Require(Enumerable.Range(0, 4).All(s => Has(g, s, Ordinary)), "The original ordinary grants all begin eligible.");
        Use(g, "dying-other", [1]); Reach(g, p => p.SkillPrompt?.SkillId == Entry && Action(p, "choose-option"));
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single();
        Require(dying.VictimSeat == 1 && Has(g, 0, Ordinary) && Has(g, 1, Ordinary) &&
            !Has(g, 2, Ordinary) && !Has(g, 3, Ordinary) && !Has(g, 2, Limited) && Has(g, 2, Locked),
            "The exact living current source and current victim keep ordinary skills; foreign actors lose nonLocked and Limited eligibility but retain Locked grants.");
        Cold(g, r); Continue(g); Reach(g, p => p.PlayerSeat == 2 && p.SkillPrompt?.SkillId == ForeignGrant && Action(p, "choose-option"));
        Require(Events<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 2 && e.SkillIds.Contains(Later)),
            "The foreign Locked entry observer really grants its own new ordinary skill during this Dying settlement.");
        Require(!Has(g, 2, Later) && Has(g, 2, Locked) && g.ResolutionStack.OfType<DyingFrame>().Any(f => f.Id == dying.Id),
            "A genuinely granted new foreign ordinary skill is dynamically unqualified in the still active exact Dying window."); Cold(g, r);
        Continue(g); Finish(g);
        Require(Events<DyingResponseEvent>(g).Any(e => e.ResolutionId == dying.Id && e.ResponderSeat == 1 && e.UsedPeach) &&
            Events<DyingResponseEvent>(g).Where(e => e.ResolutionId == dying.Id && e.UsedPeach).All(e => e.ResponderSeat is 0 or 1) &&
            g.State.Players[1].Hp == 1 && Enumerable.Range(0, 4).All(s => Has(g, s, Ordinary)) && Has(g, 2, Later) && Has(g, 2, Limited),
            "Only the source and victim can pay real Peaches, and closing the Dying frame restores all still enabled foreign grants, including the acquired one.");
        Cold(g, r); Use(g, "dying-owner"); Reach(g, p => p.SkillPrompt?.SkillId == Entry && Action(p, "choose-option"));
        Require(g.ResolutionStack.OfType<DyingFrame>().Single().VictimSeat == 0 && Has(g, 0, Ordinary) && !Has(g, 1, Ordinary) && !Has(g, 2, Later),
            "A later actual Dying victim changes the cached qualification frontier without retaining the earlier victim exemption.");
        Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == ForeignGrant); Continue(g);
        Reach(g, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0);
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach"); Finish(g);
        Require(g.State.Players[0].Hp == 1 && Enumerable.Range(0, 4).All(s => Has(g, s, Ordinary)),
            "The source/victim overlap can pay an actual Peach and the second closed frame restores foreign qualification again."); Cold(g, r);
    }

    public static void ChaoticAmbitionPaysConvertedAndProviderCardsBeforeOptionalFinisher()
    {
        var (g, r) = Create(cardId: "standard:peach", fragile: true);
        LuanwuUse(g); Reach(g, Nearest); Require(P(g)!.PlayerSeat == 1 && P(g)!.ValidTargetSeats.SequenceEqual([0, 2]),
            "The first real other actor receives both equal nearest seats in clockwise order.");
        var choice = P(g)!.Choices.First(c => c.Cards.Count == 2 && c.Targets.SequenceEqual([2])); var materials = choice.Cards.ToArray();
        Private(g); Reject(g); Cold(g, r); Answer(g, c => c.Id == choice.Id);
        Reach(g, p => Nearest(p) && p.PlayerSeat == 2);
        var converted = Events<CardActionAcceptedEvent>(g).Single(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 1 && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(converted.PhysicalCards.Select(c => c.CardId).SequenceEqual(materials) && converted.PhysicalCards.All(c => c.From == CardLocation.Hand(1)) &&
            converted.ConversionChain.Single().SkillId == "classic:fuhun" && materials.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.Use) == 1),
            "A real two-material view-as Slash owns both exact entities and resolves before the next actor is offered a choice.");
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("request-option") == "decline");
        Reach(g, p => Nearest(p) && p.PlayerSeat == 3); Answer(g, c => c.Parameters.GetValueOrDefault("request-option") == "decline");
        Reach(g, p => p.SkillPrompt?.SkillId == Entry); Cold(g, r); Continue(g);
        Reach(g, p => Action(p, "unlimited-virtual-slash"));
        Require(Events<DyingResponseEvent>(g).Any(e => e.ResponderSeat == 3 && e.UsedPeach) && g.State.Players[3].Hp == 1 &&
            P(g)!.Choices.Any(c => c.Targets.SequenceEqual([2])),
            "The final actor's real HP loss and rescue child return before the source receives its distant optional Slash.");
        var hp = g.State.Players[2].Hp; Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([2])); Finish(g);
        var final = Events<CardActionAcceptedEvent>(g).Last(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(final.PhysicalCards.Count == 0 && final.TargetSeats.SequenceEqual([2]) && g.State.Players[2].Hp == hp - 1 &&
            Events<ProgramUnlimitedSlashChoiceEvent>(g).Single() is { OwnerSeat: 0, TargetSeat: 2, Used: true } &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == "boundary:luanwu"),
            "The limited activation finishes with a real zero-entity distant Slash, ordinary damage, and no second game-wide activation."); Cold(g, r);

        var (faction, fr) = Create(faction: true); LuanwuUse(faction); Reach(faction, Nearest);
        Answer(faction, c => c.Parameters.GetValueOrDefault("request-option") == "faction" && c.Targets.SequenceEqual([2]));
        Reach(faction, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"));
        var provider = P(faction)!.PlayerSeat; var provided = P(faction)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash").Cards.Single();
        Cold(faction, fr); Answer(faction, c => c.Cards.SequenceEqual([provided]) && c.Parameters.GetValueOrDefault("response") == "faction-slash-slash");
        Reach(faction, p => Nearest(p) && p.PlayerSeat == 2);
        var requested = Events<CardActionAcceptedEvent>(faction).Single(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 1 && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(requested.ProviderSeat == provider && requested.RequesterSeat == 1 && requested.PhysicalCards.Single().CardId == provided &&
            requested.PhysicalCards.Single().From == CardLocation.Hand(provider) && faction.CardMovements.Count(m => m.CardId == provided && m.Reason == CardMoveReasons.Use) == 1,
            "The faction child retains its real acting source, requester, distinct provider and exact once-paid entity.");
        Answer(faction, c => c.Parameters.GetValueOrDefault("request-option") == "decline"); Reach(faction, p => Nearest(p) && p.PlayerSeat == 3);
        Answer(faction, c => c.Parameters.GetValueOrDefault("request-option") == "decline"); Reach(faction, p => Action(p, "unlimited-virtual-slash"));
        Cold(faction, fr); Answer(faction, c => c.Parameters.GetValueOrDefault("offer-option") == "decline"); Finish(faction);
        Require(Events<ProgramUnlimitedSlashChoiceEvent>(faction).Single() is { Used: false } &&
            !Events<CardActionAcceptedEvent>(faction).Any(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash),
            "Declining the final offer creates no virtual use and does not repeat the completed faction payment."); Cold(faction, fr);

        var (declined, dr) = Create(faction: true); var actorHp = declined.State.Players[1].Hp;
        LuanwuUse(declined); Reach(declined, Nearest); Answer(declined, c => c.Parameters.GetValueOrDefault("request-option") == "faction" && c.Targets.SequenceEqual([2]));
        Reach(declined, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-decline"));
        for (var providerIndex = 0; providerIndex < 3 && P(declined)!.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-decline"); providerIndex++)
        { Cold(declined, dr); Answer(declined, c => c.Parameters.GetValueOrDefault("response") == "faction-slash-decline");
          Reach(declined, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-decline") || Nearest(p)); }
        Require(Nearest(P(declined)!) && P(declined)!.PlayerSeat == 2 && declined.State.Players[1].Hp == actorHp - 1 &&
            Events<ProgramSkillHpLostEvent>(declined).Count(e => e.SkillId == "boundary:luanwu" && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            !Events<CardActionAcceptedEvent>(declined).Any(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 1) &&
            Events<ProgramUnlimitedSlashChoiceEvent>(declined).Length == 0,
            "All real faction providers declining pays the requesting actor's HP exactly once and resumes the next participant before any final offer.");
        Cold(declined, dr); DeclineRemaining(declined); Finish(declined); Cold(declined, dr);

        var (horse, hr) = Create(cardId: "standard:defensive_horse", wushen: true);
        Use(horse, "equip", [0]); Finish(horse); Use(horse, "equip", [2]); Finish(horse);
        LuanwuUse(horse); Reach(horse, Nearest);
        Require(P(horse)!.PlayerSeat == 1 && P(horse)!.ValidTargetSeats.SequenceEqual([0, 2, 3]),
            "Real defensive horses make all three living opponents equally nearest at distance two.");
        var heart = P(horse)!.Choices.First(c => c.Cards.Count == 1 && c.Targets.SequenceEqual([2])); var heartCard = heart.Cards.Single(); var heartHp = horse.State.Players[1].Hp;
        Cold(horse, hr); Answer(horse, c => c.Id == heart.Id); Reach(horse, p => Nearest(p) && p.PlayerSeat == 2);
        var divine = Events<CardActionAcceptedEvent>(horse).Single(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(divine.PhysicalCards.Single().CardId == heartCard && divine.ConversionChain.Single().SkillId == "classic:wushen" &&
            horse.State.Players[1].Hp == heartHp && horse.CardMovements.Count(m => m.CardId == heartCard && m.Reason == CardMoveReasons.Use) == 1,
            "The exact Heart identity variant retains 武神's independent unlimited distance and pays its real single material without incorrectly losing HP.");
        DeclineRemaining(horse); Finish(horse); Cold(horse, hr);

        var (quota, qr) = Create(); Play(quota, LegalActionKind.Slash, [1]); Finish(quota);
        Require(!quota.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash), "The owner's ordinary Slash quota is already spent.");
        LuanwuUse(quota); Reach(quota, Nearest); DeclineRemaining(quota, offerTarget: 2); Finish(quota);
        var unlimited = Events<CardActionAcceptedEvent>(quota).Last(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash).Action;
        Require(unlimited.PhysicalCards.Count == 0 && unlimited.TargetSeats.SequenceEqual([2]) &&
            !quota.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) &&
            Events<ActualPlayPhaseCardUseRecordedEvent>(quota).Any(e => e.CardActionId == unlimited.ActionId && e.State.ActorSeat == 0 && e.State.UseCount == 2),
            "The distant virtual finisher bypasses the already spent ordinary quota, records the real second own-phase use and leaves the quota spent."); Cold(quota, qr);
    }

    public static void SourceLossKeepsPaidSlashAndNativeAiReadsCommittedStartHistory()
    {
        var (g, r) = Create(sourceLoss: true); var hp = g.State.Players[2].Hp;
        LuanwuUse(g); Reach(g, Nearest); var choice = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Targets.SequenceEqual([2])); var card = choice.Cards.Single();
        Answer(g, c => c.Id == choice.Id); Reach(g, p => p.SkillPrompt?.SkillId == Loss && Action(p, "select-target"));
        Require(g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            !Events<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 2),
            "The actual committed-use observer starts after physical payment and before the original Slash damage.");
        Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([0])); Finish(g);
        Require(g.State.Players[2].Hp == hp - 1 && g.CardMovements.Count(m => m.CardId == card && m.Reason == CardMoveReasons.Use) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.Reason == CardMoveReasons.UseFinished) == 1 &&
            Events<CurrentTurnNonLockedSkillSuppressionIssuedEvent>(g).Any(e => e.Suppression.TargetSeat == 0) &&
            Events<ProgramUnlimitedSlashChoiceEvent>(g).Length == 0 &&
            !Events<ProgramSkillHpLostEvent>(g).Any(e => e.TargetSeat is 2 or 3),
            "An actual cost observer invalidates the nonLocked source: the already paid original Slash finishes once, remaining actors and finisher cancel."); Cold(g, r);
        var (native, nr) = Create(native: true);
        // StartGame can drive the whole bounded unattended game. Read its entire
        // committed history before inspecting completion or requesting another step.
        var started = Events<ProgramSkillStartedEvent>(native).Any(e => e.SkillId == "boundary:luanwu");
        for (var step = 0; !started && step < 80 && native.State.Status != EngineStatus.Completed; step++)
        { Accept(native, new AdvanceOneStepCommand(native.Revision)); started = Events<ProgramSkillStartedEvent>(native).Any(e => e.SkillId == "boundary:luanwu"); }
        Require(started && Events<ProgramUnlimitedSlashChoiceEvent>(native).Length == 1 &&
            Events<CardActionAcceptedEvent>(native).Any(e => e.Action.Type == CardActionType.Use && e.Action.ActorSeat != 0 && e.Action.PhysicalCards.Count > 0),
            "Native unattended AI activates the new public estimate and makes real participant payments without manually answering any AI choice."); Cold(native, nr);
    }

    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Has(GameEngine g, int seat, string skill) => g.CreateSnapshot(0).Players[seat].Skills!.Any(s => s.ContentId == skill);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Nearest(PendingDecision p) => Action(p, "nearest-legal-slash");
    private static void LuanwuUse(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, "boundary:luanwu", Luanwu, [], [], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static int Play(GameEngine g, LegalActionKind kind, IReadOnlyList<int>? targets = null)
    {
        var action = g.GetHumanLegalActions().First(a => a.Kind == kind && (targets is null || a.TargetSeats.SequenceEqual(targets)));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource }); return action.CardId.Value;
    }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void DeclineRemaining(GameEngine g, int? offerTarget = null)
    {
        for (var step = 0; step < 4; step++)
        {
            Reach(g, p => Nearest(p) || Action(p, "unlimited-virtual-slash"));
            if (Nearest(P(g)!)) { Answer(g, c => c.Parameters.GetValueOrDefault("request-option") == "decline"); continue; }
            Answer(g, c => offerTarget is { } target ? c.Targets.SequenceEqual([target]) : c.Parameters.GetValueOrDefault("offer-option") == "decline"); return;
        }
        throw new InvalidOperationException("The fixed actor cursor did not return to its final offer.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Finish(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => ReachUntil(g, () => P(g) is { } p && predicate(p));
    private static void ReachUntil(GameEngine g, Func<bool> predicate)
    { for (var step = 0; step < 160; step++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed Jia Xu prefix missed its boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p?.SkillPrompt?.SkillId == Gain || p?.SkillPrompt?.SkillId == Entry && Action(p, "choose-option")) Continue(g);
        else if (p is { Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { Kind: DecisionKind.RespondDodge } && p.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        else if (p is { Kind: DecisionKind.RespondSlash } && p.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "take-duel-damage");
        else if (p is { Kind: DecisionKind.Nullification } && p.PlayerSeat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "no-nullification");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Private(GameEngine g) { var p = P(g)!; Require(p.IsPrivate, "The actual choosing actor owns a private input window."); for (var seat = 0; seat < 4; seat++) if (seat != p.PlayerSeat) Require(g.CreateSnapshot(seat).PendingDecision is null, "Other views do not expose physical payment choices."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished answer is atomically rejected before paying entities."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "All four private views, exact owning frames, movement facts, public events and real commands cold-restore identically.");
    private static void Frozen<T>(IReadOnlyList<T> list) { Require(list is IList<T> { IsReadOnly: true }, "The committed nested collection is read only."); try { ((IList<T>)list).Add(default!); throw new InvalidOperationException("A committed nested collection allowed mutation."); } catch (NotSupportedException) { } }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(string cardId = "standard:slash", Suit suit = Suit.Heart,
        bool curtain = false, bool rewrite = false, bool mixed = false, bool mixedRed = false, bool outside = false,
        bool grantAtEntry = false, bool fragile = false, bool faction = false, bool sourceLoss = false, bool native = false,
        bool gainDying = false, bool wushen = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Fixture(cardId, suit, curtain, rewrite, mixed, mixedRed, outside, grantAtEntry, fragile, faction, sourceLoss, native, gainDying, wushen));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = !native, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 4 : 8 }, registry);
        Accept(game, new StartGameCommand());
        if (!native) { Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0); Accept(game, new SelectGeneralCommand(0, "fixture:jx-owner", game.Revision, P(game)!.PromptId)); Finish(game); }
        return (game, registry);
    }
    private sealed class Fixture(string cardId, Suit suit, bool curtain, bool rewrite, bool mixed, bool mixedRed,
        bool outside, bool grantAtEntry, bool fragile, bool faction, bool sourceLoss, bool native, bool gainDying, bool wushen) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-jia-xu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"activations":[
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
              {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"dying-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":6}]},
              {"id":"dying-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":7}]},
              {"id":"draw-many","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]},
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"shed-curtain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:weimu"],"sourceBind":"standard:none"}]},
              {"id":"gain-curtain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["boundary:weimu"]}]}]},
             {"id":"{{Gain}}","revision":1,"triggers":[{"id":"paid-draw","window":"cardsGained","subject":"owner","optional":false,{{(gainDying ? "\"usageScope\":\"game\",\"usageLimit\":1," : "")}}"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:weimu.prevented-damage-draw"],"effects":[{{(gainDying ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":7}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"draw-seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{Entry}}","revision":1,"triggers":[{"id":"real-dying-entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}]},
             {"id":"{{ForeignGrant}}","revision":1,"triggers":[{"id":"grant-in-window","window":"dyingEntering","subject":"any","optional":false,"condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"greaterThan","right":{"kind":"integerConstant","value":0} },"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Later}}"]},{"op":"chooseOption","target":"owner","resultBind":"foreign-entry-seen","options":[{"id":"continue"}]}]}]},
             {"id":"fixture:jx-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"fixture:jx-outside","revision":1,"triggers":[{"id":"actual-other-turn","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1}]}]},
             {"id":"{{Loss}}","revision":1,"triggers":[{"id":"invalidate-real-source","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLiving"},{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]}]},
             {"id":"fixture:jx-request","revision":1,"cardPolicies":[{"id":"real-shared-faction","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"wei"}]}]}
            """;
            var descriptions = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Gain, Entry, ForeignGrant, "fixture:jx-quiet", "fixture:jx-outside", Loss, "fixture:jx-request" })
                descriptions[id] = id is Gain or Entry or ForeignGrant
                    ? new { name = id, description = "真实命令边界夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                    : (object)new { name = id, description = "真实命令边界夹具" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = descriptions }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实通用能力夹具") { Program = catalog.Programs[id], Tags = id == "fixture:jx-quiet" || id == ForeignGrant ? SkillTag.Locked : SkillTag.None });
            foreach (var id in new[] { Ordinary, Later }) b.AddSkill(new(id, id, "普通资格夹具"));
            b.AddSkill(new(Locked, Locked, "锁定资格夹具") { Tags = SkillTag.Locked }); b.AddSkill(new(Limited, Limited, "限定资格夹具") { Tags = SkillTag.Limited });
            b.AddSkill(new("fixture:jx-pick-owner", "固定来源", "只用于正式选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:jx-pick-other", "固定其他角色", "只用于正式选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -100000d : 100000d) });
            var owner = new List<string> { "boundary:weimu", "boundary:luanwu", "fixture:jx-pick-owner" };
            if (!native) owner.AddRange([Driver, Gain, Entry, Ordinary, Locked, Limited, "classic:qice"]);
            if (rewrite) owner.Add("classic:hongyan");
            b.AddGeneral(new("fixture:jx-owner", "界贾诩真实机制", "supporter", "boundary:wansha", "qun", 6, owner.ToArray()));
            for (var i = 1; i < 4; i++)
            {
                var skills = native ? new List<string>() : new List<string> { Ordinary, Locked, Limited, Entry, "fixture:jx-quiet", "classic:fuhun" };
                if (curtain && i == 1) skills.Add("boundary:weimu"); if (outside && i == 1) skills.Add("fixture:jx-outside");
                if (grantAtEntry) skills.Add(ForeignGrant);
                if (wushen && i == 1) skills.Add("classic:wushen");
                if (faction) skills.Add("fixture:jx-request"); if (sourceLoss) skills.Add(Loss);
                b.AddGeneral(new($"fixture:jx-other-{i}", "固定其他角色", "supporter", "fixture:jx-pick-other", "wei", 6, skills.ToArray()) { InitialHp = fragile && i == 3 ? 1 : null });
            }
            b.AddDeck(new("fixture:jx-deck", "固定实际实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                new ContentDeckPhysicalCard(cardId, mixed ? i % 2 == 0 ? Suit.Spade : mixedRed ? Suit.Heart : Suit.Club : suit, 7)).ToArray() });
            b.AddMode(new(Mode, "界贾诩真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:jx-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:jx-owner", "fixture:jx-other-1", "fixture:jx-other-2", "fixture:jx-other-3"]));
        }
    }
}

