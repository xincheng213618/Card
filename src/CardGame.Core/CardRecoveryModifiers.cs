using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramCardUseBenefitPolicy(int RecoveryBonus,
    SkillProgramDamageModifierExpiration Expiration, bool ExpireBooleanStatesAtOwnerTurnStart);

public sealed partial class SkillProgramEffect
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCardUseBenefitPolicy? CardUseBenefitPolicy { get; init; }
}

public sealed record TurnCardRecoveryModifier(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, IReadOnlyList<CardKind> CardKinds,
    int Amount, SkillProgramDamageModifierExpiration Expiration = SkillProgramDamageModifierExpiration.CurrentTurnEnd);
public sealed record CardRecoveryModifierGrantedEvent(TurnCardRecoveryModifier Modifier) : IGameEvent;
public sealed record IssuedCardRecoveryModifier(long GrantSequence, CardUseEffectSource Source, int Amount);
public sealed record IssuedCardRecoveryApplication(int TargetSeat, int BaseAmount, int ModifiedAmount);
public sealed record ProgramCardRecoveryModifiedEvent(long ResolutionId, CardUseEffectSource Source,
    int ActorSeat, int TargetSeat, CardKind CardKind, int BaseAmount, int ModifiedAmount) : IGameEvent;

public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<IssuedCardRecoveryModifier>? _issuedRecoveryModifiers;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IssuedCardRecoveryModifier>? IssuedRecoveryModifiers
    {
        get => _issuedRecoveryModifiers;
        init => _issuedRecoveryModifiers = value is null ? null : Array.AsReadOnly(value.ToArray());
    }

    private readonly IReadOnlyList<IssuedCardRecoveryApplication>? _recoveryModifierApplications;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IssuedCardRecoveryApplication>? RecoveryModifierApplications
    {
        get => _recoveryModifierApplications;
        init => _recoveryModifierApplications = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnCardRecoveryModifier> _recoveryModifiers = [];
    internal IReadOnlyList<TurnCardRecoveryModifier> RecoveryModifiers => _recoveryModifiers;

    internal TurnCardRecoveryModifier GrantRecoveryModifier(int turnNumber, int turnSeat, long parentFrameId,
        int effectIndex, CardUseEffectSource source, IReadOnlyList<CardKind> cardKinds, int amount,
        SkillProgramDamageModifierExpiration expiration = SkillProgramDamageModifierExpiration.CurrentTurnEnd)
    {
        if (amount is <= 0 or > 20 || cardKinds.Count == 0 || !Enum.IsDefined(expiration) ||
            cardKinds.Any(kind => kind is not (CardKind.Peach or CardKind.Alcohol)))
            throw new InvalidOperationException("A recovery modifier requires positive recovery, supported basic cards and a valid expiration.");
        var kinds = Array.AsReadOnly(cardKinds.Distinct().Order().ToArray());
        var existing = _recoveryModifiers.SingleOrDefault(item => item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat || existing.Source != source ||
                existing.Amount != amount || existing.Expiration != expiration || !existing.CardKinds.SequenceEqual(kinds))
                throw new InvalidOperationException("A recovery modifier grant key changed its meaning.");
            return existing;
        }
        var grant = new TurnCardRecoveryModifier(++_grantSequence, turnNumber, turnSeat, parentFrameId,
            effectIndex, source, kinds, amount, expiration);
        _recoveryModifiers.Add(grant);
        return grant;
    }

    internal IReadOnlyList<IssuedCardRecoveryModifier> GetRecoveryModifiers(int turnNumber, int turnSeat,
        int actorSeat, CardKind cardKind) => _recoveryModifiers
        .Where(item => item.Source.OwnerSeat == actorSeat && item.CardKinds.Contains(cardKind) &&
            (item.Expiration == SkillProgramDamageModifierExpiration.NextOwnerTurnStart ||
             item.TurnNumber == turnNumber && item.TurnSeat == turnSeat))
        .OrderBy(item => item.GrantSequence)
        .Select(item => new IssuedCardRecoveryModifier(item.GrantSequence, item.Source, item.Amount)).ToArray();

    internal IReadOnlyList<long> ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(int ownerSeat, int turnNumber) =>
        ExpireRecoveryAndRuleModifiers(ownerSeat, grantedTurn => grantedTurn < turnNumber);
    internal IReadOnlyList<long> ExpireRecoveryAndRuleModifiersOnDeath(int ownerSeat) =>
        ExpireRecoveryAndRuleModifiers(ownerSeat, _ => true);

    private IReadOnlyList<long> ExpireRecoveryAndRuleModifiers(int ownerSeat, Func<int, bool> expiredTurn)
    {
        bool Expires(CardUseEffectSource source, int turn, SkillProgramDamageModifierExpiration expiration) =>
            expiration == SkillProgramDamageModifierExpiration.NextOwnerTurnStart &&
            source.OwnerSeat == ownerSeat && expiredTurn(turn);
        var ids = _recoveryModifiers.Where(item => Expires(item.Source, item.TurnNumber, item.Expiration))
            .Select(item => item.GrantSequence)
            .Concat(_ruleModifiers.Where(item => Expires(item.Source, item.TurnNumber, item.Expiration))
                .Select(item => item.GrantSequence))
            .Concat(_handLimitKindExemptions.Where(item => Expires(item.Source, item.TurnNumber, item.Expiration))
                .Select(item => item.GrantSequence)).Order().ToArray();
        var expired = ids.ToHashSet();
        _recoveryModifiers.RemoveAll(item => expired.Contains(item.GrantSequence));
        _ruleModifiers.RemoveAll(item => expired.Contains(item.GrantSequence));
        _handLimitKindExemptions.RemoveAll(item => expired.Contains(item.GrantSequence));
        return ids;
    }

    private bool RecoveryModifiersAreInvalid() => _recoveryModifiers.Any(item =>
        item.Amount is <= 0 or > 20 || item.CardKinds.Count == 0 ||
        item.CardKinds.Distinct().Count() != item.CardKinds.Count ||
        item.CardKinds.Any(kind => kind is not (CardKind.Peach or CardKind.Alcohol)) ||
        !Enum.IsDefined(item.Expiration) || item.TurnNumber <= 0 || item.Source.OwnerSeat < 0);
}
