using System.Linq.Expressions;
using System.Reflection;
using CardGame.Core;

internal static class ProgramInteractionChecks
{
    public static void RegistrationAndDispatchPreserveBoundaries()
    {
        var methods = Methods(typeof(Host));
        var catalog = ProgramInteractionCatalog.Discover(methods);
        var reversed = ProgramInteractionCatalog.Discover(methods.Reverse());
        Require(catalog.Answers.SequenceEqual(reversed.Answers) &&
                catalog.Prefixes.SequenceEqual(reversed.Prefixes) &&
                catalog.AiChoices.SequenceEqual(reversed.AiChoices) &&
                catalog.BeforeAnswers.SequenceEqual(reversed.BeforeAnswers),
            "Reflection enumeration must not change interaction registration or precedence.");

        var production = ProgramInteractionCatalog.Discover(
            ProgramRuntimeModuleCatalog.Default.Methods.Concat(Methods(typeof(ProgramAiChoiceSelector))));
        foreach (var method in production.Answers.Values.Concat(production.Prefixes.Values)
                     .Concat(production.AiChoices.Values).Concat(production.BeforeAnswers).Distinct())
        {
            var parameters = method.GetParameters().Select(parameter => parameter.ParameterType);
            if (!method.IsStatic) parameters = parameters.Prepend(method.DeclaringType!);
            var signature = Expression.GetDelegateType(parameters.Append(method.ReturnType).ToArray());
            Require(method.CreateDelegate(signature).Method == method,
                "A registered production interaction must bind directly without reflection invocation.");
        }

        foreach (var names in new[]
                 {
                     new[] { "DuplicateAnswerA", "DuplicateAnswerB" },
                     new[] { "DuplicateAiA", "DuplicateAiB" },
                     new[] { "DuplicateBeforeA", "DuplicateBeforeB" },
                     new[] { "Prefix", "NestedPrefix" },
                     new[] { "Prefix", "OverlappingAnswer" },
                     new[] { "WrongAnswer" }, new[] { "WrongAi" }, new[] { "WrongBefore" },
                     new[] { "GenericAnswer" }, new[] { "EmptyAnswer" }, new[] { "UnknownAi" }
                 })
            Reject(() => ProgramInteractionCatalog.Discover(names.Select(name =>
                typeof(InvalidHost).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!)));

        var host = new Host();
        var router = Router(catalog, host);
        var choice = Choice("published", "exact");
        host.Expected = choice;
        router.Resolve(choice);
        Require(host.Calls.SequenceEqual(["before:10", "before:20", "answer"]),
            "Pre-handlers must run by explicit order before the exact answer handler.");
        var bindings = host.BindingCount;
        host.Calls.Clear();
        router.Resolve(choice);
        Require(host.BindingCount == bindings && host.Calls.SequenceEqual(["before:10", "before:20", "answer"]),
            "Reusing an answer must preserve dispatch while retaining this session's delegates.");

        foreach (var (id, expected) in new[]
                 {
                     ("stop-first", new[] { "before:10" }),
                     ("stop-second", new[] { "before:10", "before:20" })
                 })
        {
            host.Calls.Clear();
            router.Resolve(Choice(id, "exact"));
            Require(host.Calls.SequenceEqual(expected), "A handled pre-answer must stop every later handler.");
        }

        host.Calls.Clear();
        host.Expected = Choice("prefix", "family:selected");
        router.Resolve(host.Expected);
        Require(host.Calls.SequenceEqual(["before:10", "before:20", "family:selected"]),
            "A prefix handler must receive the original action and original published choice.");

        foreach (var action in new[] { "activate", "skip" })
        {
            host.Calls.Clear();
            host.Expected = Choice("group-" + action, action);
            router.Resolve(host.Expected);
            Require(host.Calls.SequenceEqual(["before:10", "before:20", action]),
                "Activation/group protocol must retain its own validation boundary after pre-handlers.");
        }
        Reject(() => router.Resolve(Choice("unknown", "Exact")));
        host.Expected = Choice("failure", "throw");
        try { router.Resolve(host.Expected); throw new Exception("The original rule failure was swallowed."); }
        catch (InvalidOperationException error)
        { Require(ReferenceEquals(error, Host.Failure), "Direct handler exceptions must not be wrapped or replaced."); }

        var decision = Decision(choice, Choice("other", "exact"));
        var frame = Frame();
        host.Expected = choice;
        host.ExpectedFrame = frame;
        foreach (var op in new[] { SkillProgramEffectOp.Draw, SkillProgramEffectOp.Recover,
                     SkillProgramEffectOp.LoseHp, SkillProgramEffectOp.Damage })
            Require(ReferenceEquals(router.SelectAi(decision, frame, Effect(op)), choice),
                "Every supported private static/instance AI signature must return the exact published choice.");
        var callsBeforeUnknown = host.Calls.ToArray();
        Reject(() => router.SelectAi(decision, frame, Effect(SkillProgramEffectOp.ChangeMaximumHp)));
        Require(host.Calls.SequenceEqual(callsBeforeUnknown), "An unknown AI opcode must never enter activation fallback.");

        var other = new Host { Expected = choice };
        Router(catalog, other).Resolve(choice);
        Require(other.Calls.SequenceEqual(["before:10", "before:20", "answer"]),
            "Sharing immutable registration must not bind a new session to an earlier host.");
    }

