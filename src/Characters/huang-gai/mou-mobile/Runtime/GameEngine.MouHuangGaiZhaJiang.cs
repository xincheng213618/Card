namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouZhaJiangSkill = "mou-mobile:zhajiang";
    private const string MouZhaJiangPolicy = "first-lost-hp-uses";
    private bool HasMouZhaJiangCapability => _contentRegistry.ProgramDependencies.HasCardPolicy(
        SkillProgramCardPolicyKind.OwnTurnFirstLostHpUses);

    private int MouZhaJiangProspectiveOrdinal(int actor)
    {
        if (!HasOngoingWholeActualUseTurn) return 0;
        var count = 0;
        var facts = WholeActualTurnUseHistory();
        var changes = EventsSinceLastBoundary(e => e is TurnStartedEvent or ActualTurnCursorReturnedEvent)
            .OfType<ProgramCardUseActorReplacedEvent>().GroupBy(e => e.CardUseFrameId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var seen = new HashSet<(long?, long?, int?)>();
        for (var i = 0; i < facts.Length; i++)
        {
            var f = facts[i];
            (long? ActionId, long? LegacyUseId, int? InstructionIndex) key = f.CardActionId is { } action
                ? (action, null, null) : (null, f.OriginFrameId, f.ProgramInstructionIndex);
            if (f.WholeActualTurnUseSequence != i + 1 || !seen.Add(key) ||
                f.RoundActionType is not (CardActionType.Use or CardActionType.Response) || f.RoundProviderSeat is not { } provider)
                throw new InvalidOperationException("ZhaJiang requires the original ordered actual-turn Use ledger.");
            var seat = f.ActorSeat;
            if (f.RoundActionType == CardActionType.Use && changes.TryGetValue(f.OriginFrameId, out var replacements))
                foreach (var changed in replacements)
                {
                    if (changed.PreviousActorSeat != seat || changed.OwnerSeat != seat || changed.ProviderSeat != provider)
                        throw new InvalidOperationException("ZhaJiang cannot borrow a replaced actor's use count.");
                    seat = changed.ActorSeat;
                }
            if (seat == actor) count = checked(count + 1);
        }
        // A declaration pays before it can publish the native Use. Its owning
        // payment reserves that admitted position through any cost children;
        // the actual ledger still records only genuinely accepted native uses.
        return checked(count + MouZhaJiangPendingDeclarations(actor, facts) + 1);
    }
    private int MouZhaJiangPendingDeclarations(int actor, CurrentTurnCardUseKindsRecordedEvent[] facts)
    {
        var payments = _resolutionStack.Select(f => f is CardDeclarationFrame d
                ? d.Succeeded != false && d.Stage != CardDeclarationStage.Cleaning ? d.Payment : null
                : f.AcceptedDeclarationPayment).OfType<DeclaredCardPayment>()
            .Where(p => p.ActorSeat == actor && p.MouZhaJiang is { } q && q.ActualTurn == _turnNumber &&
                q.TurnOwner == _turnProgression.OwnerSeat).GroupBy(p => p.DeclarationId);
        var pending = 0;
        foreach (var group in payments)
        {
            if (group.ToArray() is not [var p] || p.MouZhaJiang is not { } q || !ValidMouZhaJiangDeclaration(p, q))
                throw new InvalidOperationException("ZhaJiang's pending declaration must have one exact live payment owner.");
            var nativeUses = ProgramEventHistory<MouZhaJiangUseAdmittedEvent>()
                .Where(e => e.DeclarationId == p.DeclarationId).ToArray();
            var nativeResponses = ProgramEventHistory<MouZhaJiangResponseAdmittedEvent>()
                .Where(e => e.DeclarationId == p.DeclarationId).ToArray();
            if (nativeUses.Length + nativeResponses.Length > 1)
                throw new InvalidOperationException("A ZhaJiang declaration cannot admit more than one native true Use.");
            var recorded = nativeUses is [var use]
                ? facts.Count(f => f.RoundActionType == CardActionType.Use && f.OriginFrameId == use.UseId &&
                    f.CardActionId == (use.ActionId > 0 ? use.ActionId : null) &&
                    f.ProgramInstructionIndex == use.ProgramInstructionIndex)
                : nativeResponses is [var response]
                    ? facts.Count(f => f.RoundActionType == CardActionType.Response && f.OriginFrameId == response.OwnerFrameId &&
                        f.CardActionId == response.ActionId)
                    : 0;
            if (recorded > 1) throw new InvalidOperationException("A ZhaJiang declaration cannot occupy two actual-use ledger entries.");
            if (recorded == 0) pending = checked(pending + 1);
        }
        return pending;
    }
    private MouZhaJiangAdmission CaptureMouZhaJiangAdmission(CharacterState actor)
    {
        if (_resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration &&
            declaration.Return.ActorSeat == actor.Seat && BeifenDeclarationIsTrueUse(declaration.Return) &&
            declaration.Payment.MouZhaJiang is { } declared && ValidMouZhaJiangDeclaration(declaration.Payment, declared)) return declared;
        var current = _resolutionStack.LastOrDefault();
        if (current is ResponseWindowFrame response) current = _resolutionStack.SingleOrDefault(f => f.Id == response.ParentFrameId);
        if (current?.AcceptedDeclarationPayment is { Claimed: false, MouZhaJiang: { } paid } payment &&
            payment.ActorSeat == actor.Seat && ValidMouZhaJiangDeclaration(payment, paid)) return paid;
        var binding = CardPolicies(actor, SkillProgramCardPolicyKind.OwnTurnFirstLostHpUses).FirstOrDefault();
        var source = binding.Source is null ? null : new CardConversionSource(binding.Source.SkillId,
            binding.Policy.Id, actor.Seat, binding.Source.SkillInstanceId);
        var ordinal = MouZhaJiangProspectiveOrdinal(actor.Seat);
        var applies = source is not null && actor.IsAlive && actor.HasHpValue && HasOngoingWholeActualUseTurn &&
            actor.Seat == _turnProgression.OwnerSeat && ordinal > 0 && ordinal <= Math.Max(0, actor.MaxHp - actor.Hp);
        return new(source, binding.Source?.Program.GameplayHash, actor.Seat, _turnNumber,
            _turnProgression.OwnerSeat, actor.Hp, actor.MaxHp, ordinal, _movementSequence, applies);
    }
    private bool ValidMouZhaJiangAdmission(MouZhaJiangAdmission q) => IsValidPlayerSeat(q.ActorSeat) &&
        q.ActualTurn >= 0 && q.MovementSequence >= 0 && q.MovementSequence <= _movementSequence && q.UseOrdinal >= 0 &&
        (q.Source is null ? q.GameplayHash is null && !q.Applies :
            q.Source.SkillId == MouZhaJiangSkill && q.Source.BindingId == MouZhaJiangPolicy &&
            q.Source.OwnerSeat == q.ActorSeat && _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash == q.GameplayHash &&
            (!q.Applies || q.ActualTurn > 0 && q.TurnOwner == q.ActorSeat && q.UseOrdinal > 0 &&
                q.UseOrdinal <= Math.Max(0, q.MaxHp - q.Hp)));
    private bool HasMouZhaJiangPreUsePrivilege(CharacterState actor)
    {
        if (!HasMouZhaJiangCapability) return false;
        if (_resolutionStack.LastOrDefault() is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } d &&
            d.Return.ActorSeat == actor.Seat && BeifenDeclarationIsTrueUse(d.Return))
            return d.Payment.MouZhaJiang is { } q && ValidMouZhaJiangDeclaration(d.Payment, q) && q.Applies;
        var current = _resolutionStack.LastOrDefault();
        if (current is ResponseWindowFrame response) current = _resolutionStack.SingleOrDefault(f => f.Id == response.ParentFrameId);
        if (current?.AcceptedDeclarationPayment is { Claimed: false } paid && paid.ActorSeat == actor.Seat &&
            _cardZones.GetLocation(paid.Cost.CardId) == CardLocation.Processing)
            return paid.MouZhaJiang is { } q && ValidMouZhaJiangDeclaration(paid, q) && q.Applies;
        return CaptureMouZhaJiangAdmission(actor).Applies;
    }
    private bool HasMouZhaJiangUseDistance(CharacterState actor, long? useId = null) => useId is { } id
        ? HasIssuedMouZhaJiangUse(id, actor.Seat) : HasMouZhaJiangPreUsePrivilege(actor);
    private DeclaredCardPayment CaptureMouZhaJiangDeclaration(DeclaredCardPayment payment, CardDeclarationParentReturn parent)
    {
        if (!HasMouZhaJiangCapability || !BeifenDeclarationIsTrueUse(parent) || !IsValidPlayerSeat(parent.ActorSeat) ||
            payment.ActorSeat != parent.ActorSeat || payment.Claimed || payment.MouZhaJiang is not null ||
            _cardZones.GetLocation(payment.Cost.CardId) != payment.Cost.From) return payment;
        var q = CaptureMouZhaJiangAdmission(_players[parent.ActorSeat]);
        AdvanceEventRulesAndQueueFact(new MouZhaJiangDeclarationAdmittedEvent(payment.DeclarationId, payment.OwnerFrameId,
            payment.ProviderSeat, payment.Cost.CardId, payment.Cost.CardKind, payment.Cost.From, payment.DeclaredKind, q));
        return payment with { MouZhaJiang = q };
    }
    private bool ValidMouZhaJiangDeclaration(DeclaredCardPayment p, MouZhaJiangAdmission q) =>
        ValidMouZhaJiangAdmission(q) && p.MouZhaJiang == q && p.ActorSeat == q.ActorSeat &&
        _resolutionStack.SingleOrDefault(f => f.Id == p.OwnerFrameId) is { } owner &&
        (owner.AcceptedDeclarationPayment == p || owner is CardDeclarationFrame d && d.Payment == p) &&
        ProgramEventHistory<MouZhaJiangDeclarationAdmittedEvent>().Count(e => e == new MouZhaJiangDeclarationAdmittedEvent(
            p.DeclarationId, p.DeclarationId, p.ProviderSeat, p.Cost.CardId, p.Cost.CardKind, p.Cost.From, p.DeclaredKind, q)) == 1;
    private void IssueMouZhaJiangUse(CardUseFrame use)
    {
        if (!HasMouZhaJiangCapability || use.MouZhaJiangUse is not null ||
            use.Action is { Type: not CardActionType.Use } || !IsValidPlayerSeat(use.SourceSeat)) return;
        var a = use.Action;
        var paid = use.AcceptedDeclarationPayment;
        if (paid is null && a?.PhysicalCards is [var cost])
            paid = UnclaimedDeclarationPayment(a.ProviderSeat, cost.CardId);
        MouZhaJiangAdmission q;
        if (paid is not null)
        {
            if (paid.ActorSeat != use.SourceSeat || paid.MouZhaJiang is not { } frozen ||
                !ValidMouZhaJiangDeclaration(paid, frozen) || a?.PhysicalCards.Any(c => c.CardId == paid.Cost.CardId &&
                    c.CardKind == paid.Cost.CardKind && c.From == paid.Cost.From) != true)
                throw new InvalidOperationException("ZhaJiang lost its exact pre-paid declaration admission.");
            q = frozen;
        }
        else q = CaptureMouZhaJiangAdmission(_players[use.SourceSeat]);
        var r = new MouZhaJiangUseReceipt(use.Id, a?.ActionId ?? 0, use.CardKind, q, paid?.DeclarationId);
        ReplaceRuntimeFrame(use.Id, use with { MouZhaJiangUse = r });
        AdvanceEventRulesAndQueueFact(new MouZhaJiangUseAdmittedEvent(use.Id, r.ActionId, r.OriginalKind, q, r.DeclarationId));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 20)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 80)]
    private bool HasIssuedMouZhaJiangUse(long useId, int actorSeat)
    {
        if (_resolutionStack.SingleOrDefault(f => f.Id == useId) is not { MouZhaJiangUse: { } r } owner ||
            r.UseId != owner.Id || !r.Admission.Applies || r.Admission.ActorSeat != actorSeat ||
            !ValidMouZhaJiangAdmission(r.Admission) || ProgramEventHistory<MouZhaJiangUseAdmittedEvent>().Count(e => e ==
                new MouZhaJiangUseAdmittedEvent(useId, r.ActionId, r.OriginalKind, r.Admission, r.DeclarationId, r.ProgramInstructionIndex)) != 1) return false;
        if (owner is ProgramSkillFrame { CardAttack: { } attack } program)
            return r.ActionId == 0 && r.DeclarationId is null && r.OriginalKind == CardKind.Duel &&
                r.ProgramInstructionIndex == program.InstructionIndex && attack.ProgramUseInstructionIndex == r.ProgramInstructionIndex &&
                attack.CardUserSeat == actorSeat && attack.EffectiveCardKind == CardKind.Duel && attack.CardId is null;
        return owner is CardUseFrame use && r.ProgramInstructionIndex is null && use.SourceSeat == actorSeat &&
            (use.Action is { Type: CardActionType.Use } a ? a.ActionId == r.ActionId && a.ActorSeat == actorSeat :
                use.Action is null && r.ActionId == 0 && use.CardId == 0 && use.PhysicalCardIds is { Count: 0 }) &&
            (use.CardKind == r.OriginalKind || IsSlashCard(r.OriginalKind) && IsSlashCard(use.CardKind) && IsCurrentSlashFireChangedUse(use));
    }
    private bool IsMouZhaJiangUnrespondable(long useId) =>
        _resolutionStack.SingleOrDefault(f => f.Id == useId)?.MouZhaJiangUse?.Admission is { } q && HasIssuedMouZhaJiangUse(useId, q.ActorSeat);
    private void IssueMouZhaJiangLegacyDuel(ProgramSkillFrame owner, int actorSeat)
    {
        if (!HasMouZhaJiangCapability) return;
        var q = CaptureMouZhaJiangAdmission(_players[actorSeat]);
        var r = new MouZhaJiangUseReceipt(owner.Id, 0, CardKind.Duel, q, null, owner.InstructionIndex);
        var current = _resolutionStack.Single(f => f.Id == owner.Id);
        ReplaceRuntimeFrame(owner.Id, current with { MouZhaJiangUse = r });
        AdvanceEventRulesAndQueueFact(new MouZhaJiangUseAdmittedEvent(owner.Id, 0, CardKind.Duel, q, null, owner.InstructionIndex));
    }
    private void RecordMouZhaJiangAwareAlcoholDebit(long useId, CharacterState actor)
    {
        if (LifecycleCardUse(useId) is { MouZhaJiangUse: null } use && HasMouZhaJiangCapability)
            IssueMouZhaJiangUse(use);
        if (!HasIssuedMouZhaJiangUse(useId, actor.Seat) && !HasIssuedWuLuKangDixianUse(useId, actor.Seat) && !CardUsePayments.ExemptsAlcoholDebit(useId, actor.Seat)) actor.UsedPlayPhaseAlcoholThisTurn = true;
    }
    private ResolutionFrame? MouZhaJiangResponseOwner(int actor, CardKind kind)
    {
        if (kind == CardKind.Nullification && ActiveNullificationWindow is { } window) return window;
        return kind == CardKind.Dodge && ActiveCardAttack is { } attack && IsSlashCard(attack.EffectiveCardKind ?? CardKind.Dodge) &&
            attack.TargetSeat == actor && ActiveFactionDefense is null ? LifecycleCardUse(attack.ResolutionId) : null;
    }
    private void PrepareMouZhaJiangResponse(CharacterState actor, IReadOnlyList<int> ids, CardKind kind)
    {
        if (!HasMouZhaJiangCapability || MouZhaJiangResponseOwner(actor.Seat, kind) is not { } owner) return;
        var current = owner.MouZhaJiangResponses ?? [];
        if (current.LastOrDefault() is { ActionId: 0 } previous)
        {
            if (previous.ActorSeat != actor.Seat || previous.Kind != kind || !previous.Materials.Select(c => c.CardId).SequenceEqual(ids))
                throw new InvalidOperationException("ZhaJiang cannot replace an unpaid response admission with another actor or entity group.");
            return;
        }
        var materials = ids.Select(id => new CardActionCost(id, GetAttackCard(id).Kind,
            CapturedDeclarationOrigin(id, _cardZones.GetLocation(id)))).ToArray();
        var q = CaptureMouZhaJiangAdmission(actor);
        var declaration = owner.AcceptedDeclarationPayment is { Claimed: false } paid && paid.ActorSeat == actor.Seat &&
            materials is [var material] && material.CardId == paid.Cost.CardId && material.CardKind == paid.Cost.CardKind &&
            material.From == paid.Cost.From && paid.MouZhaJiang == q && ValidMouZhaJiangDeclaration(paid, q)
                ? paid.DeclarationId : (long?)null;
        var prep = new MouZhaJiangResponseReceipt(current.Count, actor.Seat, kind, q, DeclarationId: declaration) { Materials = materials };
        ReplaceRuntimeFrame(owner.Id, owner with { MouZhaJiangResponses = Array.AsReadOnly(current.Append(prep).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MouZhaJiangResponsePreparedEvent(owner.Id, prep.Index, actor.Seat, kind, q, materials.Length, declaration));
        for (var i = 0; i < materials.Length; i++) AdvanceEventRulesAndQueueFact(new MouZhaJiangResponseMaterialEvent(owner.Id,
            prep.Index, i, materials[i].CardId, materials[i].CardKind, materials[i].From));
    }
    private CardConversionSource? MouZhaJiangCounterspellSource(CharacterState actor, IReadOnlyList<int> ids)
    {
        PrepareMouZhaJiangResponse(actor, ids, CardKind.Nullification);
        return MouZhaJiangResponseOwner(actor.Seat, CardKind.Nullification)?.MouZhaJiangResponses?.LastOrDefault() is
            { ActionId: 0, Admission: { Applies: true } q } && ValidMouZhaJiangAdmission(q) ? q.Source : null;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 239)]
    private void ObserveMouZhaJiangAdmission(IGameEvent payload)
    {
        if (!HasMouZhaJiangCapability) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use &&
            (use.Action is not null || IsActualTurnLegacyVirtualUse(use))) { IssueMouZhaJiangUse(use); return; }
        if (payload is not CardActionAcceptedEvent { Action: { Type: CardActionType.Response } a } ||
            a.EffectiveKind is not (CardKind.Dodge or CardKind.Nullification) || a.ActorSeat != a.ProviderSeat ||
            a.ResponderSeat != a.ActorSeat || a.RequesterSeat is not null ||
            MouZhaJiangResponseOwner(a.ActorSeat, a.EffectiveKind) is not { } owner) return;
        var parentAction = owner is NullificationWindowFrame n ? NullificationParentActionId(n) : (owner as CardUseFrame)?.Action?.ActionId;
        if (a.ParentActionId != parentAction) return;
        if (owner.MouZhaJiangResponses?.Any(r => r.ActionId == a.ActionId) == true) return;
        if (owner.MouZhaJiangResponses?.LastOrDefault() is not { ActionId: 0 } && a.PhysicalCards.Count > 0)
            throw new InvalidOperationException("A material response must freeze ZhaJiang before its genuine payment, not after a cost child.");
        if (owner.MouZhaJiangResponses?.LastOrDefault() is not { ActionId: 0 })
            PrepareMouZhaJiangResponse(_players[a.ActorSeat], a.PhysicalCards.Select(c => c.CardId).ToArray(), a.EffectiveKind);
        owner = _resolutionStack.Single(f => f.Id == owner.Id);
        var rs = owner.MouZhaJiangResponses!.ToArray(); var r = rs[^1];
        if (r.ActorSeat != a.ActorSeat || r.Kind != a.EffectiveKind || !ValidMouZhaJiangAdmission(r.Admission) ||
            r.Materials.Count != a.PhysicalCards.Count || !r.Materials.Zip(a.PhysicalCards).All(p => p.First.CardId == p.Second.CardId &&
                p.First.CardKind == p.Second.CardKind && p.First.From == p.Second.From))
            throw new InvalidOperationException("ZhaJiang response admission changed its genuine actor, source or payment.");
        rs[^1] = r with { ActionId = a.ActionId };
        ReplaceRuntimeFrame(owner.Id, owner with { MouZhaJiangResponses = Array.AsReadOnly(rs) });
        AdvanceEventRulesAndQueueFact(new MouZhaJiangResponseAdmittedEvent(owner.Id, r.Index, a.ActionId, a.ActorSeat, a.EffectiveKind, r.Admission, r.DeclarationId));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 29050)]
    private void AssertMouZhaJiangAdmissions()
    {
        if (!HasMouZhaJiangCapability) return;
        foreach (var use in _resolutionStack.Where(u => u.MouZhaJiangUse is not null))
        {
            var r = use.MouZhaJiangUse!;
            if (!ValidMouZhaJiangAdmission(r.Admission) || r.UseId != use.Id ||
                ProgramEventHistory<MouZhaJiangUseAdmittedEvent>().Count(e => e == new MouZhaJiangUseAdmittedEvent(use.Id,
                    r.ActionId, r.OriginalKind, r.Admission, r.DeclarationId, r.ProgramInstructionIndex)) != 1)
                throw new InvalidOperationException("ZhaJiang lost the exact native Use admission frozen before its costs.");
        }
        foreach (var owner in _resolutionStack.Where(f => f.MouZhaJiangResponses is not null))
        foreach (var r in owner.MouZhaJiangResponses!)
        {
            if (!ValidMouZhaJiangAdmission(r.Admission) || r.ActorSeat != r.Admission.ActorSeat ||
                r.DeclarationId is { } declarationId && (r.Materials is not [var material] ||
                    ProgramEventHistory<MouZhaJiangDeclarationAdmittedEvent>().Count(e => e.DeclarationId == declarationId &&
                        e.OwnerFrameId == declarationId && e.ProviderSeat == r.ActorSeat && e.CardId == material.CardId &&
                        e.PrintedKind == material.CardKind && e.From == material.From && e.DeclaredKind == r.Kind && e.Admission == r.Admission) != 1) ||
                ProgramEventHistory<MouZhaJiangResponsePreparedEvent>().Count(e => e == new MouZhaJiangResponsePreparedEvent(
                    owner.Id, r.Index, r.ActorSeat, r.Kind, r.Admission, r.Materials.Count, r.DeclarationId)) != 1 ||
                r.Materials.Select((c, i) => ProgramEventHistory<MouZhaJiangResponseMaterialEvent>().Count(e => e ==
                    new MouZhaJiangResponseMaterialEvent(owner.Id, r.Index, i, c.CardId, c.CardKind, c.From)) == 1).Any(ok => !ok) ||
                r.ActionId > 0 && ProgramEventHistory<MouZhaJiangResponseAdmittedEvent>().Count(e => e ==
                    new MouZhaJiangResponseAdmittedEvent(owner.Id, r.Index, r.ActionId, r.ActorSeat, r.Kind, r.Admission, r.DeclarationId)) != 1)
                throw new InvalidOperationException("ZhaJiang lost its owning native response preparation and true-Use return.");
        }
    }
}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 20)]
    private bool MouZhaJiangSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        HasMouZhaJiangPreUsePrivilege(query.Actor);
}
