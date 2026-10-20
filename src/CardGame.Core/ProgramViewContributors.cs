using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// These are positions in the existing projection, not new visibility policies.
// In particular, covered/held Processing cards stay excluded even in diagnostics,
// while concealed contest cards become visible only through revealAll.
internal enum ProgramViewContribution
{
    ProgramPublicCards,
    PublicCardsBeforeResolution,
    PublicCardsAfterResolution,
    PrivateCards,
    InvariantProcessing,
    PublicProcessing,
    PublicProcessingExclusion,
    ConcealedCardExclusion,
    NamedAmbushPiles,
    PrivateWhirlPiles
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramViewContributorAttribute(ProgramViewContribution contribution, int order) : Attribute
{
    internal ProgramViewContribution Contribution { get; } = contribution;
    internal int Order { get; } = order;
}

internal delegate IReadOnlyList<NamedAmbushPileSnapshot>? ProgramNamedAmbushViewTransform(
    IReadOnlyList<NamedAmbushPileSnapshot>? existing, int holderSeat, int viewerSeat, bool revealAll);
internal delegate IReadOnlyList<PrivateWhirlPileSnapshot>? ProgramPrivateWhirlViewTransform(
    IReadOnlyList<PrivateWhirlPileSnapshot>? existing, int ownerSeat, int viewerSeat, bool revealAll);
internal sealed record ProgramViewRegistration(MethodInfo Method, Type DelegateType);

// Shared metadata contains neither match state nor match-bound delegates.
internal sealed class ProgramViewContributionCatalog
{
    internal IReadOnlyDictionary<ProgramViewContribution, IReadOnlyList<ProgramViewRegistration>> Phases { get; }

    private ProgramViewContributionCatalog(
        Dictionary<ProgramViewContribution, SortedDictionary<int, ProgramViewRegistration>> phases) =>
        Phases = new ReadOnlyDictionary<ProgramViewContribution, IReadOnlyList<ProgramViewRegistration>>(
            phases.ToDictionary(p => p.Key,
                p => (IReadOnlyList<ProgramViewRegistration>)Array.AsReadOnly(p.Value.Values.ToArray())));

    internal static ProgramViewContributionCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var phases = Enum.GetValues<ProgramViewContribution>().ToDictionary(p => p,
            _ => new SortedDictionary<int, ProgramViewRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramViewContributorAttribute>())
        {
            if (!Enum.IsDefined(attribute.Contribution) || attribute.Order <= 0)
                throw new InvalidOperationException($"View contributor '{method.Name}' requires a valid contribution and positive order.");
            var delegateType = DelegateType(attribute.Contribution);
            var signature = delegateType.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters ||
                !signature.ReturnType.IsAssignableFrom(method.ReturnType) ||
                !method.GetParameters().Select(p => p.ParameterType)
                    .SequenceEqual(signature.GetParameters().Select(p => p.ParameterType)) ||
                method.GetParameters().Any(p => p.IsOptional || p.IsOut || p.ParameterType.IsByRef))
                throw new InvalidOperationException($"View contributor '{method.Name}' has an invalid handler signature.");
            var phase = phases[attribute.Contribution];
            if (phase.Values.Any(row => row.Method == method))
                throw new InvalidOperationException($"Duplicate view contributor '{attribute.Contribution}:{method.Name}'.");
            if (!phase.TryAdd(attribute.Order, new(method, delegateType)))
                throw new InvalidOperationException($"Duplicate view contributor order '{attribute.Contribution}:{attribute.Order}'.");
        }
        return new(phases);
    }

    internal static Type DelegateType(ProgramViewContribution contribution) => contribution switch
    {
        ProgramViewContribution.ProgramPublicCards or ProgramViewContribution.PublicCardsBeforeResolution or
            ProgramViewContribution.PublicCardsAfterResolution => typeof(Func<IEnumerable<CardSnapshot>>),
        ProgramViewContribution.PrivateCards => typeof(Func<int, IEnumerable<CardSnapshot>>),
        ProgramViewContribution.InvariantProcessing or ProgramViewContribution.PublicProcessing =>
            typeof(Func<IReadOnlyList<Card>, IReadOnlyList<Card>>),
        ProgramViewContribution.PublicProcessingExclusion or ProgramViewContribution.ConcealedCardExclusion => typeof(Func<int, bool>),
        ProgramViewContribution.NamedAmbushPiles => typeof(ProgramNamedAmbushViewTransform),
        ProgramViewContribution.PrivateWhirlPiles => typeof(ProgramPrivateWhirlViewTransform),
        _ => throw new ArgumentOutOfRangeException(nameof(contribution))
    };
}

