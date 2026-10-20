namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPhaseStartCardUseParticipant(int actor) => HasOtherPlayRestrictionPolicy(actor) ||
        _turnCardUseEffects.ActionProhibitions.Any(ban => ban.TurnNumber == _turnNumber && ban.TurnSeat == _currentSeat &&
            ban.Source.OwnerSeat == actor && CompleteProgramEventHistory().OfType<PhaseStartCardControlPaidEvent>().Any(e =>
                e.Receipt.ProgramFrameId == ban.ParentFrameId && e.Receipt.Kind == PhaseStartCardControlKind.StartingEquipment &&
                e.Receipt.ArmorAcquired && e.Receipt.Source.OwnerSeat == actor && e.Receipt.Source.SkillId == ban.Source.SkillId &&
                e.Receipt.Source.BindingId == ban.Source.BindingId && e.Receipt.Source.SkillInstanceId == ban.Source.SkillInstanceId &&
                e.Receipt.ActualTurnNumber == ban.TurnNumber && e.Receipt.ActualTurnOwnerSeat == ban.TurnSeat));
    private bool HasOtherPlayRestrictionPolicy(int actor) => _phase == TurnPhase.Play && actor == _currentSeat &&
        _players[actor].IsAlive && HasCardPolicy(_players[actor], SkillProgramCardPolicyKind.StopUsingAfterTrickOrHpCount);
    private bool IsOtherPlayRestrictionUseForbidden(int actor, CardKind kind, CardActionType type) =>
        HasOtherPlayRestrictionPolicy(actor) &&
        (type == CardActionType.Use || type == CardActionType.Response &&
            (kind == CardKind.Nullification || IsProgramResponseCardUse(_players[actor], kind))) &&
        (GetActualPlayPhaseUseCount(actor) >= _players[actor].Hp ||
            CompleteProgramEventHistory().OfType<ActualPlayPhaseCardUseRecordedEvent>().Any(e => e.State.ActorSeat == actor &&
                e.State.TurnNumber == _turnNumber && e.State.PhaseInstanceId == _cardUseDebitPhaseInstanceId &&
                e.EffectiveCardKind is { } used && CardCatalog.Get(used).CategoryName == "锦囊牌"));
    private void GrantOtherPlayRestriction(ProgramSkillFrame f)
    {
        if (!HasExactPhaseStartCardControl(f) || f.PhaseStartCardControl is not
                { Kind: PhaseStartCardControlKind.OtherPlayRestriction, Stage: PhaseStartCardControlStage.GrantChildren,
                  GrantedSkillId: { } skillId, Payment: { } payment } r || f.PendingMovementContinuation is not null ||
            CompleteProgramEventHistory().OfType<OtherPlayRestrictionGrantedEvent>().Any(e => e.ProgramFrameId == f.Id))
            throw new InvalidOperationException("Other-Play restriction requires its exact once-paid, drained phase owner grant.");
        var recipient = _players[r.ActualTurnOwnerSeat];
        if (!recipient.IsAlive) return;
        var sourceId = $"turn:{r.ActualTurnNumber}:{r.ActualTurnOwnerSeat}:phase-control:{f.Id}:{r.Source.SkillId}:{r.Source.SkillInstanceId}";
        var existing = GetSkillBindingShard(recipient).ActiveGrants.Where(g => g.SkillId == skillId)
            .OrderBy(g => g.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        var instance = existing?.SkillInstanceId ?? $"turn-shared:{recipient.Seat}:{skillId}";
        recipient.SkillGrants.Grant(new SkillGrant($"{sourceId}:{skillId}", skillId, instance, sourceId,
            TurnExpiry: new SkillGrantTurnExpiry(r.ActualTurnNumber, r.ActualTurnOwnerSeat)));
        var distance = !IsSlashCard(payment.Kind) &&
            !(CardCatalog.Get(payment.Kind).CategoryName == "锦囊牌" && !IsRedSuit(payment.EffectiveSuit) && payment.EffectiveSuit != Suit.None);
        AdvanceEventRulesAndQueueFact(new OtherPlayRestrictionGrantedEvent(f.Id, r.PhaseParentFrameId, r.Source, r.GameplayHash,
            r.ActualTurnNumber, r.ActualTurnOwnerSeat, r.PhaseInstanceId, recipient.Seat, skillId, sourceId, distance,
            payment.CardId, payment.SequenceBefore, payment.SequenceAfter));
    }
    private void GrantStartingEquipmentSlashProhibition(ProgramSkillFrame f)
    {
        if (!HasExactPhaseStartCardControl(f) || f.PhaseStartCardControl is not
                { Kind: PhaseStartCardControlKind.StartingEquipment, Stage: PhaseStartCardControlStage.GrantChildren,
                  ArmorAcquired: true, Payment: { } p } r || p.To != CardLocation.Hand(f.OwnerSeat) ||
            p.From.Zone != CardZoneKind.Equipment || EquipmentCatalog.Get(p.Kind).Slot != EquipmentSlot.Armor ||
            f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Starting equipment control requires its actual paid armor acquisition.");
        var ban = _turnCardUseEffects.GrantActionProhibition(r.ActualTurnNumber, r.ActualTurnOwnerSeat, f.Id, f.InstructionIndex - 1,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash], [CardActionType.Use, CardActionType.Response]);
        AdvanceEventRulesAndQueueFact(new CardActionProhibitionGrantedEvent(ban));
    }
    private bool IsExactOtherPlayRestrictionGrant(OtherPlayRestrictionGrantedEvent grant)
    {
        if (grant.ProgramFrameId <= 0 || grant.PhaseParentFrameId <= 0 || grant.RecipientSeat != grant.ActualTurnOwnerSeat ||
            !IsValidPlayerSeat(grant.RecipientSeat) || !IsValidPlayerSeat(grant.Source.OwnerSeat) || grant.Source.OwnerSeat == grant.RecipientSeat ||
            _contentRegistry.GetSkill(grant.Source.SkillId).Program is not { } program || program.GameplayHash != grant.GameplayHash ||
            ProgramInstructionResolver.Default.FindTrigger(program, grant.Source.BindingId)?.Effects is not
                [{ Op: SkillProgramEffectOp.GrantOtherPlayUseRestriction, SkillIds: [var skill] }] || skill != grant.SkillId ||
            !ActualHandGainMutationBinding(grant.ProgramFrameId, grant.Source.SkillId, grant.Source.OwnerSeat,
                SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramEffectOp.GrantOtherPlayUseRestriction)) return false;
        var paid = CompleteProgramEventHistory().OfType<PhaseStartCardControlPaidEvent>().Where(e => e.Receipt.ProgramFrameId == grant.ProgramFrameId).ToArray();
        if (paid is not [{ Receipt: var r }] || r.Source != grant.Source || r.GameplayHash != grant.GameplayHash ||
            r.PhaseParentFrameId != grant.PhaseParentFrameId || r.ActualTurnNumber != grant.ActualTurnNumber ||
            r.ActualTurnOwnerSeat != grant.ActualTurnOwnerSeat || r.PhaseInstanceId != grant.PhaseInstanceId || r.GrantedSkillId != grant.SkillId ||
            r.Payment is not { } p || p.CardId != grant.CostCardId || p.SequenceBefore != grant.CostSequenceBefore ||
            p.SequenceAfter != grant.CostSequenceAfter || p.From.OwnerSeat != grant.Source.OwnerSeat) return false;
        var distance = !IsSlashCard(p.Kind) && !(CardCatalog.Get(p.Kind).CategoryName == "锦囊牌" &&
            p.EffectiveSuit != Suit.None && !IsRedSuit(p.EffectiveSuit));
        return distance == grant.DirectedDistanceOne && p.SequenceAfter > p.SequenceBefore &&
            grant.GrantSourceId == $"turn:{r.ActualTurnNumber}:{r.ActualTurnOwnerSeat}:phase-control:{r.ProgramFrameId}:{r.Source.SkillId}:{r.Source.SkillInstanceId}" &&
            _cardMovements.Count(m => m.CardId == p.CardId && m.From == p.From && m.To == p.To && m.CardKind == p.Kind &&
                m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.Reason.Value == PhaseStartControlReason(r.Kind)) == 1;
    }
    [ProgramDistanceRule(ProgramDistanceStage.DistanceProjection, 50)]
    private RuleQueryEvaluation ApplyOtherPlayRestrictionDistance(CharacterState source, CharacterState target, RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat || !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantOtherPlayUseRestriction)) return original;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _currentSeat)) return original;
        var grants = history.OfType<OtherPlayRestrictionGrantedEvent>().Where(g => g.DirectedDistanceOne &&
            g.ActualTurnNumber == _turnNumber && g.ActualTurnOwnerSeat == _currentSeat && g.RecipientSeat == source.Seat &&
            g.Source.OwnerSeat == target.Seat && IsExactOtherPlayRestrictionGrant(g)).ToArray();
        if (grants.Length == 0) return original;
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-other-play-one", ConvertRuleValue(original))],
            grants.Select(g => (RuleQueryContribution)new FiniteRuleQueryContribution($"other-play-distance:{g.ProgramFrameId}", SkillRuleOperation.Set, 1)).ToArray());
    }
}
