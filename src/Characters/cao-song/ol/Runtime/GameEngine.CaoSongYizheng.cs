namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CaoSongYizhengGrantedEvent[] ActiveCaoSongYizhengGrants(int beneficiary)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantYizhengSupport)) return [];
        var grants = ProgramEventHistory<CaoSongYizhengGrantedEvent>();
        if (grants.Count == 0) return [];
        var expired = ProgramEventHistory<CaoSongYizhengExpiredEvent>().Select(e => e.GrantId).ToHashSet();
        return grants
            .Where(e => e.RecipientSeat == beneficiary && !expired.Contains(e.GrantId))
            .OrderBy(e => e.Source.OwnerSeat).ThenBy(e => e.GrantId).ToArray();
    }

    private bool CanPayCaoSongYizheng(CaoSongYizhengGrantedEvent grant, int beneficiary) =>
        IsValidPlayerSeat(beneficiary) && _players[beneficiary] is { IsAlive: true, HasHpValue: true } &&
        IsValidPlayerSeat(grant.Source.OwnerSeat) && grant.Source.OwnerSeat != beneficiary &&
        _players[grant.Source.OwnerSeat] is { IsAlive: true, HasHpValue: true } owner &&
        owner.MaxHp > _players[beneficiary].MaxHp &&
        ActiveCaoSongYizhengGrants(beneficiary).Any(e => e == grant);

    private bool HasRecoveryAmountSupport(int targetSeat) => IsValidPlayerSeat(targetSeat) &&
        _players[targetSeat] is { IsAlive: true, HasHpValue: true } target && target.Hp < target.MaxHp &&
        ActiveCaoSongYizhengGrants(targetSeat).Any(grant => CanPayCaoSongYizheng(grant, targetSeat));

    private bool HasDamageAmountSupport(int sourceSeat, int targetSeat) =>
        IsValidPlayerSeat(sourceSeat) && IsValidPlayerSeat(targetSeat) && _players[targetSeat].IsAlive &&
        ActiveCaoSongYizhengGrants(sourceSeat).Any(grant => CanPayCaoSongYizheng(grant, sourceSeat));

    private void IssueCaoSongYizheng(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding, OwnerSeat: var owner } context ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } ending ||
            ending.OwnerSeat != frame.OwnerSeat || ending.TurnNumber != _turnNumber || owner != frame.OwnerSeat || frame.OwnerSeat != _currentSeat || frame.SelectedTargetSeats is not [var recipient] ||
            recipient == frame.OwnerSeat || !_players[recipient].IsAlive ||
            ProgramEventHistory<CaoSongYizhengGrantedEvent>().Any(e => e.GrantId == frame.Id))
            throw new InvalidOperationException("Yizheng requires one exact other living recipient at the owner's native ending.");
        AdvanceEventRulesAndQueueFact(new CaoSongYizhengGrantedEvent(frame.Id, CaoSongSource(frame), recipient, _turnNumber));
    }

    // The granted duration is independent of retaining the skill. A dead donor
    // cannot make a new payment, but a cost already paid remains effective.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26031)]
    private void ExpireCaoSongYizhengAtNextOwnTurn(TurnStartedEvent started)
    {
        var grants = ProgramEventHistory<CaoSongYizhengGrantedEvent>();
        if (grants.Count == 0) return;
        var expired = ProgramEventHistory<CaoSongYizhengExpiredEvent>().Select(e => e.GrantId).ToHashSet();
        foreach (var grant in grants
            .Where(e => e.Source.OwnerSeat == started.ActorSeat && !expired.Contains(e.GrantId)))
            AdvanceEventRulesAndQueueFact(new CaoSongYizhengExpiredEvent(grant.GrantId, started.ActorSeat, started.TurnNumber));
    }

    private void PayCaoSongYizhengMaximum(long parentId, CaoSongYizhengGrantedEvent grant, int beneficiary, bool recovery)
    {
        if (_resolutionStack.LastOrDefault()?.Id != parentId || !CanPayCaoSongYizheng(grant, beneficiary))
            throw new InvalidOperationException("Yizheng maximum-HP cost lost its current issuing grant and strict maximum comparison.");
        var owner = _players[grant.Source.OwnerSeat];
        var previous = owner.MaxHp; var hpBefore = owner.Hp;
        owner.MaxHp = checked(previous - 1); owner.Hp = Math.Min(owner.Hp, owner.MaxHp);
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat, -1, owner.MaxHp, grant.Source.SkillId)
        { HpBeforeClamp = HasQimeiHealthSubject(owner.Seat) || owner.XiaHuiRestrictions is { Count: > 0 } ? hpBefore : null });
        AdvanceEventRulesAndQueueFact(new CaoSongYizhengAmountPaidEvent(parentId, grant.GrantId,
            owner.Seat, beneficiary, recovery, previous, owner.MaxHp));
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeRecoveryApply, 26030)]
    private bool ApplyCaoSongRecoverySupport()
    {
        if (_resolutionStack.LastOrDefault() is not RecoveryReplacementFrame
            { Stage: RecoveryReplacementStage.Choosing, PendingApplication: { } application } frame) return false;
        if (frame.CaoSongRecoverySupport is null)
        {
            if (application.RequestedAmount <= 0 || !HasRecoveryAmountSupport(application.TargetSeat)) return false;
            ReplaceRuntimeTop(frame = frame with { CaoSongRecoverySupport = new(application.TargetSeat, application.RequestedAmount)
            { Grants = ActiveCaoSongYizhengGrants(application.TargetSeat) } });
        }
        while (_resolutionStack.LastOrDefault() is RecoveryReplacementFrame { CaoSongRecoverySupport: { Complete: false } receipt } current && current.Id == frame.Id)
        {
            if (receipt.NextGrantIndex >= receipt.Grants.Count)
            { ReplaceRuntimeTop(current with { CaoSongRecoverySupport = receipt with { Complete = true,
                HpBeforeApplication = _players[receipt.BeneficiarySeat].Hp } }); return false; }
            var grant = receipt.Grants[receipt.NextGrantIndex];
            if (receipt.WaitingForMaximumChildren)
            {
                if (TryBeginHpChangedProgramWindow(current.Id, PostEventContinuation.AmountSupport)) return true;
                ValidateCaoSongSupportPaid(current.Id, receipt, true);
                var before = current.PendingApplication!.RequestedAmount;
                ReplaceRuntimeTop(current with { PendingApplication = current.PendingApplication with { RequestedAmount = checked(before + 1) },
                    CaoSongRecoverySupport = receipt with { NextGrantIndex = receipt.NextGrantIndex + 1, WaitingForMaximumChildren = false } });
                AdvanceEventRulesAndQueueFact(new CaoSongYizhengAmountIncreasedEvent(current.Id, grant.GrantId,
                    receipt.BeneficiarySeat, true, before, before + 1));
                continue;
            }
            if (_winner != Winner.None || !CanPayCaoSongYizheng(grant, receipt.BeneficiarySeat) ||
                !_players[receipt.BeneficiarySeat].IsAlive || _players[receipt.BeneficiarySeat].Hp >= _players[receipt.BeneficiarySeat].MaxHp)
            { ReplaceRuntimeTop(current with { CaoSongRecoverySupport = receipt with { NextGrantIndex = receipt.NextGrantIndex + 1 } }); continue; }
            ReplaceRuntimeTop(current with { CaoSongRecoverySupport = receipt with { WaitingForMaximumChildren = true } });
            PayCaoSongYizhengMaximum(current.Id, grant, receipt.BeneficiarySeat, true);
            if (TryBeginHpChangedProgramWindow(current.Id, PostEventContinuation.AmountSupport)) return true;
        }
        return false;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDamageApply, 26030)]
    private bool ApplyCaoSongDamageSupport()
    {
        if (_resolutionStack.LastOrDefault() is not BeforeDamageProgramWindowFrame frame || frame.Prevented ||
            frame.RedirectedTargetSeat is not null || frame.Amount <= 0 ||
            CurrentDamageAttempt is not { IsSourceLess: false } attack || attack.SourceSeat != frame.SourceSeat || attack.TargetSeat != frame.TargetSeat)
            return false;
        if (frame.CaoSongDamageSupport is null)
        {
            if (!HasDamageAmountSupport(frame.SourceSeat, frame.TargetSeat)) return false;
            ReplaceRuntimeTop(frame = frame with { CaoSongDamageSupport = new(frame.SourceSeat, frame.Amount)
            { Grants = ActiveCaoSongYizhengGrants(frame.SourceSeat) } });
        }
        while (_resolutionStack.LastOrDefault() is BeforeDamageProgramWindowFrame { CaoSongDamageSupport: { Complete: false } receipt } current && current.Id == frame.Id)
        {
            if (receipt.NextGrantIndex >= receipt.Grants.Count)
            { ReplaceRuntimeTop(current with { CaoSongDamageSupport = receipt with { Complete = true } }); return false; }
            var grant = receipt.Grants[receipt.NextGrantIndex];
            if (receipt.WaitingForMaximumChildren)
            {
                if (TryBeginHpChangedProgramWindow(current.Id, PostEventContinuation.AmountSupport)) return true;
                ValidateCaoSongSupportPaid(current.Id, receipt, false);
                if (CurrentDamageAttempt is not { } actual || actual.SourceSeat != current.SourceSeat || actual.TargetSeat != current.TargetSeat)
                    throw new InvalidOperationException("Yizheng paid cost lost its original damage attempt.");
                actual.IncreaseFinalizedDamageAmount(1);
                ReplaceRuntimeTop(current with { Amount = checked(current.Amount + 1),
                    CaoSongDamageSupport = receipt with { NextGrantIndex = receipt.NextGrantIndex + 1, WaitingForMaximumChildren = false } });
                AdvanceEventRulesAndQueueFact(new CaoSongYizhengAmountIncreasedEvent(current.Id, grant.GrantId,
                    receipt.BeneficiarySeat, false, current.Amount, current.Amount + 1));
                continue;
            }
            if (_winner != Winner.None || !CanPayCaoSongYizheng(grant, receipt.BeneficiarySeat))
            { ReplaceRuntimeTop(current with { CaoSongDamageSupport = receipt with { NextGrantIndex = receipt.NextGrantIndex + 1 } }); continue; }
            ReplaceRuntimeTop(current with { CaoSongDamageSupport = receipt with { WaitingForMaximumChildren = true } });
            PayCaoSongYizhengMaximum(current.Id, grant, receipt.BeneficiarySeat, false);
            if (TryBeginHpChangedProgramWindow(current.Id, PostEventContinuation.AmountSupport)) return true;
        }
        return false;
    }

    private void ValidateCaoSongSupportPaid(long parentId, CaoSongAmountSupportReceipt receipt, bool recovery)
    {
        var grant = receipt.Grants[receipt.NextGrantIndex];
        if (ProgramEventHistory<CaoSongYizhengAmountPaidEvent>().Where(e => e.ParentFrameId == parentId &&
            e.GrantId == grant.GrantId && e.Recovery == recovery).ToArray() is not [var paid] ||
            paid.OwnerSeat != grant.Source.OwnerSeat || paid.BeneficiarySeat != receipt.BeneficiarySeat ||
            paid.MaximumHpBefore != paid.MaximumHpAfter + 1 ||
            ProgramEventHistory<CaoSongYizhengAmountIncreasedEvent>().Any(e => e.ParentFrameId == parentId && e.GrantId == grant.GrantId && e.Recovery == recovery))
            throw new InvalidOperationException("Yizheng amount increment requires one exact cost and cannot repeat a fulfilled payment.");
    }

    private bool IsAmountSupportHealthChild(ResolutionFrame? parent, HpChangedTriggerWindowFrame hp)
    {
        var receipt = parent switch
        {
            RecoveryReplacementFrame r => r.CaoSongRecoverySupport,
            BeforeDamageProgramWindowFrame d => d.CaoSongDamageSupport,
            _ => null
        };
        if (parent is null || receipt is not { WaitingForMaximumChildren: true, Complete: false } ||
            receipt.NextGrantIndex < 0 || receipt.NextGrantIndex >= receipt.Grants.Count ||
            hp.Continuation != PostEventContinuation.AmountSupport || hp.ResumeFrameId != parent.Id || hp.Change.ParentFrameId != parent.Id ||
            hp.Change.Kind != HpChangeKind.MaximumHp || hp.Change.Amount != 1 ||
            hp.Change.TargetSeat != receipt.Grants[receipt.NextGrantIndex].Source.OwnerSeat || hp.Change.SourceSeat is not null ||
            hp.CardId is not null || hp.CardKind is not null) return false;
        ValidateCaoSongSupportPaid(parent.Id, receipt, parent is RecoveryReplacementFrame);
        return true;
    }

    private int SupportedCaoSongRecoveryAmount(RecoveryReplacementFrame recovery, int baseAmount)
    {
        if (recovery.CaoSongRecoverySupport is not { } receipt) return baseAmount;
        if (receipt.OriginalAmount <= 0 || receipt.NextGrantIndex < 0 || receipt.NextGrantIndex > receipt.Grants.Count ||
            receipt.Grants.Select(g => g.GrantId).Distinct().Count() != receipt.Grants.Count)
            throw new InvalidOperationException("Recovery support lost its frozen recipient, positive original amount or grant cursor.");
        var increases = ProgramEventHistory<CaoSongYizhengAmountIncreasedEvent>()
            .Where(e => e.ParentFrameId == recovery.Id && e.Recovery).ToArray();
        var amount = receipt.OriginalAmount;
        foreach (var increase in increases)
        {
            if (increase.BeneficiarySeat != receipt.BeneficiarySeat || increase.Before != amount || increase.After != amount + 1 ||
                receipt.Grants.SingleOrDefault(g => g.GrantId == increase.GrantId) is not { } grant ||
                increases.Count(e => e.GrantId == grant.GrantId) != 1 ||
                ProgramEventHistory<CaoSongYizhengAmountPaidEvent>().Where(e => e.ParentFrameId == recovery.Id &&
                    e.GrantId == grant.GrantId && e.Recovery && e.OwnerSeat == grant.Source.OwnerSeat &&
                    e.BeneficiarySeat == receipt.BeneficiarySeat && e.MaximumHpBefore == e.MaximumHpAfter + 1).Count() != 1)
                throw new InvalidOperationException("Recovery support has an increment without its exact once-paid issued grant.");
            amount++;
        }
        if (recovery.PendingApplication is { } pending &&
            (pending.TargetSeat != receipt.BeneficiarySeat || pending.RequestedAmount != amount))
            throw new InvalidOperationException("The prepared recovery application lost its frozen supported recipient or exact amount.");
        return Math.Max(baseAmount, amount);
    }

    private int SupportedRecoveryHpBefore(RecoveryReplacementFrame recovery, int fallback) =>
        recovery.AmountBonus is { } bonus ? bonus.HpBefore :
        recovery.CaoSongRecoverySupport is { Complete: true, HpBeforeApplication: { } hp } ? hp : fallback;
}
