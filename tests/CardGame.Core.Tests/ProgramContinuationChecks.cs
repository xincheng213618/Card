using System.Reflection;
using CardGame.Core;
using Catalog = CardGame.Core.GameEngine.ProgramContinuationCatalog;

internal static class ProgramContinuationChecks
{
    public static void OrderedRegistrationPreservesReturnBoundaries()
    {
        var calls = new List<string>();
        var expectedId = 41L;
        var frame = new ProgramSkillFrame(expectedId, 0, "fixture:continuation", "run", "hash", 0, [], []);
        var catalog = new Catalog([
            new(ProgramContinuationKind.Resume, 30, "unreached", (_, _) => throw new Exception("Handled continuation fell through."), null),
            new(ProgramContinuationKind.Resume, 20, "accept", (_, id) => { Require(id == expectedId, "The owner identity changed."); calls.Add("accept"); return true; }, null),
            new(ProgramContinuationKind.MovementTail, 10, "tail", null, (_, current) => { Require(ReferenceEquals(frame, current), "Movement receipt was replaced."); calls.Add("tail"); return true; }),
            new(ProgramContinuationKind.Resume, 10, "decline", (_, _) => { calls.Add("decline"); return false; }, null),
            new(ProgramContinuationKind.Movement, 10, "movement", null, (_, current) => { Require(ReferenceEquals(frame, current), "Movement lost the original receipt."); calls.Add("movement"); return false; })
        ]);
        // The synthetic handlers deliberately do not access the game. This tests
        // dispatch ordering separately from the real paid-child scenarios below.
        Require(catalog.TryResume(null!, expectedId) && calls.SequenceEqual(["decline", "accept"]),
            "Discovery order must not affect dispatch, and acceptance must stop the chain.");
        calls.Clear();
        Require(!catalog.TryReturnMovement(null!, frame) && calls.SequenceEqual(["movement"]),
            "A declined movement must return to the caller's payment boundary, not enter its tail.");
        Require(catalog.TryReturnMovement(null!, frame, afterPayment: true) && calls.SequenceEqual(["movement", "tail"]),
            "The post-payment tail must run only when its caller explicitly reaches it.");

        var failure = new InvalidOperationException("original rule failure");
        var throwing = new Catalog([new(ProgramContinuationKind.Resume, 1, "throw", (_, _) => throw failure, null)]);
        try { throwing.TryResume(null!, expectedId); throw new Exception("A rule failure was swallowed."); }
        catch (InvalidOperationException caught) { Require(ReferenceEquals(caught, failure), "Reflection must not wrap or replace rule failures."); }

        Reject(() => new Catalog([catalog.Registrations[0], catalog.Registrations[0]]));
        Reject(() => new Catalog([new(ProgramContinuationKind.Resume, 1, "wrong-signature", null, (_, _) => true)]));
        Reject(() => new Catalog([new((ProgramContinuationKind)999, 1, "unknown-kind", (_, _) => true, null)]));
        Reject(() => new Catalog([]));

        var methods = typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
        var forward = Catalog.Discover(methods);
        var reversed = Catalog.Discover(methods.Reverse());
        Require(forward.Registrations.Select(item => (item.Kind, item.Order, item.Method))
                .SequenceEqual(reversed.Registrations.Select(item => (item.Kind, item.Order, item.Method))) &&
            Enum.GetValues<ProgramContinuationKind>().All(kind => forward.Registrations.Any(item => item.Kind == kind)),
            "Every continuation phase must bind and remain deterministic under reflection enumeration changes.");
        var registeredMethod = methods.First(method => method.GetCustomAttribute<ProgramContinuationAttribute>() is not null);
        Reject(() => Catalog.Discover([registeredMethod, registeredMethod]));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid continuation registration was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
