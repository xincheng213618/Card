using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLiRuChecks
{
    private const string Mieji = "boundary:mieji-current", Fencheng = "boundary:fencheng-current", Juece = "boundary:juece-current";
    private const string Driver = "fixture:lr-driver", Top = "fixture:lr-top", Cost = "fixture:lr-cost", Hp = "fixture:lr-hp", Gain = "fixture:lr-gain", Damage = "fixture:lr-damage";
    private const string Dying = "fixture:lr-dying";
    private const string Mode = "identity:classic-li-ru-fixture";
    private const string MiejiReason = "skill-program." + Mieji + ".ChooseCategoryOrSequentialDiscard";
    private const string FenchengReason = "skill-program." + Fencheng + ".EscalatingDiscardOrDamageFromSelected";

    // Draft only. No loader, build or test execution has been performed for this staged file.
    public static void SequentialAnyDiscardKeepsTopPaymentAndChildReturns()
    {
        var (g, r) = Create(observers: true, replenish: true); Play(g); GiveTrick(g, 0); GiveTrick(g, 1);
        Use(g, "equip", [1]); Play(g); Use(g, "hurt", [1]); Play(g);
        var topCard = V(g, 0).Hand.First(c => c.Kind == CardKind.Dismantlement && c.Suit == Suit.Heart).Id;
        var firstCard = V(g, 1).Hand.First(c => c.Kind == CardKind.Dismantlement).Id;
        var lion = V(g, 1).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        StartMieji(g, topCard, 1); Reach(g, p => p.SkillPrompt?.SkillId == Top);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Mieji);
        var rootId = root.Id;
        var topReceipt = root.SequentialDiscardTopPayment!;
        var topMove = g.CardMovements.Single(m => m.Sequence > topReceipt.MovementSequenceBefore && m.CardId == topCard &&
            m.Reason.Value == "skill-program." + Mieji + ".MoveBoundCards");
        Require(root.SequentialDiscard is null && root.InstructionIndex == 2 && topReceipt.CardId == topCard &&
            topMove.From == CardLocation.Hand(0) && topMove.To == CardLocation.DrawPile &&
            !F<CardUseDeclaredEvent>(g).Any(e => e.CardId == topCard),
            "The actual red trick entity is placed on top once without being used; its original movement child precedes any forced discard.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, IsSequential);
        Private(g, 1); Reject(g); g = RestoreAfterCold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "sequential");
        Require(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([firstCard])),
            "The explicit two-card branch accepts a trick as its first real payment and does not inherit classic's non-trick complement filter.");
        Answer(g, c => c.Cards.SequenceEqual([firstCard])); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        root = SequentialRoot(g); var firstPayment = root.SequentialDiscard!.Payment!;
        Require(root.Id == rootId &&
            root.SequentialDiscard is { Remaining: 1, Stage: ProgramSequentialDiscardStage.AwaitingMovement } &&
            root.PendingMovementContinuation is { SubjectSeat: 1, BeforeCount: 0, CoverageResultBind: null } &&
            Moves(g, MiejiReason) is [var paid] && paid.CardId == firstCard &&
            V(g, 1).Equipment.Any(c => c.Id == lion) && firstPayment.ActualCount == 1,
            "Only the first real entity has paid while its own movement observer is pending; the second equipment cost remains untouched.");
        g = RestoreAfterCold(g, r); FrozenDiscardCollections(SequentialRoot(g).SequentialDiscard!);
        Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(SequentialRoot(g).SequentialDiscard!.Remaining == 1 && Moves(g, MiejiReason).Length == 1,
            "The first payment's real reward gain child completes before the second private discard is published.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, IsSequential);
        Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        root = SequentialRoot(g);
        Require(root.SequentialDiscard is { Remaining: 0, Stage: ProgramSequentialDiscardStage.AwaitingMovement } &&
            Moves(g, MiejiReason).Length == 2 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h =>
                h.ResumeFrameId == root.Id && h.Change.ParentFrameId == root.Id && h.Change.TargetSeat == 1 &&
                h.Continuation == PostEventContinuation.AwaitedProgramMovement),
            "The second Silver Lion entity pays exactly once; its actual recovery child retains the same sequential owner before completion.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var secondWindow = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
        Require(secondWindow.Batch.Movements is [var second] && second.CardId == lion && second.From == CardLocation.Equipment(1) &&
            second.To == CardLocation.DiscardPile && secondWindow.Batch.AwaitingProgramFrameId == root.Id,
            "The second movement batch is its exact already-paid equipment entity, not a rebuilt hand selection.");
        g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(F<ProgramSequentialDiscardFinishedEvent>(g).Single().RemainingUnpaid == 0 && Moves(g, MiejiReason).Length == 2 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == Mieji) && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SequentialDiscard is not null),
            "Both full child chains return once and retain the original per-play-phase use limit."); Cold(g, r);
    }

    public static void SequentialShortfallAndOriginalSourceLossKeepRealCosts()
    {
        var (g, r) = Create(); Play(g); GiveTrick(g, 0); Clear(g, 1); Use(g, "draw", [1]); Play(g);
        Require(V(g, 1).HandCount == 1 && V(g, 1).Equipment.Count == 0, "Real commands establish exactly one legal forced discard entity.");
        var cost = V(g, 0).Hand.First(c => c.Kind == CardKind.Dismantlement).Id;
        var only = V(g, 1).Hand.Single().Id; StartMieji(g, cost, 1); Reach(g, IsSequential);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "sequential"); g = RestoreAfterCold(g, r);
        Answer(g, c => c.Cards.SequenceEqual([only])); Play(g);
        Require(F<ProgramSequentialDiscardFinishedEvent>(g).Single().RemainingUnpaid == 1 && Moves(g, MiejiReason).Single().CardId == only &&
            g.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile) == 1,
            "The documented insufficient-second-card default stops the unpaid remainder and retains both actual first payments."); Cold(g, r);

        var (lost, lr) = Create(observers: true, removeSource: true); Play(lost); GiveTrick(lost, 0);
        var placed = V(lost, 0).Hand.First(c => c.Kind == CardKind.Dismantlement).Id;
        StartMieji(lost, placed, 1); Reach(lost, p => p.SkillPrompt?.SkillId == Top);
        var original = lost.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Mieji);
        lost = RestoreAfterCold(lost, lr); Continue(lost); Play(lost);
        Require(lost.CardMovements.Count(m => m.CardId == placed && m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile &&
                m.Reason.Value == "skill-program." + Mieji + ".MoveBoundCards") == 1 &&
            !F<ProgramSequentialDiscardStartedEvent>(lost).Any(e => e.FrameId == original.Id) &&
            F<ProgramSkillResolvedEvent>(lost).Any(e => e.FrameId == original.Id && !e.Completed) &&
            !lost.GetHumanLegalActions().Any(a => a.SkillId == Mieji),
            "Actual runtime source removal inside the paid top movement cancels only the unpaid challenge, without recapture or replacement source."); Cold(lost, lr);
    }

    public static void ChosenStartEscalationUsesActualDiscardAndFireReturn()
    {
        var (g, r) = Create(observers: true); Play(g);
        Accept(g, new UseProgramSkillCommand(0, Fencheng, "chosen-start-fire-walk", [], [2], g.Revision, P(g)!.PromptId)); Reach(g, IsSequential);
        var root = SequentialRoot(g); var rootId = root.Id;
        Require(root.SequentialDiscard is { StartSeat: 2, Cursor: 0, ChooserSeat: 2, PreviousCount: 0 } &&
            root.SequentialDiscard.Order.SequenceEqual([2, 3, 1]) && F<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Fencheng && e.Scope == SkillUsageScope.Game) == 1,
            "The real chosen start freezes a clockwise ring excluding the source, with first threshold one and one original game usage.");
        Private(g, 2); g = RestoreAfterCold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "discard");
        var ids = P(g)!.Choices.Where(c => c.Cards.Count == 1).Take(2).Select(c => c.Cards.Single()).ToArray();
        Require(ids.Length == 2, "The fixed actual chooser can exceed the first threshold by paying two real entities.");
        var before = g.CardMovements.Count; Answer(g, c => c.Cards.SequenceEqual([ids[0]]));
        Answer(g, c => c.Cards.SequenceEqual([ids[1]]));
        Require(g.CardMovements.Count == before && SequentialRoot(g).SequentialDiscard!.SelectedCardIds.SequenceEqual(ids),
            "All private selections freeze before the atomic ring payment moves any entity.");
        g = RestoreAfterCold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        root = SequentialRoot(g); var batch = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == rootId);
        Require(root.SequentialDiscard is { PreviousCount: 2, Stage: ProgramSequentialDiscardStage.AwaitingMovement } &&
            batch.Batch.Movements.Count == 2 && batch.Batch.Movements.Select(m => m.CardId).SequenceEqual(ids) &&
            root.SequentialDiscard.Payment is { ActualCount: 2 } && root.Id == rootId,
            "The atomic exact ledger fixes actual predecessor two while all cost children still belong to the original ring.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, IsSequential);
        Require(P(g)!.PlayerSeat == 3 && SequentialRoot(g).SequentialDiscard!.PreviousCount == 2 &&
            P(g)!.Prompt.Contains("2", StringComparison.Ordinal), "The next participant is seat three and sees the actual previous paid count two.");
        var hpBefore = g.State.Players[3].Hp; Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "damage"); Reach(g, p => p.SkillPrompt?.SkillId == Damage);
        root = SequentialRoot(g); var actual = g.ResolutionStack.OfType<DamageFrame>().Single(f => f.ParentFrameId == rootId);
        Require(root.SequentialDiscard is { Stage: ProgramSequentialDiscardStage.AwaitingDamage, PreviousCount: 0, ChooserSeat: 3 } &&
            root.AttackAttempt is { SourceSeat: 0, TargetSeat: 3, Nature: DamageNature.Fire } && root.AttackReturn is not null &&
            actual.SourceSeat == 0 && actual.TargetSeat == 3 && actual.Amount == 2 && actual.Nature == DamageNature.Fire &&
            g.State.Players[3].Hp == hpBefore - 2, "The actual two-fire program damage pauses in its mature typed owner before the next actor; choosing damage resets the next threshold.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = RestoreAfterCold(g, r); Continue(g); Reach(g, IsSequential);
        Require(P(g)!.PlayerSeat == 1 && SequentialRoot(g).SequentialDiscard!.PreviousCount == 0 &&
            SequentialRoot(g).AttackAttempt is null && SequentialRoot(g).AttackReturn is null,
            "Real recovery and HP observers complete before the ring advances to seat one with threshold one.");
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "discard"); var last = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([last])); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Reach(g, p => p.SkillPrompt?.SkillId == Cost); g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(Moves(g, FenchengReason).Length == 3 && F<ProgramSequentialDiscardFinishedEvent>(g).Single().ProcessedSeats == 3 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == Fencheng) && F<ProgramSequentialDiscardDamageChosenEvent>(g) is [var fire] && fire.TargetSeat == 3,
            "The three exact participants finish once, with actual counts two/fire-reset/one and no game-usage refund."); Cold(g, r);

        var (dead, dr) = Create(observers: true, fatalCost: true); Play(dead);
        Accept(dead, new UseProgramSkillCommand(0, Fencheng, "chosen-start-fire-walk", [], [2], dead.Revision, P(dead)!.PromptId)); Reach(dead, IsSequential);
        var paidRootId = SequentialRoot(dead).Id; Answer(dead, c => c.Parameters.GetValueOrDefault("branch") == "discard");
        var actualCost = P(dead)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); Answer(dead, c => c.Cards.SequenceEqual([actualCost]));
        Answer(dead, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Reach(dead, p => p.SkillPrompt?.SkillId == Cost);
        dead = RestoreAfterCold(dead, dr); Continue(dead); Reach(dead, p => p.SkillPrompt?.SkillId == Dying);
        var originalDying = dead.ResolutionStack.OfType<DyingFrame>().Single();
        var costChild = dead.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Cost);
        Require(originalDying.VictimSeat == 2 && originalDying.ParentFrameId == costChild.Id && originalDying.ResumesProgramSkill &&
            SequentialRoot(dead).Id == paidRootId && SequentialRoot(dead).SequentialDiscard is
                { ChooserSeat: 2, Stage: ProgramSequentialDiscardStage.AwaitingMovement, Payment.ActualCount: 1 } &&
            Moves(dead, FenchengReason) is [var once] && once.CardId == actualCost,
            "Real LostHP in the paid movement observer creates one exact current victim Dying entry beneath the same paid ring.");
        Private(dead, 2); dead = RestoreAfterCold(dead, dr); Continue(dead); Reach(dead, IsSequential);
        Require(!dead.State.Players[2].IsAlive && dead.State.Winner == Winner.None && P(dead)!.PlayerSeat == 3 &&
            SequentialRoot(dead).Id == paidRootId && SequentialRoot(dead).SequentialDiscard is { Cursor: 1, PreviousCount: 1 } &&
            Moves(dead, FenchengReason).Length == 1,
            "No-Peach death completes the exact paid child's typed return and keeps the next original ring seat, without a second cost or blanket source cancellation.");
        dead = RestoreAfterCold(dead, dr); Answer(dead, c => c.Parameters.GetValueOrDefault("branch") == "damage"); Reach(dead, IsSequential);
        Require(P(dead)!.PlayerSeat == 1 && SequentialRoot(dead).SequentialDiscard!.PreviousCount == 0,
            "The surviving next participant's actual fire/HP child completes and resets the final threshold.");
        Answer(dead, c => c.Parameters.GetValueOrDefault("branch") == "damage"); Play(dead);
        Require(F<ProgramSequentialDiscardFinishedEvent>(dead).Single(e => e.FrameId == paidRootId).ProcessedSeats == 3 &&
            Moves(dead, FenchengReason).Length == 1 && F<SkillUsageConsumedEvent>(dead).Count(e => e.SkillId == Fencheng && e.Scope == SkillUsageScope.Game) == 1,
            "The original limited usage and already-paid entity finish once after a real participant death."); Cold(dead, dr);
    }

    public static void EndingPublicHandComparisonAndNativeForcedChoices()
    {
        var (g, r) = Create(observers: true); Play(g); SetHand(g, 0, 2); SetHand(g, 1, 2); SetHand(g, 2, 3); SetHand(g, 3, 0);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => p.SkillPrompt?.SkillId == Juece && Action(p, "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, p => p.SkillPrompt?.SkillId == Juece && Action(p, "select-target"));
        Require(P(g)!.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([1, 3]),
            "The actual Ending target query includes equal and zero public hand counts while excluding the larger hand.");
        g = RestoreAfterCold(g, r); var hp = g.State.Players[1].Hp; Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => p.SkillPrompt?.SkillId == Damage);
        var juece = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Juece);
        var ending = g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single(f => f.Id == juece.WindowContext!.ParentFrameId);
        Require(ending.OwnerSeat == 0 && ending.TurnNumber == g.State.TurnNumber && juece.SelectedTargetSeats.SequenceEqual([1]) &&
            g.State.Players[1].Hp == hp - 1 && g.ResolutionStack.OfType<DamageFrame>().Any(d => d.ParentFrameId == juece.Id && d.TargetSeat == 1 && d.Amount == 1),
            "The equal-hand target receives one real damage under that exact actual Ending, not the classic empty-hand predicate.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = RestoreAfterCold(g, r); Continue(g);
        FinishFrame(g, juece.Id);
        Require(F<ProgramSkillResolvedEvent>(g).Single(e => e.FrameId == juece.Id).Completed &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == juece.Id),
            "The original Ending damage and both real child windows return through their same owning candidate exactly once."); Cold(g, r);

        var (native, nr) = Create(); Play(native); GiveTrick(native, 0);
        var cost = V(native, 0).Hand.First(c => c.Kind == CardKind.Dismantlement).Id;
        var targetCount = V(native, 1).HandCount + V(native, 1).Equipment.Count;
        var hasTrick = V(native, 1).Hand.Any(c => c.Kind == CardKind.Dismantlement);
        StartMieji(native, cost, 1); Reach(native, IsSequential); Require(P(native)!.PlayerSeat == 1, "The forced target owns its actual private native-AI prompt.");
        var frameId = SequentialRoot(native).Id; native = RestoreAfterCold(native, nr); PlayNative(native);
        var branch = F<ProgramSequentialDiscardBranchEvent>(native).Single(e => e.FrameId == frameId);
        Require(branch.PrimaryBranch == hasTrick && Moves(native, MiejiReason).Length == (hasTrick ? 1 : Math.Min(2, targetCount)) &&
            F<ProgramSequentialDiscardFinishedEvent>(native).Single(e => e.FrameId == frameId).RemainingUnpaid == 0,
            "Native forced choice uses only its own actual materials, pays the selected branch once and returns to the real human play."); Cold(native, nr);
    }

    private static (GameEngine, ContentRegistry) Create(bool observers = false, bool replenish = false, bool removeSource = false, bool fatalCost = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(observers, replenish, removeSource, fatalCost));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:lr-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void GiveTrick(GameEngine g, int target)
    {
        Use(g, "search"); Reach(g, p => Action(p, "declared-deck-criterion")); Answer(g, c => c.Parameters.GetValueOrDefault("criterion") == "red");
        Reach(g, p => Action(p, "declared-deck-recipient")); Answer(g, c => c.Targets.SequenceEqual([target])); Play(g);
    }
    private static void StartMieji(GameEngine g, int card, int target) => Accept(g,
        new UseProgramSkillCommand(0, Mieji, "topdeck-trick", [card], [target], g.Revision, P(g)!.PromptId));
    private static void Clear(GameEngine g, int seat)
    {
        if (V(g, seat).HandCount + V(g, seat).Equipment.Count == 0) return;
        Use(g, "clear", [seat]); Reach(g, p => Action(p, "select-owned-cards"));
        while (P(g) is { } p && Action(p, "select-owned-cards")) Answer(g, c => c.Cards.Count == 1);
        Play(g);
    }
    private static void SetHand(GameEngine g, int seat, int count) { Clear(g, seat); for (var i = 0; i < count; i++) { Use(g, "draw", [seat]); Play(g); } }
    private static ProgramSkillFrame SequentialRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SequentialDiscard is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static bool IsSequential(PendingDecision p) => Action(p, "sequential-discard");
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void PlayNative(GameEngine g)
    {
        for (var i = 0; i < 80; i++) { if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return; Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        throw new InvalidOperationException("The native forced choice did not return its original play.");
    }
    private static void FinishFrame(GameEngine g, long id)
    {
        for (var i = 0; i < 80; i++) { if (g.ResolutionStack.All(f => f.Id != id)) return; Advance(g); }
        throw new InvalidOperationException("The original real program did not finish its typed child return.");
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 180; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Li Ru command boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack,
            Events = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is not null && Action(p, "select-owned-cards")) Answer(g, c => c.Cards.Count == 1);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "All four prepared views, immutable private selections, typed parents, actual ledgers and command histories cold-restore identically."); return restored;
    }
    private static void Cold(GameEngine g, ContentRegistry r) => _ = RestoreAfterCold(g, r);
    private static void FrozenDiscardCollections(ProgramSequentialDiscardDraft actual)
    {
        void Frozen<T>(IReadOnlyList<T> values)
        {
            Require(values is System.Collections.Generic.IList<T> { IsReadOnly: true }, "The recovered receipt exposes a frozen collection, including an empty selection.");
            var rejected = false;
            try { ((System.Collections.Generic.IList<T>)values).Add(default!); } catch (NotSupportedException) { rejected = true; }
            Require(rejected, "An observer cannot append an entry to the recovered receipt.");
            if (values.Count == 0) return;
            rejected = false;
            try { ((System.Collections.Generic.IList<T>)values)[0] = default!; } catch (NotSupportedException) { rejected = true; }
            Require(rejected, "An observer cannot rewrite an existing recovered receipt entry.");
        }
        void All(ProgramSequentialDiscardDraft value)
        {
            Frozen(value.Order); Frozen(value.SelectedCardIds); Frozen(value.SelectedLocations);
            Frozen(value.Payment!.CardIds); Frozen(value.Payment.SourceLocations);
        }
        All(actual);
        var restored = JsonSerializer.Deserialize<ProgramSequentialDiscardDraft>(JsonSerializer.Serialize(actual))!;
        All(restored);
        var paid = actual.Payment!; var ids = paid.CardIds.ToArray(); var places = paid.SourceLocations.ToArray();
        var order = actual.Order.ToArray(); var selected = actual.SelectedCardIds.ToArray(); var selectedPlaces = actual.SelectedLocations.ToArray();
        var constructedPayment = new ProgramSequentialDiscardPayment(paid.ChooserSeat, paid.Cursor, ids, places,
            paid.SequenceBefore, paid.SequenceAfter, paid.ActualCount);
        var constructed = new ProgramSequentialDiscardDraft(actual.Kind, actual.InstructionIndex, actual.Source, actual.GameplayHash,
            actual.ActualTurnNumber, actual.ActualTurnOwnerSeat, actual.StartSeat, order, actual.Cursor, actual.ChooserSeat,
            actual.PreviousCount, actual.Remaining, actual.PrimaryBranch, actual.Stage, selected, selectedPlaces, constructedPayment);
        var assigned = actual with { Order = order, SelectedCardIds = selected, SelectedLocations = selectedPlaces,
            Payment = paid with { CardIds = ids, SourceLocations = places } };
        ids[0] = int.MinValue; places[0] = CardLocation.DrawPile; order[0] = int.MinValue;
        if (selected.Length > 0) selected[0] = int.MinValue;
        if (selectedPlaces.Length > 0) selectedPlaces[0] = CardLocation.DrawPile;
        foreach (var value in new[] { constructed, assigned })
        {
            All(value);
            Require(value.Order.SequenceEqual(actual.Order) && value.SelectedCardIds.SequenceEqual(actual.SelectedCardIds) &&
                value.SelectedLocations.SequenceEqual(actual.SelectedLocations) && value.Payment!.CardIds.SequenceEqual(paid.CardIds) &&
                value.Payment.SourceLocations.SequenceEqual(paid.SourceLocations),
                "Constructor and init/with setters clone caller arrays without sharing private paid state.");
        }
    }
    private static void Private(GameEngine g, int chooser)
    {
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(seat).PendingDecision is null, "A foreign observer sees no private discard choices or selected IDs.");
        var p = g.CreateSnapshot(chooser).PendingDecision!;
        Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }),
            "Prepared outer and nested private choices are immutable.");
    }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished discard choice cannot mutate selection or pay any cost."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool observers, bool replenish, bool removeSource, bool fatalCost) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-li-ru", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:lr-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"clear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand","equipment"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true}]},
                    {"id":"draw","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                    {"id":"search","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"declareDeckCriterionAndGiveMatchingCard","target":"owner"}]}]},
                  {"id":"fixture:lr-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:lr-top","revision":1,"triggers":[{"id":"top","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:mieji-current.MoveBoundCards"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:mieji-current.ChooseCategoryOrSequentialDiscard","skill-program.boundary:fencheng-current.EscalatingDiscardOrDamageFromSelected"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:lr-cost.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-damage","revision":1,"triggers":[{"id":"damage","window":"afterDamageApplied","subject":"owner","optional":false,"damageOccurrence":"perDamage","effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
                  {"id":"fixture:lr-dying","revision":1,"triggers":[{"id":"dying","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (replenish) ((JsonArray)rules["skills"]![3]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""));
            if (fatalCost) ((JsonArray)rules["skills"]![3]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":8}"""));
            if (removeSource) ((JsonArray)rules["skills"]![2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:mieji-current"],"sourceBind":"fixture:lr-noop"}"""));
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "小固定夹具" }, ["fixture:lr-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                  [Top] = Pause("置顶孩子"), [Cost] = Pause("弃牌孩子"), [Hp] = Pause("真实回复"), [Gain] = Pause("真实得牌"), [Damage] = Pause("真实火伤"), [Dying] = Pause("真实濒死入口") } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:lr-quiet", Top, Cost, Hp, Gain, Damage, Dying }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:lr-noop", "已替换来源", "无运行能力"));
            b.AddSkill(new("fixture:lr-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var ownerSkills = new List<string> { Fencheng, Juece, Driver }; if (observers) ownerSkills.AddRange([Top, Cost, Hp, Gain, Damage, Dying]);
            b.AddGeneral(new("fixture:lr-owner", "界李儒机制", "supporter", Mieji, "qun", 3, ownerSkills, Gender: GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:lr-target-{i}", "固定目标", "supporter", "fixture:lr-selection", "qun", 6,
                observers ? ["fixture:lr-quiet", Cost, Hp, Gain, Damage, Dying] : ["fixture:lr-quiet"], Gender: GeneralGender.Male));
            b.AddDeck(new("fixture:lr-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i =>
                new ContentDeckPhysicalCard(i % 2 == 0 ? "classic:silver-lion" : "standard:dismantlement", i % 2 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界李儒", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lr-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:lr-owner", "fixture:lr-target-1", "fixture:lr-target-2", "fixture:lr-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
