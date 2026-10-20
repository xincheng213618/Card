namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string CategoryTalismanSkill = "ol:shoufu-talisman";
    private bool _synchronizingCategoryTalismans;
    private bool TracksCategoryTalismans => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.PlaceBoundHandAsCategoryTalisman);
    private CategoryTalismanPlacedEvent? LiveCategoryTalisman(int seat) => !TracksCategoryTalismans ? null :
        CompleteProgramEventHistory().OfType<CategoryTalismanPlacedEvent>().LastOrDefault(e => e.HolderSeat == seat &&
            _cardZones.GetLocation(e.CardId) == e.Pile.Location && !CompleteProgramEventHistory()
                .OfType<CategoryTalismanReleasedEvent>().Any(release => release.PlacementFrameId == e.FrameId));
    private bool IsCategoryTalismanUseForbidden(int actorSeat, CardKind effectiveKind, CardActionType type) =>
        type is CardActionType.Use or CardActionType.Response && LiveCategoryTalisman(actorSeat) is { } talisman &&
        GetProgramCardCategory(effectiveKind) == talisman.Category;

    [ProgramCapability]
    private sealed class ZhangLingProgramCapability(GameEngine engine) : IZhangLingProgramHost
    {
        public SkillProgramStepOutcome PlaceCategoryTalisman(ProgramSkillFrame frame, string bind) => engine.PlaceCategoryTalisman(frame, bind);
        public SkillProgramStepOutcome ReleaseCategoryTalisman(ProgramSkillFrame frame) => engine.ReleaseCategoryTalisman(frame);
        public SkillProgramStepOutcome UseHuqiSlash(ProgramSkillFrame frame, string bind) => engine.UseHuqiSlash(frame, bind);
    }
    [ProgramCapability]
    private sealed class CategoryTalismanSelectionQuery(GameEngine engine) : ICategoryTalismanSelectionQuery
    {
        public IReadOnlyList<int> EligibleRecipients(int ownerSeat) => Array.AsReadOnly(engine._players
            .Where(p => p.IsAlive && p.Seat != ownerSeat && engine.LiveCategoryTalisman(p.Seat) is null).Select(p => p.Seat).ToArray());
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26050)]
    private void ObserveZhangLingNativeFacts(IGameEvent payload)
    {
        if (payload is DamageAppliedEvent { Amount: > 0 } damageApplied &&
            _resolutionStack.LastOrDefault() is DamageFrame damage && CurrentDamageAttempt is { DamageWasApplied: true } attack &&
            damage.ParentFrameId == attack.ResolutionId && damage.TargetSeat == damageApplied.TargetSeat && damage.SourceSeat == damageApplied.SourceSeat &&
            (TracksCategoryTalismans || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.UseRedJudgmentSlashAtDamageSource)))
            AdvanceEventRulesAndQueueFact(new ZhangLingDamageAppliedEvent(damage.Id, damageApplied.SourceLess ? null : damageApplied.SourceSeat, damageApplied.TargetSeat, damageApplied.Amount));
        if (payload is ProgramSkillStartedEvent or ProgramBindingStartedEvent)
        {
            var id = payload is ProgramSkillStartedEvent activation ? activation.FrameId : ((ProgramBindingStartedEvent)payload).FrameId;
            if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == id) is { SkillId: "ol:huqi" or "ol:shoufu" } frame &&
                !CompleteProgramEventHistory().OfType<ZhangLingBindingIssuedEvent>().Any(e => e.FrameId == id))
            {
                var damageId = frame.WindowContext?.DamageFrameId;
                var source = damageId is { } nativeDamage ? CompleteProgramEventHistory().OfType<ZhangLingDamageAppliedEvent>()
                    .SingleOrDefault(e => e.DamageFrameId == nativeDamage)?.SourceSeat : null;
                AdvanceEventRulesAndQueueFact(new ZhangLingBindingIssuedEvent(id,
                    new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash, damageId, source));
            }
        }
        if (!TracksCategoryTalismans || payload is not CardMovedEvent moved || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase) return;
        var record = _cardMovements.LastOrDefault(m => m.CardId == moved.CardId && m.From == moved.From && m.To == moved.To && m.Reason == moved.Reason);
        if (record is null || GetProgramDiscardSource(record)?.OwnerSeat is not { } owner || owner != phase.ActorSeat ||
            CompleteProgramEventHistory().OfType<CategoryTalismanDiscardRecordedEvent>().Any(e => e.MovementSequence == record.Sequence)) return;
        var card = _cardZones.CardsAt(record.To).Single(c => c.Id == record.CardId);
        AdvanceEventRulesAndQueueFact(new CategoryTalismanDiscardRecordedEvent(phase, owner, card.Id, record.Sequence, GetProgramCardCategory(card.Kind)));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 65)]
    private void SynchronizeCategoryTalismanGrants()
    {
        if (!TracksCategoryTalismans || _synchronizingCategoryTalismans) return;
        _synchronizingCategoryTalismans = true;
        try
        {
            foreach (var player in _players.Where(p => p.IsAlive))
                if (LiveCategoryTalisman(player.Seat) is { } talisman && !player.SkillGrants.Grants.Any(g => g.GrantId == talisman.GrantId))
                    player.SkillGrants.Grant(new(talisman.GrantId, CategoryTalismanSkill, talisman.Pile.SkillInstanceId, "state:category-talisman"));
        }
        finally { _synchronizingCategoryTalismans = false; }
    }
    private bool CanReleaseCategoryTalisman(int holder, ProgramSkillWindowContext context)
    {
        if (LiveCategoryTalisman(holder) is not { } talisman) return false;
        if (context.Window == SkillProgramTriggerWindow.AfterDamageApplied)
            return context.TargetSeat == holder && context.DamageFrameId is { } damage && CompleteProgramEventHistory()
                .OfType<ZhangLingDamageAppliedEvent>().Any(e => e.DamageFrameId == damage && e.TargetSeat == holder && e.Amount > 0);
        if (context.Window != SkillProgramTriggerWindow.CardsMoved || context.MovementBatch?.DiscardRecoveryPhase is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase || phase.ActorSeat != holder) return false;
        return CompleteProgramEventHistory().OfType<CategoryTalismanDiscardRecordedEvent>()
            .Count(e => e.Phase == phase && e.OwnerSeat == holder && e.Category == talisman.Category) >= 2;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26050)]
    private bool CanOfferCategoryTalismanRelease(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ReleaseCategoryTalisman) ||
        LiveCategoryTalisman(candidate.OwnerSeat) is { } t && t.Pile.SkillInstanceId == candidate.SkillInstanceId && CanReleaseCategoryTalisman(candidate.OwnerSeat, context);

    private SkillProgramStepOutcome PlaceCategoryTalisman(ProgramSkillFrame supplied, string bind)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var cards = frame.CardSetBindings.Single(b => b.Name == bind).CardIds;
        if (cards is not [var cardId] || frame.SelectedTargetSeats is not [var target] || target == frame.OwnerSeat ||
            !_players[target].IsAlive || LiveCategoryTalisman(target) is not null || _cardZones.GetLocation(cardId) != CardLocation.Hand(frame.OwnerSeat))
            throw new InvalidOperationException("A talisman requires one currently owned hand entity and one other unmarked recipient.");
        var card = GetHand(_players[frame.OwnerSeat]).Single(c => c.Id == cardId);
        var grantId = $"state:category-talisman:{frame.Id}";
        var pile = new PublicPersistentPileSource(target, CategoryTalismanSkill, grantId, 1, PublicPileIdentity(CategoryTalismanSkill, grantId));
        _cardZones.EnsurePublicPersistentPile(pile.Location);
        _publicPersistentPiles.Add((target, CategoryTalismanSkill, grantId), pile);
        _players[target].SkillGrants.Grant(new(grantId, CategoryTalismanSkill, grantId, "state:category-talisman"));
        var before = _movementSequence;
        var movement = new CategoryTalismanMovementReceipt(SkillProgramEffectOp.PlaceBoundHandAsCategoryTalisman,
            frame.InstructionIndex, cardId, CardLocation.Hand(frame.OwnerSeat), pile.Location, before, before);
        ReplaceRuntimeTop(frame = frame with { CategoryTalismanMovement = movement, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCard(card, movement.From, movement.To, new("program.category-talisman.place"));
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { CategoryTalismanMovement = movement with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new CategoryTalismanPlacedEvent(frame.Id, new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), frame.GameplayHash,
            target, cardId, GetProgramCardCategory(card.Kind), grantId, pile, before, _movementSequence));
        if (!TryDrainFireTargetMovement(frame)) ReturnCategoryTalismanMovement(frame);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome ReleaseCategoryTalisman(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.WindowContext is not { } context || !CanReleaseCategoryTalisman(frame.OwnerSeat, context) || LiveCategoryTalisman(frame.OwnerSeat) is not { } talisman)
            return SkillProgramStepOutcome.Continue;
        var card = PublicPileCards(talisman.Pile).Single(c => c.Id == talisman.CardId);
        var before = _movementSequence;
        var movement = new CategoryTalismanMovementReceipt(SkillProgramEffectOp.ReleaseCategoryTalisman, frame.InstructionIndex, card.Id,
            talisman.Pile.Location, CardLocation.DiscardPile, before, before);
        ReplaceRuntimeTop(frame = frame with { CategoryTalismanMovement = movement, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        _publicPersistentPiles.Remove((talisman.HolderSeat, CategoryTalismanSkill, talisman.Pile.SkillInstanceId));
        _players[talisman.HolderSeat].SkillGrants.RemoveGrant(talisman.GrantId);
        MoveCard(card, movement.From, movement.To, new("program.category-talisman.release"));
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { CategoryTalismanMovement = movement with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new CategoryTalismanReleasedEvent(frame.Id, talisman.FrameId, talisman.HolderSeat, card.Id, before, _movementSequence));
        if (!TryDrainFireTargetMovement(frame)) ReturnCategoryTalismanMovement(frame);
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 70)]
    private bool IsCategoryTalismanMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.CategoryTalismanMovement is { } receipt && receipt.InstructionIndex == frame.InstructionIndex &&
        effect?.Op == receipt.Operation && pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramContinuation(ProgramContinuationKind.Resume, 26050)]
    private bool ResumeCategoryTalismanMovement(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { CategoryTalismanMovement: not null } frame || frame.Id != id) return false;
        return ReturnCategoryTalismanMovement(frame);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26050)]
    private bool ReturnCategoryTalismanMovement(ProgramSkillFrame frame)
    {
        if (frame.CategoryTalismanMovement is not { } movement) return false;
        if (frame.PendingMovementContinuation is null || movement.After <= movement.Before ||
            _cardMovements.Count(m => m.CardId == movement.CardId && m.From == movement.From && m.To == movement.To && m.Sequence > movement.Before && m.Sequence <= movement.After) != 1)
            throw new InvalidOperationException("A talisman movement lost its exact once-paid entity receipt.");
        if (TryDrainFireTargetMovement(frame)) return true;
        ReplaceRuntimeTop(frame = frame with { CategoryTalismanMovement = null, PendingMovementContinuation = null });
        if (movement.Operation == SkillProgramEffectOp.UseRedJudgmentSlashAtDamageSource &&
            IssueHuqiSlash(frame, movement.JudgmentBind!) != SkillProgramStepOutcome.Continue) return true;
        AdvanceRuntimeProgram(frame.Id); return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26050)]
    private bool ContinueIssuedZhangLingBinding(ProgramSkillFrame frame) => frame.CategoryTalismanMovement is not null ||
        CompleteProgramEventHistory().OfType<ZhangLingBindingIssuedEvent>().Any(e => e.FrameId == frame.Id && e.GameplayHash == frame.GameplayHash &&
            e.Source == new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId));

    private SkillProgramStepOutcome UseHuqiSlash(ProgramSkillFrame supplied, string bind)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var binding = frame.CardSetBindings.Single(b => b.Name == bind);
        // A final judgment face is retained even if another real judgment effect
        // has already claimed the card. Only an entity still in Processing is
        // our cleanup cost; a claimed entity must never be taken back from hand.
        if (binding.CardIds is [var cardId] && _cardZones.GetLocation(cardId) == CardLocation.Processing)
        {
            var before = _movementSequence;
            var movement = new CategoryTalismanMovementReceipt(SkillProgramEffectOp.UseRedJudgmentSlashAtDamageSource,
                frame.InstructionIndex, cardId, CardLocation.Processing, CardLocation.DiscardPile, before, before, bind);
            ReplaceRuntimeTop(frame = frame with { CategoryTalismanMovement = movement, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
            MoveCard(GetAttackCard(cardId), movement.From, movement.To, new("program.huqi.judgment-finished"));
            ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { CategoryTalismanMovement = movement with { After = _movementSequence } });
            if (!TryDrainFireTargetMovement(frame)) ReturnCategoryTalismanMovement(frame);
            return SkillProgramStepOutcome.AwaitChild;
        }
        return IssueHuqiSlash(frame, bind);
    }
    private SkillProgramStepOutcome IssueHuqiSlash(ProgramSkillFrame frame, string bind)
    {
        var binding = frame.CardSetBindings.Single(b => b.Name == bind);
        if (binding.CardIds.Count == 0 && binding.FrozenRevealedSuit is null) return SkillProgramStepOutcome.Continue;
        var suit = binding.FrozenRevealedSuit ??
            throw new InvalidOperationException("Huqi requires its frozen actual final judgment suit.");
        var issued = CompleteProgramEventHistory().OfType<ZhangLingBindingIssuedEvent>().Single(e => e.FrameId == frame.Id);
        if (suit is not (Suit.Heart or Suit.Diamond) || issued.DamageSourceSeat is not { } source || !IsValidPlayerSeat(source) ||
            !_players[source].IsAlive || source == frame.OwnerSeat || !_players[frame.OwnerSeat].IsAlive ||
            !CanUseSlashTarget(_players[frame.OwnerSeat], _players[source], new Card(0, CardKind.Slash, Suit.None, 0),
                new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
                CardKind.Slash, noEffectiveRank: true, physicalCardIds: []))
            return SkillProgramStepOutcome.Continue;
        var attack = ActiveCardAttack?.ResolutionId;
        var duel = ActiveDuel?.OwnerFrameId;
        var childId = _resolutionSequence + 1;
        ReplaceRuntimeTop(frame = frame with { HuqiSlash = new(frame.InstructionIndex, source, suit, attack, duel, childId) });
        if (attack is { } attackId) UpdateCardAttackState(attackId, state => state! with { Active = false });
        if (duel is { } duelId) UpdateCardContinuations(duelId, state => state with { Duel = state.Duel! with { Active = false } });
        return UseTriggeredProgramVirtualSlash(frame, source);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 26050)]
    private bool RestoreHuqiSuspendedAttack(AttackCompletionReceipt completion)
    {
        if (completion.ProgramFrameId is not { } parentId || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuqiSlash: { } receipt } parent || parent.Id != parentId) return false;
        if (receipt.ChildUseId != completion.ResolutionId || receipt.InstructionIndex != parent.InstructionIndex ||
            _resolutionStack.Any(f => f.Id == receipt.ChildUseId)) throw new InvalidOperationException("Huqi lost its exact retired native Slash child.");
        if (receipt.SuspendedAttackId is { } attackId) UpdateCardAttackState(attackId, state => state! with { Active = true });
        if (receipt.SuspendedDuelId is { } duelId) UpdateCardContinuations(duelId, state => state with { Duel = state.Duel! with { Active = true } });
        ReplaceRuntimeTop(parent with { HuqiSlash = null }); AdvanceRuntimeProgram(parent.Id); return true;
    }
}
