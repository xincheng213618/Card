using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record RecoveryAmountBonusSource(CardConversionSource Source, string GameplayHash,
    int Amount, int? MaximumRecoveryOrdinal);

public sealed record RecoveryAmountBonusReceipt(int SourceSeat, int TargetSeat,
    int BaseRequestedAmount, int ModifiedRequestedAmount, int HpBefore, int HpAfter,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int? ActualTurnRecoveryOrdinal)
{
    public int MaxHpAtApplication { get; init; }
    public long AppliedRecoveryFrameId { get; init; }
    private readonly IReadOnlyList<RecoveryAmountBonusSource> _sources = Array.AsReadOnly(Array.Empty<RecoveryAmountBonusSource>());
    public IReadOnlyList<RecoveryAmountBonusSource> Sources
    {
        get => _sources;
        init => _sources = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record RecoveryAmountBonusAppliedEvent(long RecoveryFrameId, RecoveryAmountBonusReceipt Receipt) : IGameEvent;
public sealed record ActualTurnRecoveryRecordedEvent(long RecoveryFrameId, long ParentFrameId,
    int SourceSeat, int TargetSeat, int TurnNumber, int TurnOwnerSeat, int Ordinal,
    int RequestedAmount, int ActualAmount, int HpBefore, int HpAfter) : IGameEvent;

public sealed partial record RecoveryReplacementFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RecoveryAmountBonusReceipt? AmountBonus { get; init; }
}

public sealed partial class GameEngine
{
    private bool TracksRecoveryAmountBonuses => _contentRegistry.ProgramDependencies
        .HasCardPolicy(SkillProgramCardPolicyKind.OwnRecoveryAmountBonus);

    private bool HasOwnRecoveryAmountSupport(int targetSeat) => TracksRecoveryAmountBonuses &&
        IsValidPlayerSeat(targetSeat) && _players[targetSeat] is { IsAlive: true, HasHpValue: true } target &&
        target.Hp < target.MaxHp && CardPolicies(target, SkillProgramCardPolicyKind.OwnRecoveryAmountBonus).Any();

    // A recipient's prefix is anchored to the real TurnStarted/TurnEnded segment.
    // It counts every positive recovery, including recovery before acquiring a policy.
    private static int? RecoveryOrdinalAt(IReadOnlyList<IGameEvent> history, int endExclusive,
        int targetSeat, int turnNumber, int turnOwnerSeat) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_RecoveryAmountBonuses.RecoveryOrdinalAt(history,endExclusive,targetSeat,turnNumber,turnOwnerSeat);

    private RecoveryAmountBonusReceipt? PrepareOwnRecoveryAmountBonus(PreparedRecoveryApplication application)
    {
        if (!HasOwnRecoveryAmountSupport(application.TargetSeat) || application.RequestedAmount <= 0 || _winner != Winner.None)
            return null;
        var target = _players[application.TargetSeat];
        var history = CompleteProgramEventHistory().ToArray();
        var ordinal = RecoveryOrdinalAt(history, history.Length, target.Seat, _turnNumber, _turnProgression.OwnerSeat);
        var sources = CardPolicies(target, SkillProgramCardPolicyKind.OwnRecoveryAmountBonus)
            .Where(item => item.Policy.MaximumRecoveryOrdinal is null || ordinal is { } n && n <= item.Policy.MaximumRecoveryOrdinal)
            .Select(item => new RecoveryAmountBonusSource(
                new(item.Source.SkillId, item.Policy.Id, target.Seat, item.Source.SkillInstanceId),
                item.Source.Program.GameplayHash, item.Policy.Value, item.Policy.MaximumRecoveryOrdinal)).ToArray();
        if (sources.Length == 0) return null;
        var requested = checked(application.RequestedAmount + sources.Sum(s => s.Amount));
        var actual = Math.Min(requested, Math.Max(0, target.MaxHp - target.Hp));
        if (actual <= 0) return null;
        return new(application.SourceSeat, target.Seat, application.RequestedAmount, requested,
            target.Hp, checked(target.Hp + actual), _turnNumber, _turnProgression.OwnerSeat, ordinal)
            { Sources = sources, MaxHpAtApplication = target.MaxHp };
    }

    private int SupportedRecoveryAmount(RecoveryReplacementFrame recovery, int baseAmount)
    {
        var supported = SupportedCaoSongRecoveryAmount(recovery, baseAmount);
        if (recovery.AmountBonus is not { } bonus) return supported;
        RequireRecoveryAmountBonus(recovery, bonus);
        return Math.Max(supported, bonus.ModifiedRequestedAmount);
    }

    private void RequireRecoveryAmountBonus(RecoveryReplacementFrame frame, RecoveryAmountBonusReceipt bonus)
    {
        if (frame.CandidateIndex is { } invalid && (invalid < 0 || invalid >= frame.Attempt.Candidates.Count))
            throw new InvalidOperationException("Recovery amount lost its selected recipient.");
        var candidate = frame.CandidateIndex is { } selected ? frame.Attempt.Candidates[selected] : null;
        var targetSeat = candidate?.OwnerSeat ?? frame.Attempt.TargetSeat;
        var sourceSeat = candidate is null ? frame.Attempt.SourceSeat : frame.Attempt.TargetSeat;
        var baseAmount = SupportedCaoSongRecoveryAmount(frame, candidate?.RecoveryAmount ?? frame.Attempt.Amount);
        if (bonus.SourceSeat != sourceSeat || bonus.TargetSeat != targetSeat || bonus.BaseRequestedAmount != baseAmount ||
            bonus.BaseRequestedAmount <= 0 || bonus.Sources.Count == 0 ||
            bonus.Sources.Select(s => s.Source).Distinct().Count() != bonus.Sources.Count ||
            bonus.ModifiedRequestedAmount != checked(bonus.BaseRequestedAmount + bonus.Sources.Sum(s => s.Amount)) ||
            bonus.HpAfter <= bonus.HpBefore || bonus.MaxHpAtApplication < 1 ||
            bonus.AppliedRecoveryFrameId <= frame.Id ||
            frame.OriginalRecovered != (candidate is null ? bonus.HpAfter - bonus.HpBefore : 0) ||
            bonus.HpAfter != Math.Min((long)bonus.MaxHpAtApplication, (long)bonus.HpBefore + bonus.ModifiedRequestedAmount) ||
            bonus.ActualTurnNumber < 0 || !IsValidPlayerSeat(bonus.ActualTurnOwnerSeat) ||
            bonus.Sources.Any(s => s.Source.OwnerSeat != targetSeat || s.Amount <= 0 ||
                !_contentRegistry.Skills.TryGetValue(s.Source.SkillId, out var skill) || skill.Program is not { } program ||
                program.GameplayHash != s.GameplayHash || !program.CardPolicies.Any(p => p.Id == s.Source.BindingId &&
                    p.Kind == SkillProgramCardPolicyKind.OwnRecoveryAmountBonus && p.Value == s.Amount &&
                    p.MaximumRecoveryOrdinal == s.MaximumRecoveryOrdinal) ||
                s.MaximumRecoveryOrdinal is { } max && (bonus.ActualTurnRecoveryOrdinal is not { } ordinal || ordinal > max)))
            throw new InvalidOperationException("Recovery amount lost its exact recipient, source policies or atomic payment.");
        var history = CompleteProgramEventHistory().ToArray();
        var facts = history.Select((e, i) => (e, i)).Where(item => item.e is RecoveryAmountBonusAppliedEvent paid && paid.RecoveryFrameId == frame.Id).ToArray();
        if (facts is not [var fact] || fact.e is not RecoveryAmountBonusAppliedEvent issued || !RecoveryAmountBonusEquals(issued.Receipt, bonus) ||
            RecoveryOrdinalAt(history, fact.i, targetSeat, bonus.ActualTurnNumber, bonus.ActualTurnOwnerSeat) != bonus.ActualTurnRecoveryOrdinal)
            throw new InvalidOperationException("Recovery amount lost its unique issued historical payment.");
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 27920)]
    private void RequireAtomicRecoveryAmountBonus(RecoveryAmountBonusAppliedEvent paid)
    {
        if (_resolutionStack.LastOrDefault() is not RecoveryFrame actual ||
            _resolutionStack.SingleOrDefault(f => f.Id == paid.RecoveryFrameId) is not RecoveryReplacementFrame owner ||
            owner.AmountBonus is not { } bonus || !RecoveryAmountBonusEquals(bonus, paid.Receipt) ||
            actual.Id != bonus.AppliedRecoveryFrameId || actual.ParentFrameId != owner.Id ||
            actual.SourceSeat != bonus.SourceSeat || actual.TargetSeat != bonus.TargetSeat ||
            actual.HpBefore != bonus.HpBefore || actual.Amount != bonus.HpAfter - bonus.HpBefore ||
            _players[actual.TargetSeat].Hp != bonus.HpAfter || owner.Stage != RecoveryReplacementStage.RecoveryApplied)
            throw new InvalidOperationException("Recovery bonus must be issued by its exact once-paid native recovery child.");
    }

    // The original short-lived RecoveryFrame is still the atomic payer here.
    // No skill holder filter: later acquisition must see all earlier recoveries.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 2)]
    private void RecordActualTurnRecovery(RecoveryAppliedEvent applied)
    {
        if (!TracksRecoveryAmountBonuses || _resolutionStack.LastOrDefault() is not RecoveryFrame recovery) return;
        var actual = _players[recovery.TargetSeat].Hp - recovery.HpBefore;
        if (actual <= 0) return;
        if (applied.SourceSeat != recovery.SourceSeat || applied.TargetSeat != recovery.TargetSeat ||
            applied.RemainingHp != _players[recovery.TargetSeat].Hp || actual > recovery.Amount || applied.Amount < actual)
            throw new InvalidOperationException("Actual recovery lost its native atomic recovery frame.");
        var history = CompleteProgramEventHistory().ToArray();
        var ordinal = RecoveryOrdinalAt(history, history.Length, recovery.TargetSeat, _turnNumber, _turnProgression.OwnerSeat);
        if (ordinal is null) return;
        if (history.OfType<ActualTurnRecoveryRecordedEvent>().Any(e => e.RecoveryFrameId == recovery.Id))
            throw new InvalidOperationException("A recovery frame cannot pay or record its actual ordinal twice.");
        AdvanceEventRulesAndQueueFact(new ActualTurnRecoveryRecordedEvent(recovery.Id, recovery.ParentFrameId,
            recovery.SourceSeat, recovery.TargetSeat, _turnNumber, _turnProgression.OwnerSeat, ordinal.Value,
            recovery.Amount, actual, recovery.HpBefore, applied.RemainingHp));
    }

    private void AssertRecoveryAmountBonusReceipts()
    {
        foreach (var frame in _resolutionStack.OfType<RecoveryReplacementFrame>())
            if (frame.AmountBonus is { } bonus)
            {
                if (frame.Stage == RecoveryReplacementStage.Choosing)
                    throw new InvalidOperationException("A recovery amount receipt must already own its atomic application.");
                RequireRecoveryAmountBonus(frame, bonus);
            }
    }

    private static bool RecoveryAmountBonusEquals(RecoveryAmountBonusReceipt a, RecoveryAmountBonusReceipt b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_RecoveryAmountBonuses.RecoveryAmountBonusEquals(a,b);
}
