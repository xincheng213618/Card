using CardGame.Core;

internal static class SkillProgramExecutorChecks
{
    public static void ExecutesComposedEffectsConditionsAndCursorOrder()
    {
        var program = Program(
            """
            {"op":"draw","target":"owner","amount":2},
            {"op":"draw","target":"owner","amount":3,"condition":{"kind":"hpAtLeast","value":4}},
            {"op":"recover","target":"selectedTarget","amount":1},
            {"op":"giveSelected","target":"selectedTarget","amount":1}
            """,
            minCards: 1,
            maxCards: 1,
            minTargets: 1,
            maxTargets: 1);
        var runtime = Runtime(program, selectedCards: [10], selectedTargets: [1]);

        new SkillProgramExecutor().Run(runtime.Frame!.Id, runtime, runtime);

        Require(runtime.Completed is { Completed: true, Reason: null } && runtime.Frame is null,
            "A completed program must finish once after its last instruction.");
        Require(runtime.Calls.SequenceEqual([
                "draw:0:0:2:::Private:skill-program.fixture:executor.Draw",
                "recover:0:1:1",
                "move:0:1:10:give:skill-program.fixture:executor.GiveSelected"
            ]),
            "The executor must preserve effect order, skip false conditions and route selected cards once.");
        Require(runtime.UpdatedCursors.SequenceEqual([1, 2, 3, 4]),
            "Every instruction, including a skipped condition, must advance before side effects.");
    }

    public static void SuspendedChildResumesWithoutRepeatingPaidEffect()
    {
        var program = Program(
            """
            {"op":"loseHp","target":"owner","amount":1},
            {"op":"draw","target":"owner","amount":1}
            """);
        var runtime = Runtime(program);
        runtime.LoseHpOutcome = SkillProgramStepOutcome.AwaitChild;
        var frameId = runtime.Frame!.Id;

        new SkillProgramExecutor().Run(frameId, runtime, runtime);

        Require(runtime.Frame is { InstructionIndex: 1 } && runtime.Completed is null &&
                runtime.Calls.SequenceEqual(["lose-hp:0:1"]),
            "A child-producing effect must retain the already-advanced cursor and leave the frame active.");

        runtime.LoseHpOutcome = SkillProgramStepOutcome.Continue;
        new SkillProgramExecutor().Run(frameId, runtime, runtime);

        Require(runtime.Completed is { Completed: true } &&
                runtime.Calls.SequenceEqual([
                    "lose-hp:0:1",
                    "draw:0:0:1:::Private:skill-program.fixture:executor.Draw"
                ]),
            "Resuming after the child must continue at the next effect without paying LoseHp twice.");
    }

    public static void InvalidSelectedCostCancelsRemainingEffectsAtomically()
    {
        var program = Program(
            """
            {"op":"giveSelected","target":"selectedTarget","amount":1},
            {"op":"draw","target":"owner","amount":2}
            """,
            minCards: 1,
            maxCards: 1,
            minTargets: 1,
            maxTargets: 1);
        var runtime = Runtime(program, selectedCards: [10], selectedTargets: [1]);
        runtime.HandCards.Clear();

        new SkillProgramExecutor().Run(runtime.Frame!.Id, runtime, runtime);

        Require(runtime.Completed is { Completed: false, Reason: { } reason } &&
                reason.Contains("所选牌", StringComparison.Ordinal) &&
                runtime.Calls.Count == 0 && runtime.UpdatedCursors.SequenceEqual([1]),
            "A missing selected cost must cancel before any partial move or later benefit.");
    }

    public static void RejectsChangedProgramsUnknownHandlersAndDuplicates()
    {
        var program = Program("""{"op":"draw","target":"owner","amount":1}""");
        var changed = Runtime(program);
        changed.Frame = changed.Frame! with { GameplayHash = "changed" };
        Throws<InvalidOperationException>(
            () => new SkillProgramExecutor().Run(changed.Frame.Id, changed, changed),
            "gameplay hash");
        Require(changed.UpdatedCursors.Count == 0 && changed.Calls.Count == 0,
            "A changed definition must fail before advancing or mutating state.");

        var recoverProgram = Program("""{"op":"recover","target":"owner","amount":1}""");
        var missing = Runtime(recoverProgram);
        var drawOnly = new SkillProgramEffectCatalog([new DrawSkillProgramEffectHandler()]);
        Throws<InvalidOperationException>(
            () => new SkillProgramExecutor(drawOnly).Run(missing.Frame!.Id, missing, missing),
            "Recover");
        Require(missing.Frame is { InstructionIndex: 0 } && missing.Calls.Count == 0,
            "Missing handlers must fail before advancing the cursor or producing side effects.");

        var composed = Runtime(Program(
            """{"op":"recover","target":"owner","amount":1}""", schemaVersion: 23));
        Throws<InvalidOperationException>(
            () => new SkillProgramExecutor(drawOnly).Run(composed.Frame!.Id, composed, composed),
            "Recover");
        Require(composed.Frame is { InstructionIndex: 0 } && composed.Calls.Count == 0,
            "Composed programs must honor an explicitly injected handler catalog before advancing.");

        Throws<InvalidOperationException>(
            () => _ = new SkillProgramEffectCatalog([
                new DrawSkillProgramEffectHandler(),
                new DuplicateDrawHandler()
            ]),
            "both");
        Throws<InvalidOperationException>(
            () => _ = new SkillProgramEffectCatalog([new InvalidOperationHandler()]),
            "invalid operation");
    }

