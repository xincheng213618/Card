using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Fixed insertion points in shared engine pipelines. Each phase replaces a
// hand-maintained per-general call chain: modules register next to their own
// implementation and the engine dispatches in explicit order, so adding a
// general never edits the central pipeline again.
internal enum ProgramPipelinePhase
{
    IncludeTriggerCandidate,
    CollectIssuedCardTrigger,
    CaptureVirtualDuelLegacy,
    ReturnCompletedProgramBinding,
    PreparePlayPhaseDecision,
    ForcedPlayHandSelection,
    FilterPlayPhaseActions,
    ContinueDetachedProgramBinding,
    UnissuedTurnStartObligation,
    ObservesResponseUseCompletion,
    CardEntitySkillLabel,
    SelectDiscardBatchIndexes,
    HealthChangeSubject,
    CaptureHealthChange,
    IncludeHealthChangeCandidates,
    IncludeNativeDrawCandidates,
    SlashTargetDuelSource,
    IssuedTriggerCandidate,
    AdmitTurnSuppression,
    RumoEntry,
    RumoOrigin,
    VirtualEquipment,
    ActualPhaseSkipped,
    LifecycleWindowCompleted,
    AppendTurnEndingItems,
    ResumeScheduledPhase,
    CancelScheduledPhase
}

// Register next to the owning implementation with an explicit order; orders are
// unique per phase and reproduce the previous central call sequence.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramPipelineHookAttribute(ProgramPipelinePhase phase, int order) : Attribute
{
    internal ProgramPipelinePhase Phase { get; } = phase;
    internal int Order { get; } = order;
}

internal delegate IReadOnlyList<ProgramTriggerCandidate> ProgramTriggerCandidateIncluder(
    CharacterState owner, SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> candidates);
internal delegate void ProgramIssuedCardTriggerCollector(
    CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result);
internal delegate void ProgramVirtualDuelLegacyCapture(ProgramSkillFrame frame, int sourceSeat, int targetSeat);
internal delegate bool ProgramCompletedBindingReturn(ProgramSkillFrame frame, bool completed);
internal delegate void ProgramPlayPhaseDecisionPreparation(CharacterState actor);
internal delegate ForcedHandSelectionSnapshot? ProgramForcedHandSelectionProvider(int seat);
internal delegate IReadOnlyList<LegalAction> ProgramPlayPhaseActionFilter(CharacterState actor, IReadOnlyList<LegalAction> actions);
internal delegate bool ProgramDetachedBindingContinuation(ProgramSkillFrame frame);
internal delegate bool ProgramTurnStartObligationProbe(int seat);
internal delegate bool ProgramResponseUseCompletionObservation(CardActionContext action);
internal delegate string? ProgramCardEntitySkillLabelProvider(int cardId);
internal delegate int[]? ProgramDiscardBatchIndexSelector(
    CardMovementBatchContext batch, ProgramTriggerCandidate candidate, ProgramInstructionFeatures features);

internal delegate bool ProgramHealthChangeSubject(int targetSeat);
internal delegate void ProgramHealthChangeCapture(HpChangeContext change);
internal delegate IReadOnlyList<ProgramTriggerCandidate> ProgramHealthCandidateIncluder(HpChangeContext change, IReadOnlyList<ProgramTriggerCandidate> candidates);
internal delegate IReadOnlyList<ProgramTriggerCandidate> ProgramNativeDrawCandidateIncluder(int recipientSeat, IReadOnlyList<ProgramTriggerCandidate> candidates);
internal delegate CardUseEffectSource? ProgramSlashTargetDuelSource(CardUseFrame use, int targetSeat);
internal delegate bool ProgramIssuedTriggerCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
internal delegate bool ProgramTurnSuppressionClaim(ProgramSkillFrame frame, int targetSeat);
internal delegate bool ProgramRumoEntryClaim(ProgramSkillFrame frame);
internal delegate bool ProgramRumoOriginClaim(long id, CardConversionSource source, string hash);
internal delegate CardKind? ProgramVirtualEquipmentProvider(int seat, EquipmentSlot slot);

// Static metadata only; delegates are bound once per engine and never shared.
internal sealed class ProgramPipelineHookCatalog
{
    internal sealed record Registration(MethodInfo Method, Type DelegateType);

