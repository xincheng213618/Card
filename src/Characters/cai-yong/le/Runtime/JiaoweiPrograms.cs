using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum XianMarkOrigin { OpeningHand, FeibaiFallbackDraw }
public sealed record JiaoweiInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<JiaoweiInitialEntity>? _jiaoweiInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JiaoweiInitialEntity>? JiaoweiInitialEntities
    { get => _jiaoweiInitialEntities; init => _jiaoweiInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record XianEntityMarkedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence, XianMarkOrigin Origin) : IGameEvent;
public sealed record XianMarkingCompletedEvent(long FrameId, XianMarkOrigin Origin, int Count) : IGameEvent;
public sealed record JiaoweiInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int EntityCount) : IGameEvent;
public sealed record JiaoweiOwnedXianEntity(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record JiaoweiLossSource(CardConversionSource Source, string GameplayHash)
{
    private readonly IReadOnlyList<JiaoweiOwnedXianEntity> _materials = Array.AsReadOnly(Array.Empty<JiaoweiOwnedXianEntity>());
    public IReadOnlyList<JiaoweiOwnedXianEntity> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record JiaoweiLossQualifiedEvent(long BatchId, CardConversionSource Source, string GameplayHash,
    int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, int MovementSequence, int ActualTurn) : IGameEvent;
public sealed record JiaoweiProtectionArmedEvent(int OwnerSeat, int ActualTurn, long ArmId, long LossBatchId,
    CardConversionSource Source, string GameplayHash, int ActualLostCount, long? ReplacedArmId) : IGameEvent;
public sealed record JiaoweiDamagePreventedEvent(long AttackOwnerFrameId, int OwnerSeat, int ActualTurn,
    long ArmId, long LossBatchId, int? SourceSeat, int Amount, DamageNature Nature, bool IsChainPropagation) : IGameEvent;

internal interface ILeCaiYongJiaoweiHost { SkillProgramStepOutcome Initialize(ProgramSkillFrame frame); }
internal sealed class InitializeLeCaiYongJiaoweiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeLeCaiYongJiaowei;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
    public override ISkillProgramEffectHandler Handler { get; } = new InitializeLeCaiYongJiaoweiHandler();
}
internal sealed class InitializeLeCaiYongJiaoweiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeLeCaiYongJiaowei;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<ILeCaiYongJiaoweiHost>().Initialize(frame);
}
