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
    private void IssueMouLuSuPlayedDeclaration(ProgramSkillFrame f)
    {
        if(!MouLuSuAnniversary.Valid(f)||f.MouLuSuAnniversary is not{Stage:MouLuSuAnniversaryStage.PlayedDeclaration,Played:{Source:{}source,Cards:[var cardId]}plan,Response:null,ActionId:0}r||!MouLuSuPlayedPlans(f).Any(p=>SameMouLuSuPlayedPlan(p,plan))||ViewAsRule(source)?.DeclarationValidation is null||UnclaimedDeclarationPayment(r.Other,cardId)is not null)throw new InvalidOperationException("Mengmou requires its original current declared red Basic choice.");
        if(!TryBeginCardDeclaration(_players[r.Other],GetAdvancedCard(cardId),plan.Kind,source,DeclarationReturn(CardDeclarationPurpose.MouLuSuAnniversaryPlayed,r.Other,target:f.OwnerSeat,finalKind:plan.Kind),[]))throw new InvalidOperationException("Mengmou cannot bypass its required native declaration challenge.");
    }
    private bool MatchesMouLuSuPlayedDeclaration(ProgramSkillFrame f,CardDeclarationFrame d)
    {
        if(!MouLuSuAnniversary.Valid(f)||f.MouLuSuAnniversary is not{Stage:MouLuSuAnniversaryStage.PlayedDeclaration,Played:{Source:{}source,Cards:[var cardId]}plan,Response:null,ActionId:0}r||d.OwnerSeat!=r.Other||d.TurnNumber!=r.Turn||d.Return is not{Purpose:CardDeclarationPurpose.MouLuSuAnniversaryPlayed,UseAction:null}ret||ret.ParentFrameId!=f.Id||ret.ParentKind!=f.Kind||ret.ParentStep!=f.Step||ret.ActorSeat!=r.Other||ret.TargetSeat!=f.OwnerSeat||ret.FinalEffectiveKind!=plan.Kind||d.TargetSeats.Count!=0||d.Payment is not{Claimed:false}p||p.DeclarationId!=d.Id||p.OwnerFrameId!=d.Id||p.ProviderSeat!=r.Other||p.ActorSeat!=r.Other||p.Cost.CardId!=cardId||p.Cost.From!=CardLocation.Hand(r.Other)||p.Source!=source||source.OwnerSeat!=r.Other||ViewAsRule(source)is not{DeclarationValidation:not null}rule||rule.OutputKind!=p.DeclaredKind||!MatchesRequiredCard(p.DeclaredKind,plan.Kind))return false;
        return ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e=>e.DeclarationId==d.Id&&e.OwnerSeat==r.Other&&e.ActorSeat==r.Other&&e.DeclaredKind==p.DeclaredKind&&e.TargetSeats.Count==0)==1&&_cardMovements.Count(m=>m.Sequence>r.Before&&m.CardId==cardId&&m.From==p.Cost.From&&m.To==CardLocation.Processing&&m.Reason.Value=="conversion.declaration.pay")==1;
    }
    private bool ReturnMouLuSuPlayedDeclaration(CardDeclarationFrame d,bool succeeded)
    {
        if(d.Return.Purpose!=CardDeclarationPurpose.MouLuSuAnniversaryPlayed)return false;
        if(_resolutionStack.Any(x=>x.Id==d.Id)||_resolutionStack.LastOrDefault()is not ProgramSkillFrame f||f.Id!=d.Return.ParentFrameId||d.Stage!=CardDeclarationStage.Returning||!MatchesMouLuSuPlayedDeclaration(f,d)||ProgramEventHistory<MouLuSuAnniversaryDeclarationReturnedEvent>().Any(e=>e.DeclarationId==d.Id))throw new InvalidOperationException("Mengmou declaration returned to another or already completed native request.");
        if(succeeded)
        {
            if(d.Succeeded!=true||_cardZones.GetLocation(d.Payment.Cost.CardId)!=CardLocation.Processing||f.AcceptedDeclarationPayment is{Claimed:false})throw new InvalidOperationException("Mengmou cannot forge a passed declaration or repay an outstanding declaration entity.");
            ReplaceRuntimeFrame(f.Id,f=f with{AcceptedDeclarationPayment=d.Payment with{OwnerFrameId=f.Id}});
        }
        MouLuSuAnniversary.DeclarationReturned(f,d.Id,succeeded);return true;
    }
    private bool MouLuSuPlayedDeclarationEdge(int i)
    {
        if(i<1||_resolutionStack[i-1]is not CardDeclarationFrame d||_resolutionStack.SingleOrDefault(x=>x.Id==d.Return.ParentFrameId)is not ProgramSkillFrame f||!MatchesMouLuSuPlayedDeclaration(f,d))return false;
        if(_resolutionStack[i]is CardDeclarationChallengeFrame c)return d.Stage==CardDeclarationStage.Challenging&&c.ParentFrameId==d.Id&&c.SeatIndex>=0&&c.SeatIndex<c.EligibleSeats.Count&&c.EligibleSeats.Distinct().Count()==c.EligibleSeats.Count&&c.EligibleSeats.All(s=>IsValidPlayerSeat(s)&&s!=d.OwnerSeat);
        if(_resolutionStack[i]is CardsMovedTriggerWindowFrame m)return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning&&d.ActiveChildFrameId==m.Id&&m.ResumeDeclarationFrameId==d.Id&&m.Batch.ParentFrameId==d.Id&&m.Batch.Movements is[var move]&&_cardMovements.Contains(move)&&move.CardId==d.Payment.Cost.CardId&&(d.Stage==CardDeclarationStage.Paying?move.From==d.Payment.Cost.From&&move.To==CardLocation.Processing&&move.Reason.Value=="conversion.declaration.pay":move.From==CardLocation.Processing&&move.To==CardLocation.DiscardPile&&move.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled");
        return _resolutionStack[i]is ProgramLifecycleTriggerWindowFrame skills&&d.Stage==CardDeclarationStage.Granting&&skills.Window==SkillProgramTriggerWindow.SkillsChanged&&skills.ResumeProgramFrameId==d.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeCardDeclaration;
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
            var cards = plan.Cards.Select(GetAdvancedCard).ToArray(); var costs = cards.Select(c => UnclaimedDeclarationPayment(actor.Seat,c.Id)?.Cost??new CardActionCost(c.Id,c.Kind,FindOwnedCardLocation(actor,c),CapturePhysicalCardColor(actor.Seat,c))).ToArray();
            var preparedSuit=FreezeCompletedResponseUseSuit(actor,cards,plan.Kind);var suit=UnclaimedDeclarationPayment(actor.Seat,cards[0].Id)?.FrozenSuit??preparedSuit;
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
    private static bool SameMouLuSuPlayedAction(CardActionContext a,CardActionContext b)=>a.ActionId==b.ActionId&&a.ParentActionId==b.ParentActionId&&a.Type==b.Type&&a.ActorSeat==b.ActorSeat&&a.ProviderSeat==b.ProviderSeat&&a.RequesterSeat==b.RequesterSeat&&a.ResponderSeat==b.ResponderSeat&&a.OpponentSeat==b.OpponentSeat&&a.EffectiveKind==b.EffectiveKind&&a.EffectiveSuit==b.EffectiveSuit&&a.EffectiveRank==b.EffectiveRank&&a.EffectiveIsRed==b.EffectiveIsRed&&a.FactionOrigin==b.FactionOrigin&&a.TargetSeats.SequenceEqual(b.TargetSeats)&&(a.DesignatedTargetSeats is null?b.DesignatedTargetSeats is null:b.DesignatedTargetSeats is{}t&&a.DesignatedTargetSeats.SequenceEqual(t))&&a.PhysicalCards.SequenceEqual(b.PhysicalCards)&&a.ConversionChain.SequenceEqual(b.ConversionChain);
    private bool IsMouLuSuPlayedWindow(CardActionContext action,SkillProgramTriggerWindow window) => window is SkillProgramTriggerWindow.CardResponseAccepted or SkillProgramTriggerWindow.CardResponseCompleted && _resolutionStack.LastOrDefault() is ProgramSkillFrame { MouLuSuAnniversary: { Response: { } accepted } r } f && MouLuSuAnniversary.Valid(f) && action.ActionId == accepted.ActionId && action.Type == CardActionType.Response && action.ActorSeat == r.Other && action.ProviderSeat == r.Other && action.RequesterSeat is null && action.TargetSeats.Count == 0 && IsSlashCard(action.EffectiveKind) && r.ActionId == action.ActionId && r.Stage == (window == SkillProgramTriggerWindow.CardResponseAccepted ? MouLuSuAnniversaryStage.PlayedAccepted : MouLuSuAnniversaryStage.PlayedCompleted);
    private MouLuSuAnniversaryPlayedReturn? MouLuSuPlayedWindowReturn(CardActionContext action,SkillProgramTriggerWindow window) => IsMouLuSuPlayedWindow(action,window) ? new(_resolutionStack[^1].Id,action.ActionId,window) : null;
    private bool ValidMouLuSuPlayedWindow(ProgramCardTriggerWindowFrame window) => window.MouLuSuAnniversaryPlayed is { } back && window.Continuation is null && window.CompletedResponseReturn is null && window.AttackOwnerFrameId is null && window.ParentFrameId == back.FrameId && _resolutionStack.SingleOrDefault(x => x.Id == back.FrameId) is ProgramSkillFrame { MouLuSuAnniversary: { Response: { } response, Played: { } plan } r } f && MouLuSuAnniversary.Valid(f) && r.ActionId == back.ActionId && window.Action.ActionId == back.ActionId && SameMouLuSuPlayedAction(response,window.Action) && window.Action.Type == CardActionType.Response && window.Action.ActorSeat == r.Other && window.Action.ProviderSeat == r.Other && window.Action.RequesterSeat is null && window.Action.TargetSeats.Count == 0 && window.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(plan.Cards) && back.Window == (r.Stage == MouLuSuAnniversaryStage.PlayedAccepted ? SkillProgramTriggerWindow.CardResponseAccepted : r.Stage == MouLuSuAnniversaryStage.PlayedCompleted ? SkillProgramTriggerWindow.CardResponseCompleted : (SkillProgramTriggerWindow)(-1)) && ProgramEventHistory<MouLuSuAnniversaryPlayedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Index && e.ActionId == back.ActionId && !e.Completed) == 1;
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
