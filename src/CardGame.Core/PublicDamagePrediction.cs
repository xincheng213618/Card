namespace CardGame.Core;

/// <summary>Public arithmetic only; these estimates neither finalize nor issue a damage attempt.</summary>
public enum PublicDamagePaymentKind { Damage, HpLoss, MaximumHpLoss, ClampedHpLoss }

public enum PublicHealthEffectKind { Damage, HpLoss, MaximumHpLoss, GrantArmor, Recover, ChangeMaximumHp, ClampedHpLoss }

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
        if (armor < 0 || amount < 0 || maxHp is < 0 || !Enum.IsDefined(paymentKind))
            throw new ArgumentOutOfRangeException(nameof(amount), "Armor, amount and maximum HP must be nonnegative and use a known payment kind.");
        var maximum = maxHp ?? Math.Max(0, hp);
        var absorbed = paymentKind == PublicDamagePaymentKind.Damage ? Math.Min(armor, amount) : 0;
        var afterMaximum = paymentKind == PublicDamagePaymentKind.MaximumHpLoss ? Math.Max(0, maximum - amount) : maximum;
        var loss = paymentKind switch
        {
            PublicDamagePaymentKind.MaximumHpLoss => (int)Math.Max(0L, (long)hp - afterMaximum),
            PublicDamagePaymentKind.ClampedHpLoss => Math.Min(Math.Max(0, hp), amount),
            _ => amount - absorbed
        };
        // Damage and HP loss preserve the full rescue debt. The explicit legacy
        // clamped payment remains distinct; maximum HP only clamps downwards.
        var afterHp = paymentKind switch
        {
            PublicDamagePaymentKind.Damage => checked(hp - loss),
            PublicDamagePaymentKind.HpLoss => checked(hp - loss),
            PublicDamagePaymentKind.ClampedHpLoss => (int)Math.Max(0L, (long)hp - amount),
            _ => Math.Min(hp, afterMaximum)
        };
        return new(hp, armor, paymentKind == PublicDamagePaymentKind.Damage ? amount : 0,
            absorbed, loss, afterHp, armor - absorbed, afterMaximum,
            paymentKind is PublicDamagePaymentKind.HpLoss or PublicDamagePaymentKind.ClampedHpLoss ? loss : 0,
            paymentKind == PublicDamagePaymentKind.MaximumHpLoss ? Math.Min(maximum, amount) : 0);
    }
}

public sealed record PublicDamageSequencePrediction(int HpLossAmount, int DamageAmount, int ArmorAbsorbed,
    int DirectHpLossAmount, int MaximumHpLossAmount, int HpAfter, int ArmorAfter, int MaxHpAfter, bool IsLethal)
{
    public static PublicDamageSequencePrediction Calculate(int hp, int maxHp, int armor,
        IEnumerable<PublicHealthEffect> effects, int? incomingDamageCap = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (maxHp < 0 || armor < 0 || incomingDamageCap is <= 0) throw new ArgumentOutOfRangeException(nameof(hp));
        var loss = 0; var damage = 0; var armorLoss = 0; var direct = 0; var maximumLoss = 0;
        // This records newly harmful payments, not a dying state that existed
        // before a rescue estimate. Once issued, later recovery cannot undo it.
        var lethal = false;
        foreach (var effect in effects)
        {
            if (effect is null || !Enum.IsDefined(effect.Kind) ||
                effect.Amount < 0 && effect.Kind != PublicHealthEffectKind.ChangeMaximumHp)
                throw new ArgumentException("A public health effect is invalid.", nameof(effects));
            if (effect.Kind == PublicHealthEffectKind.GrantArmor) armor = checked(armor + effect.Amount);
            else if (effect.Kind == PublicHealthEffectKind.Recover)
                hp = checked(hp + (int)Math.Min(effect.Amount, Math.Max(0L, (long)maxHp - hp)));
            else if (effect.Kind == PublicHealthEffectKind.ChangeMaximumHp)
            {
                // The existing program operation keeps at least one maximum HP.
                maxHp = Math.Max(1, checked(maxHp + effect.Amount));
                var clamp = (int)Math.Max(0L, (long)hp - maxHp); hp -= clamp; loss = checked(loss + clamp);
            }
            else
            {
                var amount = effect.Kind == PublicHealthEffectKind.Damage && incomingDamageCap is { } cap
                    ? Math.Min(effect.Amount, cap) : effect.Amount;
                var payment = PublicDamagePrediction.Calculate(hp, armor, amount, effect.Kind switch
                {
                    PublicHealthEffectKind.HpLoss => PublicDamagePaymentKind.HpLoss,
                    PublicHealthEffectKind.ClampedHpLoss => PublicDamagePaymentKind.ClampedHpLoss,
                    PublicHealthEffectKind.MaximumHpLoss => PublicDamagePaymentKind.MaximumHpLoss,
                    _ => PublicDamagePaymentKind.Damage
                }, maxHp);
                hp = payment.HpAfter; armor = payment.ArmorAfter; maxHp = payment.MaxHpAfter;
                loss = checked(loss + payment.HpLossAmount); damage = checked(damage + payment.DamageAmount);
                armorLoss = checked(armorLoss + payment.ArmorAbsorbed);
                direct = checked(direct + payment.DirectHpLossAmount);
                maximumLoss = checked(maximumLoss + payment.MaximumHpLossAmount);
                lethal |= payment.IsLethal && (payment.HpLossAmount > 0 || payment.MaximumHpLossAmount > 0);
            }
        }
        return new(loss, damage, armorLoss, direct, maximumLoss, hp, armor, maxHp, lethal);
    }
}