    public static void AiSelectionPreservesPublishedOrderAndConditionalPolicies()
    {
        var catalog = ProgramInteractionCatalog.Discover(Methods(typeof(ProgramAiChoiceSelector)));
        var router = new ProgramInteractionRouter(catalog, (method, type) => method.CreateDelegate(type),
            _ => throw new Exception("A pure AI policy reached answer activation."));
        var frame = Frame();
        var first = Choice("z", "unused", [1, 2], [0, 1], ("rank-sum", "10"));
        var tied = Choice("a", "unused", [3, 4], [2, 3], ("rank-sum", "10"));
        var lowerRank = Choice("b", "unused", [5, 6], [0], ("rank-sum", "9"));
        var fewerCards = Choice("c", "unused", [7], [], ("rank-sum", "99"));
        var decision = Decision(first, tied, lowerRank, fewerCards);
        var original = decision.Choices.ToArray();
        Require(ReferenceEquals(router.SelectAi(decision, frame, Effect(SkillProgramEffectOp.SelectSourceCard)), first) &&
                ReferenceEquals(router.SelectAi(decision, frame, Effect(SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill)), first) &&
                ReferenceEquals(router.SelectAi(decision, frame, Effect(SkillProgramEffectOp.CollectPublicPile)), first),
            "First-choice and equal-target-count policies must retain publication order, not alphabetize ties.");
        Require(ReferenceEquals(router.SelectAi(decision, frame, Effect(SkillProgramEffectOp.RevealTargetHandCard)), tied),
            "The explicit ordinal policy must preserve its distinct choice-ID ordering.");
        Require(ReferenceEquals(router.SelectAi(decision, frame,
                    Effect(SkillProgramEffectOp.SelectCardSubset, SkillProgramSubsetAiOrder.MostCardsThenRankSum)), tied),
            "Subset selection must prioritize card count, then rank sum, then ordinal ID.");
        Reject(() => router.SelectAi(decision, frame, Effect(SkillProgramEffectOp.SelectCardSubset)));
        Require(ReferenceEquals(router.SelectAi(decision, frame,
                    Effect(SkillProgramEffectOp.MoveBoundCards, destination: SkillProgramCardDestination.DrawPileBottom)), tied),
            "Bottom placement must retain its ordinal policy.");
        Reject(() => router.SelectAi(decision, frame,
            Effect(SkillProgramEffectOp.MoveBoundCards, destination: SkillProgramCardDestination.DiscardPile)));

        var pass = Choice("pass", "unused", parameters: [("branch", "pass")]);
        var nonPass = Choice("z-non-pass", "unused", parameters: [("branch", "use")]);
        Require(ReferenceEquals(router.SelectAi(Decision(pass, nonPass, tied), frame,
                    Effect(SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol)), nonPass),
            "The non-pass policy must choose the first published eligible option.");
        var ordinary = Choice("a", "unused", parameters: [("card-kind", nameof(CardKind.Slash))]);
        var drawLaterId = Choice("z", "unused", parameters: [("card-kind", nameof(CardKind.DrawTwo))]);
        var drawEarlierId = Choice("b", "unused", parameters: [("card-kind", nameof(CardKind.DrawTwo))]);
        Require(ReferenceEquals(router.SelectAi(Decision(ordinary, drawLaterId, drawEarlierId), frame,
                    Effect(SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)), drawEarlierId),
            "Draw Two preference must precede ordinal tie-breaking.");

        var empty = Choice("00", "unused");
        var multiple = Choice("01", "unused", [10, 11]);
        var coveredZ = Choice("z", "unused", [12]);
        var coveredA = Choice("a", "unused", [13]);
        var coverage = Decision(empty, multiple, coveredZ, coveredA);
        var coverageEffect = Effect(SkillProgramEffectOp.SelectAndMoveOwnedCard, coverageBind: "coverage");
        var inspected = new List<int>();
        Require(ReferenceEquals(ProgramAiChoiceSelector.SelectByCoverage(coverage, coverageEffect, card =>
                { inspected.Add(card); return true; }), coveredA) && inspected.SequenceEqual([12, 13]),
            "Coverage ranking must only inspect singleton payments and use ordinal ties among matching cards.");
        Require(ReferenceEquals(ProgramAiChoiceSelector.SelectByCoverage(coverage, coverageEffect, _ => false), empty),
            "When no singleton reduces coverage, the old ordinal fallback must remain unchanged.");
        Require(ReferenceEquals(ProgramAiChoiceSelector.SelectByCoverage(Decision(coveredZ, empty, coveredA),
                Effect(SkillProgramEffectOp.SelectAndMoveOwnedCard),
                _ => throw new Exception("Disabled coverage policy inspected a card.")), coveredZ),
            "Without a coverage binding, selection must return the first published choice without sorting or querying cards.");
        Require(decision.Choices.Zip(original).All(pair => ReferenceEquals(pair.First, pair.Second)),
            "AI ranking must neither replace published choices nor reorder the prompt's original collection.");
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    private static ProgramInteractionRouter Router(ProgramInteractionCatalog catalog, Host host) =>
        new(catalog, (method, type) =>
        {
            host.BindingCount++;
            return method.IsStatic ? method.CreateDelegate(type) : method.CreateDelegate(type, host);
        }, host.Activation);

    private static ProgramSkillFrame Frame() => new(41, 0, "fixture:interaction", "run", "hash", 0, [], []);

    private static PendingDecision Decision(params PromptChoice[] choices) =>
        new(DecisionKind.ProgramTrigger, 0, "Fixture", [], []) { PromptId = new(7), Revision = 9, Choices = choices };

    private static PromptChoice Choice(string id, string action, IReadOnlyList<int>? cards = null,
        IReadOnlyList<int>? targets = null, params (string Key, string Value)[] parameters)
    {
        var values = parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        values.Add("program-action", action);
        return new(new(id), id, cards ?? [], targets ?? [], values);
    }

    private static SkillProgramEffect Effect(SkillProgramEffectOp op, SkillProgramSubsetAiOrder? order = null,
        SkillProgramCardDestination? destination = null, string? coverageBind = null) =>
        new(op, SkillProgramEffectTarget.Owner, 0, new(SkillProgramConditionKind.Always, 0, []),
            aiOrder: order, destination: destination, coverageResultBind: coverageBind);

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("An invalid interaction contract or route was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Host
    {
        internal static readonly InvalidOperationException Failure = new("original handler failure");
        internal readonly List<string> Calls = [];
        internal int BindingCount;
        internal PromptChoice? Expected;
        internal ProgramSkillFrame? ExpectedFrame;

        [ProgramAnswerBefore(20)]
        private bool BeforeSecond(PromptChoice choice) { Calls.Add("before:20"); return choice.Id.Value == "stop-second"; }
        [ProgramAnswerBefore(10)]
        private bool BeforeFirst(PromptChoice choice) { Calls.Add("before:10"); return choice.Id.Value == "stop-first"; }
        [ProgramAnswer("exact")]
        private void Answer(PromptChoice choice) { Require(ReferenceEquals(choice, Expected), "Answer reconstructed the published choice."); Calls.Add("answer"); }
        [ProgramAnswerPrefix("family:")]
        private void Prefix(PromptChoice choice, string action) { Require(ReferenceEquals(choice, Expected), "Prefix lost the published choice."); Calls.Add(action); }
        [ProgramAnswer("throw")]
        private static void Throw(PromptChoice choice) => throw Failure;
        [ProgramAiChoice(SkillProgramEffectOp.Draw)]
        private static PromptChoice AiDecision(PendingDecision decision) => decision.Choices[0];
        [ProgramAiChoice(SkillProgramEffectOp.Recover)]
        private PromptChoice AiFrame(ProgramSkillFrame frame) { Require(ReferenceEquals(frame, ExpectedFrame), "AI frame changed."); return Expected!; }
        [ProgramAiChoice(SkillProgramEffectOp.LoseHp)]
        private PromptChoice AiPair(PendingDecision decision, ProgramSkillFrame frame) { Require(ReferenceEquals(frame, ExpectedFrame), "AI frame changed."); return decision.Choices[0]; }
        [ProgramAiChoice(SkillProgramEffectOp.Damage)]
        private PromptChoice AiFull(PendingDecision decision, ProgramSkillFrame frame, SkillProgramEffect effect) { Require(ReferenceEquals(frame, ExpectedFrame) && effect.Op == SkillProgramEffectOp.Damage, "AI instruction context changed."); return decision.Choices[0]; }

        internal void Activation(PromptChoice choice)
        {
            var action = choice.Parameters["program-action"];
            if (action is not ("activate" or "skip")) throw new InvalidOperationException("Unknown activation.");
            Require(ReferenceEquals(choice, Expected), "Activation lost the published choice.");
            Calls.Add(action);
        }
    }

    private static class InvalidHost
    {
        [ProgramAnswer("same")] private static void DuplicateAnswerA(PromptChoice choice) { }
        [ProgramAnswer("same")] private static void DuplicateAnswerB(PromptChoice choice) { }
        [ProgramAiChoice(SkillProgramEffectOp.Draw)] private static PromptChoice DuplicateAiA(PendingDecision decision) => decision.Choices[0];
        [ProgramAiChoice(SkillProgramEffectOp.Draw)] private static PromptChoice DuplicateAiB(PendingDecision decision) => decision.Choices[0];
        [ProgramAnswerBefore(1)] private static bool DuplicateBeforeA(PromptChoice choice) => false;
        [ProgramAnswerBefore(1)] private static bool DuplicateBeforeB(PromptChoice choice) => false;
        [ProgramAnswerPrefix("prefix:")] private static void Prefix(PromptChoice choice) { }
        [ProgramAnswerPrefix("prefix:nested:")] private static void NestedPrefix(PromptChoice choice) { }
        [ProgramAnswer("prefix:exact")] private static void OverlappingAnswer(PromptChoice choice) { }
        [ProgramAnswer("wrong")] private static bool WrongAnswer(PromptChoice choice) => false;
        [ProgramAiChoice(SkillProgramEffectOp.Draw)] private static void WrongAi(PendingDecision decision) { }
        [ProgramAnswerBefore(2)] private static bool WrongBefore(string choice) => false;
        [ProgramAnswer("generic")] private static void GenericAnswer<T>(PromptChoice choice) { }
        [ProgramAnswer] private static void EmptyAnswer(PromptChoice choice) { }
        [ProgramAiChoice((SkillProgramEffectOp)int.MaxValue)] private static PromptChoice UnknownAi(PendingDecision decision) => decision.Choices[0];
    }
}
