using System.Reflection;

namespace CardGame.Core;

// A completed trigger returns to exactly one owning window. Its handler keeps
// that window's cursor, receipt and phase checks; discovery never captures a game.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ProgramWindowReturnAttribute(params SkillProgramTriggerWindow[] windows) : Attribute
{
    internal IReadOnlyList<SkillProgramTriggerWindow> Windows { get; } = Array.AsReadOnly(windows);
}

internal sealed class ProgramWindowReturnCatalog
{
    internal sealed record Registration(SkillProgramTriggerWindow Window, string Method,
        Action<GameEngine, ProgramSkillFrame, bool> Return);

    private readonly IReadOnlyDictionary<SkillProgramTriggerWindow, Registration> _byWindow;
    internal IReadOnlyList<Registration> Registrations { get; }

    internal ProgramWindowReturnCatalog(IEnumerable<Registration> registrations)
    {
        var entries = registrations.OrderBy(entry => entry.Window).ToArray();
        if (entries.Length == 0 || entries.Any(entry => !Enum.IsDefined(entry.Window) ||
                string.IsNullOrWhiteSpace(entry.Method) || entry.Return is null))
            throw new InvalidOperationException("Program window returns require nonempty, typed registrations.");
        if (entries.GroupBy(entry => entry.Window).FirstOrDefault(group => group.Count() != 1) is { } duplicate)
            throw new InvalidOperationException($"Duplicate program window return '{duplicate.Key}'.");
        Registrations = Array.AsReadOnly(entries);
        _byWindow = new System.Collections.ObjectModel.ReadOnlyDictionary<SkillProgramTriggerWindow, Registration>(
            entries.ToDictionary(entry => entry.Window));
    }

    internal static ProgramWindowReturnCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        var entries = new List<Registration>();
        foreach (var method in methods)
        {
            if (method.GetCustomAttribute<ProgramWindowReturnAttribute>() is not { } registration) continue;
            if (method.DeclaringType != typeof(GameEngine) && !ProgramRuntimeModuleCatalog.IsDeclared(method.DeclaringType) ||
                method.DeclaringType == typeof(GameEngine) && method.IsStatic || method.IsGenericMethod ||
                method.ReturnType != typeof(void) || !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual([typeof(ProgramSkillFrame), typeof(bool)]) || registration.Windows.Count == 0)
                throw new InvalidOperationException($"Program window return '{method.Name}' has an invalid host signature.");
            var callback = method.DeclaringType == typeof(GameEngine)
                ? method.CreateDelegate<Action<GameEngine, ProgramSkillFrame, bool>>()
                : (Action<GameEngine, ProgramSkillFrame, bool>)((engine, frame, completed) =>
                    ((Action<ProgramSkillFrame, bool>)engine.BindRuntimeHandler(method, typeof(Action<ProgramSkillFrame, bool>)))(frame, completed));
            entries.AddRange(registration.Windows.Select(window => new Registration(window, method.Name, callback)));
        }
        return new(entries);
    }

    internal void Return(GameEngine engine, ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext ??
            throw new InvalidOperationException("A trigger program frame lost its window context.");
        if (!_byWindow.TryGetValue(context.Window, out var registration))
            throw new InvalidOperationException("Unsupported lifecycle program continuation.");
        registration.Return(engine, frame, completed);
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramWindowReturnCatalog> ProgramWindowReturns = new(() =>
        ProgramWindowReturnCatalog.Discover(RuntimeModuleMethods), true);
}
