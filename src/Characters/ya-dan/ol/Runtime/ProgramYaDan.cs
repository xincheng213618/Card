using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum QingyaStage { SideChoice, HandChoice, DiscardChildren, NativeChoice, NativeUse, NativeChildren }
public sealed record QingyaHandMaterial(int CardId,CardKind Kind,int HolderSeat,int Slot,long HandEntry);
public sealed record QingyaDiscardEntity(int CardId,CardKind Kind,int HolderSeat,long Sequence);
public sealed record QingyaArc(int Direction,IReadOnlyList<int> Seats)
{
    private IReadOnlyList<int> _seats=Array.AsReadOnly(Seats.ToArray());
    public IReadOnlyList<int> Seats{get=>_seats;init=>_seats=Array.AsReadOnly(value.ToArray());}
}
public sealed record QingyaAction(LegalActionKind Kind,int CardId,CardKind? PlayedKind,int? TargetCardId,IReadOnlyList<int> Targets)
{
    private IReadOnlyList<int> _targets=Array.AsReadOnly(Targets.ToArray());
    public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed record QingyaNativeOrigin(long ProgramFrameId,long GrantId,CardConversionSource Source,string GameplayHash,int ActualTurn,long UseId,long ActionId,int OriginalActor,int ProviderSeat,int CardId,CardKind Kind);
public sealed record YaDanReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,long ParentId,QingyaStage Stage)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ActualDiscardRecoveryPhaseKey? IssuedPhase{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? OriginalUseId{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? OriginalActionId{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? OriginalProvider{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? OriginalTarget{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? Direction{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? GrantId{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ActualDiscardRecoveryPhaseKey? DuePhase{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QingyaAction? SelectedAction{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QingyaNativeOrigin? NativeOrigin{get;init;}
    public int Cursor{get;init;} public long Before{get;init;} public long After{get;init;}
    public int IssuedHistoryCutoff{get;init;}public long ReservedUseId{get;init;}public bool NativeCompleted{get;init;}
    private IReadOnlyList<int> _livingSeats=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> LivingSeats{get=>_livingSeats;init=>_livingSeats=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<QingyaArc> _arcs=Array.AsReadOnly(Array.Empty<QingyaArc>());
    public IReadOnlyList<QingyaArc> Arcs{get=>_arcs;init=>_arcs=Array.AsReadOnly(value.Select(a=>a with{Seats=a.Seats.ToArray()}).ToArray());}
    private IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets{get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<QingyaHandMaterial> _materials=Array.AsReadOnly(Array.Empty<QingyaHandMaterial>());
    public IReadOnlyList<QingyaHandMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<QingyaDiscardEntity> _entities=Array.AsReadOnly(Array.Empty<QingyaDiscardEntity>());
    public IReadOnlyList<QingyaDiscardEntity> Entities{get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public YaDanReceipt? YaDan{get;init;}}
public sealed partial record CardUseFrame{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QingyaNativeOrigin? QingyaOrigin{get;init;}}
public sealed record YaDanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,long ParentId,long? OriginalUseId,long? OriginalActionId,int? OriginalTarget,ActualDiscardRecoveryPhaseKey? IssuedPhase,int HistoryCutoff,long? GrantId,ActualDiscardRecoveryPhaseKey? DuePhase):IGameEvent;
public sealed record QingyaLivingSeatEvent(long FrameId,int Index,int Seat):IGameEvent;
public sealed record QingyaArcSeatEvent(long FrameId,int Direction,int Index,int Seat):IGameEvent;
public sealed record QingyaArcFrozenEvent(long FrameId,int Direction,int Count):IGameEvent;
public sealed record QingyaSideChosenEvent(long FrameId,int Direction):IGameEvent;
public sealed record QingyaHandCandidateEvent(long FrameId,int Cursor,int Index,QingyaHandMaterial Material):IGameEvent;
public sealed record QingyaHandDiscardedEvent(long FrameId,int Cursor,QingyaHandMaterial Material,long Sequence,long Before,long After):IGameEvent;
public sealed record QingyaParticipantSkippedEvent(long FrameId,int Cursor,int Seat):IGameEvent;
public sealed record QingyaGrantIssuedEvent(long GrantId,CardConversionSource Source,string GameplayHash,int ActualTurn,ActualDiscardRecoveryPhaseKey? IssuedPhase,int HistoryCutoff,int EntityCount):IGameEvent;
public sealed record QingyaGrantConsumedEvent(long GrantId,long FrameId,ActualDiscardRecoveryPhaseKey Phase):IGameEvent;
public sealed record QingyaNativeSelectedEvent(long FrameId,int CardId,LegalActionKind Kind,CardKind? PlayedKind,int? TargetCardId,int TargetCount):IGameEvent;
public sealed record QingyaNativeTargetEvent(long FrameId,int Index,int Seat):IGameEvent;
public sealed record QingyaNativeIssuedEvent(QingyaNativeOrigin Origin):IGameEvent;
public sealed record QingyaNativeReturnedEvent(QingyaNativeOrigin Origin):IGameEvent;
public sealed record YaDanCompletedEvent(long FrameId):IGameEvent;
internal interface IYaDanHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}

internal sealed class QingyaBetweenDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler{get;}=new QingyaHandler(SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class QingyaNextPhaseDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler{get;}=new QingyaHandler(SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse);
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.ActualPhaseEnded)];
}
internal sealed class QingyaHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IYaDanHost>().Resolve(f,op);
}
internal static class YaDanComposition
{
    [ProgramCatalogValidation(470)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>e.Op is SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards or SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse))&&
            !p.Activations.Any(a=>a.Effects.Any(e=>e.Op is SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards or SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse)))return;
        if(p.Id!="ol:qingya"||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Triggers.Count!=2)
            throw new InvalidOperationException(path+": Qingya requires its exact native Slash and next-actual-phase pair.");
        var first=p.Triggers.SingleOrDefault(t=>t.Id=="discard-between");
        var next=p.Triggers.SingleOrDefault(t=>t.Id=="use-next-actual-phase");
        if(first is null||next is null||first.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||
            first.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||!first.Optional||
            !first.CardKinds.ToHashSet().SetEquals([CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash])||
            next.Window!=SkillProgramTriggerWindow.ActualPhaseEnded||next.Subject!=SkillProgramTriggerSubject.Owner||
            next.Optional||next.OwnerRelation is not null||next.CardKinds.Count!=0)
            throw new InvalidOperationException(path+": Qingya changed its original target-finalized or exact issued-phase contract.");
        foreach(var t in p.Triggers)
            if(t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||
                t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||
                t.Effects[0].Op!=(t==first?SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards:SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse))
                throw new InvalidOperationException(path+": Qingya must have one exact owner instruction without artificial marker, usage or condition gates.");
    }
}

