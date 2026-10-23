using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappyGuixinHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveHappyShenCaoCaoGuixinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHappyShenCaoCaoGuixin;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new HappyGuixinHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class HappyGuixinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHappyShenCaoCaoGuixin;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IHappyGuixinHost>().Resolve(f);
}
internal static class HappyGuixinComposition
{
    [ProgramCatalogValidation(41400)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveHappyShenCaoCaoGuixin)) return;
        if (p.Id != "happy:guixin" || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers is not [{ Id: "random-claim-conditional-turnover", Window: SkillProgramTriggerWindow.AfterDamageApplied,
                Subject: SkillProgramTriggerSubject.Owner, DamageOccurrence: SkillProgramDamageOccurrence.PerDamagePoint,
                Optional: true, Condition.Kind: SkillProgramTriggerConditionKind.Always, UsageScope: null, UsageLimit: null,
                DynamicUsageLimit: null, NamedUsageGroup: null, MarkerCost: null, ChoiceGroup: null, RequiredPolarity: null,
                Effects: [{ Op: SkillProgramEffectOp.ResolveHappyShenCaoCaoGuixin, Target: SkillProgramEffectTarget.Owner,
                    TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] }])
            throw new InvalidOperationException(path + ": Happy Guixin requires one optional per-damage-point random owner acquisition with conditional turnover.");
    }
}

public enum HappyGuixinStage { Visiting, MovementChildren, TurnoverChildren, Finished }
public sealed record HappyGuixinPayment(MobileGuixinPayment Movement, uint RandomBefore, uint RandomAfter)
{
    private readonly IReadOnlyList<OlGuixinCandidate> _candidates = Array.AsReadOnly(Array.Empty<OlGuixinCandidate>());
    public IReadOnlyList<OlGuixinCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HappyGuixinReceipt(CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount)
{
    private readonly IReadOnlyList<int> _seats = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<HappyGuixinPayment> _payments = Array.AsReadOnly(Array.Empty<HappyGuixinPayment>());
    public IReadOnlyList<int> OtherSeats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<HappyGuixinPayment> Payments { get => _payments; init => _payments = Array.AsReadOnly(value.Select(p => p with
        { Candidates = p.Candidates, Movement = p.Movement with { Movements = p.Movement.Movements } }).ToArray()); }
    public int Cursor { get; init; }
    public HappyGuixinStage Stage { get; init; }
    public bool TurnoverIssued { get; init; }
    public bool? FaceDownBefore { get; init; }
    public bool? FaceDownAfter { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappyGuixinReceipt? HappyGuixin { get; init; } }

// Foreign hand identities, the frozen random pool, and RNG state remain on the original frame.
public sealed record HappyGuixinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount, string SeatOrder) : IGameEvent;
public sealed record HappyGuixinSeatSkippedEvent(long FrameId, int Cursor, int TargetSeat) : IGameEvent;
public sealed record HappyGuixinMovementPaidEvent(long FrameId, int Cursor, int TargetSeat, long BatchId, long Before, long After) : IGameEvent;
public sealed record HappyGuixinTurnoverIssuedEvent(long FrameId, int ActualObtained, bool Before, bool After) : IGameEvent;
public sealed record HappyGuixinCompletedEvent(long FrameId, int PaidCharacters, int ActualObtained, bool TurnoverIssued, bool Cancelled) : IGameEvent;
