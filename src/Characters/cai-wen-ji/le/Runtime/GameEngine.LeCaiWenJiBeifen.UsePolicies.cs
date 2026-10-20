namespace CardGame.Core;

public sealed partial class GameEngine
{
    private BeifenUseQualification? CaptureBeifenUseQualification(CharacterState actor)
    {
        if (!HasLeCaiWenJiBeifenCapability || !_setupComplete || !actor.IsAlive) return null;
        var source = GetSkillBindingShard(actor).ProgramInstances.SelectMany(i => i.Program.Triggers
            .Where(IsBeifenLossTrigger).Select(t => new BeifenLossSource(
                new(i.SkillId, t.Id, actor.Seat, i.SkillInstanceId), i.Program.GameplayHash)))
            .OrderBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        if (source is null) return null;
        var hand = GetHand(actor);
        var marked = hand.Count(c => HasShuangjiaEntityMark(c.Id));
        return marked < hand.Count - marked ? new(source.Source, source.GameplayHash, actor.Seat,
            marked, hand.Count - marked, _movementSequence) : null;
    }

    private bool HasBeifenLiveUsePrivilege(CharacterState actor) => CaptureBeifenUseQualification(actor) is not null;

    // A validated declaration pays before re-entering the original native use
    // dispatcher. That exact continuation must query its pre-payment right,
    // including the absence of a right, rather than its now-changed Hand.
    private bool HasBeifenPreUsePrivilege(CharacterState actor)
    {
        if (_resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration &&
            declaration.Return.ActorSeat == actor.Seat && BeifenDeclarationIsTrueUse(declaration.Return))
            return declaration.Payment.Beifen is { } q && ValidBeifenDeclaration(declaration.Payment, q);
        var current = _resolutionStack.LastOrDefault();
        if (current is ResponseWindowFrame response)
            current = _resolutionStack.SingleOrDefault(f => f.Id == response.ParentFrameId);
        if (current?.AcceptedDeclarationPayment is { Claimed: false } accepted && accepted.ActorSeat == actor.Seat &&
            _cardZones.GetLocation(accepted.Cost.CardId) == CardLocation.Processing)
            return accepted.Beifen is { } paid && ValidBeifenDeclaration(accepted, paid);
        return HasBeifenLiveUsePrivilege(actor);
    }
    private bool HasBeifenUseDistance(CharacterState actor, long? existingUseFrameId = null) =>
        existingUseFrameId is { } id ? HasIssuedBeifenUse(id, actor.Seat) : HasBeifenPreUsePrivilege(actor);

    private static bool BeifenDeclarationIsTrueUse(CardDeclarationParentReturn parent) => parent.Purpose is
        CardDeclarationPurpose.Use or CardDeclarationPurpose.Dodge or CardDeclarationPurpose.Counterspell or
        CardDeclarationPurpose.Recovery or CardDeclarationPurpose.BorrowedSword or CardDeclarationPurpose.ProvidedSlash or
        CardDeclarationPurpose.BorrowedSwordProvidedSlash or CardDeclarationPurpose.QinglongProvidedSlash;

    private DeclaredCardPayment CaptureBeifenDeclarationPayment(DeclaredCardPayment payment,
        CardDeclarationParentReturn parent)
    {
        if (!BeifenDeclarationIsTrueUse(parent) || !IsValidPlayerSeat(parent.ActorSeat) ||
            payment.ActorSeat != parent.ActorSeat || payment.Claimed || payment.Beifen is not null ||
            _cardZones.GetLocation(payment.Cost.CardId) != payment.Cost.From ||
            CaptureBeifenUseQualification(_players[parent.ActorSeat]) is not { } q) return payment;
        if (ProgramEventHistory<BeifenDeclarationQualifiedEvent>().Any(e => e.DeclarationId == payment.DeclarationId))
            throw new InvalidOperationException("Beifen cannot qualify the same declaration twice.");
        AdvanceEventRulesAndQueueFact(new BeifenDeclarationQualifiedEvent(payment.DeclarationId, payment.OwnerFrameId,
            payment.ProviderSeat, payment.Cost.CardId, payment.Cost.From, payment.DeclaredKind, q));
        return payment with { Beifen = q };
    }

