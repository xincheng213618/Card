namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouHuangGaiRuntime MouHuangGaiRules=>GetRuntimeModule<MouHuangGaiRuntime>();
    private bool ValidMouHg(ProgramSkillFrame f)=>MouHuangGaiRules.Valid(f);
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount,35400)]
    private int MouHgDamageBonus(IDamageAttempt attack)=>MouHuangGaiRules.DamageBonus(attack.IsChainPropagation||attack is not CardAttackHandle?null:new MouHuangGaiFacts(this).Use(attack.ResolutionId));
    private bool MouHgLiejiParent(ProgramSkillFrame f)
    {
        if(f.SkillId!=MouHuangGaiRuntime.Lieji||f.TriggerId!="after-trick"||f.ActivationId!=f.TriggerId||f.InstructionIndex!=1||f.WindowContext is not{Window:SkillProgramTriggerWindow.CardUseCompleted,CardUse:{}info}w||w.OwnerSeat!=f.OwnerSeat)return false;
        var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(at<1||_resolutionStack[at-1]is not ProgramCardTriggerWindowFrame p||p.Id!=w.ParentFrameId||p.Action.ActionId!=info.CardActionId||p.Action.ActorSeat!=f.OwnerSeat||
            CardUseCategoryCatalog.Get(p.Action.EffectiveKind)is not(CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick)||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count)return false;
        var c=p.Candidates[p.CandidateIndex];return c.OwnerSeat==f.OwnerSeat&&c.SkillId==f.SkillId&&c.TriggerId==f.TriggerId&&c.SkillInstanceId==f.SkillInstanceId&&c.GameplayHash==f.GameplayHash;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,35400)]
    private bool MouHgMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.MouHuangGai is{Stage:MouHgStage.RevealChildren or MouHgStage.ObtainChildren or MouHgStage.NativeChildren or MouHgStage.DiscardSlashChildren}&&ValidMouHg(f)&&e?.Op==SkillProgramEffectOp.ResolveMouHuangGaiQuzhou&&p==f.PendingMovementContinuation&&p.BeforeCount==0&&p.CoverageResultBind is null;
}
