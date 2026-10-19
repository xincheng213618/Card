using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum FuMarkOrigin { OpeningHand, WenlanObtain, WenlanHandRewrite }
public sealed record FuEntityNameSnapshot(int CardId, CardKind PrintedKind, CardKind? AliasKind, long Revision);
public sealed record FuleInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, int EntrySequence);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<FuleInitialEntity>? _fuleInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FuleInitialEntity>? FuleInitialEntities
    { get => _fuleInitialEntities; init => _fuleInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record FuEntityAssignedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    FuMarkOrigin Origin, int Index, int CardId, CardKind PrintedKind, CardKind? PreviousAliasKind,
    CardKind? AliasKind, long PreviousRevision, long Revision, long RandomBefore, long RandomAfter, bool Changed) : IGameEvent;
public sealed record FuMarkingCompletedEvent(long FrameId, FuMarkOrigin Origin, int Count) : IGameEvent;
public sealed record FuleInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int EntityCount) : IGameEvent;

internal interface ILeCaoZhiFuleHost { SkillProgramStepOutcome Initialize(ProgramSkillFrame frame); }
internal sealed class InitializeLeCaoZhiFuleDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeLeCaoZhiFule;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new InitializeLeCaoZhiFuleHandler();
}
internal sealed class InitializeLeCaoZhiFuleHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeLeCaoZhiFule;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<ILeCaoZhiFuleHost>().Initialize(frame);
}
