using System.Reflection;

namespace CardGame.Core;

internal enum ProgramContinuationKind { Resume, Movement, MovementTail }

// Register beside the implementation. Order is part of deterministic rule execution,
// not reflection discovery order. The handler retains its exact ownership checks.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ProgramContinuationAttribute(ProgramContinuationKind kind, int order) : Attribute
{
    internal ProgramContinuationKind Kind { get; } = kind;
    internal int Order { get; } = order;
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramContinuationCatalog> ProgramContinuations = new(() =>
        ProgramContinuationCatalog.Discover(typeof(GameEngine).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic)), true);

    internal sealed class ProgramContinuationCatalog
    {
        internal sealed record Registration(
            ProgramContinuationKind Kind, int Order, string Method,
            Func<GameEngine, long, bool>? Resume,
            Func<GameEngine, ProgramSkillFrame, bool>? Movement);

        private readonly Registration[] _resume;
        private readonly Registration[] _movement;
        private readonly Registration[] _movementTail;
        internal IReadOnlyList<Registration> Registrations { get; }

        internal ProgramContinuationCatalog(IEnumerable<Registration> registrations)
        {
            var entries = registrations.OrderBy(item => item.Kind).ThenBy(item => item.Order).ToArray();
            if (entries.Length == 0 || entries.Any(item => !Enum.IsDefined(item.Kind) || item.Order < 0 ||
                    string.IsNullOrWhiteSpace(item.Method) ||
                    (item.Kind == ProgramContinuationKind.Resume
                        ? item.Resume is null || item.Movement is not null
                        : item.Movement is null || item.Resume is not null)))
                throw new InvalidOperationException("Program continuations require nonempty, ordered, typed registrations.");
            if (entries.GroupBy(item => (item.Kind, item.Order)).FirstOrDefault(group => group.Count() != 1) is { } duplicate)
                throw new InvalidOperationException($"Duplicate program continuation {duplicate.Key.Kind} order {duplicate.Key.Order}: " +
                    string.Join(", ", duplicate.Select(item => item.Method)) + ".");
            Registrations = Array.AsReadOnly(entries);
            _resume = entries.Where(item => item.Kind == ProgramContinuationKind.Resume).ToArray();
            _movement = entries.Where(item => item.Kind == ProgramContinuationKind.Movement).ToArray();
            _movementTail = entries.Where(item => item.Kind == ProgramContinuationKind.MovementTail).ToArray();
        }

        internal static ProgramContinuationCatalog Discover(IEnumerable<MethodInfo> methods)
        {
            var entries = new List<Registration>();
            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<ProgramContinuationAttribute>() is not { } registration) continue;
                var parameters = method.GetParameters();
                if (method.DeclaringType != typeof(GameEngine) || method.IsStatic || method.IsGenericMethod ||
                    method.ReturnType != typeof(bool) || parameters.Length != 1 ||
                    parameters[0].ParameterType != typeof(ProgramSkillFrame) &&
                    (registration.Kind != ProgramContinuationKind.Resume || parameters[0].ParameterType != typeof(long)))
                    throw new InvalidOperationException($"Program continuation '{method.Name}' has an invalid host signature.");

                Func<GameEngine, long, bool>? resume = null;
                Func<GameEngine, ProgramSkillFrame, bool>? movement = null;
                if (parameters[0].ParameterType == typeof(long))
                    resume = method.CreateDelegate<Func<GameEngine, long, bool>>();
                else
                {
                    var invoke = method.CreateDelegate<Func<GameEngine, ProgramSkillFrame, bool>>();
                    if (registration.Kind == ProgramContinuationKind.Resume)
                        resume = (engine, frameId) => invoke(engine, engine.GetActiveProgramFrame(frameId));
                    else movement = invoke;
                }
                entries.Add(new(registration.Kind, registration.Order, method.Name, resume, movement));
            }
            return new(entries);
        }

        internal bool TryResume(GameEngine engine, long frameId)
        {
            // A declined handler may replace the frame. Do not cache a frame here:
            // each handler resolves its current owner just as the executor does.
            foreach (var entry in _resume)
                if (entry.Resume!(engine, frameId)) return true;
            return false;
        }

        internal bool TryReturnMovement(GameEngine engine, ProgramSkillFrame frame, bool afterPayment = false)
        {
            // Movement callbacks share the exact child-return receipt captured by
            // their caller; do not manufacture a new frame or repay its cost.
            foreach (var entry in afterPayment ? _movementTail : _movement)
                if (entry.Movement!(engine, frame)) return true;
            return false;
        }
    }
}
