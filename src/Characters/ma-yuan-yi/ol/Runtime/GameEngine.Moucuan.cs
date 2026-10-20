using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int MoucuanFactionCount()=>_players.Select(GetEffectiveFactionId).OfType<string>().Distinct(StringComparer.Ordinal).Count();
    private bool HasPaidMoucuanSourceLoss(ProgramSkillFrame f)=>f.MaYuanYi is{Op:SkillProgramEffectOp.ResolveMoucuanAwakening,Stage:MaYuanYiStage.MaximumChildren}r&&f.HpConditionalSourceReceipt is not null&&
        !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)&&ProgramEventHistory<MoucuanMaximumPaidEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.MaximumBefore==r.MaximumBefore&&e.MaximumAfter==r.MaximumBefore-1&&e.HpAfter==Math.Min(r.HpBefore,r.MaximumBefore-1))==1;
}
