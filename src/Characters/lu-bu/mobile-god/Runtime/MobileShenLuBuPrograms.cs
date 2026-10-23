using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IMobileShenLuBuHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MobileShenLuBuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MobileShenLuBuHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveMobileShenLuBuWuqian ? [new ReadSelectedTarget()] : [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class MobileShenLuBuWuqianDescriptor : MobileShenLuBuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenLuBuWuqian; }
internal sealed class MobileShenLuBuDamageCardDescriptor : MobileShenLuBuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GainMobileShenLuBuDamageCard; }
internal sealed class MobileShenLuBuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IMobileShenLuBuHost>().Execute(f, op);
}
public sealed record MobileLuBuProgramReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    SkillProgramEffectOp Op, int TargetSeat, long Before, long After = 0)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MobileLuBuWuqianIssuedEvent(long FrameId, CardConversionSource Source, int TargetSeat,
    string GrantId, string SkillInstanceId, string SourceId) : IGameEvent;
public sealed record MobileLuBuWuqianEndedEvent(long FrameId, long UseFrameId) : IGameEvent;
public sealed record MobileLuBuWushuangRevokedEvent(int TargetSeat, string GrantId, string SkillId,
    string SkillInstanceId, string SourceId, long UseFrameId) : IGameEvent;
public sealed record MobileLuBuUseCapturedEvent(long UseFrameId, int ActorSeat, long? ActionId, CardKind Kind) : IGameEvent;
public sealed record MobileLuBuUseReceipt(long UseFrameId, int ActorSeat, long? ActionId, CardKind Kind)
{
    private readonly IReadOnlyList<long> _issued = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> IssuedFrames { get => _issued; init => _issued = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileLuBuProgramReceipt? MobileLuBu { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileLuBuUseReceipt? MobileLuBuUse { get; init; } }
internal static class MobileShenLuBuComposition
{
    [ProgramCatalogValidation(40325)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShenLuBuWuqian or SkillProgramEffectOp.GainMobileShenLuBuDamageCard)) return;
        if (p.Id != "mobile-god:wuqian" || p.Activations is not [{ MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerPhase: null, UsesPerTurn: null,
                MarkerCost: { Marker: PlayerMarkerKind.Rage, Amount: 2 }, Effects: [{ Op: SkillProgramEffectOp.ResolveMobileShenLuBuWuqian, Target: SkillProgramEffectTarget.Owner }] }] ||
            p.Triggers is not [{ Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: true,
                Effects: [{ Op: SkillProgramEffectOp.GainMobileShenLuBuDamageCard, Target: SkillProgramEffectTarget.Owner }] }])
            throw new InvalidOperationException(path + ": Mobile Wuqian requires the repeatable two-Rage target issue and actual ending-stage damage-card gain.");
    }
}
