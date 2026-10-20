using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// These are positions in the native legality query, not interchangeable
// priorities. Queries remain live and payment/effect execution stays elsewhere.
internal enum ProgramCardLegalityStage
{
    BeforeIntrinsicUseBan,
    BeforeTurnUseBan,
    LegalActionProjection,
    BeforeLegalActionKind,
    AfterLegalActionKind,
    MaterialUseBan,
    ViewAsBan,
    NamedConversionBan,
    ConfiguredConversionBan,
    ConversionUsageBan,
    DyingConversionBan,
    ConversionSourceBan,
    SubmittedUseValidation,
    HandPhysicalBan,
    EntityPhysicalBan,
    NativeResponseBan
}

internal readonly record struct ProgramCardUseQuery(
    int ActorSeat, CardKind Kind, CardActionType ActionType, long? ExistingUseFrameId);
internal readonly record struct ProgramLegalActionQuery(
    CharacterState Actor, LegalAction Action, CardKind? EffectiveKind, Card? NativeOriginalCard);
internal readonly record struct ProgramCardMaterialQuery(
    int ActorSeat, CardKind Kind, IEnumerable<int> CardIds, bool IsResponse);
internal readonly record struct ProgramViewAsLegalityQuery(
    CharacterState Owner, CardKind Kind, IReadOnlyList<int> CardIds,
    CardConversionSource Source, bool ForResponse, bool DyingUse, int? RequestedUseTarget);
internal readonly record struct ProgramConfiguredViewAsQuery(
    CharacterState Owner, Card Card, IndexedSkillProgramInstance Instance, SkillProgramViewAs Rule,
    bool ForResponse, bool DyingUse, int? RequestedUseTarget);
internal readonly record struct ProgramViewAsSourceQuery(
    CharacterState Owner, Card Card, CardConversionSource Source, bool ForResponse, bool DyingUse);
internal readonly record struct ProgramSubmittedUseQuery(
    int ActorSeat, CardKind Kind, IReadOnlyList<int> CardIds, IReadOnlyList<CardConversionSource> ConversionChain);
internal readonly record struct ProgramNativeResponseQuery(
    CharacterState Provider, CardKind Kind, IReadOnlyList<int> CardIds,
    CardConversionSource? Source = null, bool DyingUse = false);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramCardLegalityAttribute(ProgramCardLegalityStage stage, int order) : Attribute
{
    internal ProgramCardLegalityStage Stage { get; } = stage;
    internal int Order { get; } = order;
}

internal sealed record ProgramCardLegalityRegistration(int Order, MethodInfo Method, Type DelegateType);

internal sealed class ProgramCardLegalityCatalog
{
    internal IReadOnlyDictionary<ProgramCardLegalityStage, IReadOnlyList<ProgramCardLegalityRegistration>> Registrations { get; }

    private ProgramCardLegalityCatalog(Dictionary<ProgramCardLegalityStage, SortedDictionary<int, ProgramCardLegalityRegistration>> entries) =>
        Registrations = new ReadOnlyDictionary<ProgramCardLegalityStage, IReadOnlyList<ProgramCardLegalityRegistration>>(
            entries.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ProgramCardLegalityRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));

    internal static ProgramCardLegalityCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var entries = Enum.GetValues<ProgramCardLegalityStage>().ToDictionary(stage => stage,
            _ => new SortedDictionary<int, ProgramCardLegalityRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramCardLegalityAttribute>())
        {
            if (!Enum.IsDefined(attribute.Stage) || attribute.Order <= 0)
                throw new InvalidOperationException($"Card legality '{method.Name}' requires a valid stage and positive order.");
            var type = DelegateType(attribute.Stage);
            var signature = type.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters ||
                method.ReturnType != signature.ReturnType ||
                !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(signature.GetParameters().Select(parameter => parameter.ParameterType)) ||
                method.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut || parameter.ParameterType.IsByRef))
                throw new InvalidOperationException($"Card legality '{method.Name}' has an invalid handler signature.");
            if (!entries[attribute.Stage].TryAdd(attribute.Order, new(attribute.Order, method, type)))
                throw new InvalidOperationException($"Duplicate card legality order '{attribute.Stage}:{attribute.Order}'.");
        }
        return new(entries);
    }

    private static Type DelegateType(ProgramCardLegalityStage stage) => stage switch
    {
        ProgramCardLegalityStage.BeforeIntrinsicUseBan or ProgramCardLegalityStage.BeforeTurnUseBan => typeof(Func<ProgramCardUseQuery, bool>),
        ProgramCardLegalityStage.LegalActionProjection => typeof(Func<ProgramLegalActionQuery, LegalAction?>),
        ProgramCardLegalityStage.BeforeLegalActionKind or ProgramCardLegalityStage.AfterLegalActionKind => typeof(Func<ProgramLegalActionQuery, bool>),
        ProgramCardLegalityStage.MaterialUseBan => typeof(Func<ProgramCardMaterialQuery, bool>),
        ProgramCardLegalityStage.ViewAsBan => typeof(Func<ProgramViewAsLegalityQuery, bool>),
        ProgramCardLegalityStage.NamedConversionBan or ProgramCardLegalityStage.ConfiguredConversionBan or
            ProgramCardLegalityStage.ConversionUsageBan => typeof(Func<ProgramConfiguredViewAsQuery, bool>),
        ProgramCardLegalityStage.DyingConversionBan or ProgramCardLegalityStage.ConversionSourceBan =>
            typeof(Func<ProgramViewAsSourceQuery, bool>),
        ProgramCardLegalityStage.SubmittedUseValidation => typeof(Action<ProgramSubmittedUseQuery>),
        ProgramCardLegalityStage.HandPhysicalBan or ProgramCardLegalityStage.EntityPhysicalBan => typeof(Func<int, int, bool>),
        ProgramCardLegalityStage.NativeResponseBan => typeof(Func<ProgramNativeResponseQuery, bool>),
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}

