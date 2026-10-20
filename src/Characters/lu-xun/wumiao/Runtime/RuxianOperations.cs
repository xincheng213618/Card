using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record WuLuXunRuxianActivatedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner) : IGameEvent;
internal sealed class ResolveWuLuXunRuxianDescriptor() : WuLuXunZhangcaiDescriptor(SkillProgramEffectOp.ResolveWuLuXunRuxian)
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
