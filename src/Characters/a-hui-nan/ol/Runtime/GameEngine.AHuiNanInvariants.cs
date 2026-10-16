namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidAHuiNan(ProgramSkillFrame f)
    {
        if(f.AHuiNanJueman is not{}r||!HasAHuiNanCapability||!AHuiNanStartedBinding(f)||r.Source!=AHuiNanSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=1||f.InstructionIndex!=1||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.PendingMovementContinuation is not null||!Enum.IsDefined(r.Stage)||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.DrawCount is<0 or>1||!r.History.SequenceEqual(AHuiNanHistory(r.ActualTurn)))return false;
        var own=r.History.Take(2).Count(e=>e.Declaration.ActorSeat==f.OwnerSeat);
        if(r.History.Count>3||r.History.Where((e,i)=>e.Declaration.Index!=i+1||e.Declaration.ActualTurn!=r.ActualTurn||e.Declaration.TurnOwner!=r.TurnOwner||!AHuiNanBasicKind(e.Kind)).Any()||ProgramEventHistory<AHuiNanJuemanStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var start]||start!=new AHuiNanJuemanStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.History.Count,own,r.History.Count==3?r.History[2].Kind:null))return false;
        var drawn=ProgramEventHistory<AHuiNanJuemanDrawnEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Stage==AHuiNanJuemanStage.DrawChildren)
        {if(r.History.Count<2||own!=1||drawn is not[var d]||d.OwnerSeat!=f.OwnerSeat||d.Count!=r.DrawCount||d.Before!=r.Before||d.After!=r.After||_cardMovements.Count(m=>m.Sequence>d.Before&&m.Sequence<=d.After&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==AHuiNanDrawReason)!=d.Count)return false;}
        else if(drawn.Length!=0||r.DrawCount!=0)return false;
        if(r.Stage is AHuiNanJuemanStage.UseChoice or AHuiNanJuemanStage.NativeUse or AHuiNanJuemanStage.NativeChildren)
        {if(r.History.Count!=3||own!=0)return false;}
        else if(r.Stage==AHuiNanJuemanStage.NoEffect&&(r.History.Count>=2&&own==1||r.History.Count==3&&own==0))return false;
        var issued=ProgramEventHistory<AHuiNanJuemanUseIssuedEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray();var returned=ProgramEventHistory<AHuiNanJuemanUseReturnedEvent>().Where(e=>e.Origin.ProgramFrameId==f.Id).ToArray();
        if(issued.Length>1||returned.Length>1||returned.Any(e=>issued.Count(i=>i.Origin==e.Origin)!=1))return false;
        if(r.Stage is AHuiNanJuemanStage.NativeUse or AHuiNanJuemanStage.NativeChildren)return r.Use is{}o&&issued is[var issue]&&issue.Origin==o&&o.ProgramFrameId==f.Id&&o.Source==r.Source&&o.GameplayHash==r.GameplayHash&&o.ActualTurn==r.ActualTurn&&o.ActorSeat==f.OwnerSeat&&o.CardKind==r.History[2].Kind&&(IsSlashCard(o.CardKind)||o.CardKind is CardKind.Peach or CardKind.Alcohol)&&returned.Length==(r.Stage==AHuiNanJuemanStage.NativeChildren?1:0);
        return r.Use is null&&issued.Length==0&&returned.Length==0;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27200)]private void AssertAHuiNan(ProgramSkillFrame f)
    {if(f.AHuiNanJueman is not null&&!ValidAHuiNan(f))throw new InvalidOperationException("Jueman lost its actual-turn first-three declarations, started source, paid draw or exact native return.");}
}
