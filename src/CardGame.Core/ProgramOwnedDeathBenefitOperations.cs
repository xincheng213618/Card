namespace CardGame.Core;

public sealed record OwnedDeathCursor(long FrameId, long ParentFrameId, int VictimSeat, int? KillerSeat,
    DeathReturnKind? ReturnKind, ResolutionFrameStep Step, bool OwnerDiedProgramsResolved,
    bool KillerProgramsResolved, IReadOnlyList<int> CleanedUpCardIds)
{
    private IReadOnlyList<int> _cleanedUpCardIds = Array.AsReadOnly(CleanedUpCardIds.ToArray());
    public IReadOnlyList<int> CleanedUpCardIds { get => _cleanedUpCardIds; init => _cleanedUpCardIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed record OwnedDeadDyingCursor(long FrameId, long ParentFrameId, int VictimSeat, int? KillerSeat,
    DyingContinuationKind Continuation, ResolutionFrameStep Step, int ResponderIndex,
    IReadOnlyList<int> ResponderSeats, IReadOnlyList<string> AttemptedSelfDyingBindings)
{
    private IReadOnlyList<int> _responderSeats = Array.AsReadOnly(ResponderSeats.ToArray());
    public IReadOnlyList<int> ResponderSeats { get => _responderSeats; init => _responderSeats = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<string> _attemptedSelfDyingBindings = Array.AsReadOnly(AttemptedSelfDyingBindings.ToArray());
    public IReadOnlyList<string> AttemptedSelfDyingBindings { get => _attemptedSelfDyingBindings; init => _attemptedSelfDyingBindings = Array.AsReadOnly(value.ToArray()); }
}

/// <summary>Only the dead original victim is suspended; no pending state lives outside this owning program.</summary>
public sealed record OwnedOriginalDamageCursor(long FrameId, long ParentFrameId, int SourceSeat, int TargetSeat,
    int Amount, DamageNature Nature, ResolutionFrameStep Step);

public sealed record ProgramOwnedDeathBenefitReturn(long ProgramFrameId, long OwnerDeathWindowFrameId,
    int CandidateIndex, ResolutionFrameStep WindowStep, ProgramTriggerCandidate Candidate,
    OwnedDeathCursor OriginalDeath, OwnedDeadDyingCursor? OriginalDying,
    OwnedOriginalDamageCursor? OriginalDamage, long? OriginalAttackOwnerFrameId, int ActualTurnNumber, int ActualTurnOwnerSeat);

public sealed record OwnedDeathBenefitReturnIssuedEvent(long ProgramFrameId, long OwnerDeathWindowFrameId,
    long OriginalDeathFrameId, long? OriginalDyingFrameId, int OwnerSeat, int RecipientSeat,
    CardConversionSource Source, string GameplayHash, string FrozenReturnHash) : IGameEvent;
public sealed record OwnedDeathBenefitReturnedEvent(long ProgramFrameId, long OwnerDeathWindowFrameId,
    long OriginalDeathFrameId, long? OriginalDyingFrameId, bool Completed) : IGameEvent;

internal interface IOwnedDeathBenefitProgramHost
{
    SkillProgramStepOutcome SelectIssuedFixedRecipientWithDeathReturn(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class SelectIssuedFixedRecipientWithDeathReturnDescriptor : PairObtainFixedRecipientDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectIssuedFixedRecipientWithDeathReturnHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied), new SelectSingleTarget()];
    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCardCount, bool selectedTarget, int initialTargetSetMaximum)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn)) return;
        if (window != SkillProgramTriggerWindow.OwnerDied || selectedCardCount != 0 || selectedTarget || initialTargetSetMaximum != 0 ||
            effects is not [{ Op: SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn, Target: SkillProgramEffectTarget.Owner },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 3, NumberExpression: null },
                { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1, NumberExpression: null }] ||
            effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException($"{path}: owned death return requires exact original-recipient/Draw3/Recover1 without another input or target selection.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn) &&
            (trigger.Window != SkillProgramTriggerWindow.OwnerDied || trigger.Subject != SkillProgramTriggerSubject.Owner ||
             !trigger.Optional || trigger.UsageScope is not null || trigger.ChoiceGroup is not null || trigger.NamedUsageGroup is not null))
            throw new InvalidOperationException($"{path}: owned death return is one optional original-owner binding, not another limited debit.");
    }
}

public sealed class SelectIssuedFixedRecipientWithDeathReturnHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectIssuedFixedRecipientWithDeathReturn;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IOwnedDeathBenefitProgramHost)host).SelectIssuedFixedRecipientWithDeathReturn(frame, effect);
}
