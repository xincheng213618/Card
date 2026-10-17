namespace CardGame.Core;

/// <summary>Public arithmetic only; these estimates neither finalize nor issue a damage attempt.</summary>
public enum PublicDamagePaymentKind { Damage, HpLoss, MaximumHpLoss }

public enum PublicHealthEffectKind { Damage, HpLoss, MaximumHpLoss, GrantArmor, Recover, ChangeMaximumHp }

public sealed record PublicHealthEffect(PublicHealthEffectKind Kind, int Amount);

public sealed record PublicDamagePrediction(int HpBefore, int ArmorBefore, int DamageAmount,
    int ArmorAbsorbed, int HpLossAmount, int HpAfter, int ArmorAfter, int MaxHpAfter,
    int DirectHpLossAmount = 0, int MaximumHpLossAmount = 0)
{
    public int ActualHpLost => Math.Max(0, HpBefore - HpAfter);
    public bool IsLethal => HpAfter <= 0 || MaxHpAfter <= 0;

    public static PublicDamagePrediction Calculate(int hp, int armor, int amount,
        PublicDamagePaymentKind paymentKind = PublicDamagePaymentKind.Damage, int? maxHp = null)
    {
        if (hp < 0 || armor < 0 || amount < 0 || maxHp is < 0 || !Enum.IsDefined(paymentKind))
            throw new ArgumentOutOfRangeException(nameof(amount), "Public payment inputs must be nonnegative and use a known payment kind.");
        var maximum = maxHp ?? hp;
        var absorbed = paymentKind == PublicDamagePaymentKind.Damage ? Math.Min(armor, amount) : 0;
        var afterMaximum = paymentKind == PublicDamagePaymentKind.MaximumHpLoss ? Math.Max(0, maximum - amount) : maximum;
        var loss = paymentKind == PublicDamagePaymentKind.MaximumHpLoss
            ? Math.Max(0, hp - afterMaximum) : amount - absorbed;
        return new(hp, armor, paymentKind == PublicDamagePaymentKind.Damage ? amount : 0,
            absorbed, loss, Math.Max(0, hp - loss), armor - absorbed, afterMaximum,
            paymentKind == PublicDamagePaymentKind.HpLoss ? amount : 0,
            paymentKind == PublicDamagePaymentKind.MaximumHpLoss ? Math.Min(maximum, amount) : 0);
    }
}

public sealed record PublicDamageSequencePrediction(int HpLossAmount, int DamageAmount, int ArmorAbsorbed,
    int DirectHpLossAmount, int MaximumHpLossAmount, int HpAfter, int ArmorAfter, int MaxHpAfter, bool IsLethal)
{
    public static PublicDamageSequencePrediction Calculate(int hp, int maxHp, int armor,
        IEnumerable<PublicHealthEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (hp < 0 || maxHp < 0 || armor < 0) throw new ArgumentOutOfRangeException(nameof(hp));
        var loss = 0; var damage = 0; var armorLoss = 0; var direct = 0; var maximumLoss = 0;
        var lethal = hp <= 0 || maxHp <= 0;
        foreach (var effect in effects)
        {
            if (effect is null || !Enum.IsDefined(effect.Kind) ||
                effect.Amount < 0 && effect.Kind != PublicHealthEffectKind.ChangeMaximumHp)
                throw new ArgumentException("A public health effect is invalid.", nameof(effects));
            if (effect.Kind == PublicHealthEffectKind.GrantArmor) armor = checked(armor + effect.Amount);
            else if (effect.Kind == PublicHealthEffectKind.Recover) hp = checked(hp + Math.Min(effect.Amount, Math.Max(0, maxHp - hp)));
            else if (effect.Kind == PublicHealthEffectKind.ChangeMaximumHp)
            {
                // The existing program operation keeps at least one maximum HP.
                maxHp = Math.Max(1, checked(maxHp + effect.Amount));
                var clamp = Math.Max(0, hp - maxHp); hp -= clamp; loss = checked(loss + clamp);
            }
            else
            {
                var payment = PublicDamagePrediction.Calculate(hp, armor, effect.Amount, effect.Kind switch
                {
                    PublicHealthEffectKind.HpLoss => PublicDamagePaymentKind.HpLoss,
                    PublicHealthEffectKind.MaximumHpLoss => PublicDamagePaymentKind.MaximumHpLoss,
                    _ => PublicDamagePaymentKind.Damage
                }, maxHp);
                hp = payment.HpAfter; armor = payment.ArmorAfter; maxHp = payment.MaxHpAfter;
                loss = checked(loss + payment.HpLossAmount); damage = checked(damage + payment.DamageAmount);
                armorLoss = checked(armorLoss + payment.ArmorAbsorbed);
                direct = checked(direct + payment.DirectHpLossAmount);
                maximumLoss = checked(maximumLoss + payment.MaximumHpLossAmount);
                lethal |= payment.IsLethal;
            }
        }
        return new(loss, damage, armorLoss, direct, maximumLoss, hp, armor, maxHp, lethal);
    }
}
