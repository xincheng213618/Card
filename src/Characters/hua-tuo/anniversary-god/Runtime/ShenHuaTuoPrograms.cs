using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SkillInvocationReturnKind { RuntimeFrame, SkipDiscard, FactionDefense, FactionSlash, NativeEquipment }
public enum SkillInvocationStage { Ready, Drawing, Children }
public enum SkillInvocationReactionKind { Jingyu, Luxin }
public sealed record SkillInvocationReaction(SkillInvocationReactionKind Kind, int OwnerSeat,
    string SkillInstanceId, long LuxinFrameId = 0, int HpDelta = 0);
public sealed record SkillInvocationReceipt
{
    private IReadOnlyList<string> _issued = Array.AsReadOnly(Array.Empty<string>());
    public IReadOnlyList<string> Issued { get => _issued; init => _issued = Array.AsReadOnly(value.ToArray()); }
}
public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillInvocationReceipt? SkillInvocations { get; init; }
}
[JsonDerivedType(typeof(SkillInvocationFrame), "skill-invocation")]
public abstract partial record ResolutionFrame;
public sealed record SkillInvocationFrame(long Id, long ParentFrameId, int ActorSeat,
    string SkillKey, string OccurrenceKey, string SkillInstanceId, string SkillName, string GameplayHash,
    int RoundNumber, int TurnNumber, int TurnOwnerSeat, SkillInvocationReturnKind ReturnKind,
    int Cursor = 0, SkillInvocationStage Stage = SkillInvocationStage.Ready,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.SkillInvocation, Step)
{
    private IReadOnlyList<SkillInvocationReaction> _reactions = Array.AsReadOnly(Array.Empty<SkillInvocationReaction>());
    public IReadOnlyList<SkillInvocationReaction> Reactions { get => _reactions; init => _reactions = Array.AsReadOnly(value.ToArray()); }
    // Trusted diagnostics only: binds an unexecuted program to the complete
    // accepted source state, including its private selection and issued key.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceFrameFingerprint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NativeDrawOutcome? DrawOutcome { get; init; }
    public long? DyingFrameId { get; init; }
}
public sealed record SkillInvocationStartedEvent(long FrameId, long ParentFrameId, int ActorSeat,
    string SkillKey, string OccurrenceKey, string SkillInstanceId, string SkillName, string GameplayHash,
    int RoundNumber, int TurnNumber, int TurnOwnerSeat, SkillInvocationReturnKind ReturnKind) : IGameEvent;
public sealed record SkillInvocationCompletedEvent(long FrameId, long ParentFrameId, string OccurrenceKey) : IGameEvent;
public sealed record ShenHuaTuoJingyuReservedEvent(long InvocationFrameId, int OwnerSeat, string SkillInstanceId,
    string SkillName, int RoundNumber) : IGameEvent;
public sealed record ShenHuaTuoLuxinArmedEvent(long FrameId, int OwnerSeat, int TargetSeat, int HpDelta) : IGameEvent;
public sealed record ShenHuaTuoLuxinConsumedEvent(long LuxinFrameId, long InvocationFrameId, int TargetSeat, int HpDelta) : IGameEvent;
public sealed record SkillInvocationHpPaidEvent(long FrameId, int Cursor, int OwnerSeat, int TargetSeat, int Delta, int Before, int After) : IGameEvent;
public sealed record SkillInvocationDrawPaidEvent(long FrameId, int Cursor, int RecipientSeat, int Count,
    long SequenceBefore, long SequenceAfter, long? AttemptFrameId, bool Replaced) : IGameEvent;

public enum ShenHuaTuoStage { LuxinChoice, Drawing, DrawChildren, DiscardChildren, RestoreChildren, AcceptSkill, LoseSkill, SkillChildren }
public sealed record ShenHuaTuoReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int TargetSeat, ShenHuaTuoStage Stage, int GiftId = 0, CardKind? GiftKind = null, int Count = 0,
    int HpDelta = 0, long SequenceBefore = 0, long SequenceAfter = 0, NativeDrawOutcome? DrawOutcome = null,
    string? SelectedGeneralId = null, string? SelectedSkillId = null, uint RandomBefore = 0, uint RandomAfterGeneral = 0,
    uint RandomAfterSkill = 0, string? GrantedGrantId = null, bool GrantAdded = false)
{
    private IReadOnlyList<string> _generals = Array.AsReadOnly(Array.Empty<string>()), _skills = Array.AsReadOnly(Array.Empty<string>());
    private IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<string> GeneralCandidates { get => _generals; init => _generals = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<string> SkillCandidates { get => _skills; init => _skills = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DiscardedIds { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenHuaTuoReceipt? ShenHuaTuo { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ResumeSkillInvocationFrameId { get; init; }
}
public sealed record ShenHuaTuoRandomGeneralEvent(long FrameId, int TargetSeat, string GeneralId) : IGameEvent;
public sealed record ShenHuaTuoRandomSkillEvent(long FrameId, int TargetSeat, string GeneralId, string SkillId) : IGameEvent;
public sealed record ShenHuaTuoSkillGrantedEvent(long FrameId, int TargetSeat, string GeneralId, string SkillId, string GrantId) : IGameEvent;
public sealed record ShenHuaTuoSkillUnchangedEvent(long FrameId, int TargetSeat, string GeneralId, string SkillId) : IGameEvent;
public sealed record ShenHuaTuoSkillRevokedEvent(long FrameId, int TargetSeat, string SkillId, string GrantId, string SkillInstanceId, string SourceId) : IGameEvent;
internal interface IShenHuaTuoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShenHuaTuoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShenHuaTuoHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ShenHuaTuoLuxinDescriptor : ShenHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenHuaTuoLuxin; }
internal sealed class ShenHuaTuoHuandaoDescriptor : ShenHuaTuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenHuaTuoHuandao; }
internal sealed class ShenHuaTuoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<IShenHuaTuoHost>().Resolve(frame, op);
}
internal static class ShenHuaTuoComposition
{
    [ProgramCatalogValidation(38700)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveShenHuaTuoLuxin or SkillProgramEffectOp.ResolveShenHuaTuoHuandao)) return;
        var valid = p.Triggers.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 &&
            p.Activations is [var a] && a.MinTargets == 1 && a.MaxTargets == 1 && a.TargetKind == SkillProgramTargetKind.OtherLiving &&
            (p.Id == "anniversary-god:luxin" && a.Id == "give-and-filter" && a.MinCards == 1 && a.MaxCards == 1 &&
                a.SourceZones.SequenceEqual([CardZoneKind.Hand]) && a.UsesPerPhase == 1 &&
                a.Effects is [{ Op: SkillProgramEffectOp.GiveSelected, Target: SkillProgramEffectTarget.SelectedTarget },
                    { Op: SkillProgramEffectOp.ResolveShenHuaTuoLuxin, Target: SkillProgramEffectTarget.Owner }] ||
             p.Id == "anniversary-god:huandao" && a.Id == "restore-and-exchange" && a.MinCards == 0 && a.MaxCards == 0 &&
                a.UsesPerGame == 1 &&
                a.Effects is [{ Op: SkillProgramEffectOp.ResolveShenHuaTuoHuandao, Target: SkillProgramEffectTarget.Owner }]);
        if (!valid) throw new InvalidOperationException(path + ": Shen Hua Tuo requires its complete paid hand gift or limited recipient restoration composition.");
    }
}
