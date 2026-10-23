namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasAnniversaryCheZhouContent => _contentRegistry.Skills.ContainsKey(AnniversaryCheZhouComposition.Shefu);
    private bool HasAnniversaryCheZhouRules => HasAnniversaryCheZhouContent;

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46400)]
    private void CaptureAnniversaryCheZhouAge(IGameEvent fact)
    {
        if (!HasAnniversaryCheZhouContent) return;
        if (fact is RoundStartedEvent round)
        {
            if (ProgramEventHistory<AnniversaryCheZhouRoundBoundaryEvent>().Any(e => e.Round == round.RoundNumber))
                throw new InvalidOperationException("Che Zhou cannot issue the same actual round boundary twice.");
            AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouRoundBoundaryEvent(round.RoundNumber, _movementSequence));
        }
        if (fact is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use) return;
        if (use.AnniversaryCheZhouAge is not null || ProgramEventHistory<AnniversaryCheZhouUseAgeCapturedEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Che Zhou cannot replace an accepted Use's original age receipt.");
        var r = new AnniversaryCheZhouUseAge(action.ActionId, action.ActorSeat, Math.Max(1, _roundNumber), _movementSequence,
            action.PhysicalCards.Count, AnniversaryCheZhouAgeAt(use, Math.Max(1, _roundNumber), _movementSequence));
        UpdateLifecycleCardUse(use.Id, f => f with { AnniversaryCheZhouAge = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouUseAgeCapturedEvent(use.Id, r.ActionId, r.Actor, r.Round, r.Cutoff, r.Materials, r.Age));
    }
    private int AnniversaryCheZhouAgeAt(CardUseFrame use, int round, long cutoff)
    {
        var action = use.Action!;
        if (action.PhysicalCards.Count == 0) return 0;
        var minimum = int.MaxValue;
        foreach (var material in action.PhysicalCards)
        {
            if (material.CardId <= 0 || material.From != CardLocation.Hand(action.ActorSeat)) return 0;
            var locationAtDeclaration = _cardMovements.LastOrDefault(m => m.CardId == material.CardId && m.Sequence <= cutoff);
            if (locationAtDeclaration is null) return 0;
            var heldThrough = cutoff;
            if (locationAtDeclaration.To != material.From)
            {
                // Most Uses declare before payment. Declarations can instead transfer
                // an already-paid cost; only that exact native receipt ends its stint.
                CardMovementRecord? departure = null;
                if (use.PaymentMovementReceipt is { } native && native.OwnerFrameId == use.Id && native.ActionId == action.ActionId)
                    departure = native.Batch.Movements.SingleOrDefault(m => m.CardId == material.CardId && m.From == material.From &&
                        m.Sequence <= cutoff && _cardMovements.Contains(m));
                if (departure is null && use.AcceptedDeclarationPayment is { } paid && paid.OwnerFrameId == use.Id &&
                    paid.ProviderSeat == action.ActorSeat && paid.Cost == material && locationAtDeclaration.From == material.From &&
                    locationAtDeclaration.To == CardLocation.Processing && locationAtDeclaration.Reason.Value == "conversion.declaration.pay" &&
                    ProgramEventHistory<CardDeclarationCommittedEvent>().Any(e => e.DeclarationId == paid.DeclarationId && e.OwnerSeat == paid.ProviderSeat))
                    departure = locationAtDeclaration;
                if (departure is null || departure.Sequence != locationAtDeclaration.Sequence) return 0;
                heldThrough = departure.Sequence - 1;
            }
            var gain = _cardMovements.LastOrDefault(m => m.CardId == material.CardId && m.Sequence <= heldThrough &&
                m.To == material.From && m.From != m.To);
            if (gain is null || _cardMovements.Any(m => m.CardId == material.CardId && m.Sequence > gain.Sequence && m.Sequence <= heldThrough &&
                m.From == material.From && m.To != m.From)) return 0;
            var acquiredRound = ProgramEventHistory<AnniversaryCheZhouRoundBoundaryEvent>().Where(e => e.MovementSequence < gain.Sequence)
                .Select(e => e.Round).DefaultIfEmpty(1).Max();
            minimum = Math.Min(minimum, Math.Max(1, checked(round - acquiredRound + 1)));
        }
        return minimum;
    }
    private bool ValidAnniversaryCheZhouUseAge(CardUseFrame use)
    {
        if (use.AnniversaryCheZhouAge is not { } r || use.Action is not { Type: CardActionType.Use } a || r.ActionId != a.ActionId ||
            r.Actor != a.ActorSeat || r.Round <= 0 || r.Round > Math.Max(1, _roundNumber) || r.Cutoff < 0 || r.Cutoff > _movementSequence ||
            r.Materials != a.PhysicalCards.Count || r.Age != AnniversaryCheZhouAgeAt(use, r.Round, r.Cutoff)) return false;
        return ProgramEventHistory<AnniversaryCheZhouUseAgeCapturedEvent>().Where(e => e.UseId == use.Id).ToArray() is [var issued] &&
            issued == new AnniversaryCheZhouUseAgeCapturedEvent(use.Id, r.ActionId, r.Actor, r.Round, r.Cutoff, r.Materials, r.Age);
    }
    private bool TryGetAnniversaryCheZhouDamageReplacement(IDamageAttempt attack, out int amount)
    {
        amount = 0;
        if (!HasAnniversaryCheZhouContent || attack is not CardAttackHandle || attack.IsSourceLess || attack.IsDelayedJudgmentDamage ||
            attack.IsProgramJudgmentDamage || attack.EffectiveCardKind is null || !IsValidPlayerSeat(attack.CardUserSeat) ||
            !IsValidPlayerSeat(attack.TargetSeat)) return false;
        var user = _players[attack.CardUserSeat]; var target = _players[attack.TargetSeat];
        if (!(user.IsAlive && HasRuntimeSkill(user, AnniversaryCheZhouComposition.Shefu)) &&
            !(target.IsAlive && target.Seat != user.Seat && HasRuntimeSkill(target, AnniversaryCheZhouComposition.Shefu))) return false;
        var use = LifecycleCardUse(attack.ResolutionId);
        // A native card with no physical Hand materials has spent zero rounds in Hand.
        if (use is null) return attack.PhysicalCards.Count == 0;
        if (!ValidAnniversaryCheZhouUseAge(use) || use.Action!.ActorSeat != attack.CardUserSeat ||
            use.Action.EffectiveKind != attack.EffectiveCardKind || use.AnniversaryCheZhouAge is not { } r)
            throw new InvalidOperationException("Shefu lost the original accepted Use, actual user or frozen Hand age.");
        amount = r.Age; return true;
    }
    private void RecordAnniversaryCheZhouDamageReplacement(IDamageAttempt attack, int previousAmount, int replacement)
    {
        AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouDamageReplacedEvent(attack.ResolutionId, attack.CardUserSeat, attack.TargetSeat, previousAmount, replacement));
        AddLog("SkillTriggered", $"【慑伏】令本次牌造成的伤害改为 {replacement}。", attack.CardUserSeat, attack.TargetSeat);
    }
}
