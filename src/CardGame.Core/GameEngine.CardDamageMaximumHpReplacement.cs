namespace CardGame.Core;

/// <summary>A frozen replacement owned by the real card's attack state.</summary>
public sealed record CardDamageMaximumHpReplacementReceipt(CardUseEffectSource Source, int RoundNumber,
    int SourceSeat, int TargetSeat, CardKind CardKind, int Amount,
    int MaximumHpBefore, int MaximumHpAfter, int HpBefore, int HpAfter);

public sealed record CardDamageReplacedWithMaximumHpLossEvent(long ParentResolutionId,
    CardDamageMaximumHpReplacementReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    // The attempt already includes card-use Alcohol/Nuzhan. Temporary card
    // modifiers explicitly describe card damage (the existing users are Luoyi).
    // Passive increases join only when their content explicitly declares this
    // layer; no blanket OwnerUsed classification is inferred for old skills.
    private int CalculateCardDamageLayer(IDamageAttempt attack)
    {
        if (attack is not CardAttackHandle card || attack.IsChainPropagation ||
            attack.IsDelayedJudgmentDamage || attack.IsProgramJudgmentDamage ||
            attack.EffectiveCardKind is not { } kind) return attack.DamageAmount;
        if (GetCardAttackState(attack.ResolutionId).CardDamageLayerAmount is { } issued) return issued;
        if (attack.DamageAmountFinalized)
            throw new InvalidOperationException("Card damage must be selected before damage-only modifiers and prevention.");
        var turn = _turnCardUseEffects.GetDamageModifiers(_turnNumber, _currentSeat,
            attack.SourceSeat, attack.CardUserSeat, kind, false).Sum(m => m.Amount);
        var passive = GetPassiveProgramDamageModifiers(attack, cardDamageOnly: true).Sum(m => m.Amount);
        var amount = checked(attack.DamageAmount + ProgramConversionDamageBonus(card) + turn + passive);
        UpdateCardAttackState(attack.ResolutionId, state => state! with { CardDamageLayerAmount = amount });
        return amount;
    }

    private static string MaximumHpReplacementUsage(string policyId) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_CardDamageMaximumHpReplacement.MaximumHpReplacementUsage(policyId);

    private bool TryReplaceCardDamageWithMaximumHpLoss(IDamageAttempt attack, out bool pending)
    {
        pending = false;
        if (!_contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.FirstRoundCardDamageBecomesMaximumHpLoss) ||
            attack is not CardAttackHandle card || attack.IsSourceLess || attack.IsDelayedJudgmentDamage ||
            attack.IsProgramJudgmentDamage || attack.IsChainPropagation || attack.DamageAmountFinalized ||
            attack.CardUserSeat != attack.SourceSeat || attack.EffectiveCardKind is not { } kind ||
            !IsValidPlayerSeat(attack.SourceSeat) || !_players[attack.SourceSeat].IsAlive ||
            !_players[attack.TargetSeat].IsAlive || !_players[attack.TargetSeat].HasHpValue || _roundNumber < 1)
            return false;
        foreach (var binding in CardPolicies(_players[attack.SourceSeat],
            SkillProgramCardPolicyKind.FirstRoundCardDamageBecomesMaximumHpLoss, kind))
        {
            var usage = MaximumHpReplacementUsage(binding.Policy.Id);
            if (_skillRuntimeState.GetUsage(attack.SourceSeat, binding.Source.SkillId, usage, SkillUsageScope.Round) != 0) continue;
            var amount = CalculateCardDamageLayer(card);
            if (amount <= 0) return false;
            if (TryBeginIdentityAppearanceForDamage(attack, amount)) { pending = true; return true; }
            var target = _players[attack.TargetSeat];
            var afterMaximum = Math.Max(0, target.MaxHp - amount);
            var receipt = new CardDamageMaximumHpReplacementReceipt(new(binding.Source.SkillId, binding.Policy.Id,
                attack.SourceSeat, binding.Source.SkillInstanceId), _roundNumber, attack.SourceSeat, target.Seat,
                kind, amount, target.MaxHp, afterMaximum, target.Hp, Math.Min(target.Hp, afterMaximum));
            if (GetCardAttackState(attack.ResolutionId).MaximumHpReplacement is not null ||
                !_skillRuntimeState.TryConsumeUsage(attack.SourceSeat, binding.Source.SkillId, usage, SkillUsageScope.Round, 1))
                throw new InvalidOperationException("The card damage replacement was already issued.");
            UpdateCardAttackState(attack.ResolutionId, state => state! with { MaximumHpReplacement = receipt });
            AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(attack.SourceSeat, binding.Source.SkillId, usage, SkillUsageScope.Round, 1));
            target.MaxHp = receipt.MaximumHpAfter;
            target.Hp = receipt.HpAfter;
            AdvanceEventRulesAndQueueFact(new CardDamageReplacedWithMaximumHpLossEvent(attack.ResolutionId, receipt));
            AddLog("MaximumHpLost", $"{target.Name} 的体力上限减少 {receipt.MaximumHpBefore - receipt.MaximumHpAfter} 点，剩余 {target.MaxHp}。",
                attack.SourceSeat, target.Seat);
            if (target.MaxHp == 0)
            {
                // Death owns its cleanup and normal death windows. It marks the
                // victim dead before publishing the max-HP fact, so a threshold
                // observer cannot transform a zero-maximum living character.
                pending = true;
                BeginPlayerDeath(attack.ResolutionId, target, null, attack, null,
                    causingCardDamageReplacementId: attack.ResolutionId);
            }
            else PublishMaximumHpDamageReplacement(attack.ResolutionId);
            return true;
        }
        return false;
    }

    private void PublishMaximumHpDamageReplacement(long ownerId)
    {
        var receipt = GetCardAttackState(ownerId).MaximumHpReplacement ??
            throw new InvalidOperationException("Maximum-HP replacement lost its owning attack receipt.");
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(receipt.TargetSeat,
            receipt.MaximumHpAfter - receipt.MaximumHpBefore, receipt.MaximumHpAfter, receipt.Source.SkillId)
            { HpBeforeClamp = receipt.HpBefore });
    }

    private bool IsExactMaximumHpReplacementDeathParent(DeathFrame death, ResolutionFrame parent)
    {
        if (parent.Id != death.ParentFrameId || parent is not (CardUseFrame or ProgramSkillFrame) ||
            GetCardAttackState(parent.Id) is not { MaximumHpReplacement: { } receipt } state) return false;
        return receipt.MaximumHpAfter == 0 && receipt.TargetSeat == death.VictimSeat && death.KillerSeat is null &&
            state.SourceSeat == receipt.SourceSeat && state.TargetSeat == receipt.TargetSeat &&
            !state.DamageWasApplied && !state.DamageAmountFinalized && !state.IsChainPropagation &&
            _players[receipt.TargetSeat].MaxHp == 0 &&
            ProgramEventHistory<CardDamageReplacedWithMaximumHpLossEvent>().Count(e => e.ParentResolutionId == parent.Id && e.Receipt == receipt) == 1;
    }

    private void AssertMaximumHpDamageReplacementReceipts()
    {
        foreach (var frame in _resolutionStack)
        {
            var state = frame switch
            {
                CardUseFrame use => use.CardAttack,
                ProgramSkillFrame program => program.CardAttack,
                JudgmentFrame judgment => judgment.CardAttack,
                _ => null
            };
            if (state?.MaximumHpReplacement is not { } receipt) continue;
            var policy = _contentRegistry.Skills.GetValueOrDefault(receipt.Source.SkillId)?.Program?.CardPolicies
                .SingleOrDefault(p => p.Id == receipt.Source.BindingId);
            if (policy is not { Kind: SkillProgramCardPolicyKind.FirstRoundCardDamageBecomesMaximumHpLoss } ||
                !policy.CardKinds.Contains(receipt.CardKind) || receipt.Source.OwnerSeat != receipt.SourceSeat ||
                string.IsNullOrWhiteSpace(receipt.Source.SkillInstanceId) || receipt.RoundNumber != _roundNumber ||
                receipt.SourceSeat != state.SourceSeat || receipt.TargetSeat != state.TargetSeat ||
                receipt.CardKind != state.EffectiveCardKind || receipt.Amount <= 0 || receipt.Amount != state.CardDamageLayerAmount ||
                receipt.MaximumHpBefore <= 0 || receipt.MaximumHpAfter != Math.Max(0, receipt.MaximumHpBefore - receipt.Amount) ||
                receipt.HpAfter != Math.Min(receipt.HpBefore, receipt.MaximumHpAfter) ||
                state.DamageWasApplied || state.DamageAmountFinalized || state.IsChainPropagation ||
                _skillRuntimeState.GetUsage(receipt.SourceSeat, receipt.Source.SkillId,
                    MaximumHpReplacementUsage(receipt.Source.BindingId), SkillUsageScope.Round) != 1 ||
                ProgramEventHistory<CardDamageReplacedWithMaximumHpLossEvent>().Count(e => e.ParentResolutionId == frame.Id && e.Receipt == receipt) != 1)
                throw new InvalidOperationException("Maximum-HP replacement lost its exact card-layer, round usage or issued source receipt.");
        }
    }
}
