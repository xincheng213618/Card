using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SimultaneousPindianChecks
{
    private const string Gushe = "ol:gushe", Jici = "ol:jici", Driver = "fixture:simultaneous-driver", Quiet = "fixture:simultaneous-quiet";
    private const string Owner = "fixture:simultaneous-owner", Mode = "identity:classic-simultaneous", Watch = "fixture:simultaneous-watch", Loss = "fixture:simultaneous-loss", Pair = "fixture:simultaneous-peer-contest";

    public static void ActualThreeWayPrivateCommitTieAndPaymentsCold()
    {
        var (g, registry) = Start(); var source = V(g,0).Hand[0].Id; var before = g.CardMovements.Count;
        Group(g,[3,1,2],source);
        for (var i = 0; i < 3; i++)
        {
            var root = Root(g); var r = root.SimultaneousPindian!; var p = P(g)!;
            Need(r.SelectionIndex == i && r.Cards.Count == i + 1 && p.PlayerSeat == new[] { 3,1,2 }[i] && r.Stage == SimultaneousPindianStage.Selecting &&
                g.CardMovements.Count == before && !E<PindianCardsRevealedEvent>(g).Any(), "All participants commit private hands before any payment or common entity reveal.");
            Need(Enumerable.Range(0,4).Where(s => s != p.PlayerSeat).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
                Enumerable.Range(0,4).All(s => g.CreateSnapshot(s).Players.Where(v => v.Seat != s).All(v => v.Hand.Count == 0)),
                "Only the current participant sees the hand prompt; no viewer sees another participant's privately committed entity.");
            var unchanged = State(g); Need(!g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted && State(g) == unchanged,
                "The issuer cannot answer another participant's private commitment.");
            g = Cold(g,registry); Step(g);
        }
        var paid = Root(g); var id = paid.Id; var group = E<PindianCardsRevealedEvent>(g).Single(e => e.ParentFrameId == id);
        Need(group.Cards.Count == 4 && group.Cards[0].CardId == source && group.Cards.Skip(1).Select(c => c.Seat).SequenceEqual([3,1,2]) &&
            Mark(g) == 1 && E<PindianResultDeterminedEvent>(g).Count(e => e.FrameId == id) == 1,
            "The four real entities share one reveal; a tied first comparison gives the source a marker before its consequence prompt.");
        var stale = P(g)!; var hand = V(g,0).HandCount; g = Cold(g,registry); Answer(g,c => Branch(c,"draw"));
        var unchanged2 = State(g); Need(!g.Submit(new AnswerPromptCommand(0,stale.PromptId,stale.Choices[0].Id,g.Revision)).Accepted && State(g) == unchanged2,
            "A paid or replaced consequence prompt cannot replay its payment.");
        Settle(g); Play(g);
        Need(Mark(g) == 3 && V(g,0).HandCount == hand + 3 && E<PindianResultDeterminedEvent>(g).Where(e => e.FrameId == id).Select(e => e.Result.OpponentSeat).SequenceEqual([3,1,2]) &&
            E<SimultaneousPindianConsequencePaidEvent>(g).Count(e => e.ProgramFrameId == id) == 6 &&
            E<SimultaneousPindianConsequencePaidEvent>(g).Where(e => e.ProgramFrameId == id).GroupBy(e => (e.ComparisonIndex,e.ConsequenceIndex)).All(xs => xs.Count() == 1),
            "Ties make both participants nonwinners; all three ordered source draws and foreign discards settle once.");
        Need(group.Cards.All(c => g.CardMovements.Count(m => m.CardId == c.CardId && m.From == CardLocation.Hand(c.Seat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1 &&
            g.CardMovements.Count(m => m.CardId == c.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.PindianFinish) == 1) &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == Gushe), "One source card is reused logically, paid and cleaned once, and the exact original phase use remains spent.");
        _ = Cold(g,registry);
    }

    public static void ActualCurrentJiCiLessEqualDeclineAndPhaseRefundCold()
    {
        foreach (var count in new[] { 1,3 })
        {
            var (g,registry) = Start(); Group(g,Enumerable.Range(1,count).ToArray()); Settle(g); Play(g); Need(Mark(g) == count,"Actual initial tied comparisons bank the required markers without injecting state.");
            NextPlay(g); Group(g,[1]); Reach(g,() => Optional(P(g),Jici));
            var root = Root(g); var window = g.ResolutionStack.OfType<PindianRevealWindowFrame>().Single();
            Need(window.Cards[0].Rank == 1 && window.Cards[0].Rank <= count && !E<PindianResultDeterminedEvent>(g).Any(e => e.FrameId == root.Id),
                "Current JiCi offers on both rank equal to X and rank below X, after the actual reveal and before comparison.");
            g = Cold(g,registry); Answer(g,c => Action(c,"activate")); Settle(g); Play(g);
            var adjustment = E<PindianRevealRankAdjustedEvent>(g).Single(e => e.WindowFrameId == window.Id);
            Need(adjustment.Before == 1 && adjustment.Bonus == count && adjustment.After == 1 + count && adjustment.PreviousPhaseUses == 1 &&
                adjustment.RestoredSkill == Gushe && adjustment.UsageGroup == "simultaneous-pindian" && Mark(g) == count &&
                E<PindianResultDeterminedEvent>(g).Single(e => e.FrameId == root.Id).Result.SourceWon && g.GetHumanLegalActions().Any(a => a.SkillId == Gushe),
                "Choosing JiCi couples the exact public rank bonus with refund of only this Gushe phase activation.");
            Group(g,[2]); Reach(g,() => Optional(P(g),Jici)); g = Cold(g,registry); Answer(g,c => Action(c,"skip")); Settle(g); Play(g);
            Need(Mark(g) == count + 1 && !g.GetHumanLegalActions().Any(a => a.SkillId == Gushe), "An actually refunded second contest is legal; declining JiCi preserves its rank and spends this new use.");
            _ = Cold(g,registry);
        }
    }

    public static void ActualTypedResultClaimsCommonMaterialOwnershipCold()
    {
        foreach (var kind in new[] { "pindianClaim","pindianClaimAllWhenSourceWins","pindianMaximumSlashClaim" })
        {
            var (g,registry) = Start("claim-" + kind); Group(g,[3,1,2]);
            Reach(g,() => P(g) is { PlayerSeat: 0 } && P(g)!.Choices.Any(c => Branch(c,"claim")));
            var root = Root(g); var id = root.Id; var source = root.SimultaneousPindian!.Cards[0].CardId;
            var unchanged = State(g); var p = P(g)!;
            Need(!g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision)).Accepted && State(g) == unchanged,
                "Only the actual frozen policy owner can obtain this comparison's public entities.");
            g = Cold(g,registry); Answer(g,c => Branch(c,"claim"));
            for (var i = 0; i < 100 && g.ResolutionStack.Any(f => f.Id == id); i++)
            {
                if (P(g) is { PlayerSeat: 0 } current && current.Choices.Any(c => Branch(c,"claim"))) { g = Cold(g,registry); Answer(g,c => Branch(c,"claim")); }
                else if (Consequence(P(g),0)) { g = Cold(g,registry); Answer(g,c => Branch(c,"draw")); }
                else Step(g);
            }
            Need(!g.ResolutionStack.Any(f => f.Id == id),"The fixed actual typed claim/payment group completes."); Play(g);
            var reveal = E<PindianCardsRevealedEvent>(g).Single(e => e.ParentFrameId == id); var taken = E<SimultaneousPindianClaimPaidEvent>(g).Where(e => e.ProgramFrameId == id && e.Taken).ToArray();
            Need(E<PindianResultDeterminedEvent>(g).Where(e => e.FrameId == id).Select(e => e.Result.OpponentSeat).SequenceEqual([3,1,2]) &&
                E<SimultaneousPindianClaimsIssuedEvent>(g).Count(e => e.ProgramFrameId == id) == 3 && taken.SelectMany(e => e.CardIds).Distinct().Count() == taken.Sum(e => e.CardIds.Count),
                "The same source entity continues three ordered logical comparisons while claim cursors never obtain an already-owned entity twice.");
            Need(taken.Any(e => e.CardIds.Contains(source)) && g.CardMovements.Count(m => m.CardId == source && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.pindian.claim") == 1 &&
                taken.All(e => e.MovementAfter - e.MovementBefore == e.CardIds.Count && e.CardIds.All(card => g.CardMovements.Count(m => m.Sequence > e.MovementBefore && m.Sequence <= e.MovementAfter && m.CardId == card &&
                    m.From == CardLocation.Processing && m.To == CardLocation.Hand(e.Source.OwnerSeat) && m.Reason.Value == "program.pindian.claim") == 1)),
                "Every taken policy choice has its exact actual Processing-to-owner-hand invoice and one material ownership transfer.");
            Need(reveal.Cards.All(c => taken.Any(e => e.CardIds.Contains(c.CardId)) ? !g.CardMovements.Any(m => m.CardId == c.CardId && m.Reason == CardMoveReasons.PindianFinish) :
                g.CardMovements.Count(m => m.CardId == c.CardId && m.Reason == CardMoveReasons.PindianFinish) == 1),
                "Claimed cards stay with their rightful owner; only actual unclaimed Processing materials finish once."); _ = Cold(g,registry);
        }
        var ids = new List<int> { 4,5 }; var list = new List<PindianPolicyClaim> { new(0,new("fixture:claim","own",0,"instance"),SkillProgramCardPolicyKind.PindianClaim,ids) };
        var frozen = (SimultaneousPindianClaimsIssuedEvent)CommittedEventProjection.Freeze(new SimultaneousPindianClaimsIssuedEvent(1,0,new(list)));
        var paid = (SimultaneousPindianClaimPaidEvent)CommittedEventProjection.Freeze(new SimultaneousPindianClaimPaidEvent(1,0,0,list[0].Source,true,ids,2,4)); ids.Clear(); list.Clear();
        Need(frozen.Claims.Claims is ICollection<PindianPolicyClaim> { IsReadOnly: true } && frozen.Claims.Claims[0].CardIds.SequenceEqual([4,5]) &&
            paid.CardIds.SequenceEqual([4,5]) && paid.CardIds is ICollection<int> { IsReadOnly: true },"Nested claim policy and paid entity facts are detached and frozen before commit observers.");
        ActualSharedTopRandomAndEffectiveRankPoliciesCold();
    }

    private static void ActualSharedTopRandomAndEffectiveRankPoliciesCold()
    {
        foreach (var scenario in new[] { "policies-top","policies-top-tiny","policies-random","policies-effective" })
        {
            var (g,registry) = Start(scenario); var original = V(g,0).Hand.Max(c => c.Id); var before = g.CardMovements.Count;
            if (scenario == "policies-top-tiny")
            {
                Accept(g,new PlayCardCommand(0,V(g,0).Hand.First().Id,[1],g.Revision,P(g)!.PromptId)); Play(g);
                Need(g.CreateSnapshot(0).DrawPileCount == 1 && g.CreateSnapshot(0).DiscardPileCount == 1,"The real tiny-deck Slash creates one discard while leaving one top for multiple Tianbian participants. " + JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Phase, g.State.DrawPileCount, g.State.DiscardPileCount, Hands = Enumerable.Range(0,4).Select(seat => V(g,seat).Hand.Count), Movements = g.CardMovements }));
                before = g.CardMovements.Count;
            }
            var observed = new List<CardMovementRecord>();
            void Observe(GameEngine engine) => engine.CardMoved += movement => observed.Add(movement);
            GameEngine ColdPolicy(GameEngine engine) { var restored = Cold(engine,registry); Observe(restored); return restored; }
            Observe(g);
            var originalHands = Enumerable.Range(0,4).ToDictionary(s => s,s => V(g,s).Hand.Select(c => c.Id).Order().ToArray());
            Group(g,[1,2,3],original); var id = Root(g).Id;
            for (var i = 0; i < 80 && !E<PindianCardsRevealedEvent>(g).Any(e => e.ParentFrameId == id); i++)
            {
                var r = Root(g).SimultaneousPindian!; var p = P(g)!;
                Need(!E<PindianTopSourceChosenEvent>(g).Any() &&
                    Enumerable.Range(0,4).Where(s => s != p.PlayerSeat).All(s => g.CreateSnapshot(s).PendingDecision is null),
                    "Private top commitments expose neither a card identity nor a foreign prompt before the common reveal.");
                if (r.Stage == SimultaneousPindianStage.RandomPolicies)
                {
                    Need(p.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0),"A real random-policy activation exposes no chosen hidden hand entity or target authority.");
                    if (p.PlayerSeat == 0)
                    {
                        var target = r.RandomSelection!.Candidates[r.RandomSelection.Index].TargetSeat; g = ColdPolicy(g);
                        Answer(g,c => Branch(c,scenario == "policies-random" && target == 3 ? "random-decline" : "random"));
                    }
                    else { g = ColdPolicy(g); Step(g); }
                }
                else
                {
                    Need(r.Stage == SimultaneousPindianStage.Selecting && r.RandomSelection!.Index == r.RandomSelection.Candidates.Count &&
                        r.TopReceipts.Select(t => t.CardId).Distinct().Count() == r.TopReceipts.Count &&
                        r.TopReceipts.All(t => g.CardMovements.Count(m => m.CardId == t.CardId && m.Sequence == t.ReservationMovementSequence && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == "skill.pindian.reserve-top") == 1),
                        "All random costs complete before top selection; each held top has its own actual once-paid movement invoice across intervening refill children.");
                    Need(Enumerable.Range(0,4).All(seat => g.CreateSnapshot(seat).ProcessingCardCount == 0 &&
                        !g.CreateSnapshot(seat).PublicRevealedCards.Any(c => r.TopReceipts.Any(t => t.CardId == c.Id))) &&
                        r.TopReceipts.All(t => g.CreateCardZoneDiagnostics().Single(d => d.CardId == t.CardId).Location == CardLocation.Processing) &&
                        observed.All(m => m.Reason.Value != "skill.pindian.reserve-top") &&
                        !E<CardMovedEvent>(g).Any(m => m.Reason.Value == "skill.pindian.reserve-top"),"No viewer sees privately held top identities before common reveal.");
                    g = ColdPolicy(g); Step(g);
                }
            }
            if (scenario == "policies-top-tiny")
            {
                Need(P(g)?.SkillPrompt?.SkillId == Watch,"The actual hand payment at common reveal opens its typed draw child with both top cards already held.");
                var held = Root(g).SimultaneousPindian!.TopReceipts.Select(t => t.CardId).ToArray();
                g = ColdPolicy(g); Answer(g,c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                Need(held.All(card => g.CreateCardZoneDiagnostics().Single(d => d.CardId == card).Location == CardLocation.Processing),"The native draw child cannot consume either privately reserved actual top entity after deck depletion.");
            }
            var root = Root(g); var receipt = root.SimultaneousPindian!; var reveal = E<PindianCardsRevealedEvent>(g).Single(e => e.ParentFrameId == id);
            Need(receipt.OriginalSourceCardId == original && root.SelectedCardIds.SequenceEqual([original]) && reveal.Cards.Count == 4 &&
                reveal.Cards.Select(c => c.CardId).Distinct().Count() == 4,
                "The original real activation input and the four actually committed material identities remain separate typed proofs.");
            if (scenario == "policies-top") Need(receipt.TopReceipts.Count == 3 && receipt.RandomSelection!.Receipts.Count == 0,"Three actual Tianbian participants reserve three different consecutive top entities rather than one unconsumed top.");
            else if (scenario == "policies-top-tiny")
            {
                Need(receipt.TopReceipts.Count == 2 && g.CardMovements.Count(m => m.Reason == CardMoveReasons.Reshuffle) == 1 &&
                    receipt.TopReceipts.All(t => g.CardMovements.Count(m => m.Sequence > before && m.CardId == t.CardId && m.To == CardLocation.Processing) == 1 &&
                        g.CardMovements.Count(m => m.Sequence == t.ReservationMovementSequence && m.CardId == t.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == "skill.pindian.reserve-top") == 1 &&
                        !g.CardMovements.Any(m => m.Sequence > receipt.RevealBefore && m.Sequence <= receipt.RevealAfter && m.CardId == t.CardId && m.To == CardLocation.Processing)),
                    "A one-card actual deck refills from a real discard for the second top; reserved tops are exclusive and the third participant falls back to hand after genuine depletion. " + JsonSerializer.Serialize(new { Before = before, Receipt = receipt, Movements = g.CardMovements }));
            }
            else
            {
                var random = receipt.RandomSelection!; var forcedSource = random.Receipts.Single(p => p.TargetSeat == 0);
                Need(random.Candidates.Count == 6 && random.Receipts.Count == (scenario == "policies-random" ? 3 : 4) &&
                    random.Receipts.Select(p => p.TargetSeat).Distinct().Count() == random.Receipts.Count &&
                    random.Receipts.All(p => p.RandomBefore != p.RandomAfter && originalHands[p.TargetSeat].Contains(p.CardId) &&
                        p.RandomAfter == NextRandomState(p.RandomBefore) && p.CardId == originalHands[p.TargetSeat][(int)(p.RandomAfter % (uint)originalHands[p.TargetSeat].Length)] &&
                        E<PindianRandomHandCommittedEvent>(g).Count(e => e.FrameId == id && e.OwnerSeat == p.OwnerSeat && e.TargetSeat == p.TargetSeat && e.Source == p.Source) == 1) &&
                    receipt.Cards[0].CardId == forcedSource.CardId && receipt.TopReceipts.Count == (scenario == "policies-random" ? 1 : 0),
                    "Multiple real Hanzhan policies use native RNG once per target; one common source is forced once, independent opponents are forced once, and a declined target may choose actual Tianbian top.");
                Need(random.Receipts.Zip(random.Receipts.Skip(1)).All(p => p.First.RandomAfter == p.Second.RandomBefore),"Cold policy pauses do not consume extra RNG or duplicate already-fixed source costs.");
                Need(forcedSource.CardId == original || !g.CardMovements.Any(m => m.CardId == original && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal),
                    "A genuinely replaced source card pays only the actual RNG-selected entity, without also consuming the original activation input.");
                if (scenario == "policies-effective") Need(receipt.Cards.Skip(1).All(c => c.OriginalRank == 13) && receipt.Cards[0].OriginalRank == 1,
                    "Actual current Tianbian heart-rank policies apply to independently forced real participant cards; the plain shared source keeps its own effective rank.");
            }
            Need(receipt.TopReceipts.All(t => E<PindianTopSourceChosenEvent>(g).Count(e => e.OwnerSeat == t.Seat && e.CardId == t.CardId && e.Source == t.Source) == 1 &&
                g.CardMovements.Count(m => m.CardId == t.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == "skill.pindian.reserve-top") == 1),
                "A reserved top is publicly identified only with its one actual shared reveal payment.");
            for (var i = 0; i < 100 && g.ResolutionStack.Any(f => f.Id == id); i++)
            {
                if (P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => Branch(c,"claim"))) { g = ColdPolicy(g); Answer(g,c => Branch(c,"claim")); }
                else if (Consequence(P(g),0)) { g = ColdPolicy(g); Answer(g,c => Branch(c,"draw")); }
                else Step(g);
            }
            Need(!g.ResolutionStack.Any(f => f.Id == id) && E<PindianResultDeterminedEvent>(g).Where(e => e.FrameId == id).Select(e => e.Result.SourceCardId).Distinct().Single() == receipt.Cards[0].CardId &&
                E<PindianResultDeterminedEvent>(g).Count(e => e.FrameId == id) == 3,
                "Top/random/rank and claim combinations finish three comparisons with one common actual source entity; every material uses its actual paid lineage."); _ = ColdPolicy(g);
        }
    }
    private static uint NextRandomState(uint state) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }

    private static PlayerSnapshot V(GameEngine g,int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0,4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Mark(GameEngine g) => V(g,0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.RaoShe)?.Count ?? 0;
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SimultaneousPindian is not null);
    private static bool Action(PromptChoice c,string action) => c.Parameters.GetValueOrDefault("program-action") == action;
    private static bool Branch(PromptChoice c,string branch) => Action(c,"simultaneous-pindian") && c.Parameters.GetValueOrDefault("branch") == branch;
    private static bool Optional(PendingDecision? p,string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Action(c,"activate"));
    private static bool Consequence(PendingDecision? p,int seat) => p?.PlayerSeat == seat && p.Choices.Any(c => Branch(c,"draw"));
    private static void Need(bool value,string why) { if (!value) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g,GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted,result.Error?.Message ?? "Rejected real simultaneous Pindian command."); }
    private static void Step(GameEngine g) => Accept(g,new AdvanceOneStepCommand(g.Revision));
    private static void Answer(GameEngine g,Func<PromptChoice,bool> pick) { var p = P(g)!; Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(pick).Id,g.Revision)); }
    private static void End(GameEngine g) => Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g,() => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextPlay(GameEngine g) { var turn = g.CreateSnapshot(0).TurnNumber; End(g); Reach(g,() => g.CreateSnapshot(0).TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); }
    private static void Group(GameEngine g,IReadOnlyList<int> targets,int? card = null) => Accept(g,new UseProgramSkillCommand(0,Gushe,"simultaneous-pindian",[card ?? V(g,0).Hand.First(c => c.Kind == CardKind.Slash).Id],targets,g.Revision,P(g)!.PromptId));
    private static void Settle(GameEngine g) => Reach(g,() => !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SimultaneousPindian is not null));
    private static void Reach(GameEngine g,Func<bool> done)
    {
        for (var i = 0; i < 240; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None,"The small actual simultaneous fixture ended before its boundary."); var p = P(g);
            if (p is { PlayerSeat: 0 } && Optional(p,Jici)) Answer(g,c => Action(c,"skip"));
            else if (p is { PlayerSeat: 0 } && Consequence(p,0)) Answer(g,c => Branch(c,"draw"));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification })
                Answer(g,c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else Step(g);
        }
        throw new InvalidOperationException("The fixed simultaneous command bound was exceeded. " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0,4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f,f.GetType())).ToArray(), Movements = g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g,ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);
        Need(State(g) == State(restored),"All safe views, paid group receipts, typed reveal ranks and native child cursors survive cold command replay."); return restored; }
    private static string Embedded(string suffix) => EmbeddedBundle("ol-wang-lang",suffix);
    private static string EmbeddedBundle(string bundle,string suffix) { using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.{suffix}")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine,ContentRegistry) Start(string scenario = "normal")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario is "paid-death" or "threshold" ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 },registry);
        Accept(g,new StartGameCommand()); Reach(g,() => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId)); Play(g); return (g,registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("simultaneous-pindian-fixture",new(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog,IReadOnlyList<string>? only = null) { foreach (var (id,program) in catalog.Programs.Where(p => only is null || only.Contains(p.Key))) b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r,_ => id == Quiet ? 10000d : -10000d) }); }
            var own = JsonNode.Parse(Embedded("rules.json"))!; if (scenario == "bad-reference") own["skills"]![1]!["triggers"]![0]!["effects"]![0]!["sourceBind"] = Quiet;
            Add(SkillProgramCatalog.Load(own.ToJsonString(),Embedded("presentation.json")));
            if (scenario.StartsWith("policies-",StringComparison.Ordinal))
            {
                Add(SkillProgramCatalog.Load(EmbeddedBundle("classic-qin-mi","rules.json"),EmbeddedBundle("classic-qin-mi","presentation.json")),["classic:tianbian"]);
                Add(SkillProgramCatalog.Load(EmbeddedBundle("boundary-taishi-ci","rules.json"),EmbeddedBundle("boundary-taishi-ci","presentation.json")),["boundary:hanzhan"]);
            }
            var defs = new List<string> { $$"""
                {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"pair","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}]},
                  {"id":"draw20","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                  {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                {"id":"{{Pair}}","revision":1,"triggers":[{"id":"pair","window":"playPhaseStarting","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingWithHand"},{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"pair","visibility":"public"}]}]}
                """ };
            if (scenario.StartsWith("paid-",StringComparison.Ordinal)) defs.Add($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.simultaneous-pindian.draw"],"usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
                """);
            if (scenario == "policies-top-tiny") defs.Add($$"""
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"draw-at-common-reveal","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.pindian.reveal"],"usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]}
                """);
            if (scenario == "paid-loss") defs.Add($$"""
                {"id":"{{Loss}}","revision":1,"triggers":[{"id":"lose","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,
                "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Gushe}}","{{Jici}}"],"sourceBind":"standard:none"}]}]}
                """);
            if (scenario.StartsWith("claim-",StringComparison.Ordinal)) defs.Add($$"""
                {"id":"fixture:simultaneous-claim","revision":1,"cardPolicies":[{"id":"actual-public-claim","kind":"{{scenario[6..]}}"}]}
                """);
            if (scenario == "equipment") defs.Add("""
                {"id":"fixture:simultaneous-recovery-watch","revision":1,"triggers":[{"id":"recover","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-recovery","options":[{"id":"continue"}]}]}]}
                """);
            var node = JsonNode.Parse($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""")!;
            var labels = node["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(),n => new { name = n!["id"]!.GetValue<string>(), description = "真实多人拼点规则驱动",
                optionLabels = n.ToJsonString().Contains("chooseOption",StringComparison.Ordinal) ? new Dictionary<string,string> { ["continue"] = "继续" } : new Dictionary<string,string>() });
            Add(SkillProgramCatalog.Load(node.ToJsonString(),JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            var skills = new List<string> { Gushe,Jici }; if (scenario.StartsWith("paid-",StringComparison.Ordinal) || scenario == "policies-top-tiny") skills.Add(Watch); if (scenario == "paid-loss") skills.Add(Loss);
            if (scenario.StartsWith("claim-",StringComparison.Ordinal)) skills.Add("fixture:simultaneous-claim");
            if (scenario == "equipment") skills.Add("fixture:simultaneous-recovery-watch");
            if (scenario is "policies-random" or "policies-effective") skills.Add("boundary:hanzhan");
            b.AddGeneral(new(Owner,"真实同时拼点者","supporter",Driver,"wei",12,skills,GeneralGender.Male));
            var peers = Enumerable.Range(1,3).Select(i => $"fixture:simultaneous-peer-{i}").ToArray(); foreach (var id in peers) b.AddGeneral(new(id,"真实拼点参与者","supporter",Quiet,"wei",12,
                scenario == "opponent-pair" ? [Pair] : scenario is "policies-top" or "policies-top-tiny" ? ["classic:tianbian"] : scenario is "policies-random" or "policies-effective" ? ["classic:tianbian","boundary:hanzhan"] : []));
            if (scenario == "equipment") b.AddCard(new("fixture:simultaneous-lion","白银狮子","装备牌","实际白银狮子恢复",CardKind.SilverLion));
            b.AddDeck(new("fixture:simultaneous-deck","固定实际一点杀",4,0,[]) { PhysicalCards = Enumerable.Range(0,scenario == "policies-top-tiny" ? 17 : 128).Select(i => new ContentDeckPhysicalCard(scenario == "equipment" && i % 2 == 0 ? "fixture:simultaneous-lion" : "standard:slash",scenario == "policies-effective" ? Suit.Heart : Suit.Spade,scenario == "ai-order" ? i % 13 + 1 : 1)).ToArray() });
            b.AddMode(new(Mode,"真实同时拼点",4,4,new Dictionary<string,int> { [nameof(Role.Lord)] = 1,[nameof(Role.Rebel)] = 3 },"fixture:simultaneous-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
        }
    }
}
