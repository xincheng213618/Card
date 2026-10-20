namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string JiyunInitialReason = "skill-program.li-feng.jiyun.initial", JiyunDrawReason = "skill-program.li-feng.jiyun.draw", JiyunCollectReason = "skill-program.li-feng.jiyun.collect", ShuliangGiftReason = "skill-program.li-feng.shuliang.gift";
    private bool HasLiFengCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeJiyunCategoryGrain) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.CollectJiyunCompletedUseEntities) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DistributeShuliangEndingGrain);
    private static CardConversionSource LiFengSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_li_feng_ol_LiFengLedger.LiFengSource(f);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27591)]
    private bool CanOfferLiFeng(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect] || !LiFengComposition.Handles(effect.Op)) return true;
        if (!_players[candidate.OwnerSeat].IsAlive) return false;
        if (effect.Op == SkillProgramEffectOp.InitializeJiyunCategoryGrain) return !ProgramEventHistory<LiFengStartedEvent>().Any(e => e.Op == effect.Op && e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillInstanceId == candidate.SkillInstanceId);
        if (effect.Op == SkillProgramEffectOp.DistributeShuliangEndingGrain) return ReferencedPublicPileSources(candidate.OwnerSeat, "ol:jiyun", candidate.SkillInstanceId).Any(p => IsLiveJiyunPile(p) && PublicPileCards(p).Count > 0);
        return effect.Op != SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases || context.SourceSeat == candidate.OwnerSeat;
    }
}
