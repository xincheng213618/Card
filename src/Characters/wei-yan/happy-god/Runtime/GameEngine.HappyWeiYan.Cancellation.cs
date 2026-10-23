namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHappyWeiYanDraw(NativeDrawOutcome draw, int owner, long frameId)
    {
        if (draw.RecipientSeat != owner || draw.RequestedCount != 2 || draw.SequenceBefore < 0 || draw.SequenceAfter < draw.SequenceBefore || draw.SequenceAfter > _movementSequence || draw.ActualDrawIds.Count > 2 || draw.ActualDrawIds.Distinct().Count() != draw.ActualDrawIds.Count ||
            draw.ActualDrawIds.Any(id => _cardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner) && m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.Reason.Value == HappyEzhiDraw) != 1)) return false;
        if (draw.AttemptFrameId is { } attempt) return ProgramEventHistory<NativeDrawAttemptStartedEvent>().Count(e => e.Context.AttemptFrameId == attempt && e.Context.ParentFrameId == frameId && e.Context.RecipientSeat == owner && e.Context.RequestedCount == 2 && e.Context.Reason.Value == HappyEzhiDraw && e.Return.Kind == NativeDrawAttemptReturnKind.ProgramOperation && e.Return.InstructionIndex == 1) == 1 &&
            ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt && e.ParentFrameId == frameId && e.RecipientSeat == owner && e.RequestedCount == 2 && e.Replaced == (draw.Replacement is not null)) == 1;
        return draw.Replacement is null;
    }
    private bool ValidHappyWeiYanCancellation(HappyWeiYanCancellation proof, CardActionContext action, long parent)
    {
        if (proof.ParentId != parent || proof.Source.SkillId != HappyWeiYanComposition.Ezhi || proof.Source.BindingId != "bone-use" || proof.Source.OwnerSeat == action.ActorSeat || proof.ActualTurn != _turnNumber ||
            proof.GameplayHash != _contentRegistry.GetSkill(proof.Source.SkillId).Program!.GameplayHash || !SameAnniversaryHzAction(proof.Action, action) || action.PhysicalCards.Count == 0 ||
            proof.Draw is not { } draw || !ValidHappyWeiYanDraw(draw, proof.Source.OwnerSeat, proof.FrameId) || draw.SequenceAfter > proof.Before ||
            proof.After <= proof.Before || proof.After > _movementSequence || proof.Bone.Holder != action.ActorSeat ||
            GetProgramCardCategory(proof.Bone.PrintedKind) != GetProgramCardCategory(action.EffectiveKind) ||
            HappyBoneAt(proof.Bone.CardId, CardLocation.Equipment(action.ActorSeat), proof.Before) is not { } original || original.Sequence != proof.Bone.Sequence || original.Source != proof.Bone.Source || original.PrintedKind != proof.Bone.PrintedKind ||
            _cardMovements.Count(m => m.CardId == proof.Bone.CardId && m.CardKind == proof.Bone.PrintedKind && m.From == CardLocation.Equipment(action.ActorSeat) && m.Reason.Value == HappyEzhiDiscard && m.Sequence > proof.Before && m.Sequence <= proof.After) != 1)
            return false;
        return ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == proof.FrameId && e.OwnerSeat == proof.Source.OwnerSeat && e.SkillId == proof.Source.SkillId && e.BindingId == proof.Source.BindingId && e.SkillInstanceId == proof.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseCommitted) == 1 &&
            ProgramEventHistory<HappyWeiYanEzhiPaidEvent>().Count(e => e == new HappyWeiYanEzhiPaidEvent(proof.FrameId, proof.Source, proof.GameplayHash, proof.ActualTurn, proof.WindowId, parent, action.ActionId, action.ActorSeat, action.EffectiveKind, proof.Bone.CardId, proof.Bone.PrintedKind, proof.Before, proof.After, true)) == 1;
    }
    private bool IsHappyWeiYanUseIneffective(CardUseFrame use)
    {
        if (use.HappyWeiYanCancellation is not { } proof) return false;
        if (use.Action is not { Type: CardActionType.Use } action || !ValidHappyWeiYanCancellation(proof, action, use.Id)) throw new InvalidOperationException("Ezhi lost the original paid actual whole-Use cancellation.");
        return true;
    }
    private bool ContinueHappyWeiYanIneffectiveSimple(long id, ProgramSimpleCardContinuation continuation)
    {
        if (LifecycleCardUse(id) is not { } use || !IsHappyWeiYanUseIneffective(use)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id) throw new InvalidOperationException("Ezhi lost the original ineffective native simple Use.");
        foreach (var card in GetCardUsePhysicalCards(id).Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing)) MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, new("skill-program.happy:ezhi.cleanup"));
        var original = use.CardId == 0 ? new Card(0, use.CardKind, Suit.None, 0) : GetAdvancedCard(use.CardId);
        FinishCardUse(id, original, use.CardKind); return true;
    }
    private bool HasHappyWeiYanIneffectiveResponse(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (continuation != ProgramCardContinuation.Dodge || LifecycleCardUse(attack.ResolutionId)?.HappyWeiYanResponses is not { } proofs) return false;
        var matches = proofs.Where(p => p.Action.ActionId == action.ActionId).ToArray(); if (matches.Length == 0) return false;
        if (action.ParentActionId != LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId || action.EffectiveKind != CardKind.Dodge || action.ActorSeat != attack.TargetSeat || matches.Any(p => !ValidHappyWeiYanCancellation(p, action, attack.ResolutionId)))
            throw new InvalidOperationException("Ezhi's canceled Dodge lost its paid native Slash-response owner.");
        return true;
    }
    private bool ContinueHappyWeiYanIneffectiveDodge(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (!HasHappyWeiYanIneffectiveResponse(attack, action, continuation)) return false;
        ClearRoundOnceCardlessDodgeResponse(attack); ClearTieredRoundDodgeResponse(attack); ClearChainedStateBasicDodgeResponse(attack); ClearDrawFundedDistinctBasicDodgeResponse(attack);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); if (!ApplyAttackDamage(attack)) CompleteAttack(attack); return true;
    }
    private bool HappyWeiYanCounterspellInvalidated(NullificationWindowFrame pending, CardActionContext action)
    {
        if (pending.HappyWeiYanCancellation is not { } proof) return false;
        if (pending.NamedAmbushResponse?.ActionId != action.ActionId || !ValidHappyWeiYanCancellation(proof, action, pending.Id))
            throw new InvalidOperationException("Ezhi's canceled Nullification lost its original paid chain node.");
        return true;
    }
}
