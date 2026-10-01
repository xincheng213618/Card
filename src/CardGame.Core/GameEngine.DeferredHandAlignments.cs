using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum DeferredHandAlignmentDueKind { SourceCurrentTurnEnd=1300, TargetNextActualTurnEnd=1301 }
public sealed record DeferredHandAlignment(long Id,CardConversionSource Source,int TargetSeat,int CreatedTurn,DeferredHandAlignmentDueKind DueKind,string ContinuationId);
public sealed record DeferredHandAlignmentScheduledEvent(DeferredHandAlignment Alignment):IGameEvent;
public sealed record DeferredHandAlignmentConsumedEvent(DeferredHandAlignment Alignment,bool Applied):IGameEvent;
public sealed record DeferredHandAlignmentCancelledEvent(long Id):IGameEvent;
public sealed record DeferredHandAlignmentResolvedEvent(long Id,int OwnerHandCount,int TargetHandCount,int DrawCount,int DiscardCount):IGameEvent;
public sealed record DeferredHandAlignmentResolution(long DueId,int TargetSeat,int OwnerHandCount,int TargetHandCount,int DrawCount,int DiscardCount);
public sealed record DeferredTurnEndFrame(long Id,int OwnerSeat,int TurnNumber,bool Skipped,IReadOnlyList<long> DueIds,int ItemIndex=0,DeferredHandAlignment? Current=null):ResolutionFrame(Id,ResolutionFrameKind.DeferredTurnEnd,ResolutionFrameStep.ResolvingEffect);
public sealed partial class GameEngine
{
    private readonly List<DeferredHandAlignment> _deferredHandAlignments=[];
    private long _deferredHandAlignmentSequence;
    private sealed partial class ProgramSkillHost:IDeferredAlignmentProgramHost
    {
        public void ScheduleDeferredHandAlignment(ProgramSkillFrame frame,string continuationId)=>engine.ScheduleDeferredHandAlignment(frame,continuationId);
        public SkillProgramStepOutcome ResolveDeferredHandAlignment(ProgramSkillFrame frame)=>engine.ResolveDeferredHandAlignment(frame);
    }
    private void CleanupDeferredHandAlignments()
    {
        foreach(var due in _deferredHandAlignments.Where(d=>_winner!=Winner.None||!_players[d.Source.OwnerSeat].IsAlive||!_players[d.TargetSeat].IsAlive||!_players[d.Source.OwnerSeat].SkillGrants.Grants.Any(g=>g.SkillId==d.Source.SkillId&&g.SkillInstanceId==d.Source.SkillInstanceId)).ToArray())
        { _deferredHandAlignments.Remove(due);AdvanceEventRulesAndQueueFact(new DeferredHandAlignmentCancelledEvent(due.Id)); }
    }
    private void ScheduleDeferredHandAlignment(ProgramSkillFrame frame,string continuationId)
    {
        var active=GetActiveProgramFrame(frame.Id);
        if(active.SelectedTargetSeats is not [var target]||target==frame.OwnerSeat||!_players[target].IsAlive||frame.WindowContext is not {Window:SkillProgramTriggerWindow.TurnEnding} context||_resolutionStack.Count<2||_resolutionStack[^2] is not TurnEndingBoundaryFrame parent||parent.Id!=context.ParentFrameId||parent.OwnerSeat!=frame.OwnerSeat||parent.TurnNumber!=_turnNumber)
            throw new InvalidOperationException("Deferred alignment requires the actual owner's ending parent and one other living target.");
        var continuation=ProgramInstructionResolver.Default.Resolve(_contentRegistry.GetSkill(frame.SkillId).Program!,ProgramInstructionSourceKind.Trigger,continuationId).Trigger!;
        var instruction=ProgramInstructionResolver.Default.Resolve(active,_contentRegistry.GetSkill(frame.SkillId).Program!).GetInstruction(active.InstructionIndex-1).Effect;
        if(instruction is not {Op:SkillProgramEffectOp.ScheduleDeferredHandAlignment}||instruction.StateId!=continuationId||context.SourceSeat!=parent.OwnerSeat||context.TargetSeat!=parent.OwnerSeat)
            throw new InvalidOperationException("Deferred scheduling requires its exact currently executing instruction and ending context.");
        var candidate=parent.Items.ElementAtOrDefault(parent.ItemIndex)?.Candidate;
        if(candidate is null||candidate.OwnerSeat!=frame.OwnerSeat||candidate.SkillId!=frame.SkillId||candidate.SkillInstanceId!=frame.SkillInstanceId||candidate.BindingId!=frame.TriggerId||context.OwnerSeat!=frame.OwnerSeat||!HasRuntimeSkillInstance(_players[frame.OwnerSeat],frame.SkillId,frame.SkillInstanceId!))
            throw new InvalidOperationException("Deferred alignment requires the exact frozen ending candidate and live source grant.");
        if(!continuation.DeferredTurnEndOnly)throw new InvalidOperationException("Invalid deferred continuation binding.");
        var source=new CardConversionSource(frame.SkillId,frame.TriggerId!,frame.OwnerSeat,frame.SkillInstanceId);
        foreach(var kind in new[]{DeferredHandAlignmentDueKind.SourceCurrentTurnEnd,DeferredHandAlignmentDueKind.TargetNextActualTurnEnd})
        {var due=new DeferredHandAlignment(++_deferredHandAlignmentSequence,source,target,_turnNumber,kind,continuationId);_deferredHandAlignments.Add(due);AdvanceEventRulesAndQueueFact(new DeferredHandAlignmentScheduledEvent(due));}
    }
    private bool TryBeginDeferredTurnEnd(CharacterState previous,bool skipped)
    {
        CleanupDeferredHandAlignments();
        var dueIds=_deferredHandAlignments.Where(d=>d.DueKind==DeferredHandAlignmentDueKind.SourceCurrentTurnEnd?d.Source.OwnerSeat==previous.Seat&&d.CreatedTurn==_turnNumber:d.TargetSeat==previous.Seat&&d.CreatedTurn<_turnNumber).OrderBy(d=>d.Id).Select(d=>d.Id).ToArray();
        if(dueIds.Length==0)return false;
        if(_resolutionStack.Count!=0||_pendingDecision is not null||_phase!=TurnPhase.Finished)throw new InvalidOperationException("Actual end alignment requires a clean finalized turn boundary.");
        PushRuntimeFrame(new DeferredTurnEndFrame(++_resolutionSequence,previous.Seat,_turnNumber,skipped,Array.AsReadOnly(dueIds)));ContinueDeferredTurnEnd();return true;
    }
    private void ContinueDeferredTurnEnd() => AdvanceRuntimeTop<DeferredTurnEndFrame>();

