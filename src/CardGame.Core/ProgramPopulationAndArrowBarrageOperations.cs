using System.Collections.ObjectModel;
namespace CardGame.Core;

public sealed record ProgramPopulationMarkerGrant(int OwnerSeat, string SkillId);
public sealed record ProgramArrowBarrageExclusion(long ProducerFrameId, int OwnerSeat, string SkillId,
    string SkillInstanceId, long CardActionId, int TargetSeat);
public sealed record ProgramArrowBarrageTargetRemovedEvent(long CardUseFrameId, long CardActionId,
    int OwnerSeat, string SkillId, int TargetSeat) : IGameEvent;

public interface IPopulationAndArrowBarrageProgramHost
{
    void GrantFactionPopulationMarker(ProgramSkillFrame frame, SkillProgramEffect effect);
    void RemoveSelectedCurrentArrowBarrageTarget(ProgramSkillFrame frame);
}
internal sealed class GrantFactionPopulationMarkerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantFactionPopulationMarker;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantFactionPopulationMarkerHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "factionId", "marker", "multiplier", "condition");
        var multiplier = r.RequiredInt("multiplier");
        if (multiplier < 0) throw new InvalidOperationException("Population marker multiplier must be nonnegative.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), multiplier,
            r.Condition(), marker: r.RequiredEnum<PlayerMarkerKind>("marker"), providerFactionId: r.RequiredIdentifier("factionId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class RemoveSelectedCurrentArrowBarrageTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RemoveSelectedCurrentArrowBarrageTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new RemoveSelectedCurrentArrowBarrageTargetHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifySelectedCardEffects,
        static (_, context) => context.NullifySelectedCardEffects());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTargets)
            throw new InvalidOperationException("ArrowBarrage exclusion requires the selected target set.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTargets, 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadTargetSet(1, 1), new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.ArrowBarrage])];
}
public sealed class GrantFactionPopulationMarkerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantFactionPopulationMarker;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host)
    { ((IPopulationAndArrowBarrageProgramHost)host).GrantFactionPopulationMarker(f, e); return SkillProgramStepOutcome.Continue; }
}
public sealed class RemoveSelectedCurrentArrowBarrageTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RemoveSelectedCurrentArrowBarrageTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host)
    { ((IPopulationAndArrowBarrageProgramHost)host).RemoveSelectedCurrentArrowBarrageTarget(f); return SkillProgramStepOutcome.Continue; }
}
public sealed partial class GameEngine
{
    private bool HasInitialPopulationMarkerGrant(int ownerSeat, string skillId, ProgramSkillWindowContext context) =>
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId)
            ?.PopulationMarkerGrants?.Contains(new(ownerSeat, skillId)) == true;

    private void GrantProgramFactionPopulationMarker(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not { Window: SkillProgramTriggerWindow.GameStarting } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.GameStarting, FrozenFactionPopulation: { } population } parent ||
            parent.Id != context.ParentFrameId || !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId) ||
            !HasSkillRoleQualification(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId, Role.Lord))
            throw new InvalidOperationException("Population marker grant lost its qualified game-start owner.");
        if (HasInitialPopulationMarkerGrant(active.OwnerSeat, active.SkillId, context)) return;
        var amount = checked(effect.Amount * population.GetValueOrDefault(effect.ProviderFactionId!));
        if (amount < 0) throw new InvalidOperationException("Population marker amount is negative.");
        ReplaceRuntimeFrame(parent.Id, parent with { PopulationMarkerGrants = Array.AsReadOnly(
            (parent.PopulationMarkerGrants ?? []).Append(new(active.OwnerSeat, active.SkillId)).ToArray()) });
        if (amount > 0) MutateParticipantMarker(active, active.OwnerSeat, effect.Marker!.Value, amount);
    }

    // Candidate, submit and execution all share this exact current owning-use query.
    // The historical CurrentCardUseTargets selector deliberately keeps its frozen semantics.
    private IReadOnlyList<int> GetCurrentArrowBarrageTargets(int ownerSeat, ProgramSkillWindowContext? context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } cardContext } ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is not { } window ||
            window.Id != context.ParentFrameId || window.ParentFrameId != cardContext.ParentCardUseFrameId ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == window.ParentFrameId) is not { Action: { } action } use ||
            window.Action.ActionId != action.ActionId || action.ActionId != cardContext.CardActionId ||
            action.Type != CardActionType.Use || action.EffectiveKind != CardKind.ArrowBarrage ||
            action.ActorSeat != ownerSeat || use.SourceSeat != ownerSeat || use.ArrowBarrageExclusion is not null) return [];
        return use.TargetSeats.Where(seat => action.EffectiveDesignatedTargetSeats.Contains(seat) &&
            _players[seat].IsAlive).Distinct().ToArray();
    }

    private void RemoveProgramSelectedCurrentArrowBarrageTarget(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var targetSeat] ||
            !GetCurrentArrowBarrageTargets(active.OwnerSeat, active.WindowContext).Contains(targetSeat))
            throw new InvalidOperationException("The ArrowBarrage exclusion no longer names one actual remaining target.");
        var cardContext = active.WindowContext!.CardUse!;
        var use = _resolutionStack.OfType<CardUseFrame>().Single(u => u.Id == cardContext.ParentCardUseFrameId);
        var targets = Array.AsReadOnly(use.TargetSeats.Where(seat => seat != targetSeat).ToArray());
        var action = CloneRoleAction(use.Action!, use.SourceSeat, targets);
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action,
            ArrowBarrageExclusion = new(active.Id, active.OwnerSeat, active.SkillId, active.SkillInstanceId, action.ActionId, targetSeat) }, action);
        // Rebase only this changed actual use's remaining candidate contexts. Historical
        // target-confirmed facts and the old selector's query remain untouched.
        foreach (var window in _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                     .Where(window => window.ParentFrameId == use.Id).ToArray())
            ReplaceRuntimeFrame(window.Id, window with { Candidates = Array.AsReadOnly(window.Candidates.Select(candidate =>
                candidate.FrozenContext?.CardUse is { } frozen && frozen.ParentCardUseFrameId == use.Id
                    ? candidate with { FrozenContext = candidate.FrozenContext with { CardUse = frozen with {
                        ActorSeat = action.ActorSeat, DesignatedTargetSeats = targets } } } : candidate).ToArray()) });
        for (var i = 0; i < _resolutionStack.Count; i++)
            if (_resolutionStack[i] is ProgramSkillFrame program && program.WindowContext?.CardUse is { } current &&
                current.ParentCardUseFrameId == use.Id)
                ReplaceRuntimeFrame(program.Id, program with { WindowContext = program.WindowContext with {
                    CardUse = current with { ActorSeat = action.ActorSeat, DesignatedTargetSeats = targets } } });
        AdvanceEventRulesAndQueueFact(new ProgramArrowBarrageTargetRemovedEvent(use.Id, action.ActionId, active.OwnerSeat, active.SkillId, targetSeat));
    }
    private sealed partial class ProgramSkillHost : IPopulationAndArrowBarrageProgramHost
    {
        public void GrantFactionPopulationMarker(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.GrantProgramFactionPopulationMarker(frame, effect);
        public void RemoveSelectedCurrentArrowBarrageTarget(ProgramSkillFrame frame) => engine.RemoveProgramSelectedCurrentArrowBarrageTarget(frame);
    }
}
