using CardGame.Core;
using static GuanduCardChecks;

internal static class GuanduEventChecks
{
    public static void TenthUseCountsNativeCounterspellButNotPlayedSlash()
    {
        {
            var (g, r) = Start(CardKind.StoreGrain, counter: true, battleEvent: GuanduBattleEvent.TenWinsTenLosses);
            for (var i = 0; i < 8; i++) { Physical(g, CardKind.StoreGrain, [0]); Play(g); }
            var ninth = Physical(g, CardKind.StoreGrain, [1]);
            Until(g, () => P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
            var ninthUse = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == ninth && e.CardKind == CardKind.StoreGrain);
            Need(E<GuanduActualCardActionEvent>(g).Count(e => e.CountsAsUse) == 9 &&
                E<GuanduActualCardActionEvent>(g).Single(e => e.UseFrameId == ninthUse.ResolutionId).UseOrdinal == 9,
                "The incoming physical trick is the ninth logical use before its real native counterspell window opens."); Cold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "nullification" &&
                c.Parameters.GetValueOrDefault("conversion-binding-id") == "counter");
            Until(g, () => E<GuanduActualCardActionEvent>(g).Any(e => e.Kind == CardKind.Nullification && e.CountsAsUse));
            var counter = E<CardActionAcceptedEvent>(g).Single(e => e.Action.EffectiveKind == CardKind.Nullification).Action;
            var counterFact = E<GuanduActualCardActionEvent>(g).Single(e => e.ActionId == counter.ActionId);
            Need(counter.Type == CardActionType.Response && counterFact is { Type: CardActionType.Response, CountsAsUse: true, UseOrdinal: 10 } &&
                counter.PhysicalCards.Count == 1 && counter.ConversionChain.Single().BindingId == "counter",
                "A real converted Nullification keeps its native Response identity while contributing exactly the tenth logical Use."); Cold(g, r); Play(g);
            var eleventh = Physical(g, CardKind.StoreGrain, [1]); Play(g);
            var eleventhUse = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == eleventh && e.CardKind == CardKind.StoreGrain);
            Need(E<GuanduActualCardActionEvent>(g).Count(e => e.ActionId == counter.ActionId) == 1 &&
                E<GuanduActualCardActionEvent>(g).Count(e => e.CountsAsUse) == 11 &&
                E<GuanduActualCardActionEvent>(g).Single(e => e.UseFrameId == eleventhUse.ResolutionId).UseOrdinal == 11 &&
                !E<GuanduCardDrawAppliedEvent>(g).Any(e => e.UseFrameId == ninthUse.ResolutionId) &&
                E<GuanduCardDrawAppliedEvent>(g).Count(e => e.UseFrameId == eleventhUse.ResolutionId) == 1 &&
                !E<AutomaticExtraCardEffectIssuedEvent>(g).Any() &&
                g.CardMovements.Count(m => m.CardId == counter.PhysicalCards.Single().CardId && m.To == CardLocation.DiscardPile) == 1 &&
                g.CardMovements.Count(m => m.CardId == eleventh && m.To == CardLocation.DiscardPile) == 1,
                "The native counterspell is counted once, cancels the ninth trick, and prevents the following eleventh trick from repeating or repaying either material."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.Duel, battleEvent: GuanduBattleEvent.TenWinsTenLosses);
            Use(g, "jianji", 7); Until(g, () => P(g)?.Kind == DecisionKind.RespondSlash); Cold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "slash-response");
            Until(g, () => E<GuanduActualCardActionEvent>(g).Any(e => e.ActorSeat == 0 && e.Kind == CardKind.Slash));
            var response = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash).Action;
            Need(response.Type == CardActionType.Response &&
                E<GuanduActualCardActionEvent>(g).Single(e => e.ActionId == response.ActionId) is { CountsAsUse: false, UseOrdinal: 1 } &&
                E<GuanduActualCardActionEvent>(g).Count(e => e.CountsAsUse) == 1,
                "A real Slash played against another actor's Duel remains a response and cannot occupy the next logical Use ordinal."); Cold(g, r); Play(g);
            var next = Physical(g, CardKind.Duel, [7]); Play(g);
            var nextUse = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == next && e.CardKind == CardKind.Duel);
            Need(E<GuanduActualCardActionEvent>(g).Single(e => e.UseFrameId == nextUse.ResolutionId) is { CountsAsUse: true, UseOrdinal: 2 } &&
                E<GuanduActualCardActionEvent>(g).Count(e => e.ActionId == response.ActionId) == 1 &&
                E<DuelResponseEvent>(g).Any(e => e.ResponderSeat == 0 && e.UsedSlash),
                "After the genuine played Slash and its complete native response, the next physical Duel is only the second Use."); Cold(g, r);
        }
    }

    public static void TenthUsesGlobalNativeCounterAndRevalidatesOriginalTarget()
    {
        {
            var (g, r) = Start(CardKind.StoreGrain, battleEvent: GuanduBattleEvent.TenWinsTenLosses);
            Use(g, "jianji", 1); Play(g);
            Need(E<GuanduActualCardActionEvent>(g).Any(e => e.ActorSeat == 1 && e.Type == CardActionType.Use && e.UseOrdinal == 1),
                "A second real actor's native exact-card use contributes to the same match-wide Use counter.");
            for (var i = 0; i < 8; i++) { Physical(g, CardKind.StoreGrain, [0]); Play(g); }
            var card = Physical(g, CardKind.StoreGrain, [1, 2]);
            Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardId == card && f.GuanduCardEffect is not null)); Cold(g, r); Play(g);
            var action = E<GuanduActualCardActionEvent>(g).Single(e => e.Kind == CardKind.StoreGrain && e.UseOrdinal == 10);
            Need(action.ActorSeat == 0 && E<GuanduCardDrawAppliedEvent>(g).Count(e => e.UseFrameId == action.UseFrameId) == 4 &&
                E<AutomaticExtraCardEffectIssuedEvent>(g).Count(e => e.CardUseFrameId == action.UseFrameId) == 1 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == action.UseFrameId) == 1,
                "The tenth use across actors repeats every original legal target effect once without creating another use or charging its material twice."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.Reinforcements, battleEvent: GuanduBattleEvent.TenWinsTenLosses);
            for (var i = 0; i < 9; i++)
            {
                Use(g, "nick", 0); Play(g);
                Act(g, g.GetHumanLegalActions().First(a => a.ConversionSource?.BindingId == "fixed-peach")); Play(g);
            }
            Use(g, "nick", 1); Play(g);
            var card = Physical(g, CardKind.Reinforcements, [1]); Play(g);
            var use = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == card && e.CardKind == CardKind.Reinforcements);
            Need(V(g, 1).Hp == V(g, 1).MaxHp && E<GuanduCardRecoveryIssuedEvent>(g).Count(e => e.UseFrameId == use.ResolutionId) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.ResolutionId) == 1,
                "A tenth-card Reinforcements that heals its only target to full cannot repeat an effect against an originally selected target that is no longer legal."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.IronChain, battleEvent: GuanduBattleEvent.TenWinsTenLosses);
            for (var i = 0; i < 9; i++)
            {
                Use(g, "nick", 0); Play(g);
                Act(g, g.GetHumanLegalActions().First(a => a.ConversionSource?.BindingId == "fixed-peach")); Play(g);
            }
            Need(!V(g, 0).IsChained, "The real tenth Iron Chain starts with its self-selected target unchained.");
            var card = Physical(g, CardKind.IronChain, [0]);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardId == card));
            Need(g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing,
                "The self-targeted Iron Chain retains its real paid entity under its committed-use observer before either native toggle."); Cold(g, r); Play(g);
            var use = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == card && e.CardKind == CardKind.IronChain);
            Need(!V(g, 0).IsChained && E<IronChainStateChangedEvent>(g).Where(e => e.ResolutionId == use.ResolutionId && e.TargetSeat == 0)
                .Select(e => e.IsChained).SequenceEqual([true, false]) &&
                E<ExtraCardEffectStartedEvent>(g).Count(e => e.CardUseFrameId == use.ResolutionId) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.ResolutionId) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "A legal original self-target remains eligible for the second native Iron Chain toggle, without a second use or physical payment."); Cold(g, r);
        }
    }

    public static void WuchaoAndUnderdogUseActualDamageAndRecipientScopes()
    {
        {
            var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.WuchaoFire);
            foreach (var seat in new[] { 5, 6, 7 }) { Use(g, "chain", seat); Play(g); }
            var hp = new[] { V(g, 5).Hp, V(g, 6).Hp, V(g, 7).Hp };
            Physical(g, CardKind.Slash, [7]); Play(g);
            var hits = E<DamageAppliedEvent>(g).ToArray();
            Need(hits.Length == 3 && hits.All(e => e.Nature == DamageNature.Fire && e.Amount == 1) &&
                Enumerable.Range(5, 3).All(seat => V(g, seat).Hp == hp[seat - 5] - 1 && !V(g, seat).IsChained),
                "Wuchao converts a real normal Slash into fire before chain capture, propagating one native base damage to each actual chained recipient."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.Underdog);
            Use(g, "hurt", 0); Play(g); var hp = V(g, 7).Hp;
            Use(g, "damage", 7); Play(g); Use(g, "damage", 7); Play(g);
            Need(E<DamageAppliedEvent>(g).Where(e => e.TargetSeat == 7).Select(e => e.Amount).SequenceEqual([2, 1]) && V(g, 7).Hp == hp - 3,
                "Underdog compares the live recipient and source HP for every real damage instance; the bonus disappears after the first hit lowers the target below the source."); Cold(g, r);
        }
    }

    public static void GrainShortageReplacesNormalCountAndRewardsUseOnce()
    {
        var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.GrainShortage);
        Need(E<ActualDrawPhaseCompletedEvent>(g).Single(e => e.OwnerSeat == 0 && !e.IsExtra).DrawCount == 1,
            "Grain shortage reduces the actual normal two-card draw by one, rather than changing opening hands or arbitrary skill draws.");
        foreach (var seat in new[] { 5, 6, 7 }) { Use(g, "chain", seat); Play(g); }
        var fire = g.GetHumanLegalActions().First(a => a.PlayedCardKind == CardKind.FireSlash && a.TargetSeat == 7);
        Act(g, fire);
        Until(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.GuanduEventFlow?.Operation == SkillProgramEffectOp.RewardGuanduDamage && f.NativeDraw is not null));
        Need(g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.FireSlash),
            "The first native damage owns the once-per-use reward's actual draw request while retaining its paid elemental Slash parent."); Cold(g, r); Play(g);
        var reward = E<GuanduDamageRewardedEvent>(g).Single();
        Need(E<DamageAppliedEvent>(g).Count() == 3 && E<GuanduDamageRewardedEvent>(g).Count() == 1 &&
            E<CardUseDeclaredEvent>(g).Single(e => e.CardKind == CardKind.FireSlash).ResolutionId == reward.UseFrameId &&
            g.CardMovements.Count(m => m.Reason.Value == "guandu.grain-shortage.draw" && m.To == CardLocation.Hand(0)) == 1,
            "Three native chained damage recipients reward the real use only once through one actual one-card draw.");
        Use(g, "damage", 7); Play(g);
        Need(E<GuanduDamageRewardedEvent>(g).Count() == 1, "A standalone skill-damage frame is not another damage-causing card use."); Cold(g, r);
    }

    public static void TurnStartDuelAndHpLossRetainRealSourceAndCold()
    {
        foreach (var duel in new[] { false, true })
        {
            var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.SlayTheGenerals, reachPlay: false);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:battle-event" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "duel"));
            var hp = V(g, 0).Hp; Cold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == (duel ? "duel" : "lose"));
            if (duel)
            {
                Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
                Answer(g, c => c.Targets.SequenceEqual([7]));
                Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.Duel) && P(g)?.Kind == DecisionKind.RespondSlash);
                var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel);
                Need(use.CardId == 0 && use.Action!.PhysicalCards.Count == 0 && use.SourceSeat == 0 && use.TargetSeats.SequenceEqual([7]) && g.State.Phase == TurnPhase.Draw,
                    "The chosen turn-start duel is a real zero-material native use under the pre-flow turn boundary, not a direct damage shortcut."); Cold(g, r);
            }
            Play(g);
            Need(duel ? E<CardUseDeclaredEvent>(g).Count(e => e.CardKind == CardKind.Duel && e.SourceSeat == 0) == 1 :
                V(g, 0).Hp == hp - 1 && E<ProgramSkillHpLostEvent>(g).Count(e => e.SkillId == "guandu:battle-event" && e.TargetSeat == 0 && e.Amount == 1) == 1,
                "Each mandatory turn-start branch returns to the original normal draw exactly once with a real Duel use or real HP loss."); Cold(g, r);
        }
    }

    public static void HoldLineTracksRoundDebtAcrossGenuineExtraTurn()
    {
        var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.HoldTheLine);
        Use(g, "jianji", 7); Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        Need(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == "guandu:battle-event"),
            "The actual Slash response offers the mode rule's one-card Slash-as-Dodge conversion."); Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("conversion-skill-id") == "guandu:battle-event"); Play(g);
        var payment = E<GuanduHoldLineUsedEvent>(g).Single();
        Use(g, "jianji-again", 7); Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != "guandu:battle-event"),
            "A second genuine response within the same round cannot reuse the paid conversion."); Play(g);
        Use(g, "extra"); Play(g); var turn = g.State.TurnNumber; End(g);
        Until(g, () => IsPlay(g) && g.State.TurnNumber > turn);
        Need(E<RoundStartedEvent>(g).Last().RoundNumber == payment.RoundNumber && E<GuanduHoldLineConsumedEvent>(g).Count(e => e.Phase.ActorSeat == 0 && e.Credits == 1) == 1 &&
            V(g, 0).HandLimit == V(g, 0).Hp, "The next actual discard consumes the one hand-limit debt; a real extra turn remains in the same actual round.");
        Use(g, "jianji", 7); Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != "guandu:battle-event"),
            "A new actual turn does not reset a once-per-round conversion."); Play(g); Cold(g, r);
        turn = g.State.TurnNumber; End(g); Until(g, () => IsPlay(g) && g.State.TurnNumber > turn);
        Use(g, "jianji", 7); Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        Need(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == "guandu:battle-event") &&
            E<RoundStartedEvent>(g).Last().RoundNumber == payment.RoundNumber + 1,
            "Only the next complete normal round reopens the mode's Slash-as-Dodge choice.");
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("conversion-skill-id") == "guandu:battle-event"); Play(g);
        Need(E<GuanduHoldLineUsedEvent>(g).Count(e => e.OwnerSeat == 0) == 2, "The reopened actual-round choice can pay a second real material."); Cold(g, r);
    }

    public static void StandoffSwitchesAtRoundFiveAndOnlyFirstSlash()
    {
        var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.Standoff);
        for (var round = 1; round <= 4; round++)
        {
            Need(E<RoundStartedEvent>(g).Last().RoundNumber == round && V(g, 0).HandLimit == V(g, 0).Hp + round,
                "Early standoff uses the actual complete-round number as its additive hand limit.");
            var old = g.State.TurnNumber; End(g); Until(g, () => IsPlay(g) && g.State.TurnNumber > old);
        }
        Need(E<RoundStartedEvent>(g).Last().RoundNumber == 5 && V(g, 0).HandLimit == V(g, 0).Hp,
            "At the fifth real round the cumulative early-round hand-limit bonus ends."); Cold(g, r);
        var before = E<DamageAppliedEvent>(g).Count(); Physical(g, CardKind.Slash, [7]); Play(g); Physical(g, CardKind.Slash, [7]); Play(g);
        Need(E<DamageAppliedEvent>(g).Skip(before).Select(e => e.Amount).SequenceEqual([2, 1]),
            "Only the first actual Slash use in the later own turn gains damage, even when a legitimate second Slash allowance exists."); Cold(g, r);
    }

    public static void SlowAdvanceTracksResponsesSkippedDrawAndNextActualDraw()
    {
        {
            var (g, r) = Start(CardKind.Duel, battleEvent: GuanduBattleEvent.SlowAdvance);
            Use(g, "jianji", 7); Until(g, () => P(g)?.Kind == DecisionKind.RespondSlash);
            Answer(g, c => c.Parameters.GetValueOrDefault("conversion-binding-id") == "slash-response"); Play(g);
            var turn = g.State.TurnNumber; End(g); Until(g, () => IsPlay(g) && g.State.TurnNumber > turn);
            Need(!E<GuanduSlowAdvanceEarnedEvent>(g).Any(e => e.Phase.ActualTurnOwnerSeat == 0 && e.Phase.TurnNumber == turn) &&
                E<ActualDrawPhaseCompletedEvent>(g).Last(e => e.OwnerSeat == 0 && !e.IsExtra).DrawCount == 2,
                "A real Slash response to another actor's native Duel during the owner's actual Play counts as played Slash and prevents the next-draw credit."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.SlowAdvance);
            Use(g, "skip-draw"); Play(g); var turn = g.State.TurnNumber; End(g);
            Until(g, () => IsPlay(g) && g.State.TurnNumber > turn);
            Need(E<ActualDrawPhaseCompletedEvent>(g).Last(e => e.OwnerSeat == 0).Skipped && !E<GuanduSlowAdvanceConsumedEvent>(g).Any(e => e.OwnerSeat == 0),
                "Skipping the next actual normal draw cannot consume a Slow Advance bonus that never contributed cards."); Cold(g, r);
            turn = g.State.TurnNumber; End(g); Until(g, () => IsPlay(g) && g.State.TurnNumber > turn);
            Need(E<ActualDrawPhaseCompletedEvent>(g).Last(e => e.OwnerSeat == 0).DrawCount == 3 &&
                E<GuanduSlowAdvanceConsumedEvent>(g).Last(e => e.OwnerSeat == 0).Credits == 2,
                "The following actual normal draw gains one rather than stacking skipped credits, and only its real completion consumes them."); Cold(g, r);
        }
    }

    public static void ArroganceChecksHandMaximumAfterItsRealDiscard()
    {
        {
            var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.Arrogance);
            var hp = V(g, 0).Hp; var turn = g.State.TurnNumber;
            Need(V(g, 4).Hp == hp, "The independent-hand branch starts with tied highest commander health.");
            End(g); Until(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn));
            Need(V(g, 0).Hp == hp - 1 && E<ProgramSkillHpLostEvent>(g).Count(e => e.SkillId == "guandu:battle-event" && e.TargetSeat == 0) == 1,
                "Hand-count maximum causes real HP loss even when the owner is not the unique highest-HP role."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.Slash, battleEvent: GuanduBattleEvent.Arrogance);
            Use(g, "nick", 4); Play(g); Use(g, "grow-five", 1); Play(g);
            var hp = V(g, 0).Hp; var turn = g.State.TurnNumber; End(g);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "guandu:battle-event" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
            Need(V(g, 0).HandCount == V(g, 1).HandCount, "The normal discard has left equal hand counts before Arrogance's additional mandatory discard."); Cold(g, r);
            var card = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").Cards.Single();
            Answer(g, c => c.Cards.SequenceEqual([card]));
            if (P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") == true)
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
            Until(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == turn));
            Need(V(g, 0).Hp == hp && V(g, 0).HandCount < V(g, 1).HandCount &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "Arrogance pays its real one-card discard first, then recomputes hand maximum and avoids the now-inapplicable HP loss."); Cold(g, r);
        }
    }

    public static void TenthBorrowedSwordRechecksOnlyWeaponOwnersAndKeepsThePairedSourceVictim()
    {
        var (g, r) = Start(CardKind.BorrowedSword, battleEvent: GuanduBattleEvent.TenWinsTenLosses);
        Use(g, "equip-target", 7); Play(g); Use(g, "draw"); Play(g);
        var weapon = V(g, 7).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        Need(V(g, 0).Equipment.Count == 0 && E<GuanduActualCardActionEvent>(g).Count(e => e.CountsAsUse) == 1,
            "The future Slash victim is the unarmed original source; the equipment's genuine native use has already consumed exactly one position in the global counter.");
        for (var i = 0; i < 8; i++)
        {
            Use(g, "nick", 0); Play(g);
            Act(g, g.GetHumanLegalActions().First(a => a.ConversionSource?.BindingId == "fixed-peach")); Play(g);
        }
        var card = Physical(g, CardKind.BorrowedSword, [7, 0]);
        Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword);
        Need(use.TargetSeats.SequenceEqual([7, 0]) && E<GuanduActualCardActionEvent>(g).Single(e => e.UseOrdinal == 10 && e.CountsAsUse).UseFrameId == use.Id &&
            V(g, 7).Equipment.Any(c => c.Id == weapon),
            "The actual tenth use orders an equipped enemy to Slash the unarmed original source; the first genuine Slash retains the enemy's weapon.");
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        Need(E<BorrowedSwordResolvedEvent>(g).Count(e => e.ResolutionId == use.Id && e.UsedSlash && e.WeaponOwnerSeat == 7 && e.SlashTargetSeat == 0) == 1 &&
            E<ExtraCardEffectStartedEvent>(g).Count(e => e.CardUseFrameId == use.Id) == 1 &&
            V(g, 7).Equipment.Any(c => c.Id == weapon) && V(g, 0).Equipment.Count == 0 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing,
            "The second actual Slash starts from the same paid Borrowed Sword after paired-target legality is rechecked, without requiring its victim to own a weapon.");
        Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage"); Play(g);
        var resolutions = E<BorrowedSwordResolvedEvent>(g).Where(e => e.ResolutionId == use.Id).ToArray();
        Need(resolutions.Length == 2 && resolutions.All(e => e.UsedSlash && e.WeaponOwnerSeat == 7 && e.SlashTargetSeat == 0) &&
            resolutions.Select(e => e.SlashCardId).Distinct().Count() == 2 && V(g, 7).Equipment.Any(c => c.Id == weapon) &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1 &&
            !g.CardMovements.Any(m => m.CardId == weapon && m.From == CardLocation.Equipment(7)),
            "Two separately paid real Slash children finish once each while the weapon stays equipped and the original tenth trick pays and retires exactly once.");
        Cold(g, r);
    }

    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
}
