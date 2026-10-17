using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum PingluStage { ObtainChildren }
public sealed record PingluTargetSample(int Index, int TargetSeat, int HandCount, int? CardId,
    CardKind? PrintedKind, CardLocation? From, uint RandomBefore, uint RandomAfter);
public sealed record PingluReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int InstructionIndex, int ActualTurn, int TurnOwner,
    long SequenceBefore)
{
    public PingluStage Stage { get; init; }
    private readonly IReadOnlyList<PingluTargetSample> _targets = Array.AsReadOnly(Array.Empty<PingluTargetSample>());
    public IReadOnlyList<PingluTargetSample> Targets
    { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public long BatchId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int ActualCount { get; init; } = -1;
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PingluReceipt? Pinglu { get; init; }
}

public sealed record PingluStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner, int TargetCount, long SequenceBefore) : IGameEvent;
public sealed record PingluTargetSampledEvent(long FrameId, int Index, int TargetSeat, int HandCount,
    int? CardId, CardKind? PrintedKind, CardLocation? From, uint RandomBefore, uint RandomAfter,
    long CaptureSequence) : IGameEvent;
public sealed record PingluEntityObtainedEvent(long FrameId, int Index, int TargetSeat, int CardId,
    CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence, long BatchId) : IGameEvent;
public sealed record PingluObtainPaidEvent(long FrameId, long BatchId, int PrimaryCount, int ActualCount,
    long Before, long After) : IGameEvent;
public sealed record PingluCompletedEvent(long FrameId, int ActualCount) : IGameEvent;

internal interface ILeZhouYuPingluHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolvePingluInRangeRandomHandsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePingluInRangeRandomHands;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new PingluHandler();
}
internal sealed class PingluHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePingluInRangeRandomHands;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<ILeZhouYuPingluHost>().Resolve(frame);
}
internal static class PingluComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:pinglu" && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolvePingluInRangeRandomHands)) return;
        if (p.Id != "le:pinglu" || p.Activations is not [var a] || p.Triggers.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            a.Id != "all-in-range-random-hand" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null || a.UsageGroup != a.Id ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null || a.CategoryTargetLedgerId is not null ||
            a.TargetPhaseLedgerId is not null || a.CardCountExpression is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 ||
            a.TargetRequiresEmptyEquipmentSlot || a.OtherCharactersOnly || a.ContinueAfterOwnerDeath ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolvePingluInRangeRandomHands, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Pinglu requires the complete automatic all-in-range original Hand obtain and phase-local custody latch, with no selected cards, targets or generic usage limit.");
    }
}