internal sealed class ProgramCardLegality
{
    private readonly Dictionary<ProgramCardLegalityStage, Delegate[]> _callbacks;

    internal ProgramCardLegality(ProgramCardLegalityCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        var bound = new Dictionary<MethodInfo, Delegate>();
        _callbacks = catalog.Registrations.ToDictionary(pair => pair.Key, pair => pair.Value.Select(registration =>
        {
            if (!bound.TryGetValue(registration.Method, out var callback))
                bound.Add(registration.Method, callback = bind(registration.Method, registration.DelegateType));
            return callback;
        }).ToArray());
    }

    internal bool IsUseForbidden(ProgramCardLegalityStage stage, ProgramCardUseQuery query)
    {
        if (stage is not (ProgramCardLegalityStage.BeforeIntrinsicUseBan or ProgramCardLegalityStage.BeforeTurnUseBan))
            throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage]) if (((Func<ProgramCardUseQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal LegalAction? ProjectAction(ProgramLegalActionQuery query)
    {
        var action = query.Action;
        foreach (var callback in _callbacks[ProgramCardLegalityStage.LegalActionProjection])
        {
            var projected = ((Func<ProgramLegalActionQuery, LegalAction?>)callback)(query with { Action = action });
            if (projected is null) return null;
            action = projected;
        }
        return action;
    }

    internal bool IsActionForbidden(ProgramCardLegalityStage stage, ProgramLegalActionQuery query)
    {
        if (stage is not (ProgramCardLegalityStage.BeforeLegalActionKind or ProgramCardLegalityStage.AfterLegalActionKind))
            throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage]) if (((Func<ProgramLegalActionQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool IsMaterialForbidden(ProgramCardMaterialQuery query)
    {
        foreach (var callback in _callbacks[ProgramCardLegalityStage.MaterialUseBan])
            if (((Func<ProgramCardMaterialQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool IsViewAsForbidden(ProgramViewAsLegalityQuery query)
    {
        foreach (var callback in _callbacks[ProgramCardLegalityStage.ViewAsBan])
            if (((Func<ProgramViewAsLegalityQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool IsConfiguredConversionForbidden(ProgramCardLegalityStage stage, ProgramConfiguredViewAsQuery query)
    {
        if (stage is not (ProgramCardLegalityStage.NamedConversionBan or ProgramCardLegalityStage.ConfiguredConversionBan or
            ProgramCardLegalityStage.ConversionUsageBan)) throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage])
            if (((Func<ProgramConfiguredViewAsQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool IsConversionSourceForbidden(ProgramCardLegalityStage stage, ProgramViewAsSourceQuery query)
    {
        if (stage is not (ProgramCardLegalityStage.DyingConversionBan or ProgramCardLegalityStage.ConversionSourceBan))
            throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage])
            if (((Func<ProgramViewAsSourceQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal void ValidateSubmittedUse(ProgramSubmittedUseQuery query)
    {
        foreach (var callback in _callbacks[ProgramCardLegalityStage.SubmittedUseValidation])
            ((Action<ProgramSubmittedUseQuery>)callback)(query);
    }

    internal bool IsPhysicalForbidden(ProgramCardLegalityStage stage, int actorSeat, int cardId)
    {
        if (stage is not (ProgramCardLegalityStage.HandPhysicalBan or ProgramCardLegalityStage.EntityPhysicalBan))
            throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage])
            if (((Func<int, int, bool>)callback)(actorSeat, cardId)) return true;
        return false;
    }

    internal bool IsNativeResponseForbidden(ProgramNativeResponseQuery query)
    {
        foreach (var callback in _callbacks[ProgramCardLegalityStage.NativeResponseBan])
            if (((Func<ProgramNativeResponseQuery, bool>)callback)(query)) return true;
        return false;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramCardLegalityCatalog> CardLegalityMetadata = new(() =>
        ProgramCardLegalityCatalog.Discover(RuntimeModuleMethods), true);
    private ProgramCardLegality? _cardLegality;
    private ProgramCardLegality CardLegality => _cardLegality ??= new(CardLegalityMetadata.Value,
        BindRuntimeHandler);

    private bool IsProgramMaterialUseForbidden(int actorSeat, CardKind kind, IEnumerable<int> cardIds, bool response = false) =>
        CardLegality.IsMaterialForbidden(new(actorSeat, kind, cardIds, response));

    private bool IsProgramViewAsAllowed(CharacterState owner, CardKind kind, IReadOnlyList<int> cardIds,
        CardConversionSource source, bool forResponse, bool dyingUse = false, int? requestedUseTarget = null) =>
        !CardLegality.IsViewAsForbidden(new(owner, kind, cardIds, source, forResponse, dyingUse, requestedUseTarget));

    private bool IsProgramHandPhysicalRestricted(int actorSeat, int cardId) =>
        CardLegality.IsPhysicalForbidden(ProgramCardLegalityStage.HandPhysicalBan, actorSeat, cardId);

    private bool IsProgramEntityPhysicalRestricted(int actorSeat, int cardId) =>
        CardLegality.IsPhysicalForbidden(ProgramCardLegalityStage.EntityPhysicalBan, actorSeat, cardId);

    private bool IsProgramNativeResponseAllowed(CharacterState provider, CardKind kind, IReadOnlyList<int> cardIds,
        CardConversionSource? source = null, bool dyingUse = false) =>
        !CardLegality.IsNativeResponseForbidden(new(provider, kind, cardIds, source, dyingUse));
}
