using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IMobileGuixinHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveMobileShenCaoCaoGuixinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenCaoCaoGuixin;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new MobileGuixinHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class MobileGuixinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShenCaoCaoGuixin;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMobileGuixinHost>().Resolve(f);
}
internal static class MobileGuixinComposition
{
    [ProgramCatalogValidation(40300)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveMobileShenCaoCaoGuixin)) return;
        if (p.Id != "mobile:guixin" || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers is not [{ Id: "claim-every-other-character", Window: SkillProgramTriggerWindow.AfterDamageApplied,
                Subject: SkillProgramTriggerSubject.Owner, DamageOccurrence: SkillProgramDamageOccurrence.PerDamagePoint,
                Optional: true, Condition.Kind: SkillProgramTriggerConditionKind.Always,
                UsageScope: null, UsageLimit: null, DynamicUsageLimit: null, NamedUsageGroup: null, MarkerCost: null,
                ChoiceGroup: null, RequiredPolarity: null,
                Effects: [{ Op: SkillProgramEffectOp.ResolveMobileShenCaoCaoGuixin, Target: SkillProgramEffectTarget.Owner,
                    TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] }])
            throw new InvalidOperationException(path + ": mobile Guixin requires one optional owner per-damage-point acquisition and turnover instruction.");
    }
}

public enum MobileGuixinStage { Choosing, MovementChildren, TurnoverChildren, Finished }
public sealed record MobileGuixinPayment(int Cursor, int TargetSeat, int CardId, CardLocation From,
    long BatchId, long Before, long After)
{
    private readonly IReadOnlyList<CardMovementRecord> _movements = [];
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MobileGuixinReceipt(CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount)
{
    private readonly IReadOnlyList<int> _seats = [];
    private readonly IReadOnlyList<MobileGuixinPayment> _payments = [];
    public IReadOnlyList<int> OtherSeats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MobileGuixinPayment> Payments
    { get => _payments; init => _payments = Array.AsReadOnly(value.Select(p => p with { Movements = p.Movements }).ToArray()); }
    public int Cursor { get; init; }
    public MobileGuixinStage Stage { get; init; }
    public bool TurnoverIssued { get; init; }
    public bool? FaceDownBefore { get; init; }
    public bool? FaceDownAfter { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileGuixinReceipt? MobileGuixin { get; init; } }
public sealed record MobileGuixinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount, string SeatOrder) : IGameEvent;
public sealed record MobileGuixinSeatSkippedEvent(long FrameId, int Cursor, int TargetSeat) : IGameEvent;
public sealed record MobileGuixinMovementPaidEvent(long FrameId, int Cursor, int TargetSeat, long BatchId, int Count, long Before, long After) : IGameEvent;
public sealed record MobileGuixinTurnoverIssuedEvent(long FrameId, bool Before, bool After) : IGameEvent;
public sealed record MobileGuixinCompletedEvent(long FrameId, int PaidCharacters, bool TurnoverIssued, bool Cancelled) : IGameEvent;
