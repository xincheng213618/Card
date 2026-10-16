using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record QingyuanPartnerSelectedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int PartnerSeat, bool FirstDamage,
    long ParentId, long? DamageFrameId, int ActualDamage, long MovementSequence) : IGameEvent;
public sealed record QingyuanGainQualifiedEvent(long BatchId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int MovementIndex, int GainingSeat, long GainSequence) : IGameEvent;
public sealed record QingyuanFirstDamageEvent(int OwnerSeat, long DamageFrameId, long DamageParentId, int Amount) : IGameEvent;
public sealed record ZhongshenRoundBoundaryEvent(int RoundNumber, long MovementSequence) : IGameEvent;
public sealed record QingyuanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long ParentId, int ActualTurn, long? BatchId, int MovementIndex) : IGameEvent;
public sealed record QingyuanTakePaidEvent(long FrameId, int HolderSeat, int CardId, long Before, long After, int ActualTurn) : IGameEvent;
public sealed record QingyuanCompletedEvent(long FrameId) : IGameEvent;
public enum QingyuanStage { ChoosingPartner, TakeChildren }
public sealed record QingyuanReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex,
    long ParentId, int ActualTurn, QingyuanStage Stage, long? BatchId = null, int MovementIndex = -1,
    QingyuanFirstDamageEvent? Damage = null, int? HolderSeat = null, int? CardId = null, long Before = 0, long After = 0,
    IReadOnlyList<int>? HolderCandidates = null, IReadOnlyList<int>? HandCandidates = null, int HolderRandomIndex = -1, int CardRandomIndex = -1)
{
    private IReadOnlyList<int>? _holderCandidates = HolderCandidates is null ? null : Array.AsReadOnly(HolderCandidates.ToArray());
    public IReadOnlyList<int>? HolderCandidates { get => _holderCandidates; init => _holderCandidates = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int>? _handCandidates = HandCandidates is null ? null : Array.AsReadOnly(HandCandidates.ToArray());
    public IReadOnlyList<int>? HandCandidates { get => _handCandidates; init => _handCandidates = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public QingyuanReceipt? Qingyuan { get; init; }
}
internal interface IHuJinDingProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class HuJinDingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new HuJinDingHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(Op switch
    { SkillProgramEffectOp.SelectQingyuanOpeningPartner => SkillProgramTriggerWindow.GameStarting,
      SkillProgramEffectOp.SelectQingyuanFirstDamagePartner => SkillProgramTriggerWindow.AfterDamageApplied,
      _ => SkillProgramTriggerWindow.CardsGained })];
}
internal sealed class SelectQingyuanOpeningPartnerDescriptor : HuJinDingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectQingyuanOpeningPartner; }
internal sealed class SelectQingyuanFirstDamagePartnerDescriptor : HuJinDingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectQingyuanFirstDamagePartner; }
internal sealed class TakeRandomQingyuanHandAfterGainDescriptor : HuJinDingDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain; }
internal sealed class HuJinDingHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHuJinDingProgramHost>().Resolve(frame, op);
}
internal static class HuJinDingComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.SelectQingyuanOpeningPartner or SkillProgramEffectOp.SelectQingyuanFirstDamagePartner or SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain;
    internal static void Activation(string path, SkillProgramActivation a)
    { if (a.Effects.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": Qingyuan requires its native lifecycle or hand-gain trigger."); }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            !Handles(e.Op) || t.Optional || t.Subject != SkillProgramTriggerSubject.Owner || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null ||
            !(e.Op switch {
                SkillProgramEffectOp.SelectQingyuanOpeningPartner => t.Window == SkillProgramTriggerWindow.GameStarting && t.UsageLimit is null,
                SkillProgramEffectOp.SelectQingyuanFirstDamagePartner => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && t.UsageLimit is null,
                _ => t.Window == SkillProgramTriggerWindow.CardsGained && t.DestinationZones.SequenceEqual([CardZoneKind.Hand]) &&
                    t.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && t.UsageScope == SkillUsageScope.Turn && t.UsageLimit == 1 }))
            throw new InvalidOperationException(path + ": Qingyuan requires its complete mandatory opening, first actual damage, or once-per-actual-turn marked hand gain.");
    }
    internal static void ViewAs(string path, SkillProgramViewAs rule)
    {
        if (rule.RoundGainedRedHandOnly != true) return;
        if (rule.InputCount != 1 || !rule.SourceZones.SequenceEqual([CardZoneKind.Hand]) || rule.OutputKind != CardKind.Dodge || rule.ForPlay || !rule.ForResponse || !rule.UseOnly ||
            rule.InputKinds.Count != 0 || !rule.InputSuits.Order().SequenceEqual(new[] { Suit.Heart, Suit.Diamond }.Order()) || rule.UseEffectiveInputSuit != true ||
            rule.VariableInputCount || rule.AllowChainedInput || rule.ExtendedUse || rule.SingleCardTrickUse || rule.CostDestination is not null || rule.AllowSameKind ||
            rule.UsesPerPhase is not null || rule.UsesPerActualTurn is not null || rule.UsageGroup is not null || rule.TieredRoundConversion is not null ||
            rule.RoundDistinctBasicUse is not null || rule.DrawFundedDistinctBasic is not null || rule.DeclarationValidation is not null || rule.LastInSourceZone == true)
            throw new InvalidOperationException(path + ": Zhongshen requires one actual current-round gained red hand entity used as Dodge without another quota, cost or response direction.");
    }
}