    private static readonly Dictionary<ProgramPipelinePhase, (Type DelegateType, Type[] Parameters, Type ReturnType)> Contracts =
        new()
        {
            [ProgramPipelinePhase.IncludeTriggerCandidate] = (typeof(ProgramTriggerCandidateIncluder),
                [typeof(CharacterState), typeof(SkillProgramTriggerWindow), typeof(IReadOnlyList<ProgramTriggerCandidate>)],
                typeof(IReadOnlyList<ProgramTriggerCandidate>)),
            [ProgramPipelinePhase.CollectIssuedCardTrigger] = (typeof(ProgramIssuedCardTriggerCollector),
                [typeof(CardActionContext), typeof(SkillProgramTriggerWindow), typeof(List<ProgramCardTriggerCandidate>)],
                typeof(void)),
            [ProgramPipelinePhase.CaptureVirtualDuelLegacy] = (typeof(ProgramVirtualDuelLegacyCapture),
                [typeof(ProgramSkillFrame), typeof(int), typeof(int)], typeof(void)),
            [ProgramPipelinePhase.ReturnCompletedProgramBinding] = (typeof(ProgramCompletedBindingReturn),
                [typeof(ProgramSkillFrame), typeof(bool)], typeof(bool)),
            [ProgramPipelinePhase.PreparePlayPhaseDecision] = (typeof(ProgramPlayPhaseDecisionPreparation),
                [typeof(CharacterState)], typeof(void)),
            [ProgramPipelinePhase.ForcedPlayHandSelection] = (typeof(ProgramForcedHandSelectionProvider),
                [typeof(int)], typeof(ForcedHandSelectionSnapshot)),
            [ProgramPipelinePhase.FilterPlayPhaseActions] = (typeof(ProgramPlayPhaseActionFilter),
                [typeof(CharacterState), typeof(IReadOnlyList<LegalAction>)], typeof(IReadOnlyList<LegalAction>)),
            [ProgramPipelinePhase.ContinueDetachedProgramBinding] = (typeof(ProgramDetachedBindingContinuation),
                [typeof(ProgramSkillFrame)], typeof(bool)),
            [ProgramPipelinePhase.UnissuedTurnStartObligation] = (typeof(ProgramTurnStartObligationProbe),
                [typeof(int)], typeof(bool)),
            [ProgramPipelinePhase.ObservesResponseUseCompletion] = (typeof(ProgramResponseUseCompletionObservation),
                [typeof(CardActionContext)], typeof(bool)),
            [ProgramPipelinePhase.CardEntitySkillLabel] = (typeof(ProgramCardEntitySkillLabelProvider),
                [typeof(int)], typeof(string)),
            [ProgramPipelinePhase.SelectDiscardBatchIndexes] = (typeof(ProgramDiscardBatchIndexSelector),
                [typeof(CardMovementBatchContext), typeof(ProgramTriggerCandidate), typeof(ProgramInstructionFeatures)],
                typeof(int[])),
            [ProgramPipelinePhase.HealthChangeSubject] = (typeof(ProgramHealthChangeSubject),
                [typeof(int)], typeof(bool)),
            [ProgramPipelinePhase.CaptureHealthChange] = (typeof(ProgramHealthChangeCapture),
                [typeof(HpChangeContext)], typeof(void)),
            [ProgramPipelinePhase.IncludeHealthChangeCandidates] = (typeof(ProgramHealthCandidateIncluder),
                [typeof(HpChangeContext), typeof(IReadOnlyList<ProgramTriggerCandidate>)], typeof(IReadOnlyList<ProgramTriggerCandidate>)),
            [ProgramPipelinePhase.IncludeNativeDrawCandidates] = (typeof(ProgramNativeDrawCandidateIncluder),
                [typeof(int), typeof(IReadOnlyList<ProgramTriggerCandidate>)], typeof(IReadOnlyList<ProgramTriggerCandidate>)),
            [ProgramPipelinePhase.SlashTargetDuelSource] = (typeof(ProgramSlashTargetDuelSource),
                [typeof(CardUseFrame), typeof(int)], typeof(CardUseEffectSource)),
            [ProgramPipelinePhase.IssuedTriggerCandidate] = (typeof(ProgramIssuedTriggerCandidate),
                [typeof(ProgramTriggerCandidate), typeof(ProgramSkillWindowContext)], typeof(bool)),
            [ProgramPipelinePhase.AdmitTurnSuppression] = (typeof(ProgramTurnSuppressionClaim),
                [typeof(ProgramSkillFrame), typeof(int)], typeof(bool)),
            [ProgramPipelinePhase.ActualPhaseSkipped] = (typeof(Func<ProgramActualPhaseSkipContext, bool>),
                [typeof(ProgramActualPhaseSkipContext)], typeof(bool)),
            [ProgramPipelinePhase.LifecycleWindowCompleted] = (typeof(Func<ProgramLifecycleTriggerWindowFrame, bool>),
                [typeof(ProgramLifecycleTriggerWindowFrame)], typeof(bool)),
            [ProgramPipelinePhase.AppendTurnEndingItems] = (typeof(Action<List<TurnEndingBoundaryItem>>),
                [typeof(List<TurnEndingBoundaryItem>)], typeof(void)),
            [ProgramPipelinePhase.ResumeScheduledPhase] = (typeof(Func<bool>), [], typeof(bool)),
            [ProgramPipelinePhase.CancelScheduledPhase] = (typeof(Func<bool>), [], typeof(bool)),
            [ProgramPipelinePhase.RumoEntry] = (typeof(ProgramRumoEntryClaim),
                [typeof(ProgramSkillFrame)], typeof(bool)),
            [ProgramPipelinePhase.RumoOrigin] = (typeof(ProgramRumoOriginClaim),
                [typeof(long), typeof(CardConversionSource), typeof(string)], typeof(bool)),
            [ProgramPipelinePhase.VirtualEquipment] = (typeof(ProgramVirtualEquipmentProvider),
                [typeof(int), typeof(EquipmentSlot)], typeof(CardKind?))
        };

