namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string QingyaDiscardReason="skill-program.ya-dan.qingya.discard";
    private bool HasYaDanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards);
    private static bool IsYaDanOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards or SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse;
    private static CardConversionSource YaDanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
}

