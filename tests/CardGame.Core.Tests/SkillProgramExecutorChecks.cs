using CardGame.Core;

internal static class SkillProgramExecutorChecks
{

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


    public static void ActiveActivationContractsRejectInvalidDefinitionsAndPreserveOrder()
    {
        const string orderedPair =
            """{"id":"pair","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"otherLivingMale","usesPerTurn":1,"effects":[{"op":"startVirtualDuel","target":"owner"}]}""";
        var pair = ProgramWithActivations(orderedPair);
        Require(pair.Activations.Single().MinTargets == 2 &&
                pair.Activations.Single().TargetKind == SkillProgramTargetKind.OtherLivingMale,
            "An ordered pair must be represented directly by the activation contract.");
        var runtime = Runtime(pair, selectedTargets: [2, 1], activationId: "pair");
        new SkillProgramExecutor().Run(runtime.Frame!.Id, runtime, runtime);
        Require(runtime.Calls.SequenceEqual(["virtual-duel:0:2,1"]) &&
                runtime.Frame is { InstructionIndex: 1 } && runtime.Completed is null,
            "The shared executor must pass both selected targets in command order and suspend once.");

        Throws<InvalidOperationException>(() => ProgramWithActivations(
            orderedPair.Replace("\"maxTargets\":2", "\"maxTargets\":17", StringComparison.Ordinal)),
            "bounded player selection limit");
        Throws<InvalidOperationException>(() => ProgramWithActivations(
            orderedPair.Replace("\"minTargets\":2", "\"minTargets\":1", StringComparison.Ordinal)),
            "selected target set");
        Throws<InvalidOperationException>(() => ProgramWithActivations(
            orderedPair.Replace("\"maxTargets\":2", "\"maxTargets\":3", StringComparison.Ordinal)),
            "selected target set");

        const string variableTargets =
            """{"id":"wounded","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":3,"targetKind":"anyWounded","usesPerTurn":1,"effects":[{"op":"recover","target":"selectedTargets","amount":1}]}""";
        var wounded = ProgramWithActivations(variableTargets).Activations.Single();
        Require(wounded.MinTargets == 2 && wounded.MaxTargets == 3 &&
                wounded.TargetKind == SkillProgramTargetKind.AnyWounded,
            "A bounded target range must remain a direct activation selection.");

        const string suited =
            """{"id":"suited","minCards":2,"maxCards":2,"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":2},{"op":"draw","target":"owner","amount":1}]}""";
        Require(ProgramWithActivations(suited).Activations.Single().SelectedCardsSameSuit,
            "Same-suit selection must survive definition loading for published action and submit validation.");
        Throws<InvalidOperationException>(() => ProgramWithActivations(
            suited.Replace("\"minCards\":2,\"maxCards\":2", "\"minCards\":1,\"maxCards\":1", StringComparison.Ordinal)),
            "exact selection of at least two");
        Throws<InvalidOperationException>(() => ProgramWithActivations(
            suited.Replace("\"maxCards\":2", "\"maxCards\":3", StringComparison.Ordinal)),
            "exact selection of at least two");

        const string first =
            """{"id":"first","usageGroup":"shared","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":1}]}""";
        var second = first.Replace("\"id\":\"first\"", "\"id\":\"second\"", StringComparison.Ordinal);
        var grouped = ProgramWithActivations(first + "," + second).Activations;
        Require(grouped.Count == 2 && grouped.All(item => item.UsageGroup == "shared" && item.UsesPerTurn == 1),
            "Distinct activations must retain the same shared usage key and limit.");
        Throws<InvalidOperationException>(() => ProgramWithActivations(first + "," +
            second.Replace("\"usesPerTurn\":1", "\"usesPerTurn\":2", StringComparison.Ordinal)),
            "usageGroup");
    }


