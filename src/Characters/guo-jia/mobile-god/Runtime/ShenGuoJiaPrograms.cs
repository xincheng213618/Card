using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ShenGuoJiaStage { Judging, JudgmentChildren, Repeat, GrowChildren, Recipient, GainChildren, CleanupChildren, AwakeningChoice, DrawAttempt, DrawChildren, CostChildren, UseChoice, IssuingUse, NativeUse, ReturnedUse }
public sealed record ShenGuoJiaJudgment(long Id, int? CardId, Suit? Suit, int? Rank, long? Handoff);
public sealed record ShenGuoJiaPlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId, CardKind? RequiredKind, bool Adjusted)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ShenGuoJiaReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int Turn, int TurnOwner, long Parent, ShenGuoJiaStage Stage)
{
    public long JudgmentId { get; init; }
    public bool MayRepeat { get; init; }
    public int Target { get; init; } = -1;
    public int Benefactor { get; init; } = -1;
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int MaximumBefore { get; init; }
    public int MaximumAfter { get; init; }
    public ShenGuoJiaPlan? Plan { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private IReadOnlyList<ShenGuoJiaJudgment> _judgments = Array.AsReadOnly(Array.Empty<ShenGuoJiaJudgment>());
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<ShenGuoJiaJudgment> Judgments { get => _judgments; init => _judgments = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenGuoJiaReceipt? ShenGuoJia { get; init; }
}
public sealed record ShenGuoJiaStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int Turn, int TurnOwner, long Parent, int Target) : IGameEvent;
public sealed record ShenGuoJiaJudgmentReturnedEvent(long FrameId, int Index, ShenGuoJiaJudgment Outcome) : IGameEvent;
public sealed record ShenGuoJiaMaximumPaidEvent(long FrameId, int Index, int Seat, int Before, int After, string Purpose) : IGameEvent;
public sealed record ShenGuoJiaBenefactorEvent(long FrameId, int Benefactor, int Recipient, string GrantSource) : IGameEvent;
public sealed record ShenGuoJiaAwakeningOverrideEvent(long FrameId, CardConversionSource Source, int Recipient, string SkillId, string InstanceId, string GameplayHash) : IGameEvent;
public sealed record ShenGuoJiaNativeIssuedEvent(long FrameId, long UseId, long ActionId, CardKind Kind, int Benefactor) : IGameEvent;
public sealed record ShenGuoJiaNativeReturnedEvent(long FrameId, long UseId, long ActionId, bool HadAttack) : IGameEvent;
public sealed record ShenGuoJiaCompletedEvent(long FrameId) : IGameEvent;
public sealed record ShenGuoJiaUnpaidCancelledEvent(long FrameId, CardConversionSource Source, int TurnUsesBefore, int PhaseUsesBefore) : IGameEvent;

internal interface IShenGuoJiaHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IProgramAwakeningOverrideQuery : IProgramSelectionQuery
{ bool Granted(int owner, string skill, string? binding = null); bool Issued(ProgramSkillFrame frame); }
internal abstract class ShenGuoJiaDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenGuoJiaHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class ResolveShenGuoJiaHuishiDescriptor() : ShenGuoJiaDescriptor(SkillProgramEffectOp.ResolveShenGuoJiaHuishi);
internal sealed class ResolveShenGuoJiaTianyiDescriptor() : ShenGuoJiaDescriptor(SkillProgramEffectOp.ResolveShenGuoJiaTianyi);
internal sealed class ResolveShenGuoJiaHuishiGrantDescriptor() : ShenGuoJiaDescriptor(SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant);
internal sealed class ResolveShenGuoJiaZuoxingDescriptor() : ShenGuoJiaDescriptor(SkillProgramEffectOp.ResolveShenGuoJiaZuoxing);
internal sealed class ShenGuoJiaHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IShenGuoJiaHost>().Resolve(frame, operation);
}
internal static class ShenGuoJiaComposition
{
    [ProgramCatalogValidation(38000)]
    internal static void Validate(string path, SkillProgram program)
    {
        static bool Owned(SkillProgramEffect effect) => effect.Op is SkillProgramEffectOp.ResolveShenGuoJiaHuishi or SkillProgramEffectOp.ResolveShenGuoJiaTianyi or SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant or SkillProgramEffectOp.ResolveShenGuoJiaZuoxing;
        if (!program.Triggers.SelectMany(trigger => trigger.Effects).Concat(program.Activations.SelectMany(activation => activation.Effects)).Any(Owned)) return;
        if (program.Modifiers.Count != 0 || program.ViewAs.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.Contributions.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Shen Guo Jia requires only his actual native instructions.");
        if (program.Id == "mobile:tianyi")
        {
            if (program.Activations.Count != 0 || program.Triggers is not [{ Id: "all-living-damaged", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner, Optional: false, UsageScope: SkillUsageScope.Game, UsageLimit: 1 } trigger] ||
                trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.Effects is not [
                    { Op: SkillProgramEffectOp.ChangeMaximumHp, Target: SkillProgramEffectTarget.Owner, Amount: 2, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.AnyLiving, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.ResolveShenGuoJiaTianyi, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Tianyi requires its complete preparation awakening, real maximum/recovery and selected recipient.");
            return;
        }
        var expected = program.Id switch { "mobile:huishi" => SkillProgramEffectOp.ResolveShenGuoJiaHuishi, "mobile:huishi-gift" => SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant, "mobile:zuoxing" => SkillProgramEffectOp.ResolveShenGuoJiaZuoxing, _ => (SkillProgramEffectOp)(-1) };
        if (program.Triggers.Count != 0 || program.Activations is not [var activation] || activation.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } effect] || effect.Op != expected ||
            activation.MinCards != 0 || activation.MaxCards != 0 || activation.TargetKind != SkillProgramTargetKind.AnyLiving || activation.UsesPerTurn is not null ||
            activation.MinTargets != (program.Id == "mobile:huishi-gift" ? 1 : 0) || activation.MaxTargets != activation.MinTargets ||
            (program.Id == "mobile:huishi-gift" ? activation.UsesPerGame != 1 || activation.UsesPerPhase is not null : activation.UsesPerPhase != 1 || activation.UsesPerGame is not null))
            throw new InvalidOperationException(path + ": Shen Guo Jia requires his exact zero-material activation and usage budget.");
    }
}
