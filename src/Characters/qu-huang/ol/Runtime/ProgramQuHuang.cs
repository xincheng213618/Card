using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum QuHuangStage { DrawOwner,DrawOther,QiejianChoice,FieldChildren,NishouChoice,Lightning,LightningChildren,SwapTarget,HoldOwner,HoldOther,GiveOwner,GiveOther,Cleanup }
public sealed record QuHuangEntity(int CardId,CardKind Kind,CardLocation From);
public sealed record QuHuangNishouUseOrigin(long ProgramFrameId,long CardUseFrameId,long ActionId,CardConversionSource Source,string GameplayHash,int ActorSeat,int CardId,CardKind PrintedKind,Suit Suit,int Rank,long LossSequence);
public sealed record QuHuangReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TurnOwner,QuHuangStage Stage,int TargetSeat)
{
    public bool Paid {get;init;} public int Step {get;init;} public long Before {get;init;} public long After {get;init;} public int ActualCount {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public NishouEquipmentLostEvent? Equipment {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? DebtId {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QuHuangNishouUseOrigin? Native {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public long? NativePaymentSequence {get;init;}
    private IReadOnlyList<QuHuangEntity> _entities=Array.AsReadOnly(Array.Empty<QuHuangEntity>()),_owner=Array.AsReadOnly(Array.Empty<QuHuangEntity>()),_other=Array.AsReadOnly(Array.Empty<QuHuangEntity>());
    public IReadOnlyList<QuHuangEntity> Entities {get=>_entities;init=>_entities=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<QuHuangEntity> OwnerCards {get=>_owner;init=>_owner=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<QuHuangEntity> OtherCards {get=>_other;init=>_other=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _grain=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Grain {get=>_grain;init=>_grain=Array.AsReadOnly(value.ToArray());}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QuHuangReceipt? QuHuang {get;init;}}
public sealed partial record CardUseFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public QuHuangNishouUseOrigin? QuHuangNishou {get;init;}}
public sealed record NishouEquipmentLostEvent(long BatchId,int OwnerSeat,int CardId,CardKind Kind,Suit PrintedSuit,int Rank,long Sequence,ActualDiscardRecoveryPhaseKey? Phase):IGameEvent;
public sealed record QuHuangStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,long ParentId,int ActualTurn,int TargetSeat):IGameEvent;
public sealed record QiejianRoundBlockedEvent(long FrameId,int OwnerSeat,int TargetSeat,int RoundNumber):IGameEvent;
public sealed record QuHuangEntityMovedEvent(long FrameId,int Step,QuHuangStage Stage,long BatchId,int CardId,CardKind Kind,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record QuHuangMovementPaidEvent(long FrameId,int Step,QuHuangStage Stage,long Before,long After,int Count):IGameEvent;
public sealed record NishouSwapDebtIssuedEvent(long DebtId,CardConversionSource Source,string GameplayHash,ActualDiscardRecoveryPhaseKey Phase):IGameEvent;
public sealed record NishouSwapDebtConsumedEvent(long DebtId,long FrameId,long BoundaryId):IGameEvent;
public sealed record NishouOriginalHandFrozenEvent(long FrameId,int HolderSeat,int CardId,CardKind Kind):IGameEvent;
public sealed record NishouLightningPaidEvent(QuHuangNishouUseOrigin Origin,long Sequence):IGameEvent;
public sealed record NishouLightningReturnedEvent(QuHuangNishouUseOrigin Origin):IGameEvent;
public sealed record QuHuangCompletedEvent(long FrameId):IGameEvent;
internal interface IQuHuangHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class QuHuangDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap?SkillProgramTriggerWindow.ActualPhaseEnded:SkillProgramTriggerWindow.CardsMoved)];
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler=>new QuHuangHandler(Op);
}
internal sealed class ResolveQiejianLastHandLossDescriptor:QuHuangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQiejianLastHandLoss;}
internal sealed class ResolveNishouEquipmentLossChoiceDescriptor:QuHuangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveNishouEquipmentLossChoice;}
internal sealed class ResolveNishouIssuedCurrentPhaseSwapDescriptor:QuHuangDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap;}
internal sealed class QuHuangHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IQuHuangHost>().Resolve(f,op);}
internal static class QuHuangComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveQiejianLastHandLoss or SkillProgramEffectOp.ResolveNishouEquipmentLossChoice or SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap;
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op)))&&!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.HpSkillGrants.Count!=0||p.BooleanStates.Count!=0||p.Contributions.Count!=0||p.DamageModifiers.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations||p.Id is not("ol:qiejian"or"ol:nishou")||p.Triggers.Count!=(p.Id=="ol:qiejian"?1:2))throw new InvalidOperationException(path+": Qu Huang requires exact independent movement/issued-phase protocols.");
        foreach(var t in p.Triggers)
        {
            var q=p.Id=="ol:qiejian";var phase=t.Id=="issued-current-phase-swap";var op=q?SkillProgramEffectOp.ResolveQiejianLastHandLoss:phase?SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap:SkillProgramEffectOp.ResolveNishouEquipmentLossChoice;
            if(t.Id!=(q?"last-hand-loss":phase?"issued-current-phase-swap":"equipment-to-discard")||t.Window!=(phase?SkillProgramTriggerWindow.ActualPhaseEnded:SkillProgramTriggerWindow.CardsMoved)||t.Subject!=SkillProgramTriggerSubject.Owner||t.Optional!=q||t.OwnerRelation is not null||t.SourceSkillId is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.UsageLimit is not null||t.DamageOccurrence is not null||t.DestinationZones.Count!=0||t.CardKinds.Count!=0||t.Effects is not[{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}e]||e.Op!=op||(phase?t.SourceZones.Count!=0||t.MovementOccurrence is not null:!t.SourceZones.SequenceEqual([q?CardZoneKind.Hand:CardZoneKind.Equipment])||t.MovementOccurrence!=SkillProgramMovementOccurrence.PerBatch))throw new InvalidOperationException(path+": Unexpected Qu Huang movement source or native phase contract.");
        }
    }
}
