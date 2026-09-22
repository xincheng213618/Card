namespace CardGame.Core;

[Flags]
public enum CardUseCategories
{
    None = 0,
    Basic = 1,
    InstantTrick = 2,
    DelayedTrick = 4,
    Equipment = 8
}

public enum SkillEffectCondition
{
    Always,
    PindianWon,
    PindianNotWon
}

public sealed record GrantNextCardTargetAdjustment(
    CardUseCategories Categories,
    int MinimumTargets = 1,
    bool AllowAdd = true,
    bool AllowRemove = true,
    bool IgnoreExtraTargetDistance = true,
    SkillEffectCondition When = SkillEffectCondition.Always) : SkillModuleEffect;

public sealed record ForbidCardUseUntilTurnEnd(
    CardUseCategories Categories,
    SkillEffectCondition When = SkillEffectCondition.Always) : SkillModuleEffect;

/// <summary>The stable content binding that granted a turn-scoped card-use effect.</summary>
public sealed record CardUseEffectSource(
    string SkillId,
    string BindingId,
    int OwnerSeat,
    string SkillInstanceId);

public sealed record TurnCardTargetAdjustment(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    CardUseCategories Categories,
    int MinimumTargets,
    bool AllowAdd,
    bool AllowRemove,
    bool IgnoreExtraTargetDistance);

public sealed record TurnCardUseProhibition(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    CardUseCategories Categories);

/// <summary>
/// A turn-scoped modifier for damage caused by a card the owner actually used.
/// Source and card user must remain the owner, and propagated chain damage never matches.
/// </summary>
public sealed record TurnCardDamageModifier(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    IReadOnlyList<CardKind> CardKinds,
    int Amount);

public sealed record TurnCardActionProhibition(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    IReadOnlyList<CardKind> CardKinds,
    IReadOnlyList<CardActionType> ActionTypes);

public sealed record TurnRuleModifier(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    SkillRuleQuery Query,
    SkillRuleOperation Operation,
    int Amount);

public sealed record TurnCardTargetRestriction(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    SkillProgramCardTargetRestriction Restriction);

public sealed record TurnCardConversion(
    long GrantSequence,
    int TurnNumber,
    int TurnSeat,
    long ParentFrameId,
    int EffectIndex,
    CardUseEffectSource Source,
    string SourceBind,
    SkillProgramCardColorRelation ColorRelation,
    bool BoundCardIsRed,
    CardKind OutputKind);

public sealed record CardTargetAdjustmentGrantedEvent(TurnCardTargetAdjustment Adjustment) : IGameEvent;

public sealed record CardUseProhibitionGrantedEvent(TurnCardUseProhibition Prohibition) : IGameEvent;

public sealed record CardDamageModifierGrantedEvent(TurnCardDamageModifier Modifier) : IGameEvent;
public sealed record CardActionProhibitionGrantedEvent(TurnCardActionProhibition Prohibition) : IGameEvent;
public sealed record TurnRuleModifierGrantedEvent(TurnRuleModifier Modifier) : IGameEvent;
public sealed record CardTargetRestrictionGrantedEvent(TurnCardTargetRestriction Restriction) : IGameEvent;
public sealed record CardConversionGrantedEvent(TurnCardConversion Conversion) : IGameEvent;

public sealed record ProgramCardDamageModifiedEvent(
    long ResolutionId,
    CardUseEffectSource Source,
    int TargetSeat,
    CardKind CardKind,
    int BaseAmount,
    int ModifiedAmount) : IGameEvent;

public sealed record TurnCardUseEffectsExpiredEvent(
    int TurnNumber,
    int TurnSeat,
    IReadOnlyList<long> GrantSequences) : IGameEvent;

/// <summary>Maps the final effective card kind to the categories used by generic rules.</summary>
public static class CardUseCategoryCatalog
{
    public const CardUseCategories All = CardUseCategories.Basic |
                                         CardUseCategories.InstantTrick |
                                         CardUseCategories.DelayedTrick |
                                         CardUseCategories.Equipment;

    public static CardUseCategories Get(CardKind effectiveKind)
    {
        if (EquipmentCatalog.IsEquipment(effectiveKind)) return CardUseCategories.Equipment;
        if (effectiveKind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning)
            return CardUseCategories.DelayedTrick;
        return CardCatalog.Get(effectiveKind).CategoryName switch
        {
            "基本牌" => CardUseCategories.Basic,
            "锦囊牌" => CardUseCategories.InstantTrick,
            _ => throw new InvalidOperationException(
                $"Card kind '{effectiveKind}' has no supported card-use category.")
        };
    }

    internal static bool IsValid(CardUseCategories categories) =>
        categories != CardUseCategories.None && (categories & ~All) == 0;
}

