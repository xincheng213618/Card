namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly List<OrderedPrintedSkillLossLease> _orderedPrintedSkillLossLeases = [];
    private bool _orderedPrintedSkillLossStateStarted;
    private static bool IsOrderedPrintedSkillLossTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw }];
    private long OrderedPrintedLossMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static string OrderedPrintedLossDrawReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.ordered-printed-skill-loss.draw";

    private int? OrderedPrintedLossActualTurnOwner() => _turnNumber > 0 && _turnProgression.TurnNumber == _turnNumber &&
        IsValidPlayerSeat(_turnProgression.OwnerSeat) && CompleteProgramEventHistory().OfType<TurnStartedEvent>()
            .Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat) &&
        !CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _turnProgression.OwnerSeat)
            ? _turnProgression.OwnerSeat : null;

    private bool CanOfferOrderedPrintedSkillLoss(int ownerSeat, SkillProgramTrigger trigger, IDamageAttempt attack) =>
        !IsOrderedPrintedSkillLossTrigger(trigger) || IsValidPlayerSeat(ownerSeat) && _players[ownerSeat].IsAlive &&
        OrderedPrintedLossActualTurnOwner() is not null && attack.DamageWasApplied && attack.DamageAmount > 0 &&
        (ownerSeat == attack.TargetSeat || !attack.IsSourceLess && ownerSeat == attack.SourceSeat);

    private bool CanOfferOrderedPrintedSkillLoss(int ownerSeat, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !IsOrderedPrintedSkillLossTrigger(trigger) || IsValidPlayerSeat(ownerSeat) && _players[ownerSeat].IsAlive &&
        OrderedPrintedLossActualTurnOwner() is not null && context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } &&
        context.OwnerSeat == ownerSeat && (context.SourceSeat == ownerSeat || context.TargetSeat == ownerSeat);

    private (string SourceId, GeneralDefinition General) OrderedPrintedLossTemplate(ProgramSkillFrame f)
    {
        var owner = _players[f.OwnerSeat];
        var origin = owner.SkillGrants.Grants.FirstOrDefault(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId &&
            g.SourceId is CharacterState.PrimarySkillSource or CharacterState.SecondarySkillSource);
        return origin?.SourceId == CharacterState.SecondarySkillSource && owner.SecondaryGeneral is { } secondary
            ? (CharacterState.SecondarySkillSource, secondary) : (CharacterState.PrimarySkillSource, owner.General);
    }

    private SkillProgramStepOutcome ExecuteOrderedPrintedSkillLoss(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OrderedPrintedSkillLoss is not null || f.InstructionIndex != 1 || f.TriggerId is null ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !IsOrderedPrintedSkillLossTrigger(GetProgramTrigger(f)) || !CanOfferOrderedPrintedSkillLoss(f.OwnerSeat, GetProgramTrigger(f), context) ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack) ||
            OrderedPrintedLossActualTurnOwner() is not { } turnOwner)
            throw new InvalidOperationException("Ordered printed-skill loss requires its exact positive damage participant and ongoing actual turn.");
        var owner = _players[f.OwnerSeat];
        var (templateSource, general) = OrderedPrintedLossTemplate(f);
        var range = GetAttackRange(f.OwnerSeat);
        var currentGrants = owner.SkillGrants.Grants;
        // Removed printed skills no longer occupy a currently owned slot. Keep
        // the original card order; neither grant IDs nor skill IDs define it.
        var lostIds = general.Skills.Where(s => s.ContentId is not null &&
                currentGrants.Any(g => g.SkillId == s.ContentId))
            .Select(s => s.ContentId!).Distinct(StringComparer.Ordinal).Take(Math.Max(0, range)).ToArray();
        var grants = currentGrants.Where(g => lostIds.Contains(g.SkillId, StringComparer.Ordinal)).ToArray();
        var lease = new OrderedPrintedSkillLossLease { ProgramFrameId = f.Id,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            ActualTurnNumber = _turnNumber, ActualTurnOwnerSeat = turnOwner, TemplateSourceId = templateSource,
            GeneralId = general.Id, FrozenAttackRange = range, LostSkillIds = lostIds, RemovedGrants = grants };
        var receipt = new ProgramOrderedPrintedSkillLossReceipt { InstructionIndex = 1, DamageWindowId = window.Id,
            DamageFrameId = damage.Id, AttackFrameId = damage.ParentFrameId, DamageSourceSeat = attack.IsSourceLess ? null : attack.SourceSeat,
            DamageTargetSeat = attack.TargetSeat, DamageAmount = attack.DamageAmount, DamageNature = GetDamageNature(attack),
            SourceLess = attack.IsSourceLess, Lease = lease, Stage = OrderedPrintedSkillLossStage.LossChildren };
        ReplaceRuntimeTop(f = f with { OrderedPrintedSkillLoss = receipt, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        // A first ownership change must be observable even before any earlier
        // program has initialized the revision baseline.
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        foreach (var grant in grants)
            if (!owner.SkillGrants.RemoveGrant(grant.GrantId)) throw new InvalidOperationException("An ordered skill-loss payment changed before its atomic removal.");
        _orderedPrintedSkillLossLeases.Add(lease);
        _orderedPrintedSkillLossStateStarted = true;
        AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillLossIssuedEvent(f.Id, lease.Source, lease.GameplayHash,
            lease.ActualTurnNumber, lease.ActualTurnOwnerSeat, templateSource, general.Id, range, lostIds.Length, grants.Length));
        for (var ordinal = 0; ordinal < lostIds.Length; ordinal++)
            AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillLostEvent(f.Id, ordinal, lostIds[ordinal]));
        foreach (var grant in grants) AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillGrantRemovedEvent(f.Id, grant));
        AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeOrderedPrintedSkillLoss(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.OrderedPrintedSkillLoss is not { } r) return false;
        AssertOrderedPrintedSkillLoss(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        if (r.Stage == OrderedPrintedSkillLossStage.LossChildren)
        {
            var before = OrderedPrintedLossMovementSequence;
            ReplaceRuntimeTop(f = f with { OrderedPrintedSkillLoss = r with { Stage = OrderedPrintedSkillLossStage.DrawChildren,
                DrawIssued = true, DrawBefore = before, DrawAfter = before } });
            var actual = _winner == Winner.None && _players[f.OwnerSeat].IsAlive ? DrawCards(_players[f.OwnerSeat], r.Lease.LostSkillIds.Count, true,
                new(OrderedPrintedLossDrawReason(f)), r.Lease.Source).Count : 0;
            var active = GetActiveProgramFrame(id); var after = OrderedPrintedLossMovementSequence;
            ReplaceRuntimeTop(active with { OrderedPrintedSkillLoss = active.OrderedPrintedSkillLoss! with { DrawActual = actual, DrawAfter = after } });
            AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillLossDrawIssuedEvent(id, r.Lease.LostSkillIds.Count, actual, before, after));
            AdvanceRuntimeProgram(id);
            return true;
        }
        if (r.Stage != OrderedPrintedSkillLossStage.DrawChildren) throw new InvalidOperationException("An ordered skill-loss continuation completed twice.");
        ReplaceRuntimeTop(f = f with { OrderedPrintedSkillLoss = r with { Stage = OrderedPrintedSkillLossStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillLossCompletedEvent(id, r.Lease.LostSkillIds.Count, r.DrawActual));
        FinishProgramSkill(f, true);
        return true;
    }

    private bool ReturnOrderedPrintedSkillLossMovement(ProgramSkillFrame f)
    {
        if (f.OrderedPrintedSkillLoss is null) return false;
        if (f.OrderedPrintedSkillLoss.Stage != OrderedPrintedSkillLossStage.DrawChildren || f.PendingMovementContinuation is null)
            throw new InvalidOperationException("An ordered skill-loss movement returned outside its once-issued Draw.");
        AssertOrderedPrintedSkillLoss(f); AdvanceRuntimeProgram(f.Id); return true;
    }

    private bool CanContinueOrderedPrintedSkillLoss(ProgramSkillFrame f) => f.OrderedPrintedSkillLoss is not null && ValidOrderedPrintedSkillLossReceipt(f);
    private bool IsOrderedPrintedSkillLossMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw && pending.SubjectSeat == f.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && f.PendingMovementContinuation == pending && CanContinueOrderedPrintedSkillLoss(f);

    private void RestoreOrderedPrintedSkillsAtTurnEnd(int turnNumber, int turnOwnerSeat)
    {
        var due = _orderedPrintedSkillLossLeases.Where(l => l.ActualTurnNumber == turnNumber && l.ActualTurnOwnerSeat == turnOwnerSeat).ToArray();
        if (due.Length == 0) return;
        if (!CompleteProgramEventHistory().OfType<TurnEndedEvent>().Any(e => e.TurnNumber == turnNumber && e.ActorSeat == turnOwnerSeat))
            throw new InvalidOperationException("Ordered printed skills return only after their exact actual TurnEnded fact.");
        var decisions = new List<(OrderedPrintedSkillLossLease Lease, SkillGrant Grant, bool Restore, string Outcome)>();
        foreach (var lease in due)
        {
            var owner = _players[lease.Source.OwnerSeat];
            var general = lease.TemplateSourceId == CharacterState.SecondarySkillSource ? owner.SecondaryGeneral : owner.General;
            foreach (var grant in lease.RemovedGrants)
            {
                var outcome = !owner.IsAlive ? "owner-dead" : general?.Id != lease.GeneralId ? "template-changed" :
                    grant.TurnExpiry is { } expiry && expiry.TurnNumber <= turnNumber ? "grant-expired" : "restored";
                if (outcome == "restored" && owner.SkillGrants.Grants.SingleOrDefault(g => g.GrantId == grant.GrantId) is { } existing)
                {
                    if (existing != grant) throw new InvalidOperationException("A returning ordered printed grant conflicts with a newly issued identity.");
                    outcome = "already-present";
                }
                decisions.Add((lease, grant, outcome is "restored" or "already-present", outcome));
            }
        }
        // Validate every identity before restoring any source. A source acquired
        // during the loss is independent and is never overwritten or removed.
        foreach (var lease in due) _orderedPrintedSkillLossLeases.Remove(lease);
        foreach (var d in decisions.Where(d => d.Outcome == "restored")) _players[d.Lease.Source.OwnerSeat].SkillGrants.Grant(d.Grant);
        foreach (var d in decisions)
            AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillGrantRestoredEvent(d.Lease.ProgramFrameId, d.Lease.Source.OwnerSeat, d.Grant, d.Restore, d.Outcome));
        foreach (var lease in due)
            AdvanceEventRulesAndQueueFact(new OrderedPrintedSkillLossExpiredEvent(lease.ProgramFrameId, lease.Source.OwnerSeat, turnNumber, turnOwnerSeat,
                decisions.Count(d => d.Lease == lease && d.Restore), decisions.Count(d => d.Lease == lease && !d.Restore)));
    }

    private sealed partial class ProgramSkillHost : IOrderedPrintedSkillLossProgramHost
    {
        public SkillProgramStepOutcome ExecuteOrderedPrintedSkillLoss(ProgramSkillFrame frame) => engine.ExecuteOrderedPrintedSkillLoss(frame);
        public bool CanContinueOrderedPrintedSkillLoss(ProgramSkillFrame frame) => engine.CanContinueOrderedPrintedSkillLoss(frame);
    }
}
