namespace CardGame.Core;

public sealed record ProgramBijingMarkedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, IReadOnlyList<int> CardIds) : IGameEvent;
public sealed record ProgramBijingRecastEvent(long FrameId, string SkillId,
    int OwnerSeat, IReadOnlyList<int> DiscardedCardIds, int DrawnCount) : IGameEvent;
public sealed record ProgramBijingPunishEvent(long FrameId, string SkillId,
    int OwnerSeat, int TurnOwnerSeat, IReadOnlyList<int> LostCardIds, IReadOnlyList<int> DiscardedCardIds) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BijingPunishState? BijingPunish { get; init; }
}

// One prompt per discarded card; the receipt rides the owning program frame.
public sealed record BijingPunishState(int TurnOwnerSeat, int OwnerSeat,
    IReadOnlyList<int> LostCardIds, int Remaining, IReadOnlyList<int> DiscardedCardIds);

public sealed partial class GameEngine
{
    // 闭境 marks survive across turns until the owner's next preparation phase
    // recasts them; replay reconstructs the map from the accepted commands.
    private readonly Dictionary<int, int> _bijingMarkedCardIds = [];

    [ProgramCapability]
    private sealed class LvKaiProgramCapability(GameEngine engine) : ILvKaiProgramHost
    {
        public SkillProgramStepOutcome TunanUseRevealedCard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.TunanProgramUseRevealedCard(frame, effect);
        public SkillProgramStepOutcome BijingMarkHandCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BijingProgramMarkHandCards(frame, effect);
        public SkillProgramStepOutcome BijingRecastMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BijingProgramRecastMarkedCards(frame, effect);
        public SkillProgramStepOutcome BijingPunishDiscardPhase(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BijingProgramPunishDiscardPhase(frame, effect);
    }
}
