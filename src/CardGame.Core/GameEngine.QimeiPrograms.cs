using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record QimeiPairGrant(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int ActualTurnNumber,int TargetSeat);
public sealed record QimeiPairArmedEvent(QimeiPairGrant Grant):IGameEvent;
public sealed record QimeiPairExpiredEvent(long ProgramFrameId,int ActualTurnNumber):IGameEvent;
public enum QimeiAttribute { Hand, Hp }
public sealed record QimeiEqualityReceipt(QimeiPairGrant Grant,long NativeChangeId,QimeiAttribute Attribute,int ActualTurnNumber,
    int ChangedSeat,int BeneficiarySeat,int Before,int After,int OtherValue);
public sealed record QimeiEqualityQualifiedEvent(QimeiEqualityReceipt Receipt):IGameEvent;
public sealed record QimeiEqualityConsumedEvent(long ProgramFrameId,QimeiEqualityReceipt Receipt):IGameEvent;
public sealed record QimeiEqualityDrawIssuedEvent(long ProgramFrameId,QimeiEqualityReceipt Receipt,long Before,long After,int ActualCount):IGameEvent;
public sealed record QimeiDrawPending(QimeiEqualityReceipt Receipt,int InstructionIndex,long Before,long After,int ActualCount);
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QimeiEqualityReceipt? QimeiEquality {get;init;}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public QimeiDrawPending? QimeiDraw {get;init;}
}
public sealed partial class GameEngine
{
    private bool HasQimeiCapability=>_players.Any(p=>p.QimeiActivePair is not null);
    private CardConversionSource QimeiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private IEnumerable<QimeiPairGrant> ActiveQimeiGrants()
    {
        if(!HasQimeiCapability)return [];
        return _players.Select(p=>p.QimeiActivePair).OfType<QimeiPairGrant>().Where(g=>
            _players[g.Source.OwnerSeat].IsAlive && _players[g.TargetSeat].IsAlive &&
            HasRuntimeSkillInstance(_players[g.Source.OwnerSeat],g.Source.SkillId,g.Source.SkillInstanceId));
    }
    private SkillProgramStepOutcome ArmQimeiPair(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.SelectedTargetSeats is not [var target] || target==f.OwnerSeat || !_players[target].IsAlive ||
            f.WindowContext is not {Window:SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SourceSeat:{} actor} || actor!=f.OwnerSeat)
            throw new InvalidOperationException("Qimei needs one actual other living recipient at the owner's preparation.");
        foreach(var old in ActiveQimeiGrants().Where(g=>g.Source.OwnerSeat==f.OwnerSeat && g.Source.SkillInstanceId==f.SkillInstanceId).ToArray())
            AdvanceEventRulesAndQueueFact(new QimeiPairExpiredEvent(old.ProgramFrameId,_turnNumber));
        var grant=new QimeiPairGrant(f.Id,QimeiSource(f),f.GameplayHash,_turnNumber,target);
        _players[f.OwnerSeat].QimeiActivePair=grant;
        AdvanceEventRulesAndQueueFact(new QimeiPairArmedEvent(grant));
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 430)]
    private void ObserveQimeiExpiry(IGameEvent payload)
    {
        if(!HasQimeiCapability)return;
        var started=payload as TurnStartedEvent;var died=payload as PlayerDiedEvent;
        if(started is null && died is null)return;
        foreach(var grant in _players.Select(p=>p.QimeiActivePair).OfType<QimeiPairGrant>().Where(g=>
            started is not null && g.Source.OwnerSeat==started.ActorSeat && g.ActualTurnNumber<started.TurnNumber ||
            died is not null && (g.Source.OwnerSeat==died.VictimSeat || g.TargetSeat==died.VictimSeat)).ToArray())
        {
            _players[grant.Source.OwnerSeat].QimeiActivePair=null;
            AdvanceEventRulesAndQueueFact(new QimeiPairExpiredEvent(grant.ProgramFrameId,_turnNumber));
        }
    }
    private bool QimeiPaidThisTurn(QimeiPairGrant grant,QimeiAttribute attribute)=>CompleteProgramEventHistory().OfType<QimeiEqualityConsumedEvent>().Any(e=>
        e.Receipt.Grant.ProgramFrameId==grant.ProgramFrameId && e.Receipt.Attribute==attribute && e.Receipt.ActualTurnNumber==_turnNumber);
    private void CaptureQimeiMovementQualification(CardMovementBatchContext batch)
    {
        if(!HasQimeiCapability || _turnNumber<1)return;
        foreach(var grant in ActiveQimeiGrants().ToArray())
        {
            if(QimeiPaidThisTurn(grant,QimeiAttribute.Hand))continue;
            var seats=new[]{grant.Source.OwnerSeat,grant.TargetSeat};
            var changed=seats.Select(seat=>new {Seat=seat,Count=batch.SourceCounts.Concat(batch.DestinationCounts??[]).FirstOrDefault(c=>c.Location==CardLocation.Hand(seat))})
                .Where(x=>x.Count is {} c && c.CountBefore!=c.CountAfter)
                .OrderBy(x=>batch.Movements.ToList().FindIndex(m=>m.From==CardLocation.Hand(x.Seat) || m.To==CardLocation.Hand(x.Seat))).ThenBy(x=>x.Seat).ToArray();
            foreach(var item in changed)
            {
                var other=item.Seat==grant.Source.OwnerSeat?grant.TargetSeat:grant.Source.OwnerSeat;
                var value=GetHand(_players[other]).Count;
                if(item.Count!.CountAfter!=value)continue;
                AdvanceEventRulesAndQueueFact(new QimeiEqualityQualifiedEvent(new(grant,batch.Id,QimeiAttribute.Hand,_turnNumber,item.Seat,other,item.Count.CountBefore,item.Count.CountAfter,value)));
                // A single native batch can qualify both endpoints; the first native
                // mutation owns this once-turn item before the reward changes counts.
                break;
            }
        }
    }
    private void CaptureQimeiHpQualification(HpChangeContext change)
    {
        if(!HasQimeiCapability || _turnNumber<1 || change.HpBefore==change.HpAfter)return;
        foreach(var grant in ActiveQimeiGrants().Where(g=>g.Source.OwnerSeat==change.TargetSeat || g.TargetSeat==change.TargetSeat).ToArray())
        {
            if(!_players[grant.Source.OwnerSeat].HasHpValue || !_players[grant.TargetSeat].HasHpValue)continue;
            if(QimeiPaidThisTurn(grant,QimeiAttribute.Hp))continue;
            var other=grant.Source.OwnerSeat==change.TargetSeat?grant.TargetSeat:grant.Source.OwnerSeat;
            if(change.HpAfter!=_players[other].Hp)continue;
            AdvanceEventRulesAndQueueFact(new QimeiEqualityQualifiedEvent(new(grant,change.Id,QimeiAttribute.Hp,_turnNumber,change.TargetSeat,other,change.HpBefore,change.HpAfter,_players[other].Hp)));
        }
    }
    private IReadOnlyList<ProgramTriggerCandidate> CollectQimeiCandidates(long nativeId,SkillProgramTriggerWindow window)=>!HasQimeiCapability?[]:
        CompleteProgramEventHistory().OfType<QimeiEqualityQualifiedEvent>().Where(e=>e.Receipt.NativeChangeId==nativeId).SelectMany(e=>
            CollectProgramTriggerCandidates(_players[e.Receipt.Grant.Source.OwnerSeat],window).Where(c=>c.SkillId==e.Receipt.Grant.Source.SkillId && c.SkillInstanceId==e.Receipt.Grant.Source.SkillInstanceId &&
                GetProgramTrigger(c).Effects is [{Op:SkillProgramEffectOp.SettleQimeiPairEquality}])).Distinct().ToArray();
    private IReadOnlyList<ProgramTriggerCandidate> CollectQimeiMovementCandidates(CardMovementBatchContext b)=>CollectQimeiCandidates(b.Id,SkillProgramTriggerWindow.CardsMoved);
    private IReadOnlyList<ProgramTriggerCandidate> CollectQimeiHpCandidates(HpChangeContext c)=>CollectQimeiCandidates(c.Id,SkillProgramTriggerWindow.AfterHealthChanged);
    private QimeiEqualityReceipt? QimeiQualification(ProgramTriggerCandidate c,long id)=>CompleteProgramEventHistory().OfType<QimeiEqualityQualifiedEvent>().Select(e=>e.Receipt)
        .SingleOrDefault(r=>r.NativeChangeId==id && r.Grant.Source.OwnerSeat==c.OwnerSeat && r.Grant.Source.SkillId==c.SkillId && r.Grant.Source.SkillInstanceId==c.SkillInstanceId);
    private ProgramSkillWindowContext CreateQimeiMovementContext(CardsMovedTriggerWindowFrame window,ProgramTriggerCandidate c)
    {
        var receipt=QimeiQualification(c,window.Id)??throw new InvalidOperationException("Qimei movement lost its native equality qualification.");
        return new(SkillProgramTriggerWindow.CardsMoved,window.Id,c.OwnerSeat,SourceSeat:receipt.ChangedSeat,TargetSeat:receipt.BeneficiarySeat,MovementBatch:window.Batch){QimeiEquality=receipt};
    }
    private ProgramSkillWindowContext CreateQimeiHpContext(HpChangeContext change,ProgramTriggerCandidate c)
    {
        var receipt=QimeiQualification(c,change.Id)??throw new InvalidOperationException("Qimei HP change lost its native equality qualification.");
        return new(SkillProgramTriggerWindow.AfterHealthChanged,change.Id,c.OwnerSeat,SourceSeat:change.SourceSeat,TargetSeat:change.TargetSeat,Amount:change.Amount,HpChange:change){QimeiEquality=receipt};
    }
    private bool IsQimeiEqualityTrigger(SkillProgramTrigger t)=>t.Effects is [{Op:SkillProgramEffectOp.SettleQimeiPairEquality}];
    private bool HasQimeiHealthSubject(int seat)=>_players.Any(p=>p.QimeiActivePair is {} g && (g.Source.OwnerSeat==seat || g.TargetSeat==seat));
    private bool CanRunQimeiEquality(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.QimeiEquality is not {} receipt || receipt.Grant.Source.OwnerSeat!=c.OwnerSeat || receipt.Grant.Source.SkillId!=c.SkillId || receipt.Grant.Source.SkillInstanceId!=c.SkillInstanceId ||
            receipt.NativeChangeId!=context.ParentFrameId || receipt.After!=receipt.OtherValue || receipt.Before==receipt.After || QimeiPaidThisTurn(receipt.Grant,receipt.Attribute))return false;
        return receipt==QimeiQualification(c,context.ParentFrameId) && _players[receipt.BeneficiarySeat].IsAlive &&
            (receipt.Attribute==QimeiAttribute.Hand ? context.MovementBatch?.Id==receipt.NativeChangeId : context.HpChange is {} change && change.Id==receipt.NativeChangeId && change.TargetSeat==receipt.ChangedSeat && change.HpBefore==receipt.Before && change.HpAfter==receipt.After);
    }
    private SkillProgramStepOutcome SettleQimeiPairEquality(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);var receipt=f.WindowContext?.QimeiEquality??throw new InvalidOperationException("Qimei lost its owning mutation receipt.");
        if(QimeiPaidThisTurn(receipt.Grant,receipt.Attribute))return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new QimeiEqualityConsumedEvent(f.Id,receipt));
        var before=_movementSequence;
        ReplaceRuntimeTop(f=f with{QimeiDraw=new(receipt,f.InstructionIndex,before,before,0),PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var cards=DrawCards(_players[receipt.BeneficiarySeat],1,true,new("program.qimei.equal-draw"));
        ReplaceRuntimeTop(f=GetActiveProgramFrame(f.Id) with{QimeiDraw=f.QimeiDraw! with{After=_movementSequence,ActualCount=cards.Count}});
        AdvanceEventRulesAndQueueFact(new QimeiEqualityDrawIssuedEvent(f.Id,receipt,before,_movementSequence,cards.Count));
        AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,22200)]
    private bool ResumeQimeiDraw(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{QimeiDraw:{} r}f || f.Id!=id)return false;
        RequireQimeiDraw(f);
        if(TryBeginCardsMovedProgramWindow(id) || TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement))return true;
        ReplaceRuntimeTop(f with{QimeiDraw=null,PendingMovementContinuation=null});AdvanceRuntimeProgram(id);return true;
    }
    private bool ValidQimeiDraw(ProgramSkillFrame f)=>f.QimeiDraw is {} r && r.InstructionIndex==f.InstructionIndex && f.WindowContext?.QimeiEquality==r.Receipt &&
        r.Receipt.Grant.Source.OwnerSeat==f.OwnerSeat && r.Receipt.Grant.Source.SkillId==f.SkillId && r.Receipt.Grant.Source.SkillInstanceId==f.SkillInstanceId &&
        r.Receipt.Grant.GameplayHash==f.GameplayHash && r.Before<=r.After && r.After<=_movementSequence && r.ActualCount is >=0 and <=1 &&
        CompleteProgramEventHistory().OfType<QimeiEqualityConsumedEvent>().Count(e=>e.ProgramFrameId==f.Id && e.Receipt==r.Receipt)==1 &&
        CompleteProgramEventHistory().OfType<QimeiEqualityDrawIssuedEvent>().Count(e=>e.ProgramFrameId==f.Id && e.Receipt==r.Receipt && e.Before==r.Before && e.After==r.After && e.ActualCount==r.ActualCount)==1 &&
        _cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.To==CardLocation.Hand(r.Receipt.BeneficiarySeat) && m.Reason.Value=="program.qimei.equal-draw")==r.ActualCount;
    private void RequireQimeiDraw(ProgramSkillFrame f){if(!ValidQimeiDraw(f))throw new InvalidOperationException("Qimei draw lost its exact mutation/source/once-consumed invoice.");}

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 420)]
    private void AssertRegisteredQimeiDraw(ProgramSkillFrame frame)
    {
        if (frame.QimeiDraw is not null) RequireQimeiDraw(frame);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 380)]
    private bool IsQimeiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.SettleQimeiPairEquality && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null && ValidQimeiDraw(f);
}
