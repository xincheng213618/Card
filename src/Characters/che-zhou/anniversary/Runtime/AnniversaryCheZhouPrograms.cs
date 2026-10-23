using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryCheZhouHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class AnniversaryCheZhouDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryCheZhouPigua;
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryCheZhouHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class AnniversaryCheZhouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryCheZhouPigua;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IAnniversaryCheZhouHost>().Resolve(frame);
}
internal static class AnniversaryCheZhouComposition
{
    internal const string Shefu = "anniversary:shefu", Pigua = "anniversary:pigua", Binding = "obtain-damaged-hand";
    [ProgramCatalogValidation(46400)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryCheZhouPigua)) return;
        if (p.Id != Pigua || p.Triggers is not [var t] || t.Id != Binding || t.Window != SkillProgramTriggerWindow.AfterDamageApplied ||
            t.Subject != SkillProgramTriggerSubject.DamageSource || !t.Optional || !t.IsSkillInvocation || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.NamedUsageGroup is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveAnniversaryCheZhouPigua,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always, TargetReference: null, ActorReference: null }] ||
            p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Pigua requires its optional per-damage source trigger and one native hand acquisition operation.");
    }
}

public sealed record AnniversaryCheZhouRoundBoundaryEvent(int Round, long MovementSequence) : IGameEvent;
public sealed record AnniversaryCheZhouUseAgeCapturedEvent(long UseId, long ActionId, int Actor, int Round, long Cutoff, int Materials, int Age) : IGameEvent;
public sealed record AnniversaryCheZhouDamageReplacedEvent(long AttackId, int CardUser, int Target, int Previous, int Replacement) : IGameEvent;
public sealed record AnniversaryCheZhouUseAge(long ActionId, int Actor, int Round, long Cutoff, int Materials, int Age);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryCheZhouUseAge? AnniversaryCheZhouAge { get; init; }
}
public enum AnniversaryCheZhouStage { Selecting, MovementChildren, Complete }
public sealed record AnniversaryCheZhouPiguaReceipt(CardConversionSource Source, string Hash, long WindowId, long DamageId,
    int Turn, int TurnSeat, int Victim, int Round, AnniversaryCheZhouStage Stage, long Before = 0, long After = 0, long BatchId = 0)
{
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<CardMovementRecord> _moves = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryCheZhouPiguaReceipt? AnniversaryCheZhou { get; init; }
}
public sealed record AnniversaryCheZhouPiguaStartedEvent(long FrameId, CardConversionSource Source, string Hash, long WindowId,
    long DamageId, int Turn, int TurnSeat, int Victim, int Round) : IGameEvent;
public sealed record AnniversaryCheZhouPiguaSelectedEvent(long FrameId, int Count) : IGameEvent;
public sealed record AnniversaryCheZhouPiguaPaidEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnSeat,
    int Victim, int Count, long BatchId, long Before, long After) : IGameEvent;
public sealed record AnniversaryCheZhouPiguaCompletedEvent(long FrameId) : IGameEvent;
