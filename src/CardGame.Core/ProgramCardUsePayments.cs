using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Stages retain distinct proof domains. A prospective conversion's affordability
// never substitutes for an issued movement receipt or an accepted native use.
internal enum ProgramCardUsePaymentStage
{
    MaterialPayment,
    CaptureMovement,
    MatchBatch,
    LeadingTail,
    TrailingTail,
    HpObserverTail,
    SharedProcessingTail,
    ProcessingAllowance,
    SlashPaymentCompleted,
    BeforeSlashDebit,
    AfterSlashDebitIdentity,
    SlashCountExemption,
    AcceptedConversionCost
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramCardUsePaymentAttribute(ProgramCardUsePaymentStage stage, int order) : Attribute
{
    internal ProgramCardUsePaymentStage Stage { get; } = stage;
    internal int Order { get; } = order;
}

internal sealed record ProgramCardUsePaymentRegistration(int Order, MethodInfo Method, Type DelegateType);

internal sealed class ProgramCardUsePaymentCatalog
{
    internal IReadOnlyDictionary<ProgramCardUsePaymentStage, IReadOnlyList<ProgramCardUsePaymentRegistration>> Stages { get; }

    private ProgramCardUsePaymentCatalog(Dictionary<ProgramCardUsePaymentStage,
        SortedDictionary<int, ProgramCardUsePaymentRegistration>> stages) =>
        Stages = new ReadOnlyDictionary<ProgramCardUsePaymentStage, IReadOnlyList<ProgramCardUsePaymentRegistration>>(
            stages.ToDictionary(pair => pair.Key, pair =>
                (IReadOnlyList<ProgramCardUsePaymentRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));

    internal static ProgramCardUsePaymentCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var stages = Enum.GetValues<ProgramCardUsePaymentStage>().ToDictionary(stage => stage,
            _ => new SortedDictionary<int, ProgramCardUsePaymentRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramCardUsePaymentAttribute>())
        {
            if (!Enum.IsDefined(attribute.Stage) || attribute.Order <= 0)
                throw new InvalidOperationException($"Card-use payment '{method.Name}' requires a valid stage and positive order.");
            var delegateType = DelegateType(attribute.Stage);
            var signature = delegateType.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters ||
                signature.ReturnType != method.ReturnType ||
                !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(signature.GetParameters().Select(parameter => parameter.ParameterType)) ||
                method.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut || parameter.ParameterType.IsByRef))
                throw new InvalidOperationException($"Card-use payment '{method.Name}' has an invalid handler signature.");
            var stage = stages[attribute.Stage];
            if (stage.Values.Any(row => row.Method == method))
                throw new InvalidOperationException($"Duplicate card-use payment handler '{attribute.Stage}:{method.Name}'.");
            if (!stage.TryAdd(attribute.Order, new(attribute.Order, method, delegateType)))
                throw new InvalidOperationException($"Duplicate card-use payment order '{attribute.Stage}:{attribute.Order}'.");
        }
        return new(stages);
    }

    internal static Type DelegateType(ProgramCardUsePaymentStage stage) => stage switch
    {
        ProgramCardUsePaymentStage.MaterialPayment => typeof(Func<IReadOnlyList<Card>, int, CardLocation?, CardMoveReason, bool>),
        ProgramCardUsePaymentStage.CaptureMovement => typeof(Action<CardMovementBatchContext>),
        ProgramCardUsePaymentStage.MatchBatch => typeof(Func<CardUseFrame, CardMovementBatchContext, bool>),
        ProgramCardUsePaymentStage.LeadingTail or ProgramCardUsePaymentStage.TrailingTail or
            ProgramCardUsePaymentStage.HpObserverTail or ProgramCardUsePaymentStage.SharedProcessingTail =>
            typeof(Func<CardUseFrame, RecoveryPaidCardUseContinuation, bool>),
        ProgramCardUsePaymentStage.ProcessingAllowance => typeof(Func<IReadOnlyList<Card>, bool>),
        ProgramCardUsePaymentStage.SlashPaymentCompleted => typeof(Action<long>),
        ProgramCardUsePaymentStage.BeforeSlashDebit or ProgramCardUsePaymentStage.AfterSlashDebitIdentity or
            ProgramCardUsePaymentStage.SlashCountExemption => typeof(Func<long, int, bool>),
        ProgramCardUsePaymentStage.AcceptedConversionCost => typeof(Action<CardActionContext>),
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}

// Catalogs are immutable and shared; bound handlers and their caches belong to
// one engine. Proof queries short-circuit, while payment observers all run.
internal sealed class ProgramCardUsePayments
{
    private readonly ProgramCardUsePaymentCatalog _catalog;
    private readonly Func<ProgramCardUsePaymentRegistration, Delegate> _bind;
    private readonly Dictionary<ProgramCardUsePaymentStage, Array> _handlers = [];

