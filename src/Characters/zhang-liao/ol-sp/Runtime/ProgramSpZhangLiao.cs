using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ISpZhangLiaoProgramHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class SpZhangLiaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new SpZhangLiaoHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.SpZhangLiaoOpportunity(effect));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ResolveMubingRankExchange ? SkillProgramTriggerWindow.PlayPhaseStarting :
            Op == SkillProgramEffectOp.PreventDamageForHighestRankGift ? SkillProgramTriggerWindow.BeforeDamageApplied : SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class ResolveMubingRankExchangeDescriptor : SpZhangLiaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMubingRankExchange;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}
internal sealed class PreventDamageForHighestRankGiftDescriptor : SpZhangLiaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventDamageForHighestRankGift;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}
internal sealed class AwakenMubingDescriptor : SpZhangLiaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenMubing;
}
internal sealed class SpZhangLiaoHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<ISpZhangLiaoProgramHost>().Execute(frame, operation);
}
public enum SpZhangLiaoStage { Payment, PaymentChildren, Obtain, ObtainChildren, GiftCards, GiftTarget, GiftChildren, CleanupChildren, HighestCard, HighestChildren }
public sealed record SpZhangLiaoReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentFrameId, SpZhangLiaoStage Stage, bool Upgraded = false, int Budget = 0, int SpentRank = 0,
    int? VictimSeat = null, int SequenceBefore = 0, int SequenceAfter = 0)
{
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _obtained = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Obtained { get => _obtained; init => _obtained = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SpZhangLiaoReceipt? SpZhangLiao { get; init; }
}
public sealed record MubingCardsAcquiredEvent(long ProgramFrameId, CardConversionSource Source, int CardCount,
    int MilitaryCardCount, int PaymentRank, int ObtainedRank, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record DiaolingAwakenedEvent(long ProgramFrameId, int OwnerSeat, int MilitaryCardCount) : IGameEvent;
public sealed record ZiquDamagePreventedEvent(long ProgramFrameId, CardConversionSource Source, int VictimSeat,
    long BeforeDamageFrameId) : IGameEvent;

internal sealed partial class ProgramAiEstimateContext
{
    internal void SpZhangLiaoOpportunity(SkillProgramEffect effect)
    {
        // A bounded opportunity prior: no deck identities or another player's hand is inspected.
        // The actual exchange still chooses and pays only after the reveal.
        _otherAdjustment += effect.Op == SkillProgramEffectOp.ResolveMubingRankExchange ? 4d :
            effect.Op == SkillProgramEffectOp.PreventDamageForHighestRankGift ? 2d : 0d;
    }
}
