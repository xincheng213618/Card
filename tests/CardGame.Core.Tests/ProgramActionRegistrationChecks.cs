using System.Linq.Expressions;
using System.Reflection;
using CardGame.Core;

internal static class ProgramActionRegistrationChecks
{
    public static void RegistrationAndDispatchPreserveBoundaries()
    {
        var methods = Methods(typeof(Host));
        var catalog = new ProgramActionCatalog(methods);
        var reversed = new ProgramActionCatalog(methods.Reverse());
        foreach (var phase in Enum.GetValues<ProgramActionSourcePhase>())
            Require(catalog.Sources[phase].SequenceEqual(reversed.Sources[phase]),
                "Source precedence must not depend on reflection enumeration.");
        foreach (var kind in Enum.GetValues<ProgramActionHandlerKind>())
            Require(catalog.Handlers[kind].SequenceEqual(reversed.Handlers[kind]),
                "Command precedence must not depend on reflection enumeration.");
        foreach (var phase in Enum.GetValues<ProgramCardActionPhase>())
            Require(catalog.Augmentations[phase].SequenceEqual(reversed.Augmentations[phase]),
                "Card-action augmentation precedence must not depend on reflection enumeration.");

        var production = new ProgramActionCatalog(ProgramRuntimeModuleCatalog.Default.Methods);
        foreach (var method in production.Sources.Values.SelectMany(items => items)
                     .Concat(production.Handlers.Values.SelectMany(items => items))
                     .Concat(production.Augmentations.Values.SelectMany(items => items)))
        {
            var parameters = method.GetParameters().Select(parameter => parameter.ParameterType);
            if (!method.IsStatic) parameters = parameters.Prepend(method.DeclaringType!);
            var signature = Expression.GetDelegateType(parameters.Append(method.ReturnType).ToArray());
            Require(method.CreateDelegate(signature).Method == method,
                "Every production action registration must bind directly, including out-parameter handlers.");
        }

        foreach (var phase in Enum.GetValues<ProgramActionSourcePhase>())
        {
            var list = (IList<MethodInfo>)catalog.Sources[phase];
            RejectMutation(() => list[0] = list[0]);
        }
        foreach (var kind in Enum.GetValues<ProgramActionHandlerKind>())
        {
            var list = (IList<MethodInfo>)catalog.Handlers[kind];
            RejectMutation(() => list.Clear());
        }
        foreach (var phase in Enum.GetValues<ProgramCardActionPhase>())
        {
            var list = (IList<MethodInfo>)catalog.Augmentations[phase];
            RejectMutation(() => list.Clear());
        }
        RejectMutation(() => ((IDictionary<ProgramActionSourcePhase, IReadOnlyList<MethodInfo>>)catalog.Sources).Clear());
        RejectMutation(() => ((IDictionary<ProgramActionHandlerKind, IReadOnlyList<MethodInfo>>)catalog.Handlers).Clear());
        RejectMutation(() => ((IDictionary<ProgramCardActionPhase, IReadOnlyList<MethodInfo>>)catalog.Augmentations).Clear());

        foreach (var names in new[]
                 {
                     new[] { "DuplicateSourceA", "DuplicateSourceB" },
                     new[] { "DuplicateHandlerA", "DuplicateHandlerB" },
                     new[] { "UnknownSourcePhase" }, new[] { "UnknownHandlerKind" },
                     new[] { "NegativeSourceOrder" }, new[] { "NegativeHandlerOrder" },
                     new[] { "StaticSource" }, new[] { "StaticHandler" },
                     new[] { "GenericSource" }, new[] { "GenericHandler" },
                     new[] { "WrongSourceReturn" }, new[] { "WrongSourceParameters" },
                     new[] { "WrongHandlerReturn" }, new[] { "WrongExecuteParameters" },
                     new[] { "WrongContributionParameters" }, new[] { "MissingOut" },
                     new[] { "RefInsteadOfOut" }, new[] { "WrongOutType" },
                     new[] { "DuplicateAugmentationA", "DuplicateAugmentationB" },
                     new[] { "UnknownAugmentationPhase" }, new[] { "NegativeAugmentationOrder" },
                     new[] { "StaticAugmentation" }, new[] { "GenericAugmentation" },
                     new[] { "WrongAugmentationReturn" }, new[] { "WrongAugmentationPrefix" },
                     new[] { "WrongAugmentationThird" }, new[] { "ReversedFourParameters" },
                     new[] { "RepeatedAugmentationParameter" }, new[] { "RefAugmentationParameter" }
                 })
            Reject(() => new ProgramActionCatalog(methods.Concat(names.Select(name =>
                typeof(InvalidHost).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!))));
        Reject(() => new ProgramActionCatalog([]));
        foreach (var phase in Enum.GetValues<ProgramActionSourcePhase>())
            Reject(() => new ProgramActionCatalog(methods.Where(method =>
                method.GetCustomAttribute<ProgramActionSourceAttribute>()?.Phase != phase)));
        foreach (var kind in Enum.GetValues<ProgramActionHandlerKind>())
            Reject(() => new ProgramActionCatalog(methods.Where(method =>
                method.GetCustomAttribute<ProgramActionHandlerAttribute>()?.Kind != kind)));
        Reject(() => ProgramActionCatalog.DelegateType((ProgramActionHandlerKind)int.MaxValue));

        var owner = Player(2);
        var provider = Player(1);
        var cards = Array.AsReadOnly(new[] { 8, 3 });
        var targets = Array.AsReadOnly(new[] { 4, 0 });
        var host = new Host { Owner = owner, Provider = provider, Cards = cards, Targets = targets };
        var router = Router(catalog, host);
        Require(host.Calls.Count == 0, "Binding action delegates must not invoke a source or handler.");

        host.AugmentationCards = Array.AsReadOnly(new[] { new Card(8, CardKind.Slash, Suit.Club, 7) });
        foreach (var selectedCardId in new int?[] { 8, null })
        {
            host.Calls.Clear();
            host.AugmentationActions = [host.TailAction];
            host.SelectedCardId = selectedCardId;
            router.Augment(ProgramCardActionPhase.BeforeProgram, host.AugmentationActions, owner, host.AugmentationCards, selectedCardId);
            Require(host.Calls.SequenceEqual(["augment:cards", "augment:two"]) &&
                    host.AugmentationActions.SequenceEqual([host.TailAction, host.PublicAction, host.PublicSecondAction]),
                "Before-program augmentation must preserve exact list/card inputs and handler order.");
            router.Augment(ProgramCardActionPhase.AfterProgram, host.AugmentationActions, owner, host.AugmentationCards, selectedCardId);
            Require(host.Calls.SequenceEqual(["augment:cards", "augment:two", "augment:four", "augment:selected"]) &&
                    ReferenceEquals(host.AugmentationActions[0], host.TailAction) &&
                    ReferenceEquals(host.AugmentationActions[1], host.PublicAction) &&
                    ReferenceEquals(host.AugmentationActions[2], host.PublicSecondAction) &&
                    ReferenceEquals(host.AugmentationActions[3], host.RoundAction) &&
                    ReferenceEquals(host.AugmentationActions[4], host.TailAction),
                "After-program augmentation must bind all four supported signatures, preserve nullable selection, and retain action identity.");
        }
        var noAugmentation = new ProgramActionCatalog(methods.Where(method =>
            method.GetCustomAttribute<ProgramCardActionAttribute>() is null));
        var plainRouter = Router(noAugmentation, host);
        host.Calls.Clear();
        var originalAugmentationCount = host.AugmentationActions.Count;
        foreach (var phase in Enum.GetValues<ProgramCardActionPhase>())
            plainRouter.Augment(phase, host.AugmentationActions, owner, host.AugmentationCards, null);
        Require(host.Calls.Count == 0 && host.AugmentationActions.Count == originalAugmentationCount,
            "An otherwise complete synthetic catalog may omit all optional augmentations.");
        host.ThrowAugmentation = true;
        SameFailure(() => router.Augment(ProgramCardActionPhase.BeforeProgram,
            host.AugmentationActions, owner, host.AugmentationCards, null));
        Require(host.Calls.SequenceEqual(["augment:cards"]), "An augmentation failure must not reach later handlers.");
        host.ThrowAugmentation = false;
        host.Calls.Clear();

        var source = router.Build(ProgramActionSourcePhase.Cardless, owner);
        Require(host.Calls.Count == 0, "Creating an action sequence must not invoke its sources.");
        var iterator = source.GetEnumerator();
        Require(host.Calls.Count == 0, "Obtaining a source enumerator must not enumerate any action.");
        Require(iterator.MoveNext() && ReferenceEquals(iterator.Current, host.PublicAction) &&
                host.Calls.SequenceEqual(["public:invoke", "public:first"]),
            "The first MoveNext must expose the original first action without enumerating later actions or sources.");
        iterator.Dispose();
        Require(host.Calls.SequenceEqual(["public:invoke", "public:first", "public:dispose"]),
            "Disposing a partial action query must dispose only its current source.");

        host.Calls.Clear();
        var actions = router.Build(ProgramActionSourcePhase.Cardless, owner).ToArray();
        Require(actions.Length == 3 && ReferenceEquals(actions[0], host.PublicAction) &&
                ReferenceEquals(actions[1], host.PublicSecondAction) && ReferenceEquals(actions[2], host.RoundAction) &&
                host.Calls.SequenceEqual(["public:invoke", "public:first", "public:second", "public:complete", "public:dispose", "round:invoke"]),
            "Sources must finish lazily in their own order while preserving every published action identity.");
        host.Calls.Clear();
        Require(ReferenceEquals(router.Build(ProgramActionSourcePhase.ProgramTail, owner).Single(), host.TailAction) &&
                host.Calls.SequenceEqual(["tail:invoke"]),
            "The program-tail source phase must remain separate from cardless sources.");

        foreach (var (route, expected) in new[]
                 {
                     ("round", new[] { "submit:round" }),
                     ("public", new[] { "submit:round", "submit:public" }),
                     ("both", new[] { "submit:round" }),
                     ("unknown", new[] { "submit:round", "submit:public" })
                 })
        {
            host.Calls.Clear();
            host.Command = new(owner.Seat, "fixture:actions", route, cards, targets, 7, new PromptId(9));
            var handled = router.TrySubmit(host.Command, out var result);
            Require(handled == (route != "unknown") && host.Calls.SequenceEqual(expected) &&
                    (handled ? ReferenceEquals(result, host.Result) : result is null),
                "Submit precedence must be independent of reversed source order, short-circuit on success, and fall through cleanly.");
        }

        foreach (var route in new[] { "round", "public", "both", "unknown" })
        {
            host.Calls.Clear();
            host.ExpectedAction = Action(route);
            var handled = router.TryExecute(owner, host.ExpectedAction, cards, targets);
            Require(handled == (route != "unknown") && host.Calls.SequenceEqual(
                    route is "round" or "both" ? ["execute:round"] : ["execute:round", "execute:public"]),
                "Execute must retain command precedence, original inputs, success short-circuit, and unknown fallthrough.");
            host.Calls.Clear();
            handled = router.TryContribute(provider, owner, host.ExpectedAction, 8);
            Require(handled == (route != "unknown") && host.Calls.SequenceEqual(
                    route is "round" or "both" ? ["contribute:round"] : ["contribute:round", "contribute:public"]),
                "Contribution handlers must receive the original provider, owner, action and card in their own order.");
        }

        host.Calls.Clear();
        host.Command = new(owner.Seat, "fixture:actions", "throw", cards, targets, 7);
        SameFailure(() => router.TrySubmit(host.Command, out _));
        Require(host.Calls.SequenceEqual(["submit:round"]), "A failing submit handler must not reach later handlers.");
        host.Calls.Clear();
        host.ExpectedAction = Action("throw");
        SameFailure(() => router.TryExecute(owner, host.ExpectedAction, cards, targets));
        Require(host.Calls.SequenceEqual(["execute:round"]), "A failing execute handler must not reach later handlers.");
        host.Calls.Clear();
        SameFailure(() => router.TryContribute(provider, owner, host.ExpectedAction, 8));
        Require(host.Calls.SequenceEqual(["contribute:round"]), "A failing contribution must not reach later handlers.");
        host.Calls.Clear();
        host.ThrowSource = true;
        var throwingSource = router.Build(ProgramActionSourcePhase.Cardless, owner);
        Require(host.Calls.Count == 0, "A source invocation failure must remain deferred until enumeration.");
        SameFailure(() => throwingSource.ToArray());
        Require(host.Calls.SequenceEqual(["public:invoke"]), "A source failure must not eagerly enter later sources.");
        host.ThrowSource = false;
        host.ThrowAfterFirst = true;
        host.Calls.Clear();
        using (var throwingIterator = router.Build(ProgramActionSourcePhase.Cardless, owner).GetEnumerator())
        {
            Require(throwingIterator.MoveNext() && ReferenceEquals(throwingIterator.Current, host.PublicAction),
                "The original first action must remain available before a later enumeration failure.");
            SameFailure(() => throwingIterator.MoveNext());
            Require(host.Calls.SequenceEqual(["public:invoke", "public:first", "public:dispose"]),
                "An enumeration failure must dispose its source and preserve the original failure without invoking later sources.");
        }

        host.ThrowAfterFirst = false;
        host.Calls.Clear();
        var other = new Host { Owner = owner, Provider = provider, Cards = cards, Targets = targets, ExpectedAction = Action("round") };
        Require(Router(catalog, other).TryExecute(owner, other.ExpectedAction, cards, targets) &&
                other.Calls.SequenceEqual(["execute:round"]) && host.Calls.Count == 0,
            "Shared catalog metadata must bind each router to its own instance.");
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static ProgramActionRouter Router(ProgramActionCatalog catalog, Host host) =>
        new(catalog, (method, type) => method.CreateDelegate(type, host));
    private static LegalAction Action(string route) =>
        new(LegalActionKind.UseProgramSkill, null, null, route)
        { ProgramSkillId = "fixture:actions", ProgramActivationId = route };
    private static CharacterState Player(int seat) => new()
    {
        Seat = seat, Name = "fixture", IsHuman = true, Role = Role.Rebel, RoleRevealed = false,
        General = new("fixture", "Fixture", "fixture", []), GeneralSelected = true,
        GeneralRevealed = true, MaxHp = 4, Hp = 4
    };
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("An invalid action registration was accepted.");
    }
    private static void RejectMutation(Action action)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new Exception("Action catalog metadata is mutable.");
    }
    private static void SameFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException failure)
        { Require(ReferenceEquals(failure, Host.Failure), "Action failures must retain their original exception instance."); return; }
        throw new Exception("An action failure was swallowed.");
    }

    private sealed class Host
    {
        internal static readonly InvalidOperationException Failure = new("original action failure");
        internal readonly List<string> Calls = [];
        internal readonly LegalAction PublicAction = Action("public");
        internal readonly LegalAction PublicSecondAction = Action("public-second");
        internal readonly LegalAction RoundAction = Action("round");
        internal readonly LegalAction TailAction = Action("tail");
        internal readonly CommandResult Result = new(true, null, 7, null!);
        internal CharacterState Owner = null!, Provider = null!;
        internal IReadOnlyList<int> Cards = null!, Targets = null!;
        internal UseProgramSkillCommand Command = null!;
        internal LegalAction ExpectedAction = null!;
        internal List<LegalAction> AugmentationActions = null!;
        internal IReadOnlyList<Card> AugmentationCards = null!;
        internal int? SelectedCardId;
        internal bool ThrowSource, ThrowAfterFirst, ThrowAugmentation;

        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 20)]
        private void AugmentTwo(List<LegalAction> actions, CharacterState actor)
        {
            Require(ReferenceEquals(actions, AugmentationActions) && ReferenceEquals(actor, Owner), "Two-parameter augmentation changed its inputs.");
            Calls.Add("augment:two"); actions.Add(PublicSecondAction);
        }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 10)]
        private void AugmentCards(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> cards)
        {
            Require(ReferenceEquals(actions, AugmentationActions) && ReferenceEquals(actor, Owner) &&
                    ReferenceEquals(cards, AugmentationCards), "Card augmentation reconstructed its inputs.");
            Calls.Add("augment:cards");
            if (ThrowAugmentation) throw Failure;
            actions.Add(PublicAction);
        }
        [ProgramCardAction(ProgramCardActionPhase.AfterProgram, 20)]
        private void AugmentSelected(List<LegalAction> actions, CharacterState actor, int? selectedCardId)
        {
            Require(ReferenceEquals(actions, AugmentationActions) && ReferenceEquals(actor, Owner) &&
                    selectedCardId == SelectedCardId, "Selected-card augmentation changed its nullable input.");
            Calls.Add("augment:selected"); actions.Add(TailAction);
        }
        [ProgramCardAction(ProgramCardActionPhase.AfterProgram, 10)]
        private void AugmentFour(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> cards, int? selectedCardId)
        {
            Require(ReferenceEquals(actions, AugmentationActions) && ReferenceEquals(actor, Owner) &&
                    ReferenceEquals(cards, AugmentationCards) && selectedCardId == SelectedCardId,
                "Four-parameter augmentation changed its ordered inputs.");
            Calls.Add("augment:four"); actions.Add(RoundAction);
        }

        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 10)]
        private IEnumerable<LegalAction> SourcePublic(CharacterState owner)
        {
            Require(ReferenceEquals(owner, Owner), "Source owner was replaced.");
            Calls.Add("public:invoke");
            if (ThrowSource) throw Failure;
            return Enumerate();
            IEnumerable<LegalAction> Enumerate()
            {
                try
                {
                    Calls.Add("public:first"); yield return PublicAction;
                    if (ThrowAfterFirst) throw Failure;
                    Calls.Add("public:second"); yield return PublicSecondAction;
                    Calls.Add("public:complete");
                }
                finally { Calls.Add("public:dispose"); }
            }
        }
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 20)]
        private IEnumerable<LegalAction> SourceRound(CharacterState owner)
        { Require(ReferenceEquals(owner, Owner), "Source owner was replaced."); Calls.Add("round:invoke"); return [RoundAction]; }
        [ProgramActionSource(ProgramActionSourcePhase.ProgramTail, 10)]
        private IEnumerable<LegalAction> SourceTail(CharacterState owner)
        { Require(ReferenceEquals(owner, Owner), "Tail owner was replaced."); Calls.Add("tail:invoke"); return [TailAction]; }

        [ProgramActionHandler(ProgramActionHandlerKind.Submit, 10)]
        private bool SubmitRound(UseProgramSkillCommand command, out CommandResult result) => Submit(command, out result, "round");
        [ProgramActionHandler(ProgramActionHandlerKind.Submit, 20)]
        private bool SubmitPublic(UseProgramSkillCommand command, out CommandResult result) => Submit(command, out result, "public");
        private bool Submit(UseProgramSkillCommand command, out CommandResult result, string route)
        {
            Require(ReferenceEquals(command, Command), "Submit reconstructed its command.");
            Calls.Add("submit:" + route); result = Result;
            if (command.ActivationId == "throw") throw Failure;
            return command.ActivationId == route || command.ActivationId == "both";
        }
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 10)]
        private bool ExecuteRound(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
            Execute(owner, action, cards, targets, "round");
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 20)]
        private bool ExecutePublic(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
            Execute(owner, action, cards, targets, "public");
        private bool Execute(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets, string route)
        {
            Require(ReferenceEquals(owner, Owner) && ReferenceEquals(action, ExpectedAction) &&
                    ReferenceEquals(cards, Cards) && ReferenceEquals(targets, Targets), "Execute changed its published inputs.");
            Calls.Add("execute:" + route);
            if (action.ProgramActivationId == "throw") throw Failure;
            return action.ProgramActivationId == route || action.ProgramActivationId == "both";
        }
        [ProgramActionHandler(ProgramActionHandlerKind.Contribution, 10)]
        private bool ContributeRound(CharacterState provider, CharacterState owner, LegalAction action, int cardId) =>
            Contribute(provider, owner, action, cardId, "round");
        [ProgramActionHandler(ProgramActionHandlerKind.Contribution, 20)]
        private bool ContributePublic(CharacterState provider, CharacterState owner, LegalAction action, int cardId) =>
            Contribute(provider, owner, action, cardId, "public");
        private bool Contribute(CharacterState provider, CharacterState owner, LegalAction action, int cardId, string route)
        {
            Require(ReferenceEquals(provider, Provider) && ReferenceEquals(owner, Owner) &&
                    ReferenceEquals(action, ExpectedAction) && cardId == 8, "Contribution changed its published inputs.");
            Calls.Add("contribute:" + route);
            if (action.ProgramActivationId == "throw") throw Failure;
            return action.ProgramActivationId == route || action.ProgramActivationId == "both";
        }
    }

    private sealed class InvalidHost
    {
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 1000)]
        private IEnumerable<LegalAction> DuplicateSourceA(CharacterState owner) => [];
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 1000)]
        private IEnumerable<LegalAction> DuplicateSourceB(CharacterState owner) => [];
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 1000)]
        private bool DuplicateHandlerA(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => false;
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 1000)]
        private bool DuplicateHandlerB(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => false;
        [ProgramActionSource((ProgramActionSourcePhase)int.MaxValue, 1000)]
        private IEnumerable<LegalAction> UnknownSourcePhase(CharacterState owner) => [];
        [ProgramActionHandler((ProgramActionHandlerKind)int.MaxValue, 1000)]
        private bool UnknownHandlerKind(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => false;
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, -1)]
        private IEnumerable<LegalAction> NegativeSourceOrder(CharacterState owner) => [];
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, -1)]
        private bool NegativeHandlerOrder(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => false;
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 1000)]
        private static IEnumerable<LegalAction> StaticSource(CharacterState owner) => [];
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 1000)]
        private static bool StaticHandler(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => false;
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 1000)]
        private IEnumerable<LegalAction> GenericSource<T>(CharacterState owner) => [];
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 1000)]
        private bool GenericHandler<T>(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => false;
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 1000)]
        private IReadOnlyList<LegalAction> WrongSourceReturn(CharacterState owner) => [];
        [ProgramActionSource(ProgramActionSourcePhase.Cardless, 1000)]
        private IEnumerable<LegalAction> WrongSourceParameters(int ownerSeat) => [];
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 1000)]
        private void WrongHandlerReturn(CharacterState owner, LegalAction action, IReadOnlyList<int> cards, IReadOnlyList<int> targets) { }
        [ProgramActionHandler(ProgramActionHandlerKind.Execute, 1000)]
        private bool WrongExecuteParameters(CharacterState owner, LegalAction action) => false;
        [ProgramActionHandler(ProgramActionHandlerKind.Contribution, 1000)]
        private bool WrongContributionParameters(CharacterState provider, CharacterState owner, LegalAction action, string cardId) => false;
        [ProgramActionHandler(ProgramActionHandlerKind.Submit, 1000)]
        private bool MissingOut(UseProgramSkillCommand command, CommandResult result) => false;
        [ProgramActionHandler(ProgramActionHandlerKind.Submit, 1000)]
        private bool RefInsteadOfOut(UseProgramSkillCommand command, ref CommandResult result) => false;
        [ProgramActionHandler(ProgramActionHandlerKind.Submit, 1000)]
        private bool WrongOutType(UseProgramSkillCommand command, out int result) { result = 0; return false; }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void DuplicateAugmentationA(List<LegalAction> actions, CharacterState actor) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void DuplicateAugmentationB(List<LegalAction> actions, CharacterState actor) { }
        [ProgramCardAction((ProgramCardActionPhase)int.MaxValue, 1000)]
        private void UnknownAugmentationPhase(List<LegalAction> actions, CharacterState actor) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, -1)]
        private void NegativeAugmentationOrder(List<LegalAction> actions, CharacterState actor) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private static void StaticAugmentation(List<LegalAction> actions, CharacterState actor) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void GenericAugmentation<T>(List<LegalAction> actions, CharacterState actor) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private bool WrongAugmentationReturn(List<LegalAction> actions, CharacterState actor) => false;
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void WrongAugmentationPrefix(CharacterState actor, List<LegalAction> actions) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void WrongAugmentationThird(List<LegalAction> actions, CharacterState actor, Card card) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void ReversedFourParameters(List<LegalAction> actions, CharacterState actor, int? selectedCardId, IReadOnlyList<Card> cards) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void RepeatedAugmentationParameter(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> cards, IReadOnlyList<Card> repeatedCards) { }
        [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 1000)]
        private void RefAugmentationParameter(List<LegalAction> actions, CharacterState actor, ref int? selectedCardId) { }
    }
}
