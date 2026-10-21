using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IShenPangTongHost { SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class ShenPangTongDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler => new ShenPangTongHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override bool UsesConversionPolarity => op == SkillProgramEffectOp.ResolveShenPangTongFengliao;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => op == SkillProgramEffectOp.ResolveShenPangTongKunyu &&
        c.Frame.SkillId == ShenPangTongComposition.Kunyu && c.Frame.WindowContext?.Window == SkillProgramTriggerWindow.OwnerDied;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveShenPangTongFengliao ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects : SkillProgramTriggerWindow.OwnerDied)];
}
internal sealed class ResolveShenPangTongFengliaoDescriptor() : ShenPangTongDescriptor(SkillProgramEffectOp.ResolveShenPangTongFengliao);
internal sealed class ResolveShenPangTongKunyuDescriptor() : ShenPangTongDescriptor(SkillProgramEffectOp.ResolveShenPangTongKunyu);
internal sealed class ShenPangTongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShenPangTongHost>().Execute(f, op);
}
public enum ShenPangTongStage { DrawAttempt, DrawChildren, DamageChildren, RemovalChildren }
public sealed record ShenPangTongReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, ShenPangTongStage Stage)
{
    public long ActionId { get; init; }
    public int TargetSeat { get; init; } = -1;
    public SkillPolarity Polarity { get; init; }
    public long DeathId { get; init; }
    public int RemovedCardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long BatchId { get; init; }
    public bool DrawReturned { get; init; }
    public int ActualDraw { get; init; }
}
public sealed record ShenPangTongRevivalReceipt(long ProgramFrameId, CardConversionSource Source, string GameplayHash, long WindowId, int RemovedCardId, long Before, long After, long BatchId);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenPangTongReceipt? ShenPangTong { get; init; } }
public sealed partial record DeathFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ShenPangTongOutcomeDeferred { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenPangTongRevivalReceipt? ShenPangTongRevival { get; init; }
}
public sealed partial class CharacterState { internal bool HasShenPangTongKunyu => SkillGrants.Grants.Any(g => g.IsEnabled && g.SkillId == "anniversary:kunyu"); }
public sealed record ShenPangTongStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, long ActionId, int TargetSeat, SkillPolarity Polarity, long DeathId) : IGameEvent;
public sealed record ShenPangTongDrawReturnedEvent(long FrameId, int TargetSeat, int ActualCount, long? AttemptId, long Before, long After) : IGameEvent;
public sealed record ShenPangTongRemovalPaidEvent(long FrameId, long DeathId, long BatchId, long Before, long After) : IGameEvent;
public sealed record ShenPangTongRevivalGrantedEvent(long FrameId, long DeathId, int OwnerSeat) : IGameEvent;
public sealed record ShenPangTongRevivedEvent(long DeathId, long ProgramFrameId, int OwnerSeat, int Hp) : IGameEvent;

internal static class ShenPangTongComposition
{
    internal const string Luansuo="anniversary:luansuo", Fengliao="anniversary:fengliao", Kunyu="anniversary:kunyu";
    [ProgramCatalogValidation(39200)] internal static void Validate(string path, SkillProgram p)
    {
        bool Own(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveShenPangTongFengliao or SkillProgramEffectOp.ResolveShenPangTongKunyu;
        if(p.Id!=Luansuo && !p.Triggers.SelectMany(t=>t.Effects).Concat(p.Activations.SelectMany(a=>a.Effects)).Any(e=>Own(e.Op)))return;
        bool Exact(SkillProgramTrigger t,string id,SkillProgramTriggerWindow window,SkillProgramEffectOp op)=>t.Id==id&&t.Window==window&&!t.Optional&&t.UsageLimit is null&&t.UsageScope is null&&t.Condition.Kind==SkillProgramTriggerConditionKind.Always&&t.Effects is [{Target:SkillProgramEffectTarget.Owner,Condition.Kind:SkillProgramConditionKind.Always} e]&&e.Op==op;
        var valid=p.Id switch{
            Luansuo=>p.Triggers.Count==0&&p.CardIdentities is [{Id:"different-discard-suit-chain",OutputKind:CardKind.IronChain} i]&&i.Zones.SequenceEqual([CardZoneKind.Hand])&&i.InputKinds.Count==0&&i.InputSuits.SequenceEqual([Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond,Suit.None])&&i.Condition.Kind==SkillProgramConditionKind.Always,
            Fengliao=>p.CardIdentities.Count==0&&p.Triggers is [var t]&&Exact(t,"unique-target",SkillProgramTriggerWindow.CardUseBeforeTargetEffects,SkillProgramEffectOp.ResolveShenPangTongFengliao)&&t.OwnerRelation==SkillProgramCardActionOwnerRelation.Actor&&!t.IncludeResponseUses&&!t.SingleActionInstance&&t.CardKinds.Count==0&&t.CardCategories.Count==0,
            Kunyu=>p.CardIdentities.Count==0&&p.Triggers is [var t]&&Exact(t,"death-rebirth",SkillProgramTriggerWindow.OwnerDied,SkillProgramEffectOp.ResolveShenPangTongKunyu)&&t.Subject==SkillProgramTriggerSubject.Owner&&t.Priority==1000,
            _=>false};
        if(!valid||p.Activations.Count!=0||p.ViewAs.Count!=0||p.Modifiers.Count!=0||p.BooleanStates.Count!=0||p.CardPolicies.Count!=0)throw new InvalidOperationException(path+": Shen Pang Tong requires the exact formal native programs.");
    }
}