    private bool ValidBeifenQualification(BeifenUseQualification q) => q.Source.SkillId == "le:beifen" &&
        q.Source.BindingId == "lost-hujia-suits" && q.Source.OwnerSeat == q.ActorSeat && IsValidPlayerSeat(q.ActorSeat) &&
        q.MarkedHandCount >= 0 && q.OtherHandCount > q.MarkedHandCount && q.SequenceBefore <= _movementSequence &&
        _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash == q.GameplayHash;
    private bool ValidBeifenDeclaration(DeclaredCardPayment payment, BeifenUseQualification q) =>
        ValidBeifenQualification(q) && payment.ActorSeat == q.ActorSeat && payment.Beifen == q &&
        _resolutionStack.SingleOrDefault(f => f.Id == payment.OwnerFrameId) is { } owner &&
        (owner.AcceptedDeclarationPayment == payment || owner is CardDeclarationFrame declaration && declaration.Payment == payment) &&
        ProgramEventHistory<BeifenDeclarationQualifiedEvent>().Where(e => e.DeclarationId == payment.DeclarationId)
            .ToArray() is [var fact] && fact.OwnerFrameId == payment.DeclarationId &&
        fact.Qualification == q && fact.ProviderSeat == payment.ProviderSeat &&
        fact.CardId == payment.Cost.CardId && fact.From == payment.Cost.From && fact.DeclaredKind == payment.DeclaredKind;

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28420)]
    private void CaptureBeifenZeroMaterialNativeUse(IGameEvent payload)
    {
        if (HasLeCaiWenJiBeifenCapability && payload is CardUseDeclaredEvent declared &&
            LifecycleCardUse(declared.ResolutionId) is { BeifenUse: null, Action: { Type: CardActionType.Use } action } use &&
            use.CardId == 0 && declared.CardId == 0 &&
            (use.PhysicalCardIds is null or { Count: 0 }) && action.PhysicalCards.Count == 0 &&
            use.SourceSeat == declared.SourceSeat && use.CardKind == declared.CardKind)
            IssueBeifenUse(use);
    }

    private DeclaredCardPayment? BeifenOriginalDeclaration(CardUseFrame use)
    {
        if (use.Action is not { Type: CardActionType.Use } action) return null;
        if (use.AcceptedDeclarationPayment is { } own && own.ActorSeat == action.ActorSeat &&
            action.PhysicalCards.Any(c => c.CardId == own.Cost.CardId && c.CardKind == own.Cost.CardKind && c.From == own.Cost.From)) return own;
        // Only the actual pending claim for this native action is eligible; a
        // previously claimed payment elsewhere in history cannot lend rights.
        if (action.PhysicalCards is [var cost] &&
            UnclaimedDeclarationPayment(action.ProviderSeat, cost.CardId) is { } pending &&
            pending.ActorSeat == action.ActorSeat && pending.Cost.CardKind == cost.CardKind && pending.Cost.From == cost.From) return pending;
        return null;
    }

    private void IssueBeifenUse(CardUseFrame use)
    {
        if (!HasLeCaiWenJiBeifenCapability || use.Action is not { Type: CardActionType.Use } action) return;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || use.BeifenUse is not null)
            throw new InvalidOperationException("Beifen must bind the one accepted original native use mother.");
        var declaration = BeifenOriginalDeclaration(use);
        BeifenUseQualification? q;
        if (declaration is not null)
        {
            q = declaration.Beifen;
            if (q is not null && !ValidBeifenDeclaration(declaration, q))
                throw new InvalidOperationException("Beifen's declaration right lost its exact paid original entity.");
        }
        else q = CaptureBeifenUseQualification(_players[action.ActorSeat]);
        if (q is null) return;
        if (!ValidBeifenQualification(q) || q.ActorSeat != action.ActorSeat || use.SourceSeat != action.ActorSeat)
            throw new InvalidOperationException("Beifen cannot lend another actor's accepted use privilege.");
        var r = new BeifenUseReceipt(use.Id, action.ActionId, use.CardKind, q, declaration?.DeclarationId);
        ReplaceRuntimeFrame(use.Id, use with { BeifenUse = r });
        AdvanceEventRulesAndQueueFact(new BeifenUseAcceptedEvent(use.Id, action.ActionId, q.Source, q.GameplayHash,
            action.ActorSeat, q.MarkedHandCount, q.OtherHandCount, use.CardKind, q.SequenceBefore, r.DeclarationId));
    }

    private bool HasIssuedBeifenUse(long useId, int actorSeat)
    {
        if (LifecycleCardUse(useId) is not { BeifenUse: { } r, Action: { Type: CardActionType.Use } action } use ||
            r.UseId != use.Id || r.ActionId != action.ActionId || action.ActorSeat != actorSeat || use.SourceSeat != actorSeat ||
            r.Qualification.ActorSeat != actorSeat || !ValidBeifenQualification(r.Qualification) ||
            !(use.CardKind == r.OriginalKind || IsSlashCard(r.OriginalKind) && use.CardKind == CardKind.FireSlash &&
                IsCurrentSlashFireChangedUse(use)) ||
            ProgramEventHistory<BeifenUseAcceptedEvent>().Where(e => e.UseId == useId).ToArray() is not [var accepted]) return false;
        var q = r.Qualification;
        return accepted.ActionId == action.ActionId && accepted.Source == q.Source && accepted.GameplayHash == q.GameplayHash &&
            accepted.ActorSeat == actorSeat && accepted.MarkedHandCount == q.MarkedHandCount &&
            accepted.OtherHandCount == q.OtherHandCount && accepted.OriginalKind == r.OriginalKind &&
            accepted.SequenceBefore == q.SequenceBefore && accepted.DeclarationId == r.DeclarationId;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 28420)]
    private void AssertBeifenUses()
    {
        if (!HasLeCaiWenJiBeifenCapability) return;
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.BeifenUse is not null))
            if (!HasIssuedBeifenUse(use.Id, use.SourceSeat))
                throw new InvalidOperationException("Beifen's native use lost its exact accepted actor, source, ratio or action invoice.");
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 50)]
    private bool DistanceCardUseExemption50HasBeifenUseDistance(ProgramCardDistanceQuery q) =>
        (HasBeifenUseDistance(q.Actor, q.CardUseFrameId) || HasMouZhaJiangUseDistance(q.Actor, q.CardUseFrameId));


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 10)]
    private bool DistanceBorrowedPaidWeaponSlash10HasBeifenPreUsePrivilege(ProgramSlashDistanceQuery q) =>
        (HasBeifenPreUsePrivilege(q.Actor) || HasMouZhaJiangPreUsePrivilege(q.Actor));

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 10)]
    private bool BeifenSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        HasBeifenPreUsePrivilege(query.Actor);
}
