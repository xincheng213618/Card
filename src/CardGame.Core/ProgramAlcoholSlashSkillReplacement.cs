using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>An atomic, source-owned permanent exchange after a real wine-boosted Slash damage segment.</summary>
public sealed record AlcoholSlashSkillReplacementReceipt
{
    private IReadOnlyList<string> _lost = Array.AsReadOnly(Array.Empty<string>());
    private IReadOnlyList<SkillGrant> _removed = Array.AsReadOnly(Array.Empty<SkillGrant>());
    private IReadOnlyList<SkillGrant> _revoked = Array.AsReadOnly(Array.Empty<SkillGrant>());
    public long ProgramFrameId { get; init; }
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public long DamageWindowId { get; init; }
    public long DamageFrameId { get; init; }
    public long CardUseFrameId { get; init; }
    public long CardActionId { get; init; }
    public int TargetSeat { get; init; }
    public int DamageAmount { get; init; }
    public DamageNature Nature { get; init; }
    public int AlcoholBonus { get; init; }
    public IReadOnlyList<string> LostSkillIds { get => _lost; init => _lost = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<SkillGrant> RemovedGrants { get => _removed; init => _removed = Array.AsReadOnly(value.ToArray()); }
    /// <summary>Includes original grants already held by an outstanding, temporary loss receipt.</summary>
    public IReadOnlyList<SkillGrant> RevokedGrants { get => _revoked; init => _revoked = Array.AsReadOnly(value.ToArray()); }
    public required SkillGrant GrantedGrant { get; init; }
    /// <summary>The then-active source whose instance was reused, retained after that independent source disappears.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillGrant? SharedInstanceGrant { get; init; }
    public bool GrantAdded { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AlcoholSlashSkillReplacementReceipt? AlcoholSlashSkillReplacement { get; init; }
}

public sealed record ProgramAlcoholSlashSkillsReplacedEvent(AlcoholSlashSkillReplacementReceipt Receipt) : IGameEvent;
public sealed record ProgramAlcoholSlashSkillReplacementCompletedEvent(long ProgramFrameId) : IGameEvent;

internal interface IAlcoholSlashSkillReplacementProgramHost
{
    SkillProgramStepOutcome ReplaceOwnSkillsAfterAlcoholSlashDamage(ProgramSkillFrame frame);
}

internal sealed class ReplaceOwnSkillsAfterAlcoholSlashDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceOwnSkillsAfterAlcoholSlashDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new ReplaceOwnSkillsAfterAlcoholSlashDamageHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnSkills, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "skillIds", "sourceBind", "condition");
        var skills = reader.RequiredIdentifierArray("skillIds");
        var granted = reader.RequiredIdentifier("sourceBind");
        if (skills.Count == 0 || skills.Distinct(StringComparer.Ordinal).Count() != skills.Count || skills.Contains(granted, StringComparer.Ordinal))
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: permanent replacement requires distinct lost skills and a different granted skill.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), skillIds: skills, sourceBind: granted);
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class ReplaceOwnSkillsAfterAlcoholSlashDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceOwnSkillsAfterAlcoholSlashDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IAlcoholSlashSkillReplacementProgramHost>().ReplaceOwnSkillsAfterAlcoholSlashDamage(frame);
}
