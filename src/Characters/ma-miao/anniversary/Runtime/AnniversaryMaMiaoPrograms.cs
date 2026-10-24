using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record AnniversaryMaMiaoEntity(int CardId, CardLocation From);
public sealed record AnniversaryMaMiaoReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, string Mode, int Holder, int Recipient)
{
    public string Stage { get; init; } = "adjust";
    public int InitialHand { get; init; }
    public int AdjustmentHp { get; init; }
    public int HandAfter { get; init; } = -1;
    public int Count { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int? CardId { get; init; }
    public CardLocation? From { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public HappyWeiYanGainRequestSettlement? Gain { get; init; }
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    public int RandomIndex { get; init; } = -1;
    private IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<AnniversaryMaMiaoEntity> _pool = [];
    public IReadOnlyList<AnniversaryMaMiaoEntity> RandomPool { get => _pool; init => _pool = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryMaMiaoReceipt? AnniversaryMaMiao { get; init; } }
public sealed record AnniversaryMaMiaoGainQualifiedEvent(long BatchId, CardConversionSource Source, string Hash, int MovementIndex, int Recipient, int Holder, int Sequence) : IGameEvent;
public sealed record AnniversaryMaMiaoTransferProducerEvent(int EntrySequence, int Recipient, long ProducerId, bool NativeSnatch, CardConversionSource? Source, string? Hash) : IGameEvent;
public sealed record AnniversaryMaMiaoStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, string Mode, int Holder, int Recipient, int InitialHand, int AdjustmentHp) : IGameEvent;
public sealed record AnniversaryMaMiaoMovementEvent(long FrameId, string Mode, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryMaMiaoAdjustedEvent(long FrameId, int InitialHand, int HandAfter, int Hp) : IGameEvent;
public sealed record AnniversaryMaMiaoRecoveryIssuedEvent(long FrameId, int Owner, int Requested) : IGameEvent;
public sealed record AnniversaryMaMiaoRandomEvent(long FrameId, int Recipient, int Holder, int CardId, CardLocation From, uint Before, uint After, int Index, int PoolCount) : IGameEvent;
public sealed record AnniversaryMaMiaoDecisionEvent(long FrameId, int Chooser, bool Accepted) : IGameEvent;
public sealed record AnniversaryMaMiaoCompletedEvent(long FrameId) : IGameEvent;
internal interface IAnniversaryMaMiaoHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class AnniversaryMaMiaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryMaMiaoHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveAnniversaryMaMiaoZhangguan ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.CardsGained)];
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveAnniversaryMaMiaoZhangguanDescriptor : AnniversaryMaMiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryMaMiaoZhangguan; }
internal sealed class ResolveAnniversaryMaMiaoCongfengDescriptor : AnniversaryMaMiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng; }
internal sealed class AnniversaryMaMiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryMaMiaoHost>().Begin(f, op);
}
internal static class AnniversaryMaMiaoComposition
{
    internal const string Zhangguan = "anniversary:zhangguan", Congfeng = "anniversary:congfeng";
    [ProgramCatalogValidation(48800)] internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryMaMiaoZhangguan or SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng);
        if (p.Id is not (Zhangguan or Congfeng) && !owns) return;
        var first = p.Id == Zhangguan;
        if (p.Id is not (Zhangguan or Congfeng) || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.Triggers is not [var t] || t.Subject != SkillProgramTriggerSubject.Owner || t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            e.Op != (first ? SkillProgramEffectOp.ResolveAnniversaryMaMiaoZhangguan : SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng) ||
            (first ? t.Id != "each-actual-turn" || t.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || t.TurnOwnerScope != SkillProgramTurnOwnerScope.AnyLiving || !t.Optional :
                t.Id != "native-foreign-gain" || t.Window != SkillProgramTriggerWindow.CardsGained || t.Optional || t.IsSkillInvocation || t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || !t.DestinationZones.SequenceEqual([CardZoneKind.Hand])))
            throw new InvalidOperationException(path + ": Ma Miao requires current every-turn adjustment and recipient-controlled native foreign gains.");
    }
}