// Bind each reached contribution once per engine. Sources are invoked eagerly
// in their old order; their returned sequences retain their original laziness.
// De-duplication and final collection freezing remain at the projection boundary.
internal sealed class ProgramViewContributors
{
    private readonly ProgramViewContributionCatalog _catalog;
    private readonly Func<ProgramViewRegistration, Delegate> _bind;
    private readonly Dictionary<ProgramViewContribution, Array> _handlers = new();

    internal ProgramViewContributors(ProgramViewContributionCatalog catalog, Func<ProgramViewRegistration, Delegate> bind)
    {
        _catalog = catalog;
        var bound = new Dictionary<ProgramViewRegistration, Delegate>();
        _bind = row =>
        {
            if (!bound.TryGetValue(row, out var callback)) bound.Add(row, callback = bind(row));
            return callback;
        };
    }

    private T[] Handlers<T>(ProgramViewContribution contribution) where T : Delegate
    {
        if (ProgramViewContributionCatalog.DelegateType(contribution) != typeof(T))
            throw new ArgumentOutOfRangeException(nameof(contribution));
        if (!_handlers.TryGetValue(contribution, out var handlers))
            _handlers.Add(contribution, handlers = _catalog.Phases[contribution].Select(row => (T)_bind(row)).ToArray());
        return (T[])handlers;
    }

    internal IEnumerable<CardSnapshot> Cards(ProgramViewContribution contribution, int viewerSeat = 0)
    {
        IEnumerable<CardSnapshot> result = [];
        if (contribution == ProgramViewContribution.PrivateCards)
            foreach (var source in Handlers<Func<int, IEnumerable<CardSnapshot>>>(contribution))
                result = result.Concat(source(viewerSeat));
        else
            foreach (var source in Handlers<Func<IEnumerable<CardSnapshot>>>(contribution))
                result = result.Concat(source());
        return result;
    }

    internal IReadOnlyList<Card> FilterProcessing(ProgramViewContribution contribution, IReadOnlyList<Card> cards)
    {
        foreach (var transform in Handlers<Func<IReadOnlyList<Card>, IReadOnlyList<Card>>>(contribution)) cards = transform(cards);
        return cards;
    }

    private bool IsExcluded(ProgramViewContribution contribution, int cardId)
    {
        foreach (var exclude in Handlers<Func<int, bool>>(contribution)) if (exclude(cardId)) return true;
        return false;
    }

    internal bool IsPublicCardVisible(int cardId, bool revealAll) =>
        revealAll || !IsExcluded(ProgramViewContribution.ConcealedCardExclusion, cardId);

    internal int ProcessingCount(IReadOnlyList<Card> cards, bool revealAll) =>
        FilterProcessing(ProgramViewContribution.PublicProcessing, cards).Count(card =>
            !IsExcluded(ProgramViewContribution.PublicProcessingExclusion, card.Id) && IsPublicCardVisible(card.Id, revealAll));

    internal IReadOnlyList<NamedAmbushPileSnapshot>? NamedAmbushPiles(
        IReadOnlyList<NamedAmbushPileSnapshot>? existing, int holderSeat, int viewerSeat, bool revealAll)
    {
        foreach (var transform in Handlers<ProgramNamedAmbushViewTransform>(ProgramViewContribution.NamedAmbushPiles))
            existing = transform(existing, holderSeat, viewerSeat, revealAll);
        return existing;
    }

    internal IReadOnlyList<PrivateWhirlPileSnapshot>? PrivateWhirlPiles(
        IReadOnlyList<PrivateWhirlPileSnapshot>? existing, int ownerSeat, int viewerSeat, bool revealAll)
    {
        foreach (var transform in Handlers<ProgramPrivateWhirlViewTransform>(ProgramViewContribution.PrivateWhirlPiles))
            existing = transform(existing, ownerSeat, viewerSeat, revealAll);
        return existing;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramViewContributionCatalog> ViewContributionMetadata = new(() =>
        ProgramViewContributionCatalog.Discover(RuntimeModuleMethods), true);
    private ProgramViewContributors? _viewContributors;
    private ProgramViewContributors ViewContributors => _viewContributors ??= new(ViewContributionMetadata.Value,
        row => BindRuntimeHandler(row.Method, row.DelegateType));
}
