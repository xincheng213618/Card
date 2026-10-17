using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ProgramNativeDrawReturnAttribute(params SkillProgramEffectOp[] operations) : Attribute
{
    internal IReadOnlyList<SkillProgramEffectOp> Operations { get; } = Array.AsReadOnly(operations);
}

public sealed partial class GameEngine
{
    // Immutable discovery metadata only. Every paid cursor and result remains
    // on its concrete producer frame; these delegates never enter a checkpoint.
    private static readonly Lazy<IReadOnlyDictionary<SkillProgramEffectOp,
        Action<GameEngine, ProgramSkillFrame, NativeDrawOutcome>>> ProgramNativeDrawReturns = new(() =>
    {
        var returns = new Dictionary<SkillProgramEffectOp, Action<GameEngine, ProgramSkillFrame, NativeDrawOutcome>>();
        foreach (var method in typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<ProgramNativeDrawReturnAttribute>() is not { } registration) continue;
            if (registration.Operations.Count == 0 || method.IsGenericMethod || method.ReturnType != typeof(void) ||
                !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                    [typeof(ProgramSkillFrame), typeof(NativeDrawOutcome)]))
                throw new InvalidOperationException("A native program draw return has an invalid signature.");
            var callback = method.CreateDelegate<Action<GameEngine, ProgramSkillFrame, NativeDrawOutcome>>();
            foreach (var op in registration.Operations)
                if (!Enum.IsDefined(op) || !returns.TryAdd(op, callback))
                    throw new InvalidOperationException("A native program draw return has an unknown or duplicate operation.");
        }
        return new ReadOnlyDictionary<SkillProgramEffectOp, Action<GameEngine, ProgramSkillFrame, NativeDrawOutcome>>(returns);
    }, true);
}
