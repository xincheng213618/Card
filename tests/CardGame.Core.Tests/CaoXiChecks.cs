using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoXiChecks
{
    private const string Gang = "ol:gangshu", Jian = "ol:jianxuan", Owner = "fixture:cx-owner", Driver = "fixture:cx-driver",
        Quiet = "fixture:cx-quiet", Response = "fixture:cx-response", Source = "fixture:cx-source", Phase = "fixture:cx-phase",
        Watch = "fixture:cx-watch", Health = "fixture:cx-health", Mode = "identity:classic-cao-xi-fixture";
    public static void NonbasicValuesCapWeaponsAndNativeSlashQuotaCold()
    {
        var (g, registry) = Start();
        foreach (var (query, times) in new[] { (SkillRuleQuery.AttackRange, 4), (SkillRuleQuery.DrawCount, 3), (SkillRuleQuery.SlashLimit, 4) })
            for (var i = 0; i < times; i++) Grow(ref g, registry, query);
        Need(E<CaoXiGangshuIncreasedEvent>(g).Length == 11 && E<CaoXiGangshuIncreasedEvent>(g).All(e => e.AfterValue == e.BeforeValue + 1 && e.AfterValue <= 5) && g.GetAttackRange(0) == 5,
            "Eleven genuine completed nonbasic uses independently grow 1/2/1 to 5/5/5 without changing any other numerical source.");
        var starts = E<CaoXiGangshuStartedEvent>(g).Length; Use(g, "study"); Play(g);
        Need(E<CaoXiGangshuStartedEvent>(g).Length == starts, "All three capped numbers suppress the optional trigger rather than offering an illegal sixth point.");
        for (var i = 0; i < 5; i++)
        {
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind)); Play(g);
        }
        Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash) && E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash) == 5,
            "The actual native phase quota permits exactly five physical Slashes, rather than only displaying an increased number.");
        _ = Cold(g, registry);
        foreach (var scenario in new[] { "bow", "crossbow" })
        {
            (g, registry) = Start(scenario); Use(g, "equip", [0]); Activate(g, Gang); Reach(g, () => GangChoice(g));
            Need(P(g)!.Choices.All(c => c.Parameters.GetValueOrDefault("query") != (scenario == "bow" ? "AttackRange" : "SlashLimit")) &&
                (scenario != "bow" || g.GetAttackRange(0) == 9), "Real equipment and a separate native range modifier remain authoritative; a value above five or infinite cannot be incremented.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("query") == "DrawCount"); Play(g); _ = Cold(g, registry);
        }
    }
    public static void ActualOffsetsDodgeCounterspellAndNonOffsetResponsesCold()
    {
        foreach (var scenario in new[] { "plain", "dodge", "uncancelable" })
        {
            var (g, registry) = Start(scenario);
            foreach (var q in new[] { SkillRuleQuery.AttackRange, SkillRuleQuery.DrawCount, SkillRuleQuery.SlashLimit }) Grow(ref g, registry, q);
            Use(g, scenario == "plain" ? "duel" : "incoming", [1]);
            if (scenario != "plain")
            { Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0") == true); Answer(g, c => c.Targets.SequenceEqual([0])); }
            if (scenario == "uncancelable")
            {
                Activate(g, "ol:choutao"); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "yan-rou") == true);
                Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand");
            }
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge });
            var id = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0]; g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([id]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response);
            Need(!E<CaoXiGangshuActualOffsetEvent>(g).Any() && g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing),
                "An accepted real response cost is not itself an offset; the old values still exist inside its native ResponseAccepted child.");
            g = Cold(g, registry); Continue(g); Play(g);
            Need(E<CaoXiGangshuActualOffsetEvent>(g).Length == (scenario == "dodge" ? 1 : 0) && E<CaoXiGangshuUnitExpiredEvent>(g).Length == (scenario == "dodge" ? 3 : 0),
                "Only a Slash actually cancelled by the completed Dodge clears all three units; Duel Slash responses and Choutao's genuine uncancelable Slash do not.");
            if (scenario == "uncancelable") Need(E<YanRouChoutaoAppliedEvent>(g).Any() && E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0), "Real Choutao allows the actual Dodge payment and still deals the uncancelable Slash's damage.");
            _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start("counterspell");
            foreach (var q in new[] { SkillRuleQuery.AttackRange, SkillRuleQuery.DrawCount, SkillRuleQuery.SlashLimit }) Grow(ref g, registry, q, "equip");
            Use(g, "study"); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            Answer(g, c => c.Cards.Count == 1); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response);
            Need(E<CaoXiGangshuActualOffsetEvent>(g).Length == 1 && E<CaoXiGangshuUnitExpiredEvent>(g).Length == 3,
                "An ordinary counterspell clears the old values at its true native toggle, before any real ResponseAccepted child can query them.");
            g = Cold(g, registry); Continue(g); Activate(g, Gang); Reach(g, () => GangChoice(g));
            Need(E<CaoXiGangshuActualOffsetEvent>(g).Length == 1 && E<CaoXiGangshuUnitExpiredEvent>(g).Length == 3,
                "A true Nullification toggle clears old values before the completed nonbasic response-use offers a new increment.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("query") == "DrawCount");
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            Answer(g, c => c.Cards.Count == 1); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response); g = Cold(g, registry); Continue(g); Activate(g, Gang); Reach(g, () => GangChoice(g));
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("query") == "AttackRange"); Play(g);
            Need(E<NullificationRespondedEvent>(g).Count(e => e.ResponderSeat == 0) == 2 && E<CaoXiGangshuActualOffsetEvent>(g).Length == 2 &&
                E<CaoXiGangshuUnitExpiredEvent>(g).Length == 4 && g.GetAttackRange(0) == 2,
                "A real counter-Nullification clears the preceding new draw unit, then its own completed use grants a fresh range unit which survives the chain.");
            _ = Cold(g, registry);
        }
        foreach (var remove in new[] { true, false })
        {
            var (g, registry) = Start("ambush");
            for (var i = 0; i < 5; i++) { Use(g, "equip", [1]); Play(g); }
            var original = E<CaoXiGangshuIncreasedEvent>(g).Where(e => e.Source.OwnerSeat == 1).Select(e => e.FrameId).ToArray();
            Need(original.Length == 5, "The real AI grows its own five attributable units through native equipment uses and private numerical choices.");
            End(g); Activate(g, "ol:shefu"); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "card") == true);
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card"); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("recorded-kind") == "Nullification") == true);
            Answer(g, c => c.Parameters.GetValueOrDefault("recorded-kind") == "Nullification");
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            original = original.Where(id => !E<CaoXiGangshuUnitExpiredEvent>(g).Any(e => e.GrantFrameId == id)).ToArray();
            Need(original.Length == 2, "The AI's real Draw consumed only its three draw units, leaving its two until-offset Slash units for the chain test.");
            Answer(g, c => c.Cards.Count == 1);
            Activate(g, "ol:shefu"); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "remove") == true);
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == (remove ? "remove" : "decline"));
            Reach(g, () => P(g) is { PlayerSeat: 1 } && GangChoice(g));
            Need(E<NullificationRespondedEvent>(g).Any(e => e.ResponderSeat == 1) && E<CaoXiGangshuUnitExpiredEvent>(g).Count(e => original.Contains(e.GrantFrameId)) == (remove ? 0 : 2) &&
                E<CaoXiGangshuActualOffsetEvent>(g).Count(e => e.OwnerSeat == 1) == (remove ? 0 : 1),
                "A real ambushed AI counterspell reports its payment but clears nothing; declining the exact deposit instead toggles and clears old units before that response's new Gangshu choice.");
            var offered = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OwnerSeat == 1 && f.CaoXiGangshu is not null).Id;
            g = Cold(g, registry); Accept(g, new AdvanceOneStepCommand(g.Revision));
            Reach(g, () => E<CaoXiGangshuIncreasedEvent>(g).Any(e => e.FrameId == offered));
            var fresh = E<CaoXiGangshuIncreasedEvent>(g).Single(e => e.FrameId == offered);
            Need(!E<CaoXiGangshuUnitExpiredEvent>(g).Any(e => e.GrantFrameId == fresh.FrameId), "The completed real response-use issues a fresh unit after the deferred actual-toggle stage, so that toggle cannot erase its new grant.");
            _ = Cold(g, registry);
        }
    }
    public static void ActualDrawSkipsAndDuringPhaseFutureGrantsCold()
    {
        foreach (var scenario in new[] { "plain", "skip", "during-draw", "guess", "nested-draw" })
        {
            var (g, registry) = Start(scenario); Grow(ref g, registry, SkillRuleQuery.DrawCount); Grow(ref g, registry, SkillRuleQuery.AttackRange);
            var first = E<CaoXiGangshuIncreasedEvent>(g).Single(e => e.Query == SkillRuleQuery.DrawCount).FrameId;
            if (scenario != "plain") { Use(g, "arm"); Play(g); }
            if (scenario == "nested-draw") Trim(g, 0, 0);
            var turn = g.State.TurnNumber; End(g);
            if (scenario == "nested-draw")
            {
                Reach(g, () => Optional(g, "ol:hongji") && E<TurnStartedEvent>(g).Last().ActorSeat == 0);
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw") == true); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw");
                Reach(g, () => Optional(g, Phase)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                Reach(g, () => Optional(g, Phase)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            }
            if (scenario is "during-draw" or "nested-draw")
            {
                Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")); Answer(g, c => c.Targets.SequenceEqual([0]));
                Activate(g, Gang); Reach(g, () => GangChoice(g)); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("query") == "DrawCount");
            }
            if (scenario == "guess")
            {
                Reach(g, () => Optional(g, "ol:jizhan")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "extra-effects-rank-guess") == true);
                Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "greater");
                Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "stop") == true || P(g) is { Kind: DecisionKind.PlayCard });
                if (P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "stop") == true) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "stop");
            }
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > turn);
            if (scenario == "skip")
            {
                Need(!E<CaoXiGangshuUnitExpiredEvent>(g).Any(e => e.GrantFrameId == first) && !E<CaoXiGangshuDrawUnitIssuedEvent>(g).Any(),
                    "A genuinely skipped Draw never freezes or consumes a next-Draw unit.");
                End(g); Reach(g, () => E<CaoXiGangshuUnitExpiredEvent>(g).Any(e => e.GrantFrameId == first)); Play(g);
            }
            Need(E<CaoXiGangshuUnitExpiredEvent>(g).Count(e => e.GrantFrameId == first) == 1 && E<CaoXiGangshuDrawUnitIssuedEvent>(g).Count(e => e.GrantFrameId == first) == 1 && g.GetAttackRange(0) == 2,
                "The next actual Draw consumes its frozen draw unit once, including a replaced early-frozen draw, while retaining the independent range unit.");
            var drawPhase = E<CaoXiGangshuDrawUnitIssuedEvent>(g).Single(e => e.GrantFrameId == first).Phase;
            Need(E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.OwnerSeat == 0 && e.ActualTurnNumber == drawPhase.TurnNumber && !e.Skipped && e.DrawCount == (scenario == "guess" ? 0 : 3)),
                "The genuine next Draw uses the composed count three, while the real rank-guess replacement completes with zero normal draws; a during-phase new unit never inflates this frozen count.");
            if (scenario is "during-draw" or "nested-draw")
            {
                var future = E<CaoXiGangshuIncreasedEvent>(g).Last(e => e.Query == SkillRuleQuery.DrawCount);
                Need(future.GrantedInDrawPhaseToken is not null && !E<CaoXiGangshuUnitExpiredEvent>(g).Any(e => e.GrantFrameId == future.FrameId) &&
                    E<CaoXiGangshuDrawUnitIssuedEvent>(g).All(e => e.GrantFrameId != future.FrameId), "A new increment gained inside DrawPhaseStarting stays reserved for a later actual Draw even before the current base freezes.");
                if (scenario == "nested-draw") Need(E<ZhangShiPingHongjiReturnedEvent>(g).Length == 1 && future.GrantedAfterActualPhaseToken > drawPhase.Token &&
                    E<ActualDrawPhaseCompletedEvent>(g).Where(e => e.OwnerSeat == 0 && e.ActualTurnNumber == drawPhase.TurnNumber).Select(e => e.DrawCount).Order().SequenceEqual([2, 3]),
                    "An actual Hongji nested Draw cannot reuse its outer phase's already reserved unit, and its later grant stays future when the older Draw parent resumes and completes.");
                End(g); Reach(g, () => E<CaoXiGangshuUnitExpiredEvent>(g).Any(e => e.GrantFrameId == future.FrameId)); Play(g);
            }
            _ = Cold(g, registry);
        }
    }
    public static void RepeatedDrawActualDamagePaidChildrenLossDeathCold()
    {
        {
            var (g, registry) = Start(); Trim(g, 0, 0); Use(g, "hurt-two", [1]); Activate(g, Jian); Reach(g, () => RecipientChoice(g));
            g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
            Need(E<CaoXiJianxuanStartedEvent>(g).Length == 1 && E<CaoXiJianxuanCompletedEvent>(g) is [{ RecipientSeat: 0, DrawCalls: 3 }] && V(g, 0).HandCount == 3,
                "One actual two-point damage instance opens one optional Jianxuan; a chosen empty hand repeatedly reaches recorded 1, then 2, and stops at 3."); _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start();
            foreach (var (q, n) in new[] { (SkillRuleQuery.AttackRange, 2), (SkillRuleQuery.DrawCount, 2), (SkillRuleQuery.SlashLimit, 4) }) for (var i = 0; i < n; i++) Grow(ref g, registry, q);
            Trim(g, 1, 2); Use(g, "hurt-two", [1]); Activate(g, Jian); Reach(g, () => RecipientChoice(g)); Answer(g, c => c.Targets.SequenceEqual([1])); Play(g);
            Need(E<CaoXiJianxuanCompletedEvent>(g) is [{ RecipientSeat: 1, DrawCalls: 4 }] && V(g, 1).HandCount == 6 &&
                E<CaoXiJianxuanComparedEvent>(g).Select(e => e.HandCount).SequenceEqual([3, 4, 5, 6]) && E<CaoXiJianxuanDrawPaidEvent>(g).All(e => e.ActualDrawCount == 1),
                "The single frozen other recipient actually draws four separate native entities through recorded 3/4/5; equality is evaluated after each payment."); _ = Cold(g, registry);
        }
        foreach (var scenario in new[] { "paid-loss", "source-death", "recipient-death" })
        {
            var (g, registry) = Start(scenario); var recipient = Enumerable.Range(1, 3).First(s => V(g, s).Role != Role.Lord); Use(g, "wound-peer", [recipient]); Play(g); Trim(g, recipient, 0);
            Use(g, "hurt-one", [recipient]); Activate(g, Jian); Reach(g, () => RecipientChoice(g)); Answer(g, c => c.Targets.SequenceEqual([recipient]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.CaoXiJianxuan is not null);
            Need(frame.CaoXiJianxuan is { Stage: CaoXiJianxuanStage.DrawChildren, Cursor: 1, ActualDrawCount: 1 } && E<CaoXiJianxuanDrawPaidEvent>(g).Length == 1,
                "The first real draw is frozen on its owning frame before the genuine CardsGained child.");
            g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
            Need(E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == recipient && e.Amount == 1), "The paid native draw child performs real recovery and suspends its HP observer before proceeding to descendant damage.");
            g = Cold(g, registry); Continue(g); Reach(g, () => Option(g, "after-child") || !V(g, recipient).IsAlive || !V(g, 0).IsAlive);
            Need(scenario == "paid-loss" ? V(g, 0).Skills!.All(s => s.ContentId != Jian) : scenario == "source-death" ? !V(g, 0).IsAlive : !V(g, recipient).IsAlive,
                "The actual movement child removes only the conditional source, kills the source, or kills the frozen beneficiary as arranged.");
            g = Cold(g, registry); if (Option(g, "after-child")) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-child"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
            Reach(g, () => E<CaoXiJianxuanCompletedEvent>(g).Length == 1); var complete = E<CaoXiJianxuanCompletedEvent>(g).Single();
            Need(complete.DrawCalls == (scenario == "recipient-death" ? 1 : 3) && E<CaoXiJianxuanDrawPaidEvent>(g).Length == complete.DrawCalls,
                "A paid receipt survives true source loss or death and resumes the same beneficiary without repayment; a dead beneficiary stops the recursion immediately."); _ = Cold(g, registry);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool GangChoice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "cao-xi" && c.Parameters.ContainsKey("query")) == true;
    private static bool RecipientChoice(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "cao-xi" && c.Parameters.ContainsKey("recipient")) == true;
    private static bool Option(GameEngine g, string option) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option) == true;
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Cao Xi command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Never answer another player's AI prompt."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Activate(GameEngine g, string skill) { Reach(g, () => Optional(g, skill)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null)
    {
        if (id == "study")
        {
            var a = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.DrawTwo);
            Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        else Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Grow(ref GameEngine g, ContentRegistry r, SkillRuleQuery query, string activation = "study")
    {
        var current = g; Use(current, activation, activation == "equip" ? [0] : []); Activate(current, Gang); Reach(current, () => GangChoice(current));
        Need(P(current) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => current.CreateSnapshot(s).PendingDecision is null), "Only the source sees its frozen private numerical decision.");
        var view = State(g); var p = P(g)!; Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && view == State(g), "A foreign seat cannot answer or mutate the private frozen choice.");
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("query") == query.ToString()); Play(g);
    }
    private static void Trim(GameEngine g, int seat, int count)
    {
        if (seat == 0) { var cards = V(g, 0).Hand.Skip(count).Select(c => c.Id).ToArray(); if (cards.Length > 0) { Use(g, "trim", cards: cards); Play(g); } }
        else while (V(g, seat).HandCount > count)
        { Use(g, "trim-peer", [seat]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Play(g); }
        Need(V(g, seat).HandCount == count, "Only native card movement establishes the exact initial hand size.");
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 440; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Cao Xi ended before its real boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (Option(g, "continue")) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected Cao Xi private prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Cao Xi actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner,
        Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => c.Parameters) } : null,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, V(g, s).GeneralId, V(g, s).HandCount }),
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }),
        Counters = E<NullificationRespondedEvent>(g), Offsets = E<CaoXiGangshuActualOffsetEvent>(g), Tail = g.Events.TakeLast(7).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves exact private numerical choices, native offset stage, per-unit Draw reservation and the already paid recursive movement cursor."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scenario is "source-death" or "recipient-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, AiPolicyVersion = scenario == "ambush" ? 2 : 1, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cao-xi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cao-xi"); if (scenario == "uncancelable") CharacterFixtureContent.Register(b, "ol:yan-rou");
            if (scenario == "ambush") CharacterFixtureContent.Register(b, "ol:cheng-yu");
            if (scenario == "guess") CharacterFixtureContent.Register(b, "ol:wang-rong");
            if (scenario == "nested-draw") CharacterFixtureContent.Register(b, "ol:zhang-shi-ping");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":70,"priority":0}],"viewAs":[{"id":"study","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"activations":[
              {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
              {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
              {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
              {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
              {"id":"arm","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
              {"id":"trim","minCards":1,"maxCards":60,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","sourceZones":["hand"],"usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Response}}}","revision":1,"triggers":[{"id":"actual-response","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","dodge","nullification"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"response","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "实际命令准备", description = "只用真实原生卡牌、伤害和移动建立边界" }, [Quiet] = new { name = "实际安静回合", description = "跳过其他角色出牌" },
                [Response] = new { name = "真实响应已支付", description = "在原生响应子帧暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body, Dictionary<string, string>? options = null)
            { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); labels[id] = new { name = id, description = "真实阶段和支付子树", optionLabels = options ?? new() }; }
            var extras = new List<string> { Driver, Gang, Jian, Response };
            if (scenario == "nested-draw") extras.Add("ol:hongji");
            if (scenario == "ambush")
            {
                extras.Add("ol:shefu");
                Add(Phase, "\"triggers\":[{\"id\":\"real-ai-trick\",\"window\":\"playEnding\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"drawTwo\"}]}]");
                Add(Source, "\"triggers\":[{\"id\":\"empty-other-ai-opening\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]");
            }
            if (scenario == "counterspell") Add(Phase, "\"triggers\":[{\"id\":\"empty-real-foreign-hands\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}]}]");
            if (scenario == "bow") { Add(Source, "\"modifiers\":[{\"id\":\"independent-range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":4,\"priority\":0}]"); extras.Add(Source); }
            if (scenario is "skip" or "during-draw" or "guess" or "nested-draw")
            {
                Add(Source, "\"hpSkillGrants\":[{\"id\":\"exact-twelve\",\"skillId\":\"" + (scenario == "guess" ? "ol:jizhan" : Phase) + "\",\"minimumHp\":12,\"maximumHp\":12}]"); extras.Add(Source);
                if (scenario != "guess") Add(Phase, "\"triggers\":[{\"id\":\"actual-phase\",\"window\":\"" + (scenario == "skip" ? "turnStartBeforeNormalFlow" : "drawPhaseStarting") + "\",\"subject\":\"owner\",\"optional\":" + (scenario == "nested-draw" ? "true" : "false") + ",\"effects\":[" +
                    (scenario == "skip" ? "{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"draw\"]}" : "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"gear\"}") + ",{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]}]");
            }
            if (scenario == "uncancelable") extras.Add("ol:choutao");
            if (scenario is "paid-loss" or "source-death" or "recipient-death")
            {
                if (scenario == "paid-loss") { extras.Remove(Jian); extras.Add(Source); Add(Source, "\"hpSkillGrants\":[{\"id\":\"healthy-jianxuan\",\"skillId\":\"" + Jian + "\",\"minimumHp\":12}]"); }
                var child = scenario == "recipient-death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":" + (scenario == "source-death" ? "20" : "1") + "}";
                Add(Watch, "\"triggers\":[{\"id\":\"paid-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.cao-xi.jianxuan.draw\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}," + child + ",{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"after\",\"options\":[{\"id\":\"after-child\"}]}]}]", new() { ["continue"] = "继续", ["after-child"] = "继续已付谏旋" });
                Add(Health, "\"triggers\":[{\"id\":\"real-recovery-child\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}]}]", new() { ["continue"] = "继续" });
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:cx-owner-pick", "本人公开评分", "固定选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(Owner, "曹羲本人", "ol-cao-xi", "fixture:cx-owner-pick", "wei", scenario is "source-death" or "recipient-death" ? 13 : 12, extras));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:cx-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            { var pick = "fixture:cx-pick-" + index; b.AddSkill(new(pick, "公开选将次序", "确定性其他角色") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (3 - index)) });
                b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 14, scenario is "paid-loss" or "source-death" or "recipient-death" ? [Quiet, Watch, Health] : scenario == "counterspell" ? [Quiet, Phase] : scenario == "ambush" ? index == 0 ? [Phase, Gang] : [Quiet, Source] : [Quiet])); }
            var equipment = scenario == "crossbow" ? CardKind.Crossbow : scenario == "bow" ? CardKind.QilinBow : CardKind.SilverLion;
            b.AddCard(new("fixture:cx-gear", "实际装备", "装备牌", "原生装备槽及能力", equipment));
            b.AddDeck(new("fixture:cx-deck", "小固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(
                scenario is "bow" or "crossbow" ? "fixture:cx-gear" : scenario is "counterspell" or "ambush" ? i % 5 == 0 ? "fixture:cx-gear" : "standard:nullification" : i % 10 == 0 ? "fixture:cx-gear" : scenario is "dodge" or "uncancelable" ? "standard:dodge" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "曹羲实际原生边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:cx-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