    internal IReadOnlyDictionary<ProgramPipelinePhase, IReadOnlyList<Registration>> Phases { get; }

    private ProgramPipelineHookCatalog(
        Dictionary<ProgramPipelinePhase, SortedDictionary<int, Registration>> phases)
    {
        Phases = new ReadOnlyDictionary<ProgramPipelinePhase, IReadOnlyList<Registration>>(
            phases.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<Registration>)Array.AsReadOnly(pair.Value.Values.ToArray())));
    }

    internal static ProgramPipelineHookCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var phases = Enum.GetValues<ProgramPipelinePhase>().ToDictionary(phase => phase,
            _ => new SortedDictionary<int, Registration>());
        foreach (var method in methods.OrderBy(method => method.DeclaringType?.FullName, StringComparer.Ordinal)
                     .ThenBy(method => method.Name, StringComparer.Ordinal))
        {
            foreach (var attribute in method.GetCustomAttributes<ProgramPipelineHookAttribute>())
            {
                var contract = Contracts[attribute.Phase];
                ValidateSignature(method, contract);
                if (!Enum.IsDefined(attribute.Phase) || attribute.Order <= 0)
                    throw new InvalidOperationException(
                        $"Pipeline hook '{method.Name}' requires a valid phase and positive explicit order.");
                if (!phases[attribute.Phase].TryAdd(attribute.Order, new Registration(method, contract.DelegateType)))
                    throw new InvalidOperationException($"Duplicate pipeline hook order '{attribute.Phase}:{attribute.Order}'.");
            }
        }
        foreach (var (phase, registrations) in phases)
            if (registrations.Count == 0 && phase != ProgramPipelinePhase.VirtualEquipment)
                throw new InvalidOperationException($"Pipeline hook phase '{phase}' has no registrations.");
        return new(phases);
    }

    private static void ValidateSignature(MethodInfo method, (Type DelegateType, Type[] Parameters, Type ReturnType) contract)
    {
        if (method.IsStatic && !ProgramRuntimeModuleCatalog.IsDeclaredStaticHandler(method) || method.IsGenericMethod || method.ContainsGenericParameters ||
            method.ReturnType != contract.ReturnType)
            throw InvalidSignature(method);
        var parameters = method.GetParameters();
        if (parameters.Length != contract.Parameters.Length)
            throw InvalidSignature(method);
        for (var index = 0; index < parameters.Length; index++)
            if (parameters[index].ParameterType != contract.Parameters[index] ||
                parameters[index].IsOptional || parameters[index].ParameterType.IsByRef ||
                parameters[index].GetCustomAttribute<ParamArrayAttribute>() is not null)
                throw InvalidSignature(method);
    }

    private static InvalidOperationException InvalidSignature(MethodInfo method) =>
        new($"Pipeline hook '{method.DeclaringType?.Name}.{method.Name}' does not match its phase contract signature.");
}

