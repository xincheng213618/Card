using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouLuBuMobile => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouLuBuLiyu);
    private static CardConversionSource MouLbSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class MouLuBuMobileCapability(GameEngine g) : IMouLuBuMobileHost
    {
        public SkillProgramStepOutcome Bonus(ProgramSkillFrame f) => g.ResolveMouLbBonus(f);
        public SkillProgramStepOutcome Liyu(ProgramSkillFrame f) => g.BeginMouLbLiyu(f);
    }
}
