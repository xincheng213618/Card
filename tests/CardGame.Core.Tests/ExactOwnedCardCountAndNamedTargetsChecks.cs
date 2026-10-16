using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ExactOwnedCardCountAndNamedTargetsChecks
{
    private const string Skill = "fixture:exact-count";
    private const string Cost = "fixture:exact-count-cost-child";
    private const string Hp = "fixture:exact-count-hp-child";
    private const string Owner = "fixture:exact-count-owner";
    private const string Mode = "identity:classic-exact-count";
    private const string PaidReason = "skill-program.fixture:exact-count.MoveBoundCards";

    public static void ExactOwnedCountFreezesPaidCostThroughRecoveryAndNamedTargets()
    {
        var (game, registry) = Start(armor: true);
        var recipient = game.CreateSnapshot(0).Players.Single(p => p.Seat != 0 && p.Hp < p.MaxHp).Seat;
        var fullHpSeat = game.CreateSnapshot(0).Players.First(p => p.Seat != 0 && p.Hp == p.MaxHp).Seat;
        var selectedSeats = new[] { 0, recipient };
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        Require(hand.Count > 8 && hand.All(c => c.Kind == CardKind.SilverLion),
            "The fixed real deck provides more than eight private own-zone candidates without a truncated subset search.");
        var armor = hand[0].Id;
        Accept(game, new PlayCardCommand(0, armor, [], game.Revision, P(game)!.PromptId));
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(game.CreateSnapshot(0).Players[0] is { Hp: 3, MaxHp: 5 } owner && owner.Equipment.Single().Id == armor,
            "A real injured owner equips its actual Silver Lion before paying the exact two-card cost.");
        Use(game);
        Reach(game, p => IsOwned(p, "cost"));
        var parent = Parent(game);
        var parentId = parent.Id;
        var retained = game.CreateSnapshot(0);
        var retainedJson = SnapshotJson.Serialize(retained);
        Require(parent.InstructionIndex == 1 && parent.OwnedCardSelection is { RequiredCount: 2, AllowEarlyFinish: false } draft &&
                draft.CandidateCardIds.Count > 8 && draft.CandidateLocations.Contains(CardLocation.Equipment(0)) &&
                P(game)!.Choices.All(c => c.Cards.Count == 1),
            "The complete legal HE candidate set is private; exactly two cards are frozen before payment and no early finish is offered.");
        AssertPrivate(game, 0);
        RejectWrongActor(game);
        Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId,
            new ChoiceId($"program-owned-set.frame-{parentId}.finish-0"), game.Revision));
        game = Cold(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([armor]));
        var other = P(game)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
        Require(!game.CardMovements.Any(m => m.Reason.Value == PaidReason), "Choosing one card does not partly pay the exact cost.");
        game = Cold(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([other]));
        Require(SnapshotJson.Serialize(retained) == retainedJson, "A live private selection never mutates an earlier prepared snapshot.");
        Reach(game, p => IsContinue(p, Cost));
        parent = Parent(game);
        var paid = game.CardMovements.Where(m => m.Reason.Value == PaidReason).ToArray();
        var cost = parent.CardSetBindings.Single(b => b.Name == "cost");
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w =>
            w.Batch.ParentFrameId == parentId);
        var costChild = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Cost);
        Require(parent.InstructionIndex == 2 && parent.OwnedCardSelection is null &&
                parent.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0, CoverageResultBind: null } &&
                cost.Visibility == SkillProgramCardSetVisibility.Private &&
                cost.CardIds.SequenceEqual([armor, other]) && cost.SourceLocations.SequenceEqual([CardLocation.Equipment(0), CardLocation.Hand(0)]) &&
                cost.FrozenSelectedSuits!.SequenceEqual([Suit.Heart, Suit.Heart]) &&
                paid.Length == 2 && paid.All(m => m.To == CardLocation.DiscardPile) &&
                movement.Batch.ParentFrameId == parentId && movement.Batch.OriginOwnerSeat == 0 &&
                movement.Batch.AwaitingProgramFrameId is null && movement.ResumeProgramFrameId is null &&
                movement.Batch.OriginSkillId == Skill && movement.Batch.OriginSkillInstanceId == parent.SkillInstanceId &&
                costChild.OwnerSeat == 0 && costChild.WindowContext?.ParentFrameId == movement.Id &&
                costChild.WindowContext.MovementBatch?.Id == movement.Batch.Id &&
                movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(new[] { armor, other }.Order()),
            "Both actual HE entities pay in one exact-parent movement batch, whose child suspends the original committed move cursor once. " +
            JsonSerializer.Serialize(new { parent.InstructionIndex, parent.CardSetBindings, paid, movement.ResumeProgramFrameId, movement.Batch }));
        AssertFrozenSuits(cost!.FrozenSelectedSuits!);
        game = Cold(game, registry);
        Reach(game, p => IsOwned(p, "decoy"));
        Require(game.CreateSnapshot(0).Players[0].Hp == 4 &&
                Facts<SilverLionRemovedRecoveryEvent>(game).Count(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) == 1 &&
                game.CardMovements.Count(m => m.Reason.Value == PaidReason) == 2,
            "Discarding the real armor recovers the owner from three to four HP before targets; the paid X remains two.");
        var decoy = P(game)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
        Answer(game, c => c.Cards.SequenceEqual([decoy]));
        Reach(game, IsTargets);
        parent = Parent(game);
        Require(parent.NamedBoundTargetSelection is { FrozenBoundCardCount: 2, MaximumTargets: 2, MinimumTargets: 0, SourceBind: "cost" } &&
                parent.CardSetBindings.Last().Name == "decoy" && parent.CardSetBindings.Last().CardIds.Count == 1 &&
                parent.CardSetBindings.Single(b => b.Name == "cost").Visibility == SkillProgramCardSetVisibility.Public &&
                P(game)!.Choices.Any(c => c.Targets.Count == 0) && P(game)!.Choices.Any(c => c.Targets.SequenceEqual(selectedSeats)) &&
                P(game)!.Choices.Any(c => c.Targets.Contains(fullHpSeat)) && P(game)!.Choices.All(c => c.Targets.Count <= 2),
            "The named paid cost survives an unrelated later binding and armor recovery; self, full-HP living players and zero targets are legal, with a frozen two-target cap.");
        AssertPrivate(game, 0);
        RejectWrongActor(game);
        Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId,
            new ChoiceId($"program-named-targets.frame-{parentId}.seats-0-1-2"), game.Revision));
        game = Cold(game, registry);
        Answer(game, c => c.Targets.SequenceEqual(selectedSeats));
        Reach(game, p => IsContinue(p, Hp));
        Require(Parent(game).InstructionIndex == 6 && Parent(game).NamedBoundTargetSelection is null &&
                Parent(game).SelectedTargetSeats.SequenceEqual(selectedSeats) &&
                game.CardMovements.Count(m => m.Reason.Value == PaidReason) == 2,
            "The real selected-target recovery child suspends the same original program and cannot repay its committed cost.");
        game = Cold(game, registry);
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(Facts<RecoveryAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1 && e.RemainingHp == 5) == 1 &&
                Facts<RecoveryAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == recipient && e.Amount == 1) == 1 &&
                game.CreateSnapshot(0).Players[0].Hp == 5 && game.CreateSnapshot(0).Players[recipient].Hp == 3 &&
                !Facts<RecoveryAppliedEvent>(game).Any(e => e.TargetSeat == fullHpSeat) &&
                !Facts<ProgramSkillHpLostEvent>(game).Any(e => e.SkillId == Skill) &&
                Facts<ProgramCardsRevealedEvent>(game).Count(e => e.FrameId == parentId && e.Bind == "cost") == 1 &&
                game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == decoy),
            "Chosen self and other recover once; the unrelated binding is retained, full-HP unchosen targets remain unchanged and red paid cards cause no HP fee.");
        AssertCompleted(game, parentId, 2);
        _ = Cold(game, registry);
    }

    private static void AssertCompleted(GameEngine game, long id, int count) => Require(
        !game.ResolutionStack.Any(f => f.Id == id) &&
        Facts<ProgramSkillStartedEvent>(game).Count(e => e.FrameId == id && e.SkillId == Skill && e.ActivationId == "pay-and-recover") == 1 &&
        Facts<ProgramSkillResolvedEvent>(game).Count(e => e.FrameId == id && e.SkillId == Skill && e.Completed) == 1 &&
        Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Cost && e.Activated && e.Completed) == 1 &&
        game.CardMovements.Count(m => m.Reason.Value == PaidReason) == count &&
        game.CardMovements.Count(m => m.Reason.Value == "skill-program.fixture:exact-count.Draw" && m.To == CardLocation.Hand(0)) == 1 &&
        !game.CardMovements.Any(m => m.CardId <= 0) && !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill),
        "The real payment child, whole original program and draw tail return once; phase quota stays spent and every cost is physical.");
    private static void AssertFrozenSuits(IReadOnlyList<Suit> suits)
    {
        var frozen = false;
        try { ((IList<Suit>)suits)[0] = Suit.None; } catch (NotSupportedException) { frozen = true; }
        Require(frozen, "The aligned frozen suit list cannot be mutated after payment or cold recovery.");
    }
    private static ProgramSkillFrame Parent(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
    private static PendingDecision? P(GameEngine game) => game.CreateSnapshot(game.State.CurrentSeat).PendingDecision ??
        Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsOwned(PendingDecision p, string bind) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && c.Parameters.GetValueOrDefault("result-bind") == bind);
    private static bool IsTargets(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("source-bind") == "cost" && c.Parameters.GetValueOrDefault("program-action") == "select-targets");
    private static bool IsContinue(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static T[] Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Use(GameEngine game, bool accepted = true)
    {
        var command = new UseProgramSkillCommand(0, Skill, "pay-and-recover", [], [], game.Revision, P(game)!.PromptId);
        if (accepted) Accept(game, command); else Reject(game, command);
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate) => Accept(game,
        new AnswerPromptCommand(P(game)!.PlayerSeat, P(game)!.PromptId, P(game)!.Choices.First(predicate).Id, game.Revision));
    private static void RejectWrongActor(GameEngine game) => Reject(game,
        new AnswerPromptCommand(1, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    {
        for (var step = 0; step < 160; step++)
        {
            if (P(game) is { } p && condition(p)) return;
            if (P(game) is { PlayerSeat: 0 } human)
            {
                if (human.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                else throw new InvalidOperationException("Unexpected real human boundary: " + JsonSerializer.Serialize(human));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded exact-cost fixture did not reach its actual native boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void AssertPrivate(GameEngine game, int actor) => Require(P(game) is { IsPrivate: true } &&
        Enumerable.Range(0, 4).Where(s => s != actor).All(s => game.CreateSnapshot(s).PendingDecision is null && game.CreateSnapshot(s).Players[actor].Hand.Count == 0),
        "The exact-cost and target prompts remain private to their real choosing owner.");
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The real command was rejected.");
    }
    private static void Reject(GameEngine game, GameCommand command)
    {
        var before = State(game); var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(!result.Accepted && result.Error is not null && State(game) == before,
            "Invalid actor, early finish, oversized targets or insufficient payment reject without changing state, entity history or accepted commands.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(cold) == State(game), "Cold replay preserves the real private draft, frozen cost/suits, child return, named cap and entity journal exactly."); return cold;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Rules(int drawTail = 1) => $$$"""
    {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
      {"id":"{{{Skill}}}","revision":1,"activations":[{"id":"pay-and-recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[
        {"op":"selectOwnedCards","target":"owner","numberExpression":"ownerLostHp","zones":["hand","equipment"],"resultBind":"cost","requireExactCount":true,"freezeSelectedCardSuits":true},
        {"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile","awaitMovementTriggers":true},
        {"op":"revealBoundCards","target":"owner","sourceBind":"cost"},
        {"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"decoy"},
        {"op":"selectTargets","target":"owner","targetKind":"anyLiving","minimumTargets":0,"maximumTargets":8,"numberExpression":"boundCardCount","sourceBind":"cost","targetAiOrder":"supportRecovery"},
        {"op":"recover","target":"selectedTargets","amount":1},
        {"op":"loseHp","target":"owner","amount":1,"condition":{"kind":"not","children":[{"kind":"boundCardsMatchSuits","sourceBind":"cost","suits":["heart","diamond","none"]}]}},
        {"op":"draw","target":"owner","amount":{{{drawTail}}} }]}]},
      {"id":"{{{Cost}}}","revision":1,"triggers":[{"id":"actual-payment","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementDiscardOnly":true,"movementOccurrence":"perOwnerBatch","movementReasons":["{{{PaidReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-child","options":[{"id":"continue"}]}]}]}]}
    """;
    private static string Presentation() => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new[] { Skill, Cost, Hp }.ToDictionary(id => id, id => new { name = id, description = "真实精确费用与冻结具名目标", optionLabels = id == Skill ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } }) });
    private static (GameEngine, ContentRegistry) Start(bool armor = false, int initialCards = 13, Suit suit = Suit.Heart, bool hongyan = false, int initialHp = 2, bool nativeAi = false, bool registeredQuji = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(armor, initialCards, suit, hongyan, initialHp, nativeAi, registeredQuji));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId)); Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }); return (game, registry);
    }
    private sealed class Fixture(bool armor, int initialCards, Suit suit, bool hongyan, int initialHp, bool nativeAi, bool registeredQuji) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("exact-count-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules(nativeAi ? 8 : 1), Presentation());
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "真实精确费用共享机制") { Program = program, ProgramPresentation = catalog.Presentations[id],
                SelectionWeights = id == Skill ? Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) : null });
            builder.AddSkill(new("fixture:exact-count-peer", "固定选将", "真实安静参与者") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            builder.AddGeneral(new(Owner, "精确费用本人", "supporter", registeredQuji ? "ol:quji" : Skill, "wu", 4, hongyan ? [Cost, Hp, "classic:hongyan"] : [Cost, Hp]) { InitialHp = initialHp });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:exact-count-peer-{i}").ToArray();
            for (var i = 0; i < peers.Length; i++) builder.AddGeneral(new(peers[i], "实际回复目标", "supporter", nativeAi ? Skill : "fixture:exact-count-peer", "wu", 4,
                nativeAi ? [Cost, Hp] : [Hp]) { InitialHp = nativeAi || i == 0 ? 2 : null });
            builder.AddDeck(new("fixture:exact-count-deck", "固定完整手牌", initialCards, 0, []) { PhysicalCards = Enumerable.Range(0, 104).Select(_ => new ContentDeckPhysicalCard(armor ? "classic:silver-lion" : "standard:dodge", suit, 7)).ToArray() });
            builder.AddMode(new(Mode, "精确费用共享真实窗口", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:exact-count-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
