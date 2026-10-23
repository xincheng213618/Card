using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum AnniversaryZhangJiaoStage { SanshouSnapshot, Obtained, Shuffled, Choosing, Damaging }
public sealed record ZhangJiaoAcquisitionPayment(long Before, long After)
{
    private readonly IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryZhangJiaoReceipt(CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Op, int InstructionIndex, AnniversaryZhangJiaoStage Stage,
    int UsedCategories = 0, int Rank = 0, int MarkerBefore = 0, int MarkerAfter = 0,
    int Cursor = 0, long Before = 0, long After = 0, uint RandomBefore = 0, uint RandomAfter = 0)
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>()), _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangJiaoAcquisitionPayment? HappySijunPayment { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryZhangJiaoReceipt? AnniversaryZhangJiao { get; init; }
}
// Obtained entities and RNG proof stay on the trusted original frame.
public sealed record AnniversaryYizhaoAccumulatedEvent(long FrameId, CardConversionSource Source, int Rank, int Before, int After, int ObtainedCount) : IGameEvent;
public sealed record AnniversarySijunObtainedEvent(long FrameId, CardConversionSource Source, int RemovedHuang, int ObtainedCount, int RankSum) : IGameEvent;
public sealed record AnniversaryZhangJiaoShuffledEvent(long FrameId, CardConversionSource Source, int TurnNumber, int TurnOwnerSeat) : IGameEvent;
public sealed record AnniversarySanshouResolvedEvent(long FrameId, CardConversionSource Source, int UsedCategoryMask, bool Prevented) : IGameEvent;
internal interface IAnniversaryZhangJiaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal abstract class AnniversaryZhangJiaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryZhangJiaoHandler(Op);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class AnniversaryYizhaoDescriptor : AnniversaryZhangJiaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangJiaoYizhao;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class AnniversarySanshouDescriptor : AnniversaryZhangJiaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSanshou;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner) throw new InvalidOperationException(r.Path + ": owner required.");
        return new(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(), sourceBind: r.OptionalIdentifier("sourceBind"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => e.SourceBind is { } bind ? [new ReadCardSet(bind)] : [];
}
internal sealed class AnniversarySijunDescriptor : AnniversaryZhangJiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun; }
internal sealed class AnniversaryTianjieDescriptor : AnniversaryZhangJiaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}
internal sealed class AnniversaryZhangJiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryZhangJiaoHost>().Resolve(f, e);
}
internal static class AnniversaryZhangJiaoComposition
{
    [ProgramCatalogValidation(39400)]
    internal static void Validate(string path, SkillProgram p)
    {
        var ops = new[] { SkillProgramEffectOp.ResolveAnniversaryZhangJiaoYizhao, SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSanshou,
            SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun, SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie };
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => ops.Contains(e.Op))) return;
        static bool One(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Effects is [{ Target: SkillProgramEffectTarget.Owner } e] && e.Op == op && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && (p.Id switch
        {
            "anniversary-god:yizhao" => p.Triggers.Count == 3 &&
                p.Triggers.All(t => !t.Optional && t.IsSkillInvocation && One(t, SkillProgramEffectOp.ResolveAnniversaryZhangJiaoYizhao)) &&
                p.Triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor) == 1 &&
                p.Triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardResponseAccepted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor) == 1 &&
                p.Triggers.Count(t => t.Window == SkillProgramTriggerWindow.DiscardPileReceived && t.MovementDiscardOnly && t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Own && t.MovementOccurrence == SkillProgramMovementOccurrence.PerCard) == 1,
            "happy:yizhao" => p.Triggers.Count == 2 &&
                p.Triggers.All(t => !t.Optional && t.IsSkillInvocation && One(t, SkillProgramEffectOp.ResolveAnniversaryZhangJiaoYizhao)) &&
                p.Triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor) == 1 &&
                p.Triggers.Count(t => t.Window == SkillProgramTriggerWindow.CardResponseAccepted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor) == 1,
            "anniversary-god:sanshou" or "happy:sanshou" => p.Triggers is [{ Window: SkillProgramTriggerWindow.BeforeDamageApplied, Optional: true, IsSkillInvocation: true,
                Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSanshou, SourceBind: null },
                    { Op: SkillProgramEffectOp.RevealTopCards, Amount: 3, ResultBind: "sanshou-top", Visibility: SkillProgramCardSetVisibility.Public },
                    { Op: SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSanshou, SourceBind: "sanshou-top" },
                    { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "sanshou-top", Destination: SkillProgramCardDestination.DiscardPile }] }],
            "anniversary-god:sijun" or "happy:sijun" => p.Triggers is [{ Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Optional: true, IsSkillInvocation: true } t] && One(t, SkillProgramEffectOp.ResolveAnniversaryZhangJiaoSijun),
            "anniversary-god:tianjie" or "happy:tianjie" => p.Triggers.Count == 2 && p.Triggers.All(t => t.Window == SkillProgramTriggerWindow.TurnEnding && t.Optional && t.IsSkillInvocation && One(t, SkillProgramEffectOp.ResolveAnniversaryZhangJiaoTianjie)) &&
                p.Triggers.Select(t => t.TurnOwnerScope).ToHashSet().SetEquals([SkillProgramTurnOwnerScope.Own, SkillProgramTurnOwnerScope.OtherLiving]),
            _ => false
        });
        if (!valid) throw new InvalidOperationException(path + ": Anniversary Zhang Jiao requires owned per-card discards, the actual-use snapshot/reveal/prevent/cleanup chain, random preparation payment and every-turn shuffle damage.");
    }
}
