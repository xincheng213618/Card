namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheLeizhenSkill = "shanhe:leizhen";
    private bool HasShanheLeizhenCapability => _contentRegistry?.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveShanheLeizhenUse) == true;

    private bool ResolveShanheLeizhenEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (card.Kind != CardKind.Leizhen) return false;
        var id = EquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } old }) _players[old].SkillGrants.RemoveGrant(id);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } next } && _contentRegistry.Skills.ContainsKey(ShanheLeizhenSkill))
            _players[next].SkillGrants.Grant(new SkillGrant(id, ShanheLeizhenSkill, id, id));
        return true;
    }

    private ShanheLeizhenChargeEarnedEvent? PendingShanheLeizhenCharge(int owner)
    {
        var spent = ProgramEventHistory<ShanheLeizhenChargeSpentEvent>().Where(e => e.OwnerSeat == owner)
            .Select(e => e.EarningActionId).ToHashSet();
        var earned = ProgramEventHistory<ShanheLeizhenChargeEarnedEvent>().Where(e => e.OwnerSeat == owner && !spent.Contains(e.ActionId)).ToArray();
        if (earned.Length > 1) throw new InvalidOperationException("Leizhen's already-paid next-Slash benefit cannot stack.");
        return earned.SingleOrDefault();
    }

    // Proposed actual Uses only. An already-paid charge belongs to its actor,
    // so the current weapon/skill grant must not be required after unequipping.
    // Incoming predictions already read the issued native base and never add this.
    private int ShanheLeizhenPublicActionBonus(int actorSeat, CardKind? effectiveKind) =>
        HasShanheLeizhenCapability && IsValidPlayerSeat(actorSeat) && _players[actorSeat].IsAlive &&
        effectiveKind is { } kind && IsSlashCard(kind) && PendingShanheLeizhenCharge(actorSeat) is not null ? 1 : 0;

    // Shared committed-use entry calls this after native conversion payment
    // and before any children can use a subsequent card. Response here means
    // a genuine native committed Dodge/Nullification Use, never a played card.
    private void RecordShanheLeizhenCommittedUse(CardActionContext action)
    {
        if (!HasShanheLeizhenCapability || !IsValidPlayerSeat(action.ActorSeat) ||
            action.Type == CardActionType.Response &&
                (action.EffectiveKind is not (CardKind.Dodge or CardKind.Nullification) ||
                 action.ProviderSeat != action.ActorSeat || action.RequesterSeat is not null)) return;
        var owner = _players[action.ActorSeat];
        var count = action.PhysicalCards.Count(c => c.From == CardLocation.Hand(owner.Seat));
        if (count == 0 || !owner.IsAlive || !HasWeaponAbility(owner, CardKind.Leizhen)) return;
        foreach (var weapon in GetEquipment(owner).Where(c => c.Kind == CardKind.Leizhen).OrderBy(c => c.Id))
        {
            // Native equipment commitment follows its actual entry. The newly
            // installed weapon did not own the earlier Hand use that installs it.
            if (action.EffectiveKind == CardKind.Leizhen && action.PhysicalCards.Any(c => c.CardId == weapon.Id)) continue;
            var instance = EquipmentSkillGrantId(weapon)!;
            if (!IsActiveProgramSourceEquipmentCard(owner.Seat, ShanheLeizhenSkill, instance, weapon)) continue;
            var entry = _cardMovements.LastOrDefault(m => m.CardId == weapon.Id && m.To == CardLocation.Equipment(owner.Seat));
            if (entry is null) throw new InvalidOperationException("Leizhen counting requires a real equipped entity and residence origin.");
            if (ProgramEventHistory<ShanheLeizhenHandUseCountedEvent>().Any(e => e.ActionId == action.ActionId && e.WeaponCardId == weapon.Id)) continue;
            var previous = ProgramEventHistory<ShanheLeizhenHandUseCountedEvent>().LastOrDefault(e =>
                e.OwnerSeat == owner.Seat && e.WeaponCardId == weapon.Id && e.EquipmentEntrySequence == entry.Sequence)?.RemainderAfter ?? 0;
            var total = checked(previous + count);
            AdvanceEventRulesAndQueueFact(new ShanheLeizhenHandUseCountedEvent(action.ActionId, owner.Seat,
                weapon.Id, entry.Sequence, count, previous, total % 3));
            if (total >= 3 && PendingShanheLeizhenCharge(owner.Seat) is null)
                AdvanceEventRulesAndQueueFact(new ShanheLeizhenChargeEarnedEvent(action.ActionId, owner.Seat, weapon.Id, entry.Sequence));
        }
    }

    private void ResolveShanheLeizhenUse(ProgramSkillFrame f)
    {
        if (f.SkillId != ShanheLeizhenSkill || f.WindowContext is not
                { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } context } ||
            context.ActorSeat != f.OwnerSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == f.WindowContext.ParentFrameId) is not { } window)
            throw new InvalidOperationException("Leizhen requires the original genuine committed-use actor window.");
        RecordShanheLeizhenCommittedUse(window.Action);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 59402)]
    private void IssueShanheLeizhenSlash(CardUseDeclaredEvent declared)
    {
        if (!HasShanheLeizhenCapability || !IsSlashCard(declared.CardKind) ||
            LifecycleCardUse(declared.ResolutionId) is not { } use || use.SourceSeat != declared.SourceSeat ||
            use.CardKind != declared.CardKind || use.ShanheLeizhen is not null ||
            PendingShanheLeizhenCharge(use.SourceSeat) is not { } charge) return;
        var receipt = new ShanheLeizhenSlashReceipt(charge.ActionId, use.SourceSeat, charge.WeaponCardId, use.Id);
        UpdateLifecycleCardUse(use.Id, current => current with { ShanheLeizhen = receipt });
        AdvanceEventRulesAndQueueFact(new ShanheLeizhenChargeSpentEvent(charge.ActionId, use.SourceSeat, charge.WeaponCardId, use.Id));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 59403)]
    private void ApplyShanheLeizhenNativeBase(CardUsedEvent fact)
    {
        if (!HasShanheLeizhenCapability || !IsSlashCard(fact.CardKind) || ActiveCardAttack is not { } attack ||
            LifecycleCardUse(attack.ResolutionId) is not { ShanheLeizhen: { BaseApplied: false } receipt } use ||
            fact.SourceSeat != use.SourceSeat || fact.TargetSeat != attack.TargetSeat || receipt.OwnerSeat != use.SourceSeat) return;
        var state = GetCardAttackState(use.Id);
        if (state.DamageAmountFinalized || state.DamageWasApplied || state.IsChainPropagation)
            throw new InvalidOperationException("Leizhen must add its already-earned benefit to the initial native Slash base.");
        var before = state.DamageAmount; var after = checked(before + 1);
        UpdateCardAttackState(use.Id, s => s! with { DamageAmount = after });
        UpdateLifecycleCardUse(use.Id, current => current with
        {
            ShanheLeizhen = receipt with { BaseApplied = true, BaseBefore = before, BaseAfter = after },
            ProgramAdjustedSlashBaseDamage = current.ProgramAdjustedSlashBaseDamage is { } original ? checked(original + 1) : null,
            PreparedTargetAttacks = current.PreparedTargetAttacks is { } prepared
                ? Array.AsReadOnly(prepared.Select(s => s with { DamageAmount = checked(s.DamageAmount + 1) }).ToArray()) : null
        });
        AdvanceEventRulesAndQueueFact(new ShanheLeizhenBaseAppliedEvent(use.Id, use.SourceSeat, before, after));
    }

    private void AssertShanheLeizhenUse(CardUseFrame use)
    {
        if (use.ShanheLeizhen is not { } r) return;
        if (r.UseFrameId != use.Id || r.OwnerSeat != use.SourceSeat ||
            ProgramEventHistory<ShanheLeizhenChargeSpentEvent>().Where(e => e.UseFrameId == use.Id).ToArray() is not [var spent] ||
            spent.EarningActionId != r.EarningActionId || spent.OwnerSeat != r.OwnerSeat || spent.WeaponCardId != r.WeaponCardId ||
            ProgramEventHistory<ShanheLeizhenChargeEarnedEvent>().Count(e => e.ActionId == r.EarningActionId &&
                e.OwnerSeat == r.OwnerSeat && e.WeaponCardId == r.WeaponCardId) != 1 ||
            (r.BaseApplied ? r.BaseBefore is not { } before || r.BaseAfter != before + 1 ||
                ProgramEventHistory<ShanheLeizhenBaseAppliedEvent>().Count(e => e.UseFrameId == use.Id &&
                    e.OwnerSeat == use.SourceSeat && e.BaseBefore == before && e.BaseAfter == before + 1) != 1 :
                r.BaseBefore is not null || r.BaseAfter is not null ||
                ProgramEventHistory<ShanheLeizhenBaseAppliedEvent>().Any(e => e.UseFrameId == use.Id)))
            throw new InvalidOperationException("Leizhen lost its once-consumed public benefit and original native Slash base receipt.");
    }
}
