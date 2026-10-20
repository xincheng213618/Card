using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record AdoptedChildRelationshipReceipt(long FrameId, CardConversionSource Source,
    string GameplayHash, long ParentFrameId, int ActualTurn, int? PreviousHolder, bool Transfer);
public sealed record AdoptedChildSlashUseReceipt(long CardUseFrameId, int ActorSeat, int ActualTurn, int PhaseInstance);
public sealed record AdoptedChildDamageReceipt(long DamageFrameId, long CardUseFrameId, int ActorSeat,
    int TargetSeat, int ActualTurn, int PhaseInstance, string SkillInstanceId);
public sealed record AdoptedChildDamageIssue(long CardUseFrameId, int ActorSeat, int TargetSeat,
    int ActualTurn, int PhaseInstance, string SkillInstanceId);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AdoptedChildRelationshipReceipt? AdoptedChildRelationship { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AdoptedChildSlashUseReceipt? AdoptedChildSlashUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AdoptedChildDamageIssue? AdoptedChildDamageIssue { get; init; }
}
public sealed partial record DamageFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AdoptedChildDamageReceipt? AdoptedChildDamage { get; init; }
}
public sealed record AdoptedChildAssignedEvent(long FrameId, CardConversionSource Source,
    int? PreviousHolder, int HolderSeat, int ActualTurn) : IGameEvent;
public sealed record AdoptedChildSkillGrantChangedEvent(int HolderSeat, string GrantId,
    string SkillInstanceId, bool Granted) : IGameEvent;
public sealed record RoundFirstSourcedDamageEvent(int RoundNumber, long DamageFrameId,
    int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record FirstRoundDamageQualifiedEvent(long FrameId, long DamageFrameId,
    int RoundNumber, int OwnerSeat, string SkillInstanceId, string BindingId) : IGameEvent;
public sealed record AdoptedChildPlayEndRequestedEvent(long DamageFrameId, long CardUseFrameId,
    int ActorSeat, int TargetSeat, int ActualTurn, int PhaseInstance) : IGameEvent;

internal interface IDingYuanProgramHost
{
    void PrepareAdoptedChild(ProgramSkillFrame frame, bool transfer);
    void AssignAdoptedChild(ProgramSkillFrame frame);
    void QualifyFirstRoundDamage(ProgramSkillFrame frame);
}
internal abstract class DingYuanOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override ISkillProgramEffectHandler Handler => new DingYuanOperationHandler(Op);
}
internal sealed class PrepareAdoptedChildAppointmentDescriptor : DingYuanOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareAdoptedChildAppointment;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class PrepareAdoptedChildTransferDescriptor : DingYuanOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareAdoptedChildTransfer;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class AssignAdoptedChildDescriptor : DingYuanOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AssignAdoptedChild;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow), new ReadSelectedTarget()];
}
internal sealed class QualifyFirstRoundDamageDescriptor : DingYuanOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QualifyFirstRoundDamage;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DingYuanOperationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        var capability = host.GetCapability<IDingYuanProgramHost>();
        switch (op)
        {
            case SkillProgramEffectOp.PrepareAdoptedChildAppointment: capability.PrepareAdoptedChild(frame, false); break;
            case SkillProgramEffectOp.PrepareAdoptedChildTransfer: capability.PrepareAdoptedChild(frame, true); break;
            case SkillProgramEffectOp.AssignAdoptedChild: capability.AssignAdoptedChild(frame); break;
            case SkillProgramEffectOp.QualifyFirstRoundDamage: capability.QualifyFirstRoundDamage(frame); break;
            default: throw new InvalidOperationException("Unknown adopted-child instruction.");
        }
        return SkillProgramStepOutcome.Continue;
    }
}

internal readonly record struct AdoptedChildParticipant(int Seat, bool Alive, bool HasMark, bool HasCixiao);
internal interface IAdoptedChildSelectionQuery : IProgramSelectionQuery
{
    IReadOnlyList<AdoptedChildParticipant> Participants { get; }
}
[ProgramTargetResolver(SkillProgramTargetKind.AdoptedChildSuccessor, SkillProgramTargetKind.LivingCixiaoOwner)]
internal sealed class AdoptedChildTargetResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) =>
        Array.AsReadOnly(queries.Get<IAdoptedChildSelectionQuery>().Participants.Where(player =>
            player.Alive && (context.TargetKind == SkillProgramTargetKind.AdoptedChildSuccessor
                ? !player.HasMark && (player.Seat != context.OwnerSeat || queries.Get<IAdoptedChildSelectionQuery>().Participants.Any(person => person.Alive && person.HasMark))
                : player.Seat != context.OwnerSeat && player.HasCixiao))
            .Select(player => player.Seat).ToArray());
}

