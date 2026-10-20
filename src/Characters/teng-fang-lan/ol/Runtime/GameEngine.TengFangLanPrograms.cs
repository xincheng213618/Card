namespace CardGame.Core;

// 落宠 evidence: one scalar record per accepted (option, target) invocation. The
// per-round ledgers (one use per option, one target per round) and the removed
// options are both derived from the committed ledger, so cold recovery replays
// the accepted commands into the same availability state. Drawn cards enter the
// target's hand, so only the count is recorded (hidden card ids stay out of the
// ordinary snapshot); the discarded card ids are public discard-pile movements.
public sealed record ProgramLuochongResolvedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RoundNumber, string OptionId, int TargetSeat,
    int RecoveredHp, int LostHp, IReadOnlyList<int> DiscardedCardIds, int DrawCount) : IGameEvent;

// 哀尘 evidence: the owner's permanent removal of one remaining 落宠 option at a
// dying entry; RemainingOptionCount is the count after this removal.
public sealed record ProgramAichenRemovedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, string RemovedOptionId, int RemainingOptionCount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LuochongPendingState? LuochongPending { get; init; }
}

// The suspension receipt of one accepted 落宠 invocation. A nonzero remaining
// discard count marks the target's own discard prompts; a zero count on op
// re-entry marks the resume after a losing-hp dying child.
public sealed record LuochongPendingState(string OptionId, int TargetSeat, int RemainingDiscards)
{
    private IReadOnlyList<int> _discardedCardIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> DiscardedCardIds
    { get => _discardedCardIds; init => _discardedCardIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial class GameEngine
{
    internal static readonly string[] LuochongOptionIds = ["recover", "loseHp", "discardTwo", "drawTwo"];


    [ProgramCapability]
    private sealed class TengFangLanProgramCapability(GameEngine engine) : ITengFangLanProgramHost
    {
        public SkillProgramStepOutcome LuochongResolve(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.LuochongProgramResolve(frame, effect);
        public SkillProgramStepOutcome AichenRemoveOption(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.AichenProgramRemoveOption(frame, effect);
    }
}
