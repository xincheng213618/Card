using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum LiuYanStage { TusheDrawChildren, LimuNative, LimuReturnedChildren, LimuRecoveryChildren }
public sealed record LiuYanLimuUseOrigin(long ProgramFrameId,long CardUseFrameId,long ActionId,CardConversionSource Source,string GameplayHash,int ActualTurn,int PhaseInstance,int ActorSeat,int CardId,CardKind PrintedKind,CardLocation From,Suit Suit,int Rank);
public sealed record LiuYanReceipt(CardConversionSource Source,string GameplayHash,int InstructionIndex,int ActualTurn,int TurnOwner,SkillProgramEffectOp Operation,LiuYanStage Stage)
{
    public long Before {get;init;} public long After {get;init;} public int DrawCount {get;init;}
    public long ParentWindowId {get;init;} public long OriginalUseId {get;init;} public long ActionId {get;init;}
    public long? PaymentSequence {get;init;} public bool RecoveryIssued {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiuYanLimuUseOrigin? Use {get;init;}
    private IReadOnlyList<int> _targets=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets {get=>_targets;init=>_targets=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiuYanReceipt? LiuYan {get;init;}}
public sealed partial record CardUseFrame
{[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LiuYanLimuUseOrigin? LiuYanLimu {get;init;}}
public sealed record LiuYanStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,SkillProgramEffectOp Operation,long ParentWindowId,long OriginalUseId,long ActionId,string TargetSignature):IGameEvent;
public sealed record LiuYanTusheDrawnEvent(long FrameId,int OwnerSeat,int Requested,int Count,long Before,long After):IGameEvent;
public sealed record LiuYanLimuIssuedEvent(LiuYanLimuUseOrigin Origin):IGameEvent;
public sealed record LiuYanLimuPaidEvent(LiuYanLimuUseOrigin Origin,long Sequence):IGameEvent;
public sealed record LiuYanLimuReturnedEvent(LiuYanLimuUseOrigin Origin):IGameEvent;
public sealed record LiuYanLimuRecoveryIssuedEvent(long FrameId,int OwnerSeat,int Amount):IGameEvent;
public sealed record LiuYanCompletedEvent(long FrameId,SkillProgramEffectOp Operation,int DrawCount,bool RecoveryIssued):IGameEvent;
internal interface ILiuYanSelectionQuery:IProgramSelectionQuery {bool CanUse(int owner,Card card,string skill);}
internal interface ILiuYanHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class LiuYanDescriptor:ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[];
    public override ISkillProgramEffectHandler Handler=>new LiuYanHandler(Op);
}
internal sealed class DrawTusheForActualNonEquipmentTargetsDescriptor:LiuYanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
internal sealed class UseLimuSelfIndulgenceAndRecoverDescriptor:LiuYanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new ConsumeSelectedCards(1)];}
internal sealed class LiuYanHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<ILiuYanHost>().Resolve(f,op);}
internal static class LiuYanComposition
{
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        bool Handles(SkillProgramEffect e)=>e.Op is SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets or SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover;
        if(!p.Activations.Any(a=>a.Effects.Any(Handles))&&!p.Triggers.Any(t=>t.Effects.Any(Handles)))return;
        if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.Contributions.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations)throw new InvalidOperationException(path+": Liu Yan native operations require their exact independent entries.");
        if(p.Id=="ol:tushe"&&p.Activations.Count==0&&p.Triggers is[var t]&&t.Id=="actual-targets"&&t.Window==SkillProgramTriggerWindow.CardUseTargetsFinalized&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&t.Optional&&t.SingleActionInstance&&!t.IncludeResponseUses&&t.CardKinds.Count==0&&t.SourceZones.Count==0&&t.DestinationZones.Count==0&&t.SourceSkillId is null&&t.SourceViewAsId is null&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.Effects is[{Op:SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])return;
        if(p.Id=="ol:limu"&&p.Triggers.Count==0&&p.Activations is[var a]&&a.Id=="self-indulgence"&&a.MinCards==1&&a.MaxCards==1&&a.MinTargets==0&&a.MaxTargets==0&&a.UsesPerTurn is null&&a.UsesPerPhase is null&&a.SourceZones.SequenceEqual(new[]{CardZoneKind.Hand,CardZoneKind.Equipment,CardZoneKind.WoodenOxGrain})&&a.CardSuits.SequenceEqual(new[]{Suit.Diamond})&&a.Effects is[{Op:SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}])return;
        throw new InvalidOperationException(path+": Liu Yan requires exact optional post-target Tushe or single-diamond self Indulgence Limu without a quota.");
    }
}

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard,27231,SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)]
internal sealed class LiuYanLimuCardFilter:IProgramSelectionCardFilter
{public bool Allows(ProgramSelectionContext c,Card card,IProgramSelectionQueries q)=>q.Get<ILiuYanSelectionQuery>().CanUse(c.OwnerSeat,card,c.SkillId);}
[ProgramSelectionModule(ProgramSelectionStage.CardSuit,27231,SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)]
internal sealed class LiuYanLimuSuit:IProgramSelectionSuitProvider
{public Suit GetSuit(ProgramSelectionContext c,Card card,IProgramSelectionQueries q)=>q.Get<IProgramSelectionCardState>().EffectiveSuit(c.OwnerSeat,card);}