    private static SkillProgram ProgramWithActivations(string activations)
    {
        var rules = $$"""
            {"schemaVersion":62,"skills":[{"id":"fixture:executor","revision":1,"minimumRulesVersion": 171,
            "modifiers":[],"viewAs":[],"activations":[{{activations}}]}]}
            """;
        const string presentation =
            """{"schemaVersion":3,"skills":{"fixture:executor":{"name":"Executor","description":"Fixture"}}}""";
        return SkillProgramCatalog.Load(rules, presentation).Programs["fixture:executor"];
    }

    private static SkillProgram Program(
        string effects,
        int minCards = 0,
        int maxCards = 0,
        int minTargets = 0,
        int maxTargets = 0)
    {
        var rules = $$"""
            {"schemaVersion":62,"skills":[{"id":"fixture:executor","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],
            "activations":[{"id":"run","minCards":{{minCards}},"maxCards":{{maxCards}},
            "minTargets":{{minTargets}},"maxTargets":{{maxTargets}},"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[{{effects}}]}]}]}
            """;
        const string presentation =
            """{"schemaVersion":3,"skills":{"fixture:executor":{"name":"Executor","description":"Fixture"}}}""";
        return SkillProgramCatalog.Load(rules, presentation).Programs["fixture:executor"];
    }

