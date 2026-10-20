using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum PijingStage { Toggles, Gifts, GiftChildren, Reciprocal, DrawChildren }
public sealed record PijingToggle(int Seat,bool Remove,IReadOnlyList<int> Pair)
{
    private IReadOnlyList<int> _pair=Array.AsReadOnly(Pair.ToArray());
    public IReadOnlyList<int> Pair {get=>_pair;init=>_pair=Array.AsReadOnly(value.ToArray());}
}
public sealed record PijingGrantIssuedEvent(long GrantId,CardConversionSource Source,string GameplayHash,int HolderSeat,long UseId,long ActionId,int ActualTurn):IGameEvent;
public sealed record PijingNextUseGroup(long GroupId,CardConversionSource Source,string GameplayHash,int HolderSeat,IReadOnlyList<long> Grants)
{
    private IReadOnlyList<long> _grants=Array.AsReadOnly(Grants.ToArray());
    public IReadOnlyList<long> Grants {get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}
}
public sealed record PijingNextUseIssuedEvent(long UseId,long ActionId,long GroupId,CardConversionSource Source,string GameplayHash,int HolderSeat,int GrantCount):IGameEvent;
public sealed record PijingNextUseGrantEvent(long UseId,long GroupId,int Index,long GrantId):IGameEvent;
public sealed record PijingGrantConsumedEvent(long FrameId,long UseId,long GroupId,long GrantId):IGameEvent;
public sealed record PijingStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long UseId,long ActionId,long WindowId,int ActualTurn,int ActorSeat,int ProviderSeat,int X,bool Reciprocal,long GroupId,int OriginalCount):IGameEvent;
public sealed record PijingTargetSlotEvent(long FrameId,bool Final,int Index,int Seat):IGameEvent;
public sealed record PijingToggleSelectedEvent(long FrameId,int Index,int Seat,bool Remove,int PairCount):IGameEvent;
public sealed record PijingTogglePairEvent(long FrameId,int Index,int PairIndex,int Seat):IGameEvent;
public sealed record PijingTargetsAppliedEvent(long FrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat,int OriginalCount,int FinalCount,bool Reciprocal):IGameEvent;
public sealed record PijingGiftSelectedEvent(long FrameId,int Index,int DonorSeat,int CardId,CardKind Kind,CardLocation From):IGameEvent;
public sealed record PijingGiftPaidEvent(long FrameId,int Index,int DonorSeat,int CardId,CardLocation From,int RecipientSeat,long BatchId,long Sequence,long Before,long After):IGameEvent;
public sealed record PijingGiftSkippedEvent(long FrameId,int Index,int DonorSeat):IGameEvent;
public sealed record PijingMovementPaidEvent(long FrameId,PijingStage Stage,int Index,int SubjectSeat,long Before,long After,int Count):IGameEvent;
public sealed record PijingGiftCompletedEvent(long FrameId,int Index,long GrantId):IGameEvent;
public sealed record PijingNextChoiceEvent(long FrameId,long GroupId,string Choice):IGameEvent;
public sealed record PijingCompletedEvent(long FrameId):IGameEvent;
public sealed record PijingRedirectedEvent(long UseId,long ActionId,long FrameId,CardConversionSource Source,string GameplayHash,int InstructionIndex,int TargetIndex,int PreviousSeat,int Seat):IGameEvent;
public sealed record PijingTargetTail(long FrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat,bool Reciprocal,IReadOnlyList<int> Original,IReadOnlyList<int> Final)
{
    private IReadOnlyList<int> _original=Array.AsReadOnly(Original.ToArray()),_final=Array.AsReadOnly(Final.ToArray());
    public IReadOnlyList<int> Original {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Final {get=>_final;init=>_final=Array.AsReadOnly(value.ToArray());}
}
public sealed record PijingReceipt(CardConversionSource Source,string GameplayHash,long UseId,long ActionId,long WindowId,int ActualTurn,int ActorSeat,int ProviderSeat,int X,bool Reciprocal,long GroupId,PijingStage Stage,IReadOnlyList<int> Original,IReadOnlyList<PijingToggle> Toggles,IReadOnlyList<long> Grants,int Index=0,long Before=0,long After=0,int Count=0,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] PijingGiftSelectedEvent? Gift=null)
{
    private IReadOnlyList<int> _original=Array.AsReadOnly(Original.ToArray());
    public IReadOnlyList<int> Original {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<PijingToggle> _toggles=Array.AsReadOnly(Toggles.Select(t=>t with{Pair=t.Pair}).ToArray());
    public IReadOnlyList<PijingToggle> Toggles {get=>_toggles;init=>_toggles=Array.AsReadOnly(value.Select(t=>t with{Pair=t.Pair}).ToArray());}
    private IReadOnlyList<long> _grants=Array.AsReadOnly(Grants.ToArray());
    public IReadOnlyList<long> Grants {get=>_grants;init=>_grants=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public PijingReceipt? Pijing{get;init;}}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public PijingTargetTail? PijingTail{get;init;}
    private IReadOnlyList<PijingNextUseGroup>? _pijingNext;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<PijingNextUseGroup>? PijingNextUses {get=>_pijingNext;init=>_pijingNext=value is null?null:Array.AsReadOnly(value.Select(g=>g with{Grants=g.Grants}).ToArray());}
}
public sealed partial record ProgramSkillWindowContext
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? PijingIssuedGroupId{get;init;}}
internal interface ILiuPanHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class LiuPanDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new LiuPanHandler(Op);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
}
internal sealed class ResolvePijingTargetTogglesAndRandomGiftsDescriptor:LiuPanDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolvePijingTargetTogglesAndRandomGifts;}
internal sealed class ResolvePijingNextSoleTargetChoiceDescriptor:LiuPanDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class LiuPanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<ILiuPanHost>().Resolve(f,op);}
internal static class LiuPanComposition
{
    [ProgramCatalogValidation(580)]
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        static bool Own(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolvePijingTargetTogglesAndRandomGifts or SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice;
        if(!p.Activations.Any(a=>a.Effects.Any(e=>Own(e.Op)))&&!p.Triggers.Any(t=>t.Effects.Any(e=>Own(e.Op))))return;
        if(p.Id!="ol:pijing"||p.Activations.Count!=0||p.Triggers.Count!=2||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0)throw new InvalidOperationException(path+": Pijing requires both complete current ordinary-OL clauses.");
        foreach(var t in p.Triggers)
        {
            var initial=t.Id=="toggle-targets";
            if(!initial&&t.Id!="next-sole-use"||t.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Optional!=initial||t.Priority!=(initial?0:-10)||t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Effects.Count!=1||t.Effects[0].Op!=(initial?SkillProgramEffectOp.ResolvePijingTargetTogglesAndRandomGifts:SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice)||t.Effects[0].Target!=SkillProgramEffectTarget.Owner||t.Effects[0].Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Pijing requires exact actual-turn toggles and its separately issued next-sole-use choice.");
        }
    }
}
