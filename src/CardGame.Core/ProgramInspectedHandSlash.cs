using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum InspectedHandSlashStage { HpChildren, Viewing, DiscardChildren, SlashIssued }
public sealed record InspectedHandSlashDraft(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, int TargetSeat, int HpBefore, int HpAfter, InspectedHandSlashStage Stage,
    IReadOnlyList<int> ViewedCardIds, bool ContainsPrintedDodge = false, int? DiscardedCardId = null,
    long SequenceBefore = 0, long SequenceAfter = 0, InspectedHandSlashReturn? SlashReturn = null)
{
    private readonly IReadOnlyList<int> _viewedCardIds = Array.AsReadOnly(ViewedCardIds.ToArray());
    public IReadOnlyList<int> ViewedCardIds { get => _viewedCardIds; init => _viewedCardIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record InspectedHandSlashReturn(long ProgramFrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, int TurnNumber, int TurnOwnerSeat, int OriginalTargetSeat, long CardUseFrameId, long ActionId);
public sealed record InspectedHandDistanceGrant(long GrantSequence, long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, int TurnNumber, int TurnOwnerSeat, int TargetSeat);
public sealed record InspectedHandHpPaidEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, int TargetSeat, int HpBefore, int HpAfter) : IGameEvent;
public sealed record InspectedHandViewedEvent(long ProgramFrameId, int ViewerSeat, int TargetSeat, int Count) : IGameEvent;
public sealed record InspectedHandDiscardPaidEvent(long ProgramFrameId, int TargetSeat, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record InspectedHandSlashIssuedEvent(InspectedHandSlashReturn Return) : IGameEvent;
public sealed record InspectedHandDistanceGrantedEvent(InspectedHandDistanceGrant Grant) : IGameEvent;
public sealed record InspectedHandFinishedEvent(long ProgramFrameId, bool IssuedSlash) : IGameEvent;

internal interface IInspectedHandSlashProgramHost { SkillProgramStepOutcome PayHpInspectHandThenDiscardOrSlash(ProgramSkillFrame frame); }
internal sealed class PayHpInspectHandThenDiscardOrSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new PayHpInspectHandThenDiscardOrSlashHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.InspectedHandSlash());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLivingWithHand)];
}
public sealed class PayHpInspectHandThenDiscardOrSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        ((IInspectedHandSlashProgramHost)host).PayHpInspectHandThenDiscardOrSlash(f);
}
internal static class InspectedHandSlashComposition
{
    internal static void Validate(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash)) return;
        if (a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLivingWithHand || a.UsesPerPhase != 1 || a.UsesPerTurn is not null ||
            a.UsesPerGame is not null || a.MarkerCost is not null || a.Condition.Kind != SkillProgramConditionKind.Always ||
            a.Effects is not [{ Op: SkillProgramEffectOp.PayHpInspectHandThenDiscardOrSlash,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": HP hand inspection requires its exact zero-card one-hand-target once-per-phase activation.");
    }
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void InspectedHandSlash()
    {
        // Public hand count is a prior, never an opponent-hand identity query.
        _otherAdjustment -= _player.Hp <= 1 ? 60d : 14d;
        _targetAdjustment -= 22d;
    }
}
internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<InspectedHandDistanceGrant> _inspectedHandDistances = [];
    internal IReadOnlyList<InspectedHandDistanceGrant> InspectedHandDistances => _inspectedHandDistances.AsReadOnly();
    internal InspectedHandDistanceGrant GrantInspectedHandDistance(long program, int instruction,
        CardConversionSource source, string hash, int turn, int owner, int target)
    {
        var prior = _inspectedHandDistances.SingleOrDefault(g => g.ProgramFrameId == program && g.InstructionIndex == instruction);
        if (prior is not null)
        {
            if (prior.Source != source || prior.GameplayHash != hash || prior.TurnNumber != turn || prior.TurnOwnerSeat != owner || prior.TargetSeat != target)
                throw new InvalidOperationException("An inspected-hand distance grant changed its exact paid origin.");
            return prior;
        }
        var grant = new InspectedHandDistanceGrant(++_grantSequence, program, instruction, source, hash, turn, owner, target);
        _inspectedHandDistances.Add(grant); return grant;
    }
    internal IEnumerable<long> ExpiringInspectedHandDistances(int turn, int owner) =>
        _inspectedHandDistances.Where(g => g.TurnNumber == turn && g.TurnOwnerSeat == owner).Select(g => g.GrantSequence);
    internal void ExpireInspectedHandDistances(IReadOnlySet<long> expired) => _inspectedHandDistances.RemoveAll(g => expired.Contains(g.GrantSequence));
}
