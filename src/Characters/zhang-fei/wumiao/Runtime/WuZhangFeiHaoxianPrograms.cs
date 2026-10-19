using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WuHaoxianStage { Recycle, RecycleChildren, Obtain, ObtainChildren, Complete }
public sealed record WuHaoxianMaterial(int CardId, CardKind PrintedKind, CardLocation From, int EffectiveRank);
public sealed record WuHaoxianReceipt(CardConversionSource Source, string GameplayHash, WuHaoxianStage Stage,
    int Before, int After = 0, long? RecycleBatchId = null, long? ObtainBatchId = null, bool Shuffled = false)
{
    private readonly IReadOnlyList<WuHaoxianMaterial> _recycle = Array.AsReadOnly(Array.Empty<WuHaoxianMaterial>());
    private readonly IReadOnlyList<WuHaoxianMaterial> _obtain = Array.AsReadOnly(Array.Empty<WuHaoxianMaterial>());
    public IReadOnlyList<WuHaoxianMaterial> RecycleMaterials
    { get => _recycle; init => _recycle = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<WuHaoxianMaterial> ObtainMaterials
    { get => _obtain; init => _obtain = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuHaoxianReceipt? WuHaoxian { get; init; }
}
public sealed record WuHaoxianStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Before) : IGameEvent;
public sealed record WuHaoxianMaterialFrozenEvent(long FrameId, bool Recycle, int Index, WuHaoxianMaterial Material) : IGameEvent;
public sealed record WuHaoxianMovementPaidEvent(long FrameId, bool Recycle, long? BatchId, int Before, int After, int Count) : IGameEvent;
public sealed record WuHaoxianShuffledEvent(long FrameId, uint RandomBefore, uint RandomAfter)
    : IGameEvent
{
    private readonly IReadOnlyList<int> _before = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _after = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> BeforeOrder { get => _before; init => _before = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AfterOrder { get => _after; init => _after = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WuHaoxianCompletedEvent(long FrameId, int RecycledCount, int ObtainedCount, bool Shuffled) : IGameEvent;
internal interface IWuZhangFeiHaoxianHost
{ SkillProgramStepOutcome ResolveHaoxian(ProgramSkillFrame frame); }
internal sealed class ResolveWuZhangFeiHaoxianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiHaoxian;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        (effect, context) => context.WuHaoxianPublicGainPrior(effect));
    public override ISkillProgramEffectHandler Handler { get; } = new WuZhangFeiHaoxianHandler();
}
internal sealed class WuZhangFeiHaoxianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhangFeiHaoxian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWuZhangFeiHaoxianHost>().ResolveHaoxian(frame);
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void WuHaoxianPublicGainPrior(SkillProgramEffect effect) => Draw(new(SkillProgramEffectOp.Draw,
        SkillProgramEffectTarget.Owner, Math.Max(1, (_publicContext.LivingPlayerCount - 1) / 3), effect.Condition));
}
