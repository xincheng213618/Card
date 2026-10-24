namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5YimieSkill = "shanhe:s5-yimie-7005";

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5795, SkillProgramEffectOp.ResolveShanheS5Yimie)]
    private bool CanOfferShanheS5Yimie(ProgramTriggerLegalityContext c) =>
        c.SkillId == ShanheS5YimieSkill && c.Owner is { IsAlive: true, HasHpValue: true, Hp: > 0 } &&
        c.Context is { Window: SkillProgramTriggerWindow.BeforeDamageApplied, SourceSeat: { } source, TargetSeat: { } target } &&
        source == c.OwnerSeat && source != target && _players[target].IsAlive && UnambiguousHp(_players[target]) is not null &&
        CurrentDamageAttempt is { IsSourceLess: false, DamageAmount: > 0 } attack && attack.SourceSeat == source && attack.TargetSeat == target &&
        !ProgramEventHistory<ShanheS5YimieFrozenEvent>().Any(e => e.Origin.Source.OwnerSeat == source && e.Origin.TurnNumber == _turnNumber);

    private bool HasShanheS5YimieCostPaid(ProgramSkillFrame f) => f.ShanheS5YimieCost is { } r &&
        ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId &&
            e.TargetSeat == f.OwnerSeat && e.Amount == 1 && e.RemainingHp == r.CostHpBefore - 1) == 1;

    private void RequireShanheS5YimieOrigin(ShanheS5YimieOrigin r)
    {
        if (r.Source.SkillId != ShanheS5YimieSkill || r.ProgramFrameId <= 0 || r.AttackId <= 0 || r.BeforeDamageWindowId <= 0 ||
            !IsValidPlayerSeat(r.Source.OwnerSeat) || !IsValidPlayerSeat(r.TargetSeat) || r.Source.OwnerSeat == r.TargetSeat ||
            r.OriginalDamage <= 0 || r.CostHpBefore <= 0 || r.X != Math.Max(0, r.OriginalTargetHp - r.OriginalDamage) ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash != r.GameplayHash ||
            ProgramEventHistory<ShanheS5YimieFrozenEvent>().Count(e => e.Origin == r) != 1)
            throw new InvalidOperationException("Yimie lost its exact frozen original source, target HP or damage amount.");
    }

    private SkillProgramStepOutcome ResolveShanheS5Yimie(ProgramSkillFrame supplied, string stage)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame window || window.Id != context.ParentFrameId ||
            CurrentDamageAttempt is not { IsSourceLess: false } attack || attack.SourceSeat != f.OwnerSeat ||
            attack.SourceSeat != window.SourceSeat || attack.TargetSeat != window.TargetSeat || window.TargetSeat == f.OwnerSeat)
            throw new InvalidOperationException("Yimie requires its original other-target actual damage window.");
        if (stage == "prepare")
        {
            if (f.InstructionIndex != 1 || f.ShanheS5YimieCost is not null || window.Prevented || window.Amount <= 0 ||
                !_players[f.OwnerSeat].IsAlive || !_players[f.OwnerSeat].HasHpValue || _players[f.OwnerSeat].Hp <= 0 ||
                UnambiguousHp(_players[window.TargetSeat]) is not { } hp ||
                _resolutionStack.Single(frame => frame.Id == attack.ResolutionId).ShanheS5YimieDamage is not null ||
                ProgramEventHistory<ShanheS5YimieFrozenEvent>().Any(e => e.Origin.Source.OwnerSeat == f.OwnerSeat && e.Origin.TurnNumber == _turnNumber))
                throw new InvalidOperationException("Yimie cannot duplicate its unpaid original damage opportunity.");
            var r = new ShanheS5YimieOrigin(f.Id, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
                window.Id, attack.ResolutionId, attack is CardAttackHandle, window.TargetSeat, _turnNumber, window.Amount, hp,
                Math.Max(0, hp - window.Amount), _players[f.OwnerSeat].Hp, GetDamageNature(attack));
            ReplaceRuntimeTop(f with { ShanheS5YimieCost = r });
            AdvanceEventRulesAndQueueFact(new ShanheS5YimieFrozenEvent(r)); return SkillProgramStepOutcome.Continue;
        }
        var origin = f.ShanheS5YimieCost ?? throw new InvalidOperationException("Yimie lost its accepted HP cost.");
        RequireShanheS5YimieOrigin(origin);
        if (f.InstructionIndex != 3 || !HasShanheS5YimieCostPaid(f) || origin.BeforeDamageWindowId != window.Id || origin.AttackId != attack.ResolutionId ||
            ProgramEventHistory<ShanheS5YimieIncreasedEvent>().Any(e => e.ProgramFrameId == f.Id))
            throw new InvalidOperationException("Yimie must return from its exact once-paid native HP loss.");
        if (window.Prevented || !_players[origin.TargetSeat].IsAlive) return SkillProgramStepOutcome.Continue;
        var before = window.Amount;
        if (!attack.DamageAmountFinalized || attack.DamageAmount != before)
            throw new InvalidOperationException("Yimie lost its current recipient's exact finalized damage amount.");
        if (origin.X > 0)
        {
            // Like the shared signed-recipient payment, the original before-damage
            // window and paid cost prove this segment. DamageWasApplied belongs to
            // the entire attack and may already be true for an earlier chain victim.
            var after = checked(before + origin.X);
            if (attack is ProgramAttackHandle)
                UpdateProgramAttackState(attack.ResolutionId, state => state with { DamageAmount = after });
            else UpdateCardAttackState(attack.ResolutionId, state => state! with { DamageAmount = after });
        }
        ReplaceRuntimeFrame(window.Id, window with { Amount = checked(before + origin.X) });
        var owning = _resolutionStack.Single(frame => frame.Id == attack.ResolutionId);
        ReplaceRuntimeFrame(owning.Id, owning with { ShanheS5YimieDamage = new(origin) });
        AdvanceEventRulesAndQueueFact(new ShanheS5YimieIncreasedEvent(f.Id, attack.ResolutionId, before, origin.X, before + origin.X));
        return SkillProgramStepOutcome.Continue;
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 5795)]
    private void RecordShanheS5YimieActualDamage(DamageAppliedEvent damage)
    {
        if (damage.Amount <= 0 || damage.SourceLess || CurrentDamageAttempt is not { } attack ||
            _resolutionStack.SingleOrDefault(f => f.Id == attack.ResolutionId) is not { ShanheS5YimieDamage: { } r } owner ||
            damage.SourceSeat != r.Origin.Source.OwnerSeat || damage.TargetSeat != r.Origin.TargetSeat) return;
        RequireShanheS5YimieOrigin(r.Origin);
        var frame = _resolutionStack.OfType<DamageFrame>().LastOrDefault();
        if (r.AppliedDamageFrameId is not null || frame is null || frame.ParentFrameId != owner.Id || frame.TargetSeat != r.Origin.TargetSeat)
            throw new InvalidOperationException("Yimie cannot borrow or duplicate another actual damage segment.");
        ReplaceRuntimeFrame(owner.Id, owner with { ShanheS5YimieDamage = r with { AppliedDamageFrameId = frame.Id, AppliedAmount = damage.Amount } });
        AdvanceEventRulesAndQueueFact(new ShanheS5YimieAppliedEvent(owner.Id, frame.Id, damage.TargetSeat, damage.Amount));
    }

    [ProgramDamageHook(ProgramDamagePhase.AttackCompletionPrelude, 5)]
    private bool CompleteShanheS5YimieCardDamage(CardAttackHandle attack, out bool paused)
    { paused = TryBeginShanheS5YimieRecovery(attack); return paused; }

    private bool TryBeginShanheS5YimieRecovery(IDamageAttempt attack)
    {
        if (_resolutionStack.SingleOrDefault(f => f.Id == attack.ResolutionId) is not { ShanheS5YimieDamage: { } receipt } owner) return false;
        RequireShanheS5YimieOrigin(receipt.Origin);
        if (_resolutionStack.LastOrDefault()?.Id != owner.Id) throw new InvalidOperationException("Yimie recovery must resume its original quiet attack frame.");
        var actual = receipt.AppliedDamageFrameId is not null;
        if (receipt.Origin.X == 0 || !actual || !_players[receipt.Origin.TargetSeat].IsAlive || _winner != Winner.None)
        {
            ReplaceRuntimeTop(owner with { ShanheS5YimieDamage = null });
            AdvanceEventRulesAndQueueFact(new ShanheS5YimieCompletedEvent(owner.Id, receipt.Origin.ProgramFrameId, receipt.Origin.TargetSeat, receipt.Origin.X, actual, false));
            return false;
        }
        var child = new ShanheS5YimieRecoveryFrame(++_resolutionSequence, owner.Id, receipt);
        PushRuntimeFrame(child); ContinueShanheS5YimieRecovery(child.Id); return true;
    }

    private bool HasShanheS5YimieRecovery(long attackId) => _resolutionStack.OfType<ShanheS5YimieRecoveryFrame>().Any(f => f.ParentFrameId == attackId);

    private void AssertShanheS5YimieRecoveryFrames()
    {
        foreach (var owner in _resolutionStack.Where(f => f.ShanheS5YimieDamage is not null))
        {
            var r = owner.ShanheS5YimieDamage!; RequireShanheS5YimieOrigin(r.Origin);
            if (r.Origin.AttackId != owner.Id || r.Origin.CardAttack != (owner is CardUseFrame) ||
                owner is not (CardUseFrame or ProgramSkillFrame) ||
                ProgramEventHistory<ShanheS5YimieIncreasedEvent>().Count(e => e.ProgramFrameId == r.Origin.ProgramFrameId && e.AttackId == owner.Id && e.X == r.Origin.X) != 1 ||
                r.AppliedDamageFrameId is { } damage && ProgramEventHistory<ShanheS5YimieAppliedEvent>().Count(e => e.AttackId == owner.Id &&
                    e.DamageFrameId == damage && e.TargetSeat == r.Origin.TargetSeat && e.Amount == r.AppliedAmount) != 1)
                throw new InvalidOperationException("Yimie lost its exact armed original damage obligation.");
        }
        foreach (var child in _resolutionStack.OfType<ShanheS5YimieRecoveryFrame>())
        {
            var i = _resolutionStack.FindIndex(f => f.Id == child.Id);
            if (i < 1 || _resolutionStack[i - 1].Id != child.ParentFrameId || _resolutionStack[i - 1].ShanheS5YimieDamage != child.Receipt ||
                child.Receipt.AppliedDamageFrameId is null || child.Receipt.Origin.X <= 0 ||
                child.Requested && ProgramEventHistory<ShanheS5YimieRecoveryRequestedEvent>().Count(e => e.FrameId == child.Id && e.AttackId == child.ParentFrameId && e.Amount == child.Receipt.Origin.X) != 1)
                throw new InvalidOperationException("Yimie recovery lost its actual damage and original typed parent.");
        }
    }

    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.BeforeOpening, 5795)]
    [NativeFrameRoute(typeof(ShanheS5YimieRecoveryFrame), 5795)]
    private void ContinueShanheS5YimieRecovery(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ShanheS5YimieRecoveryFrame frame || frame.Id != id) return;
        var origin = frame.Receipt.Origin; RequireShanheS5YimieOrigin(origin);
        if (!frame.Requested)
        {
            ReplaceRuntimeTop(frame = frame with { Requested = true });
            AdvanceEventRulesAndQueueFact(new ShanheS5YimieRecoveryRequestedEvent(frame.Id, frame.ParentFrameId, frame.Receipt.AppliedDamageFrameId!.Value,
                origin.Source.OwnerSeat, origin.TargetSeat, origin.X));
            var target = _players[origin.TargetSeat];
            if (target.IsAlive && target.HasHpValue && HappyQiaoAnyWounded(target.Seat) &&
                !TryQueueRecoveryReplacement(frame.Id, origin.Source.OwnerSeat, target.Seat, origin.X, new(RecoveryAttemptProducer.Program)))
            {
                var amount = Math.Max(0, Math.Min(origin.X, target.MaxHp - target.Hp));
                if (amount > 0)
                {
                    var recovery = BeginRecovery(frame.Id, origin.Source.OwnerSeat, target.Seat, amount);
                    try
                    {
                        target.Hp += amount;
                        AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(origin.Source.OwnerSeat, target.Seat, amount, target.Hp));
                    }
                    finally { PopResolutionFrame(recovery, ResolutionFrameKind.Recovery); }
                }
            }
        }
        if (TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.ShanheS5YimieRecovery)) return;
        var parent = _resolutionStack.Single(f => f.Id == frame.ParentFrameId);
        if (parent.ShanheS5YimieDamage != frame.Receipt) throw new InvalidOperationException("Yimie lost its exact recovery producer.");
        ReplaceRuntimeFrame(parent.Id, parent with { ShanheS5YimieDamage = null });
        AdvanceEventRulesAndQueueFact(new ShanheS5YimieCompletedEvent(parent.Id, origin.ProgramFrameId, origin.TargetSeat, origin.X, true, true));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ShanheS5YimieRecovery);
        CompleteDamageAttack(origin.CardAttack ? new CardAttackHandle(this, origin.AttackId) : new ProgramAttackHandle(this, origin.AttackId));
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 5795)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 5795)]
    private bool ContinuePaidShanheS5Yimie(ProgramSkillFrame f) => HasShanheS5YimieCostPaid(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 5795)]
    private void AssertShanheS5YimieCost(ProgramSkillFrame f)
    {
        if (f.ShanheS5YimieCost is not { } origin) return;
        RequireShanheS5YimieOrigin(origin);
        if (origin.ProgramFrameId != f.Id || origin.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            origin.GameplayHash != f.GameplayHash || f.WindowContext?.ParentFrameId != origin.BeforeDamageWindowId || f.InstructionIndex is < 1 or > 3)
            throw new InvalidOperationException("Yimie cost lost its original skill frame.");
    }

    private sealed partial class ProgramSkillHost : IShanheS5YimieHost
    { public SkillProgramStepOutcome ResolveShanheS5Yimie(ProgramSkillFrame f, string stage) => engine.ResolveShanheS5Yimie(f, stage); }
}
