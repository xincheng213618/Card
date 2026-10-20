namespace CardGame.Core;

// 诗怨 evidence: one scalar per accepted draw option each turn; the per-option
// limit and the 余威 doubling are both derived from the committed ledger, so
// cold recovery replays the accepted commands into the same state.
public sealed record ProgramShiYuanTargetDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, int SourceSeat, int DrawCount, long CardActionId) : IGameEvent;
public sealed record ProgramYuWeiActiveTurnEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber) : IGameEvent;

// 毒逝 evidence: the dead owner's chosen heir for the transferred skill.
public sealed record ProgramDuShiGrantEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat) : IGameEvent;

public sealed partial class GameEngine
{

    [ProgramCapability]
    private sealed class LiuBianProgramCapability(GameEngine engine) : ILiuBianProgramHost
    {
        public SkillProgramStepOutcome ShiYuanTargetDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ShiYuanProgramTargetDraw(frame, effect);
        public SkillProgramStepOutcome DuShiGrantSkill(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.DuShiProgramGrantSkill(frame, effect);
        public SkillProgramStepOutcome YuWeiMarkActiveTurn(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YuWeiProgramMarkActiveTurn(frame, effect);
    }
}
