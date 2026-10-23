using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MobileShiDuzuoGrantIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int RecipientSeat, int CardId, bool FromDiscardPile, int BeforeSequence, int AfterSequence) : IGameEvent;

public sealed record MobileShiBihanGuardIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int DamageTargetSeat, int DiscarderSeat, int PlannedDiscardCount) : IGameEvent;

public sealed record MobileShiBihanGuardCompletedEvent(long FrameId, CardConversionSource Source,
    int DamageTargetSeat, int DiscarderSeat, int DiscardedCount, bool DamageReduced) : IGameEvent;

public sealed record MobileShiChengPuReceipt(string Operation, int ActualTurn, int Stage = 0, int TargetSeat = -1,
    int DamageTargetSeat = -1, int Discarded = 0, long CardId = 0, int Before = 0);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiChengPuReceipt? MobileShiChengPu { get; init; }
}

internal static class MobileShiChengPuComposition
{
    internal const string DuzuoSkill = "mobile:shi-duzuo";
    internal const string BihanSkill = "mobile:shi-bihan";
    internal const string DuzuoGrantTrigger = "duzuo-grant";
    internal const string BihanGuardTrigger = "bihan-guard";
    internal const string DuzuoGrantMoveReason = "skill-program.mobile:shi-duzuo.fire-slash-grant";
    internal const string BihanDiscardMoveReason = "skill-program.mobile:shi-bihan.discard";

    [ProgramCatalogValidation(67300)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == DuzuoSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.Activations.Count != 0 || program.BooleanStates.Count != 0 ||
                program.Triggers is not [{ Id: DuzuoGrantTrigger }] ||
                program.Triggers[0] is not { Window: SkillProgramTriggerWindow.CardsGained, Subject: SkillProgramTriggerSubject.Owner,
                    Optional: true, MovementOccurrence: SkillProgramMovementOccurrence.PerBatch } ||
                program.Triggers[0].DestinationZones is not [CardZoneKind.Hand] ||
                program.Triggers[0].ExcludedMovementReasons is not [DuzuoGrantMoveReason] ||
                program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiDuzuoGrant, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Duzuo requires exactly its own-reason-excluded per-batch hand-gain trigger.");
            return;
        }
        if (program.Id != BihanSkill) return;
        if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.Contributions.Count != 0 || program.Activations.Count != 0 || program.BooleanStates.Count != 0 ||
            program.Triggers is not [{ Id: BihanGuardTrigger }] ||
            program.Triggers[0] is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, Subject: SkillProgramTriggerSubject.Any,
                Optional: true, Condition: { Kind: SkillProgramTriggerConditionKind.DamageCardIsSlash } } ||
            program.Triggers[0].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiBihanGuard, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path + ": Bihan requires exactly its slash-damage guard trigger.");
    }
}

internal interface IMobileShiChengPuProgramHost
{
    SkillProgramStepOutcome ResolveDuzuoGrant(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveBihanGuard(ProgramSkillFrame frame);
}

internal sealed class ResolveMobileShiDuzuoGrantDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiDuzuoGrant;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiChengPuHandler(SkillProgramEffectOp.ResolveMobileShiDuzuoGrant);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class ResolveMobileShiBihanGuardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiBihanGuard;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiChengPuHandler(SkillProgramEffectOp.ResolveMobileShiBihanGuard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}

internal sealed class MobileShiChengPuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiBihanGuard => host.GetCapability<IMobileShiChengPuProgramHost>().ResolveBihanGuard(frame),
        _ => host.GetCapability<IMobileShiChengPuProgramHost>().ResolveDuzuoGrant(frame)
    };
}
