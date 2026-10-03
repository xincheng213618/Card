namespace CardGame.Core;

public enum ProgramDualColorDuelStage { ChoosingOwner, ChoosingOther, PaymentChildren, DuelIssued }

public sealed record ProgramDualColorDuelDraft(int InstructionIndex, int OtherSeat, int TurnNumber,
    ProgramDualColorDuelStage Stage, bool? OwnerIsRed, bool? OtherIsRed,
    IReadOnlyList<int> OwnerCardIds, IReadOnlyList<int> OtherCardIds,
    long PaymentStartSequence = 0, long PaymentEndSequence = 0, long? CardUseFrameId = null);

public sealed record ProgramDualColorDuelOrigin(long ParentProgramFrameId, int InstructionIndex,
    long CardUseFrameId, int OwnerSeat, int OtherSeat, int InitialActorSeat, int InitialTargetSeat,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, int TurnNumber,
    int OwnerDiscardCount, int OtherDiscardCount, long PaymentStartSequence, long PaymentEndSequence,
    bool AttackStarted = false);

// The first commitment stays on its private owning frame. These facts only exist
// after both commitments, and contain no concealed physical-card identifiers.
public sealed record ProgramDualColorsCommittedEvent(long FrameId, string SkillId, int OwnerSeat,
    int OtherSeat, bool OwnerIsRed, bool OtherIsRed) : IGameEvent;
public sealed record ProgramDualColorCardsDiscardedEvent(long FrameId, string SkillId, int OwnerSeat,
    int OtherSeat, int OwnerCount, int OtherCount) : IGameEvent;
public sealed record ProgramDualColorDuelIssuedEvent(long FrameId, long CardUseFrameId, string SkillId,
    int OwnerSeat, int ActorSeat, int TargetSeat, int OwnerDiscardCount, int OtherDiscardCount) : IGameEvent;

internal interface IDualColorDuelProgramHost
{
    SkillProgramStepOutcome ChoosePrivateColorsDiscardAndDuel(ProgramSkillFrame frame);
}

internal sealed class ChoosePrivateColorsDiscardAndDuelDescriptor : ProgramOperationDescriptorBase, IActualPlayPhaseUseLedgerOperation
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChoosePrivateColorsDiscardAndDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new DualColorDuelHandler();
    public override ProgramOperationLegalityPolicy LegalityPolicy => ProgramOperationLegalityPolicy.OtherRecipient;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.DualColorDuel());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("Private color comparison is owned by its initiating program.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];

    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCardCount, bool selectedTarget, int targetSetMaximum)
    {
        if (effects.Any(effect => effect.Op == SkillProgramEffectOp.ChoosePrivateColorsDiscardAndDuel) &&
            (window is not null || selectedCardCount != 0 || !selectedTarget || targetSetMaximum != 0 || effects.Count != 1))
            throw new InvalidOperationException($"Invalid skill program at {path}: private color Duel requires one standalone zero-card other-target activation.");
    }
}

internal sealed class DualColorDuelHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChoosePrivateColorsDiscardAndDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IDualColorDuelProgramHost)host).ChoosePrivateColorsDiscardAndDuel(frame);
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void DualColorDuel()
    {
        // Activation uses public counts only. Actual color selection separately
        // sees that chooser's own hand; the opponent's colors remain unknown.
        var other = _publicContext.SelectedTarget?.HandCount ?? 3;
        _ownerDraw -= _player.HandCount * 0.4d;
        _targetDraw -= other * 0.4d;
        _ownerHpLoss += 0.25d;
        _targetHpLoss += 0.5d;
    }
}
