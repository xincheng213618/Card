using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record SourceCurseDeposit(long DepositFrameId, CardConversionSource Source,
    string GameplayHash, int TargetSeat, int CardId, CardKind CardKind, Suit PrintedSuit,
    int CreatedTurn, string BenefitSkillId, string? BenefitSkillInstanceId,
    string? BenefitGameplayHash, int ActualDrawCount = 0)
{
    [JsonIgnore] public CardLocation Location =>
        new(CardZoneKind.PublicPersistentPile, TargetSeat, "source-curse:" + DepositFrameId);
}
public sealed record SourceCurseLoss(SourceCurseDeposit Deposit, int ActualTurn,
    long MovementSequence, CardLocation Destination, string Reason);
public sealed record SourceCurseUseIdentity(SourceCurseDeposit Deposit, long ActionId,
    int ActorSeat, bool? EffectiveIsRed);
public enum SourceCurseStage { DepositChildren, DrawChildren, ObtainChildren, LossHpChildren }

public sealed record SourceCurseProgramReceipt
{
    private IReadOnlyList<SourceCurseLoss> _losses = Array.Empty<SourceCurseLoss>();
    public int InstructionIndex { get; init; }
    public SourceCurseStage Stage { get; init; }
    public SourceCurseDeposit? Deposit { get; init; }
    public CardLocation? PaymentFrom { get; init; }
    public long ActionId { get; init; }
    public long OriginalParentId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawCount { get; init; }
    public int ActualTurn { get; init; }
    public IReadOnlyList<SourceCurseLoss> Losses
    {
        get => _losses;
        init => _losses = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
    // Advanced before issuing each real HP loss, so a typed child cannot repay it.
    public int LossIndex { get; init; }
    public int? CurrentHpTarget { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SourceCurseProgramReceipt? SourceCurseReceipt { get; init; }
}
public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SourceCurseDeposit? SourceCurseOrigin { get; init; }
}
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SourceCurseUseIdentity? SourceCurseUse { get; init; }
}
// Viewer-safe public facts; exact grant instances and content hashes stay on trusted frames/events.
public sealed record SourceCurseSnapshot(int SourceSeat, int ActualDrawCount, CardSnapshot Card);
public sealed partial record PlayerSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SourceCurseSnapshot>? SourceCurses { get; init; }
}
public sealed record SourceCurseDepositedEvent(SourceCurseDeposit Deposit,
    CardLocation From, long MovementSequence) : IGameEvent;
public sealed record SourceCurseLostEvent(SourceCurseLoss Loss) : IGameEvent;
public sealed record SourceCurseJudgmentIssuedEvent(long JudgmentFrameId,
    SourceCurseDeposit Deposit) : IGameEvent;
public sealed record SourceCurseDrawIssuedEvent(long ProgramFrameId, long ParentFrameId,
    SourceCurseDeposit Deposit, long ActionId, int ActualDrawCount,
    long Before, long After) : IGameEvent;
public sealed record SourceCurseObtainedEvent(long ProgramFrameId, SourceCurseDeposit Deposit,
    long Before, long After) : IGameEvent;
public sealed record SourceCurseLossRosterIssuedEvent(long ProgramFrameId,
    long ActualTurnEndFrameId, int ActualTurn, IReadOnlyList<SourceCurseLoss> Losses) : IGameEvent;
public sealed record SourceCurseHpLossIssuedEvent(long ProgramFrameId,
    int ActualTurn, int TargetSeat, int RosterIndex) : IGameEvent;

