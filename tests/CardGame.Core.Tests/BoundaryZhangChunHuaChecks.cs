using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryZhangChunHuaChecks
{
    private const string Skill = "boundary:jianmie-current";
    private const string Binding = "simultaneous-color-discard-duel";
    private const string Cost = "fixture:dual-cost";
    private const string Hp = "fixture:dual-hp";
    private const string Gain = "fixture:dual-gain";
    private const string Before = "fixture:dual-before";
    private const string Reason = "skill-program.dual-color.discard";
    private const string Mode = "identity:classic-dual-color-fixture";

    public static void PrivateCommitmentsAtomicPaymentAndRealDuelCold()
    {
        var (game, registry) = Create();
        Require(game.State.Players[0].Hp == 2 && game.State.Players[0].MaxHp == 4 && Hand(game, 0).Count == 6,
            "The formal Lord fixture really begins wounded with six actual black Slashes.");
        var sourceCards = Hand(game, 0).Select(card => card.Id).ToArray();
        var otherCards = Hand(game, 1).Select(card => card.Id).ToArray();
        Begin(game, 1); var root = Paid(game);
        Require(root.DualColorDuel!.Stage == ProgramDualColorDuelStage.ChoosingOwner, "The real activation owns its first private commitment.");
        Private(game); RejectUnpublished(game); Cold(game, registry); AnswerColor(game, "black");
        Require(P(game) is { PlayerSeat: 1 } && Paid(game).DualColorDuel is { OwnerIsRed: false, OtherIsRed: null } &&
            Facts<ProgramDualColorsCommittedEvent>(game).Length == 0 && CostMoves(game).Length == 0,
            "The first color is retained privately, without public color facts or any early hand payment.");
        Private(game); Cold(game, registry);
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == Cost);
        var receipt = Paid(game).DualColorDuel!;
        Require(receipt.Stage == ProgramDualColorDuelStage.PaymentChildren && receipt.OwnerCardIds.SequenceEqual(sourceCards) &&
            receipt.OtherCardIds.Count == 0 && receipt.OtherIsRed == true && Hand(game, 0).Count == 0 && Hand(game, 1).Count == otherCards.Length,
            "The native opponent chooses from its own private hand; both sets commit before any payment observer.");
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(frame => frame.Batch.ParentFrameId == root.Id);
        Require(movement.ResumeProgramFrameId == root.Id && movement.Batch.AwaitingProgramFrameId is null &&
            movement.Batch.Movements.Count == sourceCards.Length && movement.Batch.Movements.All(move =>
                move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile && move.Reason.Value == Reason),
            "The paid mixed-owner batch has its exact direct Program return, even when the other set is empty.");
        Frozen(receipt.OwnerCardIds); Frozen(receipt.OtherCardIds); Cold(game, registry);
        Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Hp);
        Require(game.State.Players[0].Hp == 3 && Paid(game).DualColorDuel!.OwnerCardIds.Count == 6 &&
            game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(),
            "The real cost observer recovers the wounded source before the future Duel is issued.");
        Cold(game, registry); Continue(game);
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:shangshi" && Action(prompt, "activate"));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == Gain && PaidOrNull(game) is not null);
        Require(Hand(game, 0).Count == 1 && Paid(game).DualColorDuel!.OwnerCardIds.Count == 6 && CostMoves(game).Length == 6,
            "A real Shangshi draw and its gain observer return without recomputing the already paid six-card comparison.");
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Before);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.DualColorDuelOrigin is not null);
        var origin = use.DualColorDuelOrigin!;
        Require(origin.ParentProgramFrameId == root.Id && origin.InitialActorSeat == 0 && origin.InitialTargetSeat == 1 &&
            origin.OwnerDiscardCount == 6 && origin.OtherDiscardCount == 0 && use.CardId == 0 && use.PhysicalCardIds?.Count == 0 &&
            use.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0, ConversionChain.Count: 0 },
            "Six paid cards beats zero despite the later one-versus-four hands; a true zero-entity Duel enters its card-use window.");
        Cold(game, registry); Continue(game); ReachPlay(game);
        Require(PaidOrNull(game) is null && Facts<ProgramDualColorsCommittedEvent>(game).Length == 1 &&
            Facts<ProgramDualColorCardsDiscardedEvent>(game).Single() is { OwnerCount: 6, OtherCount: 0 } &&
            Facts<ProgramDualColorDuelIssuedEvent>(game).Single().CardUseFrameId == use.Id &&
            Facts<CardUseFinishedEvent>(game).Any(fact => fact.ResolutionId == use.Id && fact.CardKind == CardKind.Duel) &&
            CostMoves(game).Select(move => move.CardId).Order().SequenceEqual(sourceCards.Order()) &&
            Facts<ActualPlayPhaseCardUseRecordedEvent>(game).Any(fact => fact.CardActionId == use.Action!.ActionId),
            "The real Duel and all cost/recovery/gain descendants finish once and enter actual Play-use history.");
        RejectSecondActivation(game); Cold(game, registry);

        var (tie, tieRegistry) = Create(); Begin(tie, 1); AnswerColor(tie, "red"); ReachPlay(tie);
        Require(Facts<ProgramDualColorCardsDiscardedEvent>(tie).Single() is { OwnerCount: 0, OtherCount: 0 } &&
            CostMoves(tie).Length == 0 && Facts<ProgramDualColorDuelIssuedEvent>(tie).Length == 0,
            "Two valid empty color selections consume the activation once, without inventing a Duel or payment.");
        RejectSecondActivation(tie); Cold(tie, tieRegistry);
    }

    public static void OpponentActorAndPaidSourceDeathReturnCold()
    {
        var (both, bothRegistry) = Create(dodges: true);
        var bothOwnerCards = Hand(both, 0).Select(card => card.Id).ToArray();
        var bothOtherCards = Hand(both, 1).Select(card => card.Id).ToArray();
        Begin(both, 1); AnswerColor(both, "black"); Reach(both, prompt => prompt.SkillPrompt?.SkillId == Cost);
        var bothRoot = Paid(both);
        var batch = both.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(frame => frame.Batch.ParentFrameId == bothRoot.Id);
        Require(bothRoot.DualColorDuel is { OwnerIsRed: false, OtherIsRed: false } bothPaid &&
            bothPaid.OwnerCardIds.SequenceEqual(bothOwnerCards) && bothPaid.OtherCardIds.SequenceEqual(bothOtherCards) &&
            Hand(both, 0).Count == 0 && Hand(both, 1).Count == 0 && batch.ResumeProgramFrameId == bothRoot.Id &&
            batch.Batch.Movements.Count == bothOwnerCards.Length + bothOtherCards.Length &&
            batch.Batch.Movements.Select(move => move.From).Distinct().OrderBy(location => location.OwnerSeat)
                .SequenceEqual(new[] { CardLocation.Hand(0), CardLocation.Hand(1) }),
            "Two nonempty private selections pay both actual hands in one atomic batch before its first observer can run.");
        Cold(both, bothRegistry); ReachPlay(both);
        Require(CostMoves(both).Length == bothOwnerCards.Length + bothOtherCards.Length &&
            Facts<ProgramDualColorDuelIssuedEvent>(both).Single() is { ActorSeat: 0, TargetSeat: 1, OwnerDiscardCount: 6, OtherDiscardCount: 4 },
            "The frozen six-versus-four result survives its real recovery child and pays each physical card once.");
        Cold(both, bothRegistry);

        var (other, otherRegistry) = Create(dodges: true);
        Begin(other, 1); AnswerColor(other, "red"); Reach(other, prompt => prompt.SkillPrompt?.SkillId == Before);
        var use = other.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.DualColorDuelOrigin is not null);
        Require(use.DualColorDuelOrigin is { InitialActorSeat: 1, InitialTargetSeat: 0, OwnerDiscardCount: 0, OtherDiscardCount: 4 } &&
            use.Action!.ActorSeat == 1 && Hand(other, 1).Count == 0 && Hand(other, 0).Count == 6,
            "The native opponent's four actual paid Dodges determine the opposite real Duel actor, without replacing the initiating source.");
        Cold(other, otherRegistry); Continue(other); ReachPlay(other);
        Require(PaidOrNull(other) is null && CostMoves(other).Length == 4 && other.State.Players[0].Hp == 1,
            "The other actor's actual Duel damages the owner and returns to the original once-paid activation.");
        Cold(other, otherRegistry);

        var (death, deathRegistry) = Create(sourceDeath: true);
        Require(death.State.Players[0].Role == Role.Rebel && death.State.Players[0].Hp == 1,
            "The true dying source is a one-HP Rebel while another Rebel and Lord remain alive.");
        var ids = Hand(death, 0).Select(card => card.Id).ToArray();
        Begin(death, 1); AnswerColor(death, "black"); Reach(death, prompt => prompt.SkillPrompt?.SkillId == Cost);
        var rootId = Paid(death).Id;
        Require(CostMoves(death).Length == ids.Length, "All exact costs have been paid before the real source-death child.");
        Cold(death, deathRegistry); Continue(death);
        ReachUntil(death, () => !death.State.Players[0].IsAlive && PaidOrNull(death) is null);
        Require(death.State.Winner == Winner.None && Facts<ProgramDualColorDuelIssuedEvent>(death).Length == 0 &&
            CostMoves(death).Select(move => move.CardId).Order().SequenceEqual(ids.Order()) &&
            !death.ResolutionStack.Any(frame => frame.Id == rootId) &&
            Facts<ProgramSkillHpLostEvent>(death).Any(fact => fact.TargetSeat == 0 && fact.Amount == 1),
            "A real unrecoverable source death drains the already paid child, clears its exact owner and issues no future Duel or duplicate cost.");
        Cold(death, deathRegistry);
    }

    private static ProgramSkillFrame? PaidOrNull(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.SkillId == Skill);
    private static ProgramSkillFrame Paid(GameEngine game) => PaidOrNull(game)!;
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat].Hand;
    private static T[] Facts<T>(GameEngine game) where T : IGameEvent => game.Events.Select(item => item.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] CostMoves(GameEngine game) => game.CardMovements.Where(move => move.Reason.Value == Reason).ToArray();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static bool Action(PendingDecision prompt, string action) => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == action);
    private static void Begin(GameEngine game, int target) => Accept(game, new UseProgramSkillCommand(0, Skill, Binding, [], [target], game.Revision, P(game)!.PromptId));
    private static void AnswerColor(GameEngine game, string color) => Answer(game, choice => choice.Parameters.GetValueOrDefault("color") == color);
    private static void Continue(GameEngine game)
    {
        if (P(game)!.PlayerSeat == 0) Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = P(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate) => ReachUntil(game, () => P(game) is { } prompt && predicate(prompt));
    private static void ReachUntil(GameEngine game, Func<bool> predicate)
    {
        for (var step = 0; step < 160; step++) { if (predicate()) return; Advance(game); }
        throw new InvalidOperationException("Fixed private-color prefix missed its actual boundary: " + JsonSerializer.Serialize(new { Prompt = P(game), Frames = game.ResolutionStack.Select(frame => new { frame.Kind, frame.Id }), game.State.Winner }));
    }
    private static void Advance(GameEngine game)
    {
        var prompt = P(game);
        if (prompt?.SkillPrompt?.SkillId is Cost or Hp or Gain or Before) Continue(game);
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision));
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "slash" && choice.Cards.Count > 0 || choice.Parameters.GetValueOrDefault("response") is "take-damage" or "take-duel-damage");
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "let-die");
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.Nullification }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "no-nullification");
        else if (prompt is { PlayerSeat: 0 } && Action(prompt, "skip")) Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void RejectUnpublished(GameEngine game)
    { var prompt = P(game)!; var before = State(game); Require(!game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new ChoiceId("unpublished-color"), game.Revision)).Accepted && State(game) == before, "An unpublished commitment changes neither private state nor payment."); }
    private static void RejectSecondActivation(GameEngine game)
    { var before = State(game); Require(!game.Submit(new UseProgramSkillCommand(0, Skill, Binding, [], [1], game.Revision, P(game)!.PromptId)).Accepted && State(game) == before, "The once-per-actual-Play allowance rejects a second activation without new selection or payment."); }
    private static void Private(GameEngine game)
    {
        var before = State(game); var prompt = P(game)!; Require(prompt.IsPrivate, "The current commitment is private.");
        foreach (var seat in Enumerable.Range(0, 4))
        {
            var view = game.CreateSnapshot(seat);
            foreach (var player in view.Players.Where(player => player.Seat != seat))
                Require(player.Hand.Count == 0, "Prepared views preserve the other participant's private hand.");
            if (seat != prompt.PlayerSeat)
                Require(view.PendingDecision is null, "Other prepared views expose neither current color choices nor another hand.");
            else
            {
                var own = view.PendingDecision!; Frozen(own.ValidCardIds); Frozen(own.ValidTargetSeats); Frozen(own.Choices);
                foreach (var choice in own.Choices)
                { Frozen(choice.Cards); Frozen(choice.Targets); Frozen(choice.ContentIds); FrozenParameters(choice.Parameters); }
            }
        }
        Require(State(game) == before, "Mutation attempts against every nested prepared color choice preserve the private command prefix.");
    }
    private static void FrozenParameters(IReadOnlyDictionary<string, string> values)
    {
        Require(values is IDictionary<string, string> { IsReadOnly: true }, "Prepared choice parameters are immutable.");
        try { ((IDictionary<string, string>)values).Add("private-color-mutation-probe", "red"); throw new InvalidOperationException("Choice parameters permitted mutation."); }
        catch (NotSupportedException) { }
    }
    private static void Frozen<T>(IReadOnlyList<T> list)
    { Require(list is IList<T> { IsReadOnly: true }, "The owning receipt is immutable."); try { ((IList<T>)list).Add(default!); throw new InvalidOperationException("Receipt collection permitted mutation."); } catch (NotSupportedException) { } }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "The real command journal cold-restores all four filtered views, typed owners, facts and exact payment ledger.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool dodges = false, bool sourceDeath = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(dodges, sourceDeath));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = sourceDeath ? Role.Rebel : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral && prompt.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:dual-owner", game.Revision, P(game)!.PromptId)); ReachPlay(game); return (game, registry);
    }

    private sealed class Fixture(bool dodges, bool sourceDeath) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-private-color-duel", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid-hand-observer","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["{{Reason}}"],"movementOccurrence":"perOwnerBatch","optional":false,"priority":100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]},{{(sourceDeath ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}")}}]}]},
                 {"id":"{{Hp}}","revision":1,"triggers":[{"id":"real-recovery-observer","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"real-gain-observer","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Before}}","revision":1,"triggers":[{"id":"real-duel-use-observer","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["duel"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"use-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:dual-quiet","revision":1,"triggers":[{"id":"quiet-actual-turn","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Cost] = Observer(Cost), [Hp] = Observer(Hp), [Gain] = Observer(Gain), [Before] = Observer(Before), ["fixture:dual-quiet"] = new { name = "安静实际回合", description = "跳过夹具AI出牌" } } }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "实际子结算夹具") { Program = program });
            builder.AddSkill(new("fixture:dual-pick", "稳定候选", "无运行程序的选将偏好") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:dual-owner", "选色真实程序", "supporter", Skill, "wei", 3,
                sourceDeath ? [Cost, Before] : [Cost, Hp, Gain, Before, "classic:jueqing", "classic:shangshi"]) { InitialHp = 1 });
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:dual-other-{index}", "实际另一参与者", "supporter", "fixture:dual-pick", "shu", 3, ["fixture:dual-quiet", Before]));
            builder.AddDeck(new("fixture:dual-deck", "固定黑色真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(dodges ? "standard:dodge" : "standard:slash", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "私密选色真实父链", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:dual-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:dual-owner", "fixture:dual-other-1", "fixture:dual-other-2", "fixture:dual-other-3"]));
        }
        private static object Observer(string name) => new { name, description = "暂停真实子结算", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