/// <summary>
/// Deterministic turn-scoped state. Checkpoints rebuild it from the accepted
/// command prefix; stable frame/effect keys make a resumed instruction idempotent.
/// </summary>
internal sealed class TurnCardUseEffectStore
{
    private readonly List<TurnCardTargetAdjustment> _targetAdjustments = [];
    private readonly List<TurnCardUseProhibition> _prohibitions = [];
    private readonly List<TurnCardDamageModifier> _damageModifiers = [];
    private readonly List<TurnCardActionProhibition> _actionProhibitions = [];
    private readonly List<TurnRuleModifier> _ruleModifiers = [];
    private readonly List<TurnCardTargetRestriction> _targetRestrictions = [];
    private readonly List<TurnCardConversion> _conversions = [];
    private long _grantSequence;

    internal IReadOnlyList<TurnCardTargetAdjustment> TargetAdjustments => _targetAdjustments;
    internal IReadOnlyList<TurnCardUseProhibition> Prohibitions => _prohibitions;
    internal IReadOnlyList<TurnCardDamageModifier> DamageModifiers => _damageModifiers;
    internal IReadOnlyList<TurnCardActionProhibition> ActionProhibitions => _actionProhibitions;
    internal IReadOnlyList<TurnRuleModifier> RuleModifiers => _ruleModifiers;
    internal IReadOnlyList<TurnCardTargetRestriction> TargetRestrictions => _targetRestrictions;
    internal IReadOnlyList<TurnCardConversion> Conversions => _conversions;

