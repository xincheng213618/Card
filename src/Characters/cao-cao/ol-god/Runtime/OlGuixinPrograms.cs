using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IOlGuixinHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveOlShenCaoCaoGuixinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOlShenCaoCaoGuixin;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new OlGuixinHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class OlGuixinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOlShenCaoCaoGuixin;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IOlGuixinHost>().Resolve(f);
}
internal static class OlGuixinComposition
{
    [ProgramCatalogValidation(40700)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveOlShenCaoCaoGuixin)) return;
        if (p.Id != "ol:guixin" || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers is not [{ Id: "priority-region-random-claim", Window: SkillProgramTriggerWindow.AfterDamageApplied,
                Subject: SkillProgramTriggerSubject.Owner, DamageOccurrence: SkillProgramDamageOccurrence.PerDamagePoint,
                Optional: true, Condition.Kind: SkillProgramTriggerConditionKind.Always, UsageScope: null, UsageLimit: null,
                DynamicUsageLimit: null, NamedUsageGroup: null, MarkerCost: null, ChoiceGroup: null, RequiredPolarity: null,
                Effects: [{ Op: SkillProgramEffectOp.ResolveOlShenCaoCaoGuixin, Target: SkillProgramEffectTarget.Owner,
                    TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] }])
            throw new InvalidOperationException(path + ": OL Guixin requires one optional per-damage-point owner priority-region sweep and turnover.");
    }
}

public enum OlGuixinStage { PriorityChoice, Visiting, MovementChildren, TurnoverChildren, Finished }
public sealed record OlGuixinCandidate(int CardId, CardLocation From);
public sealed record OlGuixinPayment(MobileGuixinPayment Movement, uint RandomBefore, uint RandomAfter)
{
    private readonly IReadOnlyList<OlGuixinCandidate> _candidates = Array.AsReadOnly(Array.Empty<OlGuixinCandidate>());
    public IReadOnlyList<OlGuixinCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
}
public sealed record OlGuixinReceipt(CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount)
{
    private readonly IReadOnlyList<int> _seats = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<OlGuixinPayment> _payments = Array.AsReadOnly(Array.Empty<OlGuixinPayment>());
    public IReadOnlyList<int> OtherSeats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<OlGuixinPayment> Payments { get => _payments; init => _payments = Array.AsReadOnly(value.Select(p => p with
        { Candidates = p.Candidates, Movement = p.Movement with { Movements = p.Movement.Movements } }).ToArray()); }
    public int Cursor { get; init; }
    public CardZoneKind? PreferredZone { get; init; }
    public OlGuixinStage Stage { get; init; }
    public bool TurnoverIssued { get; init; }
    public bool? FaceDownBefore { get; init; }
    public bool? FaceDownAfter { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OlGuixinReceipt? OlGuixin { get; init; } }

// Player-facing history exposes neither the frozen foreign hand candidates nor RNG state.
public sealed record OlGuixinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount, string SeatOrder) : IGameEvent;
public sealed record OlGuixinPriorityChosenEvent(long FrameId, CardZoneKind PreferredZone) : IGameEvent;
public sealed record OlGuixinSeatSkippedEvent(long FrameId, int Cursor, int TargetSeat) : IGameEvent;
public sealed record OlGuixinMovementPaidEvent(long FrameId, int Cursor, int TargetSeat, CardZoneKind ChosenZone, long BatchId, long Before, long After) : IGameEvent;
public sealed record OlGuixinTurnoverIssuedEvent(long FrameId, bool Before, bool After) : IGameEvent;
public sealed record OlGuixinCompletedEvent(long FrameId, int PaidCharacters, bool TurnoverIssued, bool Cancelled) : IGameEvent;
