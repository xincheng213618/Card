using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IMobileSimaYiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class MobileSimaYiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MobileSimaYiHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.InitializeMobileShenSimaYiJilue
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.InitializeMobileShenSimaYiJilue => [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.SkillsChanged])],
        SkillProgramEffectOp.ResolveMobileShenSimaYiJilue => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)]
    };
}
internal sealed class MobileSimaYiInitializeDescriptor : MobileSimaYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMobileShenSimaYiJilue; }
internal sealed class MobileSimaYiJilueDescriptor : MobileSimaYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenSimaYiJilue; }
internal sealed class MobileSimaYiLianpoDescriptor : MobileSimaYiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenSimaYiLianpo; }
internal sealed class MobileSimaYiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IMobileSimaYiHost>().Execute(f, op);
}
public enum MobileSimaYiStage { Choice, Drawing, Children }
public sealed record MobileSimaYiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    SkillProgramEffectOp Operation, int Cost, MobileSimaYiStage Stage = MobileSimaYiStage.Choice, string? Choice = null,
    int DrawCount = 0, NativeDrawOutcome? DrawOutcome = null)
{
    private readonly IReadOnlyList<string> _skills = Array.AsReadOnly(Array.Empty<string>());
    public IReadOnlyList<string> Skills { get => _skills; init => _skills = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileSimaYiReceipt? MobileSimaYi { get; init; } }
public sealed record MobileSimaJilueInitializedEvent(long FrameId, int OwnerSeat, string SkillInstanceId, int AcquisitionOrdinal, string? FactionId) : IGameEvent;
public sealed record MobileSimaJilueChoiceEvent(long FrameId, CardConversionSource Source, string Choice, int Paid, int ActualTurn) : IGameEvent;
public sealed record MobileSimaJilueDrawReturnedEvent(long FrameId, int OwnerSeat, int Requested, int Actual, long Before, long After) : IGameEvent;
internal static class MobileSimaYiComposition
{
    [ProgramCatalogValidation(40425)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = p.Triggers.SelectMany(t => t.Effects).Select(e => e.Op).ToArray();
        if (!ops.Any(op => op is SkillProgramEffectOp.InitializeMobileShenSimaYiJilue or SkillProgramEffectOp.ResolveMobileShenSimaYiJilue or SkillProgramEffectOp.ResolveMobileShenSimaYiLianpo)) return;
        var valid = p.Id == "mobile-god:jilue" && p.Activations.Count == 0 && p.Triggers.Count == 3 &&
            p.Triggers.Count(t => t.Window is SkillProgramTriggerWindow.GameStarting or SkillProgramTriggerWindow.SkillsChanged && !t.Optional &&
                t.Subject == SkillProgramTriggerSubject.Owner && t.Effects is [{ Op: SkillProgramEffectOp.InitializeMobileShenSimaYiJilue, Target: SkillProgramEffectTarget.Owner }]) == 2 &&
            p.Triggers.Count(t => t.Window == SkillProgramTriggerWindow.PlayPhaseStarting && t.Optional && t.Subject == SkillProgramTriggerSubject.Owner &&
                t.Effects is [{ Op: SkillProgramEffectOp.ResolveMobileShenSimaYiJilue, Target: SkillProgramEffectTarget.Owner }]) == 1;
        valid |= p.Id == "mobile-god:lianpo" && p.Activations.Count == 0 && p.Triggers is [{ Window: SkillProgramTriggerWindow.CharacterDied,
            Subject: SkillProgramTriggerSubject.Owner, Optional: true, Condition.Kind: SkillProgramTriggerConditionKind.DeathKillerIsOwner,
            Effects: [{ Op: SkillProgramEffectOp.ResolveMobileShenSimaYiLianpo, Target: SkillProgramEffectTarget.Owner }] }];
        if (!valid) throw new InvalidOperationException(path + ": Mobile Sima Yi requires actual acquisition, Play-start learning and killer-owned death windows.");
    }
}
