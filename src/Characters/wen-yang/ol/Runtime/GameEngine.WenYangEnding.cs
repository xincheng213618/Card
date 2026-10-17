namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool WenYangDebtConsumed(long id)=>ProgramEventHistory<WenYangQingjiaoDebtConsumedEvent>().Any(e=>e.DebtId==id);
    private bool ValidWenYangDebt(WenYangQingjiaoDebtIssuedEvent debt)=>debt.Source.SkillId=="ol:qingjiao"&&IsValidPlayerSeat(debt.Source.OwnerSeat)&&debt.TurnOwner==debt.Source.OwnerSeat&&debt.ActualTurn>0&&debt.Gained is>=0 and<=8&&
        _contentRegistry.GetSkill(debt.Source.SkillId).Program?.GameplayHash==debt.GameplayHash&&ProgramEventHistory<WenYangQingjiaoDebtIssuedEvent>().Count(e=>e.FrameId==debt.FrameId)==1&&
        ProgramEventHistory<WenYangStartedEvent>().Count(e=>e.FrameId==debt.FrameId&&e.Source==debt.Source&&e.GameplayHash==debt.GameplayHash&&e.ActualTurn==debt.ActualTurn&&e.TurnOwner==debt.TurnOwner&&e.Op==SkillProgramEffectOp.ResolveQingjiaoWholeHandExchange)==1&&
        ProgramEventHistory<WenYangMovementIssuedEvent>().Count(e=>e.FrameId==debt.FrameId&&e.Step==1&&e.Stage==WenYangStage.HandDiscardChildren)==1;
    private void AppendWenYangEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if(!HasWenYangCapability)return;
        foreach(var debt in ProgramEventHistory<WenYangQingjiaoDebtIssuedEvent>().Where(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_currentSeat&&!WenYangDebtConsumed(e.FrameId)).ToArray())
        {
            if(!ValidWenYangDebt(debt)||!_players[debt.Source.OwnerSeat].IsAlive)continue;
            var t=_contentRegistry.GetSkill(debt.Source.SkillId).Program!.Triggers.Single(t=>t.Effects is[{Op:SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard}]);
            var c=new ProgramTriggerCandidate(debt.Source.OwnerSeat,debt.Source.SkillId,t.Id,debt.Source.SkillInstanceId,debt.GameplayHash,t.Priority,items.Count);
            items.Add(new(TurnEndingBoundaryItemKind.Program,t.Priority,$"wen-yang-ending:{debt.FrameId}",c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));
        }
    }
    private WenYangQingjiaoDebtIssuedEvent? CurrentWenYangDebt(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasWenYangCapability||c.SkillId!="ol:qingjiao"||c.BindingId!="issued-actual-ending"||context.Window!=SkillProgramTriggerWindow.TurnEnding||context.OwnerSeat!=c.OwnerSeat||context.SourceSeat!=_currentSeat||context.TargetSeat!=_currentSeat||c.OwnerSeat!=_currentSeat||context.OccurrenceIndex!=c.OccurrenceIndex||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}parent||parent.OwnerSeat!=_currentSeat||parent.TurnNumber!=_turnNumber||parent.ItemIndex<0||parent.ItemIndex>=parent.Items.Count||parent.Items[parent.ItemIndex].Candidate!=c)return null;
        var debt=ProgramEventHistory<WenYangQingjiaoDebtIssuedEvent>().SingleOrDefault(e=>parent.Items[parent.ItemIndex].StableIdentity==$"wen-yang-ending:{e.FrameId}");
        return debt is not null&&ValidWenYangDebt(debt)&&debt.Source.OwnerSeat==c.OwnerSeat&&debt.Source.SkillId==c.SkillId&&debt.Source.SkillInstanceId==c.SkillInstanceId&&debt.GameplayHash==c.GameplayHash&&debt.ActualTurn==_turnNumber&&debt.TurnOwner==_currentSeat?debt:null;
    }
    private bool IsIssuedWenYangQingjiaoEndingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>_winner==Winner.None&&IsValidPlayerSeat(c.OwnerSeat)&&_players[c.OwnerSeat].IsAlive&&CurrentWenYangDebt(c,context)is{}debt&&
        (!WenYangDebtConsumed(debt.FrameId)||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.WenYang is{Op:SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard}r&&r.DebtId==debt.FrameId&&MountObserverCandidateMatches(f,c)&&f.WindowContext==context));
}
