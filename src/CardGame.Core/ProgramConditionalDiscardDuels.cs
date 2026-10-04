namespace CardGame.Core;

public enum ConditionalDiscardDuelStage { OwnerChoice, OwnerChildren, TargetChoice, TargetChildren, DuelIssued }
public sealed record ConditionalDiscardDuelPayment(int PayerSeat, int CardId, CardKind PrintedKind,
    CardLocation From, long SequenceBefore, long SequenceAfter);
public sealed record ConditionalDiscardDuelDraft(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, int TargetSeat, ConditionalDiscardDuelStage Stage,
    ConditionalDiscardDuelPayment? OwnerPayment = null, ConditionalDiscardDuelPayment? TargetPayment = null,
    long? CardUseFrameId = null, int? OwnerHpAtIssue = null, int? TargetHpAtIssue = null);
public sealed record ConditionalDiscardDuelOrigin(long ParentProgramFrameId, int InstructionIndex, long CardUseFrameId,
    CardConversionSource Source, string GameplayHash, int TurnNumber, int TurnOwnerSeat, int InitialActorSeat,
    int InitialTargetSeat, int OwnerHpAtIssue, int TargetHpAtIssue, bool AttackStarted = false);
// No unpublished hand-card identity enters public events. Paid entities are proven in the owning receipt and movement ledger.
public sealed record ConditionalDiscardDuelStartedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, int TurnNumber, int TurnOwnerSeat, int TargetSeat) : IGameEvent;
public sealed record ConditionalDiscardDuelPaidEvent(long ProgramFrameId, int PayerSeat, bool OwnerCost,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record ConditionalDiscardDuelIssuedEvent(ConditionalDiscardDuelOrigin Origin) : IGameEvent;
public sealed record ConditionalDiscardDuelFinishedEvent(long ProgramFrameId, bool Issued) : IGameEvent;

internal interface IConditionalDiscardDuelProgramHost
{
    SkillProgramStepOutcome DiscardSlashThenOtherCardAndUseDuel(ProgramSkillFrame frame);
}
internal sealed class DiscardSlashThenOtherCardAndUseDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardSlashThenOtherCardAndUseDuelHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.ConditionalDiscardDuel());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLivingInAttackRange)];
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void ConditionalDiscardDuel()
    {
        // Public priors only: one own Slash costs a card; the other chooser may pay a Slash and prevent the Duel.
        _otherAdjustment -= 8d;
        _targetAdjustment -= 8d;
        if (_publicContext.SelectedTarget is { } other && other.Hp >= _player.Hp)
        {
            var pSlash = Math.Clamp(other.HandCount * 0.12d, 0d, 0.8d);
            var pWin = Math.Clamp(0.5d + (_player.HandCount - 1 - other.HandCount) * 0.08d, 0.15d, 0.85d);
            _otherAdjustment -= (1d - pSlash) * (1d - pWin) * 12d;
            _targetAdjustment -= (1d - pSlash) * pWin * 20d;
        }
    }
}
public sealed class DiscardSlashThenOtherCardAndUseDuelHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IConditionalDiscardDuelProgramHost)host).DiscardSlashThenOtherCardAndUseDuel(frame);
}
internal static class ConditionalDiscardDuelComposition
{
    internal static void Validate(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel)) return;
        if (a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLivingInAttackRange || a.UsesPerTurn is not null ||
            a.UsesPerPhase is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always ||
            a.MarkerCost is not null || a.Effects is not [{ Op: SkillProgramEffectOp.DiscardSlashThenOtherCardAndUseDuel,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"{path}: conditional discard Duel needs an unconditional unlimited zero-card one-range-target activation; its two real costs are internal.");
    }
}
