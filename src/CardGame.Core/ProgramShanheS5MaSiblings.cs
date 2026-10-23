using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IShanheS5TieqiProgramHost
{
    SkillProgramStepOutcome SuppressAndJudgeAllColor(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class SuppressCurrentSlashTargetAndJudgeColorAllDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction | ProgramContextCapability.Judgment;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ProhibitCurrentResponse,
        static (_, context) => context.ProhibitCurrentResponse());
    public override ISkillProgramEffectHandler Handler { get; } = new ShanheS5TieqiHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "judgmentReason", "resultBind", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: color Tieqi requires its owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, reader.Condition(),
            judgmentReason: reader.RequiredIdentifier("judgmentReason"), resultBind: reader.RequiredIdentifier("resultBind"),
            visibility: SkillProgramCardSetVisibility.Public);
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashBeforeResponse), new RequireCardActionActor(),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
}

internal sealed class ShanheS5TieqiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int subject, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheS5TieqiProgramHost>().SuppressAndJudgeAllColor(frame, effect);
}

public sealed record ShanheS5TieqiPayment(int Before, int After, long BatchId,
    IReadOnlyList<int> CardIds, IReadOnlyList<CardLocation> From)
{
    private readonly IReadOnlyList<int> _ids = Array.AsReadOnly(CardIds.ToArray());
    private readonly IReadOnlyList<CardLocation> _from = Array.AsReadOnly(From.ToArray());
    public IReadOnlyList<int> CardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5TieqiPayment? ShanheS5Tieqi { get; init; }
}

public sealed record ShanheS5TieqiPaidEvent(long FrameId, long CardUseFrameId, int TargetSeat,
    int Count, int Before, int After) : IGameEvent;

internal static class ShanheS5MaSiblingsContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard)) return;
        if (trigger.Window != SkillProgramTriggerWindow.SlashBeforeResponse || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            !trigger.Optional || trigger.Effects is not [{ Op: SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard }] ||
            trigger.CardKinds.Count != 3 || !new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }.All(trigger.CardKinds.Contains))
            throw new InvalidOperationException($"Invalid skill program at {path}: color Tieqi requires one optional actor Slash-family response-window operation.");
    }
}
