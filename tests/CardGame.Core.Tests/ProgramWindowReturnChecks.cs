using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using ReturnCatalog = CardGame.Core.ProgramWindowReturnCatalog;

internal static class ProgramWindowReturnChecks
{
    private const string Skill = "fixture:window-return";

    public static void RegistrationAndTypedParentGuardsPreserveBoundaries()
    {
        CheckReturnRegistration();
        CheckTypedParentGuards();
    }

    private static void CheckReturnRegistration()
    {
        var frame = new ProgramSkillFrame(41, 0, Skill, "run", "hash", 0, [], [])
        { WindowContext = new(SkillProgramTriggerWindow.GameStarting, 17, 0) };
        var calls = new List<(ProgramSkillFrame Frame, bool Completed)>();
        var catalog = new ReturnCatalog([
            new(SkillProgramTriggerWindow.AfterNormalDraw, "draw", (_, current, completed) => calls.Add((current, completed))),
            new(SkillProgramTriggerWindow.GameStarting, "start", (_, current, completed) => calls.Add((current, completed)))
        ]);
        catalog.Return(null!, frame, false);
        var next = frame with { Id = 42, WindowContext = new(SkillProgramTriggerWindow.AfterNormalDraw, 18, 0) };
        catalog.Return(null!, next, true);
        Require(calls.Count == 2 && ReferenceEquals(calls[0].Frame, frame) && !calls[0].Completed &&
                ReferenceEquals(calls[1].Frame, next) && calls[1].Completed,
            "Typed return dispatch must receive the current frame and original completion outcome without capturing a prior invocation.");
        RequireThrows(() => catalog.Return(null!, frame with { WindowContext = null }, true));
        RequireThrows(() => catalog.Return(null!, frame with
        { WindowContext = new(SkillProgramTriggerWindow.TurnEnding, 17, 0) }, true));
        Require(calls.Count == 2, "Unsupported or context-free returns must not dispatch any handler.");
        var marker = new InvalidOperationException("original return failure");
        var failing = new ReturnCatalog([new(SkillProgramTriggerWindow.GameStarting, "failure", (_, _, _) => throw marker)]);
        try { failing.Return(null!, frame, true); throw new Exception("A return failure was swallowed."); }
        catch (InvalidOperationException error) { Require(ReferenceEquals(error, marker), "Return failures must propagate without reflection wrapping."); }
        RequireThrows(() => new ReturnCatalog([]));
        RequireThrows(() => new ReturnCatalog([catalog.Registrations[0], catalog.Registrations[0]]));
        RequireThrows(() => new ReturnCatalog([new((SkillProgramTriggerWindow)int.MaxValue, "invalid", (_, _, _) => { })]));
        RequireThrows(() => new ReturnCatalog([new(SkillProgramTriggerWindow.GameStarting, " ", (_, _, _) => { })]));
        RequireThrows(() => new ReturnCatalog([new(SkillProgramTriggerWindow.GameStarting, "missing", null!)]));
        try { ((IList<ReturnCatalog.Registration>)catalog.Registrations)[0] = catalog.Registrations[0]; throw new Exception("Return metadata was mutable."); }
        catch (NotSupportedException) { }
        var methods = typeof(GameEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
        var forward = ReturnCatalog.Discover(methods);
        var reverse = ReturnCatalog.Discover(methods.Reverse());
        Require(forward.Registrations.Select(item => (item.Window, item.Method))
                .SequenceEqual(reverse.Registrations.Select(item => (item.Window, item.Method))),
            "Every production return declaration must bind independently of reflection discovery order.");
        var declared = methods.First(method => method.GetCustomAttribute<ProgramWindowReturnAttribute>() is not null);
        RequireThrows(() => ReturnCatalog.Discover([declared, declared]));
        RequireThrows(() => ReturnCatalog.Discover([typeof(ProgramWindowReturnChecks)
            .GetMethod(nameof(InvalidReturnHost), BindingFlags.Static | BindingFlags.NonPublic)!]));
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.GameStarting)]
    private static void InvalidReturnHost(ProgramSkillFrame frame, bool completed) { }

    private static void CheckTypedParentGuards()
    {
        var game = GameEngine.CreateStandard(new GameOptions
        { Seed = 7, PlayerCount = 5, ModeId = "identity:standard-5", AdvanceAfterHumanCommands = false },
            CardGame.Content.Standard.StandardContentRegistry.CreateWithClassicGenerals());
        var catalog = ReturnCatalog.Discover(typeof(GameEngine).GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
        var frame = new ProgramSkillFrame(41, 0, Skill, "run", "hash", 0, [], []);
        var before = JsonSerializer.Serialize(game.ResolutionStack);
        foreach (var window in new[]
        {
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramTriggerWindow.RoundStarting,
            SkillProgramTriggerWindow.AfterNormalDraw, SkillProgramTriggerWindow.DrawPhaseStarting,
            SkillProgramTriggerWindow.PlayEnding, SkillProgramTriggerWindow.AfterHpLost,
            SkillProgramTriggerWindow.CardsMoved, SkillProgramTriggerWindow.CharacterDied,
            SkillProgramTriggerWindow.TurnEnding, SkillProgramTriggerWindow.PlayPhaseStarting
        })
        {
            RequireThrows(() => catalog.Return(game, frame with { WindowContext = new(window, 17, 0) }, true));
            Require(JsonSerializer.Serialize(game.ResolutionStack) == before && game.Events.Count == 0 && game.Revision == 0,
                "An orphaned typed return must fail before any frame, committed fact or revision changes.");
        }
        var stack = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var parent = new ProgramLifecycleTriggerWindowFrame(17, 0, SkillProgramTriggerWindow.AfterNormalDraw, [],
            ProgramLifecycleContinuation.CompleteDrawPhase, new SkillProgramTriggerFacts(0, 4, true));
        stack.Push(parent);
        var afterDraw = frame with { WindowContext = new(SkillProgramTriggerWindow.AfterNormalDraw, 18, 0) };
        RequireThrows(() => catalog.Return(game, afterDraw, true));
        RequireThrows(() => catalog.Return(game, afterDraw with
        { WindowContext = afterDraw.WindowContext! with { ParentFrameId = parent.Id } }, true));
        Require(ReferenceEquals(stack.Last(), parent) && parent.CandidateIndex == 0,
            "A matching frame kind cannot substitute either a foreign parent id or a different lifecycle continuation.");
        stack.CompleteTop(parent.Id, parent.Kind);
    }

    private static void RequireThrows(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An invalid trigger-window return was accepted.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
