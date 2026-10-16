using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum GoudeOption { Draw=1, Discard=2, Slash=4, Faction=8 }
public enum GoudeStage { Option, DiscardTarget, DiscardCard, SlashTarget, Faction, MovementChildren, NativeSlash, NativeChildren }
public sealed record GoudeHandMaterial(int CardId,CardKind Kind,int HolderSeat,int Slot,long HandEntry);
public sealed record GoudeSlashOrigin(long ProgramFrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,long UseId,long ActionId,int OriginalActor,int ProviderSeat,int OriginalTarget);
public sealed record GoudeReceipt(CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,long WindowId,int HistoryCutoff,string FactionId,int ExecutedMask,GoudeStage Stage)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public GoudeOption? Option{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? TargetSeat{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public GoudeHandMaterial? PaidMaterial{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public GoudeSlashOrigin? NativeOrigin{get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string? NewFaction{get;init;}
    public long Before{get;init;}public long After{get;init;}public int ActualCount{get;init;}public bool NativeCompleted{get;init;}
    private IReadOnlyList<int> _factionSeats=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> FactionSeats{get=>_factionSeats;init=>_factionSeats=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<GoudeHandMaterial> _materials=Array.AsReadOnly(Array.Empty<GoudeHandMaterial>());
    public IReadOnlyList<GoudeHandMaterial> Materials{get=>_materials;init=>_materials=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public GoudeReceipt? Goude{get;init;}}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public GoudeSlashOrigin? GoudeSlash{get;init;}}
public sealed record GoudeZeroSlashDeclaredEvent(int ActualTurn,long UseId,long ActionId,int ActorSeat,int ProviderSeat):IGameEvent;
public sealed record GoudeStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,long WindowId,int HistoryCutoff,string FactionId,int ExecutedMask):IGameEvent;
public sealed record GoudeFactionSeatFrozenEvent(long FrameId,int Index,int Seat):IGameEvent;
public sealed record GoudeOptionChosenEvent(long FrameId,GoudeOption Option):IGameEvent;
public sealed record GoudeTargetChosenEvent(long FrameId,GoudeOption Option,int TargetSeat):IGameEvent;
public sealed record GoudeHandMaterialFrozenEvent(long FrameId,int Index,GoudeHandMaterial Material):IGameEvent;
public sealed record GoudeMovementPaidEvent(long FrameId,GoudeOption Option,long Before,long After,int ActualCount,GoudeHandMaterial? Material,long? Sequence):IGameEvent;
public sealed record GoudeFactionChosenEvent(long FrameId,string Previous,string Current):IGameEvent;
public sealed record GoudeSlashIssuedEvent(GoudeSlashOrigin Origin):IGameEvent;
public sealed record GoudeSlashReturnedEvent(long FrameId,long UseId):IGameEvent;
public sealed record GoudeCompletedEvent(long FrameId):IGameEvent;
internal interface IMengDaHost{SkillProgramStepOutcome Resolve(ProgramSkillFrame f);}
internal sealed class ResolveGoudeActualTurnEndDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveGoudeActualTurnEnd;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler{get;}=new GoudeHandler();
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class GoudeHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveGoudeActualTurnEnd;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IMengDaHost>().Resolve(f);
}
internal static class MengDaComposition
{
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveGoudeActualTurnEnd)))return;
        if(p.Id!="ol:goude"||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Triggers.Count!=2||
            p.Triggers.Select(t=>t.TurnOwnerScope).Order().SequenceEqual(new[]{SkillProgramTurnOwnerScope.Own,SkillProgramTurnOwnerScope.OtherLiving}.Order())==false)
            throw new InvalidOperationException(path+": Goude requires both exact own/foreign actual-turn-end opportunities.");
        foreach(var t in p.Triggers)
            if(t.Window!=SkillProgramTriggerWindow.AfterTurnEnded||t.Subject!=SkillProgramTriggerSubject.Owner||!t.Optional||t.UsageLimit is not null||t.MarkerCost is not null||
                t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Effects is not[{Op:SkillProgramEffectOp.ResolveGoudeActualTurnEnd,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])
                throw new InvalidOperationException(path+": Goude requires a single optional native completed-turn instruction without artificial faction, marker or turn limits.");
    }
}