public sealed partial class GameEngine
{
    private const string AdoptedChildGrantSource = "marker:adopted-child";
    private bool _synchronizingAdoptedChildGrants;
    private bool TracksDingYuan => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.PrepareAdoptedChildAppointment) == true;
    private int? AdoptedChildHolder => _players.Where(player => player.IsAlive && player.Markers.GetValueOrDefault(PlayerMarkerKind.AdoptedChild) > 0)
        .Select(player => (int?)player.Seat).SingleOrDefault();
    private static string AdoptedChildGrantId(int seat) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ding_yuan_ol_ProgramDingYuan.AdoptedChildGrantId(seat);

    [ProgramCapability]
    private sealed class DingYuanProgramCapability(GameEngine engine) : IDingYuanProgramHost
    {
        public void PrepareAdoptedChild(ProgramSkillFrame frame, bool transfer) => engine.PrepareAdoptedChild(frame, transfer);
        public void AssignAdoptedChild(ProgramSkillFrame frame) => engine.AssignAdoptedChild(frame);
        public void QualifyFirstRoundDamage(ProgramSkillFrame frame) => engine.QualifyFirstRoundDamage(frame);
    }
    [ProgramCapability]
    private sealed class AdoptedChildSelectionQuery(GameEngine engine) : IAdoptedChildSelectionQuery
    {
        public IReadOnlyList<AdoptedChildParticipant> Participants => Array.AsReadOnly(engine._players.Select(player =>
            new AdoptedChildParticipant(player.Seat, player.IsAlive,
                player.Markers.GetValueOrDefault(PlayerMarkerKind.AdoptedChild) > 0,
                engine.HasRuntimeSkill(player, "ol:cixiao"))).ToArray());
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26020)]
    private bool CanOfferDingYuan(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(effect => effect.Op is SkillProgramEffectOp.PrepareAdoptedChildAppointment or SkillProgramEffectOp.PrepareAdoptedChildTransfer))
        {
            var transfer = trigger.Effects[0].Op == SkillProgramEffectOp.PrepareAdoptedChildTransfer;
            return context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && candidate.OwnerSeat == _currentSeat &&
                transfer == (AdoptedChildHolder is not null) &&
                _players.Any(player => player.IsAlive && (transfer || player.Seat != candidate.OwnerSeat) && player.Seat != AdoptedChildHolder) &&
                (!transfer || GetHand(_players[candidate.OwnerSeat]).Count + GetEquipment(_players[candidate.OwnerSeat]).Count > 0);
        }
        if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.QualifyFirstRoundDamage))
            return FirstRoundDamageFor(context) is not null &&
                !CompleteProgramEventHistory().OfType<FirstRoundDamageQualifiedEvent>().Any(fact =>
                    fact.DamageFrameId == context.DamageFrameId && fact.OwnerSeat == candidate.OwnerSeat &&
                    fact.SkillInstanceId == candidate.SkillInstanceId && fact.BindingId == candidate.BindingId);
        if (candidate.SkillId == "ol:panshi" && trigger.Id == "preparation-tribute")
            return GetHand(_players[candidate.OwnerSeat]).Count > 0 && _players.Any(player =>
                player.IsAlive && player.Seat != candidate.OwnerSeat && HasRuntimeSkill(player, "ol:cixiao"));
        return true;
    }

    private void PrepareAdoptedChild(ProgramSkillFrame supplied, bool transfer)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || frame.AdoptedChildRelationship is not null || frame.WindowContext is not { } context ||
            frame.SkillId != "ol:cixiao" || !CanOfferDingYuan(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!,
                frame.SkillInstanceId, frame.GameplayHash, 0), GetProgramTrigger(frame), context) ||
            transfer != (AdoptedChildHolder is not null))
            throw new InvalidOperationException("An adopted-child relationship requires its exact own preparation and initial relation.");
        ReplaceRuntimeFrame(frame.Id, frame with { AdoptedChildRelationship = new(frame.Id,
            new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash,
            context.ParentFrameId, _turnNumber, AdoptedChildHolder, transfer) });
    }

    private void AssignAdoptedChild(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.AdoptedChildRelationship is not { } receipt || receipt.FrameId != frame.Id ||
            receipt.Source.OwnerSeat != frame.OwnerSeat || receipt.Source.SkillInstanceId != frame.SkillInstanceId ||
            receipt.GameplayHash != frame.GameplayHash || receipt.ParentFrameId != frame.WindowContext?.ParentFrameId ||
            receipt.ActualTurn != _turnNumber || frame.InstructionIndex != (receipt.Transfer ? 4 : 3) ||
            frame.SelectedTargetSeats is not [var successor])
            throw new InvalidOperationException("An adopted-child assignment lost its owning preparation receipt.");
        // The ordinary movement instruction owns and remembers a paid transfer.
        // A nested relationship change can invalidate it, never charge it again.
        if (receipt.PreviousHolder != AdoptedChildHolder || !_players[successor].IsAlive ||
            !receipt.Transfer && successor == frame.OwnerSeat || successor == receipt.PreviousHolder) return;
        if (receipt.PreviousHolder is { } old) SetStrategicMarker(old, PlayerMarkerKind.AdoptedChild, frame.OwnerSeat, 0, frame);
        SetStrategicMarker(successor, PlayerMarkerKind.AdoptedChild, frame.OwnerSeat, 1, frame);
        AdvanceEventRulesAndQueueFact(new AdoptedChildAssignedEvent(frame.Id, receipt.Source, receipt.PreviousHolder, successor, _turnNumber));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26020)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 26020)]
    private void SynchronizeAdoptedChildSkillGrants()
    {
        if (!TracksDingYuan || _synchronizingAdoptedChildGrants) return;
        _synchronizingAdoptedChildGrants = true;
        try
        {
            foreach (var player in _players)
            {
                var id = AdoptedChildGrantId(player.Seat);
                var existing = player.SkillGrants.Grants.SingleOrDefault(grant => grant.GrantId == id);
                var marked = player.IsAlive && player.Markers.GetValueOrDefault(PlayerMarkerKind.AdoptedChild) > 0;
                if (!marked && existing is not null)
                {
                    player.SkillGrants.RemoveGrant(id);
                    AdvanceEventRulesAndQueueFact(new AdoptedChildSkillGrantChangedEvent(player.Seat, id, existing.SkillInstanceId, false));
                }
                else if (marked && existing is null)
                {
                    var instance = GetSkillBindingShard(player).ActiveGrants.Where(grant => grant.SkillId == "ol:panshi")
                        .OrderBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()?.SkillInstanceId ?? id;
                    player.SkillGrants.Grant(new(id, "ol:panshi", instance, AdoptedChildGrantSource));
                    AdvanceEventRulesAndQueueFact(new AdoptedChildSkillGrantChangedEvent(player.Seat, id, instance, true));
                }
            }
        }
        finally { _synchronizingAdoptedChildGrants = false; }
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26021)]
    private void ObserveDingYuanNativeFacts(IGameEvent payload)
    {
        if (TracksDingYuan && payload is CardUseDeclaredEvent declared && _phase == TurnPhase.Play &&
            declared.SourceSeat == _currentSeat && IsSlashCard(declared.CardKind) &&
            LifecycleCardUse(declared.ResolutionId) is { } use && use.SourceSeat == declared.SourceSeat &&
            use.CardKind == declared.CardKind && use.AdoptedChildSlashUse is null)
            ReplaceRuntimeFrame(use.Id, use with { AdoptedChildSlashUse = new(use.Id, use.SourceSeat,
                _turnNumber, _cardUseDebitPhaseInstanceId) });
        if (payload is DamageRequestedEvent requested && CurrentDamageAttempt is { } requestedAttack &&
            _resolutionStack.LastOrDefault() is DamageFrame requestedFrame && requestedFrame.Id == requested.ResolutionId &&
            requestedFrame.ParentFrameId == requestedAttack.ResolutionId && requestedFrame.AdoptedChildDamage is null &&
            requestedFrame.TargetSeat == requested.TargetSeat && requestedFrame.SourceSeat == requested.SourceSeat &&
            LifecycleCardUse(requestedAttack.ResolutionId)?.AdoptedChildDamageIssue is { } issue &&
            issue.CardUseFrameId == requestedAttack.ResolutionId && issue.ActorSeat == requested.SourceSeat &&
            issue.TargetSeat == requested.TargetSeat && issue.ActualTurn == _turnNumber && issue.PhaseInstance == _cardUseDebitPhaseInstanceId)
            ReplaceRuntimeFrame(requestedFrame.Id, requestedFrame with { AdoptedChildDamage = new(requestedFrame.Id,
                issue.CardUseFrameId, requested.SourceSeat, requested.TargetSeat, issue.ActualTurn, issue.PhaseInstance,
                issue.SkillInstanceId) });
        if (payload is not DamageAppliedEvent { Amount: > 0 } applied ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || CurrentDamageAttempt is not { } attack ||
            damage.ParentFrameId != attack.ResolutionId || damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat ||
            damage.Amount != applied.Amount || damage.Nature != applied.Nature || attack.IsSourceLess != applied.SourceLess || !attack.DamageWasApplied) return;
        if (TracksRoundFirstDamage && _roundNumber > 0 && !applied.SourceLess &&
            !CompleteProgramEventHistory().OfType<RoundFirstSourcedDamageEvent>().Any(fact => fact.RoundNumber == _roundNumber))
            AdvanceEventRulesAndQueueFact(new RoundFirstSourcedDamageEvent(_roundNumber, damage.Id,
                applied.SourceSeat, applied.TargetSeat, applied.Amount));
        if (damage.AdoptedChildDamage is { } debt && debt.DamageFrameId == damage.Id &&
            debt.CardUseFrameId == attack.ResolutionId && debt.ActorSeat == applied.SourceSeat && debt.TargetSeat == applied.TargetSeat &&
            debt.ActualTurn == _turnNumber && debt.ActorSeat == _currentSeat && debt.PhaseInstance == _cardUseDebitPhaseInstanceId &&
            _phase == TurnPhase.Play && !CompleteProgramEventHistory().OfType<AdoptedChildPlayEndRequestedEvent>()
                .Any(fact => fact.DamageFrameId == damage.Id))
        {
            _strategicEndPlayTurn = _turnNumber;
            AdvanceEventRulesAndQueueFact(new AdoptedChildPlayEndRequestedEvent(damage.Id, debt.CardUseFrameId,
                debt.ActorSeat, debt.TargetSeat, debt.ActualTurn, debt.PhaseInstance));
        }
    }

    private void CaptureAdoptedChildSlashDamageIssue(IDamageAttempt attack)
    {
        if (!TracksDingYuan || LifecycleCardUse(attack.ResolutionId) is not { } use) return;
        AdoptedChildDamageIssue? issue = IsAdoptedChildSlashDamage(attack, out var origin)
            ? new(use.Id, attack.SourceSeat, attack.TargetSeat, origin.ActualTurn, origin.PhaseInstance,
                GetRuntimeSkillInstanceId(_players[attack.SourceSeat], "ol:panshi")) : null;
        if (use.AdoptedChildDamageIssue != issue)
            ReplaceRuntimeFrame(use.Id, use with { AdoptedChildDamageIssue = issue });
    }
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 90)]
    private int AdoptedChildSlashDamage(IDamageAttempt attack) =>
        !attack.IsChainPropagation && LifecycleCardUse(attack.ResolutionId)?.AdoptedChildDamageIssue is { } issue &&
        issue.CardUseFrameId == attack.ResolutionId && issue.ActorSeat == attack.SourceSeat &&
        issue.TargetSeat == attack.TargetSeat && issue.ActualTurn == _turnNumber && issue.PhaseInstance == _cardUseDebitPhaseInstanceId ? 1 : 0;
    private bool IsAdoptedChildSlashDamage(IDamageAttempt attack, out AdoptedChildSlashUseReceipt origin)
    {
        origin = null!;
        // OL's operator FAQ requires a direct Slash target. Elemental damage
        // still propagates its finalized amount, but a chained recipient cannot
        // acquire a second Panshi bonus or an independent end-Play consequence.
        if (!TracksDingYuan || attack.IsSourceLess || attack.IsChainPropagation || attack.CardUserSeat != attack.SourceSeat ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            !IsValidPlayerSeat(attack.SourceSeat) || !IsValidPlayerSeat(attack.TargetSeat) || attack.SourceSeat == attack.TargetSeat ||
            !HasRuntimeSkill(_players[attack.SourceSeat], "ol:panshi") || !HasRuntimeSkill(_players[attack.TargetSeat], "ol:cixiao") ||
            LifecycleCardUse(attack.ResolutionId) is not { AdoptedChildSlashUse: { } stamp } use ||
            use.SourceSeat != attack.SourceSeat || use.CardKind != kind || stamp.CardUseFrameId != use.Id ||
            stamp.ActorSeat != attack.SourceSeat || stamp.ActualTurn != _turnNumber || stamp.ActorSeat != _currentSeat ||
            stamp.PhaseInstance != _cardUseDebitPhaseInstanceId || _phase != TurnPhase.Play) return false;
        origin = stamp; return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26020)]
    private void AssertAdoptedChildRelationshipFrame(ProgramSkillFrame frame)
    {
        if (frame.AdoptedChildRelationship is not { } receipt) return;
        if (frame.SkillId != "ol:cixiao" || receipt.FrameId != frame.Id ||
            receipt.Source != new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId) ||
            receipt.GameplayHash != frame.GameplayHash || receipt.ParentFrameId != frame.WindowContext?.ParentFrameId ||
            receipt.ActualTurn != _turnNumber || receipt.Transfer != (receipt.PreviousHolder is not null) ||
            frame.InstructionIndex is < 1 || frame.InstructionIndex > (receipt.Transfer ? 4 : 3))
            throw new InvalidOperationException("An adopted-child frame lost its exact source, initial relationship or preparation.");
    }
}
