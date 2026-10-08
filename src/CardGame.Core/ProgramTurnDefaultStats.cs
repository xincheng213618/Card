using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum TurnDefaultStatKind { DrawCount, AttackRange, SlashLimit, HandLimit }

// Slots retain their original index even when their values become equal. The
// generic contract uses the first minimum slot for each positive damage event,
// and consumes next-turn values at the actual turn boundary, including a
// face-down skipped turn. These ordering rules are not an official FAQ claim.
public sealed record TurnDefaultStatPoolInitializedEvent(CardConversionSource Source, string GameplayHash,
    string StateId, int ActualTurnNumber, int ActualTurnOwnerSeat, bool HasPool, bool MidTurn,
    int Slot0, int Slot1, int Slot2, int Slot3) : IGameEvent;
public sealed record TurnDefaultStatMinimumIncreasedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, string StateId, long DamageWindowId, long DamageFrameId, long AttackFrameId,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int Amount, DamageNature Nature,
    int SlotIndex, int Before, int After) : IGameEvent;
public sealed record TurnDefaultStatAllocationStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, string StateId, long ParentFrameId, SkillProgramTriggerWindow Window,
    int ActualTurnNumber, int ActualTurnOwnerSeat, TurnDefaultStatKind FirstStat, int UsedSlotMask,
    int Slot0, int Slot1, int Slot2, int Slot3) : IGameEvent;
public sealed record TurnDefaultStatAssignedEvent(long FrameId, CardConversionSource Source, string StateId,
    int ActualTurnNumber, int ActualTurnOwnerSeat, TurnDefaultStatKind Stat, int SlotIndex,
    int Value, int UsedSlotMask) : IGameEvent;

public sealed record ProgramTurnDefaultStatAllocationReceipt
{
    private IReadOnlyList<int> _slots = Array.AsReadOnly(Array.Empty<int>());
    public required TurnDefaultStatAllocationStartedEvent Origin { get; init; }
    public TurnDefaultStatKind CurrentStat { get; init; }
    public int UsedSlotMask { get; init; }
    public IReadOnlyList<int> Slots { get => _slots; init => _slots = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramTurnDefaultStatAllocationReceipt? TurnDefaultStatAllocation { get; init; }
}

internal interface ITurnDefaultStatsProgramHost
{
    SkillProgramStepOutcome ExecuteTurnDefaultStatOperation(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class TurnDefaultStatsDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => Op == SkillProgramEffectOp.AllocateCurrentTurnDefaultStats
        ? new AllocateCurrentTurnDefaultStatsHandler() : new IncreaseNextTurnDefaultStatMinimumHandler();
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.AllocateCurrentTurnDefaultStats
        ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramContextCapability RequiredCapabilities => Op == SkillProgramEffectOp.IncreaseNextTurnDefaultStatMinimum
        ? ProgramContextCapability.Damage : ProgramContextCapability.None;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Op == SkillProgramEffectOp.AllocateCurrentTurnDefaultStats
            ? [new RequireTriggerWindows([SkillProgramTriggerWindow.DrawPhaseStarting,
                SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramTriggerWindow.DiscardPhaseStarting])]
            : [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class AllocateCurrentTurnDefaultStatsDescriptor : TurnDefaultStatsDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AllocateCurrentTurnDefaultStats; }
internal sealed class IncreaseNextTurnDefaultStatMinimumDescriptor : TurnDefaultStatsDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.IncreaseNextTurnDefaultStatMinimum; }
public abstract class TurnDefaultStatsHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((ITurnDefaultStatsProgramHost)host).ExecuteTurnDefaultStatOperation(frame, effect);
}
public sealed class AllocateCurrentTurnDefaultStatsHandler : TurnDefaultStatsHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.AllocateCurrentTurnDefaultStats; }
public sealed class IncreaseNextTurnDefaultStatMinimumHandler : TurnDefaultStatsHandler
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.IncreaseNextTurnDefaultStatMinimum; }

internal static class TurnDefaultStatsComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.AllocateCurrentTurnDefaultStats or
        SkillProgramEffectOp.IncreaseNextTurnDefaultStatMinimum;
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var program in programs.Values)
        {
            if (program.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op))))
                throw new InvalidOperationException("Turn-default stat operations are trigger-only.");
            var bindings = program.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))).ToArray();
            foreach (var trigger in bindings)
            {
                if (trigger.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Amount: 1,
                        Condition.Kind: SkillProgramConditionKind.Always } effect] ||
                    trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.Optional ||
                    trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null ||
                    trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null || trigger.ChoiceGroup is not null ||
                    trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null || trigger.Subject != SkillProgramTriggerSubject.Owner ||
                    trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || trigger.DamageCardKinds.Count != 0 ||
                    trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 || trigger.Suits.Count != 0 ||
                    trigger.RequireDamageSource is not null || trigger.RequireNoCardConversion is not null)
                    throw new InvalidOperationException("Turn-default stats require sole mandatory unconditional owner instructions without quotas or card filters.");
                var valid = effect.Op == SkillProgramEffectOp.AllocateCurrentTurnDefaultStats
                    ? (trigger.Window is SkillProgramTriggerWindow.DrawPhaseStarting or SkillProgramTriggerWindow.PlayPhaseStarting or
                        SkillProgramTriggerWindow.DiscardPhaseStarting) && trigger.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive
                    : trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                        trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage;
                if (!valid) throw new InvalidOperationException($"Invalid turn-default stat trigger '{program.Id}/{trigger.Id}'.");
                // AfterDamageApplied has no configurable turnOwnerScope. Its
                // default Own metadata is unused by that native window; the
                // operation checks the actual turn owner before earning a boost.
            }
            foreach (var group in bindings.GroupBy(t => t.Effects[0].StateId, StringComparer.Ordinal))
                if (group.Count() != 4 || group.Count(t => t.Window == SkillProgramTriggerWindow.DrawPhaseStarting) != 1 ||
                    group.Count(t => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting) != 1 ||
                    group.Count(t => t.Window == SkillProgramTriggerWindow.DiscardPhaseStarting) != 1 ||
                    group.Count(t => t.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1)
                    throw new InvalidOperationException("Each turn-default stat pool requires exactly three phase allocations and one per-damage minimum increment.");
        }
    }
}
