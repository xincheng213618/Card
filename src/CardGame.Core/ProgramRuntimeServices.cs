namespace CardGame.Core;

// Trusted runtime primitives preserve the command session and the one frame
// store. Modules receive individual frames, never the mutable store itself.
internal interface IProgramRuntimeFrames : IProgramRuntimeOperations
{
    ResolutionFrame? Top { get; }
    ResolutionFrame? Find(long id);
    ResolutionFrame? ParentOf(long id);
    ResolutionFrame? ChildOf(long id);
    IReadOnlyList<T> OfType<T>() where T : ResolutionFrame;
    ProgramSkillFrame ActiveProgram(long id);
    long NextId();
    void ReplaceTop(ResolutionFrame frame);
    void Replace(long id, ResolutionFrame frame);
    void Push(ResolutionFrame frame);
    void Pop(long id, ResolutionFrameKind kind);
    void Advance(long id);
    void AdvanceTop<T>() where T : ResolutionFrame;
    void AdvanceProgram(long id);
}

internal interface IProgramRuntimeEvents : IProgramRuntimeOperations
{
    void AdvanceFact(IGameEvent fact);
}

internal interface IProgramRuntimePrompts : IProgramRuntimeOperations
{
    PendingDecision? Current { get; }
    PromptId NextId();
    void Clear();
    void Set(PendingDecision decision, bool isHuman);
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ProgramRuntimeFrameServices(GameEngine engine) : IProgramRuntimeFrames
    {
        public ResolutionFrame? Top => engine._resolutionStack.LastOrDefault();
        public ResolutionFrame? Find(long id) => engine._resolutionStack.FirstOrDefault(frame => frame.Id == id);
        public ResolutionFrame? ParentOf(long id)
        {
            var index = engine._resolutionStack.FindLastIndex(frame => frame.Id == id);
            return index > 0 ? engine._resolutionStack[index - 1] : null;
        }
        public ResolutionFrame? ChildOf(long id)
        {
            var index = engine._resolutionStack.FindLastIndex(frame => frame.Id == id);
            return index >= 0 && index + 1 < engine._resolutionStack.Count ? engine._resolutionStack[index + 1] : null;
        }
        public IReadOnlyList<T> OfType<T>() where T : ResolutionFrame =>
            Array.AsReadOnly(engine._resolutionStack.OfType<T>().ToArray());
        public ProgramSkillFrame ActiveProgram(long id) => engine.GetActiveProgramFrame(id);
        public long NextId() => ++engine._resolutionSequence;
        public void ReplaceTop(ResolutionFrame frame) => engine.ReplaceRuntimeTop(frame);
        public void Replace(long id, ResolutionFrame frame) => engine.ReplaceRuntimeFrame(id, frame);
        public void Push(ResolutionFrame frame) => engine.PushRuntimeFrame(frame);
        public void Pop(long id, ResolutionFrameKind kind) => engine.PopResolutionFrame(id, kind);
        public void Advance(long id) => engine.AdvanceRuntimeFrame(id);
        public void AdvanceTop<T>() where T : ResolutionFrame => engine.AdvanceRuntimeTop<T>();
        public void AdvanceProgram(long id) => engine.AdvanceRuntimeProgram(id);
    }

    [ProgramCapability]
    private sealed class ProgramRuntimeEventServices(GameEngine engine) : IProgramRuntimeEvents
    {
        public void AdvanceFact(IGameEvent fact) => engine.AdvanceEventRulesAndQueueFact(fact);
    }

    [ProgramCapability]
    private sealed class ProgramRuntimePromptServices(GameEngine engine) : IProgramRuntimePrompts
    {
        public PendingDecision? Current => engine._pendingDecision;
        public PromptId NextId() => engine.CreatePromptId();
        public void Clear() => engine.ClearPendingDecision();
        public void Set(PendingDecision decision, bool isHuman)
        {
            engine._pendingDecision = decision;
            engine._status = isHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        }
    }
}
