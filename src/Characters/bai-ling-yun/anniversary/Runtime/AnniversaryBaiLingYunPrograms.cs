using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryBaiLingYunHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class ResolveAnniversaryBaiLingYunLinghuiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryBaiLingYunHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class AnniversaryBaiLingYunHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryBaiLingYunHost>().Begin(frame);
}
public enum AnniversaryBlyStage { Viewing, Choice, UsePayment, IssuingUse, NativeUse, NativeTail, GainChildren }
public sealed record AnniversaryBlyReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, AnniversaryBlyStage Stage)
{
    private readonly IReadOnlyList<int> _viewed = [];
    public IReadOnlyList<int> Viewed { get => _viewed; init => _viewed = Array.AsReadOnly(value.ToArray()); }
    public long ViewBefore { get; init; }
    public long ViewAfter { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public AnniversaryBlyPlan? Plan { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public int? GainId { get; init; }
    private readonly IReadOnlyList<long> _nativeBatches = [];
    public IReadOnlyList<long> NativeBatches { get => _nativeBatches; init => _nativeBatches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryBlyPlan(int CardId, CardKind Kind, LegalActionKind ActionKind, int? TargetCardId, string? Adjustment)
{
    private readonly IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryBlyNative(long FrameId, int Instruction, CardConversionSource Source, string Hash, int Turn, int CardId, long UseId, long ActionId);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryBlyReceipt? AnniversaryBly { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryBlyNative? AnniversaryBlyUse { get; init; } }
public sealed record AnniversaryBlyStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, int ViewedCount) : IGameEvent;
public sealed record AnniversaryBlyNativeIssuedEvent(AnniversaryBlyNative Origin) : IGameEvent;
public sealed record AnniversaryBlyNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryBlyNativeBatchEvent(long FrameId, long UseId, long BatchId, int Count) : IGameEvent;
public sealed record AnniversaryBlyGainCompletedEvent(long FrameId, int RequestedCount, long Before, long After) : IGameEvent;

internal static class AnniversaryBaiLingYunComposition
{
    internal const string Linghui = "anniversary:linghui", Xiace = "anniversary:xiace", Yuxin = "anniversary:yuxin";
    [ProgramCatalogValidation(43600)] internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui)) return;
        if (program.Id != Linghui || program.Triggers.Count != 2 || program.Activations.Count != 0 || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 ||
            program.Triggers.Any(t => t.Window != SkillProgramTriggerWindow.TurnEnding || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.Effects is not [{ Op: SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]) ||
            !program.Triggers.Any(t => t.Id == "own-ending" && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own) ||
            !program.Triggers.Any(t => t.Id == "foreign-ending" && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving))
            throw new InvalidOperationException(path + ": Linghui requires its exact own/qualified foreign ending private top-card native-use pipeline.");
    }
}
