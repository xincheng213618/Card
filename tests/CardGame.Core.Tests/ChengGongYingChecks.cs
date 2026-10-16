using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChengGongYingChecks
{
    private const string Skill = "ol:kuangxiang", Hongji = "ol:hongji", Owner = "fixture:cgy-owner", Driver = "fixture:cgy-driver", Quiet = "fixture:cgy-quiet", Watch = "fixture:cgy-watch", Health = "fixture:cgy-health", Conditional = "fixture:cgy-conditional", Phase = "fixture:cgy-phase", Prime = "fixture:cgy-prime", Boundary = "fixture:cgy-boundary", Mode = "identity:classic-cheng-gong-ying-fixture", Legacy = "identity:cheng-gong-ying-legacy-fixture";
    private const string DrawReason = "skill-program.cheng-gong-ying.kuangxiang.draw", DiscardReason = "skill-program.cheng-gong-ying.kuangxiang.discard";
    public static void PreparationAdjustmentsPrivateDiscardExactGainAndCold()
    {
        {
            var (g, r) = Start("black"); Need(V(g).HandCount == 2 && V(g).Hp == 9 && V(g).MaxHp == 9, "A genuine initial hand and native Lord HP are present before optional Preparation.");
            Activate(g); Need(Current(g) is { Stage: ChengGongYingStage.TargetChoice } && P(g)!.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([0, 1, 2, 3]), "The actual optional Preparation can select any living character, including itself.");
            g = Cold(g, r); Target(g, 0); Play(g);
            var draw = E<KuangxiangAdjustmentIssuedEvent>(g).Single(); var gained = E<KuangxiangCardObtainedEvent>(g);
            Need(draw is { Draw: true, Recipient: 0, ActualCount: 2 } && gained.Length == 2 && gained.All(e => e.EffectiveSuit == Suit.Spade && g.CardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == DrawReason)) && V(g).HandCount == 6,
                "Only the two real adjustment draws receive entity invoices; the later normal Draw2 remains independent."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("discard"); Activate(g); Target(g, 1); Reach(g, () => Current(g) is { Stage: ChengGongYingStage.DiscardChoice });
            var prompt = P(g)!; Need(prompt.PlayerSeat == 1 && prompt.IsPrivate && g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(2).PendingDecision is null, "The foreign target chooses its own hand discards privately; the source cannot inspect its hand selection.");
            var before = State(g); Need(!g.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before, "The source cannot answer a private foreign discard choice.");
            g = Cold(g, r); Play(g); Need(V(g, 1).HandCount == 4 && E<KuangxiangAdjustmentIssuedEvent>(g).Single() is { Draw: false, ActualCount: 2 } && g.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == DiscardReason) == 2 && E<KuangxiangCardObtainedEvent>(g).Length == 0,
                "The native target really discards its own excess hand cards simultaneously, without inventing obtained marks or touching equipment."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("equal"); Activate(g); Target(g, 0); Play(g);
            Need(E<KuangxiangAdjustmentIssuedEvent>(g).Single() is { Draw: false, ActualCount: 0 } && E<KuangxiangCardObtainedEvent>(g).Length == 0 && V(g).HandCount == 6, "Choosing an existing four-card hand is legal, with zero physical adjustment and normal Draw2 afterwards."); _ = Cold(g, r);
            (g, r) = Start("black"); Skip(g); Play(g); Need(E<ChengGongYingStartedEvent>(g).Length == 0 && V(g).HandCount == 4, "The current-client optional wording permits a genuine decline without issuing any adjustment receipt."); _ = Cold(g, r);
        }
    }
    public static void BlackPhysicalUsesConversionsMultiTargetsChainsAndLightningCold()
    {
        foreach (var scene in new[] { "black", "duel", "aoe", "conversion" })
        {
            var (g, r) = Start(scene); Activate(g); Target(g, 0); Play(g); var mark = E<KuangxiangCardObtainedEvent>(g).First(); var hp = Enumerable.Range(0, 4).Select(s => V(g, s).Hp).ToArray();
            var kind = scene == "duel" ? CardKind.Duel : scene == "aoe" ? CardKind.ArrowBarrage : CardKind.Slash;
            g = Cold(g, r); Native(g, mark.CardId, kind, scene == "aoe" ? null : [1]); Play(g);
            var bound = E<KuangxiangBlackUseBoundEvent>(g).Single(e => e.CardId == mark.CardId);
            Need(bound.GrantFrameId == mark.FrameId && bound.ObtainedSequence == mark.MovementSequence && bound.EffectiveKind == kind && (scene == "aoe" ? Enumerable.Range(1, 3).All(s => V(g, s).Hp == hp[s] - 2) : V(g, 1).Hp == hp[1] - 2), "A genuinely obtained black physical card applies exactly +1 to every native card recipient, including a one-entity effective conversion: " + scene + " " + Diagnostic(g));
            if (scene == "black")
            {
                var plain = V(g).Hand.First(c => !E<KuangxiangCardObtainedEvent>(g).Any(e => e.CardId == c.Id)); var before = V(g, 1).Hp;
                Native(g, plain.Id, CardKind.Slash, [1]); Play(g); Need(V(g, 1).Hp == before - 1 && !E<KuangxiangBlackUseBoundEvent>(g).Any(e => e.CardId == plain.Id), "An ordinary unmarked card cannot borrow a different physical card's grant.");
                Use(g, "damage", targets: [1]); Play(g); Need(E<DamageAppliedEvent>(g).Last().Amount == 1, "Cardless skill damage never borrows a black-card invoice.");
                var otherMarked = E<KuangxiangCardObtainedEvent>(g).Single(e => e.CardId != mark.CardId).CardId; var otherPlain = V(g).Hand.First(c => c.Id != otherMarked).Id; before = V(g, 1).Hp;
                Use(g, "pair", cards: [otherMarked, otherPlain], targets: [1]); Play(g);
                Need(V(g, 1).Hp == before - 1 && !E<KuangxiangBlackUseBoundEvent>(g).Any(e => e.CardId == otherMarked), "A genuine two-material virtual Slash cannot impersonate the one obtained physical card's own use.");
            }
            _ = Cold(g, r);
        }
        foreach (var scene in new[] { "chain-formal", "chain-legacy" })
        {
            var (g, r) = Start(scene); Activate(g); Target(g, 0); Play(g); foreach (var seat in new[] { 1, 2, 3 }) { Use(g, "chain", targets: [seat]); Play(g); }
            var hp = Enumerable.Range(0, 4).Select(s => V(g, s).Hp).ToArray(); var marked = E<KuangxiangCardObtainedEvent>(g).First().CardId; Native(g, marked, CardKind.ThunderSlash, [1]);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Play(g);
            Need(Enumerable.Range(1, 3).All(s => V(g, s).Hp == hp[s] - 2) && E<DamageAppliedEvent>(g).Where(e => e.Nature == DamageNature.Thunder).Select(e => e.Amount).SequenceEqual([2, 2, 2]), "The first black elemental card damage establishes the native chain base; later recipients inherit +1 exactly once under both armor paths: " + Diagnostic(g)); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("lightning"); Activate(g); Target(g, 0); Play(g); var marked = E<KuangxiangCardObtainedEvent>(g).First().CardId; Native(g, marked, CardKind.Lightning); Play(g); var before = V(g).Hp;
            End(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); Need(g.ResolutionStack.OfType<JudgmentFrame>().Any(j => j.DelayedCardId == marked && j.KuangxiangBlack is not null), "The actual delayed judgment owns its exact invoiced held Lightning rather than the separate revealed judgment card.");
            g = Cold(g, r); Continue(g); Play(g); var applied = E<DamageAppliedEvent>(g).Last(); var requested = E<DamageRequestedEvent>(g).Last();
            Need(V(g).Hp == before - 4 && applied is { TargetSeat: 0, Amount: 4, Nature: DamageNature.Thunder } && requested is { TargetSeat: 0, Amount: 4, SourceCard: CardKind.Lightning } && E<KuangxiangLightningBoundEvent>(g).Single().CardId == marked,
                "The real held black Lightning deals exactly four native damage and cold-returns without duplicating +1: " + JsonSerializer.Serialize(new { Before = before, After = V(g).Hp, Applied = applied, Requested = requested, Bound = E<KuangxiangLightningBoundEvent>(g) }));
            Need(!applied.SourceLess && !requested.SourceLess && applied.SourceSeat == 0 && requested.SourceSeat == 0,
                "The current native delayed-Lightning protocol represents the held card's source as its judgment holder; Kuangxiang preserves that existing provenance contract."); _ = Cold(g, r);
        }
    }
    public static void RedNextActualDrawStackingSkippedReplacementAndExpiryCold()
    {
        {
            var (g, r) = Start("red"); Activate(g); Target(g, 1); Play(g); Need(E<KuangxiangRedDrawIssuedEvent>(g).Single() is { Recipient: 1, Count: 2 } && E<KuangxiangRedDrawConsumedEvent>(g).Length == 0, "Two actually obtained red cards issue an unconsumed foreign recipient obligation.");
            End(g); Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 1)); Need(V(g, 1).HandCount == 4 && E<KuangxiangRedDrawConsumedEvent>(g).Single() is { Recipient: 1, Count: 2, Phase.Kind: ActualDiscardRecoveryPhaseKind.Draw }, "The next genuine Draw consumes both red cards and draws zero, without reducing another character's normal Draw.");
            g = Cold(g, r); Reach(g, () => g.State.CurrentSeat == 0 && P(g)?.Kind == DecisionKind.PlayCard); End(g); Reach(g, () => E<TurnEndedEvent>(g).Count(e => e.ActorSeat == 1) == 2);
            Need(V(g, 1).HandCount == 6 && E<KuangxiangRedDrawConsumedEvent>(g).Length == 1, "A later real Draw regains its normal two cards after the exact one-phase obligation expires."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("stack"); Activate(g); Target(g, 1); Play(g); Trim(g, 1); Trim(g, 1); End(g);
            Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 1)); var grants = E<KuangxiangRedDrawIssuedEvent>(g).Where(e => e.Recipient == 1).ToArray();
            Need(grants.Length == 2 && grants.Sum(e => e.Count) == 4 && E<KuangxiangRedDrawConsumedEvent>(g).Count(e => e.Recipient == 1) == 2 && V(g, 1).HandCount == 4, "Two real Preparation owners independently refill the same recipient before its Draw, stacking all four red reductions while the normal draw count clamps to zero: " + Diagnostic(g)); _ = Cold(g, r);
        }
        foreach (var scene in new[] { "skip", "replacement" })
        {
            var (g, r) = Start(scene); Activate(g); Target(g, 1); Play(g);
            End(g); Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 1));
            if (scene == "skip")
            {
                Need(E<KuangxiangRedDrawConsumedEvent>(g).Length == 0 && V(g, 1).HandCount == 4, "A genuinely skipped Draw preserves the obligation and performs no substitute normal draw.");
                g = Cold(g, r); Reach(g, () => E<KuangxiangRedDrawConsumedEvent>(g).Length == 1); Need(E<KuangxiangRedDrawConsumedEvent>(g).Single().Phase.TurnNumber > 1, "The remaining red reduction reaches the next actual Draw rather than disappearing at a skipped phase.");
            }
            else Need(E<KuangxiangRedDrawConsumedEvent>(g).Length == 1 && V(g, 1).HandCount == 7 && g.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.fixture:cgy-phase.Draw") == 3, "A real Draw replacement draws its own three cards and consumes this phase's normal-count reduction without leaking it into a later phase.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("hongji");
            for (var preparations = 0; preparations < 2; preparations++)
            {
                if (Optional(g)) { Activate(g); Target(g, 0); }
                else
                {
                    Need(OptionalHongji(g), "The real Preparation has either its own Kuangxiang choice or the actual Hongji grant.");
                    Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                    Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw") == true);
                    g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw");
                }
                if (preparations == 0) Reach(g, () => Optional(g) || OptionalHongji(g));
            }
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Boundary && E<ZhangShiPingHongjiConsumedEvent>(g).Length == 1);
            var red = E<KuangxiangRedDrawConsumedEvent>(g).Single(); var draws = E<ActualDiscardRecoveryPhaseStartedEvent>(g).Where(e => e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Draw).Select(e => e.Phase).ToArray();
            Need(draws.Length == 2 && red.Phase == draws[0] && red.Count == 2 && E<KuangxiangDrawConfirmedEvent>(g).Count(e => draws.Contains(e.Phase)) == 2 &&
                E<ZhangShiPingHongjiGrantedEvent>(g).Single() is { RecipientSeat: 0, Phase: TurnPhase.Draw } && E<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == Hongji && e.Started && e.Phase == TurnPhase.Draw) == 1 && V(g).HandCount == 6,
                "A real Hongji promise inserts the fifth native Draw producer with no DrawStarting candidate: the original Draw consumes red2 once, then the inserted Draw really confirms its new token and draws an unreduced two.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(E<ZhangShiPingHongjiReturnedEvent>(g).Length == 1 && E<KuangxiangRedDrawConsumedEvent>(g).Length == 1 && E<KuangxiangDrawConfirmedEvent>(g).Count(e => draws.Contains(e.Phase)) == 2,
                "The real DrawEnded child and Hongji typed return never reconfirm either token or consume the red invoice a second time across cold recovery.");
            var turn = g.State.TurnNumber; End(g); Reach(g, () => g.State.TurnNumber > turn && g.State.CurrentSeat == 0 && P(g)?.Kind == DecisionKind.PlayCard);
            Need(V(g).HandCount == 8 && E<KuangxiangRedDrawConsumedEvent>(g).Length == 1 && E<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == Hongji && e.Started && e.Phase == TurnPhase.Draw) == 1,
                "After declining later genuine Preparation offers, the subsequent normal Draw regains its full two cards; neither the original red grant nor old Hongji promise leaks into it."); _ = Cold(g, r);
        }
    }
    public static void PaidLossDeathHealthMovementAndReshuffleChildrenCold()
    {
        foreach (var scene in new[] { "loss", "source-death", "recipient-death" })
        {
            var (g, r) = Start(scene); Activate(g); Target(g, scene == "recipient-death" ? 1 : 0); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var paid = E<KuangxiangAdjustmentIssuedEvent>(g).Single(); Need(paid.ActualCount == 2 && E<KuangxiangCardObtainedEvent>(g).Length == 2 && Current(g) is { Stage: ChengGongYingStage.DrawChildren }, "The exact native draw and frozen original card invoices are already issued before its pausable native movement child.");
            g = Cold(g, r); Continue(g);
            if (scene == "loss")
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Play(g);
                Need(V(g).Skills!.All(s => s.ContentId != Skill), "The real nested recovery/HP child removes the conditional source before its paid draw returns."); var before = V(g, 1).Hp; Native(g, E<KuangxiangCardObtainedEvent>(g).First().CardId, CardKind.Slash, [1]); Play(g); Need(V(g, 1).Hp == before - 2, "The original invoiced black entity retains its damage effect after genuine skill loss.");
            }
            else
            {
                Reach(g, () => g.ResolutionStack.OfType<DyingFrame>().Any()); g = Cold(g, r); Reach(g, () => E<ChengGongYingCompletedEvent>(g).Any(e => e.FrameId == paid.FrameId));
                Need(!V(g, scene == "source-death" ? 0 : 1).IsAlive && g.State.Winner == Winner.None && E<KuangxiangAdjustmentIssuedEvent>(g).Count(e => e.FrameId == paid.FrameId) == 1, "Actual non-Lord source/recipient death drains the original paid movement subtree once without fabricating a second draw or repeating payment: " + Diagnostic(g));
                if (scene == "source-death") Need(E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0) || g.State.CurrentSeat != 0, "A paid Preparation owner's real death advances through the existing native finalizer without starting its nonexistent Draw/Ending.");
            }
            Need(E<ChengGongYingCompletedEvent>(g).Count(e => e.FrameId == paid.FrameId) == 1, "Cold native child returns complete the exact paid instruction once."); _ = Cold(g, r);
        }
        foreach (var scene in new[] { "reshuffle", "empty-deck" })
        {
            var (g, r) = Start(scene); Activate(g); Target(g, 0); Play(g); var paid = E<KuangxiangAdjustmentIssuedEvent>(g).Single();
            if (scene == "reshuffle") Need(paid.ActualCount == 2 && E<KuangxiangReshuffleCapturedEvent>(g).Single() is { Count: 4 } shuffle && shuffle.Before >= paid.Before && shuffle.After <= paid.After && E<KuangxiangCardObtainedEvent>(g).Length == 2, "The actual exhausted small deck reshuffles only the original four real discards, and invoices only the two cards actually drawn rather than the shuffle movements.");
            else Need(paid.ActualCount == 0 && E<KuangxiangCardObtainedEvent>(g).Length == 0 && E<KuangxiangRedDrawIssuedEvent>(g).Length == 0, "A genuinely exhausted draw/deck creates no fabricated obtained entity or red reduction."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ChengGongYingReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ChengGongYing is not null)?.ChengGongYing;
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Cheng Gong Ying command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only answer the actual human; never submit AI responses."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static bool Optional(GameEngine g) => P(g)?.SkillPrompt?.SkillId == Skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool OptionalHongji(GameEngine g) => P(g)?.SkillPrompt?.SkillId == Hongji && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Target(GameEngine g, int target) => Answer(g, c => c.Targets.SequenceEqual([target]));
    private static void Continue(GameEngine g) { var p = P(g)!; if (V(g, p.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g, string id, int[]? cards = null, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Trim(GameEngine g, int target) { Use(g, "trim", targets: [target]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0"); Play(g); }
    private static void Native(GameEngine g, int id, CardKind kind, int[]? targets = null)
    { var action = g.GetHumanLegalActions().First(a => a.CardId == id && (a.PlayedCardKind ?? V(g).Hand.First(c => c.Id == id).Kind) == kind && (targets is null || a.TargetSeats.SequenceEqual(targets))); Accept(g, new PlayCardCommand(0, id, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource }); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var n = 0; n < 500; n++)
        {
            if (done()) return; if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("Cheng Gong Ying ended before actual boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else if (p is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification, PlayerSeat: 0 }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (Optional(g)) Skip(g);
            else if (OptionalHongji(g)) Skip(g);
            else if (p?.SkillPrompt?.SkillId == Boundary) Continue(g);
            else if (p?.SkillPrompt?.SkillId == Prime) Answer(g, _ => true);
            else if (p is not null) throw new InvalidOperationException("Unexpected Cheng Gong Ying prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Cheng Gong Ying actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, g.State.Winner, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(3).Select(c => c.Parameters) } : null, Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Cold replay preserves all four privacy views, physical obtained-card invoices, next actual Draw obligations and exact paid native children without duplicate cost."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "source-death" ? Role.Renegade : Role.Lord, ModeId = scene.EndsWith("legacy", StringComparison.Ordinal) ? Legacy : Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, () => Optional(g) || scene == "hongji" && OptionalHongji(g)); return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cheng-gong-ying", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:cheng-gong-ying");
            if (scene == "hongji") CharacterFixtureContent.Register(builder, "ol:zhang-shi-ping");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"real-slash-room","query":"slashLimit","operation":"add","value":8,"priority":0},{"id":"real-distance","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"keep-real-hand","query":"handLimit","operation":"add","value":20,"priority":0}],"viewAs":[{"id":"one-real-entity","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},{"id":"two-real-entities","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true}],"activations":[
              {"id":"pair","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-real-entities","outputKind":"slash"}]},
              {"id":"trim","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
              {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
              {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Health}}","revision":1,"triggers":[{"id":"real-health-pause","window":"{{(scene=="loss"?"afterHpRecovered":"afterDamageApplied")}}","subject":"owner",{{(scene=="loss"?"":"\"damageOccurrence\":\"perDamage\",")}}"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
             {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"real-healthy-source","skillId":"{{Skill}}","minimumHp":8}]},
             {"id":"{{Prime}}","revision":1,"triggers":[{"id":"real-small-deck-emptying","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":4},{"op":"selectOwnedCards","target":"owner","zones":["hand"],"amount":4,"resultBind":"original"},{"op":"moveBoundCards","target":"owner","sourceBind":"original","destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}{{(scene=="loss"?",{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}":",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}")}}]}]}
            ]}
            """)!;
            if (scene == "skip") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Phase}}","revision":1,"triggers":[{"id":"native-skip-draw","window":"turnStartBeforeNormalFlow","subject":"owner","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]}"""));
            if (scene == "replacement") rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Phase}}","revision":1,"triggers":[{"id":"draw-three","window":"drawPhaseStarting","subject":"owner","optional":false,"drawPhaseMode":"replacement","usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":3}]}]}"""));
            if (scene == "hongji")
            {
                var effects = rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Prime)!["triggers"]![0]!["effects"]!.AsArray(); effects.RemoveAt(0); effects[0]!["amount"] = 2;
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Boundary}}","revision":1,"triggers":[{"id":"real-hongji-draw-ended-child","window":"drawPhaseEnded","subject":"owner","priority":-1000,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"return","options":[{"id":"continue"}]}]}]}"""));
            }
            var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "成公英真实原生边界夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in catalog.Programs) builder.AddSkill(new(id, id, "实际原生边界") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var role = scene == "source-death" ? Role.Renegade : Role.Lord; builder.AddSkill(new("fixture:cgy-pick", "固定本人", "真实公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == role ? 100000d : -100000d) });
            var own = new List<string> { Driver, scene == "loss" ? Conditional : Skill }; if (scene is "loss" or "source-death") own.Add(Watch); if (scene is "loss" or "lightning") own.Add(Health); if (scene is "reshuffle" or "hongji") own.Add(Prime); if (scene == "hongji") { own.Add(Hongji); own.Add(Boundary); }
            builder.AddGeneral(new(Owner, "实际成公英", "ol-cheng-gong-ying", "fixture:cgy-pick", "qun", 8, own) { InitialHp = scene == "loss" ? 7 : 8 }); var peers = Enumerable.Range(1, 3).Select(i => "fixture:cgy-peer-" + i).ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:cgy-pick-" + index; builder.AddSkill(new(pick, "固定其他角色", "真实公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == role ? -100000d : 100003d - index) });
                var skills = new List<string> { Quiet }; if (index == 0 && scene is "skip" or "replacement") skills.Add(Phase); if (index == 0 && scene == "stack") skills.Add(Skill); if (index == 0 && scene == "recipient-death") skills.Add(Watch); if (index == 0 && scene.StartsWith("chain-", StringComparison.Ordinal)) skills.Add(Health);
                builder.AddGeneral(new(id, "实际其他角色", "supporter", pick, "wei", 8, skills));
            }
            var kind = scene switch { "duel" => CardKind.Duel, "aoe" => CardKind.ArrowBarrage, "conversion" => CardKind.Crossbow, "chain-formal" or "chain-legacy" => CardKind.ThunderSlash, "lightning" => CardKind.Lightning, _ => CardKind.Slash };
            builder.AddCard(new("fixture:cgy-card", "真实固定实体", kind is CardKind.Slash or CardKind.ThunderSlash ? "基本牌" : kind == CardKind.Crossbow ? "装备牌" : "锦囊牌", "原生牌实体", kind));
            builder.AddDeck(new("fixture:cgy-deck", "成公英固定小牌堆", scene == "discard" ? 6 : scene is "equal" or "hongji" ? 4 : 2, 2, []) { PhysicalCards = Enumerable.Range(0, scene == "reshuffle" ? 12 : scene == "empty-deck" ? 8 : 80).Select(i => new ContentDeckPhysicalCard("fixture:cgy-card", scene is "red" or "stack" or "skip" or "replacement" or "hongji" ? Suit.Heart : Suit.Spade, scene == "lightning" ? 5 : i % 13 + 1)).ToArray() });
            builder.AddMode(new(scene.EndsWith("legacy", StringComparison.Ordinal) ? Legacy : Mode, "成公英实际原生规则", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:cgy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