    internal TurnCardTargetAdjustment GrantTargetAdjustment(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        GrantNextCardTargetAdjustment effect)
    {
        var existing = _targetAdjustments.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || existing.Categories != effect.Categories ||
                existing.MinimumTargets != effect.MinimumTargets || existing.AllowAdd != effect.AllowAdd ||
                existing.AllowRemove != effect.AllowRemove ||
                existing.IgnoreExtraTargetDistance != effect.IgnoreExtraTargetDistance)
            {
                throw new InvalidOperationException("A target-adjustment grant key changed its meaning.");
            }
            return existing;
        }

        var granted = new TurnCardTargetAdjustment(
            ++_grantSequence,
            turnNumber,
            turnSeat,
            parentFrameId,
            effectIndex,
            source,
            effect.Categories,
            effect.MinimumTargets,
            effect.AllowAdd,
            effect.AllowRemove,
            effect.IgnoreExtraTargetDistance);
        _targetAdjustments.Add(granted);
        return granted;
    }

    internal TurnCardUseProhibition GrantProhibition(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        ForbidCardUseUntilTurnEnd effect)
    {
        var existing = _prohibitions.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || existing.Categories != effect.Categories)
            {
                throw new InvalidOperationException("A card-use prohibition grant key changed its meaning.");
            }
            return existing;
        }

        var granted = new TurnCardUseProhibition(
            ++_grantSequence,
            turnNumber,
            turnSeat,
            parentFrameId,
            effectIndex,
            source,
            effect.Categories);
        _prohibitions.Add(granted);
        return granted;
    }

    internal TurnCardDamageModifier GrantDamageModifier(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        IReadOnlyList<CardKind> cardKinds,
        int amount)
    {
        var frozenKinds = Array.AsReadOnly(cardKinds.Distinct().Order().ToArray());
        var existing = _damageModifiers.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || !existing.CardKinds.SequenceEqual(frozenKinds) ||
                existing.Amount != amount)
            {
                throw new InvalidOperationException("A card-damage modifier grant key changed its meaning.");
            }
            return existing;
        }

        var granted = new TurnCardDamageModifier(
            ++_grantSequence,
            turnNumber,
            turnSeat,
            parentFrameId,
            effectIndex,
            source,
            frozenKinds,
            amount);
        _damageModifiers.Add(granted);
        return granted;
    }

    internal TurnCardActionProhibition GrantActionProhibition(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        IReadOnlyList<CardKind> cardKinds,
        IReadOnlyList<CardActionType> actionTypes)
    {
        var frozenKinds = Array.AsReadOnly(cardKinds.Distinct().Order().ToArray());
        var frozenActions = Array.AsReadOnly(actionTypes.Distinct().Order().ToArray());
        var existing = _actionProhibitions.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || !existing.CardKinds.SequenceEqual(frozenKinds) ||
                !existing.ActionTypes.SequenceEqual(frozenActions))
                throw new InvalidOperationException("A card-action prohibition grant key changed its meaning.");
            return existing;
        }

        var granted = new TurnCardActionProhibition(
            ++_grantSequence, turnNumber, turnSeat, parentFrameId, effectIndex, source,
            frozenKinds, frozenActions);
        _actionProhibitions.Add(granted);
        return granted;
    }

    internal TurnRuleModifier GrantRuleModifier(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        SkillRuleQuery query,
        SkillRuleOperation operation,
        int amount)
    {
        var existing = _ruleModifiers.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || existing.Query != query ||
                existing.Operation != operation || existing.Amount != amount)
                throw new InvalidOperationException("A turn rule-modifier grant key changed its meaning.");
            return existing;
        }

        var granted = new TurnRuleModifier(
            ++_grantSequence, turnNumber, turnSeat, parentFrameId, effectIndex, source,
            query, operation, amount);
        _ruleModifiers.Add(granted);
        return granted;
    }

    internal TurnCardTargetRestriction GrantTargetRestriction(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        SkillProgramCardTargetRestriction restriction)
    {
        var existing = _targetRestrictions.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || existing.Restriction != restriction)
                throw new InvalidOperationException("A card-target restriction grant key changed its meaning.");
            return existing;
        }

        var granted = new TurnCardTargetRestriction(
            ++_grantSequence, turnNumber, turnSeat, parentFrameId, effectIndex, source, restriction);
        _targetRestrictions.Add(granted);
        return granted;
    }

    internal TurnCardConversion GrantConversion(
        int turnNumber,
        int turnSeat,
        long parentFrameId,
        int effectIndex,
        CardUseEffectSource source,
        string sourceBind,
        SkillProgramCardColorRelation colorRelation,
        bool boundCardIsRed,
        CardKind outputKind)
    {
        var existing = _conversions.SingleOrDefault(item =>
            item.ParentFrameId == parentFrameId && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.TurnNumber != turnNumber || existing.TurnSeat != turnSeat ||
                existing.Source != source || existing.SourceBind != sourceBind ||
                existing.ColorRelation != colorRelation || existing.BoundCardIsRed != boundCardIsRed ||
                existing.OutputKind != outputKind)
                throw new InvalidOperationException("A card-conversion grant key changed its meaning.");
            return existing;
        }

        var granted = new TurnCardConversion(
            ++_grantSequence, turnNumber, turnSeat, parentFrameId, effectIndex, source,
            sourceBind, colorRelation, boundCardIsRed, outputKind);
        _conversions.Add(granted);
        return granted;
    }

    internal bool IsCardUseForbidden(
        int turnNumber,
        int turnSeat,
        int actorSeat,
        CardKind effectiveKind,
        CardActionType actionType)
    {
        var category = CardUseCategoryCatalog.Get(effectiveKind);
        return actionType == CardActionType.Use && _prohibitions.Any(item =>
            item.TurnNumber == turnNumber &&
            item.TurnSeat == turnSeat &&
            item.Source.OwnerSeat == actorSeat &&
            (item.Categories & category) != 0) ||
            _actionProhibitions.Any(item =>
                item.TurnNumber == turnNumber &&
                item.TurnSeat == turnSeat &&
                item.Source.OwnerSeat == actorSeat &&
                item.CardKinds.Contains(effectiveKind) &&
                item.ActionTypes.Contains(actionType));
    }

    internal IReadOnlyList<TurnRuleModifier> GetRuleModifiers(
        int turnNumber,
        int turnSeat,
        int actorSeat,
        SkillRuleQuery query) =>
        _ruleModifiers.Where(item =>
                item.TurnNumber == turnNumber && item.TurnSeat == turnSeat &&
                item.Source.OwnerSeat == actorSeat && item.Query == query)
            .OrderBy(item => item.GrantSequence)
            .ToArray();

    internal bool HasTargetRestriction(
        int turnNumber,
        int turnSeat,
        int actorSeat,
        SkillProgramCardTargetRestriction restriction) =>
        _targetRestrictions.Any(item =>
            item.TurnNumber == turnNumber && item.TurnSeat == turnSeat &&
            item.Source.OwnerSeat == actorSeat && item.Restriction == restriction);

    internal IReadOnlyList<TurnCardConversion> GetConversions(
        int turnNumber,
        int turnSeat,
        int actorSeat,
        CardKind outputKind,
        bool inputIsRed) =>
        _conversions.Where(item =>
                item.TurnNumber == turnNumber && item.TurnSeat == turnSeat &&
                item.Source.OwnerSeat == actorSeat && item.OutputKind == outputKind &&
                item.ColorRelation == SkillProgramCardColorRelation.OppositeBoundCard &&
                item.BoundCardIsRed != inputIsRed)
            .OrderBy(item => item.GrantSequence)
            .ToArray();

    internal IReadOnlyList<TurnCardTargetAdjustment> GetTargetAdjustments(
        int turnNumber,
        int turnSeat,
        int actorSeat,
        CardKind effectiveKind)
    {
        var category = CardUseCategoryCatalog.Get(effectiveKind);
        return _targetAdjustments
            .Where(item =>
                item.TurnNumber == turnNumber &&
                item.TurnSeat == turnSeat &&
                item.Source.OwnerSeat == actorSeat &&
                (item.Categories & category) != 0)
            .OrderBy(item => item.GrantSequence)
            .ToArray();
    }

    internal IReadOnlyList<TurnCardDamageModifier> GetDamageModifiers(
        int turnNumber,
        int turnSeat,
        int sourceSeat,
        int cardUserSeat,
        CardKind effectiveKind,
        bool isChainPropagation)
    {
        if (isChainPropagation || sourceSeat != cardUserSeat) return [];
        return _damageModifiers
            .Where(item =>
                item.TurnNumber == turnNumber &&
                item.TurnSeat == turnSeat &&
                item.Source.OwnerSeat == sourceSeat &&
                item.CardKinds.Contains(effectiveKind))
            .OrderBy(item => item.GrantSequence)
            .ToArray();
    }

    internal bool ConsumeTargetAdjustment(long grantSequence)
    {
        var index = _targetAdjustments.FindIndex(item => item.GrantSequence == grantSequence);
        if (index < 0) return false;
        _targetAdjustments.RemoveAt(index);
        return true;
    }

    internal IReadOnlyList<long> ExpireTurn(int turnNumber, int turnSeat)
    {
        var expired = _targetAdjustments
            .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
            .Select(item => item.GrantSequence)
            .Concat(_prohibitions
                .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
                .Select(item => item.GrantSequence))
            .Concat(_damageModifiers
                .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
                .Select(item => item.GrantSequence))
            .Concat(_actionProhibitions
                .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
                .Select(item => item.GrantSequence))
            .Concat(_ruleModifiers
                .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
                .Select(item => item.GrantSequence))
            .Concat(_targetRestrictions
                .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
                .Select(item => item.GrantSequence))
            .Concat(_conversions
                .Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat)
                .Select(item => item.GrantSequence))
            .Order()
            .ToArray();
        if (expired.Length == 0) return expired;
        var expiredSet = expired.ToHashSet();
        _targetAdjustments.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        _prohibitions.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        _damageModifiers.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        _actionProhibitions.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        _ruleModifiers.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        _targetRestrictions.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        _conversions.RemoveAll(item => expiredSet.Contains(item.GrantSequence));
        return expired;
    }

    internal void AssertInvariants()
    {
        var all = _targetAdjustments.Select(item => item.GrantSequence)
            .Concat(_prohibitions.Select(item => item.GrantSequence))
            .Concat(_damageModifiers.Select(item => item.GrantSequence))
            .Concat(_actionProhibitions.Select(item => item.GrantSequence))
            .Concat(_ruleModifiers.Select(item => item.GrantSequence))
            .Concat(_targetRestrictions.Select(item => item.GrantSequence))
            .Concat(_conversions.Select(item => item.GrantSequence)).ToArray();
        if (all.Length != all.Distinct().Count() || all.Any(sequence => sequence <= 0) ||
            all.Any(sequence => sequence > _grantSequence) ||
            _targetAdjustments.Any(item => !CardUseCategoryCatalog.IsValid(item.Categories) ||
                item.MinimumTargets < 1 || !item.AllowAdd && !item.AllowRemove) ||
            _prohibitions.Any(item => !CardUseCategoryCatalog.IsValid(item.Categories)) ||
            _damageModifiers.Any(item => item.Amount <= 0 || item.CardKinds.Count == 0 ||
                item.CardKinds.Distinct().Count() != item.CardKinds.Count) ||
            _actionProhibitions.Any(item => item.CardKinds.Count == 0 || item.ActionTypes.Count == 0 ||
                item.CardKinds.Distinct().Count() != item.CardKinds.Count ||
                item.ActionTypes.Distinct().Count() != item.ActionTypes.Count) ||
            _ruleModifiers.Any(item =>
                item.Query == SkillRuleQuery.SlashLimit &&
                    (item.Operation != SkillRuleOperation.Add || item.Amount <= 0) ||
                item.Query == SkillRuleQuery.SlashDistanceLimit &&
                    item.Operation != SkillRuleOperation.Unlimited ||
                item.Query is not (SkillRuleQuery.SlashLimit or SkillRuleQuery.SlashDistanceLimit)) ||
            _targetRestrictions.Any(item => item.Restriction != SkillProgramCardTargetRestriction.SelfOnly) ||
            _conversions.Any(item => string.IsNullOrWhiteSpace(item.SourceBind) ||
                item.ColorRelation != SkillProgramCardColorRelation.OppositeBoundCard ||
                item.OutputKind != CardKind.Duel))
        {
            throw new InvalidOperationException("Turn card-use effects are inconsistent.");
        }
    }
}
