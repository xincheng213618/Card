using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MoQiangangStage { TianenLossChildren, EnteringRumo, EntryChildren }
public sealed record MoQiangangRemovedGrant(string GrantId, string SkillInstanceId, string SourceId);
public sealed record MoQiangangReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner, long RevisionBefore)
{
    private readonly IReadOnlyList<MoQiangangRemovedGrant> _removed = Array.AsReadOnly(Array.Empty<MoQiangangRemovedGrant>());
    public IReadOnlyList<MoQiangangRemovedGrant> RemovedGrants
    { get => _removed; init => _removed = Array.AsReadOnly(value.ToArray()); }
    public MoQiangangStage Stage { get; init; }
    public long RevisionAfter { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoQiangangReceipt? MoQiangang { get; init; }
}
public sealed record MoQiangangStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner, int GrantCount, long RevisionBefore) : IGameEvent;
public sealed record MoQiangangTianenLossPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int GrantCount, long RevisionBefore, long RevisionAfter) : IGameEvent;
public sealed record MoQiangangTianenGrantRemovedEvent(long FrameId, int Index, string GrantId,
    string SkillInstanceId, string SourceId) : IGameEvent;
public sealed record MoQiangangEnteredEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record MoQiangangCompletedEvent(long FrameId) : IGameEvent;

internal interface IMoSunQuanQiangangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveMoSunQuanQiangangEntryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSunQuanQiangangEntry;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler { get; } = new MoSunQuanQiangangHandler();
}
internal sealed class MoSunQuanQiangangHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoSunQuanQiangangEntry;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IMoSunQuanQiangangHost>().Resolve(frame);
}
internal static class MoSunQuanQiangangComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "ol:mo-qiangang" && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveMoSunQuanQiangangEntry)) return;
        if (p.Id != "ol:mo-qiangang" || p.Activations is not [var a] || p.Triggers.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            a.Id != "lose-tianen-enter-rumo" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveMoSunQuanQiangangEntry,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Qiangang requires its actual Play activation, real Tianen loss and permanent paid Rumo entry.");
    }
}
