namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheZhanhunDaoSkill = "shanhe:zhanhun-dao";

    private sealed partial class ProgramSkillHost : IShanheZhanhunDaoProgramHost
    {
        public SkillProgramStepOutcome ResolveShanheZhanhunDao(ProgramSkillFrame frame) => engine.ResolveShanheZhanhunDao(frame);
    }

    private bool ResolveShanheZhanhunDaoEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (card.Kind != CardKind.ZhanhunDao) return false;
        var grantId = EquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } oldSeat })
            _players[oldSeat].SkillGrants.RemoveGrant(grantId);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } newSeat } && _contentRegistry.Skills.ContainsKey(ShanheZhanhunDaoSkill))
            _players[newSeat].SkillGrants.Grant(new SkillGrant(grantId, ShanheZhanhunDaoSkill, grantId, grantId));
        return true;
    }

    private bool HasActiveShanheZhanhunDao(int ownerSeat, string skillId, string skillInstanceId) =>
        skillId == ShanheZhanhunDaoSkill && GetEquipment(_players[ownerSeat]).Any(card => card.Kind == CardKind.ZhanhunDao &&
            IsActiveProgramSourceEquipmentCard(ownerSeat, skillId, skillInstanceId, card));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5754, SkillProgramEffectOp.ResolveShanheZhanhunDao)]
    private bool CanOfferShanheZhanhunDao(ProgramTriggerLegalityContext gate) =>
        HasActiveShanheZhanhunDao(gate.OwnerSeat, gate.SkillId, gate.Candidate.SkillInstanceId) &&
        CurrentDamageAttempt is { IsSourceLess: false } damage && damage.SourceSeat == gate.OwnerSeat &&
        gate.WindowContext is { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } targetSeat } &&
        damage.TargetSeat == targetSeat && _players[targetSeat] is { IsAlive: true, HasHpValue: true } target && IsRuleWounded(target);

    private SkillProgramStepOutcome ResolveShanheZhanhunDao(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!HasActiveShanheZhanhunDao(f.OwnerSeat, f.SkillId, f.SkillInstanceId) ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame window ||
            window.Id != context.ParentFrameId || window.Prevented || window.SourceSeat != f.OwnerSeat ||
            CurrentDamageAttempt is not { IsSourceLess: false } damage || damage.SourceSeat != f.OwnerSeat ||
            damage.TargetSeat != window.TargetSeat || window.Amount <= 0 ||
            _players[window.TargetSeat] is not { IsAlive: true, HasHpValue: true } target || !IsRuleWounded(target))
            throw new InvalidOperationException("Zhanhun Dao requires its actual equipped source and current wounded damage target.");

        var before = target.MaxHp;
        var after = HasWushuangTactic(target.Seat, 2020) ? 7 : target.HasShenPangTongKunyu ? 1 : Math.Max(0, checked(before - 1));
        PreventProgramCurrentDamage(f);
        if (!TryApplyHappyQiaoMaximumHp(f.Id, target.Seat, after, f.SkillId))
        {
            var hp = target.Hp;
            target.MaxHp = after;
            target.Hp = Math.Min(hp, after);
            AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(target.Seat, after - before, after, f.SkillId) { HpBeforeClamp = hp });
        }
        AdvanceEventRulesAndQueueFact(new ShanheZhanhunDaoResolvedEvent(f.Id, window.Id, damage.ResolutionId,
            f.OwnerSeat, target.Seat, window.Amount, before, after));
        if (after == 0 && target.IsAlive)
        {
            BeginPlayerDeath(f.Id, target, null, null, null, causingProgramSkillFrameId: f.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        return SkillProgramStepOutcome.Continue;
    }
}
