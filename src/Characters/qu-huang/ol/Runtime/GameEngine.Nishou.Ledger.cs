namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool NishouSwapAvailable(int owner,ActualDiscardRecoveryPhaseKey? phase)=>phase is{}p&&IsRecordedActualDiscardRecoveryPhase(p)&&p==CurrentActualDiscardRecoveryPhase()&&!ProgramEventHistory<ActualPhaseEndedEvent>().Any(e=>e.Phase==p)&&!ProgramEventHistory<NishouSwapDebtIssuedEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Phase.TurnNumber==p.TurnNumber);
    private IReadOnlyList<ActualPhaseEndItem> CollectNishouPhaseEndCandidates(ActualDiscardRecoveryPhaseKey phase)
    {
        if(!HasQuHuangCapability)return [];
        return Array.AsReadOnly(ProgramEventHistory<NishouSwapDebtIssuedEvent>().Where(e=>e.Phase==phase&&_players[e.Source.OwnerSeat].IsAlive&&!ProgramEventHistory<NishouSwapDebtConsumedEvent>().Any(d=>d.DebtId==e.DebtId)).OrderBy(e=>e.DebtId).Select(e=>{var t=_contentRegistry.GetSkill(e.Source.SkillId).Program!.Triggers.Single(t=>t.Id=="issued-current-phase-swap");return new ActualPhaseEndItem(new(e.Source.OwnerSeat,e.Source.SkillId,t.Id,e.Source.SkillInstanceId,e.GameplayHash,t.Priority),e.DebtId);}).ToArray());
    }
    private ActualPhaseEndFrame AppendNishouLatePhaseItems(ActualPhaseEndFrame frame)
    {if(!HasQuHuangCapability||!frame.ItemsCaptured)return frame;var extra=CollectNishouPhaseEndCandidates(frame.Phase).Where(i=>!frame.Items.Any(old=>old.EntitlementId==i.EntitlementId)).ToArray();return extra.Length==0?frame:frame with{Items=Array.AsReadOnly(frame.Items.Concat(extra).ToArray())};}
    private bool IsIssuedNishouCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(c.SkillId!="ol:nishou"||c.BindingId!="issued-current-phase-swap"||!_players[c.OwnerSeat].IsAlive||!IsExactActualPhaseEndCandidate(c,context)||context.ActualPhaseEnd is not{}due)return false;
        return ProgramEventHistory<NishouSwapDebtIssuedEvent>().Any(e=>e.DebtId==due.EntitlementId&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.GameplayHash==c.GameplayHash&&e.Phase==due.Phase&&!ProgramEventHistory<NishouSwapDebtConsumedEvent>().Any(x=>x.DebtId==e.DebtId));
    }
}
