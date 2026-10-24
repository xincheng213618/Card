using System.Text.Json.Serialization;

namespace CardGame.Core;

internal static class BoundaryZhouCangComposition
{
    internal const string Skill = "boundary:zhongyong-mobile", Binding = "completed-play-slash";
    [ProgramCatalogValidation(53300)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong)) return;
        if (p.Id != Skill || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.Triggers is not [{ Id: Binding, Window: SkillProgramTriggerWindow.CardUseCompleted,
                Optional: true, SingleActionInstance: true, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor,
                Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong, Target: SkillProgramEffectTarget.Owner }] } t] ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.IncludeResponseUses || t.Subject is not null || t.Priority != 0 ||
            t.UsageLimit is not null || t.UsageScope is not null || t.MarkerCost is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": mobile Zhongyong requires one actual Play Slash completion and its complete entity/response protocol.");
    }
}
internal interface IBoundaryZhouCangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f); }
internal sealed class ResolveBoundaryZhouCangZhongyongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new BoundaryZhouCangHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted), new RequireCardActionActor()];
}
internal sealed class BoundaryZhouCangHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryZhouCangZhongyong;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IBoundaryZhouCangHost>().Resolve(f);
}

public enum BoundaryZhongyongStage { Choose, GiveSlash, AcquireSlashChildren, AcquireDodgeChildren, GiveSlashChildren, GiveDodgeChildren }
public sealed record BoundaryZhongyongUseOriginEvent(long UseId, long ActionId, int Actor, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record BoundaryZhongyongEntityOriginEvent(long ActionId, int CardId, CardLocation From, long Sequence, long? PaidSequence) : IGameEvent;
public sealed record BoundaryZhongyongStartedEvent(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, long WindowId,
    bool Responded, int SlashCount, int DodgeCount, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record BoundaryZhongyongMovementEvent(long FrameId, int Index, BoundaryZhongyongStage Stage, int Recipient, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record BoundaryZhongyongMovementSettledEvent(long FrameId, int Index, BoundaryZhongyongStage Stage, int Recipient, int Requested, long Before, long After) : IGameEvent;
public sealed record BoundaryZhongyongUseBannedEvent(long FrameId, int Owner, int CardId, int Turn, int TurnOwner, long Sequence) : IGameEvent;
public sealed record BoundaryZhongyongBonusEvent(long FrameId, long UseId, int Owner, int Recipient, int Turn, int TurnOwner) : IGameEvent;
public sealed record BoundaryZhongyongReceipt(CardConversionSource Source, string Hash, long UseId, long ActionId, long WindowId,
    ActualDiscardRecoveryPhaseKey Phase, bool Responded, BoundaryZhongyongStage Stage)
{
    private readonly IReadOnlyList<int> _slash = [], _dodge = [], _excluded = [], _requested = [];
    public IReadOnlyList<int> Slash { get => _slash; init => _slash = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Dodge { get => _dodge; init => _dodge = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Excluded { get => _excluded; init => _excluded = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Requested { get => _requested; init => _requested = Array.AsReadOnly(value.ToArray()); }
    public bool Paid { get; init; }
    public int Recipient { get; init; }
    public int Index { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public HappyWeiYanGainRequestSettlement? Settlement { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundaryZhongyongReceipt? BoundaryZhongyong { get; init; }
}
