using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuanHaiChecks
{
    private const string Xiewei = "ol:xiewei", Youque = "ol:youque", Driver = "fixture:gh-driver", Watch = "fixture:gh-watch";
    private const string Owner = "fixture:gh-owner", Quiet = "fixture:gh-quiet", Mode = "identity:classic-guan-hai";
    private const string Store = "skill-program.guan-hai.xiewei.store", Discard = "skill-program.guan-hai.youque.discard", Draw = "skill-program.guan-hai.youque.draw";
    private static string Scene = "";

    public static void XieweiAtomicHandBaitsTrueUsesAndPlayedBoundariesCold()
    {
        {
            var (g, r) = Start("slash"); var a = XieweiAction(g); var cards = a.SelectableCardIds.ToArray();
            Need(cards.Length == 2 && cards.All(id => V(g, 0).Hand.Any(c => c.Id == id)) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "Only the actual owner can inspect the two original Hand costs before the public PUT.");
            var state = State(g); var p = P(g)!;
            Need(!g.Submit(new UseProgramSkillCommand(1, Xiewei, a.ProgramActivationId!, cards, [1], g.Revision, p.PromptId)).Accepted && State(g) == state,
                "A peer cannot spend another player's exact published pair.");
            g = Cold(g, r); SubmitXiewei(g, a, cards, 1); Play(g); AssertStored(g, cards, 0);
            var paid = E<XieweiPaymentPreparedEvent>(g).Single(); var bound = E<XieweiOutputBoundEvent>(g).Single();
            Need(!paid.ResponseUse && bound.Kind == CardKind.Slash && bound.ActorSeat == 0 && bound.ProviderSeat == 0 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == bound.UseId && e.CardKind == CardKind.Slash) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == bound.UseId) == 1,
                "A pair's single public deposit belongs to one genuine native Slash declaration and complete return.");
            state = State(g); Need(!g.Submit(new UseProgramSkillCommand(0, Xiewei, a.ProgramActivationId!, cards, [1], g.Revision, p.PromptId)).Accepted && State(g) == state,
                "Replaying the original prompt cannot spend the bait entities a second time.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("dodge"); Incoming(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var choice = P(g)!.Choices.First(IsXiewei); var cards = choice.Cards.ToArray(); var prompt = P(g)!;
            Need(prompt.IsPrivate && cards.Length == 2 && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
                "The true Dodge publishes only its owner's private two-Hand choices.");
            var state = State(g); Need(!g.Submit(new AnswerPromptCommand(2, prompt.PromptId, choice.Id, g.Revision)).Accepted && State(g) == state,
                "A foreign actor cannot answer the private defensive pair.");
            g = Cold(g, r); Answer(g, c => c.Id == choice.Id); Play(g); AssertStored(g, cards, 0);
            var bound = E<XieweiOutputBoundEvent>(g).Single();
            Need(bound.ResponseUse && bound.Kind == CardKind.Dodge && E<CardResponseCompletedEvent>(g).Any(e => e.ActionId == bound.ActionId &&
                e.OriginalContinuation == ProgramCardContinuation.Dodge && e.NativeActorSeat == 0 && e.ProviderSeat == 0),
                "Defending a genuine Slash is a true Dodge response-use, with its native completion rather than a fabricated Use declaration.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("multiple-targets"); var a = XieweiAction(g); var cards = a.SelectableCardIds.ToArray();
            var targets = new[] { 1, 3 }; var hp = targets.Select(s => V(g, s).Hp).ToArray();
            Need(a.MinTargetCount == 1 && a.MaxTargetCount == 2 && targets.All(a.SelectableTargetSeats.Contains),
                "The genuine cardTargetCount+1 program query publishes both legal adjacent recipients for this same pair.");
            Accept(g, new UseProgramSkillCommand(0, Xiewei, a.ProgramActivationId!, cards, targets, g.Revision, P(g)!.PromptId));
            Reach(g, () => HumanContinue(g)); var bound = E<XieweiOutputBoundEvent>(g).Single(); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == bound.UseId);
            AssertStored(g, cards, 0);
            Need(use.TargetSeats.SequenceEqual(targets) && use.Action!.TargetSeats.SequenceEqual(targets) && E<XieweiPaymentPreparedEvent>(g).Length == 1 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && E<CardUseFinishedEvent>(g).All(e => e.ResolutionId != use.Id),
                "Both recipients belong to one original declared Use/action and one atomic pair payment before any complete-use return.");
            g = Cold(g, r); Continue(g); Play(g); AssertStored(g, cards, 0);
            Need(targets.Select((s, i) => V(g, s).Hp == hp[i] - 1).All(x => x) && targets.All(s => E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == s && e.Amount == 1) == 1) &&
                E<XieweiOutputBoundEvent>(g).Length == 1 && E<XieweiBaitStoredEvent>(g).Length == 2 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1,
                "The native target continuation settles both actual targets once, while its original two Hand entities enter bait only once and the whole Use finishes once.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("hand-only"); Native(g, a => a.Kind == LegalActionKind.Equip); Play(g);
            var keep = V(g, 0).Hand.First().Id; Use(g, "give", V(g, 0).Hand.Where(c => c.Id != keep).Select(c => c.Id).ToArray(), [2]); Play(g);
            Need(V(g, 0).HandCount == 1 && V(g, 0).Equipment.Count == 1 && !g.GetHumanLegalActions().Any(a => a.SkillId == Xiewei),
                "One Hand plus one Equipment never becomes the required two Hand cards."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("black-armor"); Use(g, "place", [V(g, 0).Hand[0].Id], [1]); Play(g); var hp = V(g, 1).Hp;
            Need(V(g, 1).Equipment is [{ Kind: CardKind.RenwangShield }], "The target genuinely equips its native black-Slash armor before the colorless conversion.");
            var a = XieweiAction(g); var cards = a.SelectableCardIds.ToArray(); Need(cards.All(id => V(g, 0).Hand.Single(c => c.Id == id).Suit == Suit.Spade), "Both genuine Hand costs are black.");
            SubmitXiewei(g, a, cards, 1); Reach(g, () => HumanContinue(g)); var bound = E<XieweiOutputBoundEvent>(g).Single();
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == bound.UseId);
            Need(use.Action is { EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: null } && use.Action.PhysicalCards.Count == 2,
                "The two original costs retain provenance without lending their black suit, point or color to the virtual Slash.");
            g = Cold(g, r); Continue(g); Play(g); AssertStored(g, cards, 0);
            Need(V(g, 1).Hp == hp - 1 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1),
                "RenwangShield cannot turn two black deposit costs into a black output and stop this genuine colorless Slash."); _ = Cold(g, r);
        }
        foreach (var scene in new[] { "duel", "barbarian", "arrows" })
        {
            var (g, r) = Start(scene); if (scene == "duel") Use(g, "fixed-native-duel", targets: [1]); else End(g);
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Kind == (scene == "arrows" ? DecisionKind.RespondDodge : DecisionKind.RespondSlash));
            var native = g.ResolutionStack.OfType<CardUseFrame>().Last(); var expected = scene == "duel" ? CardKind.Duel : scene == "barbarian" ? CardKind.BarbarianAssault : CardKind.ArrowBarrage;
            var request = g.ResolutionStack.OfType<ResponseWindowFrame>().Last();
            Need(native.CardKind == expected && native.SourceSeat != 0 && native.Action is { Type: CardActionType.Use } &&
                request.ParentFrameId == native.Id && request.ResponderSeat == 0 && request.IncomingCard == expected && request.RequiredCardKind == (scene == "arrows" ? CardKind.Dodge : CardKind.Slash) &&
                P(g)!.IsPrivate && V(g, 0).HandCount >= 2 && P(g)!.Choices.All(c => !IsXiewei(c)) && P(g)!.Choices.Any(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver) && E<XieweiPaymentPreparedEvent>(g).Length == 0 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == native.Id && e.SourceSeat == native.SourceSeat && e.CardKind == expected) == 1,
                "The real foreign Duel/AOE requests only a played card; a two-Hand true-use conversion cannot answer it.");
            var responseId = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver).Cards.Single();
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([responseId]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver); Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == native.Id));
            Need(E<XieweiBaitStoredEvent>(g).Length == 0 && E<XieweiPaymentPreparedEvent>(g).Length == 0 &&
                (scene == "duel" ? E<DuelResponseEvent>(g).Count(e => e.ResolutionId == native.Id && e.ResponderSeat == 0 && e.UsedSlash && e.SlashCardId == responseId) == 1 :
                    E<GroupResponseEvent>(g).Count(e => e.ResolutionId == native.Id && e.IncomingCard == expected && e.ResponderSeat == 0 && e.UsedResponse && e.ResponseCardId == responseId) == 1),
                "The actual single-entity played-card response completes its real Duel/AOE parent without silently issuing any Xiewei payment or bait.");
        }
        {
            var (g, r) = Start("hujia"); var provider = Enumerable.Range(1, 3).Single(s => V(g, s).GeneralId == "fixture:gh-peer-1");
            Need(V(g, provider).Role == Role.Loyalist && V(g, 0).Role == Role.Lord &&
                r.Generals[V(g, provider).GeneralId!].FactionId == "wei" && r.Generals[V(g, 0).GeneralId!].FactionId == "wei",
                "The actual native provider is a Wei Loyalist protecting its real Wei Lord, not an enemy expected to ignore its own AI relationship. " + Diagnostic(g));
            Incoming(g, Enumerable.Range(1, 3).First(s => s != provider && s is 1 or 3));
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "faction-defense-request");
            Reach(g, () => P(g) is { } p && p.PlayerSeat == provider && p.Choices.Any(IsXiewei));
            var offered = P(g)!.Choices.First(IsXiewei).Cards.ToArray(); g = Cold(g, r);
            Reach(g, () => E<FactionDefenseResolvedEvent>(g).Any(e => e.Succeeded && e.ProviderSeat == provider));
            var bound = E<XieweiOutputBoundEvent>(g).Single(); AssertStored(g, offered, provider);
            Need(bound is { ActorSeat: 0, RequesterSeat: 0, ResponseUse: true } && bound.ProviderSeat == provider &&
                E<CardResponseCompletedEvent>(g).Any(e => e.ActionId == bound.ActionId && e.NativeActorSeat == 0 && e.ProviderSeat == provider &&
                    e.OriginalContinuation == ProgramCardContinuation.FactionDefenseDodge),
                "Actual Hujia retains the requesting Dodge user and the separate real pair provider; it never invents a provider-owned Slash/Dodge Use."); _ = Cold(g, r);
        }
    }

    public static void YouqueMinimumEntityNativePindianTieAndEmptyBoundariesCold()
    {
        foreach (var scene in new[] { "minimum", "tie", "owner-empty", "opponent-empty" })
        {
            var (g, r) = Start(scene);
            if (scene == "minimum")
            {
                Use(g, "stock"); Play(g); var ids = new[] { V(g, 0).Hand.First(c => c.Rank == 5).Id, V(g, 0).Hand.First(c => c.Rank == 9).Id };
                SubmitXiewei(g, XieweiAction(g, ids), ids, 1); Play(g);
            }
            else StorePair(g);
            var bait = Pile(g, 0); var hand = V(g, 0).Hand.Select(c => c.Id).ToArray();
            Need(bait.Length == 2 && (scene == "minimum" ? bait.Select(c => c.Rank).Order().SequenceEqual([5, 9]) : bait.Select(c => c.Rank).Distinct().Count() == 1),
                "The actual public pair retains its two distinct point values or two equal minimum originals.");
            if (scene == "owner-empty")
            {
                Use(g, "give", hand, [2]); Play(g); Need(V(g, 0).HandCount == 0 && Pile(g, 0).Length == 2 && !g.GetHumanLegalActions().Any(a => a.SkillId == Youque),
                    "Bait is not secretly transferred into Hand to evade the native Pindian source's nonempty-Hand rule."); _ = Cold(g, r); continue;
            }
            if (scene == "opponent-empty")
            {
                while (V(g, 1).HandCount != 0) { Use(g, "trim-peer", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.ContainsKey("slot-index"))); Answer(g, c => c.Parameters.ContainsKey("slot-index")); Play(g); }
                var a = g.GetHumanLegalActions().First(x => x.SkillId == Youque);
                Need(!a.SelectableTargetSeats.Contains(1), "A genuinely empty opponent cannot be selected for the native contest.");
                var before = State(g); Need(!g.Submit(new UseProgramSkillCommand(0, Youque, "minimum-bait-pindian", [], [1], g.Revision, P(g)!.PromptId)).Accepted && State(g) == before,
                    "Forging the empty opponent is rejected without moving bait or changing the current prompt."); _ = Cold(g, r); continue;
            }
            BeginYouque(g); var prompt = P(g)!;
            if (scene == "minimum")
            {
                var low = bait.Single(c => c.Rank == 5).Id; var high = bait.Single(c => c.Rank == 9).Id;
                Need(prompt.Choices.Where(c => c.Cards.Count != 0).All(c => c.Cards.SequenceEqual([low])), "A higher original bait is not admitted merely because it shares the pile and source.");
                var frameId = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.GuanHai?.Stage == GuanHaiStage.ChooseBait).Id; var before = State(g);
                Need(!g.Submit(new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId($"youque.{frameId}.bait.{high}"), g.Revision)).Accepted && State(g) == before,
                    "A forged nonminimum original is rejected without starting Pindian or moving either bait.");
                g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([low])); Play(g);
                var issued = E<YouquePindianIssuedEvent>(g).Single(); var actual = E<PindianResultDeterminedEvent>(g).Single().Result;
                Need(issued.CardId == low && actual.SourceCardId == low && !g.CardMovements.Any(m => m.Sequence > issued.Before && m.CardId == low && m.To == CardLocation.Hand(0)) &&
                    g.CardMovements.Count(m => m.Sequence > issued.Before && m.CardId == low && m.From == issued.Pile.Location && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1,
                    "The genuine minimum flows directly from its original pile through native Pindian, never through a new Hand gain. " + JsonSerializer.Serialize(new { low, high, issued, actual,
                        Moves = g.CardMovements.Where(m => m.CardId == low || m.CardId == high) }));
                _ = Cold(g, r); continue;
            }
            Need(prompt.Choices.Where(c => c.Cards.Count == 1).SelectMany(c => c.Cards).Distinct().Order().SequenceEqual(bait.Select(c => c.Id).Order()),
                "All tied minimum originals are selectable; no arbitrary first-card choice is imposed.");
            var chosen = bait[1].Id; var originalHand = hand.Order().ToArray(); g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([chosen]));
            Reach(g, () => g.ResolutionStack.OfType<PindianFrame>().Any());
            var frame = g.ResolutionStack.OfType<PindianFrame>().Single();
            var reservation = E<YouquePindianIssuedEvent>(g).Single();
            Need(frame.SourceCardId == chosen && frame.OwnedPileSource is { CardId: var source } && source == chosen &&
                V(g, 0).Hand.Select(c => c.Id).Order().SequenceEqual(originalHand) && !g.CardMovements.Any(m => m.Sequence > reservation.Before && m.CardId == chosen && m.To == CardLocation.Hand(0)),
                "The selected original remains a genuine source-pile Pindian payment, never a transient manufactured Hand card.");
            g = Cold(g, r); Play(g); var result = E<YouquePindianReturnedEvent>(g).Single();
            Need(result.SourceCardId == chosen && result.SourceRank == result.OpponentRank && E<YouqueDiscardPaidEvent>(g).Length == 0 &&
                E<YouqueSlashIssuedEvent>(g).Length == 0 && E<YouqueRewardDrawReturnedEvent>(g).Length == 0 && E<YouquePhaseDisabledEvent>(g).Length == 0 &&
                Pile(g, 0).Select(c => c.Id).SequenceEqual([bait[0].Id]) && g.GetHumanLegalActions().Any(a => a.SkillId == Youque),
                "An actual tied contest consumes only its chosen bait and the opponent's card; it has no loser, counter-Slash, damage reward or invented once-per-phase cap."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("minimum-alcohol-king"); Use(g, "stock"); Play(g);
            var recipe = r.GetDeck("fixture:gh-deck").PhysicalCards!;
            // These original physical recipe IDs were created as inputIndex+1;
            // effective Hand rank may already be K, so select printed5/9 by identity.
            var ids = new[] { 5, 9 }.Select(rank => V(g, 0).Hand.First(c => c.Kind == CardKind.Alcohol &&
                c.Id > 0 && c.Id <= recipe.Count && recipe[c.Id - 1].Rank == rank).Id).ToArray();
            SubmitXiewei(g, XieweiAction(g, ids), ids, 1); Play(g); AssertStored(g, ids, 0);
            var baits = Pile(g, 0); var low = baits.Single(c => c.Kind == CardKind.Alcohol && c.Rank == 5).Id;
            var high = baits.Single(c => c.Kind == CardKind.Alcohol && c.Rank == 9).Id;
            Need(r.GetSkill(Driver).Program!.CardPolicies.Any(p => p.Kind == SkillProgramCardPolicyKind.AlcoholKingIdentityRank) &&
                r.GetSkill("wumiao:shenduan").Program is not null && Enumerable.Range(0, 4).All(v => Baits(g.CreateSnapshot(v).Players[0]).Any(c => c.Id == low)),
                "Real printed Alcohol5/9 are deposited by Xiewei into the same original public bait pile while the owner has a live Hand/Grain King policy and the registry enables the Wu contest protocol.");
            g = Cold(g, r); BeginYouque(g);
            Need(P(g)!.Choices.Where(c => c.Cards.Count == 1).All(c => c.Cards.SequenceEqual([low])),
                "The genuine minimum bait selector admits printed Alcohol5, never the higher original Alcohol9.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([low])); Play(g);
            var issued = E<YouquePindianIssuedEvent>(g).Single(); var actual = E<PindianResultDeterminedEvent>(g).Single().Result;
            Need(issued.CardId == low && actual.SourceCardId == low && actual.SourceRank == 5 &&
                E<YouquePindianReturnedEvent>(g).Single().SourceRank == 5 &&
                E<WuLuKangContestStartedEvent>(g).Length == 1 && E<WuLuKangPindianInputRevealedEvent>(g).Single(e => e.Seat == 0) is { CardId: var original, Rank: 5 } && original == low &&
                E<DouyuSwapCommittedEvent>(g).Length == 0 && E<WuLuKangKDrawIssuedEvent>(g).Length == 0 &&
                g.CardMovements.Count(m => m.Sequence > issued.Before && m.CardId == low && m.From == issued.Pile.Location &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1 &&
                !g.CardMovements.Any(m => m.Sequence > issued.Before && m.CardId == low && m.To == CardLocation.Hand(0)) &&
                Pile(g, 0).Any(c => c.Id == high),
                "The exact private owned-pile Alcohol source uses printed5 through native Pindian under the Wu protocol; a Hand/Grain-only King identity cannot turn that pile source into13, issue a false K reward or manufacture a Hand payment."); _ = Cold(g, r);
        }
    }

    public static void YouqueWholeDiscardForeignSlashAndLatestSpadeRewardCold()
    {
        foreach (var scene in new[] { "lose", "win", "spade", "dodged" })
        {
            var (g, r) = Start(scene); if (scene == "spade") { Use(g, "stock"); Play(g); }
            StorePair(g, scene == "spade" ? Suit.Spade : null); var bait = Pile(g, 0); BeginYouque(g); Answer(g, c => c.Cards.SequenceEqual([bait[0].Id]));
            Reach(g, () => E<YouqueDiscardMaterialEvent>(g).Length != 0);
            var material = E<YouqueDiscardMaterialEvent>(g); var loser = scene == "lose" ? 0 : 1; var winner = loser == 0 ? 1 : 0;
            Need(material.Length != 0 && material.All(m => m.LoserSeat == loser && m.From == CardLocation.Hand(loser)),
                "The real native Pindian rank2 loser freezes its entire printed-rank7 Hand for one mandatory logical discard.");
            g = Cold(g, r);
            if (scene == "dodged")
            {
                Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } p && p.Choices.Any(IsXiewei));
                var dodge = P(g)!.Choices.First(IsXiewei); var ids = dodge.Cards.ToArray(); g = Cold(g, r); Answer(g, c => c.Id == dodge.Id); Play(g);
                var counter = E<YouqueSlashIssuedEvent>(g).Single().Origin; AssertStored(g, ids, 0);
                Need(counter.ActorSeat == 1 && counter.TargetSeat == 0 && E<CardResponseCompletedEvent>(g).Any(e => e.ParentFrameId == counter.UseId &&
                    e.OriginalContinuation == ProgramCardContinuation.Dodge) && E<YouqueRewardDrawReturnedEvent>(g).Length == 0 && E<YouquePhaseDisabledEvent>(g).Length == 0 &&
                    E<YouqueSlashReturnedEvent>(g).Count(e => e.UseId == counter.UseId) == 1 && g.GetHumanLegalActions().Any(a => a.SkillId == Youque),
                    "A genuinely dodged foreign zero-entity Slash fully returns without damage, Draw2 or a no-Spade phase penalty; depositing a new real pair is separate.");
                _ = Cold(g, r); continue;
            }
            Reach(g, () => HumanContinue(g));
            var reward = E<YouqueRewardDrawReturnedEvent>(g).Single(); var rewardParent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == reward.FrameId);
            Need(g.CardMovements.Count(m => m.Reason.Value == Draw && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 2 && reward.Actual == 2 &&
                rewardParent.GuanHai is { Paid: true, Stage: GuanHaiStage.RewardChildren } && E<YouquePhaseDisabledEvent>(g).Length == 0 &&
                !E<GuanHaiCompletedEvent>(g).Any(e => e.FrameId == reward.FrameId),
                "The typed Draw2 outcome has returned, while its actual gain child still suspends RewardChildren before the current-Spade decision and reward completion. " +
                JsonSerializer.Serialize(new { reward, Receipt = rewardParent.GuanHai, Disabled = E<YouquePhaseDisabledEvent>(g), Completed = E<GuanHaiCompletedEvent>(g), Prompt = P(g) }) + " " + Diagnostic(g));
            g = CompleteRewardChildren(g, r, reward);
            var issued = E<YouqueSlashIssuedEvent>(g).Single().Origin; var returned = E<YouqueRewardDrawReturnedEvent>(g).Single();
            Need(issued.ActorSeat == loser && issued.TargetSeat == winner && issued.Source.OwnerSeat == 0 && returned.RecipientSeat == 0 && returned.Requested == 2 && returned.Actual == 2 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == issued.UseId && e.SourceSeat == loser && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
                E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == loser && e.TargetSeat == winner && e.Amount > 0),
                "The actual loser, including a foreign opponent, issues its one zero-entity native Slash; only genuine positive damage awards the original GuanHai two real cards.");
            Need(E<NativeLogicalDiscardInvocationRecordedEvent>(g).Count(e => e.ParentFrameId == issued.ProgramFrameId && e.ExecutorSeat == loser && e.HandCount == material.Length && e.ActualCount == material.Length && e.Reason.Value == Discard) == 1 &&
                E<YouqueDiscardPaidEvent>(g).Length == material.Length && E<YouqueDiscardPaidEvent>(g).Select(e => e.CardId).Order().SequenceEqual(material.Select(m => m.CardId).Order()) &&
                g.CardMovements.Count(m => m.Reason.Value == Draw && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 2,
                "All frozen high cards pay once as one whole discard, independently of the real two-card reward draw.");
            Play(g);
            Need(E<YouqueSlashReturnedEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.UseId) == 1 &&
                (scene == "spade" ? E<YouquePhaseDisabledEvent>(g).Length == 0 && Pile(g, 0).Any(c => c.Suit == Suit.Spade) && g.GetHumanLegalActions().Any(a => a.SkillId == Youque)
                    : E<YouquePhaseDisabledEvent>(g).Length == 1 && !Pile(g, 0).Any(c => c.Suit == Suit.Spade) && !g.GetHumanLegalActions().Any(a => a.SkillId == Youque)),
                "After the complete native draw/child return, the current original bait pile decides the exact actual-Play disable; a remaining Spade preserves eligibility.");
            if (scene == "win")
            {
                var turn = g.State.TurnNumber; End(g); Reach(g, () => g.State.TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
                Need(Pile(g, 0).Length == 1 && E<YouquePhaseDisabledEvent>(g).Length == 1 && g.GetHumanLegalActions().Any(a => a.SkillId == Youque),
                    "Only the paid actual Play token is disabled; the same persistent remaining bait is usable in a later real Play phase.");
            }
            _ = Cold(g, r);
        }
    }

    public static void GuanHaiPaidChildrenSourceLossDeathLionOxAndInputRejectionCold()
    {
        foreach (var scene in new[] { "paid-loss", "paid-death", "lion", "ox" })
        {
            var (g, r) = Start(scene); int? equipment = null, grain = null;
            if (scene is "lion" or "ox")
            {
                if (scene == "lion") { Use(g, "wound"); Play(g); }
                Native(g, a => a.Kind == LegalActionKind.Equip); Play(g); equipment = V(g, 0).Equipment.Single().Id;
                if (scene == "ox") { grain = V(g, 0).Hand.First().Id; Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], g.Revision, P(g)!.PromptId)); Play(g);
                    Need(V(g, 0).WoodenOxGrain!.Any(c => c.Id == grain) && Enumerable.Range(1, 3).All(s => (g.CreateSnapshot(s).Players[0].WoodenOxGrain?.Count ?? 0) == 0), "The real Ox holds its original private grain before any bait or discard child."); }
            }
            var action = XieweiAction(g); var cards = action.SelectableCardIds.ToArray(); SubmitXiewei(g, action, cards, 1);
            if (scene is "paid-loss" or "paid-death")
            {
                Reach(g, () => HumanContinue(g)); var prepared = E<XieweiPaymentPreparedEvent>(g).Single();
                Need(E<XieweiBaitStoredEvent>(g).Length == 2 && E<CardUseFinishedEvent>(g).All(e => e.ResolutionId != prepared.UseId),
                    "The real paid Hand batch pauses in its native observer before the mother Slash finishes.");
                g = Cold(g, r); Continue(g);
                Reach(g, () => !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.GuanHai is not null) && !g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == prepared.UseId));
                AssertStored(g, cards, 0, requireStillBait: false);
                if (scene == "paid-loss") Need(cards.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == prepared.Pile.Location && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "skill-program.public-pile.source-lost") == 1),
                    "The native source-loss cleanup retires each already-paid original bait once, independently of the original atomic Hand PUT. " + Diagnostic(g));
                Need(scene == "paid-loss" ? V(g, 0).IsAlive && !(V(g, 0).Skills?.Any(s => s.ContentId == Xiewei) ?? false) && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == prepared.UseId) == 1
                    : !V(g, 0).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) && P(g)?.PlayerSeat != 0,
                    "Paid source loss or genuine Dying/death cannot repay the same originals or leave a dead actor's private payment menu."); _ = Cold(g, r); continue;
            }
            Play(g); AssertStored(g, cards, 0); BeginYouque(g); var bait = Pile(g, 0); Answer(g, c => c.Cards.SequenceEqual([bait[0].Id])); Reach(g, () => HumanContinue(g));
            var frozen = E<YouqueDiscardMaterialEvent>(g); Need(frozen.Length != 0 && E<YouqueSlashIssuedEvent>(g).Length == 0 && V(g, 0).Equipment.Any(c => c.Id == equipment),
                "The mandatory whole-Hand discard really pauses before its derived equipment loss and before the counter-Slash.");
            g = Cold(g, r); Continue(g); Reach(g, () => HumanContinue(g));
            Need(scene == "lion" ? E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1) :
                grain is { } id && g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "equipment.wooden-ox.grain-discard"),
                "The actual derived equipment removal opens the genuine Lion recovery or Ox original-grain child, rather than falsely charging Equipment as Xiewei's Hand cost.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(E<YouqueSlashIssuedEvent>(g).Length == 1 && E<YouqueSlashReturnedEvent>(g).Length == 1 && E<YouqueRewardDrawReturnedEvent>(g).Single().Actual == 2 &&
                E<YouqueDiscardPaidEvent>(g).Length == frozen.Length && g.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Equipment(0)) == 1,
                "Native equipment/recovery/damage descendants drain before one counter-Slash and one reward; cold restoration cannot repay primary discard or equipment removal."); _ = Cold(g, r);
        }
    }

    private static bool IsXiewei(PromptChoice c) => c.Parameters.GetValueOrDefault("conversion-skill-id") == Xiewei;
    private static LegalAction XieweiAction(GameEngine g, IReadOnlyList<int>? ids = null) => g.GetHumanLegalActions().FirstOrDefault(a => a.SkillId == Xiewei && a.PlayedCardKind == CardKind.Slash &&
        (ids is null || a.SelectableCardIds.Order().SequenceEqual(ids.Order()))) ?? throw new InvalidOperationException("Missing real Xiewei pair: " + Diagnostic(g));
    private static void SubmitXiewei(GameEngine g, LegalAction a, int[] cards, int target) => Accept(g, new UseProgramSkillCommand(0, Xiewei, a.ProgramActivationId!, cards, [target], g.Revision, P(g)!.PromptId));
    private static void StorePair(GameEngine g, Suit? suit = null)
    { var ids = suit is null ? null : V(g, 0).Hand.Where(c => c.Suit == suit).Take(2).Select(c => c.Id).ToArray(); var a = XieweiAction(g, ids); SubmitXiewei(g, a, a.SelectableCardIds.ToArray(), 1); Play(g); }
    private static void BeginYouque(GameEngine g, int target = 1)
    { Accept(g, new UseProgramSkillCommand(0, Youque, "minimum-bait-pindian", [], [target], g.Revision, P(g)!.PromptId)); Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Youque && p.Choices.Any(c => c.Cards.Count == 1)); }
    private static CardSnapshot[] Baits(PlayerSnapshot player) => player.PublicPersistentPiles is { } piles ? piles.Where(p => p.SourceSkillId == Xiewei).SelectMany(p => p.Cards).ToArray() :
        player.PublicPersistentPileSkillId == Xiewei ? (player.PublicPersistentPileCards ?? []).ToArray() : [];
    private static CardSnapshot[] Pile(GameEngine g, int seat) => Baits(V(g, seat));
    private static void AssertStored(GameEngine g, IReadOnlyList<int> cards, int owner, bool requireStillBait = true)
    {
        var records = E<XieweiBaitStoredEvent>(g).Where(e => cards.Contains(e.CardId)).ToArray();
        Need(records.Length == 2 && records.Select(e => e.BatchId).Distinct().Count() == 1 && records.All(e => e.From == CardLocation.Hand(owner) && e.Pile.Zone == CardZoneKind.PublicPersistentPile &&
            g.CardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == e.From && m.To == e.Pile && m.Reason.Value == Store) == 1) &&
            (!requireStillBait || Enumerable.Range(0, 4).All(viewer => Baits(g.CreateSnapshot(viewer).Players[owner]).Count(c => cards.Contains(c.Id)) == 2)) &&
            !g.CardMovements.Any(m => cards.Contains(m.CardId) && m.To == CardLocation.Processing && (m.Reason == CardMoveReasons.Use || m.Reason == CardMoveReasons.Respond)),
            "Exactly one atomic two-Hand PUT exposes the same original baits publicly without a fake Hand→Processing use/discard payment. " + Diagnostic(g));
    }
    private static bool HumanContinue(GameEngine g) => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static GameEngine CompleteRewardChildren(GameEngine g, ContentRegistry registry, YouqueRewardDrawReturnedEvent reward)
    {
        var children = new HashSet<long>();
        for (var i = 0; i < 3; i++)
        {
            Reach(g, () => HumanContinue(g) || !g.ResolutionStack.Any(f => f.Id == reward.FrameId));
            if (!HumanContinue(g)) break;
            var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Watch && f.TriggerId == "real-reward-draw-child");
            var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
            Need(children.Add(child.Id) && children.Count <= 2 && moved.Batch.ParentFrameId == reward.FrameId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => m.Sequence > reward.Before && m.Sequence <= reward.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == Draw) &&
                E<YouquePhaseDisabledEvent>(g).Length == 0 && !E<GuanHaiCompletedEvent>(g).Any(e => e.FrameId == reward.FrameId),
                "Each actual Draw2 gain observer has a distinct native child and the same exact already-paid draw interval; none may bypass the current-Spade decision. " + Diagnostic(g));
            g = Cold(g, registry); Continue(g);
        }
        Need(children.Count is >= 1 and <= 2 && !HumanContinue(g) && !g.ResolutionStack.Any(f => f.Id == reward.FrameId),
            "Every real one-card/batched gain child completes once before the paid reward parent returns. " + Diagnostic(g));
        return g;
    }
    private static bool NativeDecline(PromptChoice c) => c.Cards.Count == 0 && !IsXiewei(c) && c.Parameters.GetValueOrDefault("response") is not ("faction-defense-request" or "faction-slash-request");
    private static void Incoming(GameEngine g, int source = 1)
    { Use(g, "incoming", targets: [source]); Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0")); Answer(g, c => c.Targets.SequenceEqual([0])); }
    private static void Use(GameEngine g, string id, int[]? cards = null, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, Func<LegalAction, bool> match)
    { var a = g.GetHumanLegalActions().FirstOrDefault(match) ?? throw new InvalidOperationException("Missing native action " + Diagnostic(g)); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources ?? [] }); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException("scene=" + Scene + " " + message); }
    private static void Accept(GameEngine g, GameCommand c)
    { try { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native command."); } catch (Exception e) { throw new InvalidOperationException("scene=" + Scene + " command=" + c.GetType().Name + " cause=" + e + " " + Diagnostic(g), e); } }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = P(g) ?? throw new InvalidOperationException("Missing prompt " + Diagnostic(g)); var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only a published genuine human choice may be answered. " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<bool> done, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 320; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Ended before " + caller + ":" + line + " " + Diagnostic(g)); var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(g, NativeDecline);
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException("Unexpected " + caller + ":" + line + " " + Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded " + caller + ":" + line + " " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Role, V(g, s).Hp, V(g, s).IsAlive, V(g, s).HandCount, Pile = Pile(g, s).Select(c => c.Id) }),
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(8) } : null,
        Frames = g.ResolutionStack.TakeLast(7).Select(f => new { f.Id, Type = f.GetType().Name, Stage = (f as ProgramSkillFrame)?.GuanHai?.Stage }), Facts = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Cold replay preserves public original bait, private opponent Hand, native pile-source Pindian and once-paid child cursors."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        Scene = scene; ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture cause=" + e.Message, e); }
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-guan-hai", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:guan-hai"); if (scene == "hujia") CharacterFixtureContent.Register(b, "classic:cao-cao");
            if (scene == "minimum-alcohol-king") CharacterFixtureContent.Register(b, "wumiao:lu-kang");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int minCards = 0, int maxCards = 0, int targets = 0) => new() {
                ["id"] = id, ["minCards"] = minCards, ["maxCards"] = maxCards, ["minTargets"] = targets, ["maxTargets"] = targets,
                ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            var defs = new JsonArray();
            var place = A("place", """{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}""", 1, 1, 1);
            place["sourceZones"] = N("[\"hand\"]"); place["cardCategories"] = N("[\"equipment\"]");
            var duel = A("fixed-native-duel", """{"op":"useSelectedActorDuel","target":"owner"}""", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            defs.Add(new JsonObject { ["id"] = Driver, ["revision"] = 1,
                ["modifiers"] = N("""[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0},{"id":"quota","query":"slashLimit","operation":"add","value":5,"priority":0}]"""),
                ["activations"] = new JsonArray(A("give", """{"op":"giveSelected","target":"selectedTarget"}""", 1, 40, 1),
                    A("stock", """{"op":"draw","target":"owner","amount":16}"""), A("wound", """{"op":"loseHp","target":"owner","amount":1}"""),
                    A("incoming", """{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}""", targets: 1),
                    A("trim-peer", """{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}""", targets: 1), place, duel) });
            if (scene == "minimum-alcohol-king") defs[0]!["cardPolicies"] = N("""[{"id":"actual-hand-grain-alcohol-K","kind":"alcoholKingIdentityRank","cardKinds":["alcohol"]}]""");
            if (scene is "duel" or "barbarian" or "arrows") defs[0]!["viewAs"] = new JsonArray(new JsonObject { ["id"] = "actual-single-played-response", ["inputKinds"] = new JsonArray(), ["inputSuits"] = new JsonArray(),
                ["outputKind"] = scene == "arrows" ? "dodge" : "slash", ["forPlay"] = false, ["forResponse"] = true, ["allowSameKind"] = true });
            defs.Add(N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (scene is "barbarian" or "arrows") defs.Add(new JsonObject { ["id"] = "fixture:gh-fixed-assault", ["revision"] = 1,
                ["triggers"] = new JsonArray(new JsonObject { ["id"] = "fixed-native-global-use", ["window"] = "turnStartBeforeNormalFlow", ["subject"] = "owner", ["optional"] = false, ["usageScope"] = "game", ["usageLimit"] = 1,
                    ["effects"] = new JsonArray(new JsonObject { ["op"] = "useVirtualOrdinaryTrick", ["target"] = "owner", ["actorRef"] = N("{\"kind\":\"owner\"}"), ["targetRef"] = N("{\"kind\":\"owner\"}"),
                        ["outputKind"] = scene == "barbarian" ? "barbarianAssault" : "arrowBarrage" }) }) });
            defs.Add(N("""{"id":"fixture:gh-low-rank","revision":1,"cardPolicies":[{"id":"real-low-pindian","kind":"pindianRankBySuit","inputSuit":"heart","value":2},{"id":"real-low-spade","kind":"pindianRankBySuit","inputSuit":"spade","value":2}]}"""));
            if (scene == "paid-loss") defs.Add(N("""{"id":"fixture:gh-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-original","skillId":"ol:xiewei","minimumHp":13}]}"""));
            if (scene == "multiple-targets") defs[0]!["modifiers"]!.AsArray().Add(N("""{"id":"real-two-targets","query":"cardTargetCount","operation":"add","value":1,"cardKinds":["slash"],"priority":0}"""));
            if (scene is "black-armor" or "multiple-targets") defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = N("""[{"id":"real-black-cost-colorless-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"colorless","options":[{"id":"continue"}]}]}]""") });
            if (scene is "lose" or "win" or "spade") defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = new JsonArray(new JsonObject {
                ["id"] = "real-reward-draw-child", ["window"] = "cardsGained", ["subject"] = "owner", ["destinationZones"] = N("[\"hand\"]"), ["movementOccurrence"] = "perBatch", ["movementReasons"] = new JsonArray(Draw), ["optional"] = false,
                ["effects"] = N("""[{"op":"chooseOption","target":"owner","resultBind":"reward","options":[{"id":"continue"}]}]""") }) });
            if (scene is "paid-loss" or "paid-death") defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = new JsonArray(new JsonObject {
                ["id"] = "paid-bait-child", ["window"] = "cardsMoved", ["subject"] = "owner", ["sourceZones"] = N("[\"hand\"]"), ["movementOccurrence"] = "perBatch", ["movementReasons"] = new JsonArray(Store), ["optional"] = false,
                ["effects"] = new JsonArray(N("""{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}"""), new JsonObject { ["op"] = "loseHp", ["target"] = "owner", ["amount"] = scene == "paid-death" ? 20 : 1 }) }) });
            if (scene is "lion" or "ox")
            {
                var payment = new JsonObject { ["id"] = "real-whole-discard-child", ["window"] = "cardsMoved", ["subject"] = "owner", ["sourceZones"] = N("[\"hand\"]"), ["movementOccurrence"] = "perBatch", ["movementReasons"] = new JsonArray(Discard), ["optional"] = false,
                    ["effects"] = N("""[{"op":"chooseOption","target":"owner","resultBind":"primary","options":[{"id":"continue"}]},{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]""") };
                var child = scene == "lion" ? N("""{"id":"real-lion-child","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"recovery"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"lion","options":[{"id":"continue"}]}]}""") :
                    N("""{"id":"real-grain-child","window":"cardsMoved","subject":"owner","sourceZones":["woodenOxGrain"],"movementOccurrence":"perBatch","movementReasons":["equipment.wooden-ox.grain-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"grain","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}""");
                defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = new JsonArray(payment, child) });
            }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实饵牌/原生拼点/整手弃牌行为夹具", optionLabels =
                new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "管亥真实原生夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var natural = scene is "duel" or "barbarian" or "arrows"; var own = new List<string> { Driver, Youque };
            if (scene != "hujia") own.Add(scene == "paid-loss" ? "fixture:gh-conditional" : Xiewei); else own.Add("classic:hujia");
            if (scene is "lose" or "lion" or "ox") own.Add("fixture:gh-low-rank"); if (scene is "paid-loss" or "paid-death" or "lion" or "ox" or "black-armor" or "multiple-targets" or "lose" or "win" or "spade") own.Add(Watch);
            b.AddSkill(new("fixture:gh-own-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(x => x, _ => -100000000d) });
            b.AddGeneral(new(Owner, "管亥真实机制", "ol-guan-hai", "fixture:gh-own-pick", scene == "hujia" ? "wei" : "qun", natural ? 2 : 12, own, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:gh-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:gh-pick-" + i; b.AddSkill(new(pick, "其他角色", "固定原生角色选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role =>
                    scene == "hujia" && i == 1 ? role == Role.Loyalist ? 100000000d : -100000000d :
                    i == 1 && natural && role == Role.Rebel ? 100000000d : 1000000d * (4 - i)) });
                var skills = new List<string> { Quiet }; if (i == 1 && scene is "barbarian" or "arrows") skills.Add("fixture:gh-fixed-assault"); if (scene == "hujia" && i == 1) skills.Add(Xiewei);
                if (scene is "win" or "spade" or "dodged") skills.Add("fixture:gh-low-rank");
                b.AddGeneral(new(peers[i - 1], "原生其他角色", "supporter", pick, scene == "hujia" && i == 1 ? "wei" : "shu", 12, skills, GeneralGender.Male));
            }
            var kind = scene switch { "minimum-alcohol-king" => CardKind.Alcohol, "duel" => CardKind.Duel, "barbarian" => CardKind.BarbarianAssault, "arrows" => CardKind.ArrowBarrage, "lion" => CardKind.SilverLion, "ox" => CardKind.WoodenOx, "black-armor" => CardKind.RenwangShield, _ => CardKind.Crossbow };
            b.AddCard(new("fixture:gh-card", "真实固定实体", CardCatalog.Get(kind).CategoryName, "原生实体", kind));
            b.AddDeck(new("fixture:gh-deck", "管亥真实小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("fixture:gh-card", scene == "black-armor" || scene == "spade" && i % 2 == 0 ? Suit.Spade : Suit.Heart, scene is "minimum" or "minimum-alcohol-king" ? i % 2 == 0 ? 5 : 9 : 7)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } :
                natural ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Loyalist)] = 2 } :
                scene == "hujia" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "管亥真实四人", 4, 4, roles, "fixture:gh-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
