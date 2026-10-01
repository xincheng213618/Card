using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace CardGame.Core;

internal enum ProgramInstructionSourceKind
{
    Activation,
    Trigger
}

internal readonly record struct ProgramInstructionIdentity(
    ProgramInstructionSourceKind SourceKind,
    string BindingId,
    int Index);

internal readonly record struct ResolvedProgramInstruction(
    ProgramInstructionIdentity Identity,
    SkillProgramEffect Effect);

/// <summary>
/// Immutable executable instructions compiled from one activation or trigger.
/// The plan contains definition data only and never caches match state.
/// </summary>
internal sealed class ProgramExecutionPlan
{
    private readonly ReadOnlyCollection<SkillProgramEffect> _instructions;

    internal ProgramExecutionPlan(
        string skillId,
        string gameplayHash,
        ProgramInstructionSourceKind sourceKind,
        string bindingId,
        IReadOnlyList<SkillProgramEffect> instructions,
        SkillProgramActivation? activation = null,
        SkillProgramTrigger? trigger = null,
        ProgramInstructionFeatures? features = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameplayHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentNullException.ThrowIfNull(instructions);
        SkillId = skillId;
        GameplayHash = gameplayHash;
        SourceKind = sourceKind;
        BindingId = bindingId;
        _instructions = Array.AsReadOnly(instructions.ToArray());
        Activation = activation;
        Trigger = trigger;
        Features = features ?? new ProgramInstructionFeatures(_instructions);
    }

    internal string SkillId { get; }
    internal string GameplayHash { get; }
    internal ProgramInstructionSourceKind SourceKind { get; }
    internal string BindingId { get; }
    internal IReadOnlyList<SkillProgramEffect> Instructions => _instructions;
    internal SkillProgramActivation? Activation { get; }
    internal SkillProgramTrigger? Trigger { get; }
    internal ProgramInstructionFeatures Features { get; }

    internal ResolvedProgramInstruction GetInstruction(int index)
    {
        if ((uint)index >= (uint)_instructions.Count)
            throw new InvalidOperationException(
                $"Program binding '{BindingId}' has no instruction at index {index}.");
        return new(new(SourceKind, BindingId, index), _instructions[index]);
    }

    internal ResolvedProgramInstruction GetPausedInstruction(int committedCursor)
    {
        if (committedCursor <= 0 || committedCursor > _instructions.Count)
            throw new InvalidOperationException(
                $"Program binding '{BindingId}' cannot resume committed cursor {committedCursor}.");
        return GetInstruction(committedCursor - 1);
    }
}

/// <summary>Resolves immutable skill definitions into reference-keyed execution plans.</summary>
internal sealed class ProgramInstructionResolver
{
    internal static ProgramInstructionResolver Default { get; } = new();

    private readonly ConditionalWeakTable<SkillProgram, ProgramPlans> _cache = new();
    private readonly ConditionalWeakTable<SkillProgramActivation, ProgramInstructionFeatures> _activationFeatures = new();
    private readonly ConditionalWeakTable<SkillProgramTrigger, ProgramInstructionFeatures> _triggerFeatures = new();

    internal ProgramInstructionFeatures Features(SkillProgramActivation activation) =>
        _activationFeatures.GetValue(activation, static source => new(source.Effects));

    internal ProgramInstructionFeatures Features(SkillProgramTrigger trigger) =>
        _triggerFeatures.GetValue(trigger, static source => new(source.Effects, source.Condition));

