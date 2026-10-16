using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramSelectionValidationChecks
{
    public static void RegisteredValidationAndTargetResolversPreserveBoundaries()
    {
        var types = typeof(GameEngine).Assembly.GetTypes();
        var forward = new ProgramSelectionValidationCatalog(types);
        var reverse = new ProgramSelectionValidationCatalog(types.Reverse());
        Require(forward.Registrations.Select(item => (item.Stage, item.Order, item.Validator.GetType()))
                .SequenceEqual(reverse.Registrations.Select(item => (item.Stage, item.Order, item.Validator.GetType()))),
            "Validation stages and precedence must not depend on reflection discovery order.");

        _calls.Clear();
        var fixtureCatalog = new ProgramSelectionValidationCatalog(
            [typeof(DrawFailureValidator), typeof(RecoverFailureValidator), typeof(AcceptValidator), typeof(LaterStageValidator)]);
        var registry = new ProgramSelectionValidationRegistry(fixtureCatalog, new Queries());
        var both = Context(SkillProgramEffectOp.Draw, SkillProgramEffectOp.Recover);
        var error = registry.Validate(ProgramSelectionValidationStage.BeforeDistinctSuits, both, Action(), [], []);
        Require(ReferenceEquals(error, RecoverFailureValidator.Error) && _calls.SequenceEqual(["accept", "recover"]),
            "The first applicable failure must retain the original error and stop without entering a later stage.");
        _calls.Clear();
        error = registry.Validate(ProgramSelectionValidationStage.BeforeDistinctSuits, Context(SkillProgramEffectOp.Draw), Action(), [], []);
        Require(ReferenceEquals(error, DrawFailureValidator.Error) && _calls.SequenceEqual(["accept", "draw"]),
            "An unrelated operation's validator must be skipped while preserving the remaining precedence.");
        _calls.Clear();
        Require(registry.Validate(ProgramSelectionValidationStage.BeforePublicPrerequisites, both, Action(), [], []) is null && _calls.Count == 0,
            "An empty stage must not execute validators from another common-check boundary.");

        foreach (var invalid in new[]
                 {
                     new[] { typeof(AcceptValidator), typeof(AcceptValidator) }, new[] { typeof(WrongContractValidator) },
                     new[] { typeof(InvalidOperationValidator) }, new[] { typeof(DuplicateOperationValidator) },
                     new[] { typeof(InvalidStageValidator) }, new[] { typeof(ParameterizedValidator) },
                     new[] { typeof(StatefulValidator) }, new[] { typeof(GenericValidator<>) },
                     new Type[] { null! }, Array.Empty<Type>()
                 })
            Reject(() => new ProgramSelectionValidationCatalog(invalid));
        AssertOriginalFailure(() => new ProgramSelectionValidationCatalog([typeof(ThrowingConstructorValidator)]));
        var throwing = new ProgramSelectionValidationRegistry(new([typeof(ThrowingExecutionValidator)]), new Queries());
        AssertOriginalFailure(() => throwing.Validate(ProgramSelectionValidationStage.BeforeDistinctSuits, both, Action(), [], []));

        var queries = new Queries { RankSumAllowed = false, ExactCountAllowed = false, GiftIssued = true };
        var originalOrder = new ProgramSelectionValidationRegistry(new(
            [typeof(ExactOwnedCountSelectionValidator), typeof(OwnedRankSumSelectionValidator)]), queries);
        error = originalOrder.Validate(ProgramSelectionValidationStage.BeforeDistinctSuits, both, Action(), [], []);
        Require(error is { Code: CommandErrorCode.IllegalAction, Message: "自己的手牌中不存在满足精确点数和的选牌方案。" } && queries.ExactReads == 0,
            "Rank-sum failure must still precede the exact-count failure with its original code and message.");
        queries.RankSumAllowed = true;
        error = originalOrder.Validate(ProgramSelectionValidationStage.BeforeDistinctSuits, both, Action(), [], []);
        Require(error is { Code: CommandErrorCode.IllegalAction, Message: "没有足额的合法区域牌支付精确费用。" } && queries.ExactReads == 1,
            "Validation must requery current facts and report the next original failure after the first condition clears.");
        var late = new ProgramSelectionValidationRegistry(new(
            [typeof(DynamicDiscardDamageSelectionValidator), typeof(BlackGiftSelectionValidator)]), queries);
        var gift = Context(SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest, SkillProgramEffectOp.DiscardTargetHpCardsAndDamage);
        error = late.Validate(ProgramSelectionValidationStage.AfterCardFilters, gift, Action(), [], [1]);
        Require(error is { Code: CommandErrorCode.IllegalAction, Message: "This limited black-hand contest was already issued in this game." } && queries.DynamicReads == 0,
            "The limited black-hand error must still stop before dynamic target/payment checks.");
        queries.GiftIssued = false;
        error = late.Validate(ProgramSelectionValidationStage.AfterCardFilters, gift, Action(), [], [1]);
        Require(error is { Code: CommandErrorCode.InvalidTarget, Message: "The exact dynamic target HP/range cost is unavailable." } && queries.DynamicReads == 1,
            "A now-available limited use must requery the following dynamic prerequisites.");

        TargetResolversPreserveFactsAndFallback(types, queries);
    }

    private static void TargetResolversPreserveFactsAndFallback(Type[] types, Queries queries)
    {
        var catalog = new ProgramTargetResolverCatalog(types);
        var reverse = new ProgramTargetResolverCatalog(types.Reverse());
        Require(catalog.Registrations.Select(item => item.Implementation)
                .SequenceEqual(reverse.Registrations.Select(item => item.Implementation)),
            "Target resolver discovery must be deterministic.");
        Reject(() => new ProgramTargetResolverCatalog([typeof(PreviousRoundFocusTargetResolver), typeof(DuplicateTargetResolver)]));
        Reject(() => new ProgramTargetResolverCatalog([typeof(InvalidTargetResolver)]));
        Reject(() => new ProgramTargetResolverCatalog([typeof(StatefulTargetResolver)]));
        Reject(() => new ProgramTargetResolverCatalog([typeof(ParameterizedTargetResolver)]));
        Reject(() => new ProgramTargetResolverCatalog([typeof(DuplicateKindTargetResolver)]));
        Reject(() => new ProgramTargetResolverCatalog([null!]));
        Reject(() => new ProgramTargetResolverCatalog([]));
        AssertOriginalFailure(() => new ProgramTargetResolverCatalog([typeof(ThrowingConstructorTargetResolver)]));
        var throwing = new ProgramTargetResolverRegistry(new([typeof(ThrowingExecutionTargetResolver)]), queries);
        AssertOriginalFailure(() => throwing.TryResolve(new(0, SkillProgramTargetKind.OtherLivingNotLastRoundFocus), out _));

        queries.Participants = Array.AsReadOnly(new[]
        {
            new ProgramTargetParticipantFacts(2, false, true, 2, 4), new(0, true, true, 4, 4),
            new(3, true, false, 0, 0), new(1, true, true, 3, 4)
        });
        queries.RoundNumber = 4;
        queries.CurrentProgramFrameId = 40;
        var source = new CardConversionSource("fixture:focus", "focus", 0, "instance");
        queries.FocusHistory = Array.AsReadOnly(new[] { new DemonRoundFocusEvent(1, source, 1, 3) });
        queries.SupportHistory = Array.AsReadOnly(new[]
        {
            new DemonSupportGrantedEvent(40, 0, 1, "magic:rende"),
            new(39, 1, 0, "magic:rende"), new(39, 1, 0, "magic:rende"),
            new(39, 2, 0, "magic:rende"), new(39, 3, 0, "magic:rende"),
            new(39, 3, 0, "magic:qingnang"), new(39, 1, 0, "magic:qingnang")
        });
        var registry = new ProgramTargetResolverRegistry(catalog, queries);
        Require(registry.MinimumTargets(SkillProgramTargetKind.OtherLivingWithoutEndingSupport) == 0 &&
                registry.EmptySelectionLabel(SkillProgramTargetKind.OtherLivingWithoutEndingSupport) == "不授予此技能。" &&
                registry.MinimumTargets(SkillProgramTargetKind.OtherLivingWithHand) == 0 &&
                registry.EmptySelectionLabel(SkillProgramTargetKind.OtherLivingWithHand) == "不获得手牌。" &&
                registry.MinimumTargets(SkillProgramTargetKind.AnyLiving) == 1,
            "Registered and generic target domains must preserve their original minimum counts and empty-choice labels.");
        Require(registry.TryResolve(new(0, SkillProgramTargetKind.OtherLivingNotLastRoundFocus), out var previous) && previous.SequenceEqual([3]),
            "Previous-round focus must exclude only the same source owner's preceding round target.");
        queries.FocusHistory = Array.AsReadOnly(new[] { new DemonRoundFocusEvent(2, source, 3, 3) });
        Require(registry.TryResolve(new(0, SkillProgramTargetKind.OtherLivingNotLastRoundFocus), out var current) &&
                current.SequenceEqual([1]) && previous.SequenceEqual([3]),
            "Cached resolvers must requery changed facts without altering an earlier result.");
        Require(registry.TryResolve(new(0, SkillProgramTargetKind.ExclusiveRendeBeneficiary), out var rende) && rende.SequenceEqual([1, 3]),
            "Exclusive support must preserve exact skill/recipient matching, living owners, deduplication and sorted seats.");
        Require(registry.TryResolve(new(0, SkillProgramTargetKind.ExclusiveQingnangBeneficiary), out var healing) && healing.SequenceEqual([1]),
            "Exclusive healing must exclude hidden HP and unwounded supporters.");
        Require(registry.TryResolve(new(0, SkillProgramTargetKind.OtherLivingWithoutEndingSupport), out var ending) && ending.SequenceEqual([3]),
            "Ending support must use the current program frame and preserve participant order.");
        queries.Participants = Array.AsReadOnly(queries.Participants.Select(player => player with { Hp = player.MaxHp }).ToArray());
        Require(registry.TryResolve(new(0, SkillProgramTargetKind.ExclusiveQingnangBeneficiary), out var empty) && empty.Count == 0,
            "A handled empty target domain must not fall through to generic selection.");
        var reads = queries.QueryReads;
        Require(!registry.TryResolve(new(0, SkillProgramTargetKind.AnyLiving), out var unknown) && unknown.Count == 0 && queries.QueryReads == reads,
            "An unknown target kind must decline without querying another domain's facts.");

        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7 }, StandardContentRegistry.Create());
        var capabilities = (ProgramCapabilityRegistry)typeof(GameEngine).GetField("_programCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var production = capabilities.Get<IGuanYuTargetSelectionQuery>();
        var participants = production.Participants;
        ReadOnly(participants, new(99, true, true, 4, 4));
        ReadOnly(production.FocusHistory, new(99, source, 1, 3));
        ReadOnly(production.SupportHistory, new(99, 0, 1, "magic:rende"));
        Require(production.Participants.SequenceEqual(participants), "A rejected fact edit must not mutate engine participants.");
        Require(typeof(IGuanYuTargetSelectionQuery).GetProperties().All(property => property.PropertyType != typeof(GameEngine) &&
                    property.PropertyType != typeof(CharacterState) && !typeof(ResolutionFrame).IsAssignableFrom(property.PropertyType)) &&
                typeof(IProgramSelectionQueries).GetMethod("Get")!.GetGenericArguments().Single().GetGenericParameterConstraints().Contains(typeof(IProgramSelectionQuery)),
            "Selection modules must receive marker-constrained read-only projections rather than engine or mutable state APIs.");
    }

    // Uses the same immutable constructor fixture as ProgramSelectionTransformChecks;
    // these mechanism tests deliberately combine operations to check error precedence.
    private static ProgramSelectionContext Context(params SkillProgramEffectOp[] operations)
    {
        var condition = new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []);
        var effects = Array.AsReadOnly(operations.Select(operation => new SkillProgramEffect(operation, SkillProgramEffectTarget.Owner, 0, condition)).ToArray());
        var activation = new SkillProgramActivation("activate", 0, 3, 0, 3, SkillProgramTargetKind.AnyLiving, 1, condition, effects);
        var program = new SkillProgram("fixture:selection-validation", 1, "fixture-hash", "fixture-runtime",
            171, [], [], Array.AsReadOnly(new[] { activation }), [], [], []);
        return new(0, program, new(program.Id, program.GameplayHash, ProgramInstructionSourceKind.Activation,
            activation.Id, effects, activation: activation));
    }

    private static LegalAction Action() => new(LegalActionKind.UseProgramSkill, null, null, "Fixture");
    private static readonly List<string> _calls = [];
    private static readonly InvalidOperationException Failure = new("Original selection failure.");

    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class AcceptValidator : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) { _calls.Add("accept"); return null; } }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 20, SkillProgramEffectOp.Recover)]
    private sealed class RecoverFailureValidator : IProgramSelectionValidator
    {
        internal static readonly CommandError Error = new(CommandErrorCode.InvalidTarget, "Original recovery failure.");
        public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) { _calls.Add("recover"); return Error; }
    }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 30, SkillProgramEffectOp.Draw)]
    private sealed class DrawFailureValidator : IProgramSelectionValidator
    {
        internal static readonly CommandError Error = new(CommandErrorCode.InvalidCard, "Original draw failure.");
        public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) { _calls.Add("draw"); return Error; }
    }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 10)]
    private sealed class LaterStageValidator : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => throw Failure; }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class WrongContractValidator { }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10, (SkillProgramEffectOp)int.MaxValue)]
    private sealed class InvalidOperationValidator : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => null; }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10, SkillProgramEffectOp.Draw, SkillProgramEffectOp.Draw)]
    private sealed class DuplicateOperationValidator : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => null; }
    [ProgramSelectionValidator((ProgramSelectionValidationStage)int.MaxValue, 10)]
    private sealed class InvalidStageValidator : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => null; }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class ParameterizedValidator : IProgramSelectionValidator
    {
        internal ParameterizedValidator(GameEngine engine) { }
        public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => null;
    }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class StatefulValidator : IProgramSelectionValidator
    {
        private int _state;
        public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) { _state++; return null; }
    }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class GenericValidator<T> : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => null; }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class ThrowingConstructorValidator : IProgramSelectionValidator
    {
        internal ThrowingConstructorValidator() => throw Failure;
        public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => null;
    }
    [ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 10)]
    private sealed class ThrowingExecutionValidator : IProgramSelectionValidator
    { public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) => throw Failure; }

    [ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
    private sealed class DuplicateTargetResolver : IProgramTargetResolver
    { public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) => []; }
    [ProgramTargetResolver((SkillProgramTargetKind)int.MaxValue)]
    private sealed class InvalidTargetResolver : IProgramTargetResolver
    { public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) => []; }
    [ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus, SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
    private sealed class DuplicateKindTargetResolver : IProgramTargetResolver
    { public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) => []; }
    [ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
    private sealed class StatefulTargetResolver : IProgramTargetResolver
    {
        private int _state;
        public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) { _state++; return []; }
    }
    [ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
    private sealed class ParameterizedTargetResolver : IProgramTargetResolver
    {
        internal ParameterizedTargetResolver(GameEngine engine) { }
        public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) => [];
    }
    [ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
    private sealed class ThrowingConstructorTargetResolver : IProgramTargetResolver
    {
        internal ThrowingConstructorTargetResolver() => throw Failure;
        public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) => [];
    }
    [ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
    private sealed class ThrowingExecutionTargetResolver : IProgramTargetResolver
    { public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) => throw Failure; }

    private sealed class Queries : IProgramSelectionQueries, IOwnedRankSumSelectionQuery, IExactOwnedCountSelectionQuery,
        IBlackGiftSelectionQuery, IDynamicDiscardDamageSelectionQuery, IGuanYuTargetSelectionQuery
    {
        internal bool RankSumAllowed { get; set; } = true;
        internal bool ExactCountAllowed { get; set; } = true;
        internal bool GiftIssued { get; set; }
        internal int ExactReads { get; private set; }
        internal int DynamicReads { get; private set; }
        internal int QueryReads { get; private set; }
        public T Get<T>() where T : class, IProgramSelectionQuery
        { QueryReads++; return this is T query ? query : throw new NotSupportedException(typeof(T).FullName); }
        bool IOwnedRankSumSelectionQuery.CanStart(int ownerSeat, ProgramExecutionPlan plan) => RankSumAllowed;
        bool IExactOwnedCountSelectionQuery.CanStart(int ownerSeat, ProgramExecutionPlan plan) { ExactReads++; return ExactCountAllowed; }
        public bool WasIssued(int ownerSeat, string skillId) => GiftIssued;
        public bool CanStart(int ownerSeat, string skillId) { DynamicReads++; return false; }
        public bool CanTarget(int ownerSeat, string skillId, int targetSeat) => throw new InvalidOperationException("A failed initial cost must short-circuit target checks.");
        public int RoundNumber { get; set; }
        public long? CurrentProgramFrameId { get; set; }
        public IReadOnlyList<ProgramTargetParticipantFacts> Participants { get; set; } = [];
        public IReadOnlyList<DemonRoundFocusEvent> FocusHistory { get; set; } = [];
        public IReadOnlyList<DemonSupportGrantedEvent> SupportHistory { get; set; } = [];
    }

    private static void ReadOnly<T>(IReadOnlyList<T> facts, T value)
    {
        Require(facts is IList<T> { IsReadOnly: true }, "Query projections must expose immutable detached lists.");
        try { ((IList<T>)facts).Add(value); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Query facts accepted mutation.");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid selection registration was accepted.");
    }
    private static void AssertOriginalFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error) { Require(ReferenceEquals(error, Failure), "A selection failure was wrapped or replaced."); return; }
        throw new InvalidOperationException("The original selection failure was swallowed.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
