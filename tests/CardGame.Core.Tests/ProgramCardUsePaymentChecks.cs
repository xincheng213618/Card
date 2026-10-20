using System.Reflection;
using CardGame.Core;

internal static class ProgramCardUsePaymentChecks
{
    public static void OrderedPaymentProofsAndMatchIsolation()
    {
        var source = Methods(typeof(Fixture)).Reverse().ToList();
        var catalog = ProgramCardUsePaymentCatalog.Discover(source);
        source.Clear();
        Need(catalog.Stages is IDictionary<ProgramCardUsePaymentStage, IReadOnlyList<ProgramCardUsePaymentRegistration>> { IsReadOnly: true } &&
            catalog.Stages.Values.All(rows => rows is IList<ProgramCardUsePaymentRegistration> { IsReadOnly: true }),
            "Shared payment metadata freezes both stages and their ordered registrations.");
        var a = new Fixture(catalog);
        var b = new Fixture(catalog);
        Need(a.Bound.Count == 0 && b.Bound.Count == 0, "Unused payment domains must not bind match delegates.");
        foreach (var mask in Enumerable.Range(0, 8))
        {
            a.Mask = mask;
            var expected = mask != 0;
            var visits = (mask & 1) != 0 ? 1 : (mask & 2) != 0 ? 2 : 3;
            Check(() => a.Payments.TryPayMaterials(a.Cards, 2, CardLocation.Hand(2), CardMoveReasons.Use));
            Check(() => a.Payments.MatchesBatch(a.Use, a.Batch));
            foreach (var stage in new[] { ProgramCardUsePaymentStage.LeadingTail, ProgramCardUsePaymentStage.TrailingTail,
                         ProgramCardUsePaymentStage.HpObserverTail, ProgramCardUsePaymentStage.SharedProcessingTail })
                Check(() => a.Payments.AllowsTail(stage, a.Use, a.Continuation));
            Check(() => a.Payments.AllowsProcessing(a.Cards));
            foreach (var stage in new[] { ProgramCardUsePaymentStage.BeforeSlashDebit,
                         ProgramCardUsePaymentStage.AfterSlashDebitIdentity, ProgramCardUsePaymentStage.SlashCountExemption })
                Check(() => a.Payments.ExemptsSlashDebit(stage, a.Use.Id, 2));

            void Check(Func<bool> query)
            {
                a.Trace.Clear();
                Need(query() == expected && a.Trace.SequenceEqual(Enumerable.Range(1, visits)) && b.Trace.Count == 0,
                    "Proof channels preserve argument identity, explicit order, short circuit and match isolation.");
            }
        }
        Need(a.Bound.Distinct().Count() == a.Bound.Count,
            "One proof callback shared by multiple stages binds once per match.");
        b.Mask = 1;
        Need(b.Payments.MatchesBatch(b.Use, b.Batch) && b.Trace.SequenceEqual([1]),
            "A second match binds its own state while sharing immutable registration metadata.");

        a.Trace.Clear(); a.Payments.CaptureMovement(a.Batch);
        Need(a.Trace.SequenceEqual([1, 2, 3]), "Payment capture runs every observer once, in original order.");
        a.Trace.Clear(); a.Payments.CompleteSlashPayment(a.Use.Id);
        Need(a.Trace.SequenceEqual([1, 2, 3]), "Native payment completion runs every applicable observer once.");
        a.Trace.Clear(); a.Payments.PayAcceptedConversionCosts(a.Action);
        Need(a.Trace.SequenceEqual([1, 2, 3]), "Accepted conversion costs retain sequential side effects.");
        a.Trace.Clear(); a.Failure = new InvalidOperationException("original payment failure");
        try { a.Payments.CaptureMovement(a.Batch); throw new Exception("Expected original failure."); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, a.Failure))
        { Need(a.Trace.SequenceEqual([1]), "An original payment failure stops subsequent callbacks without reflection wrapping."); }