    internal ProgramExecutionPlan? Find(
        SkillProgram program, ProgramInstructionSourceKind sourceKind, string? bindingId)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (bindingId is null) return null;
        var plans = _cache.GetValue(program, BuildPlans);
        return sourceKind switch
        {
            ProgramInstructionSourceKind.Activation => plans.Activations.GetValueOrDefault(bindingId),
            ProgramInstructionSourceKind.Trigger => plans.Triggers.GetValueOrDefault(bindingId),
            _ => throw new InvalidOperationException($"Unsupported program instruction source '{sourceKind}'.")
        };
    }

    internal SkillProgramActivation? FindActivation(SkillProgram program, string? bindingId) =>
        Find(program, ProgramInstructionSourceKind.Activation, bindingId)?.Activation;

    internal SkillProgramTrigger? FindTrigger(SkillProgram program, string? bindingId) =>
        Find(program, ProgramInstructionSourceKind.Trigger, bindingId)?.Trigger;

    internal bool ProgramUsesTriggerValue(SkillProgram program, SkillProgramTriggerValueKind kind) =>
        _cache.GetValue(program, BuildPlans).TriggerValues.Contains(kind);

    internal ProgramExecutionPlan Resolve(
        SkillProgram program,
        ProgramInstructionSourceKind sourceKind,
        string bindingId)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        var plans = _cache.GetValue(program, BuildPlans);
        return sourceKind switch
        {
            ProgramInstructionSourceKind.Activation when plans.Activations.TryGetValue(bindingId, out var plan) => plan,
            ProgramInstructionSourceKind.Trigger when plans.Triggers.TryGetValue(bindingId, out var plan) => plan,
            ProgramInstructionSourceKind.Activation => throw Unknown(program, "activation", bindingId),
            ProgramInstructionSourceKind.Trigger => throw Unknown(program, "trigger", bindingId),
            _ => throw new InvalidOperationException($"Unsupported program instruction source '{sourceKind}'.")
        };
    }

    internal ProgramExecutionPlan Resolve(ProgramSkillFrame frame, SkillProgram program)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(program);
        if (!string.Equals(frame.SkillId, program.Id, StringComparison.Ordinal) ||
            !string.Equals(frame.GameplayHash, program.GameplayHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The program frame does not match its immutable skill definition.");
        return frame.TriggerId is not null
            ? Resolve(program, ProgramInstructionSourceKind.Trigger, frame.TriggerId)
            : Resolve(program, ProgramInstructionSourceKind.Activation, frame.ActivationId);
    }

    private ProgramPlans BuildPlans(SkillProgram program)
    {
        var activations = new Dictionary<string, ProgramExecutionPlan>(StringComparer.Ordinal);
        foreach (var activation in program.Activations)
            Add(activations, activation.Id, new(program.Id, program.GameplayHash,
                ProgramInstructionSourceKind.Activation, activation.Id, activation.Effects,
                activation: activation, features: Features(activation)), program.Id);

        var triggers = new Dictionary<string, ProgramExecutionPlan>(StringComparer.Ordinal);
        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in program.Triggers)
        {
            if (!triggerIds.Add(trigger.Id)) throw Duplicate(program.Id, trigger.Id);
            Add(triggers, trigger.Id, new(program.Id, program.GameplayHash,
                ProgramInstructionSourceKind.Trigger, trigger.Id, trigger.Effects,
                trigger: trigger, features: Features(trigger)), program.Id);
        }
        return new(activations.ToFrozenDictionary(StringComparer.Ordinal),
            triggers.ToFrozenDictionary(StringComparer.Ordinal),
            triggers.Values.SelectMany(plan => plan.Features.ValueKinds).ToFrozenSet());
    }

    private static void Add(
        IDictionary<string, ProgramExecutionPlan> plans,
        string bindingId,
        ProgramExecutionPlan plan,
        string skillId)
    {
        if (!plans.TryAdd(bindingId, plan)) throw Duplicate(skillId, bindingId);
    }

    private static InvalidOperationException Unknown(SkillProgram program, string kind, string bindingId) =>
        new($"Skill program '{program.Id}' has no {kind} binding '{bindingId}'.");

    private static InvalidOperationException Duplicate(string skillId, string bindingId) =>
        new($"Skill program '{skillId}' contains duplicate binding '{bindingId}'.");

    private sealed record ProgramPlans(
        IReadOnlyDictionary<string, ProgramExecutionPlan> Activations,
        IReadOnlyDictionary<string, ProgramExecutionPlan> Triggers,
        FrozenSet<SkillProgramTriggerValueKind> TriggerValues);
}
