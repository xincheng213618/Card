using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IBoundaryQuanCongProgramHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame);
}

internal sealed class ResolveBoundaryQuanCongYaomingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming;
    public override ISkillProgramEffectHandler Handler { get; } = new BoundaryQuanCongYaomingHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(
        Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeDrawInvocation,
        Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeDrawInvocation);
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class BoundaryQuanCongYaomingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat,
        ISkillProgramEffectHost host) => host.GetCapability<IBoundaryQuanCongProgramHost>().Resolve(frame);
}

internal static class BoundaryQuanCongComposition
{
    internal const string Skill = "boundary:yaoming-mobile";
    internal const string Activation = "charged-intervention", Trigger = "recharge-and-intervene";

    [ProgramCatalogValidation(51200)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects))
                .Any(e => e.Op == SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming)) return;
        if (program.Id != Skill || program.Activations is not [{ Id: Activation, MinCards: 0, MaxCards: 0,
                MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming }] }] ||
            program.Triggers is not [{ Id: Trigger, Window: SkillProgramTriggerWindow.AfterDamageApplied,
                Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false,
                DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryQuanCongYaoming }] }])
            throw new InvalidOperationException(path + ": charged Yaoming requires its original phase action and one actual-damage recharge binding.");
    }
}

public enum BoundaryQuanCongYaomingStage { Option, Card, Drawing, Children, Complete }

public sealed record BoundaryQuanCongYaomingReceipt(
    CardConversionSource Source, string GameplayHash, long ParentWindowId, int Turn,
    BoundaryQuanCongYaomingStage Stage)
{
    public string? Option { get; init; }
    public int TargetSeat { get; init; } = -1;
    public int? DiscardedCardId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? DrawAttemptId { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundaryQuanCongYaomingReceipt? BoundaryQuanCongYaoming { get; init; }
}

public sealed record BoundaryQuanCongYaomingStateEvent(int OwnerSeat, string SkillInstanceId, long FrameId,
    string Cause, int Remaining, string? PendingOption, int DamageAmount = 0) : IGameEvent;
public sealed record BoundaryQuanCongYaomingStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long ParentWindowId, int Turn) : IGameEvent;
public sealed record BoundaryQuanCongYaomingSelectedEvent(long FrameId, string Option, int TargetSeat) : IGameEvent;
public sealed record BoundaryQuanCongYaomingDiscardedEvent(long FrameId, int TargetSeat, int CardId,
    long Before, long After) : IGameEvent;
public sealed record BoundaryQuanCongYaomingDrawReturnedEvent(long FrameId, int RecipientSeat,
    long? AttemptId, int ActualCount) : IGameEvent;