        var empty = new ProgramCardUsePayments(ProgramCardUsePaymentCatalog.Discover([]),
            _ => throw new InvalidOperationException("Empty stages must not bind a handler."));
        Need(!empty.TryPayMaterials(a.Cards, 2, null, CardMoveReasons.Use) && !empty.MatchesBatch(a.Use, a.Batch) &&
            !empty.AllowsTail(ProgramCardUsePaymentStage.LeadingTail, a.Use, a.Continuation) &&
            !empty.AllowsProcessing(a.Cards) && !empty.ExemptsSlashDebit(ProgramCardUsePaymentStage.BeforeSlashDebit, a.Use.Id, 2),
            "Absence of a payment proof denies the special path and leaves fallback handling to the native caller.");
        empty.CaptureMovement(a.Batch); empty.CompleteSlashPayment(a.Use.Id); empty.PayAcceptedConversionCosts(a.Action);

        Reject(typeof(DuplicateOrder), "Duplicate card-use payment order");
        Reject(typeof(DuplicateHandler), "Duplicate card-use payment handler");
        Reject(typeof(InvalidStage), "valid stage");
        Reject(typeof(InvalidOrder), "positive order");
        Reject(typeof(WrongSignature), "invalid handler signature");
        Reject(typeof(OptionalArgument), "invalid handler signature");
        Reject(typeof(GenericHandler), "invalid handler signature");
        Reject(typeof(RefArgument), "invalid handler signature");

