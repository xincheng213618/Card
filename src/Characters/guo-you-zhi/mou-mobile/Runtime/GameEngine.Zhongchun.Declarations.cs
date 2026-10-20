namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool MatchesMouGyzDeclaration(ProgramSkillFrame root, CardDeclarationFrame d)
    {
        if (root.MouGuoYouZhi is not { Stage: MouGuoYouZhiStage.IssuingUse, Native: { UseId: 0, ActionId: 0 } n } r || !ValidMouGyzNative(n, root, false) || d.OwnerSeat != n.ActorSeat || d.TurnNumber != r.ActualTurn ||
            d.Return is not { Purpose: CardDeclarationPurpose.Use, UseAction: { } a } ret || ret.ParentFrameId != root.Id || ret.ParentKind != root.Kind || ret.ParentStep != root.Step || ret.ActorSeat != n.ActorSeat || !SameMouGyzNative(n, n with { Action = a }) ||
            d.Payment is not { Claimed: false } p || p.DeclarationId != d.Id || p.OwnerFrameId != d.Id || p.ProviderSeat != n.ActorSeat || p.ActorSeat != n.ActorSeat || p.Cost.CardId != n.CardId || p.Cost.From != CardLocation.Hand(n.ActorSeat) || n.Action.ConversionSource is not { } source || p.Source != source || source.OwnerSeat != n.ActorSeat || ViewAsRule(source) is not { DeclarationValidation: not null } rule || rule.OutputKind != p.DeclaredKind || !d.TargetSeats.SequenceEqual(a.TargetSeats)) return false;
        return ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e => e.DeclarationId == d.Id && e.OwnerSeat == n.ActorSeat && e.ActorSeat == n.ActorSeat && e.DeclaredKind == p.DeclaredKind && e.TargetSeats.SequenceEqual(a.TargetSeats)) == 1 &&
            _cardMovements.Count(m => m.Sequence > r.After && m.CardId == n.CardId && m.From == p.Cost.From && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay") == 1;
    }
    private bool IsMouGyzDeclarationQuery(CharacterState actor, int? selectedId) => selectedId is { } id && actor.IsAlive &&
        _resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Applying, Succeeded: true } d && _resolutionStack.Count >= 2 && _resolutionStack[^2] is ProgramSkillFrame root && MatchesMouGyzDeclaration(root, d) && d.OwnerSeat == actor.Seat && d.Payment.Cost.CardId == id && _cardZones.GetLocation(id) == CardLocation.Processing && UnclaimedDeclarationPayment(actor.Seat, id) == d.Payment;
    private bool MouGyzDeclarationEdge(int i)
    {
        if (i < 1 || _resolutionStack[i - 1] is not CardDeclarationFrame d || _resolutionStack.SingleOrDefault(f => f.Id == d.Return.ParentFrameId) is not ProgramSkillFrame root || !MatchesMouGyzDeclaration(root, d)) return false;
        if (_resolutionStack[i] is CardDeclarationChallengeFrame c) return d.Stage == CardDeclarationStage.Challenging && c.ParentFrameId == d.Id && c.SeatIndex >= 0 && c.SeatIndex < c.EligibleSeats.Count && c.EligibleSeats.All(s => IsValidPlayerSeat(s) && s != d.OwnerSeat);
        if (_resolutionStack[i] is CardsMovedTriggerWindowFrame moved) return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning && d.ActiveChildFrameId == moved.Id && moved.ResumeDeclarationFrameId == d.Id && moved.Batch.ParentFrameId == d.Id && moved.Batch.Movements.Count == 1 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.CardId == d.Payment.Cost.CardId && (d.Stage == CardDeclarationStage.Paying ? m.From == d.Payment.Cost.From && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay" : m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled"));
        return _resolutionStack[i] is ProgramLifecycleTriggerWindowFrame skills && d.Stage == CardDeclarationStage.Granting && skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == d.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeCardDeclaration;
    }
    private bool ReturnMouGyzDeclaration(CardDeclarationFrame d, bool success)
    {
        if (success || d.Return.Purpose != CardDeclarationPurpose.Use || _resolutionStack.SingleOrDefault(f => f.Id == d.Return.ParentFrameId) is not ProgramSkillFrame { MouGuoYouZhi: { Stage: MouGuoYouZhiStage.IssuingUse } r } root) return false;
        if (_resolutionStack.Any(f => f.Id == d.Id) || _resolutionStack.LastOrDefault()?.Id != root.Id || d.Stage != CardDeclarationStage.Returning || !MatchesMouGyzDeclaration(root, d) || ProgramEventHistory<MouGuoYouZhiNativeIssuedEvent>().Any(e => e.FrameId == root.Id)) throw new InvalidOperationException("Zhongchun declaration failed outside its original obtained entity.");
        ReplaceRuntimeTop(root = root with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.Returned, Native = null } }); FinishMouGyz(root); return true;
    }
}