    public static void ReflectionDiscoversEveryPrimitiveHandler()
    {
        var catalog = SkillProgramEffectCatalog.Discover(typeof(SkillProgramExecutor).Assembly);
        Require(catalog.Handlers.Select(handler => handler.Op).Order().SequenceEqual(
                Enum.GetValues<SkillProgramEffectOp>().Order()),
            "Core reflection discovery must find one handler for every current primitive operation.");
        Require(catalog.Handlers.Count == Enum.GetValues<SkillProgramEffectOp>().Length &&
                catalog.Handlers.Select(handler => handler.GetType().FullName)
                    .SequenceEqual(catalog.Handlers.Select(handler => handler.GetType().FullName)
                        .Order(StringComparer.Ordinal)),
            "Reflection discovery must return exactly one handler per operation in stable type-name order.");
        Require(Enum.GetValues<SkillProgramEffectOp>()
                .All(op => catalog.Resolve(op).Op == op) &&
                SkillProgramEffectCatalog.Default.Handlers.Count == Enum.GetValues<SkillProgramEffectOp>().Length,
            "Discovered and cached default catalogs must resolve each primitive operation.");
        Throws<NotSupportedException>(
            () => ((ICollection<ISkillProgramEffectHandler>)catalog.Handlers).Clear(),
            null);
    }

    private static SkillProgram Program(
        string effects,
        int minCards = 0,
        int maxCards = 0,
        int minTargets = 0,
        int maxTargets = 0,
        int schemaVersion = 1)
    {
        var minimumRules = schemaVersion == 23 ? "\"minimumRulesVersion\":128," : string.Empty;
        var rules = $$"""
            {"schemaVersion":{{schemaVersion}},"skills":[{"id":"fixture:executor","revision":1,{{minimumRules}}"modifiers":[],"viewAs":[],
            "activations":[{"id":"run","minCards":{{minCards}},"maxCards":{{maxCards}},
            "minTargets":{{minTargets}},"maxTargets":{{maxTargets}},"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[{{effects}}]}]}]}
            """;
        const string presentation =
            """{"schemaVersion":1,"skills":{"fixture:executor":{"name":"Executor","description":"Fixture"}}}""";
        return SkillProgramCatalog.Load(rules, presentation).Programs["fixture:executor"];
    }

    private static FakeRuntime Runtime(
        SkillProgram program,
        IReadOnlyList<int>? selectedCards = null,
        IReadOnlyList<int>? selectedTargets = null)
    {
        var frame = new ProgramSkillFrame(
            41,
            0,
            program.Id,
            "run",
            program.GameplayHash,
            0,
            selectedCards ?? [],
            selectedTargets ?? [])
        {
            SkillInstanceId = "fixture-instance"
        };
        return new FakeRuntime(program, frame);
    }

