using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum XiangChongStage { Choice, Targets, Cards, PaymentChildren, ClaimChoice, ClaimChildren, EquipmentReady, EquipmentUse, NativeChildren }
public sealed record XiangChongCard(int CardId, CardKind Kind, CardLocation From);
public sealed record XiangChongEquipmentOrigin(long ProgramFrameId, long CardUseFrameId, int CardId, CardKind CardKind, int ActorSeat, CardLocation From);
public sealed record XiangChongEquipmentMaterialClaim(long OriginalUseId,long OriginalActionId,long LossBatchId,long LossSequence,
    CardLocation OriginalFrom,CardConversionSource Source,string GameplayHash,XiangChongEquipmentOrigin Equipment);
public sealed record XiangChongEquipmentTermination(long Sequence,string Reason,bool ActorDead);
public sealed record XiangChongEquipmentResult(XiangChongEquipmentOrigin Origin,int ActorSeat,long? EntrySequence,int? ReplacedCardId)
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongEquipmentTermination? Termination { get; init; } }
public sealed record XiangChongReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int TurnOwner, string Branch, XiangChongStage Stage)
{
    public long PhaseToken { get; init; }
    public int Required { get; init; }
    public int Recipient { get; init; } = -1;
    public long LossBatchId { get; init; }
    public long LossSequence { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongCard? Lost { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CardLocation? LossDestination { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long BatchId { get; init; }
    public bool NativeCompleted { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongEquipmentOrigin? Equipment { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongEquipmentMaterialClaim? MaterialClaim { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongEquipmentResult? NativeResult { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongCard? Replaced { get; init; }
    private IReadOnlyList<XiangChongCard> _cards=Array.AsReadOnly(Array.Empty<XiangChongCard>()),_selected=Array.AsReadOnly(Array.Empty<XiangChongCard>()),_paid=Array.AsReadOnly(Array.Empty<XiangChongCard>());
    public IReadOnlyList<XiangChongCard> Cards { get=>_cards; init=>_cards=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XiangChongCard> Selected { get=>_selected; init=>_selected=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XiangChongCard> Payments { get=>_paid; init=>_paid=Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<XiangChongCard> _aux=Array.AsReadOnly(Array.Empty<XiangChongCard>());
    public IReadOnlyList<XiangChongCard> Auxiliaries { get=>_aux; init=>_aux=Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongReceipt? XiangChong { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public XiangChongEquipmentOrigin? XiangChongGuyingEquipment { get; init; } }
public sealed record XiangChongStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwner, string Branch, long PhaseToken, int Required, long LossBatchId, long LossSequence) : IGameEvent;
public sealed record XiangChongGuyingInvokedEvent(long FrameId, int OwnerSeat, int ActualTurn, long BatchId, long LossSequence, int CardId, CardConversionSource Source) : IGameEvent;
public sealed record XiangChongDebtConsumedEvent(long FrameId, int OwnerSeat, int Count, long ThroughFrameId) : IGameEvent;
public sealed record XiangChongBranchPaidEvent(long FrameId, int OwnerSeat, int RecipientSeat, string Branch, long PhaseToken, long Before, long After) : IGameEvent;
public sealed record XiangChongEntityMovedEvent(long FrameId, string Segment, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record XiangChongEquipmentIssuedEvent(XiangChongEquipmentOrigin Origin) : IGameEvent;
public sealed record XiangChongEquipmentReturnedEvent(XiangChongEquipmentOrigin Origin, bool Issued) : IGameEvent;
public sealed record XiangChongUseMaterialClaimedEvent(long UseId,long ActionId,long ProgramFrameId,CardConversionSource Source,string GameplayHash,int CardId,CardKind Kind,int RecipientSeat,long LossSequence,long ClaimSequence) : IGameEvent;
public sealed record XiangChongEquipmentMaterialClaimIssuedEvent(XiangChongEquipmentMaterialClaim Claim) : IGameEvent;
public sealed record XiangChongEquipmentMaterialEnteredEvent(XiangChongEquipmentMaterialClaim Claim,long Sequence,int EquippedSeat) : IGameEvent;
public sealed record XiangChongEquipmentResultEvent(XiangChongEquipmentResult Result) : IGameEvent;
public sealed record XiangChongCompletedEvent(long FrameId, string Branch, bool Paid, string Reason) : IGameEvent;
internal interface IXiangChongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal abstract class XiangChongDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler=>new XiangChongHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; }=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if(Op!=SkillProgramEffectOp.ResolveMuzhenExchange) return ParseOwnerAlways(r);
        r.AllowOnly("op","target","stateId","condition");
        var target=r.RequiredEnum<SkillProgramEffectTarget>("target");var state=r.RequiredIdentifier("stateId");
        if(target!=SkillProgramEffectTarget.Owner||state is not("gift" or "place"))throw new InvalidOperationException("Muzhen requires its exact gift or placement branch.");
        var e=new SkillProgramEffect(Op,target,0,r.Condition(),stateId:state);RequireAlways(e,r.Path);return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op switch
    {SkillProgramEffectOp.ResolveGuyingAtomicLossChoice=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)],SkillProgramEffectOp.SettleGuyingPreparationDebt=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)],_=>[]};
}
internal sealed class ResolveGuyingAtomicLossChoiceDescriptor:XiangChongDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveGuyingAtomicLossChoice;}
internal sealed class SettleGuyingPreparationDebtDescriptor:XiangChongDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleGuyingPreparationDebt;}
internal sealed class ResolveMuzhenExchangeDescriptor:XiangChongDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMuzhenExchange;}
internal sealed class XiangChongHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost h)=>h.GetCapability<IXiangChongHost>().Resolve(f,e);}
internal static class XiangChongComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveGuyingAtomicLossChoice or SkillProgramEffectOp.SettleGuyingPreparationDebt or SkillProgramEffectOp.ResolveMuzhenExchange;
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op)))&&!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        bool Effect(IReadOnlyList<SkillProgramEffect> es,SkillProgramEffectOp op)=>es is[var e]&&e.Op==op&&e.Target==SkillProgramEffectTarget.Owner&&e.Condition.Kind==SkillProgramConditionKind.Always;
        if(p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.BooleanStates.Count!=0)throw new InvalidOperationException(path+": XiangChong requires only its complete native operations.");
        if(p.Id=="ol:guying"&&p.Activations.Count==0&&p.Triggers is[var loss,var prep]&&
           loss.Id=="atomic-off-turn-loss"&&loss.Window==SkillProgramTriggerWindow.CardsMoved&&loss.Subject==SkillProgramTriggerSubject.Owner&&loss.OwnerRelation is null&&!loss.Optional&&loss.MovementOccurrence==SkillProgramMovementOccurrence.PerOwnerBatch&&loss.SourceZones.SequenceEqual(new[]{CardZoneKind.Hand,CardZoneKind.Equipment})&&loss.MovementReasons.Count==0&&loss.ExcludedMovementReasons.Count==0&&!loss.MovementDiscardOnly&&loss.UsageLimit is null&&loss.UsageScope is null&&Effect(loss.Effects,SkillProgramEffectOp.ResolveGuyingAtomicLossChoice)&&
           prep.Id=="settle-debt"&&prep.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&prep.Subject==SkillProgramTriggerSubject.Owner&&prep.OwnerRelation is null&&!prep.Optional&&prep.UsageLimit is null&&prep.UsageScope is null&&Effect(prep.Effects,SkillProgramEffectOp.SettleGuyingPreparationDebt)&&
           p.Triggers.All(t=>t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.MarkerCost is null&&t.CardKinds.Count==0&&t.CardCategories.Count==0&&t.DestinationZones.Count==0&&t.SourceSkillId is null&&t.SourceViewAsId is null&&!t.IncludeResponseUses&&!t.SingleActionInstance&&!t.IgnoreOwnSkillMovements))return;
        if(p.Id=="ol:muzhen"&&p.Triggers.Count==0&&p.Activations.Count==2&&p.Activations.Select(a=>a.Id).SequenceEqual(new[]{"gift-and-obtain-equipment","place-and-obtain-hand"})&&
           p.Activations.All(a=>a.MinCards==0&&a.MaxCards==0&&a.MinTargets==0&&a.MaxTargets==0&&a.TargetKind==SkillProgramTargetKind.AnyLiving&&a.UsesPerTurn is null&&a.UsesPerPhase is null&&a.UsesPerGame is null&&a.MarkerCost is null&&a.Condition.Kind==SkillProgramConditionKind.Always&&Effect(a.Effects,SkillProgramEffectOp.ResolveMuzhenExchange))&&p.Activations[0].Effects[0].StateId=="gift"&&p.Activations[1].Effects[0].StateId=="place")return;
        throw new InvalidOperationException(path+": Guying and Muzhen require their exact complete actual-loss/debt or two independent actual-Play branches.");
    }
}
