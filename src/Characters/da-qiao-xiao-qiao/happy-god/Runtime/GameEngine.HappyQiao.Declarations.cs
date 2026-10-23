namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesHappyQiaoShutuDeclaration(ProgramSkillFrame frame, CardDeclarationFrame declaration)
    {
        if (frame.HappyQiao is not { Stage: HappyQiaoStage.Declaration, Native: { UseId: 0, ActionId: 0, Plan: { Form: "native", Cards: [var cardId], Action.ConversionSource: { } source } plan } native } receipt ||
            declaration.Return is not { Purpose: CardDeclarationPurpose.HappyQiaoShutuUse, UseAction: { } action } ret || ret.ParentFrameId != frame.Id || ret.ParentKind != frame.Kind || ret.ParentStep != frame.Step ||
            ret.ActorSeat != frame.OwnerSeat || ret.FinalEffectiveKind != plan.Kind || !SameHappyQiaoPlan(plan, plan with { Action = action }) || declaration.OwnerSeat != frame.OwnerSeat || declaration.TurnNumber != receipt.Turn ||
            declaration.Payment is not { Claimed: false } payment || payment.DeclarationId != declaration.Id || payment.OwnerFrameId != declaration.Id || payment.ActorSeat != frame.OwnerSeat || payment.ProviderSeat != frame.OwnerSeat ||
            payment.Cost.CardId != cardId || payment.Cost.From != CardLocation.Hand(frame.OwnerSeat) || payment.Source != source || ViewAsRule(source) is not { DeclarationValidation: not null } rule ||
            rule.OutputKind != payment.DeclaredKind || !declaration.TargetSeats.SequenceEqual(plan.Targets) || native.Source != receipt.Source) return false;
        return ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e => e.DeclarationId == declaration.Id && e.OwnerSeat == frame.OwnerSeat && e.ActorSeat == frame.OwnerSeat && e.DeclaredKind == payment.DeclaredKind && e.TargetSeats.SequenceEqual(plan.Targets)) == 1 &&
            _cardMovements.Count(m => m.Sequence > receipt.Before && m.CardId == cardId && m.From == payment.Cost.From && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay") == 1;
    }
    private bool ReturnHappyQiaoShutuDeclaration(CardDeclarationFrame declaration, bool succeeded)
    {
        if (declaration.Return.Purpose != CardDeclarationPurpose.HappyQiaoShutuUse) return false;
        if (_resolutionStack.Any(f => f.Id == declaration.Id) || _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != declaration.Return.ParentFrameId ||
            declaration.Stage != CardDeclarationStage.Returning || !MatchesHappyQiaoShutuDeclaration(frame, declaration) || ProgramEventHistory<HappyQiaoDeclarationReturnedEvent>().Any(e => e.DeclarationId == declaration.Id))
            throw new InvalidOperationException("Shutu declaration returned to another or already paid native intent.");
        var receipt = frame.HappyQiao!;
        if (succeeded)
        {
            if (declaration.Succeeded != true || _cardZones.GetLocation(declaration.Payment.Cost.CardId) != CardLocation.Processing || frame.AcceptedDeclarationPayment is { Claimed: false })
                throw new InvalidOperationException("Shutu cannot fake a passed declaration or pay its real entity twice.");
            ReplaceRuntimeTop(frame = frame with { AcceptedDeclarationPayment = declaration.Payment with { OwnerFrameId = frame.Id }, HappyQiao = receipt with { Stage = HappyQiaoStage.IssuingUse, DeclarationId = declaration.Id, DeclarationPassed = true } });
        }
        else ReplaceRuntimeTop(frame = frame with { HappyQiao = receipt with { Stage = HappyQiaoStage.ReturnedUse, Native = null, DeclarationId = declaration.Id, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new HappyQiaoDeclarationReturnedEvent(frame.Id, declaration.Id, succeeded));
        if (succeeded) IssueHappyQiaoShutuUse(frame); else ContinueHappyQiao(frame.Id);
        return true;
    }
    private bool HappyQiaoShutuDeclarationEdge(int index)
    {
        if (index < 1 || _resolutionStack[index - 1] is not CardDeclarationFrame declaration ||
            _resolutionStack.SingleOrDefault(f => f.Id == declaration.Return.ParentFrameId) is not ProgramSkillFrame frame || !MatchesHappyQiaoShutuDeclaration(frame, declaration)) return false;
        if (_resolutionStack[index] is CardDeclarationChallengeFrame challenge) return declaration.Stage == CardDeclarationStage.Challenging && challenge.ParentFrameId == declaration.Id && challenge.SeatIndex >= 0 && challenge.SeatIndex < challenge.EligibleSeats.Count &&
            challenge.EligibleSeats.Distinct().Count() == challenge.EligibleSeats.Count && challenge.EligibleSeats.All(s => IsValidPlayerSeat(s) && s != declaration.OwnerSeat);
        if (_resolutionStack[index] is CardsMovedTriggerWindowFrame moved) return declaration.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning && declaration.ActiveChildFrameId == moved.Id && moved.ResumeDeclarationFrameId == declaration.Id && moved.Batch.ParentFrameId == declaration.Id &&
            moved.Batch.Movements is [var move] && _cardMovements.Contains(move) && move.CardId == declaration.Payment.Cost.CardId && (declaration.Stage == CardDeclarationStage.Paying
                ? move.From == declaration.Payment.Cost.From && move.To == CardLocation.Processing && move.Reason.Value == "conversion.declaration.pay"
                : move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && move.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled");
        return _resolutionStack[index] is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && declaration.Stage == CardDeclarationStage.Granting && skills.ResumeProgramFrameId == declaration.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeCardDeclaration;
    }
    private bool FinishHappyQiaoAlcoholCost(CardUseFrame use, Card card)
    {
        if (use.HappyQiaoShutuUse is not { Plan: { Form: "multi", Kind: CardKind.Alcohol } plan } native) return false;
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == native.FrameId) is not { } frame || !HappyQiaoNativeUseRidesOn(use, frame) || !plan.Cards.Contains(card.Id))
            throw new InvalidOperationException("Shutu lost its original multi-material native Alcohol cost set.");
        foreach (var id in plan.Cards) if (_cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(c => c.Id == id) is { } cost)
            MoveCard(cost, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
        return true;
    }
}