    internal ProgramCardUsePayments(ProgramCardUsePaymentCatalog catalog,
        Func<ProgramCardUsePaymentRegistration, Delegate> bind)
    {
        _catalog = catalog;
        var bound = new Dictionary<(MethodInfo Method, Type DelegateType), Delegate>();
        _bind = row =>
        {
            var key = (row.Method, row.DelegateType);
            if (!bound.TryGetValue(key, out var callback)) bound.Add(key, callback = bind(row));
            return callback;
        };
    }

    private T[] Handlers<T>(ProgramCardUsePaymentStage stage) where T : Delegate
    {
        if (ProgramCardUsePaymentCatalog.DelegateType(stage) != typeof(T))
            throw new ArgumentOutOfRangeException(nameof(stage));
        if (!_handlers.TryGetValue(stage, out var handlers))
            _handlers.Add(stage, handlers = _catalog.Stages[stage].Select(row => (T)_bind(row)).ToArray());
        return (T[])handlers;
    }

    internal bool TryPayMaterials(IReadOnlyList<Card> cards, int providerSeat, CardLocation? source, CardMoveReason reason)
    {
        foreach (var pay in Handlers<Func<IReadOnlyList<Card>, int, CardLocation?, CardMoveReason, bool>>(ProgramCardUsePaymentStage.MaterialPayment))
            if (pay(cards, providerSeat, source, reason)) return true;
        return false;
    }

    internal void CaptureMovement(CardMovementBatchContext batch)
    {
        foreach (var capture in Handlers<Action<CardMovementBatchContext>>(ProgramCardUsePaymentStage.CaptureMovement)) capture(batch);
    }

    internal bool MatchesBatch(CardUseFrame use, CardMovementBatchContext batch)
    {
        foreach (var matches in Handlers<Func<CardUseFrame, CardMovementBatchContext, bool>>(ProgramCardUsePaymentStage.MatchBatch))
            if (matches(use, batch)) return true;
        return false;
    }

    internal bool AllowsTail(ProgramCardUsePaymentStage stage, CardUseFrame use, RecoveryPaidCardUseContinuation continuation)
    {
        foreach (var allows in Handlers<Func<CardUseFrame, RecoveryPaidCardUseContinuation, bool>>(stage))
            if (allows(use, continuation)) return true;
        return false;
    }

    internal bool AllowsProcessing(IReadOnlyList<Card> cards)
    {
        foreach (var allows in Handlers<Func<IReadOnlyList<Card>, bool>>(ProgramCardUsePaymentStage.ProcessingAllowance))
            if (allows(cards)) return true;
        return false;
    }

    internal void CompleteSlashPayment(long useId)
    {
        foreach (var complete in Handlers<Action<long>>(ProgramCardUsePaymentStage.SlashPaymentCompleted)) complete(useId);
    }

    internal bool ExemptsSlashDebit(ProgramCardUsePaymentStage stage, long useId, int actorSeat)
    {
        foreach (var exempts in Handlers<Func<long, int, bool>>(stage)) if (exempts(useId, actorSeat)) return true;
        return false;
    }

    internal void PayAcceptedConversionCosts(CardActionContext action)
    {
        foreach (var pay in Handlers<Action<CardActionContext>>(ProgramCardUsePaymentStage.AcceptedConversionCost)) pay(action);
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramCardUsePaymentCatalog> CardUsePaymentMetadata = new(() =>
        ProgramCardUsePaymentCatalog.Discover(RuntimeModuleMethods), true);
    private ProgramCardUsePayments? _cardUsePayments;
    private ProgramCardUsePayments CardUsePayments => _cardUsePayments ??= new(CardUsePaymentMetadata.Value,
        row => BindRuntimeHandler(row.Method, row.DelegateType));

    // The built-in committed-Slash test stays between the two registered groups
    // to retain its original short-circuit position at movement-return sites.
    private bool HasPaidCardUseMovementTail(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) =>
        CardUsePayments.AllowsTail(ProgramCardUsePaymentStage.LeadingTail, use, continuation) ||
        continuation.Kind == RecoveryPaidCardUseKind.CommittedSlash ||
        CardUsePayments.AllowsTail(ProgramCardUsePaymentStage.TrailingTail, use, continuation);

    private bool ObservesPaidCardUseChildren(CardUseFrame use, RecoveryPaidCardUseContinuation continuation) =>
        continuation.Kind == RecoveryPaidCardUseKind.CommittedSlash && OwnsGenericSlashPaymentTail(use) ||
        CardUsePayments.AllowsTail(ProgramCardUsePaymentStage.LeadingTail, use, continuation) ||
        CardUsePayments.AllowsTail(ProgramCardUsePaymentStage.TrailingTail, use, continuation);
}
