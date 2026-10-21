namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<MouLuSuAnniversaryPlayedPlan> MouLuSuPlayedPlans(ProgramSkillFrame f)
    {
        if (!MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { Yin: true, Operation: SkillProgramEffectOp.ResolveMouLuSuMengmou } r || r.Index >= r.X || !_players[r.Other].IsAlive) return [];
        var actor = _players[r.Other]; var plans = new List<MouLuSuAnniversaryPlayedPlan>();
        foreach (var card in GetResponseCards(actor,CardKind.Slash).Where(c => CanUseCardAsResponse(actor,c,CardKind.Slash)))
        {
            var kind = GetEffectiveResponseKind(actor,card,CardKind.Slash); var identity = HasProgramCardIdentity(actor,card);
            if (!identity && IsNativeResponseCard(card,CardKind.Slash) && (!IsOwnPublicPileCard(actor,card) || IsNativePublicPileOriginalChoice(actor,card,kind,true))) plans.Add(new(kind,null,false) { Cards = [card.Id] });
            foreach (var source in identity ? GetProgramCardIdentitySources(actor,card,kind,true) : GetProgramViewAsConversions(actor,card,kind,true)) plans.Add(new(kind,source,false) { Cards = [card.Id] });
        }
        plans.AddRange(GetProgramMultiCardViewAsSelections(actor,CardKind.Slash,true).Select(p => new MouLuSuAnniversaryPlayedPlan(p.OutputKind,p.Source,false) { Cards = p.Cards.Select(c => c.Id).ToArray() }));
        plans.AddRange(GetZhangbaSlashPairs(actor).Select(p => new MouLuSuAnniversaryPlayedPlan(CardKind.Slash,null,true) { Cards = p.Select(c => c.Id).ToArray() }));
        return Array.AsReadOnly(plans.ToArray());
    }
    private static bool SameMouLuSuPlayedPlan(MouLuSuAnniversaryPlayedPlan a,MouLuSuAnniversaryPlayedPlan b) => a.Kind == b.Kind && a.Source == b.Source && a.Zhangba == b.Zhangba && a.Cards.SequenceEqual(b.Cards);
    private void IssueMouLuSuPlayed(ProgramSkillFrame f)
    {
        if (!MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { Stage: MouLuSuAnniversaryStage.PlayedAccepted, Played: { } plan, Response: null, ActionId: 0 } r || !MouLuSuPlayedPlans(f).Any(p => SameMouLuSuPlayedPlan(p,plan))) throw new InvalidOperationException("Mengmou lost its exact currently legal Played Slash.");
        var actor = _players[r.Other]; CardActionContext action;
        if (plan.Source is { } multiSource && plan.Cards.Count > 1)
        {
            var selection = FindProgramMultiCardViewAsSelection(actor,plan.Cards,plan.Kind,true,multiSource) ?? throw new InvalidOperationException("Mengmou lost its original multi-card response materials.");
            action = MoveProgramMultiCardResponse(actor,selection,f.Id,f.OwnerSeat);
        }
        else
        {
            var cards = plan.Cards.Select(GetAdvancedCard).ToArray(); var costs = cards.Select(c => new CardActionCost(c.Id,c.Kind,FindOwnedCardLocation(actor,c),CapturePhysicalCardColor(actor.Seat,c))).ToArray();
            var suit = FreezeCompletedResponseUseSuit(actor,cards,plan.Kind);
            PrepareWuZhangFeiResponseRanks(actor,plan.Kind,cards,plan.Source); PrepareLeCaoZhiWenlanResponse(actor,plan.Kind,cards); PrepareMouGszHuResponse(actor,cards,plan.Kind,plan.Source,null);
            if (cards is [var one]) PrepareNativePublicPileResponsePayment(actor,one,plan.Kind,plan.Source);
            if (plan.Source is { } source) { ValidateLastZoneConversionCommit(source,actor,cards,true); ValidateOwnPublicPileConversionCommit(source,actor,cards,true,plan.Kind); ConsumeProgramViewAsUsage([source]); }
            foreach (var card in cards) { MoveCard(card,FindOwnedCardLocation(actor,card),CardLocation.Processing,CardMoveReasons.Respond,disclosure:CardMovementDisclosure.FaceUp); AdvanceEventRulesAndQueueFact(new CardRespondedEvent(card.Id,actor.Seat,f.OwnerSeat,plan.Kind)); }
            action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,null,CardActionType.Response,actor.Seat,actor.Seat,null,actor.Seat,f.OwnerSeat,plan.Kind,[],costs,plan.Source is { } conversion ? [conversion] : [],effectiveSuit:suit,effectiveIsRed:CaptureActionColor(costs,suit)));
            BindWuZhangFeiResponseRanks(action); AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
            if (plan.Source is { } converted) AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(f.Id,converted.SkillId,converted.BindingId,actor.Seat,plan.Cards,plan.Kind,false,[]));
        }
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,plan.Kind);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouLuSuAnniversary = f.MouLuSuAnniversary! with { Response = action, ActionId = action.ActionId, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryPlayedEvent(f.Id,r.Index,action.ActionId,false));
        if (!TryBeginProgramCardWindow(null,action,SkillProgramTriggerWindow.CardResponseAccepted,[f.OwnerSeat],null)) FinishMouLuSuPlayedAccepted(f);
    }
    private bool IsMouLuSuPlayedWindow(CardActionContext action,SkillProgramTriggerWindow window) => window is SkillProgramTriggerWindow.CardResponseAccepted or SkillProgramTriggerWindow.CardResponseCompleted && _resolutionStack.LastOrDefault() is ProgramSkillFrame { MouLuSuAnniversary: { Response: { } accepted } r } f && MouLuSuAnniversary.Valid(f) && action.ActionId == accepted.ActionId && action.Type == CardActionType.Response && action.ActorSeat == r.Other && action.ProviderSeat == r.Other && action.RequesterSeat is null && action.TargetSeats.Count == 0 && IsSlashCard(action.EffectiveKind) && r.ActionId == action.ActionId && r.Stage == (window == SkillProgramTriggerWindow.CardResponseAccepted ? MouLuSuAnniversaryStage.PlayedAccepted : MouLuSuAnniversaryStage.PlayedCompleted);
    private MouLuSuAnniversaryPlayedReturn? MouLuSuPlayedWindowReturn(CardActionContext action,SkillProgramTriggerWindow window) => IsMouLuSuPlayedWindow(action,window) ? new(_resolutionStack[^1].Id,action.ActionId,window) : null;
    private bool ValidMouLuSuPlayedWindow(ProgramCardTriggerWindowFrame window) => window.MouLuSuAnniversaryPlayed is { } back && window.Continuation is null && window.CompletedResponseReturn is null && window.AttackOwnerFrameId is null && window.ParentFrameId == back.FrameId && _resolutionStack.SingleOrDefault(x => x.Id == back.FrameId) is ProgramSkillFrame { MouLuSuAnniversary: { Response: { } response, Played: { } plan } r } f && MouLuSuAnniversary.Valid(f) && r.ActionId == back.ActionId && window.Action.ActionId == back.ActionId && System.Text.Json.JsonSerializer.Serialize(response) == System.Text.Json.JsonSerializer.Serialize(window.Action) && window.Action.Type == CardActionType.Response && window.Action.ActorSeat == r.Other && window.Action.ProviderSeat == r.Other && window.Action.RequesterSeat is null && window.Action.TargetSeats.Count == 0 && window.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(plan.Cards) && back.Window == (r.Stage == MouLuSuAnniversaryStage.PlayedAccepted ? SkillProgramTriggerWindow.CardResponseAccepted : r.Stage == MouLuSuAnniversaryStage.PlayedCompleted ? SkillProgramTriggerWindow.CardResponseCompleted : (SkillProgramTriggerWindow)(-1)) && ProgramEventHistory<MouLuSuAnniversaryPlayedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Index && e.ActionId == back.ActionId && !e.Completed) == 1;
    private bool TryReturnMouLuSuPlayedWindow(ProgramCardTriggerWindowFrame window)
    {
        if (window.MouLuSuAnniversaryPlayed is not { } returned) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuSuAnniversary: { } r } f || f.Id != returned.FrameId || window.ParentFrameId != f.Id || window.Action.ActionId != returned.ActionId || r.ActionId != returned.ActionId || !IsMouLuSuPlayedWindow(window.Action,returned.Window)) throw new InvalidOperationException("Mengmou response observer returned to another native Played receipt.");
        if (returned.Window == SkillProgramTriggerWindow.CardResponseAccepted) FinishMouLuSuPlayedAccepted(f); else FinishMouLuSuPlayedCompleted(f); return true;
    }
    private void FinishMouLuSuPlayedAccepted(ProgramSkillFrame f)
    {
        var r = f.MouLuSuAnniversary!; var action = r.Response!; if (!IsMouLuSuPlayedWindow(action,SkillProgramTriggerWindow.CardResponseAccepted)) throw new InvalidOperationException("Mengmou must complete its exact accepted response before disposing it.");
        ReplaceRuntimeTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.PlayedCostChildren }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        foreach (var cost in action.PhysicalCards)
        {
            if (IsProgramAlternativeCost(action,cost.CardId) || IsExchangedCardClaim(action.ActionId,cost.CardId) || TryGetSiruiDestroyedResponseCost(action,cost.CardId,out _)) continue;
            if (_cardZones.GetLocation(cost.CardId) != CardLocation.Processing) throw new InvalidOperationException("Mengmou cannot silently dispose a different response entity.");
            MoveCard(GetAdvancedCard(cost.CardId),CardLocation.Processing,CardLocation.DiscardPile,CardMoveReasons.ResponseFinished);
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouLuSuAnniversary = f.MouLuSuAnniversary! with { After = _movementSequence } }); MouLuSuAnniversary.Continue(f.Id);
    }
    private void BeginMouLuSuPlayedCompleted(ProgramSkillFrame f)
    {
        if (!MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { Stage: MouLuSuAnniversaryStage.PlayedCostChildren, Response: { } action } r || f.PendingMovementContinuation is not null) throw new InvalidOperationException("Mengmou must drain its actual Played payment before the Completed window.");
        ReplaceRuntimeTop(f = f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.PlayedCompleted } });
        if (!TryBeginProgramCardWindow(null,action,SkillProgramTriggerWindow.CardResponseCompleted,[f.OwnerSeat],null)) FinishMouLuSuPlayedCompleted(f);
    }
    private void FinishMouLuSuPlayedCompleted(ProgramSkillFrame f)
    {
        var r = f.MouLuSuAnniversary!; if (!IsMouLuSuPlayedWindow(r.Response!,SkillProgramTriggerWindow.CardResponseCompleted) || r.Played is not { } plan) throw new InvalidOperationException("Mengmou cannot repeat its native Played completion.");
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouLuSuAnniversary = f.MouLuSuAnniversary! with { Stage = MouLuSuAnniversaryStage.PlayedChildren, Index = r.Index + 1, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryPlayedEvent(f.Id,r.Index,r.ActionId,true)); MouLuSuAnniversary.Continue(f.Id);
    }
    private bool IsMouLuSuPlayedCostFinished(ProgramCardTriggerWindowFrame w,CardActionCost cost) => w.MouLuSuAnniversaryPlayed is { Window: SkillProgramTriggerWindow.CardResponseCompleted } back && ValidMouLuSuPlayedWindow(w) && _resolutionStack.SingleOrDefault(f => f.Id == back.FrameId) is ProgramSkillFrame { MouLuSuAnniversary: { } r } && w.Action.PhysicalCards.Contains(cost) && _cardMovements.Any(m => m.CardId == cost.CardId && m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished);
}
