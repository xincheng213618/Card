namespace CardGame.Core;

/// <summary>A private selection from the complete physical hand, before any card is moved.</summary>
public sealed record ProgramOwnedHandRankSumSelection(
    int InstructionIndex, int OwnerSeat, string SkillId, string BindingId,
    string SkillInstanceId, string GameplayHash, string ResultBind, int RequiredRankSum,
    IReadOnlyList<int> CandidateCardIds, IReadOnlyList<int> CandidateRanks,
    IReadOnlyList<int> SelectedCardIds);

internal sealed record RequireOwnedHandRankSumActivation : ProgramResourceOperation;

internal static class OwnedHandRankSumContract
{
    internal static void ValidateActivation(string path, IReadOnlyList<SkillProgramEffect> effects,
        int minCards, int maxCards, int minTargets, int maxTargets, SkillProgramTargetKind targetKind)
    {
        var selectors = effects.Where(e => e.Op == SkillProgramEffectOp.SelectOwnedHandRankSum).ToArray();
        if (selectors.Length == 0) return;
        if (selectors.Length != 1 || effects[0] != selectors[0] ||
            selectors[0].Condition.Kind != SkillProgramConditionKind.Always ||
            minCards != 0 || maxCards != 0 || minTargets != 1 || maxTargets != 1 ||
            targetKind != SkillProgramTargetKind.OtherLiving)
            throw new InvalidOperationException($"Invalid skill program at {path}: owned-hand rank-sum selection requires the first unconditional instruction of a zero-card, one-other-living-target activation.");
    }
}

internal sealed class SelectOwnedHandRankSumProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectOwnedHandRankSum;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectOwnedHandRankSumSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.SelectOwnedCards, static (e, c) => c.SelectOwnedHandRankSum(e));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "exactRankSum", "resultBind", "condition");
        var rankSum = r.RequiredInt("exactRankSum");
        if (rankSum is < 1 or > 208)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.exactRankSum: requires an integer from 1 through 208.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"), zones: [CardZoneKind.Hand])
        { ExactRankSum = rankSum };
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireOwnedHandRankSumActivation(), new CaptureSourceCard(effect.ResultBind!,
            effect.ExactRankSum!.Value, true, SkillProgramEffectTarget.Owner)];
}

public interface IOwnedHandRankSumHost
{
    SkillProgramStepOutcome BeginOwnedHandRankSum(ProgramSkillFrame frame, int exactRankSum, string resultBind);
}

public sealed class SelectOwnedHandRankSumSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectOwnedHandRankSum;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host is IOwnedHandRankSumHost selector
        ? selector.BeginOwnedHandRankSum(frame, effect.ExactRankSum!.Value, effect.ResultBind!)
        : throw new InvalidOperationException("The program host does not support owned-hand rank-sum selection.");
}
