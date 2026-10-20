using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasFeiYiHezhong=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RevealSingletonAndArmRankEffects);
    private bool FeiYiDirectionAvailable(int owner,bool greater)=>!ProgramEventHistory<FeiYiHezhongPaidEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Turn==_turnNumber&&e.TurnOwner==_turnProgression.OwnerSeat&&e.Greater==greater);
}
