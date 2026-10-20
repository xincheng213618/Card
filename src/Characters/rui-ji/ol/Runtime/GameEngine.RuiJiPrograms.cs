namespace CardGame.Core;

// 巧力 evidence: every committed option, draw, distribution and ending gain is a
// scalar fact; cold recovery rebuilds the turn counts from these facts alone.
public sealed record ProgramQiaoliArmorCommittedEvent(long FrameId, string SkillId, string ActivationId,
    int OwnerSeat, int TurnNumber) : IGameEvent;
public sealed record ProgramQiaoliWeaponDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int Range, int DrawnCount, int TurnNumber) : IGameEvent;
public sealed record ProgramQiaoliDistributionEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int CardId, int? RecipientSeat) : IGameEvent;
public sealed record ProgramQiaoliEndingGainEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int CardId, int TurnNumber) : IGameEvent;
public sealed record ProgramQingliangRevealEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ActorSeat, int CardCount) : IGameEvent;
public sealed record ProgramQingliangResolutionEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int ActorSeat, string Choice, int DiscardCount, bool Nullified) : IGameEvent;

// One prompt per drawn card; the distribution rides the owning trigger frame.
public sealed record RuiJiDistributionState(int OwnerSeat, IReadOnlyList<int> CardIds, int Cursor);
// 清靓 suspends between the option prompt and the suit prompt on the same frame.
public sealed record RuiJiQingliangState(long ParentUseFrameId, int ActorSeat, string Stage);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RuiJiDistributionState? RuiJiDistribution { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RuiJiQingliangState? RuiJiQingliang { get; init; }
}

public sealed partial class GameEngine
{
    internal const string QiaoliWeaponBinding = "qiaoli-weapon";
    internal const string QiaoliArmorBinding = "qiaoli-armor";
    private const string QingliangRevealBinding = "qingliang-reveal";



    [ProgramCapability]
    private sealed class RuiJiProgramCapability(GameEngine engine) : IRuiJiProgramHost
    {
        public SkillProgramStepOutcome QiaoliWeaponDuel(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramDuelLaunch(frame, unrespondable: false);
        public SkillProgramStepOutcome QiaoliArmorDuel(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramDuelLaunch(frame, unrespondable: true);
        public SkillProgramStepOutcome QiaoliWeaponDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramWeaponDamageDraw(frame, effect);
        public SkillProgramStepOutcome QiaoliEndingEquipmentGain(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QiaoliProgramEndingEquipmentGain(frame, effect);
        public SkillProgramStepOutcome QingliangChooseOption(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.QingliangProgramChooseOption(frame, effect);
    }
}
