using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27140)]
    private bool QualifyGuying(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>
        t.Effects is[{Op:SkillProgramEffectOp.ResolveGuyingAtomicLossChoice}]?XiangLoss(c.OwnerSeat,context)is not null:
        t.Effects is not[{Op:SkillProgramEffectOp.SettleGuyingPreparationDebt}]||context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&c.OwnerSeat==_currentSeat&&XiangDebtCount(c.OwnerSeat)>0;
}