internal interface ISourceCurseProgramHost
{
    SkillProgramStepOutcome ExecuteSourceCurse(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal abstract class SourceCurseDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new SourceCurseHandler(Op);
    protected SkillProgramEffect ParseOwner(ProgramOperationNodeReader r, bool reference)
    {
        if (reference) r.AllowOnly("op", "target", "skillIds", "condition");
        else r.AllowOnly("op", "target", "condition");
        var skills = reference ? r.RequiredIdentifierArray("skillIds") : [];
        if (reference && skills.Count != 1)
            throw new InvalidOperationException("A source curse requires one exact paired skill.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            0, r.Condition(), skillIds: skills);
        RequireAlways(effect, r.Path); return effect;
    }
}
internal sealed class DepositSelectedSourceCurseDescriptor : SourceCurseDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DepositSelectedSourceCurse;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (e, c) => c.LoseHp(new(SkillProgramEffectOp.LoseHp,
            SkillProgramEffectTarget.SelectedTarget, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, true);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ConsumeSelectedCards(1), new ReadSelectedTarget()];
}
internal sealed class DrawForSourceCurseUseDescriptor : SourceCurseDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawForSourceCurseUse;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, true);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
}
internal sealed class LoseHpForLostSourceCursesDescriptor : SourceCurseDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHpForLostSourceCurses;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwner(r, false);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class SourceCurseHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f,
        int seat, ISkillProgramEffectHost h) => ((ISourceCurseProgramHost)h).ExecuteSourceCurse(e, f);
}
internal static class SourceCurseComposition
{
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var owner in programs.Values)
        {
            foreach (var deposit in owner.Activations.SelectMany(a => a.Effects)
                         .Where(e => e.Op == SkillProgramEffectOp.DepositSelectedSourceCurse))
            {
                var paired = deposit.SkillIds.Single();
                if (!programs.TryGetValue(paired, out var benefit) ||
                    !benefit.Triggers.Any(t => t.Effects is [{ Op: SkillProgramEffectOp.DrawForSourceCurseUse } e] &&
                        e.SkillIds.Single() == owner.Id) ||
                    !new[] { SkillProgramTurnOwnerScope.Own, SkillProgramTurnOwnerScope.OtherLiving }.All(scope =>
                        owner.Triggers.Count(t => t.TurnOwnerScope == scope &&
                            t.Effects is [{ Op: SkillProgramEffectOp.LoseHpForLostSourceCurses }]) == 1))
                    throw new InvalidOperationException(owner.Id + ": curse deposit requires its exact paired draw producer and both actual turn-end scopes.");
            }
            foreach (var draw in owner.Triggers.SelectMany(t => t.Effects)
                         .Where(e => e.Op == SkillProgramEffectOp.DrawForSourceCurseUse))
                if (!programs.TryGetValue(draw.SkillIds.Single(), out var source) ||
                    !source.Activations.Any(a => a.Effects is [{ Op: SkillProgramEffectOp.DepositSelectedSourceCurse } e] &&
                        e.SkillIds.Single() == owner.Id))
                    throw new InvalidOperationException(owner.Id + ": curse reward requires its reciprocal source deposit program.");
            if (owner.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.LoseHpForLostSourceCurses)) &&
                !owner.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.DepositSelectedSourceCurse)))
                throw new InvalidOperationException(owner.Id + ": curse loss producer requires a real source deposit program.");
        }
    }
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (a.Effects.Any(e => e.Op is SkillProgramEffectOp.DrawForSourceCurseUse or SkillProgramEffectOp.LoseHpForLostSourceCurses))
            throw new InvalidOperationException(path + ": curse benefit/loss requires its actual trigger parent.");
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.DepositSelectedSourceCurse)) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.DepositSelectedSourceCurse }] ||
            a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerPhase != 1 ||
            a.UsesPerTurn is not null || a.UsesPerGame is not null ||
            !a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null ||
            a.ContinueAfterOwnerDeath || a.CardCountExpression is not null)
            throw new InvalidOperationException(path + ": curse deposit requires one unconditional HE transfer, one other living target and one actual-Play use.");
    }
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        SkillProgramTurnOwnerScope scope, bool optional, SkillProgramCardActionOwnerRelation? relation,
        bool includeResponseUses)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DepositSelectedSourceCurse))
            throw new InvalidOperationException(path + ": curse deposit requires an actual Play activation.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DrawForSourceCurseUse) &&
            (effects is not [{ Op: SkillProgramEffectOp.DrawForSourceCurseUse }] || optional ||
             window != SkillProgramTriggerWindow.CardUseCommitted ||
             relation != SkillProgramCardActionOwnerRelation.Observer || includeResponseUses))
            throw new InvalidOperationException(path + ": curse draw requires one mandatory genuine committed-use observer.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.LoseHpForLostSourceCurses) &&
            (effects is not [{ Op: SkillProgramEffectOp.LoseHpForLostSourceCurses }] || optional ||
             window != SkillProgramTriggerWindow.AfterTurnEnded || subject != SkillProgramTriggerSubject.Owner ||
             scope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving)))
            throw new InvalidOperationException(path + ": curse loss requires the actual turn-end cursor and source-owned roster.");
    }
}
