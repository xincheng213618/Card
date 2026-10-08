using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ForeignSelectedCardSlashStage { ChoosingCard, IssuingUse, UseIssued, SettlementChildren, DrawChildren }

/// <summary>The original issuer owns the selected entity, real Use and its once-paid tail.</summary>
public sealed record ForeignSelectedCardSlashReceipt(
    int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int PhaseInstanceId, int ActorSeat, ForeignSelectedCardSlashStage Stage,
    int? CardId = null, CardKind? PrintedKind = null, CardLocation? From = null, bool GeneralWeapon = false,
    long PaymentBefore = 0, long PaymentAfter = 0, long? PaymentBatchId = null,
    ForeignSelectedCardSlashReturn? SlashReturn = null, bool DamagedIssuer = false,
    int RequestedDraw = 0, int ActualDraw = 0, long DrawBefore = 0, long DrawAfter = 0);

public sealed record ForeignSelectedCardSlashReturn(
    long ProgramFrameId, int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int PhaseInstanceId, int ActorSeat, int OriginalTargetSeat,
    int CardId, CardKind PrintedKind, CardLocation From, bool GeneralWeapon,
    long CardUseFrameId, long ActionId);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForeignSelectedCardSlashReceipt? ForeignSelectedCardSlash { get; init; }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ForeignSelectedCardSlashReturn? ForeignSelectedCardSlashReturn { get; init; }
}

// These facts and their nested return are scalar; they add no collection projection case.
public sealed record ForeignSelectedCardSlashStartedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int PhaseInstanceId, int ActorSeat) : IGameEvent;
public sealed record ForeignSelectedCardSlashIssuedEvent(ForeignSelectedCardSlashReturn Return) : IGameEvent;
public sealed record ForeignSelectedCardSlashPaidEvent(long ProgramFrameId, long CardUseFrameId, int CardId,
    CardKind PrintedKind, CardLocation From, CardLocation To, long Before, long After, long BatchId) : IGameEvent;
public sealed record ForeignSelectedCardSlashDamageRecordedEvent(long ProgramFrameId, long CardUseFrameId,
    long ActionId, long DamageFrameId, int SourceSeat, int TargetSeat, int Amount, bool SourceLess) : IGameEvent;
public sealed record ForeignSelectedCardSlashResolvedEvent(long ProgramFrameId, long CardUseFrameId,
    long ActionId, bool DamagedIssuer) : IGameEvent;
public sealed record ForeignSelectedCardSlashDrawIssuedEvent(long ProgramFrameId, long CardUseFrameId,
    int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record ForeignSelectedCardSlashFinishedEvent(long ProgramFrameId, bool UseIssued) : IGameEvent;

internal static class ForeignSelectedCardSlashContract
{
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (!activation.Effects.Any(e => e.Op == SkillProgramEffectOp.UseSelectedForeignCardAsSlash)) return;
        if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving || activation.UsesPerPhase != 1 ||
            activation.UsesPerTurn is not null || activation.UsesPerGame is not null || activation.MarkerCost is not null ||
            activation.Condition.Kind != SkillProgramConditionKind.Always || activation.ContinueAfterOwnerDeath ||
            activation.Effects is not [{ Op: SkillProgramEffectOp.UseSelectedForeignCardAsSlash,
                Target: SkillProgramEffectTarget.SelectedTarget, Condition.Kind: SkillProgramConditionKind.Always,
                TargetReference: null }])
            throw new InvalidOperationException($"Invalid skill program at {path}: a selected foreign-card Slash requires one unconditional, zero-card, one-other-target, once-per-Play activation.");
    }

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.UseSelectedForeignCardAsSlash))
            throw new InvalidOperationException($"Invalid skill program at {path}: a selected foreign-card Slash requires its own Play activation, not a trigger.");
    }
}

internal interface ISelectedForeignCardSlashHost
{
    SkillProgramStepOutcome UseSelectedForeignCardAsSlash(ProgramSkillFrame frame, int actorSeat);
}

internal sealed record RequireSelectedForeignCardSlashActivation : ProgramResourceOperation;

internal sealed class UseSelectedForeignCardAsSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseSelectedForeignCardAsSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new UseSelectedForeignCardAsSlashHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationLegalityPolicy LegalityPolicy => new(ExcludesOwnerAsTarget: true);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RequestSlashByTarget,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.target: a selected foreign-card Slash requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, target, 1, reader.Condition(), outputKind: CardKind.Slash,
            targetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget, useCardActionWindows: true);
        RequireAlways(effect, reader.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireSelectedForeignCardSlashActivation(), new ReadSelectedTarget()];
}

internal sealed class UseSelectedForeignCardAsSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UseSelectedForeignCardAsSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host is ISelectedForeignCardSlashHost selected
        ? selected.UseSelectedForeignCardAsSlash(frame, targetSeat)
        : throw new InvalidOperationException("A selected foreign-card Slash requires its owning program host.");
}
