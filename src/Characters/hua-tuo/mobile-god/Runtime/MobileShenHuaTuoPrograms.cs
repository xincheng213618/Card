using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MobileWulingAnimal { Tiger, Crane, Bear, Ape, Deer }
public enum MobileHuaTuoStage { Recipient, Order, Entry, EntryChildren, DeerRecoveryChildren, Drawing, Ape, Children, RecoverAll, Finished }
public sealed record MobileHuaTuoReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int InstructionIndex, MobileHuaTuoStage Stage, int TargetSeat = -1, long CourseId = 0, int AnimalIndex = 0,
    int Cursor = 0, long Before = 0, long After = 0)
{
    private IReadOnlyList<MobileWulingAnimal> _order = Array.AsReadOnly(Array.Empty<MobileWulingAnimal>());
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>()), _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<MobileWulingAnimal> Order { get => _order; init => _order = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileHuaTuoReceipt? MobileHuaTuo { get; init; }
}
internal sealed record MobileWulingCourse(CardConversionSource Source, int TargetSeat)
{
    private IReadOnlyList<MobileWulingAnimal> _order = Array.AsReadOnly(Array.Empty<MobileWulingAnimal>());
    internal IReadOnlyList<MobileWulingAnimal> Order { get => _order; init => _order = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MobileWulingTaughtEvent(long CourseId, CardConversionSource Source, int TargetSeat) : IGameEvent;
public sealed record MobileWulingAdvancedEvent(long FrameId, long CourseId, int Index, bool Active) : IGameEvent;
public sealed record MobileWulingGrantEvent(long CourseId, int TargetSeat, string SkillId, string GrantId, bool Added) : IGameEvent;
public sealed record MobileWulingGrantRevokedEvent(long CourseId, int TargetSeat, string SkillId, string GrantId, string SkillInstanceId, string SourceId) : IGameEvent;
public sealed record MobileHuaTuoPaidEvent(long FrameId, int Cursor, string Kind, int TargetSeat, long Before, long After) : IGameEvent;
internal interface IMobileHuaTuoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MobileHuaTuoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MobileHuaTuoHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class TeachMobileHuaTuoDescriptor : MobileHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.TeachMobileShenHuaTuoWuling; }
internal sealed class AdvanceMobileHuaTuoDescriptor : MobileHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdvanceMobileShenHuaTuoWuling; }
internal sealed class DepositMobileHuaTuoDescriptor : MobileHuaTuoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositMobileShenHuaTuoYouyi;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class RecoverMobileHuaTuoDescriptor : MobileHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverMobileShenHuaTuoYouyi; }
internal sealed class MobileHuaTuoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IMobileHuaTuoHost>().Resolve(f, op);
}
