using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouLuBuMobile => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouLuBuLiyu);
    private static CardConversionSource MouLbSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_bu_mou_mobile_MouLuBuMobile.MouLbSource(f);
    [ProgramCapability] private sealed class MouLuBuMobileCapability(GameEngine g) : IMouLuBuMobileHost
    {
        public SkillProgramStepOutcome Bonus(ProgramSkillFrame f) => g.ResolveMouLbBonus(f);
        public SkillProgramStepOutcome Liyu(ProgramSkillFrame f) => g.BeginMouLbLiyu(f);
    }
}
