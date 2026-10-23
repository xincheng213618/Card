namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XiahouEnGainReason = "skill-program.anniversary-shiwu.damage-card";
    private static CardConversionSource XiahouEnSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryXiahouEnHost(GameEngine engine) : IAnniversaryXiahouEnHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp operation) => engine.BeginAnniversaryXiahouEn(f, operation); }

    [ProgramPipelineHook(ProgramPipelinePhase.VirtualEquipment, 45000)]
    private CardKind? XiahouEnVirtualQinggang(int seat, EquipmentSlot slot) => slot == EquipmentSlot.Weapon && IsValidPlayerSeat(seat) &&
        HasRuntimeSkill(_players[seat], AnniversaryXiahouEnComposition.Chijian) &&
        !GetEquipment(_players[seat]).Any(c => EquipmentCatalog.IsEquipment(c.Kind) && EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Weapon)
            ? CardKind.QinggangSword : null;
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeBaseTerms, 45000)]
    private void XiahouEnVirtualSwordRange(ProgramAttackRangeQuery query, List<RuleQueryBaseTerm> terms)
    {
        if (query.ExcludedEquipmentSlot != EquipmentSlot.Weapon && XiahouEnVirtualQinggang(query.Player.Seat, EquipmentSlot.Weapon) is { } kind &&
            EquipmentCatalog.Get(kind).WeaponAttackRange is { } range && terms[0].Value < range)
            terms[0] = new RuleQueryBaseTerm($"skill:{query.Player.Seat}:anniversary-chijian:attack-range", range);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 45000,
        SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu, SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage,
        SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian)]
    private bool CanOfferAnniversaryXiahouEn(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) => trigger.Id switch
    {
        AnniversaryXiahouEnComposition.Duel => context.SourceSeat is { } other && other == _currentSeat &&
            CanIssueSelectedActorDuel(other, candidate.OwnerSeat),
        AnniversaryXiahouEnComposition.Damage => XiahouEnDamageOrigin(candidate.OwnerSeat, context) is not null,
        AnniversaryXiahouEnComposition.Death => context.SourceSeat is { } killer && killer != candidate.OwnerSeat &&
            IsValidPlayerSeat(killer) && _players[killer].IsAlive,
        _ => false
    };

    private XiahouEnShiwuOrigin? XiahouEnDamageOrigin(int owner, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.Amount is not > 0 ||
            CurrentDamageAttempt is not { IsSourceLess: false, DamageWasApplied: true, DamageAmount: > 0 } attack ||
            attack.EffectiveCardKind != CardKind.Duel || context.SourceSeat != attack.SourceSeat || context.TargetSeat != attack.TargetSeat ||
            LifecycleCardUse(attack.ResolutionId) is not { SelectedActorDuelOrigin: { XiahouEnShiwu: { } origin } } use ||
            origin.Source.OwnerSeat != owner || !ValidXiahouEnShiwuUse(use, use.SelectedActorDuelOrigin) ||
            !(attack.SourceSeat == owner && attack.TargetSeat == origin.TurnOwner || attack.TargetSeat == owner && attack.SourceSeat == origin.TurnOwner)) return null;
        return origin;
    }

    private SkillProgramStepOutcome BeginAnniversaryXiahouEn(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryXiahouEn is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context)
            throw new InvalidOperationException("Shiwu requires its original unpaid trigger instruction.");
        if (operation == SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwu)
        {
            if (!IsForeignActualTurnPindian(f) || context.SourceSeat is not { } other || !CanIssueSelectedActorDuel(other, f.OwnerSeat))
                throw new InvalidOperationException("Shiwu requires the exact other actual turn start and legal owner-to-turn-owner Duel.");
            var origin = new XiahouEnShiwuOrigin(f.Id, f.InstructionIndex, XiahouEnSource(f), f.GameplayHash, _turnNumber, other,
                context.ParentFrameId, _resolutionSequence + 1, 0);
            ReplaceRuntimeTop(f = f with { AnniversaryXiahouEn = new(XiahouEnSource(f), f.GameplayHash, operation, context.ParentFrameId,
                _turnNumber, other, AnniversaryXiahouEnStage.NativeUse, origin) });
            var native = new ProgramSelectedActorDuelOrigin(f.Id, f.InstructionIndex, f.OwnerSeat, f.OwnerSeat, f.SkillId, f.ActivationId!,
                f.SkillInstanceId, f.GameplayHash, _turnNumber) { InitialTargetSeat = other, XiahouEnShiwu = origin };
            var card = new Card(0, CardKind.Duel, Suit.None, 0);
            var id = BeginCardUse(card, f.OwnerSeat, [other], CardKind.Duel, physicalCardIds: [], selectedActorDuelOrigin: native);
            var use = LifecycleCardUse(id)!;
            if (id != origin.UseId || use.Action is not { Type: CardActionType.Use } action)
                throw new InvalidOperationException("Shiwu lost its exact declared native Duel.");
            origin = origin with { ActionId = action.ActionId };
            UpdateLifecycleCardUse(id, u => u with { SelectedActorDuelOrigin = native with { XiahouEnShiwu = origin } });
            ReplaceRuntimeFrame(f.Id, f with { AnniversaryXiahouEn = f.AnniversaryXiahouEn! with { Origin = origin } });
            AdvanceEventRulesAndQueueFact(new XiahouEnShiwuIssuedEvent(origin));
            BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, [other], LegalActionKind.Duel, playedCardKind: CardKind.Duel);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var before = _movementSequence;
        if (operation == SkillProgramEffectOp.BequeathAnniversaryXiahouEnChijian)
        {
            if (context.Window != SkillProgramTriggerWindow.OwnerDied || context.SourceSeat is not { } killer || killer == f.OwnerSeat ||
                !_players[killer].IsAlive || _players[f.OwnerSeat].IsAlive || _resolutionStack.OfType<DeathFrame>().LastOrDefault() is not { } death ||
                death.VictimSeat != f.OwnerSeat || death.KillerSeat != killer)
                throw new InvalidOperationException("Shiwu bequest requires the real death and its actual surviving other killer.");
            ReplaceRuntimeTop(f = f with { AnniversaryXiahouEn = new(XiahouEnSource(f), f.GameplayHash, operation, context.ParentFrameId,
                _turnNumber, _currentSeat, AnniversaryXiahouEnStage.SkillChildren, Before: before, After: before) });
            foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
            AcquireRuntimeSkills(_players[killer], $"anniversary-shiwu-bequest:{f.OwnerSeat}:{f.SkillInstanceId}:{f.Id}", [AnniversaryXiahouEnComposition.Chijian]);
            AdvanceEventRulesAndQueueFact(new XiahouEnChijianGrantedEvent(f.Id, f.OwnerSeat, killer, true));
            ContinueAnniversaryXiahouEn(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        var damageOrigin = XiahouEnDamageOrigin(f.OwnerSeat, context) ?? throw new InvalidOperationException("Shiwu settlement lost its original actual Duel damage.");
        if (context.DamageFrameId is not { } damageId || context.SourceSeat is not { } source || context.TargetSeat is not { } target)
            throw new InvalidOperationException("Shiwu damage requires an exact original native damage frame.");
        var receipt = new AnniversaryXiahouEnReceipt(XiahouEnSource(f), f.GameplayHash, operation, context.ParentFrameId, _turnNumber,
            damageOrigin.TurnOwner, source == f.OwnerSeat ? AnniversaryXiahouEnStage.GainChildren : AnniversaryXiahouEnStage.SkillChildren,
            damageOrigin, damageId, source, target, context.Amount, before, before);
        ReplaceRuntimeTop(f = f with { AnniversaryXiahouEn = receipt });
        AdvanceEventRulesAndQueueFact(new XiahouEnShiwuDamageBoundEvent(f.Id, damageOrigin, damageId, source, target, receipt.DamageAmount));
        if (source == f.OwnerSeat)
        {
            var pool = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => IsDamageCardKind(c.Kind) || IsCompletedUndamagedDamageUse(c.Kind) || c.Kind == CardKind.Lightning).ToArray();
            if (pool.Length > 0 && _players[f.OwnerSeat].IsAlive)
            {
                var card = pool[_random.Next(pool.Length)];
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null), AnniversaryXiahouEn = receipt with { GainedCardId = card.Id } });
                MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(f.OwnerSeat), new(XiahouEnGainReason));
                f = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(f = f with { AnniversaryXiahouEn = f.AnniversaryXiahouEn! with { After = _movementSequence } });
            }
            AdvanceEventRulesAndQueueFact(new XiahouEnShiwuGainPaidEvent(f.Id, before, _movementSequence,
                _cardMovements.Count(m => m.Sequence > before && m.Sequence <= _movementSequence && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == XiahouEnGainReason)));
        }
        else
        {
            // The period follows only the owner's loss clause. The opponent's new independent source is permanent.
            foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
            AcquireRuntimeSkills(_players[source], $"anniversary-shiwu-duel:{f.OwnerSeat}:{f.SkillInstanceId}:{damageOrigin.FrameId}", [AnniversaryXiahouEnComposition.Chijian]);
            AdvanceEventRulesAndQueueFact(new XiahouEnChijianGrantedEvent(f.Id, f.OwnerSeat, source, false));
            var owner = _players[f.OwnerSeat];
            foreach (var grant in owner.SkillGrants.Grants.Where(g => g.SkillId == AnniversaryXiahouEnComposition.Chijian && !g.IsModeRule).ToArray())
            {
                owner.SkillGrants.RemoveGrant(grant.GrantId);
                AdvanceEventRulesAndQueueFact(new XiahouEnChijianLostGrantEvent(f.Id, f.OwnerSeat, _turnNumber, _currentSeat, owner.General.Id, owner.SecondaryGeneral?.Id, grant));
            }
        }
        ContinueAnniversaryXiahouEn(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 45000)]
    private bool ResumeAnniversaryXiahouEn(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryXiahouEn: not null } f || f.Id != id) return false; ContinueAnniversaryXiahouEn(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 45000)]
    private bool ReturnAnniversaryXiahouEnMovement(ProgramSkillFrame f) => f.AnniversaryXiahouEn is not null && ResumeAnniversaryXiahouEn(f.Id);
    private void ContinueAnniversaryXiahouEn(long id)
    {
        var f = GetActiveProgramFrame(id); RequireAnniversaryXiahouEn(f); var r = f.AnniversaryXiahouEn!;
        if (r.Stage == AnniversaryXiahouEnStage.NativeUse)
        {
            if (LifecycleCardUse(r.Origin!.UseId) is not null) return;
            if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("Shiwu must wait for its native attack cleanup.");
            if (ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.Origin.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel) != 1)
                throw new InvalidOperationException("Shiwu cannot finish an unreturned or different Duel.");
            ReplaceRuntimeTop(f = f with { AnniversaryXiahouEn = r with { Stage = AnniversaryXiahouEnStage.NativeReturned } });
            AdvanceEventRulesAndQueueFact(new XiahouEnShiwuReturnedEvent(f.Id, r.Origin.UseId, r.Origin.ActionId));
        }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 45000)]
    private void RestoreXiahouEnSwordAtActualTurnEnd(TurnEndedEvent ended)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryXiahouEnShiwuDamage)) return;
        var restored = ProgramEventHistory<XiahouEnChijianRestoredEvent>().Select(e => (e.FrameId, e.GrantId)).ToHashSet();
        foreach (var loss in ProgramEventHistory<XiahouEnChijianLostGrantEvent>().Where(e => e.ActualTurn == ended.TurnNumber && e.TurnOwner == ended.ActorSeat && !restored.Contains((e.FrameId, e.Grant.GrantId))).ToArray())
        {
            var owner = _players[loss.OwnerSeat]; var grant = loss.Grant;
            var outcome = WasGrantPermanentlyRevokedAfterLoss(owner.Seat, grant, loss) ? "permanently-lost" : !owner.IsAlive ? "owner-dead" :
                grant.SourceId == CharacterState.PrimarySkillSource && owner.General.Id != loss.GeneralId ||
                grant.SourceId == CharacterState.SecondarySkillSource && owner.SecondaryGeneral?.Id != loss.SecondaryGeneralId ? "template-changed" :
                RandomDamageTargetSkillGrantExpired(grant) || grant.PhaseExpiry is not null ? "grant-expired" : "restored";
            if (outcome == "restored" && owner.SkillGrants.Grants.SingleOrDefault(g => g.GrantId == grant.GrantId) is { } existing)
            { if (existing != grant) throw new InvalidOperationException("Shiwu cannot replace a newly changed grant identity."); outcome = "already-present"; }
            if (outcome == "restored") owner.SkillGrants.Grant(grant);
            AdvanceEventRulesAndQueueFact(new XiahouEnChijianRestoredEvent(loss.FrameId, loss.OwnerSeat, grant.GrantId, ended.TurnNumber, ended.ActorSeat, outcome));
        }
    }
}
