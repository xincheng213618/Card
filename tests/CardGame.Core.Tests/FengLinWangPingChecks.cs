using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinWangPingChecks
{
    public static void PaidHandDemandAndFrozenQualification()
    {
        var (game, registry) = Start("normal");
        var target = Other(game); Driver(game, "boost", [target]);
        var before = Snapshot(game).Players;
        Require(before[target].HandCount == before[0].HandCount, "Equal hands must become strictly greater only after real payment.");
        var cost = Hand(game, 0).First();
        Reject(game, new UseProgramSkillCommand(0, "classic:feijun", "relative-zone-demand", [cost], [target], game.Revision, P(game)!.PromptId));
        Reject(game, new UseProgramSkillCommand(0, "classic:feijun", "relative-zone-demand", [cost, cost], [], game.Revision, P(game)!.PromptId));
        Use(game, cost); Reach(game, p => IsTargetChoice(p));
        Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, new ChoiceId("relative-zone.unpublished-owner"), game.Revision));
        Require(Zones(game).Single(z => z.CardId == cost).Location == CardLocation.DiscardPile &&
            P(game)!.Choices.Any(c => c.Targets.SequenceEqual([target]) && c.Parameters["mode"] == "hand"), "Target comparison follows paid discard.");
        Replay(game, registry); Choose(game, c => c.Targets.SequenceEqual([target]) && c.Parameters["mode"] == "hand");
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:reward-pause");
        var commit = Declarations(game).Single();
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "classic:feijun");
        var reward = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "classic:binglue");
        var window = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.ProgramTarget is not null);
        Require(commit.FirstEncounter && commit.ParentProgramFrameId == parent.Id && commit.DeclarationOrdinal == 1 &&
            window.ResumeProgramFrameId == parent.Id && reward.WindowContext!.ParentFrameId == window.Id &&
            reward.WindowContext.ProgramTarget == commit && parent.SelectedTargetSeats.SequenceEqual([target]) &&
            !game.ResolutionStack.OfType<CardUseFrame>().Any() && RewardDraws(game) == 2,
            "First encounter is an exact program parent/window/reward chain, never a card use.");
        Require(Snapshot(game).Players[0].HandCount > Snapshot(game).Players[target].HandCount,
            "Real reward draw must reverse current hand comparison so later give proves frozen qualification.");
        Replay(game, registry); Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Any(m => m.From.OwnerSeat == target && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.classic:feijun.MoveBoundCards") &&
            game.GetHumanLegalActions().All(a => a.ProgramSkillId != "classic:feijun"), "AI target gives its own real HE entity after reward despite reversed counts; phase entry spent.");
        Replay(game, registry); Conserve(game);
    }

    public static void PaymentNestedComparisonAndEmptyDemand()
    {
        foreach (var mode in new[] { "empty", "cost-draw", "cost-claim", "cost-death" })
        {
            var (game, registry) = Start(mode);
            if (mode != "empty") Driver(game, "boost", [Other(game)]);
            var cost = Hand(game, 0).First(); Use(game, cost);
            if (mode != "empty")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == "fixture:cost-pause"); Replay(game, registry);
                Require(!Declarations(game).Any(), "Actual discard child must finish before any target declaration.");

                Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            if (mode == "cost-claim")
            {
                Reach(game, IsTargetChoice);
                Require(Zones(game).Single(z => z.CardId == cost).Location.OwnerSeat is > 0, "Another living observer really claimed the paid discard before comparison.");
                Replay(game, registry); Choose(game, c => c.Parameters["mode"] == "hand");
                Reach(game, p => p.SkillPrompt?.SkillId == "fixture:reward-pause");
                Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
                Require(Declarations(game).Length == 1 && RewardDraws(game) == 2, "Claimed payment remains paid; declaration compares new live counts and proceeds without stale cost ownership.");
                Replay(game, registry); Conserve(game); continue;
            }
            if (mode == "cost-death")
            {
                for (var i = 0; i < 45 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:feijun"); i++) Tick(game);
                Require(!Snapshot(game).Players[0].IsAlive, "Real payment child death cancels source without a target.");
            }
            else
            {
                Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
                Require(game.GetHumanLegalActions().All(a => a.ProgramSkillId != "classic:feijun"), "No eligible target consumes real discard and phase entry.");
            }
            Require(!Declarations(game).Any() && RewardDraws(game) == 0 && game.CardMovements.Any(m => m.CardId == cost && m.To == CardLocation.DiscardPile),
                "Post-child counts (including returned payment) decide strict greater; no eligible target grants no reward or refund.");
            Replay(game, registry); Conserve(game);
        }
    }

    public static void EquipmentPaymentAndTargetPrivateDiscard()
    {
        var (game, registry) = Start("equipment");
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, P(game)!.PromptId));
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
        NextPlay(game);
        var target = Snapshot(game).Players.First(p => p.Seat != 0 && Equipment(game, p.Seat).Count > 0).Seat;
        var paid = Equipment(game, 0).Single(); var discarded = Equipment(game, target).Single();
        Require(Equipment(game, 0).Count == Equipment(game, target).Count, "Equal equipment only becomes eligible after equipment payment.");
        Use(game, paid); Reach(game, IsTargetChoice);
        Choose(game, c => c.Targets.SequenceEqual([target]) && c.Parameters["mode"] == "equipment");
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:reward-pause"); Replay(game, registry);
        Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(game.CardMovements.Any(m => m.CardId == paid && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) &&
            game.CardMovements.Any(m => m.CardId == discarded && m.From == CardLocation.Equipment(target) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.classic:feijun.MoveBoundCards"),
            "Both actual equipment payments and target-owned discard resolve through normal movement.");
        Replay(game, registry); Conserve(game);
    }

    public static void FirstTargetHistoryAcrossSkillLossAndIndependentReward()
    {
        var (game, registry) = Start("history"); var target = Other(game);
        BoostToComparable(game, target); Demand(game, registry, target);
        var originalInstance = Declarations(game).Single().SourceSkillInstanceId;
        Require(RewardDraws(game) == 2, "First target draws exactly two.");
        Driver(game, "lose-source"); Driver(game, "regain-source");
        NextPlay(game); BoostToComparable(game, target); Demand(game, registry, target);
        var repeated = Declarations(game).Last();
        Require(repeated.SourceSkillInstanceId != originalInstance && !repeated.FirstEncounter && RewardDraws(game) == 2,
            "Real source loss/regrant changes grant identity but retains whole-game target history.");
        Driver(game, "lose-reward"); NextPlay(game);
        var second = Snapshot(game).Players.First(p => p.IsAlive && p.Seat != 0 && p.Seat != target).Seat;
        BoostToComparable(game, second); Demand(game, registry, second);
        Require(Declarations(game).Last().FirstEncounter && RewardDraws(game) == 2, "Absent independent Binglue grant cannot reward a first declaration.");
        Driver(game, "regain-reward"); NextPlay(game); BoostToComparable(game, second); Demand(game, registry, second);
        Require(!Declarations(game).Last().FirstEncounter && RewardDraws(game) == 2, "Reward regrant does not turn an already declared target into a first encounter.");
        var third = Snapshot(game).Players.Single(p => p.Seat != 0 && p.Seat != target && p.Seat != second).Seat;
        NextPlay(game); BoostToComparable(game, third); Demand(game, registry, third);
        Require(Declarations(game).Select(d => d.DeclarationOrdinal).SequenceEqual(Enumerable.Range(1, 5)) && RewardDraws(game) == 4,
            "Another genuinely first target grants its own two real draws; declaration ordinals remain strict across grants: " + JsonSerializer.Serialize(Declarations(game)) + "; draws=" + RewardDraws(game));
        Replay(game, registry); Conserve(game, 80);
    }

    public static void RewardSuppressionAndDeathContinuation()
    {
        foreach (var mode in new[] { "commit-suppression", "reward-death", "target-death" })
        {
            var (game, registry) = Start(mode); var target = Other(game); Driver(game, "boost", [target]);
            Use(game, Hand(game, 0).First()); Reach(game, IsTargetChoice);
            Choose(game, c => c.Targets.SequenceEqual([target]) && c.Parameters["mode"] == "hand");
            if (mode == "target-death")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == "fixture:reward-boundary" && p.Choices.Any(c => c.Targets.SequenceEqual([target])));
                Replay(game, registry); Choose(game, c => c.Targets.SequenceEqual([target]));
            }
            if (mode == "commit-suppression")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == "fixture:commit-pause"); Replay(game, registry);
                Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            for (var i = 0; i < 55 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:feijun"); i++) Tick(game);
            Require(Declarations(game).Single().FirstEncounter && !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "classic:feijun") &&
                !game.CardMovements.Any(m => m.From.OwnerSeat == target && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.classic:feijun.MoveBoundCards"),
                "Declaration remains recorded while suppression/source death/target death cancels the invalid continuation.");
            if (mode == "commit-suppression") Require(RewardDraws(game) == 0 && Snapshot(game).Players[0].IsAlive && Snapshot(game).Players[0].Hp == 19, "Independent Binglue is suppressed after declaration, before its own trigger.");
            else Require(RewardDraws(game) == 2 && !Snapshot(game).Players[mode == "reward-death" ? 0 : target].IsAlive, "Real reward-gain child death waits and cancels the corresponding live continuation.");
            Replay(game, registry); Conserve(game);
        }
    }

    public static void NativeAiAndTargetPrivacyReplay()
    {
        var (game, registry) = Start("ai", false); Driver(game, "supply");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.ProgramTrigger && p.IsPrivate && p.SkillPrompt?.SkillId == "classic:feijun");
        var commit = Declarations(game).Last(); var source = commit.OwnerSeat;
        Require(source != 0 && commit.TargetSeat == 0 && RewardDraws(game) == 2 && P(game)!.Choices.Any(c => c.Cards.Count == 1), "Native AI really pays, declares and rewards, then target privately chooses its own HE.");
        var targetPrompt = game.CreateSnapshot(0, false).PendingDecision!;
        for (var seat = 1; seat < 4; seat++)
        {
            var observer = game.CreateSnapshot(seat, false);
            Require(observer.PendingDecision is null && observer.Players[0].Hand.Count == 0, "Owner and unrelated observers cannot read another seat's private HE choice.");
        }
        Reject(game, new AnswerPromptCommand(source, targetPrompt.PromptId, targetPrompt.Choices[0].Id, game.Revision));
        Replay(game, registry);
        var given = targetPrompt.Choices.First(c => c.Cards.Count == 1).Cards.Single(); Choose(game, c => c.Cards.SequenceEqual([given]));
        for (var i = 0; i < 8 && P(game)?.SkillPrompt?.SkillId == "classic:feijun"; i++) Tick(game);
        Require(game.CardMovements.Any(m => m.CardId == given && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(source)) &&
            !game.AcceptedCommands.OfType<UseProgramSkillCommand>().Any(c => c.SkillId == "classic:feijun") &&
            !game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.ActorSeat != 0), "AI uses only ordinary AdvanceOneStep; private target alone submits its real give choice.");
        Replay(game, registry); Conserve(game);
    }

    public static void GivenCardNestedMovementAndReplay()
    {
        var (game, registry) = Start("gift-removal"); var target = Other(game); Driver(game, "boost", [target]);
        Use(game, Hand(game, 0).First()); Reach(game, IsTargetChoice); Choose(game, c => c.Targets.SequenceEqual([target]) && c.Parameters["mode"] == "hand");
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:given-boundary" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        var transfer = game.CardMovements.Last(m => m.From.OwnerSeat == target && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.classic:feijun.MoveBoundCards");
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "classic:feijun");
        var gained = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "fixture:given-boundary");
        Require(parent.PendingMovementContinuation is { SubjectSeat: 0 } && parent.CardSetBindings.Single(b => b.Name == "given").CardIds.SequenceEqual([transfer.CardId]) &&
            parent.CardSetBindings.Single(b => b.Name == "given").SourceLocations.SequenceEqual([transfer.From]) && Snapshot(game).ProcessingCardCount == 0 &&
            gained.WindowContext!.Window == SkillProgramTriggerWindow.CardsGained && gained.WindowContext.MovementBatch!.ParentFrameId == parent.Id &&
            gained.WindowContext.MovementBatch.OriginSkillId == parent.SkillId && gained.WindowContext.MovementBatch.OriginSkillInstanceId == parent.SkillInstanceId &&
            gained.WindowContext.MovementBatch.OriginOwnerSeat == parent.OwnerSeat,
            "OwnerHand await keeps the exact source-owner card bind and creates a true CardsGained child under the paying activation frame.");
        Replay(game, registry);
        Choose(game, c => c.Cards.SequenceEqual([transfer.CardId]));
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:given-boundary" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        Require(Zones(game).Single(z => z.CardId == transfer.CardId).Location == CardLocation.DiscardPile &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == parent.Id && f.PendingMovementContinuation is not null),
            "Actual gained child moves the same transferred entity again while the parent still awaits; no Processing cost is fabricated.");
        Replay(game, registry); Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == parent.Id) &&
            Zones(game).Single(z => z.CardId == transfer.CardId).Location == CardLocation.DiscardPile && RewardDraws(game) == 2,
            "Parent resumes once without regaining, re-discarding or requiring the original gifted entity to remain in hand.");
        Replay(game, registry); Conserve(game);
    }

    public static void ResourceContracts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Read(string suffix) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("classic-wang-ping.rules.json"); var presentation = Read("classic-wang-ping.presentation.json"); SkillProgramCatalog.Load(rules, presentation);
        foreach (var invalid in new[] { rules.Replace("\"minCards\": 1", "\"minCards\": 0"), rules.Replace("\"usesPerPhase\": 1", "\"usesPerPhase\": 2"), rules.Replace("\"awaitMovementTriggers\": true", "\"awaitMovementTriggers\": false"), rules.Replace("\"discardPile\"", "\"ownerHand\""), rules.Replace("\"programTargetCommitted\"", "\"cardUseCommitted\""), rules.Replace("\"sourceActivationId\": \"relative-zone-demand\"", "\"sourceActivationId\": \"missing\""), rules.Replace("\"sourceProgramId\": \"classic:feijun\"", "\"sourceProgramId\": \"missing\"") })
        { var rejected = false; try { SkillProgramCatalog.Load(invalid, presentation); } catch (InvalidOperationException) { rejected = true; } Require(rejected, "New descriptors require real HE paid-discard order, awaited movement, one phase entry, program window and configured exact declaration source."); }
    }

    private static (GameEngine, ContentRegistry) Start(string mode, bool humanOwner = true)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(mode));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-relative-zone-check", UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, humanOwner ? "fixture:relative-owner" : "fixture:relative-plain", game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); return (game, registry);
    }
    private static IReadOnlyList<CardZoneDiagnostic> Zones(GameEngine g) => g.CreateCardZoneDiagnostics();
    private static GameSnapshot Snapshot(GameEngine g) => g.CreateSnapshot(0, true);
    private static IReadOnlyList<int> Hand(GameEngine g, int seat) => Zones(g).Where(z => z.Location == CardLocation.Hand(seat)).Select(z => z.CardId).ToArray();
    private static IReadOnlyList<int> Equipment(GameEngine g, int seat) => Zones(g).Where(z => z.Location == CardLocation.Equipment(seat)).Select(z => z.CardId).ToArray();
    private static int Other(GameEngine g) => Snapshot(g).Players.First(p => p.Seat != 0).Seat;
    private static ProgramTargetCommitContext[] Declarations(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramTargetCommittedEvent>().Select(e => e.Declaration).ToArray();
    private static int RewardDraws(GameEngine g) => g.CardMovements.Count(m => m.Reason.Value == "program.first-target.reward");
    private static PendingDecision? P(GameEngine g) => g.PendingDecision ?? Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsTargetChoice(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "relative-zone-target");
    private static void Driver(GameEngine g, string id, int[]? targets = null) { Accept(g, new UseProgramSkillCommand(0, "fixture:relative-driver", id, [], targets ?? [], g.Revision, P(g)!.PromptId)); Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0); }
    private static void BoostToComparable(GameEngine g, int target)
    { for (var i = 0; i < 10 && Snapshot(g).Players[target].HandCount < Snapshot(g).Players[0].HandCount; i++) Driver(g, "boost", [target]); Require(Snapshot(g).Players[target].HandCount >= Snapshot(g).Players[0].HandCount, "Small fixture supplies only the live hand-count shortfall."); }
    private static void Use(GameEngine g, int card) => Accept(g, new UseProgramSkillCommand(0, "classic:feijun", "relative-zone-demand", [card], [], g.Revision, P(g)!.PromptId));
    private static void Choose(GameEngine g, Func<PromptChoice, bool> predicate)
    { var prompt = P(g)!; var chosen = prompt.Choices.FirstOrDefault(predicate) ?? throw new InvalidOperationException("Required published choice missing: " + JsonSerializer.Serialize(prompt.Choices) + "; counts=" + string.Join(",", Snapshot(g).Players.Select(p => p.HandCount))); Accept(g, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, chosen.Id, g.Revision)); }
    private static void NextPlay(GameEngine g) { var turn = Snapshot(g).TurnNumber; Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => Snapshot(g).TurnNumber > turn && p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard); }
    private static void Demand(GameEngine g, ContentRegistry r, int target)
    {
        var old = Declarations(g).Length; Use(g, Hand(g, 0).First()); Reach(g, IsTargetChoice); Choose(g, c => c.Targets.SequenceEqual([target]) && c.Parameters["mode"] == "hand");
        Reach(g, p => p.PlayerSeat == 0 && (p.Kind == DecisionKind.PlayCard || p.SkillPrompt?.SkillId == "fixture:reward-pause"));
        if (P(g)!.Kind != DecisionKind.PlayCard) { Replay(g, r); Choose(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard); }
        Require(Declarations(g).Length == old + 1, "Each actual demand adds one declaration.");
    }
    private static void Tick(GameEngine g)
    {
        if (P(g) is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard) Accept(g, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, g.Revision));
        else if (P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 }) Choose(g, c => c.Cards.Count == 0);
        else if (P(g) is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 }) Choose(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue" || c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards" || c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var i = 0; i < 140; i++) { if (P(g) is { } p && predicate(p)) return; Tick(g); } throw new InvalidOperationException("Missing fixed-seed boundary: " + P(g) + "; generals=" + string.Join(",", Snapshot(g).Players.Select(p => p.GeneralId)) + "; AI=" + JsonSerializer.Serialize(g.AiThoughts.TakeLast(4)) + "; declarations=" + Declarations(g).Length + "; bindings=" + string.Join(",", g.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Select(b => b.SkillId + ":" + b.Window + ":" + b.Activated + ":" + b.Completed))); }
    private static void Replay(GameEngine g, ContentRegistry registry)
    {
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())); var restored = GameReplay.Restore(checkpoint, registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat, false)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat, false))) &&
            g.CardMovements.SequenceEqual(restored.CardMovements) && g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(restored.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))), "All observer snapshots, ordered entity movement, events and RNG replay from accepted command JSON.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, "Rejected fixed fixture: " + result.Error?.Message); }
    private static void Reject(GameEngine g, GameCommand command) { var before = GameCheckpointJson.Serialize(g.CreateCheckpoint()); Require(!g.Submit(command).Accepted && before == GameCheckpointJson.Serialize(g.CreateCheckpoint()), "Unpublished or wrong actor input rejects atomically."); }
    private static void Conserve(GameEngine g, int count = 64) => Require(Zones(g).Count == count && Zones(g).Select(z => z.CardId).Distinct().Count() == count, "All physical fixture entities retain one location.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wangping", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            object Act(string id, object[] effects, bool targeted = false) => new { id, minCards = 0, maxCards = 0, minTargets = targeted ? 1 : 0, maxTargets = targeted ? 1 : 0, targetKind = "otherLiving", usesPerTurn = (int?)null, effects };
            var programs = new List<object> { new { id = "fixture:relative-driver", revision = 1, activations = new[] {
                Act("boost", [new { op = "draw", target = "selectedTarget", amount = 2 }], true),
                Act("large-boost", [new { op = "draw", target = "selectedTarget", amount = 8 }], true),
                Act("supply", [new { op = "draw", target = "owner", amount = 8 }]),
                Act("lose-source", [new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { "classic:feijun" }, sourceBind = "standard:none" }]),
                Act("regain-source", [new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { "standard:none" }, sourceBind = "classic:feijun" }]),
                Act("lose-reward", [new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { "classic:binglue" }, sourceBind = "standard:none" }]),
                Act("regain-reward", [new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { "standard:none" }, sourceBind = "classic:binglue" }]) } } };
            object Pause(string bind) => new { op = "chooseOption", target = "owner", resultBind = bind, options = new[] { new { id = "continue" } } };
            programs.Add(new { id = "fixture:reward-pause", revision = 1, triggers = new[] { new { id = "gain", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { "program.first-target.reward" }, optional = false, effects = new[] { Pause("reward") } } } });
            if (mode is "cost-draw" or "cost-claim" or "cost-death")
                programs.Add(new { id = "fixture:cost-pause", revision = 1, triggers = new[] { new { id = "cost", window = "discardPileReceived", subject = "owner", discardOwnerScope = "own", movementOccurrence = "perCard", movementReasons = new[] { "skill-program.classic:feijun.MoveBoundCards" }, optional = false, effects = mode == "cost-death" ? new[] { Pause("cost"), new { op = "loseHp", target = "owner", amount = 20 } } : new[] { mode == "cost-claim" ? Pause("cost-child") : new { op = "draw", target = "owner", amount = 2 }, Pause("cost") } } } });
            if (mode == "gift-removal")
                programs.Add(new { id = "fixture:given-boundary", revision = 1, triggers = new[] { new { id = "given", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { "skill-program.classic:feijun.MoveBoundCards" }, optional = false, effects = new object[] { new { op = "selectOwnedCards", target = "owner", amount = 1, zones = new[] { "hand" }, resultBind = "removed" }, new { op = "moveBoundCards", target = "owner", sourceBind = "removed", destination = "discardPile", awaitMovementTriggers = true }, Pause("given") } } } });
            if (mode == "cost-claim")
                programs.Add(new { id = "fixture:other-claim", revision = 1, triggers = new[] { new { id = "claim", window = "discardPileReceived", subject = "owner", discardOwnerScope = "other", movementOccurrence = "perCard", movementReasons = new[] { "skill-program.classic:feijun.MoveBoundCards" }, optional = false, effects = new[] { new { op = "claimMovedCards", target = "owner" } } } } });
            if (mode == "commit-suppression")
                programs.Add(new { id = "fixture:commit-pause", revision = 1, triggers = new[] { new { id = "commit", window = "programTargetCommitted", subject = "owner", priority = 100, optional = false, effects = new[] { Pause("commit"), new { op = "loseHp", target = "owner", amount = 2 } } } } });
            if (mode is "reward-death" or "target-death")
                programs.Add(new { id = "fixture:reward-boundary", revision = 1, triggers = new[] { new { id = "gain", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { "program.first-target.reward" }, optional = false, priority = 100, usageScope = "game", usageLimit = 1, effects = mode == "reward-death" ? new object[] { new { op = "loseHp", target = "owner", amount = 20 } } : new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "loseHp", target = "selectedTarget", amount = 20 } } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }), JsonSerializer.Serialize(new { schemaVersion = 3, skills = programs.Select(p => JsonSerializer.SerializeToElement(p).GetProperty("id").GetString()!).ToDictionary(id => id, id => new { name = id, description = "真实选择与子链检查", optionLabels = id is "fixture:relative-driver" or "fixture:reward-boundary" or "fixture:other-claim" ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } }) }));
            foreach (var entry in catalog.Programs) builder.AddSkill(new(entry.Key, entry.Key, entry.Key) { Program = entry.Value });
            builder.AddSkill(new("fixture:hp-suppression", "体力压制", "体力19时压制其他技能") { SuppressionRule = new(19) });
            var extra = new List<string> { "classic:binglue", "fixture:reward-pause" };
            if (mode != "ai") extra.Add("fixture:relative-driver");
            if (mode == "gift-removal") extra.Add("fixture:given-boundary");
            if (mode is "cost-draw" or "cost-claim" or "cost-death") extra.Add("fixture:cost-pause");
            if (mode == "commit-suppression") extra.AddRange(["fixture:commit-pause", "fixture:hp-suppression"]);
            if (mode is "reward-death" or "target-death") extra.Add("fixture:reward-boundary");
            builder.AddGeneral(new("fixture:relative-owner", "飞军机制将", "supporter", "classic:feijun", "shu", mode is "cost-death" or "reward-death" ? 19 : 20, extra, GeneralGender.Male));
            builder.AddGeneral(new("fixture:relative-plain", "提供者", "supporter", mode == "ai" ? "fixture:relative-driver" : "standard:none", "wei", 20, mode == "cost-claim" ? ["fixture:other-claim"] : [], GeneralGender.Male));
            for (var seat = 2; seat < 4; seat++) builder.AddGeneral(new($"fixture:relative-{seat}", "目标", "supporter", "standard:none", "wei", 20, mode == "cost-claim" ? ["fixture:other-claim"] : [], GeneralGender.Male));
            builder.AddDeck(new("fixture:relative-deck", "实体小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, mode == "history" ? 80 : 64).Select(i => new ContentDeckPhysicalCard(mode == "equipment" && i % 2 == 0 ? "standard:qinggang_sword" : "standard:dodge", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-relative-zone-check", "目标声明机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:relative-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:relative-owner", "fixture:relative-plain", "fixture:relative-2", "fixture:relative-3"]));
        }
    }
}