// Bound once per engine; every dispatcher keeps the semantics of the pipeline
// position it replaced (fold, first-hit, or ordered fan-out).
internal sealed class ProgramPipelineHooks
{
    private readonly ProgramTriggerCandidateIncluder[] _includeTriggerCandidate;
    private readonly ProgramIssuedCardTriggerCollector[] _collectIssuedCardTrigger;
    private readonly ProgramVirtualDuelLegacyCapture[] _captureVirtualDuelLegacy;
    private readonly ProgramCompletedBindingReturn[] _returnCompletedProgramBinding;
    private readonly ProgramPlayPhaseDecisionPreparation[] _preparePlayPhaseDecision;
    private readonly ProgramForcedHandSelectionProvider[] _forcedPlayHandSelection;
    private readonly ProgramPlayPhaseActionFilter[] _filterPlayPhaseActions;
    private readonly ProgramDetachedBindingContinuation[] _continueDetachedProgramBinding;
    private readonly ProgramTurnStartObligationProbe[] _unissuedTurnStartObligation;
    private readonly ProgramResponseUseCompletionObservation[] _observesResponseUseCompletion;
    private readonly ProgramCardEntitySkillLabelProvider[] _cardEntitySkillLabel;
    private readonly ProgramDiscardBatchIndexSelector[] _selectDiscardBatchIndexes;

    private readonly ProgramHealthChangeSubject[] _healthChangeSubject;
    private readonly ProgramHealthChangeCapture[] _captureHealthChange;
    private readonly ProgramHealthCandidateIncluder[] _includeHealthChangeCandidates;
    private readonly ProgramNativeDrawCandidateIncluder[] _includeNativeDrawCandidates;
    private readonly ProgramSlashTargetDuelSource[] _slashTargetDuelSource;
    private readonly ProgramIssuedTriggerCandidate[] _issuedTriggerCandidate;
    private readonly ProgramTurnSuppressionClaim[] _admitTurnSuppression;
    private readonly ProgramRumoEntryClaim[] _rumoEntry;
    private readonly ProgramRumoOriginClaim[] _rumoOrigin;
    private readonly Func<ProgramActualPhaseSkipContext, bool>[] _actualPhaseSkipped;
    private readonly Func<ProgramLifecycleTriggerWindowFrame, bool>[] _lifecycleWindowCompleted;
    private readonly Action<List<TurnEndingBoundaryItem>>[] _appendTurnEndingItems;
    private readonly Func<bool>[] _resumeScheduledPhase, _cancelScheduledPhase;
    private readonly ProgramVirtualEquipmentProvider[] _virtualEquipment;

