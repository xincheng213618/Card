using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record CategoryTalismanPlacedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int HolderSeat, int CardId, SkillProgramCardCategory Category, string GrantId, PublicPersistentPileSource Pile,
    long Before, long After) : IGameEvent;
public sealed record CategoryTalismanReleasedEvent(long FrameId, long PlacementFrameId, int HolderSeat,
    int CardId, long Before, long After) : IGameEvent;
public sealed record CategoryTalismanDiscardRecordedEvent(ActualDiscardRecoveryPhaseKey Phase,
    int OwnerSeat, int CardId, long MovementSequence, SkillProgramCardCategory Category) : IGameEvent;
public sealed record ZhangLingBindingIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long? DamageFrameId, int? DamageSourceSeat) : IGameEvent;
public sealed record ZhangLingDamageAppliedEvent(long DamageFrameId, int? SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record CategoryTalismanMovementReceipt(SkillProgramEffectOp Operation, int InstructionIndex,
    int CardId, CardLocation From, CardLocation To, long Before, long After, string? JudgmentBind = null);
public sealed record HuqiSlashReceipt(int InstructionIndex, int SourceSeat, Suit FrozenJudgmentSuit,
    long? SuspendedAttackId, long? SuspendedDuelId, long ChildUseId);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CategoryTalismanMovementReceipt? CategoryTalismanMovement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HuqiSlashReceipt? HuqiSlash { get; init; }
}

internal interface IZhangLingProgramHost
{
    SkillProgramStepOutcome PlaceCategoryTalisman(ProgramSkillFrame frame, string bind);
    SkillProgramStepOutcome ReleaseCategoryTalisman(ProgramSkillFrame frame);
    SkillProgramStepOutcome UseHuqiSlash(ProgramSkillFrame frame, string bind);
}
internal abstract class ZhangLingOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new ZhangLingOperationHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        if (Op == SkillProgramEffectOp.ReleaseCategoryTalisman) return ParseOwnerAlways(reader);
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("A category talisman/judgment operation is owner scoped.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, reader.Path); return effect;
    }
}
internal sealed class PlaceBoundHandAsCategoryTalismanDescriptor : ZhangLingOperationDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceBoundHandAsCategoryTalisman;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSingleCardSet(effect.SourceBind!), new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand]), new ReadSelectedTarget(), new GiftCardSet(effect.SourceBind!)];
}
internal sealed class ReleaseCategoryTalismanDescriptor : ZhangLingOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReleaseCategoryTalisman;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class UseRedJudgmentSlashAtDamageSourceDescriptor : ZhangLingOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseRedJudgmentSlashAtDamageSource;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!), new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied),
            new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}
internal sealed class ZhangLingOperationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        var capability = host.GetCapability<IZhangLingProgramHost>();
        return op switch
        {
            SkillProgramEffectOp.PlaceBoundHandAsCategoryTalisman => capability.PlaceCategoryTalisman(frame, effect.SourceBind!),
            SkillProgramEffectOp.ReleaseCategoryTalisman => capability.ReleaseCategoryTalisman(frame),
            SkillProgramEffectOp.UseRedJudgmentSlashAtDamageSource => capability.UseHuqiSlash(frame, effect.SourceBind!),
            _ => throw new InvalidOperationException("Unknown category talisman operation.")
        };
    }
}
internal interface ICategoryTalismanSelectionQuery : IProgramSelectionQuery
{
    IReadOnlyList<int> EligibleRecipients(int ownerSeat);
}
[ProgramTargetResolver(SkillProgramTargetKind.OtherLivingWithoutCategoryTalisman)]
internal sealed class CategoryTalismanTargetResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) =>
        queries.Get<ICategoryTalismanSelectionQuery>().EligibleRecipients(context.OwnerSeat);
}
