namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IEnumerable<XingzuoEndingIssuedEvent> CurrentXingzuoDebts()=>!HasRuanYuCapability?[]:ProgramEventHistory<XingzuoEndingIssuedEvent>().Where(e=>e.ActualTurn==_turnNumber&&e.Source.OwnerSeat==_currentSeat&&_players[e.Source.OwnerSeat].IsAlive&&_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash==e.GameplayHash&&!ProgramEventHistory<XingzuoEndingConsumedEvent>().Any(c=>c.DebtId==e.DebtId));
    private void AppendRuanYuXingzuoEndingItems(List<TurnEndingBoundaryItem> items)
    {
        foreach(var debt in CurrentXingzuoDebts().OrderBy(e=>e.DebtId))
        {var t=_contentRegistry.GetSkill("ol:xingzuo").Program!.Triggers.Single(t=>t.Id=="issued-ending");var c=new ProgramTriggerCandidate(debt.Source.OwnerSeat,"ol:xingzuo",t.Id,debt.Source.SkillInstanceId,debt.GameplayHash,t.Priority,items.Count);items.Add(new(TurnEndingBoundaryItemKind.Program,t.Priority,"ruan-yu.xingzuo:"+debt.DebtId,c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));}
    }
    private XingzuoEndingIssuedEvent? CurrentRuanYuEndingDebt(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasRuanYuCapability||c.SkillId!="ol:xingzuo"||c.BindingId!="issued-ending"||context.Window!=SkillProgramTriggerWindow.TurnEnding||context.OwnerSeat!=c.OwnerSeat||c.OwnerSeat!=_currentSeat||context.SourceSeat!=_currentSeat||context.TargetSeat!=_currentSeat||context.OccurrenceIndex!=c.OccurrenceIndex||_resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(f=>f.Id==context.ParentFrameId)is not{}parent||parent.OwnerSeat!=c.OwnerSeat||parent.TurnNumber!=_turnNumber||parent.ItemIndex<0||parent.ItemIndex>=parent.Items.Count||parent.Items[parent.ItemIndex].Candidate!=c)return null;
        return ProgramEventHistory<XingzuoEndingIssuedEvent>().SingleOrDefault(e=>parent.Items[parent.ItemIndex].StableIdentity=="ruan-yu.xingzuo:"+e.DebtId&&e.Source.SkillId==c.SkillId&&e.Source.BindingId=="private-bottom"&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.GameplayHash==c.GameplayHash&&e.ActualTurn==_turnNumber&&ProgramEventHistory<RuanYuStartedEvent>().Count(s=>s.FrameId==e.DebtId&&s.Source==e.Source&&s.GameplayHash==e.GameplayHash&&s.Op==SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange&&s.ActualTurn==e.ActualTurn)==1);
    }
    private bool IsIssuedRuanYuXingzuoEndingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>_winner==Winner.None&&IsValidPlayerSeat(c.OwnerSeat)&&_players[c.OwnerSeat].IsAlive&&CurrentRuanYuEndingDebt(c,context)is{}debt&&(!ProgramEventHistory<XingzuoEndingConsumedEvent>().Any(e=>e.DebtId==debt.DebtId)||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.RuanYu?.DebtId==debt.DebtId&&f.WindowContext==context&&MountObserverCandidateMatches(f,c)));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27430)]
    private bool CanOfferRuanYu(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not[{ }e]||!RuanYuComposition.Handles(e.Op))return true;
        return e.Op switch
        {
            SkillProgramEffectOp.ResolveXingzuoPrivateBottomExchange=>context.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&c.OwnerSeat==_currentSeat&&HasRuntimeSkillInstance(_players[c.OwnerSeat],c.SkillId,c.SkillInstanceId),
            SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange=>IsIssuedRuanYuXingzuoEndingCandidate(c,context),
            SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw=>IsIssuedRuanYuMiaoxianRedCandidate(c,context),
            _=>false
        };
    }
}