    internal ProgramPipelineHooks(ProgramPipelineHookCatalog catalog, GameEngine engine)
    {
        T[] Bind<T>(ProgramPipelinePhase phase) where T : class => catalog.Phases[phase]
            .Select(registration => engine.BindRuntimeHandler(registration.Method, registration.DelegateType))
            .Select(bound => (T)(object)bound)
            .ToArray();
        _includeTriggerCandidate = Bind<ProgramTriggerCandidateIncluder>(ProgramPipelinePhase.IncludeTriggerCandidate);
        _collectIssuedCardTrigger = Bind<ProgramIssuedCardTriggerCollector>(ProgramPipelinePhase.CollectIssuedCardTrigger);
        _captureVirtualDuelLegacy = Bind<ProgramVirtualDuelLegacyCapture>(ProgramPipelinePhase.CaptureVirtualDuelLegacy);
        _returnCompletedProgramBinding = Bind<ProgramCompletedBindingReturn>(ProgramPipelinePhase.ReturnCompletedProgramBinding);
        _preparePlayPhaseDecision = Bind<ProgramPlayPhaseDecisionPreparation>(ProgramPipelinePhase.PreparePlayPhaseDecision);
        _forcedPlayHandSelection = Bind<ProgramForcedHandSelectionProvider>(ProgramPipelinePhase.ForcedPlayHandSelection);
        _filterPlayPhaseActions = Bind<ProgramPlayPhaseActionFilter>(ProgramPipelinePhase.FilterPlayPhaseActions);
        _continueDetachedProgramBinding = Bind<ProgramDetachedBindingContinuation>(ProgramPipelinePhase.ContinueDetachedProgramBinding);
        _unissuedTurnStartObligation = Bind<ProgramTurnStartObligationProbe>(ProgramPipelinePhase.UnissuedTurnStartObligation);
        _observesResponseUseCompletion = Bind<ProgramResponseUseCompletionObservation>(ProgramPipelinePhase.ObservesResponseUseCompletion);
        _cardEntitySkillLabel = Bind<ProgramCardEntitySkillLabelProvider>(ProgramPipelinePhase.CardEntitySkillLabel);
        _selectDiscardBatchIndexes = Bind<ProgramDiscardBatchIndexSelector>(ProgramPipelinePhase.SelectDiscardBatchIndexes);
        _healthChangeSubject = Bind<ProgramHealthChangeSubject>(ProgramPipelinePhase.HealthChangeSubject);
        _captureHealthChange = Bind<ProgramHealthChangeCapture>(ProgramPipelinePhase.CaptureHealthChange);
        _includeHealthChangeCandidates = Bind<ProgramHealthCandidateIncluder>(ProgramPipelinePhase.IncludeHealthChangeCandidates);
        _includeNativeDrawCandidates = Bind<ProgramNativeDrawCandidateIncluder>(ProgramPipelinePhase.IncludeNativeDrawCandidates);
        _slashTargetDuelSource = Bind<ProgramSlashTargetDuelSource>(ProgramPipelinePhase.SlashTargetDuelSource);
        _issuedTriggerCandidate = Bind<ProgramIssuedTriggerCandidate>(ProgramPipelinePhase.IssuedTriggerCandidate);
        _admitTurnSuppression = Bind<ProgramTurnSuppressionClaim>(ProgramPipelinePhase.AdmitTurnSuppression);
        _rumoEntry = Bind<ProgramRumoEntryClaim>(ProgramPipelinePhase.RumoEntry);
        _rumoOrigin = Bind<ProgramRumoOriginClaim>(ProgramPipelinePhase.RumoOrigin);
        _actualPhaseSkipped = Bind<Func<ProgramActualPhaseSkipContext, bool>>(ProgramPipelinePhase.ActualPhaseSkipped);
        _lifecycleWindowCompleted = Bind<Func<ProgramLifecycleTriggerWindowFrame, bool>>(ProgramPipelinePhase.LifecycleWindowCompleted);
        _appendTurnEndingItems = Bind<Action<List<TurnEndingBoundaryItem>>>(ProgramPipelinePhase.AppendTurnEndingItems);
        _resumeScheduledPhase = Bind<Func<bool>>(ProgramPipelinePhase.ResumeScheduledPhase);
        _cancelScheduledPhase = Bind<Func<bool>>(ProgramPipelinePhase.CancelScheduledPhase);
        _virtualEquipment = Bind<ProgramVirtualEquipmentProvider>(ProgramPipelinePhase.VirtualEquipment);

    }

    internal IReadOnlyList<ProgramTriggerCandidate> IncludeTriggerCandidate(
        CharacterState owner, SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> candidates)
    {
        foreach (var include in _includeTriggerCandidate) candidates = include(owner, window, candidates);
        return candidates;
    }

    internal void CollectIssuedCardTrigger(
        CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        foreach (var collect in _collectIssuedCardTrigger) collect(action, window, result);
    }

    internal void CaptureVirtualDuelLegacy(ProgramSkillFrame frame, int sourceSeat, int targetSeat)
    {
        foreach (var capture in _captureVirtualDuelLegacy) capture(frame, sourceSeat, targetSeat);
    }

    internal bool ReturnCompletedProgramBinding(ProgramSkillFrame frame, bool completed)
    {
        foreach (var bindingReturn in _returnCompletedProgramBinding)
            if (bindingReturn(frame, completed)) return true;
        return false;
    }

    internal void PreparePlayPhaseDecision(CharacterState actor)
    {
        foreach (var prepare in _preparePlayPhaseDecision) prepare(actor);
    }

    internal ForcedHandSelectionSnapshot? ForcedPlayHandSelection(int seat)
    {
        foreach (var provider in _forcedPlayHandSelection)
            if (provider(seat) is { } snapshot) return snapshot;
        return null;
    }

    internal IReadOnlyList<LegalAction> FilterPlayPhaseActions(CharacterState actor, IReadOnlyList<LegalAction> actions)
    {
        foreach (var filter in _filterPlayPhaseActions) actions = filter(actor, actions);
        return actions;
    }

    internal bool ContinueDetachedProgramBinding(ProgramSkillFrame frame)
    {
        foreach (var continuation in _continueDetachedProgramBinding)
            if (continuation(frame)) return true;
        return false;
    }

