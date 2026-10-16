using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class KeBiNengChecks
{
    private const string Skill = "ol:pingduan", Owner = "fixture:kbn-owner", Driver = "fixture:kbn-driver", Quiet = "fixture:kbn-quiet",
        Watch = "fixture:kbn-watch", Health = "fixture:kbn-health", Before = "fixture:kbn-before", Conditional = "fixture:kbn-source", Grain = "fixture:kbn-grain",
        Swap = "fixture:kbn-swap", Mode = "identity:classic-ke-bi-neng-fixture";
    public static void OrderedItemsAndPerItemDrawsRecastEquipmentCold()
    {
        var (g, r) = Start(); Draw(g); Equip(g, CardKind.SilverLion); Use(g, "lose"); Play(g); var hp = V(g).Hp; var hand = V(g).HandCount;
        var lion = V(g).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id; Begin(g); BasicBoundary(g);
        var prompt = P(g)!; var before = State(g);
        Need(prompt is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
            !g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
            "The original recipient privately chooses its genuine legal basic actions; another seat cannot see or answer that choice.");
        g = Cold(g, r); ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.CardId > 0 && a.ConversionSource is null); RewardBoundary(g, 1);
        Need(Current(g)!.CompletedItems == 1 && E<KeBiNengPingduanDrawPaidEvent>(g) is [{ Item: 1, RecastDraw: false, DrawCount: 1 }] && V(g).HandCount == hand,
            "The whole real Slash finishes before its separate first reward draw and before any recast is requested.");
        g = Cold(g, r); Continue(g); RecastBoundary(g); var recast = Current(g)!.Cards.First(c => c.From.Zone == CardZoneKind.Hand); var recastId = recast.CardId;
        Answer(g, c => c.Cards.SequenceEqual([recastId])); Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.RecastDrawChildren && P(g)?.SkillPrompt?.SkillId == Watch);
        Need(E<CardRecastEvent>(g).Single().CardId == recastId && E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => e.RecastDraw) == 1,
            "A real trick entity is recast, and its ordinary recast draw has its own native CardsGained child before the second reward.");
        g = Cold(g, r); Continue(g); RewardBoundary(g, 2);
        Need(E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => e.Item == 2) == 2 && V(g).HandCount == hand + 1,
            "Recast's own draw and the completed item's extra draw are two separate actual native draws, rather than one combined final reward.");
        g = Cold(g, r); Continue(g); EquipmentBoundary(g); Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
        Need(V(g).Hp == hp + 1 && V(g).Equipment.All(c => c.Id != lion) && V(g).Hand.Any(c => c.Id == lion) && Current(g)!.CompletedItems == 3,
            "The selected equipped SilverLion genuinely moves to the owner's hand and heals its former owner before its native HP child.");
        g = Cold(g, r); Continue(g); RewardBoundary(g, 3); g = Cold(g, r); Continue(g); Play(g);
        Need(E<KeBiNengPingduanCompletedEvent>(g) is [{ CompletedItems: 3 }] && E<KeBiNengPingduanDrawPaidEvent>(g).Length == 4 && V(g).HandCount == hand + 3 &&
            g.CardMovements.Count(m => m.CardId == lion && m.Reason.Value == "skill-program.ke-bi-neng.pingduan.equipment") == 1 && !g.GetHumanLegalActions().Any(a => a.SkillId == Skill),
            "All three items settle once, with exactly three rewards plus the recast draw, and consume only the accepted Play allowance."); _ = Cold(g, r);
    }
    public static void NativeBasicKindsConversionsAndQuotaCold()
    {
        foreach (var scenario in new[] { "peach", "alcohol", "converted", "round", "funded", "chained", "zhangba" })
        {
            var (g, r) = Start(scenario);
            if (scenario == "peach") { Use(g, "lose"); Play(g); }
            if (scenario == "funded")
            {
                while (V(g).Hp > V(g).HandCount) { Use(g, "lose"); Play(g); }
                Need(V(g).Hp == V(g).HandCount && V(g).Hand.Count > 0 && V(g).Hand.All(c => c.Suit == Suit.Heart),
                    "The real funded actor first pays native HP loss until its actual nonempty same-color hand exactly equals HP.");
            }
            if (scenario == "zhangba") { Draw(g); Equip(g, CardKind.ZhangbaSerpentSpear); }
            if (scenario == "chained") { Use(g, "chain"); Play(g); Need(V(g).IsChained, "The release conversion begins with a genuinely chained actor."); }
            var hp = V(g).Hp; Begin(g); Need(Current(g)?.Stage == KeBiNengPingduanStage.BasicChoice, "No legal BasicChoice in scenario " + scenario + ": " + Diagnostic(g)); BasicBoundary(g); g = Cold(g, r);
            ChooseBasic(g, a => scenario switch { "peach" => a.Kind == LegalActionKind.Peach, "alcohol" => a.Kind == LegalActionKind.Alcohol,
                "converted" => a.PlayedCardKind == CardKind.FireSlash && a.ConversionSource?.BindingId == "converted-basic", "round" => a.RoundOnceCardlessBasicUse is not null,
                "funded" => a.DrawFundedDistinctBasicUse is not null, "chained" => a.ChainedStateBasicUse is not null, _ => a.EquipmentKind == CardKind.ZhangbaSerpentSpear });
            if (scenario == "peach") { Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); }
            if (scenario == "funded")
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
                Need(Current(g)?.Stage == KeBiNengPingduanStage.BasicIssuing && g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Count() == 1 && !E<KeBiNengPingduanNativeIssuedEvent>(g).Any(),
                    "The native draw-funded payment owns its actual pre-Use draw child while Pingduan keeps the original exact accepted basic intent.");
                g = Cold(g, r); Continue(g);
            }
            Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            var issued = E<KeBiNengPingduanNativeIssuedEvent>(g).Single().Origin;
            Need(issued.ActorSeat == 0 && E<KeBiNengPingduanNativeReturnedEvent>(g).Count(e => e.Origin == issued) == 1 &&
                E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems == 1 && E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => e.Item == 1) == 1,
                "Every real basic family returns through one native use and earns exactly one first-item reward, even when no trick can follow.");
            if (scenario == "peach") Need(issued.CardKind == CardKind.Peach && V(g).Hp == hp + 1, "A real self Peach uses native recovery and its HP child rather than a synthetic Slash.");
            if (scenario == "alcohol") { Play(g); Need(V(g).HasAlcoholEffect && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol), "Pingduan's real Alcohol pays its ordinary Alcohol allowance."); }
            if (scenario == "converted") Need(issued.CardKind == CardKind.FireSlash && g.CardMovements.Count(m => m.Reason == CardMoveReasons.Use && m.From == CardLocation.Hand(0)) == 1, "A physical Dodge really pays the converted elemental Slash.");
            if (scenario == "chained") Need(!V(g).IsChained && E<ChainedStateBasicPaidEvent>(g).Length == 1, "The exact native chained-state cost is actually paid once.");
            if (scenario == "funded") Need(E<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 && E<DrawFundedDistinctBasicReturnedEvent>(g).Length == 1, "A paid draw-funded basic preserves its own native return as well as Pingduan's outer return.");
            if (scenario == "zhangba") Need(g.CardMovements.Count(m => m.Reason == CardMoveReasons.Use && m.From == CardLocation.Hand(0)) == 2, "True Zhangba pays two actual owned entities, with its native weapon provenance.");
            Need(E<CardUseDebitRecordedEvent>(g).All(e => e.Debit.CardActionId != issued.ActionId), "Pingduan Slash does not debit the ordinary Slash count."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("no-recast"); Begin(g); BasicBoundary(g); ChooseBasic(g, a => a.Kind == LegalActionKind.Slash); Play(g);
            var ordinary = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash); Native(g, ordinary); Play(g);
            Need(E<CardUseDebitRecordedEvent>(g).Length == 1 && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash), "Pingduan first leaves one real ordinary Slash, whose actual native debit then exhausts the phase allowance."); _ = Cold(g, r);
            var (other, registry) = Start("no-recast"); Native(other, other.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash)); Play(other); Begin(other);
            Reach(other, () => E<KeBiNengPingduanCompletedEvent>(other).Length == 1);
            Need(E<KeBiNengPingduanCompletedEvent>(other).Single().CompletedItems == 0 && !E<KeBiNengPingduanNativeIssuedEvent>(other).Any(),
                "Using the ordinary Slash first prevents a quota-exhausted Pingduan Slash; the request does not waive its current-use qualification."); _ = Cold(other, registry);
        }
        {
            var (g, r) = Start("identity"); Draw(g); Equip(g, CardKind.SilverLion); Use(g, "lose"); Play(g); var hp = V(g).Hp; var lion = V(g).Equipment.Single().Id; var dodge = V(g).Hand.First(c => c.Kind == CardKind.Dodge).Id;
            Need(g.GetHumanLegalActions().Any(a => a.CardId == dodge && a.ConversionSource?.BindingId == "optional-trick" && a.PlayedCardKind == CardKind.Duel), "The negative recast material has a genuine currently available optional native Duel conversion.");
            Begin(g); BasicBoundary(g); ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.CardId == lion && a.ConversionSource?.BindingId == "equipment-basic");
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); RecastBoundary(g);
            Need(V(g).Hp == hp + 1 && g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.Reason == CardMoveReasons.Use) == 1,
                "The original equipped SilverLion really pays a native converted FireSlash and drains its actual recovery child before requesting the next item.");
            var lightning = V(g).Hand.First(c => c.Kind == CardKind.Lightning).Id;
            Need(Current(g)!.Cards.All(c => c.CardId != dodge && CardCatalog.Get(V(g).Hand.Single(h => h.Id == c.CardId).Kind).CategoryName == "锦囊牌") && Current(g)!.Cards.Any(c => c.CardId == lightning),
                "A real delayed trick can be recast, while merely being usable as a Duel never changes a physical Dodge into a recastable trick.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([lightning]));
            Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            Need(E<CardRecastEvent>(g).Single() is { CardKind: CardKind.Lightning } && V(g).Hand.Any(c => c.Id == dodge) &&
                g.CardMovements.Count(m => m.CardId == lightning && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.RecastDiscard) == 1,
                "The selected real delayed trick is paid once and receives its native recast draw plus reward, retaining the unselected optional conversion material."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("furong-alcohol"); var paid = V(g).Hand.First(c => c.Kind == CardKind.Alcohol).Id;
            Accept(g, new UseProgramSkillCommand(0, "ol:xiaosi", "paired-basic-discard", [], [1], g.Revision, P(g)!.PromptId));
            Reach(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: "ol:xiaosi" } && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost"));
            Answer(g, c => c.Cards.SequenceEqual([paid]));
            Reach(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: "ol:xiaosi" } && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use"));
            Need(E<FuRongPairPaidEvent>(g).Length == 2 && E<FuRongPairPaidEvent>(g).All(e => e.Payment.PrintedKind == CardKind.Alcohol),
                "Both participants really discard their own Alcohol, and the AI answers its own mandatory private payment through native Advance.");
            g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Cards.SequenceEqual([paid]));
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } || P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "finish") == true);
            if (P(g)?.Kind != DecisionKind.PlayCard) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Play(g);
            var free = E<FuRongNativeIssuedEvent>(g).Single().Return.CardUseFrameId;
            Need(V(g).HasAlcoholEffect && E<JianjiOrdinaryAlcoholPaidEvent>(g).Single(e => e.NativeUseId == free) is { CountsQuota: false },
                "The genuinely issued Xiaosi discard Alcohol applies native wine while preserving its own explicit unlimited-use exemption in the shared actual-turn ledger.");
            g = Cold(g, r); Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource?.BindingId == "alcohol-slash")); Play(g);
            Need(!V(g).HasAlcoholEffect && E<CardUseDebitRecordedEvent>(g).Length == 1, "A real ordinary converted Slash consumes the free wine and spends only its actual Slash allowance.");
            Begin(g); BasicBoundary(g);
            Need(Current(g)!.BasicActions.Any(a => a.Kind == LegalActionKind.Alcohol), "The earlier quota-free discarded Alcohol cannot incorrectly block Pingduan's ordinary Alcohol in the same real turn.");
            g = Cold(g, r); ChooseBasic(g, a => a.Kind == LegalActionKind.Alcohol); Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            var ordinary = E<KeBiNengPingduanNativeIssuedEvent>(g).Single().Origin;
            Need(V(g).HasAlcoholEffect && ordinary.CardKind == CardKind.Alcohol && E<JianjiOrdinaryAlcoholPaidEvent>(g).Single(e => e.NativeUseId == ordinary.CardUseFrameId) is { CountsQuota: true } &&
                E<JianjiOrdinaryAlcoholPaidEvent>(g).Count(e => e.ActualTurn == g.State.TurnNumber && e.ActorSeat == 0 && e.CountsQuota) == 1 && E<AlcoholAppliedEvent>(g).Length == 2,
                "Only the subsequent ordinary Pingduan Alcohol debits the turn's wine allowance, after two different real native uses and a cold return."); _ = Cold(g, r);
        }
    }
    public static void ForeignRecipientPrivacyDeclinesAndOrderedStopsCold()
    {
        {
            var (g, r) = Start("foreign-stale"); var firstTurn = g.State.TurnNumber;
            Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol)); Play(g);
            Need(E<JianjiOrdinaryAlcoholPaidEvent>(g).Any(e => e.ActualTurn == firstTurn && e.ActorSeat == 0 && e.CountsQuota), "The earlier real self turn actually spent its native ordinary Alcohol allowance.");
            Use(g, "lose"); Play(g); Use(g, "lose"); Play(g);
            Need(V(g).Hp < Enumerable.Range(1, 3).Min(s => V(g, s).Hp), "Real HP loss gives the source AI one publicly lowest-HP target under its ordinary target policy.");
            g = Cold(g, r); End(g); Reach(g, () => Current(g) is { Stage: KeBiNengPingduanStage.BasicChoice, ActorSeat: 0 } && P(g)?.PlayerSeat == 0);
            var turn = g.State.TurnNumber; var prompt = P(g)!;
            Need(turn > firstTurn && g.State.CurrentSeat == 1 && V(g, 1).Skills!.Any(s => s.ContentId == Skill) && E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 0) == 1 &&
                Current(g)!.BasicActions.Any(a => a.Kind == LegalActionKind.Alcohol) && !V(g).HasAlcoholEffect,
                "The actual AI source requests the original human in a later foreign turn: the recipient has not begun another own turn to reset its old flag, yet current-turn Alcohol remains legal.");
            g = Cold(g, r); ChooseBasic(g, a => a.Kind == LegalActionKind.Alcohol); Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            var state = State(g); Need(E<JianjiOrdinaryAlcoholPaidEvent>(g).Count(e => e.ActualTurn == turn && e.ActorSeat == 0 && e.CountsQuota) == 1 && V(g).HasAlcoholEffect &&
                !g.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(c => c.Cards.Count == 1).Id, g.Revision)).Accepted && State(g) == state,
                "The new actual turn pays its own single Alcohol quota once, and replaying the earlier accepted private choice cannot use or reward it again.");
            g = Cold(g, r); Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.TurnNumber == turn && e.ActorSeat == 1));
            Need(!V(g).HasAlcoholEffect && E<AlcoholExpiredEvent>(g).Count(e => e.PlayerSeat == 0) == 2,
                "The earlier self-turn wine and the later foreign-turn wine each expire at their own actual turn end without borrowing the recipient's next turn boundary."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("actor"); Draw(g); Begin(g); BasicBoundary(g); ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.CardId > 0 && a.ConversionSource is null && a.TargetSeats.SequenceEqual([1]));
            Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true); Answer(g, c => c.Targets.SequenceEqual([3]));
            Reach(g, () => E<ProgramCardUseActorReplacedEvent>(g).Any() && P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true);
            var changed = E<ProgramCardUseActorReplacedEvent>(g).Single(); var native = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == changed.CardUseFrameId);
            Need(native.SourceSeat == 3 && native.Action!.ActorSeat == 3 && native.Action.ProviderSeat == 0 && native.KeBiNengPingduanUse!.ActorSeat == 0,
                "A genuine finalized-target role replacement changes the current card actor while preserving the accepted Pingduan recipient and original payment provider.");
            g = Cold(g, r); Continue(g); RecastBoundary(g);
            Need(E<KeBiNengPingduanDrawPaidEvent>(g).Single().Item == 1 && Current(g)!.ActorSeat == 0 && E<KeBiNengPingduanNativeReturnedEvent>(g).Single().Origin.ActorSeat == 0,
                "The exact replacement chain returns after cold native completion and rewards the original recipient, rather than the replacement damage source.");
            DeclineItem(g); Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1); _ = Cold(g, r);
        }
        foreach (var stop in new[] { 0, 1, 2 })
        {
            var (g, r) = Start(); Draw(g); if (stop == 2) Equip(g, CardKind.SilverLion); Begin(g); BasicBoundary(g);
            if (stop == 0) DeclineItem(g);
            else
            {
                ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null); RecastBoundary(g);
                if (stop == 1) DeclineItem(g);
                else { Answer(g, c => c.Cards.Count == 1); EquipmentBoundary(g); g = Cold(g, r); DeclineItem(g); }
            }
            Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            Need(E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems == stop && E<KeBiNengPingduanItemPaidEvent>(g).Length == stop &&
                E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => !e.RecastDraw) == stop,
                "Declining each real ordered item stops every later item and grants no reward for an unexecuted item."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("no-basic"); Begin(g); Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            Need(!E<KeBiNengPingduanItemPaidEvent>(g).Any() && !E<KeBiNengPingduanDrawPaidEvent>(g).Any(), "A real hand containing only Dodge has no usable basic here and never skips directly to recast or equipment."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("foreign-alcohol"); var alcohol = V(g).Hand.First(c => c.Kind == CardKind.Alcohol).Id; Use(g, "gift", [1], [alcohol]); Play(g); var current = g.State.CurrentSeat;
            Begin(g, 1); Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.BasicChoice);
            var p = P(g)!; var state = State(g);
            Need(p is { PlayerSeat: 1, IsPrivate: true }, "The foreign AI personally owns its private request: " + Diagnostic(g));
            Need(g.CreateSnapshot(0).PendingDecision is null, "The human source cannot see the foreign recipient's private choice.");
            Need(!g.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && State(g) == state,
                "The source cannot answer the foreign recipient's real private request, and the rejected wrong-seat reply leaves all state intact.");
            g = Cold(g, r); Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            Need(E<KeBiNengPingduanNativeIssuedEvent>(g).Single().Origin.ActorSeat == 1 && E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems >= 1 && g.State.CurrentSeat == current &&
                E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 1) == 0, "Real AI Advance executes the recipient's native basic outside its turn, retaining the actual source turn and all response/damage children."); _ = Cold(g, r);
            Need(V(g, 1).HasAlcoholEffect && E<AlcoholAppliedEvent>(g).Single().SourceSeat == 1, "The foreign actor's genuine basic is an actual Alcohol effect, not a fabricated Slash or direct skill buff.");
            Play(g); g = Cold(g, r); End(g); Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0));
            Need(!V(g, 1).HasAlcoholEffect && E<AlcoholExpiredEvent>(g).Count(e => e.PlayerSeat == 1) == 1,
                "The real foreign Alcohol expires exactly once at its actual enclosing turn end instead of leaking into the recipient's next turn."); _ = Cold(g, r);
        }
    }
    public static void PaidChildrenSourceLossDeathLionAndOxCold()
    {
        {
            var (g, r) = Start("slot-cancel"); Draw(g); Equip(g, CardKind.SilverLion);
            Native(g, g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "ol:youlong" && a.PlayedCardKind == CardKind.DrawTwo));
            Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "equipment-slot-named-use") == true);
            Answer(g, c => c.Parameters.GetValueOrDefault("equipment-slot") == EquipmentSlot.DefensiveHorse.ToString()); Play(g);
            Begin(g); BasicBoundary(g); ChooseBasic(g, a => a.ConversionSource?.SkillId == "ol:youlong" && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "equipment-slot-named-use") == true);
            g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("equipment-slot") == EquipmentSlot.Armor.ToString()); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            Need(Current(g)?.Stage == KeBiNengPingduanStage.BasicIssuing && !E<KeBiNengPingduanNativeIssuedEvent>(g).Any() && E<EquipmentSlotNamedUsePaidEvent>(g).Length == 2,
                "The real nested Youlong payment owns its abolished armor and private movement child before any Pingduan Basic Use exists.");
            g = Cold(g, r); Continue(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true); Answer(g, c => c.Targets.SequenceEqual([1]));
            Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            Need(!V(g, 1).IsAlive && g.State.Winner == Winner.None && E<EquipmentSlotNamedUseCancelledEvent>(g).Length == 1 && E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems == 0 &&
                !E<KeBiNengPingduanNativeIssuedEvent>(g).Any() && !E<KeBiNengPingduanDrawPaidEvent>(g).Any(),
                "An actually paid native slot cost whose original target dies returns its exact cancellation, finishes the outer request and grants no use reward or later item."); _ = Cold(g, r);
        }
        foreach (var scenario in new[] { "paid-loss", "source-death", "reward-damage" })
        {
            var (g, r) = Start(scenario); Draw(g); if (scenario == "paid-loss") Equip(g, CardKind.SilverLion); Begin(g); BasicBoundary(g);
            ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.CardId > 0 && a.ConversionSource is null); RewardBoundary(g, 1);
            var id = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.KeBiNengPingduan is not null).Id; g = Cold(g, r); Continue(g);
            if (scenario == "reward-damage") { Reach(g, () => P(g)?.SkillPrompt?.SkillId == Before); g = Cold(g, r); Continue(g); }
            if (scenario == "source-death")
            {
                Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Any(e => e.FrameId == id));
                Need(!V(g).IsAlive && g.State.Winner == Winner.None && E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems == 1 && E<KeBiNengPingduanDrawPaidEvent>(g).Length == 1,
                    "Actual sourced damage and dying kill the non-Lord recipient in the paid first reward subtree; no later item or duplicate reward is fabricated."); _ = Cold(g, r); continue;
            }
            RecastBoundary(g);
            if (scenario == "paid-loss")
            {
                Need(V(g).Skills!.All(s => s.ContentId != Skill), "A real reward child withdraws the exact HP-conditional source after the first native use was already paid.");
                g = Cold(g, r); Answer(g, c => c.Cards.Count == 1 && V(g).Hand.Any(h => h.Id == c.Cards[0])); EquipmentBoundary(g); Answer(g, c => c.Cards.Count == 1);
            }
            else DeclineItem(g);
            Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Any(e => e.FrameId == id));
            Need(E<KeBiNengPingduanNativeIssuedEvent>(g).Length == 1 && E<KeBiNengPingduanCompletedEvent>(g).Single().CompletedItems == (scenario == "paid-loss" ? 3 : 1),
                "Paid source loss and actual nested damage return to the original frozen instruction without repaying its native use or truncating an accepted surviving effect."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("ox"); Draw(g); Equip(g, CardKind.WoodenOx); var ox = V(g).Equipment.Single().Id; var grain = V(g).Hand[0].Id;
            Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], g.Revision, P(g)!.PromptId)); Play(g); Need(V(g).WoodenOxGrain!.Single().Id == grain, "Real WoodenOx stores one exact private hand entity before Pingduan.");
            Begin(g); BasicBoundary(g); ChooseBasic(g, a => a.Kind == LegalActionKind.Slash && a.CardId > 0 && V(g).Hand.Any(h => h.Id == a.CardId)); RecastBoundary(g); Answer(g, c => c.Cards.Count == 1);
            EquipmentBoundary(g); g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([ox])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Grain);
            var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.KeBiNengPingduan is not null); var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.Movements.Any(m => m.CardId == grain));
            Need(Current(g)?.CompletedItems == 3 && moved.Batch.ParentBatchId == parent.KeBiNengPingduan!.PaidBatchId && V(g).Hand.Any(c => c.Id == ox) && V(g).WoodenOxGrainCount == 0 &&
                !E<KeBiNengPingduanDrawPaidEvent>(g).Any(e => e.Item == 3), "The native Ox transfer and its exact nested grain disposal precede the third reward and retain the owning payment batch.");
            g = Cold(g, r); Continue(g); Reach(g, () => E<KeBiNengPingduanCompletedEvent>(g).Length == 1);
            Need(g.CardMovements.Count(m => m.CardId == grain && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0) &&
                E<KeBiNengPingduanDrawPaidEvent>(g).Count(e => e.Item == 3) == 1, "Cold grain-movement damage drains before exactly one final reward, without storing or discarding the original grain twice."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static KeBiNengPingduanReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.KeBiNengPingduan is not null)?.KeBiNengPingduan;
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Ke Bi Neng command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the actual human answers a private request."); var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact Ke Bi Neng choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Begin(GameEngine g, int target = 0) => Accept(g, new UseProgramSkillCommand(0, Skill, "ordered-three-items", [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Draw(GameEngine g) { Use(g, "draw"); Play(g); }
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources ?? [] });
    private static void Equip(GameEngine g, CardKind kind)
    { var id = V(g).Hand.First(c => c.Kind == kind).Id; Use(g, "place", [0], [id]); Play(g); }
    private static void BasicBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.BasicChoice);
    private static void RecastBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.RecastChoice);
    private static void EquipmentBoundary(GameEngine g) => Reach(g, () => Current(g)?.Stage == KeBiNengPingduanStage.EquipmentChoice);
    private static void RewardBoundary(GameEngine g, int item) => Reach(g, () => Current(g)?.CompletedItems == item && P(g)?.SkillPrompt?.SkillId == Watch && Current(g)?.Stage != KeBiNengPingduanStage.RecastDrawChildren);
    private static void ChooseBasic(GameEngine g, Func<LegalAction, bool> predicate)
    { var r = Current(g)!; var index = Enumerable.Range(0, r.BasicActions.Count).FirstOrDefault(i => predicate(r.BasicActions[i]), -1); Need(index >= 0, "No expected real native basic: " + Diagnostic(g)); Answer(g, c => c.Parameters.GetValueOrDefault("option") == index.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    private static void DeclineItem(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option") == "decline");
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 260; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Ke Bi Neng ended before its actual boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "after-child") == true) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-child");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("declaration-answer") == "pass") == true) Answer(g, c => c.Parameters.GetValueOrDefault("declaration-answer") == "pass");
            else if (p is not null) throw new InvalidOperationException("Unexpected Ke Bi Neng boundary: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded Ke Bi Neng boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => c.Parameters) } : null,
        Receipt = Current(g)?.Stage, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Tail = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(copy), "Cold replay retains every private native option, original actor and exact paid use/movement/reward child."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == "source-death" ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ke-bi-neng", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:ke-bi-neng");
            if (scenario == "slot-cancel") CharacterFixtureContent.Register(b, "ol:wo-long-feng-chu");
            if (scenario == "furong-alcohol") CharacterFixtureContent.Register(b, "ol:fu-rong");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
              {"id":"gift","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
              {"id":"trim","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"native-health","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备", description = "仅原生指令" }, [Quiet] = new { name = "安静其他回合", description = "跳实际出牌" },
                [Health] = new { name = "真实回复子窗", description = "保留真实HP回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body, string[]? options = null) { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); names[id] = new { name = id, description = "原生边界", optionLabels = (options ?? []).ToDictionary(x => x, x => x) }; }
            var skills = new List<string> { Driver, Health };
            if (scenario == "furong-alcohol") skills.Add("ol:xiaosi");
            if (scenario == "slot-cancel")
            {
                skills.Add("ol:youlong"); Add(Watch, "\"triggers\":[{\"id\":\"actual-slot-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementReasons\":[\"equipment.slot-abolished\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"slot\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}]}]", ["continue"]); skills.Add(Watch);
            }
            if (scenario == "paid-loss") { Add(Conditional, "\"hpSkillGrants\":[{\"id\":\"full-source\",\"skillId\":\"ol:pingduan\",\"minimumHp\":13}]"); skills.Add(Conditional); } else skills.Add(Skill);
            if (scenario is "plain" or "paid-loss" or "source-death" or "reward-damage")
            {
                var child = scenario == "paid-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scenario == "source-death" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : scenario == "reward-damage" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" : "";
                var limit = scenario == "plain" ? "" : "\"usageScope\":\"game\",\"usageLimit\":1,";
                Add(Watch, "\"triggers\":[{\"id\":\"actual-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.ke-bi-neng.pingduan.reward\",\"card.recast.draw\"]," + limit + "\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"child\",\"options\":[{\"id\":\"continue\"}]}" + child + "]}]", ["continue"]); skills.Add(Watch);
            }
            if (scenario == "funded") { Add(Watch, "\"triggers\":[{\"id\":\"real-paid-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"skill-program.draw-funded-distinct-basic.draw\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"cost\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Watch); }
            if (scenario == "ox") { Add(Grain, "\"triggers\":[{\"id\":\"native-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"movementOccurrence\":\"perBatch\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"grain\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}]", ["continue"]); skills.Add(Grain); }
            var driver = rules["skills"]![0]!;
            string? conversion = scenario switch
            {
                "converted" => """{"id":"converted-basic","inputKinds":["dodge"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"extendedUse":true}""",
                "round" => """{"id":"round-basic","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"roundOnceCardlessBasic":{"usageGroup":"fixture-pingduan-round","usesPerRound":1}}""",
                "funded" => """{"id":"funded-basic","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"fixture:pingduan-funded"}}""",
                "chained" => """{"id":"release-basic","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"chainedStateCost":false}""", _ => null
            };
            if (conversion is not null) driver["viewAs"] = new JsonArray(JsonNode.Parse(conversion));
            if (scenario == "furong-alcohol") driver["viewAs"] = JsonNode.Parse("""[{"id":"alcohol-slash","inputKinds":["alcohol"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]""");
            if (scenario == "identity") driver["viewAs"] = JsonNode.Parse("""[{"id":"optional-trick","inputKinds":["dodge"],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true},{"id":"equipment-basic","inputKinds":["silverLion"],"inputSuits":[],"inputCount":1,"sourceZones":["hand","equipment"],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"extendedUse":true}]""");
            if (scenario == "actor") { Add(Swap, "\"triggers\":[{\"id\":\"actual-actor\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"role\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Swap); }
            if (scenario == "reward-damage") { Add(Before, "\"triggers\":[{\"id\":\"native-damage\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"before\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Before); }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:kbn-pick-owner", "本人选将", "固定小夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实轲比能", "ol-ke-bi-neng", "fixture:kbn-pick-owner", "qun", scenario == "source-death" ? 13 : 12, skills));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:kbn-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index))) { var pick = "fixture:kbn-pick-" + index; b.AddSkill(new(pick, "其他角色", "固定公开次序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - index)) }); b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 12, scenario == "foreign-stale" && index == 0 ? [Skill] : [Quiet])); }
            b.AddCard(new("fixture:kbn-lion", "白银狮子", "装备牌", "真实离场回复", CardKind.SilverLion));
            b.AddCard(new("fixture:kbn-ox", "木牛流马", "装备牌", "真实粮移动", CardKind.WoodenOx));
            b.AddCard(new("fixture:kbn-spear", "丈八蛇矛", "装备牌", "真实两牌杀", CardKind.ZhangbaSerpentSpear));
            var ids = scenario is "no-basic" or "converted" or "round" or "funded" or "chained" ? new[] { "standard:dodge" } : scenario == "peach" ? ["standard:peach"] : scenario is "alcohol" or "foreign-alcohol" or "foreign-stale" or "furong-alcohol" ? ["standard:alcohol"] : scenario == "zhangba" ? new[] { "standard:dodge", "fixture:kbn-spear" } :
                scenario == "no-recast" ? ["standard:slash"] : scenario == "identity" ? ["standard:slash", "standard:dodge", "standard:lightning", "standard:duel", "fixture:kbn-lion"] : scenario == "slot-cancel" ? ["standard:slash", "standard:duel", "fixture:kbn-lion"] : scenario == "ox" ? ["standard:slash", "standard:duel", "fixture:kbn-ox"] :
                new[] { "standard:slash", "standard:peach", "standard:alcohol", "standard:lightning", "standard:duel", "fixture:kbn-lion" };
            b.AddDeck(new("fixture:kbn-deck", "固定实体小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(ids[i % ids.Length], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "轲比能真实原生小局", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:kbn-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
