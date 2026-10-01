using System.Reflection;
using System.Text.Json;
using CardGame.Core;

internal static class RuntimeFrameStoreChecks
{
    public static void VerifyBoundaries(GameEngine game)
    {
        var frames = new FrameStore();
        var owner = new CardUseFrame(1, 0, 17, CardKind.Slash, [1]);
        var child = new ResponseWindowFrame(2, 1, 0, 1, CardKind.Slash,
            RequiredCardKind: CardKind.Dodge);
        frames.Push(owner);
        frames.Push(child);
        MustReject(() => frames.Push(owner));
        MustReject(() => frames.Replace(owner with { Id = 99 }));
        MustReject(() => frames.Replace(child with { Kind = ResolutionFrameKind.Damage }));
        MustReject(() => frames.CompleteTop(owner.Id, owner.Kind));
        if (frames.Count != 2 || !ReferenceEquals(frames[0], owner) ||
            !ReferenceEquals(frames[1], child))
            throw new InvalidOperationException("Rejected transitions changed the frame stack.");
        frames.CompleteTop(child.Id, child.Kind);
        frames.CompleteTop(owner.Id, owner.Kind);
        if (frames.Count != 0) throw new InvalidOperationException("Child-first cleanup did not finish.");

        // The real response fixture also verifies that a stale parent write is
        // rejected before mutation, and a synchronous retired-program return is
        // harmless. It then continues through the normal public command path.
        var before = JsonSerializer.Serialize(game.ResolutionStack);
        var parent = game.ResolutionStack.OfType<CardUseFrame>().First();
        if (game.ResolutionStack[^1].Id == parent.Id)
            throw new InvalidOperationException("The boundary fixture requires an active response child.");
        MustReject(() => Invoke(game, "ReplaceRuntimeTop", parent with { CausedDamage = true }));
        Invoke(game, "AdvanceRuntimeProgram", long.MaxValue);
        if (JsonSerializer.Serialize(game.ResolutionStack) != before)
            throw new InvalidOperationException("A stale parent write or retired continuation changed live frames.");
    }

    private static void Invoke(GameEngine game, string method, object value)
    {
        try
        {
            typeof(GameEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(game, [value]);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static void MustReject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid runtime transition was accepted.");
    }
}
