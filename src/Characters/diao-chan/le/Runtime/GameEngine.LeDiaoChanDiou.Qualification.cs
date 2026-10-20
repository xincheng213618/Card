namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DiouSkill = "le:diou", DiouBinding = "completed-tanban-use";
    private bool HasDiouCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDiouTanbanUse);
    private static CardConversionSource DiouSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_diao_chan_le_LeDiaoChanDiou_Qualification.DiouSource(f);

    private void CaptureDiouQualification(ResolutionFrame supplied, CardActionContext action)
    {
        if (!HasDiouCapability || ProgramEventHistory<DiouQualificationAcceptedEvent>().Any(e =>
            e.NativeParentId == supplied.Id && e.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new DiouQualificationAcceptedEvent(supplied.Id, action.ActionId,
            action.ActorSeat, action.ProviderSeat, action.EffectiveKind, action.PhysicalCards.Count));
        if (supplied.AcceptedDeclarationPayment is { DiouDeclarationClaims: { } prepaid } paid)
        {
            var transferred = new List<DiouUseClaim>();
            foreach (var original in prepaid)
            {
                if (original.NativeParentId != paid.DeclarationId || original.ActionId != 0 ||
                    original.ActorSeat != action.ActorSeat || original.ProviderSeat != action.ProviderSeat ||
                    action.PhysicalCards is not [var cost] || cost != paid.Cost ||
                    ProgramEventHistory<DiouUseClaimedEvent>().Count(e => e.Claim == original) != 1)
                    throw new InvalidOperationException("Diou lost its exact prepayment Tanban declaration claim.");
                var q = original with { NativeParentId = supplied.Id, ActionId = action.ActionId };
                transferred.Add(q); AdvanceEventRulesAndQueueFact(new DiouUseClaimedEvent(q));
                foreach (var m in ProgramEventHistory<DiouMaterialQualifiedEvent>().Where(e =>
                    e.NativeParentId == paid.DeclarationId && e.ActionId == 0 && e.Source == q.Source).ToArray())
                    AdvanceEventRulesAndQueueFact(m with { NativeParentId = supplied.Id, ActionId = action.ActionId });
            }
            var actual = _resolutionStack.Single(f => f.Id == supplied.Id);
            ReplaceRuntimeFrame(actual.Id, actual with { DiouClaims = (actual.DiouClaims ?? []).Concat(transferred).ToArray() });
            return;
        }
        if (action.PhysicalCards.Count == 0) return;
        var material = action.PhysicalCards.Select(c => (Cost: c, Marked: HasTanbanEntityMark(c.CardId))).ToArray();
        if (!material.Any(c => c.Marked)) return;
        var bindings = GetSkillBindingShard(_players[action.ActorSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCompleted)
            .Where(b => b.SkillId == DiouSkill && b.Trigger.Id == DiouBinding &&
                b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveDiouTanbanUse }]).ToArray();
        var claims = new List<DiouUseClaim>();
        foreach (var b in bindings)
        {
            var source = new CardConversionSource(b.SkillId, b.Trigger.Id, action.ActorSeat, b.SkillInstanceId);
            var q = new DiouUseClaim(source, b.Program.GameplayHash, supplied.Id, action.ActionId,
                action.ActorSeat, action.ProviderSeat, action.EffectiveKind, _turnNumber, _turnProgression.OwnerSeat,
                material.Length, material.Count(c => c.Marked), _movementSequence);
            claims.Add(q); AdvanceEventRulesAndQueueFact(new DiouUseClaimedEvent(q));
            for (var i = 0; i < material.Length; i++)
            {
                var c = material[i];
                AdvanceEventRulesAndQueueFact(new DiouMaterialQualifiedEvent(supplied.Id, action.ActionId,
                    source, i, c.Cost.CardId, c.Cost.CardKind, c.Cost.From, c.Marked));
            }
        }
        if (claims.Count > 0)
        {
            var parent = _resolutionStack.Single(f => f.Id == supplied.Id);
            ReplaceRuntimeFrame(parent.Id, parent with { DiouClaims = (parent.DiouClaims ?? []).Concat(claims).ToArray() });
        }
    }
    private void IssueDiouUse(CardUseFrame use)
    {
        if (!HasDiouCapability || use.Action is not { Type: CardActionType.Use } a) return;
        BindDiouNativeUse(use); CaptureDiouQualification(LifecycleCardUse(use.Id)!, a);
    }
    private DeclaredCardPayment CaptureDiouDeclaration(DeclaredCardPayment paid, CardDeclarationParentReturn parent)
    {
        if (!HasDiouCapability || !WeiwanDeclarationIsTrueUse(parent)) return paid;
        var claims = new List<DiouUseClaim>();
        if (HasTanbanEntityMark(paid.Cost.CardId))
        foreach (var b in GetSkillBindingShard(_players[parent.ActorSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCompleted)
            .Where(b => b.SkillId == DiouSkill && b.Trigger.Id == DiouBinding &&
                b.Trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveDiouTanbanUse }]))
        {
            var source = new CardConversionSource(b.SkillId, b.Trigger.Id, parent.ActorSeat, b.SkillInstanceId);
            var q = new DiouUseClaim(source, b.Program.GameplayHash, paid.DeclarationId, 0, parent.ActorSeat,
                paid.ProviderSeat, paid.DeclaredKind, _turnNumber, _turnProgression.OwnerSeat, 1, 1, _movementSequence);
            claims.Add(q); AdvanceEventRulesAndQueueFact(new DiouUseClaimedEvent(q));
            AdvanceEventRulesAndQueueFact(new DiouMaterialQualifiedEvent(paid.DeclarationId, 0, source, 0,
                paid.Cost.CardId, paid.Cost.CardKind, paid.Cost.From, true));
        }
        return paid with { DiouDeclarationClaims = claims };
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29042)]
    private void ObserveDiouNativeAcceptance(IGameEvent payload)
    {
        if (!HasDiouCapability) return;
        if (payload is CardUseDeclaredEvent e && LifecycleCardUse(e.ResolutionId) is { Action: { Type: CardActionType.Use } a } use)
        {
            BindDiouNativeUse(use);
            CaptureDiouQualification(LifecycleCardUse(use.Id)!, a);
        }
        else if (payload is CardActionAcceptedEvent { Action: { Type: CardActionType.Response } response })
        {
            if (response.EffectiveKind == CardKind.Nullification && ActiveNullificationWindow is { } counterspell &&
                IsCompletedResponseUse(response, ProgramCardContinuation.NullificationResponse))
                CaptureDiouQualification(counterspell, response);
            else if (response.EffectiveKind == CardKind.Dodge && ActiveCardAttack is { } attack &&
                attack.EffectiveCardKind is { } incoming && IsSlashCard(incoming) && attack.TargetSeat == response.ActorSeat &&
                LifecycleCardUse(attack.ResolutionId) is { } parent && IsCompletedResponseUse(response, ProgramCardContinuation.Dodge))
                CaptureDiouQualification(parent, response);
        }
    }
    private bool ValidDiouClaim(DiouUseClaim q, CardActionContext action)
    {
        if (q.Source.SkillId != DiouSkill || q.Source.BindingId != DiouBinding || q.Source.OwnerSeat != q.ActorSeat ||
            q.ActionId != action.ActionId || q.ProviderSeat != action.ProviderSeat || q.TanbanMaterialCount < 1 ||
            q.MaterialCount != action.PhysicalCards.Count || q.MovementSequence > _movementSequence ||
            _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash != q.GameplayHash ||
            ProgramEventHistory<DiouUseClaimedEvent>().Count(e => e.Claim == q) != 1 ||
            q.EffectiveKind != action.EffectiveKind && !(IsSlashCard(q.EffectiveKind) && IsSlashCard(action.EffectiveKind)) ||
            !MouGuquActorLineageMatches(q.NativeParentId, q.ActionId, q.ActorSeat, action.ActorSeat, q.ProviderSeat, action.Type)) return false;
        var materials = ProgramEventHistory<DiouMaterialQualifiedEvent>().Where(e =>
            e.NativeParentId == q.NativeParentId && e.ActionId == q.ActionId && e.Source == q.Source).OrderBy(e => e.Index).ToArray();
        return materials.Length == q.MaterialCount && materials.Count(m => m.Tanban) == q.TanbanMaterialCount &&
            materials.Select(m => m.CardId).Distinct().Count() == materials.Length &&
            materials.Select((m, i) => m.Index == i && action.PhysicalCards.Any(c => c.CardId == m.CardId &&
                c.CardKind == m.PrintedKind && c.From == m.From)).All(b => b);
    }
    private DiouUseClaim? CompletedDiouClaim(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (c.SkillId != DiouSkill || c.BindingId != DiouBinding || context.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            context.CardUse is not { } used || _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            GetCardActionWindow(w) != SkillProgramTriggerWindow.CardUseCompleted || w.Action.ActionId != used.CardActionId ||
            w.Action.Type == CardActionType.Response && w.CompletedResponseReturn is null ||
            _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId)?.DiouClaims is not { } claims) return null;
        return claims.SingleOrDefault(q => q.NativeParentId == w.ParentFrameId && q.ActionId == used.CardActionId &&
            q.Source.OwnerSeat == c.OwnerSeat && q.Source.SkillInstanceId == c.SkillInstanceId &&
            q.GameplayHash == c.GameplayHash && ValidDiouClaim(q, w.Action));
    }
    private bool IsIssuedDiouCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        _players[c.OwnerSeat].IsAlive && CompletedDiouClaim(c, context) is { } q &&
        !ProgramEventHistory<DiouStartedEvent>().Any(e => e.Claim.Source == q.Source && e.Claim.ActionId == q.ActionId);
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 24)]
    private void CollectIssuedDiouCandidates(CardActionContext action, SkillProgramTriggerWindow window,
        List<ProgramCardTriggerCandidate> result)
    {
        if (!HasDiouCapability || window != SkillProgramTriggerWindow.CardUseCompleted ||
            _resolutionStack.LastOrDefault() is not { DiouClaims: { } claims } parent) return;
        foreach (var q in claims.Where(q => q.NativeParentId == parent.Id && q.ActionId == action.ActionId &&
            _players[q.Source.OwnerSeat].IsAlive && ValidDiouClaim(q, action)))
        {
            if (result.Any(c => c.OwnerSeat == q.Source.OwnerSeat && c.SkillId == q.Source.SkillId &&
                c.TriggerId == q.Source.BindingId && c.SkillInstanceId == q.Source.SkillInstanceId)) continue;
            var t = _contentRegistry.GetSkill(q.Source.SkillId).Program!.Triggers.Single(t => t.Id == q.Source.BindingId);
            result.Add(new(q.Source.OwnerSeat, -1, q.Source.SkillId, q.Source.BindingId, q.GameplayHash,
                q.Source.SkillInstanceId, t.Priority, CreateCardActionProgramContext(action, window, 0,
                    q.Source.OwnerSeat, -1, CaptureProgramTriggerFacts(_players[q.Source.OwnerSeat], action))));
        }
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ObservesResponseUseCompletion, 2)]
    private bool HasIssuedDiouResponse(CardActionContext action) => HasDiouCapability && _resolutionStack.Any(f =>
        f.DiouClaims?.Any(q => q.NativeParentId == f.Id && q.ActionId == action.ActionId &&
            _players[q.Source.OwnerSeat].IsAlive && ValidDiouClaim(q, action)) == true);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29041)]
    private void ObserveDiouPublicHandReveals(IGameEvent payload)
    {
        if (!HasDiouCapability || !_setupComplete || _turnNumber <= 0) return;
        var (frame, ids) = payload switch
        {
            CardsRevealedEvent e => (e.ResolutionId, e.Cards.Select(c => c.Id).ToArray()),
            ProgramCardsRevealedEvent e => (e.FrameId, e.Cards.Select(c => c.Id).ToArray()),
            FireAttackCardRevealedEvent e => (e.ResolutionId, new[] { e.CardId }),
            ProgramHandComparisonCardRevealedEvent e => (e.FrameId, new[] { e.CardId }),
            ProgramHandComparisonSourceRevealedEvent e => (e.FrameId, new[] { e.CardId }),
            ProgramHandDraftRevealedEvent e => (e.FrameId, MiuyanDraftHandIds(e).ToArray()),
            ProgramQingliangRevealEvent e => (e.FrameId, MiuyanQingliangHandIds(e).ToArray()),
            ProgramJuanxiaTrickUsedEvent { TrickKind: nameof(CardKind.FireAttack), CardId: > 0 } e => (e.FrameId, MiuyanJuanxiaHandIds(e).ToArray()),
            _ => (0L, Array.Empty<int>())
        };
        foreach (var p in _players)
        foreach (var card in GetHand(p).Where(c => ids.Contains(c.Id)))
            if (!DiouWasShownThisTurn(card.Id))
                AdvanceEventRulesAndQueueFact(new DiouPublicHandShownEvent(_turnNumber, _turnProgression.OwnerSeat,
                    frame, p.Seat, card.Id, card.Kind, _movementSequence));
    }
    private bool DiouWasShownThisTurn(int id) => ProgramEventHistory<DiouPublicHandShownEvent>().Any(e =>
        e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat && e.CardId == id);
}
