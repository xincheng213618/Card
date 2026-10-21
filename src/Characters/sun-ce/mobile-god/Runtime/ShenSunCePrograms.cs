using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IShenSunCeProgramHost
{ SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation); }

internal abstract class ShenSunCeDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShenSunCeHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (effect, context) => context.ShenSunCeOpportunity(effect));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => operation switch
    {
        SkillProgramEffectOp.ResolveShenSunCeYingba => [new ReadSelectedTarget()],
        SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)],
        SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)]
    };
}
internal sealed class ResolveShenSunCeYingbaDescriptor() : ShenSunCeDescriptor(SkillProgramEffectOp.ResolveShenSunCeYingba);
internal sealed class ResolveShenSunCeFuhaiDeathDescriptor() : ShenSunCeDescriptor(SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath);
internal sealed class ResolveShenSunCeFuhaiDrawDescriptor() : ShenSunCeDescriptor(SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw);
internal sealed class ResolveShenSunCeFengheDescriptor() : ShenSunCeDescriptor(SkillProgramEffectOp.ResolveShenSunCeFenghe)
{ public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class ShenSunCeHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShenSunCeProgramHost>().Execute(frame, operation);
}

public enum ShenSunCeStage { TargetChildren, MaximumChildren, OwnerChildren, GiftChoice, GiftChildren, MarkerChildren, DrawAttempt, DrawChildren }
public sealed record ShenSunCeReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int InstructionIndex, long ParentId, int ActualTurn, int SubjectSeat, int Amount, ShenSunCeStage Stage)
{
    public long DeathId { get; init; }
    public int CardId { get; init; }
    public int RecipientSeat { get; init; } = -1;
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public int RequestedDraw { get; init; }
    public bool DrawReturned { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenSunCeReceipt? ShenSunCe { get; init; }
}
public sealed record ShenSunCeStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, long ParentId, int ActualTurn, int SubjectSeat, int Amount, long DeathId) : IGameEvent;
public sealed record ShenSunCeMaximumPaidEvent(long FrameId, int Seat, int Before, int After, int HpBefore, int HpAfter) : IGameEvent;
public sealed record ShenSunCeGiftPaidEvent(long FrameId, int CardId, int RecipientSeat, long Sequence) : IGameEvent;
public sealed record ShenSunCeDrawReturnedEvent(long FrameId, CardConversionSource Source, SkillProgramEffectOp Operation,
    int ActualTurn, int Requested, int Actual, long? AttemptId, long SequenceBefore, long SequenceAfter) : IGameEvent;

internal interface IShenSunCeSelectionQueries : IProgramSelectionQuery { bool CanSubdue(int seat); }
[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 38100, SkillProgramEffectOp.ResolveShenSunCeYingba)]
internal sealed class ShenSunCeSelection : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    { candidates.Targets = candidates.Targets.Where(queries.Get<IShenSunCeSelectionQueries>().CanSubdue).ToArray(); return true; }
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void ShenSunCeOpportunity(SkillProgramEffect effect)
    {
        if (effect.Op == SkillProgramEffectOp.ResolveShenSunCeYingba)
        {
            ApplyTargetHealth(new(PublicHealthEffectKind.MaximumHpLoss, 1));
            ChangeMaximumHp(new(SkillProgramEffectOp.ChangeMaximumHp, SkillProgramEffectTarget.Owner, -1, effect.Condition));
            _targetAdjustment -= 2d;
        }
        else if (effect.Op is SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw or SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath)
            Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition));
    }
}

internal static class ShenSunCeComposition
{
    [ProgramCatalogValidation(38100)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveShenSunCeYingba or
            SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw or SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath or SkillProgramEffectOp.ResolveShenSunCeFenghe;
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(Reserved)) return;
        static bool Effect(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) => effects is
            [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        static bool Trigger(SkillProgramTrigger t, string id, SkillProgramEffectOp op) => t.Id == id && !t.Optional &&
            t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && Effect(t.Effects, op);
        var valid = p.Id switch
        {
            "mobile:yingba" => p.Triggers.Count == 0 && p.Activations is [{ Id: "subdue", MinCards: 0, MaxCards: 0,
                MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, UsesPerPhase: 1, UsesPerGame: null } a] &&
                a.Condition.Kind == SkillProgramConditionKind.Always && Effect(a.Effects, SkillProgramEffectOp.ResolveShenSunCeYingba),
            "mobile:fuhai" => p.Activations.Count == 0 && p.Triggers.Count == 2 && p.Triggers.Any(t =>
                Trigger(t, "marked-target-draw", SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw) && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
                t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && !t.IncludeResponseUses) && p.Triggers.Any(t =>
                Trigger(t, "marked-death-growth", SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath) && t.Subject == SkillProgramTriggerSubject.Any),
            "mobile:fenghe" => p.Activations.Count == 0 && p.Triggers is [var t] &&
                Trigger(t, "prevent-and-give", SkillProgramEffectOp.ResolveShenSunCeFenghe) && t.Subject == SkillProgramTriggerSubject.DamageTarget,
            _ => false
        };
        if (!valid || p.ViewAs.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0 || p.Contributions.Count != 0 || p.CardIdentities.Count != 0)
            throw new InvalidOperationException(path + ": Shen Sun Ce requires his exact single-operation native bindings, once-per-Play one-target Yingba and unbudgeted locked trigger continuations.");
    }
}
