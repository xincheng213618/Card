namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<GuquUseClaim> CaptureMouGuquClaims(long parent, long action, int actor, int provider,
        CardKind kind, IReadOnlyList<CardActionCost> materials)
    {
        var claims = new List<GuquUseClaim>();
        foreach (var source in CurrentMouGuquSources())
        {
            var que = materials.Where(c => IsMouGuquQueForSource(source.Source, c.CardId)).DistinctBy(c => c.CardId).ToArray();
            if (que.Length == 0) continue;
            var q = new GuquUseClaim(source.Source, source.Hash, parent, action, actor, provider, kind,
                _turnNumber, _turnProgression.OwnerSeat, _movementSequence, que.Length);
            claims.Add(q); AdvanceEventRulesAndQueueFact(new GuquUseClaimedEvent(q));
            for (var i = 0; i < que.Length; i++) AdvanceEventRulesAndQueueFact(new GuquUseMaterialClaimedEvent(parent, action,
                source.Source, i, que[i].CardId, que[i].CardKind, que[i].From));
        }
        return Array.AsReadOnly(claims.ToArray());
    }
    private void IssueMouGuquUse(CardUseFrame supplied)
    {
        if (!HasMouGuquCapability || supplied.Action is not { Type: CardActionType.Use } action) return;
        var ids = action.PhysicalCards.Select(c => c.CardId).ToArray();
        var source = action.ConversionChain.FirstOrDefault();
        if (supplied.GuquAdmission is not null || !IsMouGuquUseAllowed(action.ActorSeat, action.EffectiveKind, ids, source))
            throw new InvalidOperationException("Guqu rejected an unpermitted true Use before its actual material payment.");
        var paid = supplied.AcceptedDeclarationPayment;
        IReadOnlyList<GuquUseClaim> claims;
        if (paid?.GuquPermit is not null)
        {
            if (!ValidMouGuquDeclaration(paid) || paid.GuquDeclarationClaims is null)
                throw new InvalidOperationException("Guqu lost its original prepaid declaration permission.");
            claims = TransferMouGuquDeclarationClaims(paid, supplied.Id, action.ActionId);
        }
        else claims = CaptureMouGuquClaims(supplied.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind, action.PhysicalCards);
        var admission = new GuquUseAdmission(supplied.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind,
            _turnNumber, _turnProgression.OwnerSeat, _movementSequence, ids.Length, paid?.GuquPermit?.DeclarationId);
        var current = LifecycleCardUse(supplied.Id)!;
        ReplaceRuntimeFrame(supplied.Id, current with { GuquAdmission = admission,
            GuquClaims = (current.GuquClaims ?? []).Concat(claims).ToArray() });
        AdvanceEventRulesAndQueueFact(new GuquUseAcceptedEvent(admission));
    }
    private void CaptureMouGuquResponseUse(ResolutionFrame parent, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (!HasMouGuquCapability || action.Type != CardActionType.Response ||
            !IsCompletedResponseUse(action, continuation == ProgramCardContinuation.NullificationResponse
                ? ProgramCompletedResponseKind.Nullification : ProgramCompletedResponseKind.Dodge)) return;
        var actual = _resolutionStack.Single(f => f.Id == parent.Id);
        if (actual.GuquResponseAdmissions?.SingleOrDefault(a => a.ActionId == action.ActionId) is { } accepted)
        {
            if (accepted.UseId != actual.Id || accepted.ActorSeat != action.ActorSeat || accepted.ProviderSeat != action.ProviderSeat || accepted.EffectiveKind != action.EffectiveKind ||
                accepted.MaterialCount != action.PhysicalCards.Count || ProgramEventHistory<GuquUseAcceptedEvent>().Count(e => e.Admission == accepted) != 1)
                throw new InvalidOperationException("Guqu response admission changed its original native parent or paid action.");
            return;
        }
        var paid = actual.AcceptedDeclarationPayment;
        var declared = paid?.GuquPermit is { } permit && ValidMouGuquDeclaration(paid) &&
            permit.ActorSeat == action.ActorSeat && permit.ProviderSeat == action.ProviderSeat &&
            permit.OutputKind == action.EffectiveKind && action.PhysicalCards is [var cost] &&
            cost.CardId == permit.CardId && cost.From == permit.From && cost.CardKind == permit.PrintedKind &&
            action.ConversionChain.Contains(permit.Conversion);
        if (!declared && !IsMouGuquPaidResponseAllowed(action))
            throw new InvalidOperationException("Guqu cannot publish a true response Use with forbidden material or conversion.");
        if (actual.GuquClaims?.Any(q => q.ActionId == action.ActionId) == true)
            return; // A paid debt may resume the same original response action.
        var claims = declared ? TransferMouGuquDeclarationClaims(paid!, parent.Id, action.ActionId)
            : CaptureMouGuquClaims(parent.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind, action.PhysicalCards);
        var admission = new GuquUseAdmission(actual.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind,
            _turnNumber, _turnProgression.OwnerSeat, _movementSequence, action.PhysicalCards.Count, declared ? paid!.DeclarationId : null);
        ReplaceRuntimeFrame(parent.Id, actual with { GuquClaims = (actual.GuquClaims ?? []).Concat(claims).ToArray(),
            GuquResponseAdmissions = (actual.GuquResponseAdmissions ?? []).Append(admission).ToArray() });
        AdvanceEventRulesAndQueueFact(new GuquUseAcceptedEvent(admission));
    }
    private IReadOnlyList<GuquUseClaim> TransferMouGuquDeclarationClaims(DeclaredCardPayment paid, long parentId, long actionId)
    {
        if (!ValidMouGuquDeclaration(paid) || paid.GuquDeclarationClaims is null)
            throw new InvalidOperationException("Guqu lost its exact prepaid declaration claim.");
        var claims = paid.GuquDeclarationClaims.Select(q => q with { ParentFrameId = parentId, ActionId = actionId }).ToArray();
        foreach (var q in claims)
        {
            AdvanceEventRulesAndQueueFact(new GuquUseClaimedEvent(q));
            var originals = ProgramEventHistory<GuquUseMaterialClaimedEvent>().Where(e => e.ParentFrameId == paid.DeclarationId &&
                e.ActionId == 0 && e.Source == q.Source).OrderBy(e => e.Index).ToArray();
            foreach (var original in originals)
                AdvanceEventRulesAndQueueFact(original with { ParentFrameId = parentId, ActionId = actionId });
        }
        return Array.AsReadOnly(claims);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29022)]
    private void CaptureMouGuquZeroUse(IGameEvent payload)
    {
        if (HasMouGuquCapability && payload is CardActionAcceptedEvent { Action: { Type: CardActionType.Response } response })
        {
            if (response.EffectiveKind == CardKind.Nullification && ActiveNullificationWindow is { } counterspell &&
                IsCompletedResponseUse(response, ProgramCardContinuation.NullificationResponse))
                CaptureMouGuquResponseUse(counterspell, response, ProgramCardContinuation.NullificationResponse);
            else if (response.EffectiveKind == CardKind.Dodge && ActiveCardAttack is { } attack &&
                attack.EffectiveCardKind is { } incoming && IsSlashCard(incoming) && attack.TargetSeat == response.ActorSeat &&
                LifecycleCardUse(attack.ResolutionId) is { } parent && IsCompletedResponseUse(response, ProgramCardContinuation.Dodge))
                CaptureMouGuquResponseUse(parent, response, ProgramCardContinuation.Dodge);
        }
        if (HasMouGuquCapability && payload is CardUseDeclaredEvent e && LifecycleCardUse(e.ResolutionId) is
            { GuquAdmission: null, Action: { Type: CardActionType.Use } } use &&
            use.SourceSeat == e.SourceSeat && use.CardId == e.CardId && use.CardKind == e.CardKind)
            IssueMouGuquUse(use);
    }
    private GuquUseClaim? MouGuquCompletedClaim(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != MouGuquSkill || candidate.BindingId != MouGuquBinding || context.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            context.CardUse is not { } used || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
            GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCompleted || window.Action.ActionId != used.CardActionId ||
            window.Action.Type == CardActionType.Response && window.CompletedResponseReturn is null ||
            _resolutionStack.SingleOrDefault(f => f.Id == window.ParentFrameId) is not { GuquClaims: { } claims }) return null;
        return claims.SingleOrDefault(q => q.ParentFrameId == window.ParentFrameId && q.ActionId == used.CardActionId &&
            q.Source.OwnerSeat == candidate.OwnerSeat && q.Source.SkillId == candidate.SkillId && q.Source.BindingId == candidate.BindingId &&
            q.Source.SkillInstanceId == candidate.SkillInstanceId && q.GameplayHash == candidate.GameplayHash && ValidMouGuquClaim(q, window.Action));
    }
    private bool ValidMouGuquClaim(GuquUseClaim q, CardActionContext action)
    {
        if (q.ActionId != action.ActionId || q.QueMaterialCount < 1 || q.ProviderSeat != action.ProviderSeat ||
            !MouGuquActorLineageMatches(q.ParentFrameId, q.ActionId, q.ActorSeat, action.ActorSeat, q.ProviderSeat, action.Type) ||
            q.EffectiveKind != action.EffectiveKind && !(IsSlashCard(q.EffectiveKind) && IsSlashCard(action.EffectiveKind)) ||
            q.Source.SkillId != MouGuquSkill || q.Source.BindingId != MouGuquBinding || q.Source.OwnerSeat != q.TurnOwner ||
            q.ActualTurn < 1 || q.MovementSequence < 0 || q.MovementSequence > _movementSequence ||
            _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash != q.GameplayHash ||
            ProgramEventHistory<GuquUseClaimedEvent>().Count(e => e.Claim == q) != 1) return false;
        var materials = ProgramEventHistory<GuquUseMaterialClaimedEvent>().Where(e => e.ParentFrameId == q.ParentFrameId &&
            e.ActionId == q.ActionId && e.Source == q.Source).OrderBy(e => e.Index).ToArray();
        return materials.Length == q.QueMaterialCount && materials.Select(m => m.CardId).Distinct().Count() == materials.Length &&
            materials.Select((m, i) => m.Index == i && action.PhysicalCards.Any(c => c.CardId == m.CardId && c.CardKind == m.PrintedKind && c.From == m.From) &&
                ProgramEventHistory<GuquQueMarkedEvent>().Any(e => e.ActualTurn == q.ActualTurn && e.TurnOwner == q.TurnOwner &&
                    e.Source == q.Source && e.GameplayHash == q.GameplayHash && e.CardId == m.CardId && e.MovementSequence <= q.MovementSequence)).All(b => b);
    }
}