    private void ContinueDeferredTurnEndCore()
    {
        while(_resolutionStack.LastOrDefault() is DeferredTurnEndFrame parent)
        {
            CleanupDeferredHandAlignments();
            if(parent.Current is not null)throw new InvalidOperationException("A deferred cursor retained its already finished child.");
            if(_winner!=Winner.None){PopResolutionFrame(parent.Id,ResolutionFrameKind.DeferredTurnEnd);CleanupDeferredHandAlignments();return;}
            if(parent.ItemIndex==parent.DueIds.Count){PopResolutionFrame(parent.Id,ResolutionFrameKind.DeferredTurnEnd);AdvanceAfterDeferredTurnEnd(_players[parent.OwnerSeat],parent.Skipped);return;}
            var due=_deferredHandAlignments.SingleOrDefault(d=>d.Id==parent.DueIds[parent.ItemIndex]);
            ReplaceRuntimeTop(parent with{ItemIndex=parent.ItemIndex+1});
            if(due is null)continue;
            if(due.Id<=0||due.Id>_deferredHandAlignmentSequence||due.Source.OwnerSeat==due.TargetSeat||!(due.DueKind==DeferredHandAlignmentDueKind.SourceCurrentTurnEnd?due.Source.OwnerSeat==parent.OwnerSeat&&due.CreatedTurn==parent.TurnNumber:due.DueKind==DeferredHandAlignmentDueKind.TargetNextActualTurnEnd&&due.TargetSeat==parent.OwnerSeat&&due.CreatedTurn<parent.TurnNumber))
                throw new InvalidOperationException("The deferred cursor contains an invalid or premature due.");
            _deferredHandAlignments.Remove(due);
            var enabled=EnabledUniqueProgramTriggers(_players[due.Source.OwnerSeat],SkillProgramTriggerWindow.TurnEnding).Any(t=>t.SkillId==due.Source.SkillId&&t.SkillInstanceId==due.Source.SkillInstanceId&&t.Trigger.Id==due.Source.BindingId);
            AdvanceEventRulesAndQueueFact(new DeferredHandAlignmentConsumedEvent(due,enabled));if(!enabled)continue;
            var program=_contentRegistry.GetSkill(due.Source.SkillId).Program!;var trigger=ProgramInstructionResolver.Default.Resolve(program,ProgramInstructionSourceKind.Trigger,due.ContinuationId).Trigger!;
            if(!trigger.DeferredTurnEndOnly)throw new InvalidOperationException("The exact continuation changed after scheduling.");
            ReplaceRuntimeTop(((DeferredTurnEndFrame)_resolutionStack[^1]) with{Current=due});
            var child=new ProgramSkillFrame(++_resolutionSequence,due.Source.OwnerSeat,due.Source.SkillId,due.ContinuationId,program.GameplayHash,0,[],[due.TargetSeat]){TriggerId=due.ContinuationId,SkillInstanceId=due.Source.SkillInstanceId!,WindowContext=new(SkillProgramTriggerWindow.TurnEnding,parent.Id,due.Source.OwnerSeat,TargetSeat:due.TargetSeat)};
            PushRuntimeFrame(child);AdvanceRuntimeProgram(child.Id);return;
        }
    }
    private DeferredHandAlignment RequireDeferredAlignmentParent(ProgramSkillFrame frame)
    {
        if(frame.WindowContext is not {Window:SkillProgramTriggerWindow.TurnEnding} context||_resolutionStack.Count<2||_resolutionStack[^2] is not DeferredTurnEndFrame parent||parent.Id!=context.ParentFrameId||parent.OwnerSeat!=_currentSeat||parent.TurnNumber!=_turnNumber||parent.Current is not {} due||parent.ItemIndex<1||parent.ItemIndex>parent.DueIds.Count||parent.DueIds[parent.ItemIndex-1]!=due.Id||context.OwnerSeat!=frame.OwnerSeat||frame.OwnerSeat!=due.Source.OwnerSeat||frame.SkillId!=due.Source.SkillId||frame.SkillInstanceId!=due.Source.SkillInstanceId||frame.TriggerId!=due.ContinuationId||frame.SelectedTargetSeats is not [var target]||target!=due.TargetSeat||context.TargetSeat!=target)
            throw new InvalidOperationException("The deferred resolver lost its exact due, source, grant, turn or parent.");
        return due;
    }
    private SkillProgramStepOutcome ResolveDeferredHandAlignment(ProgramSkillFrame frame)
    {
        var due=RequireDeferredAlignmentParent(frame);var ownerCount=GetHand(_players[frame.OwnerSeat]).Count;var targetCount=GetHand(_players[due.TargetSeat]).Count;
        var draw=targetCount<ownerCount?Math.Max(0,Math.Min(5,ownerCount)-targetCount):0;var discard=Math.Max(0,targetCount-ownerCount);
        var frozen=new DeferredHandAlignmentResolution(due.Id,due.TargetSeat,ownerCount,targetCount,draw,discard);ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with{DeferredHandAlignmentResolution=frozen});
        AdvanceEventRulesAndQueueFact(new DeferredHandAlignmentResolvedEvent(due.Id,ownerCount,targetCount,draw,discard));
        if(draw>0){DrawProgramCards(frame.Id,due.TargetSeat,draw,null,null,SkillProgramCardSetVisibility.Private,new("skill-program.deferred-hand.draw"));BeginDeferredAlignmentMovement(frame.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(discard>0){var effect=GetOwnedSelectionEffect(GetActiveProgramFrame(frame.Id));return SelectProgramOwnedCards(GetActiveProgramFrame(frame.Id),due.TargetSeat,discard,null,[CardZoneKind.Hand],effect.ResultBind!,discard,0,[],[]);}
        return SkillProgramStepOutcome.Continue;
    }
    private SkillProgramEffect DeferredOwnedSelectionEffect(ProgramSkillFrame frame,SkillProgramEffect original)
    {
        RequireDeferredAlignmentParent(frame);var draft=frame.DeferredHandAlignmentResolution??throw new InvalidOperationException("Missing frozen hand alignment delta.");
        if(original.Op!=SkillProgramEffectOp.ResolveDeferredHandAlignment||draft.DiscardCount<=0)throw new InvalidOperationException("Only a frozen deferred discard owns this draft.");
        return new(SkillProgramEffectOp.SelectOwnedCards,SkillProgramEffectTarget.SelectedTarget,draft.DiscardCount,original.Condition,resultBind:original.ResultBind,zones:[CardZoneKind.Hand],minimumCards:draft.DiscardCount);
    }
    private void CompleteDeferredAlignmentDiscard(long frameId,IReadOnlyList<int> ids,IReadOnlyList<CardLocation> locations)
    {
        var frame=GetActiveProgramFrame(frameId);var due=RequireDeferredAlignmentParent(frame);var draft=frame.DeferredHandAlignmentResolution!;
        if(ids.Count!=draft.DiscardCount||ids.Distinct().Count()!=ids.Count||locations.Any(l=>l!=CardLocation.Hand(due.TargetSeat))||ids.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(due.TargetSeat)))throw new InvalidOperationException("Deferred discard requires its exact frozen private hand entities.");
        MoveCards(ids.Select(id=>GetHand(_players[due.TargetSeat]).Single(c=>c.Id==id)).ToArray(),CardLocation.Hand(due.TargetSeat),CardLocation.DiscardPile,new("skill-program.deferred-hand.discard"));BeginDeferredAlignmentMovement(frameId);
    }
    private void BeginDeferredAlignmentMovement(long frameId)
    {
        var frame=GetActiveProgramFrame(frameId);ReplaceRuntimeTop(frame with{PendingMovementContinuation=new(frame.DeferredHandAlignmentResolution!.TargetSeat,0,null)});
        if(!TryBeginCardsMovedProgramWindow())ReturnRuntimeProgramMovement(frameId);
    }
    private bool CompleteDeferredTurnEndBinding(ProgramSkillFrame frame,ProgramSkillWindowContext context)
    {
        if(_resolutionStack.LastOrDefault() is not DeferredTurnEndFrame parent)return false;
        if(parent.Id!=context.ParentFrameId||parent.Current is not {} due||parent.ItemIndex<1||parent.ItemIndex>parent.DueIds.Count||parent.DueIds[parent.ItemIndex-1]!=due.Id||context.OwnerSeat!=frame.OwnerSeat||frame.OwnerSeat!=due.Source.OwnerSeat||frame.SkillId!=due.Source.SkillId||frame.SkillInstanceId!=due.Source.SkillInstanceId||frame.TriggerId!=due.ContinuationId)throw new InvalidOperationException("A finished deferred binding lost its exact parent cursor.");
        ReplaceRuntimeTop(parent with{Current=null});ContinueDeferredTurnEnd();return true;
    }
    private bool IsExactDeferredChild(ProgramSkillFrame child) =>
        _resolutionStack.FirstOrDefault() is DeferredTurnEndFrame parent && parent.Current is { } due &&
        _resolutionStack.Count >= 2 && _resolutionStack[1] is ProgramSkillFrame active && active.Id == child.Id &&
        child.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } context && context.ParentFrameId == parent.Id && context.OwnerSeat == child.OwnerSeat && context.TargetSeat == due.TargetSeat &&
        parent.OwnerSeat == _currentSeat && parent.TurnNumber == _turnNumber && _phase == TurnPhase.Finished &&
        child.OwnerSeat == due.Source.OwnerSeat && child.SkillId == due.Source.SkillId && child.SkillInstanceId == due.Source.SkillInstanceId && child.TriggerId == due.ContinuationId && child.SelectedTargetSeats is [var target] && target == due.TargetSeat;

    private bool HasDeferredTurnEndBoundaryFrame()
    {
        if(_resolutionStack.FirstOrDefault() is not DeferredTurnEndFrame frame)return false;
        if(frame.OwnerSeat!=_currentSeat||frame.TurnNumber!=_turnNumber||_phase!=TurnPhase.Finished||frame.ItemIndex<1||frame.ItemIndex>frame.DueIds.Count||frame.Current is null||_resolutionStack.Count<2||_resolutionStack[1] is not ProgramSkillFrame child||child.WindowContext?.ParentFrameId!=frame.Id||child.SkillId!=frame.Current.Source.SkillId||child.SkillInstanceId!=frame.Current.Source.SkillInstanceId||child.TriggerId!=frame.Current.ContinuationId||child.SelectedTargetSeats is not [var target]||target!=frame.Current.TargetSeat)throw new InvalidOperationException("A deferred end boundary lost its exact active child.");
        return true;
    }
}