    internal bool HasUnissuedTurnStartObligation(int seat)
    {
        foreach (var probe in _unissuedTurnStartObligation)
            if (probe(seat)) return true;
        return false;
    }

    internal bool ObservesResponseUseCompletion(CardActionContext action)
    {
        foreach (var observation in _observesResponseUseCompletion)
            if (observation(action)) return true;
        return false;
    }

    internal IReadOnlyList<string> CardEntitySkillLabels(int cardId)
    {
        List<string>? labels = null;
        foreach (var provider in _cardEntitySkillLabel)
            if (provider(cardId) is { } label)
                (labels ??= []).Add(label);
        return (IReadOnlyList<string>?)labels ?? [];
    }

    internal int[]? SelectDiscardBatchIndexes(
        CardMovementBatchContext batch, ProgramTriggerCandidate candidate, ProgramInstructionFeatures features)
    {
        foreach (var selector in _selectDiscardBatchIndexes)
            if (selector(batch, candidate, features) is { } indexes) return indexes;
        return null;
    }

    internal bool HealthChangeSubject(int targetSeat)
    {
        foreach (var hook in _healthChangeSubject)
            if (hook(targetSeat)) return true;
        return false;
    }

    internal void CaptureHealthChange(HpChangeContext change)
    {
        foreach (var hook in _captureHealthChange) hook(change);
    }

    internal IReadOnlyList<ProgramTriggerCandidate> IncludeHealthChangeCandidates(HpChangeContext change, IReadOnlyList<ProgramTriggerCandidate> candidates)
    {
        foreach (var hook in _includeHealthChangeCandidates) candidates = hook(change, candidates);
        return candidates;
    }

    internal IReadOnlyList<ProgramTriggerCandidate> IncludeNativeDrawCandidates(int recipientSeat, IReadOnlyList<ProgramTriggerCandidate> candidates)
    {
        foreach (var hook in _includeNativeDrawCandidates) candidates = hook(recipientSeat, candidates);
        return candidates;
    }

    internal CardUseEffectSource? SlashTargetDuelSource(CardUseFrame use, int targetSeat)
    {
        foreach (var hook in _slashTargetDuelSource)
            if (hook(use, targetSeat) is { } source) return source;
        return null;
    }

    internal bool IssuedTriggerCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        foreach (var hook in _issuedTriggerCandidate)
            if (hook(candidate, context)) return true;
        return false;
    }

    internal bool AdmitTurnSuppression(ProgramSkillFrame frame, int targetSeat)
    {
        foreach (var hook in _admitTurnSuppression)
            if (hook(frame, targetSeat)) return true;
        return false;
    }

    internal bool TryBeginPhaseSkipped(ProgramActualPhaseSkipContext context)
    {
        foreach (var hook in _actualPhaseSkipped) if (hook(context)) return true;
        return false;
    }
    internal bool LifecycleWindowCompleted(ProgramLifecycleTriggerWindowFrame frame)
    {
        foreach (var hook in _lifecycleWindowCompleted) if (hook(frame)) return true;
        return false;
    }
    internal void AppendTurnEndingItems(List<TurnEndingBoundaryItem> items)
    {
        foreach (var hook in _appendTurnEndingItems) hook(items);
    }
    internal bool ResumeScheduledPhase()
    {
        foreach (var hook in _resumeScheduledPhase) if (hook()) return true;
        return false;
    }
    internal bool CancelScheduledPhase()
    {
        foreach (var hook in _cancelScheduledPhase) if (hook()) return true;
        return false;
    }

    internal bool RumoEntry(ProgramSkillFrame frame)
    {
        foreach (var hook in _rumoEntry)
            if (hook(frame)) return true;
        return false;
    }

    internal bool RumoOrigin(long id, CardConversionSource source, string hash)
    {
        foreach (var hook in _rumoOrigin)
            if (hook(id, source, hash)) return true;
        return false;
    }

    internal CardKind? VirtualEquipment(int seat, EquipmentSlot slot)
    {
        foreach (var provider in _virtualEquipment)
            if (provider(seat, slot) is { } kind) return kind;
        return null;
    }
}

public sealed partial class GameEngine
{
    private CardKind? ProgramVirtualEquipment(int seat, EquipmentSlot slot) =>
        _programPipelineHooks.VirtualEquipment(seat, slot);
}
