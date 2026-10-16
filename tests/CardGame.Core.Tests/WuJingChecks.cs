using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WuJingChecks
{
    private const string Heji = "ol:heji", Liubing = "ol:liubing", Owner = "fixture:wj-owner", Driver = "fixture:wj-driver", Quiet = "fixture:wj-quiet", Convert = "fixture:wj-convert", Response = "fixture:wj-response", Watch = "fixture:wj-watch", Health = "fixture:wj-health", Conditional = "fixture:wj-conditional", Prevent = "fixture:wj-prevent", Zero = "fixture:wj-zero", Fire = "fixture:wj-fire", Mode = "identity:classic-wu-jing-fixture";
    public static void HejiSelfForeignRecursivePhysicalConversionAndCold()
    {
        {
            var (g, r) = Start("recursive"); var hp = V(g, 2).Hp; Native(g, CardKind.Duel, 2); Reach(g, () => Optional(g, Heji));
            Need(V(g, 2).Hp == hp - 1 && E<WuJingHejiNativeIssuedEvent>(g).Length == 0, "The original self-used Duel genuinely resolves before Heji; target2 is outside ordinary one-range Slash distance.");
            Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice });
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null), "Only Wu Jing can inspect his available hand/conversion choices.");
            g = Cold(g, r); Follow(g, CardKind.Duel); Reach(g, () => Optional(g, Heji) && E<WuJingRewardIssuedEvent>(g).Length == 1);
            var reward = E<WuJingRewardIssuedEvent>(g).Single(); Need(reward.CardId is not null && V(g).Hand.Any(c => c.Id == reward.CardId) && V(g, 2).Hp == hp - 2, "A real unconverted native follow-up Duel gains one seeded red physical card before its next recursive choice.");
            Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice }); Need(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([reward.CardId!.Value]) && c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Duel)), "The just-earned red entity is genuinely usable in the next recursive Heji.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([reward.CardId!.Value]) && c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Duel)); Play(g);
            Need(V(g, 2).Hp == hp - 3 && E<WuJingHejiNativeIssuedEvent>(g).Length == 2 && E<WuJingHejiPaidEvent>(g).Length == 2 && E<WuJingRewardIssuedEvent>(g).Length == 2 && E<WuJingHejiReturnedEvent>(g).Length == 2, "Two real recursive native uses each pay, resolve, reward and return exactly once; a later optional decline stops the chain."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("foreign"); Native(g, CardKind.Duel, 2); Play(g);
            Need(E<WuJingHejiNativeIssuedEvent>(g).Any(e => e.ActorSeat == 1 && e.TargetSeat == 2 && E<CardUseDeclaredEvent>(g).Any(d => d.ResolutionId == e.IncomingUseId && d.SourceSeat == 0)) && E<WuJingRewardIssuedEvent>(g).All(e => e.CardId is null), "A foreign original user's real Duel triggers another living Wu Jing, with the same target and a genuine empty-red-deck reward rather than fabricated cards."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("conversion"); Native(g, CardKind.Duel, 2); Reach(g, () => Optional(g, Heji)); Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice }); g = Cold(g, r); Follow(g, CardKind.Duel);
            var live = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.WuJingHeji is not null); var action = live.Action!;
            Need(action.Type == CardActionType.Use && action.EffectiveKind == CardKind.Duel && action.PhysicalCards.Count == 1 && action.ConversionChain.Any(s => s.SkillId == Convert) && E<WuJingHejiPaidEvent>(g).Count(e => e.UseId == live.Id) == 1, "The genuine paid native Duel retains its actual single entity and exact conversion source before completion."); g = Cold(g, r); Play(g);
            Need(E<WuJingHejiNativeIssuedEvent>(g).Single() is { RewardEligible: false, Kind: CardKind.Duel } issued && issued.UseId == live.Id && issued.ActionId == action.ActionId && E<WuJingHejiPaidEvent>(g).Length == 1 && E<WuJingHejiReturnedEvent>(g).Count(e => e.UseId == live.Id) == 1 && E<WuJingRewardIssuedEvent>(g).Length == 0, "A genuine single-entity converted Duel is fully usable by Heji while its conversion prevents only the red reward."); _ = Cold(g, r);
        }
        foreach (var scene in new[] { "round-zero", "draw-zero", "chain-zero", "kenshang" })
        {
            var (g, r) = Start(scene); if (scene == "chain-zero") { Use(g, "chain"); Play(g); }
            var hp = V(g, 2).Hp; Native(g, CardKind.Duel, 2); Reach(g, () => Optional(g, Heji)); Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice }); g = Cold(g, r);
            if (scene == "kenshang")
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "kenshang-request-open"); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "kenshang-request-pick") == true);
                Need(P(g) is { IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null), "Kenshang chooses its actual variable HE materials privately inside the original Heji request.");
                Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card"); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card"); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
            }
            else
            {
                if (scene == "draw-zero") Need(V(g).HandCount == V(g).Hp && V(g).Hand.All(c => c.Suit == Suit.Heart), "The real original Duel payment leaves exactly the same-color hand/HP qualification required by the independent funded source.");
                Answer(g, c => scene == "round-zero" ? c.Parameters.GetValueOrDefault("kind") == "round-once" : c.Parameters.ContainsKey(scene == "draw-zero" ? "draw-funded-basic" : "chained-state-basic"));
                if (scene == "chain-zero")
                {
                    Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
                    var paid = E<ChainedStateBasicPaidEvent>(g).Single().Payment;
                    Need(!V(g).IsChained && paid is { WasChained: true, DesiredChained: false, EffectiveKind: CardKind.Slash } && g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.ChainedStateBasicUse?.Payment == paid && u.WuJingHeji is { Origin.Materials.Count: 0 }), "The real Slash-release cost changes the actor out of chain once and retains its exact paid native zero-entity use in a genuine committed-use child; this exit cost has no fabricated character-state child. " + JsonSerializer.Serialize(new { Paid = paid, V(g).IsChained, Frames = g.ResolutionStack.Select(f => new { f.Id, Kind = f.GetType().Name }) })); g = Cold(g, r); Continue(g);
                }
            }
            Play(g); var issued = E<WuJingHejiNativeIssuedEvent>(g).Single();
            Need(issued.TargetSeat == 2 && !issued.RewardEligible && issued.MaterialCount == (scene == "kenshang" ? 2 : 0) && V(g, 2).Hp == hp - 2 && E<WuJingHejiPaidEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && E<WuJingHejiReturnedEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && E<WuJingRewardIssuedEvent>(g).Length == 0, "The real Heji same-target need supports independently paid cardless, draw-funded, chained and variable-HE native Slash producers without range/quota borrowing or unconverted reward: " + scene);
            if (scene == "round-zero") Need(E<RoundOnceCardlessBasicQuotaPaidEvent>(g).Length == 1 && E<WuJingActualPhysicalSlashEvent>(g).Length == 0, "The zero-entity round quota pays once and does not consume Liubing's first actual physical Slash.");
            if (scene == "draw-zero") Need(E<DrawFundedDistinctBasicPaidEvent>(g).Single().ActualDrawCount == 1, "The native funded conversion really draws once rather than manufacturing a zero-entity card without its cost."); _ = Cold(g, r);
        }
        foreach (var scene in new[] { "round-zero-fire", "physical-fire" })
        {
            var (g, r) = Start(scene); var target = scene == "physical-fire" ? 1 : 2; var hp = V(g, target).Hp;
            Native(g, scene == "physical-fire" ? CardKind.Slash : CardKind.Duel, target);
            if (scene == "physical-fire") { Reach(g, () => Optional(g, Fire)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); }
            Reach(g, () => Optional(g, Heji)); Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice });
            if (scene == "round-zero-fire") Answer(g, c => c.Parameters.GetValueOrDefault("kind") == "round-once"); else Follow(g, CardKind.Slash);
            Reach(g, () => Optional(g, Fire)); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-slash-fire") == true);
            var issued = E<WuJingHejiNativeIssuedEvent>(g).Single(); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "convert"); g = Cold(g, r); Play(g);
            var changed = E<ProgramCurrentSlashFireChangedEvent>(g).Single();
            Need(changed.CardUseFrameId == issued.UseId && changed.ActionId == issued.ActionId && changed.OriginalKind == CardKind.Slash && changed.FinalKind == CardKind.FireSlash && issued.Kind == CardKind.Slash &&
                issued.MaterialCount == (scene == "physical-fire" ? 1 : 0) && issued.RewardEligible == (scene == "physical-fire") && V(g, target).Hp == hp - 2 &&
                E<WuJingHejiPaidEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && E<WuJingHejiReturnedEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && E<WuJingRewardIssuedEvent>(g).Length == 0,
                "A genuine committed child changes the same Heji Slash to FireSlash while its accepted kind, entity invoice, payment and return remain exact; the final converted use earns no original-card reward: " + scene);
            if (scene == "round-zero-fire") Need(E<RoundOnceCardlessBasicQuotaPaidEvent>(g).Length == 1 && E<RoundOnceCardlessBasicUseIssuedEvent>(g).Single().Receipt.EffectiveKind == CardKind.Slash && E<WuJingActualPhysicalSlashEvent>(g).Length == 0, "The real modified zero-entity Heji use preserves the original independent round payment and never becomes an entity Slash."); _ = Cold(g, r);
        }
    }
    public static void LiubingFirstPhysicalSlashActualTurnSuitAndFrozenAppearanceCold()
    {
        var (g, r) = Start("suit"); Need(V(g).Hand.All(c => c.Suit == Suit.Spade), "Unplayed physical Slashes retain their actual printed Spade suit.");
        Use(g, "incoming-duel", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); Answer(g, c => c.Cards.Count == 1); Play(g);
        Need(E<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 0) && E<WuJingActualPhysicalSlashEvent>(g).All(e => e.ActorSeat != 0), "A real Duel Slash is played, never counted as a genuine Use or spent as Liubing's first physical Slash.");
        var first = V(g).Hand.First().Id; Native(g, CardKind.Slash, 1, first); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.PhysicalCardIds is { } ids && ids.Contains(first));
        Need(use.Action is { EffectiveSuit: Suit.Diamond, EffectiveIsRed: true } && use.WuJingLiubing?.CardId == first && use.CardAttack?.AppearanceSuit == Suit.Diamond && V(g).Hand.All(c => c.Suit == Suit.Spade), "The first true physical Slash freezes Diamond consistently in action, attack and response appearance without recoloring any unplayed hand card.");
        g = Cold(g, r); Play(g); Need(E<WuJingLiubingAppearanceIssuedEvent>(g).Single().Receipt.CardId == first && E<WuJingActualPhysicalSlashEvent>(g).Single(e => e.ActorSeat == 0).Ordinal == 1, "The real first Use has one persistent actual-turn ordinal and one appearance invoice.");
        var second = V(g).Hand.First().Id; Native(g, CardKind.Slash, 1, second); Play(g); Need(E<CardActionAcceptedEvent>(g).Any(e => e.Action.Type == CardActionType.Use && e.Action.PhysicalCards.Any(c => c.CardId == second) && e.Action.EffectiveSuit == Suit.Spade && e.Action.EffectiveIsRed == false) && E<WuJingLiubingAppearanceIssuedEvent>(g).Length == 1, "The next physical Slash in the same actual turn remains black, even after the first use fully retires.");
        Use(g, "lose-liubing"); Play(g); var third = V(g).Hand.First().Id; Native(g, CardKind.Slash, 1, third); Play(g); Use(g, "gain-liubing"); Play(g); var fourth = V(g).Hand.First().Id; Native(g, CardKind.Slash, 1, fourth); Play(g);
        Need(E<WuJingActualPhysicalSlashEvent>(g).Where(e => e.ActorSeat == 0).Select(e => e.Ordinal).SequenceEqual([1, 2, 3, 4]) && E<WuJingLiubingAppearanceIssuedEvent>(g).Length == 1, "A genuine use while the skill is absent remains part of actual-turn history; reacquiring it cannot invent another first Slash.");
        var turn = g.State.TurnNumber; End(g); Reach(g, () => g.State.TurnNumber > turn && g.State.CurrentSeat == 0 && P(g)?.Kind == DecisionKind.PlayCard); Native(g, CardKind.Slash, 1); Play(g);
        Need(E<WuJingLiubingAppearanceIssuedEvent>(g).Length == 2 && E<WuJingLiubingAppearanceIssuedEvent>(g).Last().Receipt.ActualTurn > 1, "A later genuine actual turn resets the physical-use ordinal rather than the skill-instance grant."); _ = Cold(g, r);
    }
    public static void LiubingWholeUseNoDamageExactDiscardClaimsAndPrivacyCold()
    {
        foreach (var scene in new[] { "claim", "black", "damaged" })
        {
            var (g, r) = Start(scene); Use(g, "trim", targets: [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && P(g)!.Choices.All(c => c.Cards.Count == 0) && g.CreateSnapshot(1).PendingDecision is null, "The actual foreign hand-cost discard is selected through opaque slots, never exposed to its chooser or unrelated viewers.");
            g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0"); Play(g); Need(E<WuJingClaimIssuedEvent>(g).Length == 0, "A true discarded Slash cost cannot masquerade as a finished no-damage Use.");
            End(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var incoming = g.ResolutionStack.OfType<CardUseFrame>().Last(u => u.SourceSeat == 1 && u.CardKind == CardKind.Slash); var entity = incoming.PhysicalCardIds is [var material] ? material : throw new InvalidOperationException("The actual incoming physical Slash must own exactly one entity."); g = Cold(g, r);
            Answer(g, c => scene == "damaged" ? c.Cards.Count == 0 : c.Cards.Count == 1); Reach(g, () => g.State.CurrentSeat == 0 && P(g)?.Kind == DecisionKind.PlayCard);
            var claims = E<WuJingClaimIssuedEvent>(g).Where(e => e.UseId == incoming.Id).ToArray();
            if (scene == "claim") Need(claims is [{ Count: 1 }] && V(g).Hand.Any(c => c.Id == entity) && E<WuJingLiubingSettledEvent>(g).Single(e => e.UseId == incoming.Id) is { FinalActor: 1 }, "A genuinely dodged foreign red Slash in its actor's actual Play enters Discard and is claimed once as that exact original physical entity.");
            else Need(claims.Length == 0 && E<WuJingLiubingSettledEvent>(g).All(e => e.UseId != incoming.Id), "A black Slash or a nonblack Slash that really damaged a target cannot be claimed: " + scene); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("multi"); var pair = new[] { V(g).Hand.First(c => c.Suit == Suit.Spade).Id, V(g).Hand.First(c => c.Suit == Suit.Heart).Id };
            var claimant = Enumerable.Range(1, 3).Single(s => V(g, s).Skills!.Any(skill => skill.ContentId == Liubing));
            Use(g, "pair", cards: pair, targets: [3]); var live = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Action?.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(pair.Order()) == true); var action = live.Action!;
            Need(action.Type == CardActionType.Use && action.ActorSeat == 0 && action.EffectiveKind == CardKind.Slash && action.TargetSeats.SequenceEqual([3]) && action.PhysicalCards.Count == 2 && action.PhysicalCards.All(c => c.From == CardLocation.Hand(0)) && action.EffectiveIsRed is null && action.ConversionChain.Any(s => s.SkillId == Driver && s.BindingId == "pair") &&
                pair.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1), "The live native two-material Slash has paid its full canonical entity set once and freezes its genuine mixed-color conversion before any response finishes."); g = Cold(g, r); Play(g); var settled = E<WuJingLiubingSettledEvent>(g).Where(e => pair.Contains(e.CardId)).ToArray();
            var claims = E<WuJingClaimIssuedEvent>(g).Where(e => settled.Any(s => s.UseId == e.UseId)).ToArray();
            Need(settled.Length == 2 && settled.All(e => e.UseId == live.Id && e.ActionId == action.ActionId) && claims is [{ Count: 2 }] && pair.All(id => V(g, claimant).Hand.Any(c => c.Id == id)) && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == live.Id) == 1 && E<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 3 && e.EffectiveCardKind == CardKind.Dodge) && E<DamageAppliedEvent>(g).Length == 0,
                "A genuine two-material colorless Slash is nonblack and claims both still-available original materials together, without treating the separate Dodge payment as use material. " + JsonSerializer.Serialize(new { Pair = pair, Claimant = claimant, Settled = settled, Claims = claims, Action = action, Responses = E<CardRespondedEvent>(g), Damage = E<DamageAppliedEvent>(g), Movements = g.CardMovements.Where(m => pair.Contains(m.CardId)) })); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("extra"); Native(g, CardKind.Slash, 1); Reach(g, () => Optional(g, "ol:fengzi")); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost") == true); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "cost"); Play(g);
            Need(E<ExtraCardEffectStartedEvent>(g).Length == 1 && E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 1) && E<WuJingClaimIssuedEvent>(g).Length == 0, "A real repeated Slash effect stays one Use: its first prevented damage does not permit claiming the entity after the later effect actually damages a target."); _ = Cold(g, r);
        }
    }
    public static void HejiPaidLossDeathRandomRedAndNativeChildrenCold()
    {
        {
            var (g, r) = Start("draw-zero-loss"); Native(g, CardKind.Duel, 2); Reach(g, () => Optional(g, Heji)); Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice });
            Need(V(g).HandCount == V(g).Hp && V(g).Hand.All(c => c.Suit == Suit.Heart), "The actual same-color five-card hand matches five HP before accepting the funded Heji payment."); Answer(g, c => c.Parameters.ContainsKey("draw-funded-basic")); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            Need(E<DrawFundedDistinctBasicPaidEvent>(g).Single().ActualDrawCount == 1 && E<WuJingHejiNativeIssuedEvent>(g).Length == 0, "The original Heji need already owns its real paid Draw1 before the native movement child; no Slash has yet been declared.");
            g = Cold(g, r); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Play(g);
            var issued = E<WuJingHejiNativeIssuedEvent>(g).Single(); Need(issued.MaterialCount == 0 && E<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 && E<DrawFundedDistinctBasicReturnedEvent>(g).Length == 1 && E<WuJingHejiReturnedEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && V(g).Skills!.All(s => s.ContentId != Heji), "After genuine nested damage removes the Heji source, its already-paid native funded Slash still declares, resolves and returns once without drawing again."); _ = Cold(g, r);
        }
        foreach (var scene in new[] { "reward-loss", "reward-death" })
        {
            var (g, r) = Start(scene); Native(g, CardKind.Duel, 2); Reach(g, () => Optional(g, Heji)); Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice }); Follow(g, CardKind.Duel);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var paid = E<WuJingHejiNativeIssuedEvent>(g).Single(); var reward = E<WuJingRewardIssuedEvent>(g).Single();
            Need(reward.CardId is not null && E<WuJingHejiPaidEvent>(g).Count(e => e.UseId == paid.UseId) == 1 && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WuJing?.Stage == WuJingStage.RewardChildren), "The real red entity is already obtained once before its native CardsGained child pauses the issued reward.");
            g = Cold(g, r); Continue(g);
            if (scene == "reward-loss") { Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Play(g); Need(V(g).IsAlive && V(g).Skills!.All(s => s.ContentId != Heji), "The real nested damage/HP subtree removes the original conditional skill before its paid reward and native use return."); }
            else { Reach(g, () => g.ResolutionStack.OfType<DyingFrame>().Any() || E<WuJingHejiReturnedEvent>(g).Any(e => e.UseId == paid.UseId)); g = Cold(g, r); Reach(g, () => E<WuJingHejiReturnedEvent>(g).Any(e => e.UseId == paid.UseId)); Need(!V(g).IsAlive && g.State.Winner == Winner.None && E<DyingResolvedEvent>(g).Any(e => e.VictimSeat == 0 && !e.Survived), "A non-Lord reward owner's genuine death drains the paid native use without offering it another follow-up or fabricating recovery."); }
            Need(E<WuJingRewardIssuedEvent>(g).Count(e => e.UseId == paid.UseId) == 1 && E<WuJingHejiPaidEvent>(g).Count(e => e.UseId == paid.UseId) == 1 && E<WuJingHejiReturnedEvent>(g).Count(e => e.UseId == paid.UseId) == 1, "Source loss, dying and cold recovery never repeat native payment, seeded acquisition or whole-use return."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("lion"); Use(g, "equip", targets: [0]); Play(g); Use(g, "lose-one"); Play(g); Need(V(g).Equipment.Any(c => c.Kind == CardKind.SilverLion), "A real native equipped Lion is available for the HE conversion payment.");
            Native(g, CardKind.Duel, 2); Reach(g, () => Optional(g, Heji)); Activate(g); Reach(g, () => Current(g) is { Stage: WuJingStage.UseChoice }); var lion = V(g).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
            Answer(g, c => c.Cards.SequenceEqual([lion]) && c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.Slash)); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); g = Cold(g, r); Continue(g); Play(g);
            var issued = E<WuJingHejiNativeIssuedEvent>(g).Single(); Need(E<WuJingHejiMaterialIssuedEvent>(g).Single(e => e.UseId == issued.UseId).Material is { CardId: var id, PrintedKind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } && id == lion &&
                E<WuJingHejiPaidEvent>(g).Count(e => e.UseId == issued.UseId) == 1 && E<WuJingRewardIssuedEvent>(g).Length == 0 && V(g).Skills!.All(s => s.ContentId != Heji) && E<WuJingHejiReturnedEvent>(g).Count(e => e.UseId == issued.UseId) == 1,
                "A genuine equipped Lion-to-Slash conversion pays through native recovery, HP and movement children, loses its conditional source, resolves once and earns no unconverted reward."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int s = 0) => g.CreateSnapshot(s).Players.Single(p => p.Seat == s);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static WuJingReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.WuJing is not null)?.WuJing;
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Wu Jing actual command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g) ?? throw new InvalidOperationException("A Wu Jing answer requires a real current prompt: " + Diagnostic(g)); Need(V(g, p.PlayerSeat).IsHuman, "Only the real human may answer; AI uses AdvanceOneStep."); var selected = p.Choices.FirstOrDefault(pick); if (selected is null) throw new InvalidOperationException("No published Wu Jing choice matches the actual requested action: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, selected.Id, g.Revision)); }
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Follow(GameEngine g, CardKind kind) => Answer(g, c => c.Cards.Count > 0 && c.Parameters.GetValueOrDefault("kind") == kind.ToString());
    private static void Use(GameEngine g, string id, int[]? cards = null, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, CardKind kind, int target, int? id = null)
    {
        var a = g.GetHumanLegalActions().First(a => a.CardId is not null && (id is null || a.CardId == id) && a.TargetSeats.SequenceEqual([target]) && (a.PlayedCardKind ?? V(g).Hand.Single(c => c.Id == a.CardId).Kind) == kind);
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 500; i++)
        {
            if (done()) return; if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("Wu Jing completed before expected boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (Optional(g, Heji) || Optional(g, "ol:fengzi")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (Current(g) is { Stage: WuJingStage.UseChoice } && p?.SkillPrompt?.SkillId == Heji && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-jing")) Answer(g, c => c.Parameters.GetValueOrDefault("kind") == "decline");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p?.SkillPrompt?.SkillId is Watch or Health) Continue(g);
            else if (p?.SkillPrompt?.SkillId == "ol:kenshang" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "keep")) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "keep");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is not null) throw new InvalidOperationException("Unexpected Wu Jing prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Wu Jing missing actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, g.State.Winner, Prompt = P(g), Players = Enumerable.Range(0, 4).Select(s => { var p = V(g, s); return new { p.Seat, p.Role, p.Hp, p.HandCount, Skills = p.Skills?.Select(x => x.ContentId) }; }), Frames = g.ResolutionStack.TakeLast(6).Select(f => new { f.Id, Kind = f.GetType().Name }), Uses = E<CardUseDeclaredEvent>(g).TakeLast(6), ActualPhases = E<ActualDiscardRecoveryPhaseStartedEvent>(g).TakeLast(4), Facts = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold replay preserves all privacy views, actual-use appearance, original materials, genuine child returns and seeded red rewards exactly."); return restored; }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "reward-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu-jing", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:wu-jing"); if (scene == "extra") CharacterFixtureContent.Register(b, "ol:wang-rong"); if (scene == "kenshang") CharacterFixtureContent.Register(b, "ol:ma-xiu-ma-tie", "standard:demo-mashu");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"slash-room","query":"slashLimit","operation":"add","value":12,"priority":0},{"id":"keep-hand","query":"handLimit","operation":"add","value":20,"priority":0}],"viewAs":[{"id":"pair","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true}],"activations":[
                {"id":"pair","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"pair","outputKind":"slash"}]},
                {"id":"incoming-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"trim","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"lose-liubing","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Liubing}}"],"sourceBind":"standard:none"}]},
                {"id":"gain-liubing","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Liubing}}"]}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{Zero}}","revision":1,"viewAs":[{"id":"round-slash","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"roundOnceCardlessBasic":{"usageGroup":"wj-round","usesPerRound":1} },{"id":"funded-slash","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"wj-funded"} },{"id":"release-slash","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"chainedStateCost":false}]},
              {"id":"{{Convert}}","revision":1,"viewAs":[{"id":"duel","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true},{"id":"he-slash","inputKinds":["silverLion"],"inputSuits":[],"sourceZones":["hand","equipment"],"outputKind":"slash","forPlay":true,"forResponse":false}]},
              {"id":"{{Response}}","revision":1,"viewAs":[{"id":"real-dodge","inputKinds":["slash"],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-heji","skillId":"{{Heji}}","minimumHp":{{(scene=="draw-zero-loss"?5:8)}}}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-hp-child","window":"{{(scene=="lion"?"afterHpRecovered":"afterDamageApplied")}}","subject":"owner",{{(scene=="lion"?"":"\"damageOccurrence\":\"perDamage\",")}}"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-paid-child","window":"{{(scene=="lion"?"cardsMoved":"cardsGained")}}","subject":"owner",{{(scene=="lion"?"\"sourceZones\":[\"equipment\"],":"\"destinationZones\":[\"hand\"],")}}"movementOccurrence":"perBatch","movementReasons":["{{(scene=="lion"?CardMoveReasons.Use.Value:scene=="draw-zero-loss"?"skill-program.draw-funded-distinct-basic.draw":"skill-program.wu-jing.heji.red")}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{{(scene=="lion"?"{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}":"{\"op\":\"damage\",\"target\":\"owner\",\"amount\":"+(scene=="reward-death"?20:1)+"}, {\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}")}}]}]},
              {"id":"{{Prevent}}","revision":1,"triggers":[{"id":"prevent-first","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]},
              {"id":"{{Fire}}","revision":1,"triggers":[{"id":"actual-fire","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":true,"effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}
            ]}
            """)!;
            if (scene == "chain-zero") rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Watch)!["triggers"] = JsonNode.Parse("""[{"id":"real-paid-zero-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]""");
            var ids = new[] { Driver, Convert, Response, Quiet, Conditional, Watch, Health, Prevent, Zero, Fire }; var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = ids.ToDictionary(id => id, id => new { name = id, description = "吴景真实用牌与支付边界", optionLabels = id is Watch or Health ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实边界") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var human = scene == "reward-death" ? Role.Renegade : Role.Lord; b.AddSkill(new("fixture:wj-pick-owner", "本人评分", "真实选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == human ? 100000d : -100000d) });
            var own = new List<string> { Driver, Liubing }; if (scene is "lion" or "reward-loss" or "reward-death" or "draw-zero-loss") own.AddRange([Conditional, Watch, Health]); else if (scene is not ("foreign" or "multi" or "extra")) own.Add(Heji);
            if (scene.Contains("zero", StringComparison.Ordinal)) own.Add(Zero); if (scene == "chain-zero") own.Add(Watch); if (scene == "kenshang") own.Add("ol:kenshang");
            if (scene.EndsWith("-fire", StringComparison.Ordinal)) own.Add(Fire);
            if (scene is "conversion" or "lion") own.Add(Convert); if (scene is "claim" or "black" or "damaged") own.Add(Response); if (scene == "extra") own.Add("ol:fengzi");
            b.AddGeneral(new(Owner, "吴景真实本人", "ol-wu-jing", "fixture:wj-pick-owner", "wu", scene is "draw-zero" or "draw-zero-loss" ? 4 : 8, own, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:wj-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:wj-pick-" + index; b.AddSkill(new(pick, "其他评分", "固定原生顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == human ? -100000d : 100003d - index) }); var skills = new List<string>();
                if (scene is not ("claim" or "black" or "damaged")) skills.Add(Quiet);
                if (scene == "foreign" && index == 0) skills.Add(Heji); if (scene is "multi" or "extra" && index == 0) skills.Add(Liubing); if (scene == "multi") skills.Add(Response); if (scene == "extra") skills.Add(Prevent);
                b.AddGeneral(new(id, "其他真实角色", "supporter", pick, "wu", 8, skills, GeneralGender.Male));
            }
            b.AddCard(new("fixture:wj-lion", "白银狮子", "装备牌", "真实支付及回复", CardKind.SilverLion));
            b.AddDeck(new("fixture:wj-deck", "固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(scene == "lion" ? "fixture:wj-lion" : scene is "recursive" or "foreign" or "reward-loss" or "reward-death" or "kenshang" || scene.Contains("zero", StringComparison.Ordinal) ? "standard:duel" : "standard:slash", scene is "recursive" or "reward-loss" or "reward-death" or "claim" or "damaged" or "extra" or "kenshang" or "physical-fire" || scene.Contains("zero", StringComparison.Ordinal) ? Suit.Heart : scene == "multi" && i % 2 == 0 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "吴景原生完整规则", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [(scene is "claim" or "black" or "damaged" ? nameof(Role.Rebel) : nameof(Role.Renegade))] = 3 }, "fixture:wj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
