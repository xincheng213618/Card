using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record FrozenHandRosterReceipt
{
    private IReadOnlyList<int> _seats=Array.Empty<int>();
    public CardConversionSource Source {get;init;}=null!;
    public string GameplayHash {get;init;}="";
    public long ParentFrameId {get;init;}
    public int Turn {get;init;}
    public int InstructionIndex {get;init;}
    public int InitialOwnerHandCount {get;init;}
    public IReadOnlyList<int> Seats {get=>_seats;init=>_seats=Array.AsReadOnly(value.ToArray());}
    public int Cursor {get;init;}
    public string Stage {get;init;}="participant";
    public string ReturnStage {get;init;}="";
    public int? DrawRecipient {get;init;}
    public int? DamageTarget {get;init;}
    public long Before {get;init;}
    public long After {get;init;}
    public int ActualDrawCount {get;init;}
    public bool DamageIssued {get;init;}
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public FrozenHandRosterReceipt? FrozenHandRoster {get;init;}
}
public sealed record FrozenHandRosterStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long ParentFrameId,
    int Turn,int OwnerHandCount,IReadOnlyList<int> Seats):IGameEvent
{
    private IReadOnlyList<int> _seats=Array.AsReadOnly(Seats.ToArray());
    public IReadOnlyList<int> Seats {get=>_seats;init=>_seats=Array.AsReadOnly(value.ToArray());}
}
public sealed record FrozenHandRosterParticipantAnsweredEvent(long FrameId,int Cursor,int ChooserSeat,bool Accepted):IGameEvent;
public sealed record FrozenHandRosterDrawPaidEvent(long FrameId,int Cursor,int RecipientSeat,string ReturnStage,long Before,long After,int ActualCount):IGameEvent;
public sealed record FrozenHandRosterDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat,int Amount):IGameEvent;
internal interface IFrozenHandRosterHost {SkillProgramStepOutcome ResolveFrozenHandRosterEndingExchange(ProgramSkillFrame f);}
internal sealed class ResolveFrozenHandRosterEndingExchangeDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFrozenHandRosterEndingExchange;
    public override ISkillProgramEffectHandler Handler{get;}=new FrozenHandRosterHandler();
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(e,c)=>c.Draw(new(SkillProgramEffectOp.Draw,SkillProgramEffectTarget.Owner,1,e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class FrozenHandRosterHandler:ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveFrozenHandRosterEndingExchange;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IFrozenHandRosterHost)h).ResolveFrozenHandRosterEndingExchange(f);
}
internal static class FrozenHandRosterComposition
{
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveFrozenHandRosterEndingExchange))return;
        if(t.Effects is not [{Op:SkillProgramEffectOp.ResolveFrozenHandRosterEndingExchange}] || t.Window!=SkillProgramTriggerWindow.TurnEnding ||
            t.Subject!=SkillProgramTriggerSubject.Owner || t.TurnOwnerScope!=SkillProgramTurnOwnerScope.Own || !t.Optional || t.UsageScope is not null)
            throw new InvalidOperationException(path+": frozen hand roster requires its optional own ending window and sole instruction.");
    }
}
