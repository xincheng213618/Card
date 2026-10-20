using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginBeiMiHuAwakening(ProgramSkillFrame f)
    {
        if(!ExactBeiMiHuLifecycleParent(f) || !CanOfferBeiMiHuLifecycle(f.OwnerSeat,GetProgramTrigger(f),f.WindowContext!))throw new InvalidOperationException("Baijia lost its own preparation or actual seven acquired cards.");
        var owner=_players[f.OwnerSeat];var r=NewBeiMiHuReceipt(f,"maximum-children") with{CumulativeDraw=BeiMiHuActualCards(f.OwnerSeat),MaximumBefore=owner.MaxHp,MaximumAfter=checked(owner.MaxHp+1)};
        ReplaceRuntimeTop(f=f with{BeiMiHu=r});ChangeProgramMaximumHp(f,1);
        AdvanceEventRulesAndQueueFact(new BeiMiHuAwakeningMaximumPaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.CumulativeDraw,r.MaximumBefore,r.MaximumAfter));AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
}
