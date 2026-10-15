using System.Reflection;
using CardGame.Core;
using AttackReceipt = CardGame.Core.GameEngine.AttackCompletionReceipt;

internal static class ProgramNativeReturnChecks
{
    public static void OrderedNativeBoundariesPreservePreparationAndIsolation()
    {
        var methods = Methods(typeof(ReturnFixture));
        var catalog = new ProgramNativeReturnCatalog(methods.Reverse());
        var natural = new ProgramNativeReturnCatalog(methods);
        Require(catalog.UseReturns.Select(entry => entry.Method.Name).SequenceEqual(natural.UseReturns.Select(entry => entry.Method.Name)) &&
            catalog.AttackReturns.Select(entry => entry.Method.Name).SequenceEqual(natural.AttackReturns.Select(entry => entry.Method.Name)),
            "Reflection enumeration must not change native return precedence.");
        var first = new ReturnFixture(catalog) { FirstPrepared = true, SecondPrepared = true };
        var second = new ReturnFixture(catalog) { SecondPrepared = true };
        var use = Use(1);
        first.Protocol.ValidateUse(ref use);
        Require(use.CardId == 8, "The validation after a native payment must receive the refreshed use.");
        var completion = first.Protocol.PrepareUse(use, hpLossMaterialReturn: true);
        first.Trace.Add("pop");
        Require(first.Protocol.ReturnUse(completion), "A successful prepared return must claim the retired native use.");
        Require(first.Trace.SequenceEqual(new[] { "refresh", "validate:8", "prepare-first", "prepare-second", "prepare-only", "pop", "tail:True", "exclusive", "return-first" }),
            "Every preparation must run before pop; the first matching exclusive continuation stops later tails.");
        var beforeRepeat = first.Trace.ToArray();
        Reject(() => first.Protocol.ReturnUse(completion));
        Require(first.Trace.SequenceEqual(beforeRepeat), "Repeating the same completion cannot re-enter a paid return or its tails.");
        Require(second.Trace.Count == 0, "Immutable registration metadata must not retain another match's callbacks or preparation flags.");

        var secondCompletion = second.Protocol.PrepareUse(Use(2), hpLossMaterialReturn: false);
        second.Trace.Add("pop");
        second.Protocol.ReturnUse(secondCompletion);
        Require(second.Trace.SequenceEqual(new[] { "prepare-first", "prepare-second", "prepare-only", "pop", "tail:False", "exclusive", "return-second" }),
            "Each completion must keep its own preparation results and choose the next eligible paired return.");

        var tails = new ReturnFixture(catalog);
        tails.Protocol.ReturnUse(tails.Protocol.PrepareUse(Use(3), false));
        Require(tails.Trace.SequenceEqual(new[] { "prepare-first", "prepare-second", "prepare-only", "tail:False", "exclusive", "tail-frame", "tail-id:3", "tail-empty" }),
            "Unclaimed native uses must execute every void tail in order, including frame, id and parameterless callbacks.");
        var exclusive = new ReturnFixture(catalog) { ExclusiveUse = true, FirstPrepared = true };
        exclusive.Protocol.ReturnUse(exclusive.Protocol.PrepareUse(Use(4), false));
        Require(exclusive.Trace.Last() == "exclusive" && !exclusive.Trace.Contains("return-first"),
            "A normal exclusive return must precede a later successful prepared return.");

        var attack = new ReturnFixture(catalog) { ExclusiveAttack = true };
        var receipt = new AttackReceipt(5, false, null, null);
        attack.Protocol.ReturnAttack(NativeAttackReturnPhase.BeforeCleanup, receipt);
        attack.Trace.Add("cleanup");
        attack.Protocol.ReturnAttack(NativeAttackReturnPhase.AfterCleanup, receipt);
        attack.Trace.Add("borrowed-boundary");
        Require(attack.Protocol.ReturnAttack(NativeAttackReturnPhase.AfterBorrowedSword, receipt), "The issued attack return must stop parent resumption.");
        Require(attack.Trace.SequenceEqual(new[] { "attack-before", "cleanup", "attack-after", "borrowed-boundary", "attack-tail", "attack-exclusive" }),
            "Attack phases must preserve cleanup and borrowed-card boundaries, and stop at the first matching return.");
        var resumed = new ReturnFixture(catalog);
        Require(!resumed.Protocol.ReturnAttack(NativeAttackReturnPhase.AfterBorrowedSword, receipt), "An unclaimed attack must reach the generic parent fallback.");
        resumed.Protocol.ReturnAttack(NativeAttackReturnPhase.BeforeParentResume, receipt);
        Require(resumed.Trace.SequenceEqual(new[] { "attack-tail", "attack-exclusive", "attack-last", "parent-check" }),
            "All declined attack callbacks must leave the final parent validation after the last native return.");

        first.Trace.Clear();
        first.Failure = new InvalidOperationException("fixture validation failure");
        try { first.Protocol.ValidateUse(ref use); throw new InvalidOperationException("Expected the native validation failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, first.Failure)) { }
        Require(first.Trace.SequenceEqual(new[] { "refresh", "validate:8" }), "A validation exception must propagate unchanged before preparation or pop.");

        _ = new ProgramNativeReturnCatalog(typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
    }

    public static void InvalidNativeReturnMetadataFailsBeforeDispatch()
    {
        var complete = Methods(typeof(ReturnFixture));
        foreach (var fixture in new[] { typeof(DuplicateFixture), typeof(InvalidSignatureFixture), typeof(InvalidStaticFixture),
                     typeof(InvalidOrderFixture), typeof(InvalidPhaseFixture), typeof(MissingContinuationFixture), typeof(WrongContinuationFixture) })
            Reject(() => new ProgramNativeReturnCatalog(complete.Concat(Methods(fixture))));
        Reject(() => new ProgramNativeReturnCatalog(complete.Where(method => method.Name != "BeforeParent")));
        var catalog = new ProgramNativeReturnCatalog(complete);
        Require(catalog.Validations is IList<ProgramNativeReturnCatalog.Validation> { IsReadOnly: true } &&
                catalog.Preparations is IList<ProgramNativeReturnCatalog.Preparation> { IsReadOnly: true } &&
                catalog.UseReturns is IList<ProgramNativeReturnCatalog.UseReturn> { IsReadOnly: true } &&
                catalog.AttackReturns is IList<ProgramNativeReturnCatalog.AttackReturn> { IsReadOnly: true },
            "Native return metadata must freeze every exposed registration collection.");
    }

    private static CardUseFrame Use(long id) => new(id, 0, 7, CardKind.Slash, [1], ResolutionFrameStep.Completed);
    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid native return registration or repeated completion was accepted.");
    }

    private sealed class ReturnFixture
    {
        internal List<string> Trace { get; } = [];
        internal ProgramNativeReturns Protocol { get; }
        internal bool FirstPrepared { get; init; }
        internal bool SecondPrepared { get; init; }
        internal bool ExclusiveUse { get; init; }
        internal bool ExclusiveAttack { get; init; }
        internal Exception? Failure { get; set; }
        internal ReturnFixture(ProgramNativeReturnCatalog catalog) => Protocol = new(catalog, (method, type) => method.CreateDelegate(type, this));

        [NativeUseValidation(10)] private void Refresh(ref CardUseFrame use) { Trace.Add("refresh"); use = use with { CardId = 8 }; }
        [NativeUseValidation(20)] private void Validate(CardUseFrame use) { Trace.Add($"validate:{use.CardId}"); if (Failure is { } failure) throw failure; }
        [NativeUsePreparation(10, 30, nameof(FirstReturn))] private bool PrepareFirst(CardUseFrame use) { Trace.Add("prepare-first"); return FirstPrepared; }
        [NativeUsePreparation(20, 40, nameof(SecondReturn))] private bool PrepareSecond(CardUseFrame use) { Trace.Add("prepare-second"); return SecondPrepared; }
        [NativeUsePreparation(30)] private bool PrepareOnly(CardUseFrame use) { Trace.Add("prepare-only"); return true; }
        private void FirstReturn(CardUseFrame use) => Trace.Add("return-first");
        private void SecondReturn(CardUseFrame use) => Trace.Add("return-second");
        [NativeUseReturn(10)] private void Tail(NativeUseReturnContext context) => Trace.Add($"tail:{context.HpLossMaterialReturn}");
        [NativeUseReturn(20)] private bool Exclusive(CardUseFrame use) { Trace.Add("exclusive"); return ExclusiveUse; }
        [NativeUseReturn(50)] private void TailFrame(CardUseFrame use) => Trace.Add("tail-frame");
        [NativeUseReturn(60)] private void TailId(long id) => Trace.Add($"tail-id:{id}");
        [NativeUseReturn(70)] private void TailEmpty() => Trace.Add("tail-empty");
        [NativeAttackReturn(NativeAttackReturnPhase.BeforeCleanup, 10)] private bool BeforeAttack(AttackReceipt receipt) { Trace.Add("attack-before"); return false; }
        [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 10)] private bool AfterAttack() { Trace.Add("attack-after"); return false; }
        [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 10)] private void AttackTail(AttackReceipt receipt) => Trace.Add("attack-tail");
        [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 20)] private bool AttackExclusive(AttackReceipt receipt) { Trace.Add("attack-exclusive"); return ExclusiveAttack; }
        [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 30)] private void AttackLast() => Trace.Add("attack-last");
        [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 10)] private void BeforeParent(AttackReceipt receipt) => Trace.Add("parent-check");
    }

    private sealed class DuplicateFixture { [NativeUseReturn(10)] private void Return(CardUseFrame use) { } }
    private sealed class InvalidSignatureFixture { [NativeUseReturn(100)] private int Return(CardUseFrame use) => 0; }
    private sealed class InvalidStaticFixture { [NativeUseReturn(100)] private static void Return(CardUseFrame use) { } }
    private sealed class InvalidOrderFixture { [NativeUseReturn(0)] private void Return(CardUseFrame use) { } }
    private sealed class InvalidPhaseFixture { [NativeAttackReturn((NativeAttackReturnPhase)99, 10)] private void Return(AttackReceipt receipt) { } }
    private sealed class MissingContinuationFixture { [NativeUsePreparation(100, 100, "absent")] private bool Prepare(CardUseFrame use) => true; }
    private sealed class WrongContinuationFixture
    {
        [NativeUsePreparation(100, 100, nameof(Return))] private bool Prepare(CardUseFrame use) => true;
        private bool Return(CardUseFrame use) => true;
    }
}
