using System.Collections.ObjectModel;
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
/// Immutable executable instructions compiled from one activation or shared-executor trigger.
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
        IReadOnlyList<SkillProgramEffect> instructions)
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
    }

    internal string SkillId { get; }
    internal string GameplayHash { get; }
    internal ProgramInstructionSourceKind SourceKind { get; }
    internal string BindingId { get; }
    internal IReadOnlyList<SkillProgramEffect> Instructions => _instructions;

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
            ProgramInstructionSourceKind.Trigger when plans.LegacyTriggerIds.Contains(bindingId) =>
                throw new InvalidOperationException(
                    $"Trigger '{program.Id}/{bindingId}' does not use the shared program executor."),
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

    private static ProgramPlans BuildPlans(SkillProgram program)
    {
        var activations = new Dictionary<string, ProgramExecutionPlan>(StringComparer.Ordinal);
        foreach (var activation in program.Activations)
            Add(activations, activation.Id, new(program.Id, program.GameplayHash,
                ProgramInstructionSourceKind.Activation, activation.Id, activation.Effects), program.Id);

        var triggers = new Dictionary<string, ProgramExecutionPlan>(StringComparer.Ordinal);
        var legacy = new HashSet<string>(StringComparer.Ordinal);
        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in program.Triggers)
        {
            if (!triggerIds.Add(trigger.Id)) throw Duplicate(program.Id, trigger.Id);
            if (!trigger.UsesSharedExecutor)
            {
                legacy.Add(trigger.Id);
                continue;
            }
            Add(triggers, trigger.Id, new(program.Id, program.GameplayHash,
                ProgramInstructionSourceKind.Trigger, trigger.Id,
                trigger.Effects.Select(effect => effect.ToExecutionEffect()).ToArray()), program.Id);
        }
        return new(activations, triggers, legacy);
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
        IReadOnlySet<string> LegacyTriggerIds);
}
