namespace CardGame.Core;

// 诱言 evidence: one scalar record per triggered gain. The gained cards enter a
// private hand, so only the public suit mask and the gain count are recorded.
public sealed record ProgramYouyanGainEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int DiscardedSuitMask, int GainedCount) : IGameEvent;

// 追还 evidence: the armed choice itself stays secret, so the arm event carries
// only the owner and turn; the chosen seat lives in the runtime map that the
// accepted private command rebuilds during replay.
public sealed record ProgramZhuihuanArmedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber) : IGameEvent;
public sealed record ProgramZhuihuanResolvedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ChosenSeat, IReadOnlyList<long> ArmFrameIds,
    IReadOnlyList<int> DamagedSeats, IReadOnlyList<int> DiscardedSeats, int DiscardedCardCount) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ZhuihuanRetaliateState? ZhuihuanRetaliate { get; init; }
}

// One repeated preparation punishment: the marked character reflects two damage
// on every recorded damager whose health still exceeds the marked character's,
// and the remaining damagers each randomly discard up to two hand cards. The
// punishment evidence accumulates on the owning frame across damage children.
public sealed record ZhuihuanRetaliateState(int ChosenSeat, IReadOnlyList<int> PendingDamagers,
    int DiscardedCardCount = 0)
{
    private readonly IReadOnlyList<int> _pendingDamagers = Array.AsReadOnly(PendingDamagers.ToArray());
    private readonly IReadOnlyList<int> _damagedSeats = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _discardedSeats = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> PendingDamagers
    { get => _pendingDamagers; init => _pendingDamagers = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DamagedSeats
    { get => _damagedSeats; init => _damagedSeats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DiscardedSeats
    { get => _discardedSeats; init => _discardedSeats = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial class GameEngine
{
    // Private 追还 arm choices: arm frame id → secretly chosen seat. The map is
    // runtime trusted state; replay rebuilds it from the accepted choice
    // commands exactly like the 闭境 marked-card map.
    private readonly Dictionary<long, int> _zhuihuanArmChoices = [];

    private static int SuitMask(Suit suit) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_yang_wan_ol_YangWanPrograms.SuitMask(suit);

    [ProgramCapability]
    private sealed class YangWanProgramCapability(GameEngine engine) : IYangWanProgramHost
    {
        public SkillProgramStepOutcome YouyanGainSuitCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YouyanProgramGainSuitCards(frame, effect);
        public SkillProgramStepOutcome ZhuihuanArm(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhuihuanProgramArm(frame, effect);
        public SkillProgramStepOutcome ZhuihuanRetaliate(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhuihuanProgramRetaliate(frame, effect);
    }
}