    private static void Throws<TException>(Action action, string? expected)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception) when (
            expected is null || exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}" + (expected is null ? "." : $" containing '{expected}'."));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeRuntime : ISkillProgramExecutionHost, ISkillProgramEffectHost
    {
        public int ResolveParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference) =>
            reference.Kind == ProgramParticipantRef.Owner ? frame.OwnerSeat : 1;

        private readonly SkillProgram _program;
        private readonly Dictionary<int, SkillProgramActorState> _actors = new()
        {
            [0] = new(new PlayerSkillContext(0, 3, 4, 1, TurnPhase.Play, IsOwnTurn: true), true),
            [1] = new(new PlayerSkillContext(1, 2, 4, 0, TurnPhase.Play), true)
        };

        internal FakeRuntime(SkillProgram program, ProgramSkillFrame frame)
        {
            _program = program;
            Frame = frame;
            foreach (var cardId in frame.SelectedCardIds) HandCards.Add(cardId);
        }

        internal ProgramSkillFrame? Frame { get; set; }
        internal (bool Completed, string? Reason)? Completed { get; private set; }
        internal List<int> UpdatedCursors { get; } = [];
        internal List<string> Calls { get; } = [];
        internal HashSet<int> HandCards { get; } = [];
        internal SkillProgramStepOutcome LoseHpOutcome { get; set; } = SkillProgramStepOutcome.Continue;
        public bool IsGameOver { get; set; }

        public ProgramSkillFrame? GetActiveFrame(long frameId) =>
            Frame?.Id == frameId ? Frame : null;

        public SkillProgram GetProgram(string skillId) =>
            skillId == _program.Id
                ? _program
                : throw new InvalidOperationException($"Unknown program {skillId}.");

        public SkillProgramActorState GetActor(int seat) => _actors[seat];

        public bool EvaluateCondition(ProgramSkillFrame frame, SkillProgramCondition condition,
            PlayerSkillContext context) => condition.Evaluate(context,
            bind => frame.PindianResultBindings.Single(item => item.Name == bind).SourceWon,
            _ => false);

        public bool OwnsSkillInstance(int ownerSeat, string skillId, string skillInstanceId) =>
            ownerSeat == 0 && skillId == _program.Id && skillInstanceId == "fixture-instance";

        public bool OwnsCards(int ownerSeat, IReadOnlyList<int> cardIds,
            IReadOnlyList<CardZoneKind> sourceZones) =>
            ownerSeat == 0 && cardIds.Distinct().Count() == cardIds.Count &&
            cardIds.All(HandCards.Contains);

        public void UpdateFrame(ProgramSkillFrame frame)
        {
            Frame = frame;
            UpdatedCursors.Add(frame.InstructionIndex);
        }

        public void Complete(ProgramSkillFrame frame, bool completed, string? reason = null)
        {
            if (Frame?.Id != frame.Id) throw new InvalidOperationException("Completed the wrong frame.");
            Completed = (completed, reason);
            Frame = null;
        }

        public void Draw(long frameId, int ownerSeat, int targetSeat, int amount,
            SkillProgramNumberExpression? numberExpression, string? resultBind,
            SkillProgramCardSetVisibility visibility, CardMoveReason reason) =>
            Calls.Add($"draw:{ownerSeat}:{targetSeat}:{amount}:{numberExpression}:{resultBind}:{visibility}:{reason.Value}");

        public void DrawSelectedTargets(long frameId, int amount, CardMoveReason reason) =>
            Calls.Add($"draw-selected-targets:{frameId}:{amount}:{reason.Value}");

        public void Recover(long frameId, int ownerSeat, int targetSeat, int amount,
            SkillProgramNumberExpression? numberExpression, string? sourceBind) =>
            Calls.Add(numberExpression is null
                ? $"recover:{ownerSeat}:{targetSeat}:{amount}"
                : $"recover:{ownerSeat}:{targetSeat}:{amount}:{numberExpression}:{sourceBind}");

        public SkillProgramStepOutcome LoseHp(long frameId, string skillId, int targetSeat, int amount)
        {
            Calls.Add($"lose-hp:{targetSeat}:{amount}");
            return LoseHpOutcome;
        }

        public SkillProgramStepOutcome Damage(ProgramSkillFrame frame, int targetSeat, int amount)
        {
            Calls.Add($"damage:{frame.OwnerSeat}:{targetSeat}:{amount}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome Pindian(ProgramSkillFrame frame, int targetSeat)
        {
            Calls.Add($"pindian:{frame.OwnerSeat}:{targetSeat}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void MoveSelected(
            ProgramSkillFrame frame,
            int targetSeat,
            IReadOnlyList<int> cardIds,
            bool toDiscard,
            CardMoveReason reason)
        {
            foreach (var cardId in cardIds) HandCards.Remove(cardId);
            Calls.Add($"move:{frame.OwnerSeat}:{targetSeat}:{string.Join(',', cardIds)}:" +
                       $"{(toDiscard ? "discard" : "give")}:{reason.Value}");
        }

        public SkillProgramStepOutcome InsertPhase(
            ProgramSkillFrame frame,
            TurnPhase phase,
            SkillProgramPhaseContinuation continuation)
        {
            Calls.Add($"insert-phase:{frame.OwnerSeat}:{phase}:{continuation}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void RecoverTo(
            long frameId,
            int ownerSeat,
            int targetSeat,
            SkillProgramNumberExpression expression,
            int minimumValue,
            bool clampToMaxHp) =>
            Calls.Add($"recover-to:{ownerSeat}:{targetSeat}:{expression}:{minimumValue}:{clampToMaxHp}");

        public void DiscardOwnedZoneCards(
            ProgramSkillFrame frame,
            IReadOnlyList<CardZoneKind> zones,
            CardMoveReason reason) =>
            Calls.Add($"discard-owned-zones:{frame.OwnerSeat}:{string.Join(',', zones)}:{reason.Value}");

        public void SetChainedState(ProgramSkillFrame frame, bool chained, int? targetSeat = null) =>
            Calls.Add($"set-chained-state:{frame.OwnerSeat}:{chained}");

        public void ChangeMaximumHp(ProgramSkillFrame frame, int amount) =>
            Calls.Add($"change-maximum-hp:{frame.OwnerSeat}:{amount}");

        public void GrantSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds) =>
            Calls.Add($"grant-skills:{frame.OwnerSeat}:{string.Join(',', skillIds)}");

        public void TurnOver(long frameId, int ownerSeat, int targetSeat) =>
            Calls.Add($"turn-over:{ownerSeat}:{targetSeat}");

        public void SetFaceState(long frameId, int ownerSeat, int targetSeat, bool faceDown) =>
            Calls.Add($"set-face-state:{ownerSeat}:{targetSeat}:{faceDown}");

        public SkillProgramStepOutcome StartJudgment(
            ProgramSkillFrame frame,
            int targetSeat,
            string reason,
            string resultBind,
            SkillProgramCardSetVisibility visibility)
        {
            Calls.Add($"start-judgment:{frame.OwnerSeat}:{targetSeat}:{reason}:{resultBind}:{visibility}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void RevealTopCards(
            long frameId,
            int ownerSeat,
            int amount,
            SkillProgramNumberExpression? numberExpression,
            string resultBind,
            SkillProgramCardSetVisibility visibility) =>
            Calls.Add($"reveal:{ownerSeat}:{amount}:{numberExpression}:{resultBind}:{visibility}");

        public void FilterBoundCards(long frameId, string sourceBind, string resultBind,
            IReadOnlyList<Suit> suits, ProgramParticipantReference? effectiveSuitFor = null,
            IReadOnlyList<SkillProgramCardCategory>? categories = null,
            IReadOnlyList<EquipmentSlot>? equipmentSlots = null,
            IReadOnlyList<CardKind>? cardKinds = null) =>
            Calls.Add($"filter-bound:{sourceBind}:{resultBind}:{string.Join(',', suits)}");

        public SkillProgramStepOutcome SelectCardSubset(
            long frameId,
            int ownerSeat,
            string sourceBind,
            string resultBind,
            int minimumCards,
            int maximumCards,
            int maximumRankSum,
            SkillProgramSubsetAiOrder aiOrder,
            bool allowFewerWhenInsufficient)
        {
            Calls.Add($"select-subset:{ownerSeat}:{sourceBind}:{resultBind}:{minimumCards}:{maximumCards}:{maximumRankSum}:{aiOrder}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public SkillProgramStepOutcome MoveBoundCards(
            long frameId,
            int ownerSeat,
            string sourceBind,
            string? exceptBind,
            SkillProgramCardDestination destination,
            CardZoneKind? destinationZone,
            CardMoveReason reason)
        {
            Calls.Add($"move-bound:{ownerSeat}:{sourceBind}:{exceptBind}:{destination}:{destinationZone}:{reason.Value}");
            return SkillProgramStepOutcome.Continue;
        }

        public SkillProgramStepOutcome SelectTarget(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            IReadOnlyList<CardZoneKind> zones)
        {
            Calls.Add($"select-target:{ownerSeat}:{targetKind}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public SkillProgramStepOutcome SelectTargets(
            long frameId,
            int ownerSeat,
            SkillProgramTargetKind targetKind,
            int minimumTargets,
            int maximumTargets,
            SkillProgramNumberExpression? numberExpression,
            SkillProgramTargetAiOrder aiOrder)
        {
            Calls.Add($"select-targets:{ownerSeat}:{targetKind}:{minimumTargets}:{maximumTargets}:{aiOrder}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public SkillProgramStepOutcome SelectSourceCard(
            long frameId,
            int ownerSeat,
            SkillProgramCardSource cardSource,
            IReadOnlyList<CardZoneKind> zones,
            string resultBind)
        {
            Calls.Add($"select-source-card:{ownerSeat}:{string.Join(',', zones)}:{resultBind}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public SkillProgramStepOutcome GiveBoundCard(
            long frameId,
            int ownerSeat,
            string sourceBind,
            SkillProgramTargetKind targetKind,
            CardMoveReason reason)
        {
            Calls.Add($"give-bound:{ownerSeat}:{sourceBind}:{targetKind}:{reason.Value}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public void ClaimDamageCards(long frameId, int ownerSeat, CardMoveReason reason) =>
            Calls.Add($"claim-damage:{ownerSeat}:{reason.Value}");

        public void TakeRandomHandCardFromSelectedTargets(
            long frameId,
            int ownerSeat,
            int amountPerTarget,
            CardMoveReason reason) =>
            Calls.Add($"take-random-hands:{ownerSeat}:{amountPerTarget}:{reason.Value}");

        public void AdjustNormalDraw(ProgramSkillFrame frame, int amount) =>
            Calls.Add($"adjust-normal-draw:{frame.OwnerSeat}:{amount}");

        public void GrantTurnCardDamageModifier(
            ProgramSkillFrame frame,
            IReadOnlyList<CardKind> cardKinds,
            int amount,
            SkillProgramDamageModifierExpiration expiration,
            SkillProgramDamageModifierSourceScope sourceScope) =>
            Calls.Add($"grant-turn-card-damage:{frame.OwnerSeat}:{string.Join(',', cardKinds)}:{amount}");

        public void GrantTurnCardActionProhibition(
            ProgramSkillFrame frame,
            IReadOnlyList<CardKind> cardKinds,
            IReadOnlyList<CardActionType> actionTypes) =>
            Calls.Add($"grant-turn-card-action-prohibition:{frame.OwnerSeat}:" +
                      $"{string.Join(',', cardKinds)}:{string.Join(',', actionTypes)}");

        public void GrantTurnRuleModifier(
            ProgramSkillFrame frame,
            SkillRuleQuery query,
            SkillRuleOperation operation,
            int amount) =>
            Calls.Add($"grant-turn-rule:{frame.OwnerSeat}:{query}:{operation}:{amount}");

        public void GrantTurnCardTargetRestriction(
            ProgramSkillFrame frame,
            SkillProgramCardTargetRestriction restriction,
            int targetSeat) =>
            Calls.Add($"grant-turn-card-target-restriction:{frame.OwnerSeat}:{targetSeat}:{restriction}");

        public void GrantTurnCardConversion(
            ProgramSkillFrame frame,
            string sourceBind,
            SkillProgramCardColorRelation colorRelation,
            CardKind outputKind) =>
            Calls.Add($"grant-turn-card-conversion:{frame.OwnerSeat}:{sourceBind}:{colorRelation}:{outputKind}");

        public SkillProgramStepOutcome StartPindian(ProgramSkillFrame frame,
            ProgramParticipantReference opponentReference, string resultBind,
            SkillProgramCardSetVisibility visibility)
        {
            Calls.Add($"start-pindian:{frame.OwnerSeat}:{opponentReference.Kind}:{resultBind}:{visibility}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void SetBooleanState(ProgramSkillFrame frame, string stateId, bool value) =>
            Calls.Add($"set-boolean:{frame.OwnerSeat}:{stateId}:{value}");

        public void ToggleBooleanState(ProgramSkillFrame frame, string stateId) =>
            Calls.Add($"toggle-boolean:{frame.OwnerSeat}:{stateId}");

        public void GrantDirectedTurnCardPolicy(ProgramSkillFrame frame,
            ProgramParticipantReference actorReference, ProgramParticipantReference targetReference,
            IReadOnlyList<CardKind> cardKinds, DirectedTurnCardPolicyEffect effects) =>
            Calls.Add($"grant-directed:{frame.OwnerSeat}:{actorReference.Kind}:{targetReference.Kind}:" +
                      $"{string.Join(',', cardKinds)}:{effects}");
    }

    private sealed class DuplicateDrawHandler : ISkillProgramEffectHandler
    {
        public SkillProgramEffectOp Op => SkillProgramEffectOp.Draw;
        public SkillProgramStepOutcome Execute(
            SkillProgramEffect effect,
            ProgramSkillFrame frame,
            int targetSeat,
            ISkillProgramEffectHost host) => SkillProgramStepOutcome.Continue;
    }

    private sealed class InvalidOperationHandler : ISkillProgramEffectHandler
    {
        public SkillProgramEffectOp Op => (SkillProgramEffectOp)999;
        public SkillProgramStepOutcome Execute(
            SkillProgramEffect effect,
            ProgramSkillFrame frame,
            int targetSeat,
            ISkillProgramEffectHost host) => SkillProgramStepOutcome.Continue;
    }
}