        static void Reject(Type type, string expected)
        {
            try { _ = ProgramCardUsePaymentCatalog.Discover(Methods(typeof(Fixture)).Concat(Methods(type))); }
            catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException($"Did not reject {type.Name}: {expected}");
        }
    }

    private static MethodInfo[] Methods(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture
    {
        internal IReadOnlyList<Card> Cards { get; } = [new(9, CardKind.Slash, Suit.Spade, 1)];
        internal CardUseFrame Use { get; } = new(31, 2, 9, CardKind.Slash, [3]);
        internal CardMovementBatchContext Batch { get; } = new(41, 31, null, 1, [], []);
        internal RecoveryPaidCardUseContinuation Continuation { get; } = new(RecoveryPaidCardUseKind.CommittedSlash, 2);
        internal CardActionContext Action { get; } = new(51, null, CardActionType.Use, 2, 2, null, null, 3, CardKind.Slash, [3], [], []);
        internal List<int> Trace { get; } = [];
        internal List<string> Bound { get; } = [];
        internal int Mask { get; set; }
        internal Exception? Failure { get; set; }
        internal ProgramCardUsePayments Payments { get; }
        internal Fixture(ProgramCardUsePaymentCatalog catalog) => Payments = new(catalog, row =>
        {
            Bound.Add(row.Method.Name);
            return row.Method.CreateDelegate(row.DelegateType, this);
        });
        private bool Query(int index)
        { Trace.Add(index); if (Failure is { } error) throw error; return (Mask & (1 << (index - 1))) != 0; }
        private void Observe(int index) { _ = Query(index); }
        private void NeedBatch(CardUseFrame use, CardMovementBatchContext batch) =>
            Need(ReferenceEquals(use, Use) && ReferenceEquals(batch, Batch), "The issued use and movement batch must remain exact.");
        private void NeedTail(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) =>
            Need(ReferenceEquals(use, Use) && ReferenceEquals(continuation, Continuation), "A proof receives its exact owner and continuation.");
        private void NeedDebit(long useId, int actor) => Need(useId == Use.Id && actor == 2, "A debit proof retains its issued use and actor.");

        [ProgramCardUsePayment(ProgramCardUsePaymentStage.MaterialPayment, 10)]
        private bool Pay1(IReadOnlyList<Card> cards, int provider, CardLocation? source, CardMoveReason reason)
        { Need(ReferenceEquals(cards, Cards) && provider == 2 && source == CardLocation.Hand(2) && reason == CardMoveReasons.Use, "Physical payment inputs changed."); return Query(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.MaterialPayment, 20)]
        private bool Pay2(IReadOnlyList<Card> cards, int provider, CardLocation? source, CardMoveReason reason) => Query(2);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.MaterialPayment, 30)]
        private bool Pay3(IReadOnlyList<Card> cards, int provider, CardLocation? source, CardMoveReason reason) => Query(3);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 10)]
        private bool Batch1(CardUseFrame use, CardMovementBatchContext batch) { NeedBatch(use, batch); return Query(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 20)]
        private bool Batch2(CardUseFrame use, CardMovementBatchContext batch) { NeedBatch(use, batch); return Query(2); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.MatchBatch, 30)]
        private bool Batch3(CardUseFrame use, CardMovementBatchContext batch) { NeedBatch(use, batch); return Query(3); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.LeadingTail, 10)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 10)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 10)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 10)]
        private bool Tail1(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) { NeedTail(use, continuation); return Query(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.LeadingTail, 20)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 20)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 20)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 20)]
        private bool Tail2(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) { NeedTail(use, continuation); return Query(2); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.LeadingTail, 30)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.TrailingTail, 30)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.HpObserverTail, 30)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SharedProcessingTail, 30)]
        private bool Tail3(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) { NeedTail(use, continuation); return Query(3); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.ProcessingAllowance, 10)]
        private bool Processing1(IReadOnlyList<Card> cards) { Need(ReferenceEquals(cards, Cards), "Processing materials changed."); return Query(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.ProcessingAllowance, 20)]
        private bool Processing2(IReadOnlyList<Card> cards) => Query(2);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.ProcessingAllowance, 30)]
        private bool Processing3(IReadOnlyList<Card> cards) => Query(3);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 10)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity, 10)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 10)]
        private bool Debit1(long useId, int actor) { NeedDebit(useId, actor); return Query(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 20)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity, 20)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 20)]
        private bool Debit2(long useId, int actor) { NeedDebit(useId, actor); return Query(2); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 30)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity, 30)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 30)]
        private bool Debit3(long useId, int actor) { NeedDebit(useId, actor); return Query(3); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 10)]
        private void Capture1(CardMovementBatchContext batch) { Need(ReferenceEquals(batch, Batch), "Capture lost its exact batch."); Observe(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 20)]
        private void Capture2(CardMovementBatchContext batch) => Observe(2);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 30)]
        private void Capture3(CardMovementBatchContext batch) => Observe(3);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 10)]
        private void Complete1(long useId) { Need(useId == Use.Id, "Completion lost its paid owner."); Observe(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 20)]
        private void Complete2(long useId) => Observe(2);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 30)]
        private void Complete3(long useId) => Observe(3);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.AcceptedConversionCost, 10)]
        private void Conversion1(CardActionContext action) { Need(ReferenceEquals(action, Action), "Accepted payment lost its frozen action."); Observe(1); }
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.AcceptedConversionCost, 20)]
        private void Conversion2(CardActionContext action) => Observe(2);
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.AcceptedConversionCost, 30)]
        private void Conversion3(CardActionContext action) => Observe(3);
    }

    private sealed class DuplicateOrder
    { [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 10)] private void Bad(CardMovementBatchContext batch) { } }
    private sealed class DuplicateHandler
    {
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 90)]
        [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 100)]
        private void Bad(CardMovementBatchContext batch) { }
    }
    private sealed class InvalidStage
    { [ProgramCardUsePayment((ProgramCardUsePaymentStage)999, 10)] private void Bad(CardMovementBatchContext batch) { } }
    private sealed class InvalidOrder
    { [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 0)] private void Bad(CardMovementBatchContext batch) { } }
    private sealed class WrongSignature
    { [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 90)] private bool Bad(CardMovementBatchContext batch) => false; }
    private sealed class OptionalArgument
    { [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 90)] private void Bad(long useId = 0) { } }
    private sealed class GenericHandler
    { [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 90)] private void Bad<T>(CardMovementBatchContext batch) { } }
    private sealed class RefArgument
    { [ProgramCardUsePayment(ProgramCardUsePaymentStage.CaptureMovement, 90)] private void Bad(ref CardMovementBatchContext batch) { } }
}
