using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MoQuanyuSoleUse(long UseId, long ActionId, long WindowId,
    int ActorSeat, int TargetSeat, CardKind Kind);

public sealed record MoQuanyuQuotaReceipt(long UseId, long ActionId, int ActorSeat,
    int OriginalTargetSeat, CardKind Kind, CardConversionSource Source,
    string GameplayHash, int ChoiceMask, bool Entered);

public enum MoQuanyuSlashStage { AdditionalTarget, Applied }

public sealed record MoQuanyuSlashReceipt(CardConversionSource Source, string GameplayHash,
    MoQuanyuSoleUse Original, int Mask, bool Entered, MoQuanyuSlashStage Stage)
{
    public int? AddedTargetSeat { get; init; }
    private readonly IReadOnlyList<PromptChoice> _choices = Array.AsReadOnly(Array.Empty<PromptChoice>());
    public IReadOnlyList<PromptChoice> Choices
    { get => _choices; init => _choices = Array.AsReadOnly(value.Select(c => c with
        { Cards = Array.AsReadOnly(c.Cards.ToArray()), Targets = Array.AsReadOnly(c.Targets.ToArray()),
          Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
              c.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)),
          ContentIds = Array.AsReadOnly(c.ContentIds.ToArray()) }).ToArray()); }
}

public sealed record MoQuanyuExtraRound(long ActionId, long ProgramFrameId,
    CardConversionSource Source, string GameplayHash, ExtraCardEffectState Native)
{
    public bool Started { get; init; }
    public bool Active { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoQuanyuSlashReceipt? MoQuanyuSlash { get; init; }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoQuanyuSoleUse? MoQuanyuSoleUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoQuanyuQuotaReceipt? MoQuanyuQuota { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoQuanyuSlashReceipt? MoQuanyuSlashPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoQuanyuExtraRound? MoQuanyuExtraRound { get; init; }
}

public sealed record MoQuanyuSoleUseCapturedEvent(MoQuanyuSoleUse Original) : IGameEvent;
public sealed record MoQuanyuQuotaAcceptedEvent(MoQuanyuQuotaReceipt Receipt) : IGameEvent;
public sealed record MoQuanyuSlashStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, MoQuanyuSoleUse Original, int Mask, bool Entered) : IGameEvent;
public sealed record MoQuanyuSlashAppliedEvent(long FrameId, long UseId, long ActionId,
    int Mask, int? AddedTargetSeat) : IGameEvent;
public sealed record MoQuanyuTargetAddedEvent(long FrameId, long UseId, long ActionId,
    int TargetIndex, int TargetSeat, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MoQuanyuExtraRoundIssuedEvent(long FrameId, long UseId, long ActionId,
    CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MoQuanyuExtraRoundStartedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MoQuanyuExtraRoundCompletedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;

internal interface IMoSunQuanSlashHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame);
}

internal sealed class ResolveMoSunQuanQuanyuSlashEffectsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSunQuanQuanyuSlashEffects;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new MoSunQuanSlashHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}

internal sealed class MoSunQuanSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSunQuanQuanyuSlashEffects;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<IMoSunQuanSlashHost>().Resolve(frame);
}

internal static class MoQuanyuSlashComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Id != "singleton-slash-effects" ||
            trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || trigger.Optional ||
            !trigger.SingleActionInstance || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            !trigger.CardKinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) ||
            trigger.CardCategories.Count != 0 || trigger.UsageLimit is not null || trigger.UsageScope is not null ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveMoSunQuanQuanyuSlashEffects,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Quanyu sword effects require the original finalized singleton native Slash and its exact mandatory effect.");
    }
}
