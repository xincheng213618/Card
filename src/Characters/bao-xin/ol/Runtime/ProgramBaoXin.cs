using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum BaoXinStage { StoreChildren,GiftChildren,CleanupChildren,DamageChildren,Mode,GiftCard,GiftTarget,ObtainChildren,YimouGiftChildren,DrawChildren }
public sealed record BaoXinEntity(int CardId,CardKind Kind,CardKind EffectiveKind,CardLocation From);
public sealed record BaoXinYimouQualification(CardConversionSource Source,string GameplayHash,long WindowId,long DamageFrameId,long AttackId,int VictimSeat,int Amount,int Distance,int? SourceSeat,int OccurrenceIndex);
public sealed partial record DamageTriggerWindowFrame
{
    private IReadOnlyList<BaoXinYimouQualification>? _baoXin;
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public IReadOnlyList<BaoXinYimouQualification>? BaoXinYimouQualifications {get=>_baoXin;init=>_baoXin=value is null?null:Array.AsReadOnly(value.ToArray());}
}
public sealed record BaoXinReceipt(CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,int Holder,BaoXinStage Stage)
{
    public bool Paid {get;init;}public int Step {get;init;}public long Before {get;init;}public long After {get;init;}public int ActualCount {get;init;}public int Cursor {get;init;}public int Recipient {get;init;}public int GiftOrdinal {get;init;}public int Branch {get;init;}public int Damage {get;init;}public bool DamageIssued {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? LastRecipient {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public int? SelectedCard {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public PublicPersistentPileSource? Pile {get;init;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public BaoXinYimouQualification? Qualification {get;init;}
    private IReadOnlyList<BaoXinEntity> _original=Array.AsReadOnly(Array.Empty<BaoXinEntity>()),_moving=Array.AsReadOnly(Array.Empty<BaoXinEntity>());
    public IReadOnlyList<BaoXinEntity> Original {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<BaoXinEntity> Moving {get=>_moving;init=>_moving=Array.AsReadOnly(value.ToArray());}
    private IReadOnlyList<int> _consumed=Array.AsReadOnly(Array.Empty<int>()),_eligible=Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Consumed {get=>_consumed;init=>_consumed=Array.AsReadOnly(value.ToArray());}
    public IReadOnlyList<int> Eligible {get=>_eligible;init=>_eligible=Array.AsReadOnly(value.ToArray());}
    public uint RandomBefore {get;init;}public uint RandomAfter {get;init;}public int RandomIndex {get;init;}
}
public sealed partial record ProgramSkillFrame {[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public BaoXinReceipt? BaoXin {get;init;}}
public sealed record BaoXinStartedEvent(long FrameId,CardConversionSource Source,string GameplayHash,SkillProgramEffectOp Op,int ActualTurn,int TurnOwner,int Holder):IGameEvent;
public sealed record BaoXinOriginalEntityEvent(long FrameId,int Index,int CardId,CardKind Kind,CardKind EffectiveKind,CardLocation From):IGameEvent;
public sealed record BaoXinMovementPaidEvent(long FrameId,int Step,BaoXinStage Stage,long Before,long After,int Count,int Recipient):IGameEvent;
public sealed record BaoXinEntityMovedEvent(long FrameId,int Step,long BatchId,int CardId,CardKind Kind,CardLocation From,CardLocation To,long Sequence):IGameEvent;
public sealed record BaoXinRandomIssuedEvent(long FrameId,int Ordinal,int CardId,int Recipient,int Index,int CandidateCount,uint Before,uint After):IGameEvent;
public sealed record BaoXinRandomCandidateEvent(long FrameId,int Ordinal,int Index,int CardId):IGameEvent;
public sealed record BaoXinMutaoGiftEvent(long FrameId,int Ordinal,int CardId,int Recipient,long Sequence):IGameEvent;
public sealed record BaoXinMutaoRetiredEvent(long FrameId,int CardId,long DepartureSequence):IGameEvent;
public sealed record BaoXinMutaoDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat,int SlashCount,int Amount):IGameEvent;
public sealed record BaoXinYimouBranchEvent(long FrameId,int Branch,int VictimSeat):IGameEvent;
public sealed record BaoXinYimouDrawIssuedEvent(long FrameId,int VictimSeat):IGameEvent;
public sealed record BaoXinCompletedEvent(long FrameId,SkillProgramEffectOp Op):IGameEvent;
internal interface IBaoXinHost {SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op);}
internal abstract class BaoXinDescriptor:ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>Op==SkillProgramEffectOp.ResolveBaoXinYimou?[new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]:[];
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy{get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler=>new BaoXinHandler(Op);
}
internal sealed class ResolveBaoXinMutaoDescriptor:BaoXinDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveBaoXinMutao;}
internal sealed class ResolveBaoXinYimouDescriptor:BaoXinDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveBaoXinYimou;}
internal sealed class BaoXinHandler(SkillProgramEffectOp op):ISkillProgramEffectHandler
{public SkillProgramEffectOp Op=>op;public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int target,ISkillProgramEffectHost host)=>host.GetCapability<IBaoXinHost>().Resolve(f,op);}
internal static class BaoXinComposition
{
    internal static bool Handles(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveBaoXinMutao or SkillProgramEffectOp.ResolveBaoXinYimou;
    internal static void ValidateProgram(string path,SkillProgram p)
    {
        if(!p.Activations.Any(a=>a.Effects.Any(e=>Handles(e.Op)))&&!p.Triggers.Any(t=>t.Effects.Any(e=>Handles(e.Op))))return;
        if(p.Id is not("ol:mutao"or"ol:yimou")||p.Modifiers.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.CardIdentities.Count!=0||p.BooleanStates.Count!=0||p.HpSkillGrants.Count!=0||p.DamageModifiers.Count!=0||p.Contributions.Count!=0||p.EquipmentSuitSkillGrants.Count!=0||p.LordSkillProjection||p.CannotChallengeDeclarations)throw new InvalidOperationException(path+": Bao Xin requires independent exact material and native damage contracts.");
        if(p.Id=="ol:mutao")
        {
            if(p.Triggers.Count!=0||p.Activations is not[var a]||a.Id!="public-slash-ring"||a.MinCards!=0||a.MaxCards!=0||a.MinTargets!=1||a.MaxTargets!=1||a.TargetKind!=SkillProgramTargetKind.AnyLiving||a.UsesPerTurn is not null||a.UsesPerPhase!=1||a.Effects is not[{Op:SkillProgramEffectOp.ResolveBaoXinMutao,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||a.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": Mutao requires the complete once-per-Play zero-card one-living-role activation.");return;
        }
        if(p.Activations.Count!=0||p.Triggers is not[var t]||t.Id!="near-damage"||t.Window!=SkillProgramTriggerWindow.AfterDamageApplied||t.Subject!=SkillProgramTriggerSubject.Any||!t.Optional||t.DamageOccurrence!=SkillProgramDamageOccurrence.PerDamage||t.OwnerRelation is not null||t.UsageLimit is not null||t.UsageScope is not null||t.SourceSkillId is not null||t.SourceViewAsId is not null||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Effects is not[{Op:SkillProgramEffectOp.ResolveBaoXinYimou,Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always}]||t.SourceZones.Count!=0||t.DestinationZones.Count!=0||t.CardKinds.Count!=0||t.CardCategories.Count!=0||t.Suits.Count!=0||t.MovementReasons.Count!=0||t.MovementOccurrence is not null||t.RequireDamageSource is not null||t.IncludeResponseUses||t.SingleActionInstance||t.MarkerCost is not null||t.DynamicUsageLimit is not null||t.NamedUsageGroup is not null||t.ChoiceGroup is not null||t.OrderGroup is not null)throw new InvalidOperationException(path+": Yimou requires one optional all-victim true after-damage occurrence with frozen directional distance.");
    }
}
