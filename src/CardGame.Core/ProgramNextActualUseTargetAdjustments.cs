namespace CardGame.Core;

// These are scalar, public facts. The owning program is the issuance identity;
// actual-turn history supplies one latest token, never a second pending use.
public sealed record NextActualUseTargetAdjustmentGrantedEvent(
    long ProgramFrameId, int InstructionIndex, int TurnNumber, int TurnOwnerSeat,
    CardConversionSource Source, string GameplayHash, string DisabledStateId) : IGameEvent;

public sealed record NextActualUseTargetAdjustmentConsumedEvent(
    long GrantProgramFrameId, int TurnNumber, int TurnOwnerSeat,
    CardConversionSource Source, long OriginFrameId, long? CardActionId,
    CardKind EffectiveKind, bool IsNullificationUse) : IGameEvent;

public enum ProgramNextActualUseAdjustmentKind { AddSlashTarget, RemoveGlobalTarget, AddRecoveryTarget }
public enum ProgramNextActualUseAdjustmentStage { Selected, Applied, Canceled }

// A selection is held only by the activation that will produce the use.
// The two targets are scalar because these mature producers start with either
// one Slash target or the card's automatic global targets.
public sealed record ProgramNextActualUseAdjustment(
    NextActualUseTargetAdjustmentGrantedEvent Grant, ProgramNextActualUseAdjustmentKind Kind,
    CardKind OutputKind, int OriginalTargetSeat, int ChangedTargetSeat,
    ProgramNextActualUseAdjustmentStage Stage = ProgramNextActualUseAdjustmentStage.Selected,
    long? CardUseFrameId = null);

public sealed record ProgramAdjustedSlashReturn(
    long? ParentProgramFrameId, long GrantProgramFrameId, long CardUseFrameId,
    string? ParentGameplayHash, string? ParentSkillInstanceId, bool IsZhangba = false,
    int PaidMovementFirstSequence = 0, int PaidMovementLastSequence = 0);

internal interface INextActualUseTargetAdjustmentProgramHost
{
    void GrantNextActualUseTargetAdjustment(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class GrantNextActualUseTargetAdjustmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantNextActualUseTargetAdjustment;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantNextActualUseTargetAdjustmentHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "stateId", "condition");
        var condition = reader.Condition();
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner ||
            condition.Kind != SkillProgramConditionKind.PindianWon)
            throw new InvalidOperationException("A next actual-use adjustment requires its owner's won Pindian result.");
        return new(Op, SkillProgramEffectTarget.Owner, 1, condition,
            stateId: reader.RequiredIdentifier("stateId"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];

    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCards, bool selectedTarget,
        SkillProgramTargetKind? targetKind, IReadOnlyList<CardZoneKind>? zones, int? minimumCards)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.GrantNextActualUseTargetAdjustment)) return;
        if (window is not null || selectedCards != 1 || minimumCards != 1 || !selectedTarget ||
            targetKind != SkillProgramTargetKind.OtherLivingWithHand || zones is null ||
            !zones.SequenceEqual([CardZoneKind.Hand]) || effects.FirstOrDefault()?.Op != SkillProgramEffectOp.Pindian ||
            effects.Count(e => e.Op == SkillProgramEffectOp.Pindian) != 1)
            throw new InvalidOperationException($"Invalid skill program at {path}: next actual-use adjustment requires one paid hand-card Pindian activation.");
    }
}

public sealed class GrantNextActualUseTargetAdjustmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantNextActualUseTargetAdjustment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        ((INextActualUseTargetAdjustmentProgramHost)host).GrantNextActualUseTargetAdjustment(frame, effect);
        return SkillProgramStepOutcome.Continue;
    }
}
