using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ChenglieStage { Targets, RevealChildren, Exchange, ExchangeChildren, Assignment, AssignmentChildren, Outcomes, Gift, GiftChildren, RecoveryChildren, CleanupChildren }
public sealed record ChenglieAttachment(int Index,int HolderSeat,int CardId,CardKind Kind,Suit Suit,bool Red,CardLocation Location,long Sequence);
public sealed record ChenglieLease(long SetupFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat)
{
    private IReadOnlyList<int> _original=[]; public IReadOnlyList<int> Original {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _added=[]; public IReadOnlyList<int> Added {get=>_added;init=>_added=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<ChenglieAttachment> _cards=[]; public IReadOnlyList<ChenglieAttachment> Cards {get=>_cards;init=>_cards=Array.AsReadOnly(value.ToArray());}
    public bool Ready {get;init;} public bool Settled {get;init;}
}
public sealed record ChenglieReceipt(CardConversionSource Source,string GameplayHash,long UseId,long ActionId,long WindowId,long SetupFrameId,bool Outcome,ChenglieStage Stage)
{
    private IReadOnlyList<int> _original=[]; public IReadOnlyList<int> Original {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _added=[]; public IReadOnlyList<int> Added {get=>_added;init=>_added=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _revealed=[]; public IReadOnlyList<int> Revealed {get=>_revealed;init=>_revealed=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _pool=[]; public IReadOnlyList<int> Pool {get=>_pool;init=>_pool=Array.AsReadOnly(value.ToArray());}
    public bool Paid {get;init;} public int Index {get;init;} public long Before {get;init;} public long After {get;init;}
    public int? SwapHand {get;init;} public int? SwapTop {get;init;} public int? GiftId {get;init;} public CardLocation? GiftFrom {get;init;} public int Count {get;init;}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ChenglieReceipt? Chenglie {get;init;}}
public sealed partial record CardUseFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public ChenglieLease? ChenglieLease {get;init;}}
public sealed record ChenglieStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long UseId,long ActionId,long WindowId,long SetupFrameId,bool Outcome,int OriginalCount):IGameEvent;
public sealed record ChenglieTargetSlotEvent(long FrameId,bool Added,int Index,int Seat):IGameEvent;
public sealed record ChenglieTargetsAppliedEvent(long FrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int ProviderSeat,int OriginalCount,int AddedCount):IGameEvent;
public sealed record ChenglieRedirectedEvent(long UseId,long ActionId,long FrameId,CardConversionSource Source,string GameplayHash,int TargetIndex,int PreviousSeat,int Seat):IGameEvent;
public sealed record ChenglieRevealedCardEvent(long FrameId,int Index,int CardId,CardKind Kind,Suit Suit,int Rank):IGameEvent;
public sealed record ChenglieSwapSelectedEvent(long FrameId,int HandCardId,int TopCardId):IGameEvent;
public sealed record ChenglieCardMovedEvent(long FrameId,ChenglieStage Stage,int Index,int CardId,CardLocation From,CardLocation To,CardMoveReason Reason,long Sequence):IGameEvent;
public sealed record ChenglieAttachmentPlacedEvent(long SetupFrameId,long UseId,long ActionId,int Index,int HolderSeat,int CardId,CardKind Kind,Suit Suit,bool Red,CardLocation Location,long Sequence):IGameEvent;
public sealed record ChenglieSetupReadyEvent(long SetupFrameId,long UseId,long ActionId,int AttachmentCount):IGameEvent;
public sealed record ChenglieResponseRecordedEvent(long SetupFrameId,long UseId,long IncomingActionId,int HolderSeat,long ResponseActionId,int ProviderSeat,ProgramCardContinuation Continuation):IGameEvent;
public sealed record ChenglieOutcomeIssuedEvent(long FrameId,long SetupFrameId,int Index,int HolderSeat,bool Red,bool Responded):IGameEvent;
public sealed record ChenglieOutcomeCompletedEvent(long FrameId,long SetupFrameId,int Index):IGameEvent;
public sealed record ChenglieMovementPaidEvent(long FrameId,ChenglieStage Stage,int Index,long Before,long After,int Count):IGameEvent;
public sealed record ChenglieLeaseSettledEvent(long FrameId,long SetupFrameId,long UseId):IGameEvent;
public sealed record ChenglieCompletedEvent(long FrameId,long SetupFrameId,bool Outcome):IGameEvent;
internal interface IMaChengHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class MaChengDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MaChengHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op==SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards?SkillProgramTriggerWindow.CardUseTargetsFinalized:SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ResolveChenglieExtraTargetsAndHiddenCardsDescriptor:MaChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards;}
internal sealed class ResolveChenglieRedHolderOutcomeDescriptor:MaChengDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveChenglieRedHolderOutcome;}
internal sealed class MaChengHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost host)=>host.GetCapability<IMaChengHost>().Resolve(f,op);}
internal static class MaChengComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards or SkillProgramEffectOp.ResolveChenglieRedHolderOutcome;
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Handles(e.Op)))return;
        if(p.Id!="ol:chenglie"||p.Activations.Count!=0||p.Triggers.Count!=2||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.DamageModifiers.Count!=0)throw new InvalidOperationException(path+": Chenglie requires the complete extra-target, secret-entity and whole-Slash outcome protocol.");
        foreach(var t in p.Triggers)
        {
            var initial=t.Id=="extra-targets";
            if(!initial&&t.Id!="red-holder-outcomes"||t.Window!=(initial?SkillProgramTriggerWindow.CardUseTargetsFinalized:SkillProgramTriggerWindow.CardUseCompleted)||t.Optional!=initial||t.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor||t.Priority!=0||t.UsageLimit is not null||t.MarkerCost is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Effects is not[var e]||e.Op!=(initial?SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards:SkillProgramEffectOp.ResolveChenglieRedHolderOutcome)||e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Chenglie must use its original finalized Slash and its separately issued completed-use continuation.");
        }
    }
}
