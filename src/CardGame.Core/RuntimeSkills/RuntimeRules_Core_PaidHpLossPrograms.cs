using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PaidHpLossPrograms
{
    internal static ProgramHpLossModifierOrigin CreatePaidHpLossModifierOrigin(ProgramSkillFrame f,ProgramPaidHpLossReceipt p)=>
        new(f.Id,p.InstructionIndex,p.ResultBind,p.OwnerSeat,p.SkillId,p.BindingId,p.SkillInstanceId,p.GameplayHash,p.UsageGroup,p.TurnNumber,p.TurnOwnerSeat,p.Requested,p.HpBefore,p.HpAfter,p.ActualLost);
}
