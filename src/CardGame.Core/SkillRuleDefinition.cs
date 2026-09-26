namespace CardGame.Core;

/// <summary>Stable skill identity, independent of its optional rule capabilities.</summary>
public interface ISkillRuleIdentity
{
    SkillKind Kind { get; }
    string Name { get; }
}

public sealed record SkillRuleDefinition(
    SkillKind Kind,
    string Name,
    INumericSkillRule? Numeric,
    ICardConversionSkillRule? Conversion,
    ICardUseSkillRule? CardUse,
    IDamageSkillRule? Damage,
    IJudgmentSkillRule? Judgment)
{
    public static SkillRuleDefinition From(ISkillRuleIdentity rule) => new(
        rule.Kind, rule.Name, rule as INumericSkillRule, rule as ICardConversionSkillRule,
        rule as ICardUseSkillRule, rule as IDamageSkillRule, rule as IJudgmentSkillRule);
}

public interface INumericSkillRule
{
    int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => currentCount;
    int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => currentLimit;
    int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) => currentDistance;
    int ModifyIncomingDistance(PlayerSkillContext owner, int currentDistance) => currentDistance;
    int ModifySupplyShortageDistanceLimit(PlayerSkillContext owner, int currentLimit) => currentLimit;
}

public interface ICardConversionSkillRule
{
    bool CanUseAsSlash(PlayerSkillContext owner, Card card) => false;
    bool CanUseAsDismantlement(PlayerSkillContext owner, Card card) => false;
    bool CanUseAsSupplyShortage(PlayerSkillContext owner, Card card) => false;
    bool CanUseAsIndulgence(PlayerSkillContext owner, Card card) => false;
    bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) => false;
    bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => false;
}

public interface ICardUseSkillRule
{
    bool IgnoresTrickDistance(PlayerSkillContext owner, CardKind trickKind) => false;
    bool ProhibitsSlashTarget(PlayerSkillContext owner) => false;
    bool ProhibitsCardTarget(PlayerSkillContext owner, CardKind cardKind) =>
        (cardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
        ProhibitsSlashTarget(owner);
    bool CanSkipDiscardPhase(PlayerSkillContext owner, bool usedOrPlayedSlashDuringPlayPhase) => false;
    int ModifyRequiredResponseCount(ResponseCountSkillContext context, int currentCount) => currentCount;
}

public interface IDamageSkillRule : ISkillRuleIdentity
{
    DamageTriggerScope AfterDamageTriggerScope => DamageTriggerScope.DamagedPlayer;
    bool CanTriggerAfterDamage(DamageSkillContext context)
    {
        if (context.Amount <= 0) return false;
        if (context.TargetSeat is not { } targetSeat)
            return AfterDamageTriggerScope == DamageTriggerScope.DamagedPlayer;
        return AfterDamageTriggerScope switch
        {
            DamageTriggerScope.DamagedPlayer => targetSeat == context.Owner.Seat,
            DamageTriggerScope.DamageSource => context.SourceSeat == context.Owner.Seat,
            DamageTriggerScope.OtherLivingPlayer => targetSeat != context.Owner.Seat,
            DamageTriggerScope.AnyLivingPlayer => true,
            _ => false
        };
    }
    int DamageTriggerPriority => 0;
    string DamageTriggerId => Kind.ToString();
    bool OffersDamageCardChoice(DamageSkillContext context) => false;
    DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) => DamageSkillEffectKind.None;
}

public interface IJudgmentSkillRule : ISkillRuleIdentity
{
    bool CanTriggerBeforeJudgment(JudgmentSkillContext context) => false;
    bool OffersJudgmentCardChoice(JudgmentSkillContext context) => CanTriggerBeforeJudgment(context);
    bool CanClaimResolvedJudgment(JudgmentSkillContext context) => false;
    int JudgmentTriggerPriority => 0;
    string JudgmentTriggerId => Kind.ToString();
}