    private static FakeRuntime Runtime(
        SkillProgram program,
        IReadOnlyList<int>? selectedCards = null,
        IReadOnlyList<int>? selectedTargets = null,
        string activationId = "run")
    {
        var frame = new ProgramSkillFrame(
            41,
            0,
            program.Id,
            activationId,
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

        public void RecoverSelectedTargets(long frameId, int ownerSeat, int amount) =>
            Calls.Add($"recover-selected-targets:{frameId}:{ownerSeat}:{amount}");

        public SkillProgramStepOutcome StartVirtualDuel(ProgramSkillFrame frame)
        {
            Calls.Add($"virtual-duel:{frame.OwnerSeat}:{string.Join(',', frame.SelectedTargetSeats)}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome RequestFactionCard(ProgramSkillFrame frame, int targetSeat,
            string providerFactionId, CardKind requiredKind)
        {
            Calls.Add($"faction-card:{frame.OwnerSeat}:{targetSeat}:{providerFactionId}:{requiredKind}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome TransferRandomOwnedCard(ProgramSkillFrame frame, int targetSeat,
            string resultBind)
        {
            Calls.Add($"random-transfer:{frame.OwnerSeat}:{targetSeat}:{resultBind}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome ExchangeSelectedTargetHands(ProgramSkillFrame frame)
        {
            Calls.Add($"exchange-hands:{frame.OwnerSeat}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void AccumulateSelectedCardCount(ProgramSkillFrame frame, string usageId,
            int threshold, string resultBind) =>
            Calls.Add($"accumulate-selected:{frame.OwnerSeat}:{usageId}:{threshold}:{resultBind}");

        public SkillProgramStepOutcome LoseHp(long frameId, string skillId, int targetSeat, int amount)
        {
            Calls.Add($"lose-hp:{targetSeat}:{amount}");
            return LoseHpOutcome;
        }

        public SkillProgramStepOutcome Damage(ProgramSkillFrame frame, int targetSeat, int amount,
            ProgramParticipantReference? sourceReference = null, DamageNature? nature = null)
        {
            Calls.Add($"damage:{frame.OwnerSeat}:{targetSeat}:{amount}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public void ReplaceJudgment(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            Calls.Add($"replace-judgment:{frame.OwnerSeat}");

        public SkillProgramStepOutcome ClaimJudgmentCard(ProgramSkillFrame frame)
        {
            Calls.Add($"claim-judgment:{frame.OwnerSeat}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome ReorderTopCards(ProgramSkillFrame frame, int maximumCards,
            SkillProgramNumberExpression? numberExpression)
        {
            Calls.Add($"reorder-top:{frame.OwnerSeat}:{maximumCards}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public SkillProgramStepOutcome RepeatJudgment(ProgramSkillFrame frame, string reason,
            string resultBind, IReadOnlyList<Suit> successSuits)
        {
            Calls.Add($"repeat-judgment:{frame.OwnerSeat}:{reason}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome SkipTurnPhases(ProgramSkillFrame frame,
            IReadOnlyList<SkillProgramTurnPhase> phases)
        {
            Calls.Add($"skip-phases:{frame.OwnerSeat}:{string.Join(",", phases)}");
            return SkillProgramStepOutcome.Continue;
        }

        public SkillProgramStepOutcome UseVirtualCard(ProgramSkillFrame frame, int targetSeat,
            CardKind cardKind, bool ignoreDistance)
        {
            Calls.Add($"virtual-card:{frame.OwnerSeat}:{targetSeat}:{cardKind}:{ignoreDistance}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome UseBoundCardByTarget(ProgramSkillFrame frame, int targetSeat,
            string sourceBind)
        {
            Calls.Add($"bound-card-use:{frame.OwnerSeat}:{targetSeat}:{sourceBind}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome RequestSlashByTarget(ProgramSkillFrame frame, int targetSeat,
            string resultBind)
        {
            Calls.Add($"slash-request:{frame.OwnerSeat}:{targetSeat}:{resultBind}");
            return SkillProgramStepOutcome.AwaitChoice;
        }

        public void PendExtraTurn(ProgramSkillFrame frame, int? targetSeat)
        {
            Calls.Add($"extra-turn:{frame.OwnerSeat}:{targetSeat?.ToString() ?? "owner"}");
        }

        public SkillProgramStepOutcome Pindian(ProgramSkillFrame frame, int targetSeat)
        {
            Calls.Add($"pindian:{frame.OwnerSeat}:{targetSeat}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome MoveSelected(
            ProgramSkillFrame frame,
            int targetSeat,
            IReadOnlyList<int> cardIds,
            bool toDiscard,
            CardMoveReason reason)
        {
            foreach (var cardId in cardIds) HandCards.Remove(cardId);
            Calls.Add($"move:{frame.OwnerSeat}:{targetSeat}:{string.Join(',', cardIds)}:" +
                       $"{(toDiscard ? "discard" : "give")}:{reason.Value}");
            return SkillProgramStepOutcome.Continue;
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
            SkillProgramCardSetVisibility visibility,
            int sourceSeat)
        {
            Calls.Add($"start-judgment:{frame.OwnerSeat}:{targetSeat}:{reason}:{resultBind}:{visibility}:{sourceSeat}");
            return SkillProgramStepOutcome.AwaitChild;
        }

        public SkillProgramStepOutcome ChooseOwnCardDiscard(
            ProgramSkillFrame frame,
            ProgramParticipantReference? chooser,
            IReadOnlyList<CardZoneKind> zones,
            CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise ChooseOwnCardDiscard.");

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
            IReadOnlyList<CardKind>? cardKinds = null,
            string? matchSuitOfBind = null) =>
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
            bool allowFewerWhenInsufficient,
            bool onePerSuit)
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
            string resultBind,
            IReadOnlyList<EquipmentSlot> equipmentSlots,
            bool skipIfNoCards,
            bool allowSameSource)
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

        public void ClaimDeathCleanupCards(ProgramSkillFrame frame) =>
            Calls.Add($"claim-death-cleanup:{frame.OwnerSeat}");

        public SkillProgramStepOutcome BindDiscardPhaseDiscards(ProgramSkillFrame frame, string resultBind)
        {
            Calls.Add($"bind-discard-phase:{frame.OwnerSeat}:{resultBind}");
            return SkillProgramStepOutcome.Continue;
        }

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
            int amount,
            IReadOnlyList<CardKind> cardKinds) =>
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
        public void DrawBoundCardCount(long frameId, int ownerSeat, int targetSeat, string sourceBind,
        string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise DrawBoundCardCount.");

        public void DrawPhaseSkillUsage(long frameId, int ownerSeat, int targetSeat, string usageId,
        string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise DrawPhaseSkillUsage.");

        public SkillProgramStepOutcome ChooseOption(ProgramSkillFrame frame, int chooserSeat,
        string resultBind, IReadOnlyList<SkillProgramChoiceOption> options) =>
            throw new NotSupportedException("The executor fixture does not exercise ChooseOption.");

        public SkillProgramStepOutcome SelectOwnedCards(ProgramSkillFrame frame, int cardOwnerSeat,
        int amount, SkillProgramNumberExpression? expression, IReadOnlyList<CardZoneKind> zones, string resultBind,
        int minimumCards, int maximumCards, IReadOnlyList<CardKind> cardKinds, IReadOnlyList<Suit> suits) =>
            throw new NotSupportedException("The executor fixture does not exercise SelectOwnedCards.");

        public SkillProgramStepOutcome HoldTargetCards(ProgramSkillFrame frame, int chooserSeat, int holderSeat,
            IReadOnlyList<CardZoneKind> zones, string resultBind, int minimumCards) =>
            throw new NotSupportedException("The executor fixture does not exercise HoldTargetCards.");

        public SkillProgramStepOutcome RevealTargetHandCard(ProgramSkillFrame frame,
            ProgramParticipantReference chooser, ProgramParticipantReference cardOwner, string resultBind,
            SkillProgramRevealMode mode, IReadOnlyList<Suit> eligibleSuits, bool allowDecline) =>
            throw new NotSupportedException("The executor fixture does not exercise RevealTargetHandCard.");

        public void RevealUniqueRankForDying(ProgramSkillFrame frame, CardZoneKind zone, int rescueHp) =>
            throw new NotSupportedException();
        public void RedirectCurrentDamage(ProgramSkillFrame frame, string sourceBind, bool drawLostHpAfterDamage) =>
            throw new NotSupportedException();
        public void ProhibitCurrentResponse(ProgramSkillFrame frame) => throw new NotSupportedException();
        public void RedirectCurrentAttack(ProgramSkillFrame frame, int targetSeat) => throw new NotSupportedException();

        public void CaptureSelectedCards(ProgramSkillFrame frame, string resultBind) =>
            throw new NotSupportedException("The executor fixture does not exercise CaptureSelectedCards.");

        public void RevealBoundCards(ProgramSkillFrame frame, string sourceBind) =>
            throw new NotSupportedException("The executor fixture does not exercise RevealBoundCards.");

        public void UseVirtualDyingAlcohol(ProgramSkillFrame frame)
        {
            Calls.Add($"virtual-dying-alcohol:{frame.OwnerSeat}");
        }

        public void ClaimMovedCards(ProgramSkillFrame frame)
        {
            Calls.Add($"claim-moved-cards:{frame.OwnerSeat}");
        }

        public void UseBoundCardAsDyingAlcohol(ProgramSkillFrame frame, string sourceBind, CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise UseBoundCardAsDyingAlcohol.");

        public SkillProgramStepOutcome ChooseDifferentCategoryDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        string resultBind,
        CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise ChooseDifferentCategoryDiscard.");

        public void GrantTurnSkills(ProgramSkillFrame frame, IReadOnlyList<string> skillIds) =>
            throw new NotSupportedException("The executor fixture does not exercise GrantTurnSkills.");

        public SkillProgramStepOutcome UseSelectedCardsAs(
        ProgramSkillFrame frame,
        int targetSeat,
        string viewAsId,
        CardKind outputKind) =>
            throw new NotSupportedException("The executor fixture does not exercise UseSelectedCardsAs.");

        public SkillProgramStepOutcome UseAllHandCardsAsOrdinaryTrick(
        ProgramSkillFrame frame,
        string viewAsId) =>
            throw new NotSupportedException("The executor fixture does not exercise UseAllHandCardsAsOrdinaryTrick.");

        public SkillProgramStepOutcome SelectTarget(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        IReadOnlyList<CardZoneKind> zones,
        PlayerMarkerKind? marker,
        ProgramParticipantReference? actorReference = null,
        bool skipIfNoTarget = false) =>
            throw new NotSupportedException("The executor fixture does not exercise SelectTarget.");

        public void ChangeAttributedMarker(
        ProgramSkillFrame frame,
        ProgramParticipantReference target,
        PlayerMarkerKind marker,
        int amount) =>
            throw new NotSupportedException("The executor fixture does not exercise ChangeAttributedMarker.");

        public SkillProgramStepOutcome CauseDeathUnlessBoundCardKind(
        ProgramSkillFrame frame,
        string sourceBind,
        IReadOnlyList<CardKind> excludedCardKinds) =>
            throw new NotSupportedException("The executor fixture does not exercise CauseDeathUnlessBoundCardKind.");

        public SkillProgramStepOutcome DistributeOwnedCards(
        ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        SkillProgramTargetKind targetKind,
        bool allowDeclineBeforeFirst,
        CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise DistributeOwnedCards.");

        public SkillProgramStepOutcome RequestAttackRangeAid(
        ProgramSkillFrame frame,
        CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise RequestAttackRangeAid.");

        public void GrantTurnHandColorRestriction(
        ProgramSkillFrame frame,
        string sourceBind,
        int targetSeat, bool useFrozenSuit = false) =>
            throw new NotSupportedException("The executor fixture does not exercise GrantTurnHandColorRestriction.");

        public void GrantTurnHandCardProhibition(ProgramSkillFrame frame, int targetSeat) =>
            throw new NotSupportedException("The executor fixture does not exercise GrantTurnHandCardProhibition.");

        public void AbolishOwnerAreas(ProgramSkillFrame frame, IReadOnlyList<CardZoneKind> zones) =>
            throw new NotSupportedException("The executor fixture does not exercise AbolishOwnerAreas.");

        public void LoseDeathSourceSkills(ProgramSkillFrame frame) =>
            throw new NotSupportedException("The executor fixture does not exercise LoseDeathSourceSkills.");

        public void PreventCurrentDamage(ProgramSkillFrame frame) =>
            throw new NotSupportedException("The executor fixture does not exercise PreventCurrentDamage.");

        public void NullifyCurrentCardEffect(ProgramSkillFrame frame) =>
            throw new NotSupportedException("The executor fixture does not exercise NullifyCurrentCardEffect.");

        public void NullifySelectedCardEffects(ProgramSkillFrame frame) =>
            throw new NotSupportedException("The executor fixture does not exercise NullifySelectedCardEffects.");

        public SkillProgramStepOutcome SelectAndMoveOwnedCard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        SkillProgramCardDestination destination,
        ProgramParticipantReference? destinationRef,
        string? resultBind,
        CardMoveReason reason,
        IReadOnlyList<SkillProgramCardCategory>? cardCategories = null,
        bool skipIfNoCards = false,
        bool allowSameOwnerHandReturn = false,
        string? coverageResultBind = null,
        bool awaitMovementTriggers = false, bool revealBeforeMove = false,
        IReadOnlyList<CardKind>? cardKinds = null,
        bool prohibitReplacingEquipment = false) =>
            throw new NotSupportedException("The executor fixture does not exercise SelectAndMoveOwnedCard.");

        public SkillProgramStepOutcome ChooseOtherOwnedCardDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise ChooseOtherOwnedCardDiscard.");

        public SkillProgramStepOutcome RestorePhaseHandDiscards(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference phaseOwner) =>
            throw new NotSupportedException("The executor fixture does not exercise RestorePhaseHandDiscards.");

        public void TakeRandomCardFromEveryOtherCharacter(
        long frameId,
        int ownerSeat,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason) =>
            throw new NotSupportedException("The executor fixture does not exercise TakeRandomCardFromEveryOtherCharacter.");

        public void RefundCardUseDebit(ProgramSkillFrame frame) =>
            throw new NotSupportedException("The executor fixture does not exercise RefundCardUseDebit.");
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
