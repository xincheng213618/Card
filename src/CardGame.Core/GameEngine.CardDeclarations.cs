namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DeclarationTurnUsage = "declaration";

    private DeclaredCardPayment? UnclaimedDeclarationPayment(int provider, int cardId) =>
        _resolutionStack.Select(frame => frame is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration
                ? declaration.Payment : frame.AcceptedDeclarationPayment)
            .LastOrDefault(payment => payment is { Claimed: false } && payment.ProviderSeat == provider &&
                payment.Cost.CardId == cardId && _cardZones.GetLocation(cardId) == CardLocation.Processing);

    private Card? AvailableDeclarationCard(CharacterState owner) =>
        _resolutionStack.Select(frame => frame.AcceptedDeclarationPayment)
            .Concat(_resolutionStack.OfType<CardDeclarationFrame>().Where(frame => frame.Stage == CardDeclarationStage.Applying).Select(frame => frame.Payment))
            .LastOrDefault(payment => payment is { Claimed: false } && payment.ProviderSeat == owner.Seat) is { } receipt
            ? _cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(card => card.Id == receipt.Cost.CardId) : null;

    private DeclaredCardPayment? TransferDeclarationToCardUse(long useId)
    {
        if (_resolutionStack.LastOrDefault() is not CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration) return null;
        PopResolutionFrame(declaration.Id, ResolutionFrameKind.CardDeclaration);
        return declaration.Payment with { OwnerFrameId = useId };
    }

    private bool ClaimDeclarationPayment(Card card, CardLocation from, CardLocation to)
    {
        if (from.OwnerSeat is not { } provider || to != CardLocation.Processing ||
            UnclaimedDeclarationPayment(provider, card.Id) is not { } payment || payment.Cost.From != from) return false;
        var parent = _resolutionStack.Single(frame => frame.Id == payment.OwnerFrameId);
        if (parent.AcceptedDeclarationPayment != payment) throw new InvalidOperationException("A declaration payment lost its actual continuation owner.");
        ReplaceRuntimeFrame(parent.Id, parent with { AcceptedDeclarationPayment = payment with { Claimed = true } });
        return true;
    }

    private CardLocation CapturedDeclarationOrigin(int cardId, CardLocation actual) =>
        _resolutionStack.Select(frame => frame is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration
                ? declaration.Payment : frame.AcceptedDeclarationPayment)
            .LastOrDefault(payment => payment is { Claimed: false } && payment.Cost.CardId == cardId)?.Cost.From ?? actual;

    // Only the exact committed cost entity is hidden. Other same-name cards
    // can become public during nested effects and keep their own description.
    private string PublicDeclarationDescription(Card card, string ordinaryDescription)
    {
        var paid = _resolutionStack.Select(frame => frame is CardDeclarationFrame declaration
                ? declaration.Payment : frame.AcceptedDeclarationPayment)
            .LastOrDefault(payment => payment is { IsRevealed: false } && payment.Cost.CardId == card.Id);
        return paid is not null && _cardZones.GetLocation(card.Id) == CardLocation.Processing
            ? "声明【" + CardCatalog.Get(paid.DeclaredKind).DisplayName + "】" : ordinaryDescription;
    }

    private void AddDeclarationSlashActions(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable)
    {
        actions.RemoveAll(action => action.ConversionSource is { } source && ViewAsRule(source)?.DeclarationValidation is not null &&
            (action.Kind == LegalActionKind.Recast || IsSlashCard(action.PlayedCardKind ?? CardKind.Dodge)));
        foreach (var card in playable.Where(card => !IsTurnHandCardRestricted(actor, card)))
        foreach (var kind in SlashKinds)
        foreach (var source in GetProgramViewAsConversions(actor, card, kind, false).Where(source => ViewAsRule(source)?.DeclarationValidation is not null))
        {
            var targets = GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: kind);
            foreach (var target in targets)
                actions.Add(new LegalAction(LegalActionKind.Slash, card.Id, target.Seat,
                    DescribeConversion(source, "声明【" + CardCatalog.Get(kind).DisplayName + "】"), PlayedCardKind: kind) { ConversionSource = source });
            AddFangtianHalberdSlashActions(actions, actor, card, targets, CardCatalog.Get(kind).DisplayName, kind, source);
            AddProgramTargetCountSlashActions(actions, actor, card, targets, CardCatalog.Get(kind).DisplayName, kind, source);
        }
    }

    private DeclaredCardPayment? LiveDeclarationPayment(int cardId) =>
        _resolutionStack.Select(frame => frame is CardDeclarationFrame declaration ? declaration.Payment : frame.AcceptedDeclarationPayment)
            .LastOrDefault(payment => payment?.Cost.CardId == cardId && _cardZones.GetLocation(cardId) == CardLocation.Processing);

    private bool CanDeclareCard(CharacterState owner, string skillId) => _turnNumber > 0 &&
        _skillRuntimeState.GetUsage(owner.Seat, skillId, DeclarationTurnUsage, SkillUsageScope.Turn) == 0;

    private CardConversionSource? PeekDeclarationConversion(CharacterState owner, Card card, CardKind kind, bool use,
        CardConversionSource? explicitSource = null)
    {
        if (UnclaimedDeclarationPayment(owner.Seat, card.Id) is { } paid) return paid.Source;
        var hasPublishedSource = explicitSource is not null || _hasSelectedResponseConversionChoice || use && _hasSelectedUseConversionChoice;
        var selected = explicitSource ?? (_hasSelectedResponseConversionChoice ? _selectedResponseConversion : use ? _selectedUseConversion : _selectedResponseConversion);
        if (selected is not null) return ViewAsRule(selected)?.DeclarationValidation is not null ? selected : null;
        if (hasPublishedSource || MatchesRequiredCard(card.Kind, kind)) return null;
        return GetProgramViewAsConversions(owner, card, kind, !use, ActiveDying is not null)
            .FirstOrDefault(source => ViewAsRule(source)?.DeclarationValidation is not null);
    }

    private CardDeclarationParentReturn DeclarationReturn(CardDeclarationPurpose purpose, int actor,
        LegalAction? action = null, int? target = null, int recovery = 1,
        IReadOnlyList<ProgramRecoveryPolicySource>? policies = null, DyingResponseEvent? dying = null,
        bool fan = false, CardKind? finalKind = null)
    {
        var parent = _resolutionStack.LastOrDefault();
        return new(purpose, parent?.Id ?? 0, parent?.Kind, parent?.Step, actor, action, target, recovery,
            policies is null ? null : Array.AsReadOnly(policies.ToArray()), dying, fan, finalKind);
    }

    private bool TryBeginCardDeclaration(CharacterState provider, Card card, CardKind kind,
        CardConversionSource? source, CardDeclarationParentReturn parent, IReadOnlyList<int> targets)
    {
        if (source is null || ViewAsRule(source)?.DeclarationValidation is null ||
            UnclaimedDeclarationPayment(provider.Seat, card.Id) is not null) return false;
        if (_cardZones.GetLocation(card.Id) != CardLocation.Hand(provider.Seat) ||
            !CanDeclareCard(provider, source.SkillId)) throw new InvalidOperationException("A declaration requires a new legal hand payment in the actual turn.");
        var id = ++_resolutionSequence;
        var declared = ViewAsRule(source)!.OutputKind;
        var payment = new DeclaredCardPayment(id, id, provider.Seat,
            new(card.Id, card.Kind, CardLocation.Hand(provider.Seat), CapturePhysicalCardColor(provider.Seat, card)), source, declared, ActorSeat: parent.ActorSeat, FrozenSuit: EffectiveSuit(provider, card));
        ClearPendingDecision();
        _selectedUseConversion = null; _selectedResponseConversion = null;
        _hasSelectedUseConversionChoice = false; _hasSelectedResponseConversionChoice = false;
        PushRuntimeFrame(new CardDeclarationFrame(id, provider.Seat, _turnNumber, payment, parent,
            Array.AsReadOnly(targets.ToArray())));
        if (!_skillRuntimeState.TryConsumeUsage(provider.Seat, source.SkillId, DeclarationTurnUsage, SkillUsageScope.Turn, 1))
            throw new InvalidOperationException("Declaration quota changed during acceptance.");
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(provider.Seat, source.SkillId, DeclarationTurnUsage, SkillUsageScope.Turn, 1));
        MoveCard(card, CardLocation.Hand(provider.Seat), CardLocation.Processing, new("conversion.declaration.pay"));
        AdvanceEventRulesAndQueueFact(new CardDeclarationCommittedEvent(id, provider.Seat, parent.ActorSeat, declared, Array.AsReadOnly(targets.ToArray())));
        AdvanceRuntimeFrame(id);
        return true;
    }

    private bool CanChallengeDeclaration(int seat, int owner) => seat != owner && _players[seat].IsAlive &&
        !GetSkillBindingShard(_players[seat]).ProgramInstances.Any(instance => instance.Program.CannotChallengeDeclarations);

    private void ContinueCardDeclaration(long id)
    {
        if (_resolutionStack.LastOrDefault() is not CardDeclarationFrame frame || frame.Id != id) return;
        if (frame.Stage == CardDeclarationStage.Paying)
        {
            if (TryBeginCardsMovedProgramWindow(id)) return;
            if (! _players[frame.OwnerSeat].IsAlive || _winner != Winner.None ||
                _cardZones.GetLocation(frame.Payment.Cost.CardId) != CardLocation.Processing)
            {
                ReplaceRuntimeTop(frame with { Stage = CardDeclarationStage.Returning, Succeeded = false });
                ContinueCardDeclaration(id); return;
            }
            var seats = Enumerable.Range(0, _playerCount).Select(offset => (_currentSeat + offset) % _playerCount)
                .Where(seat => CanChallengeDeclaration(seat, frame.OwnerSeat)).ToArray();
            ReplaceRuntimeTop(frame with { Stage = CardDeclarationStage.Challenging });
            var child = new CardDeclarationChallengeFrame(++_resolutionSequence, id, Array.AsReadOnly(seats));
            PushRuntimeFrame(child); ContinueCardDeclarationChallenge(child.Id); return;
        }
        if (frame.Stage == CardDeclarationStage.Granting)
        {
            if (TryBeginAdvancedSkillsChanged(id)) return;
            ReplaceRuntimeTop(frame with { Stage = CardDeclarationStage.Returning });
            ContinueCardDeclaration(id); return;
        }
        if (frame.Stage == CardDeclarationStage.Cleaning)
        {
            if (TryBeginCardsMovedProgramWindow(id)) return;
            ReplaceRuntimeTop(frame with { Stage = CardDeclarationStage.Returning });
            ContinueCardDeclaration(id); return;
        }
        if (frame.Stage == CardDeclarationStage.Returning) ReturnCardDeclaration(frame);
    }

    private void ContinueCardDeclarationChallenge(long id)
    {
        if (_resolutionStack.LastOrDefault() is not CardDeclarationChallengeFrame child || child.Id != id) return;
        var frame = _resolutionStack.OfType<CardDeclarationFrame>().Single(parent => parent.Id == child.ParentFrameId);
        while (child.SeatIndex < child.EligibleSeats.Count && !CanChallengeDeclaration(child.EligibleSeats[child.SeatIndex], frame.OwnerSeat))
        { child = child with { SeatIndex = child.SeatIndex + 1 }; ReplaceRuntimeTop(child); }
        if (child.SeatIndex == child.EligibleSeats.Count)
        {
            PopResolutionFrame(id, ResolutionFrameKind.CardDeclarationChallenge);
            ReplaceRuntimeTop(frame with { Succeeded = true, Stage = CardDeclarationStage.Returning });
            ContinueCardDeclaration(frame.Id); return;
        }
        var seat = child.EligibleSeats[child.SeatIndex];
        _pendingDecision = new(DecisionKind.RespondDodge, seat,
            $"{_players[frame.OwnerSeat].Name} 声明【{CardCatalog.Get(frame.Payment.DeclaredKind).DisplayName}】，是否质疑？", [], [], SourceSeat: frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            Choices = Array.AsReadOnly(new[] {
                new PromptChoice(new ChoiceId("declaration.pass"), "不质疑", [], [], new Dictionary<string,string>{{"declaration-answer","pass"}}),
                new PromptChoice(new ChoiceId("declaration.challenge"), "质疑并翻牌", [], [], new Dictionary<string,string>{{"declaration-answer","challenge"}}) })
        };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void AnswerCardDeclaration(PromptChoice choice)
    {
        var child = _resolutionStack.LastOrDefault() as CardDeclarationChallengeFrame ?? throw new InvalidOperationException("The declaration challenge has no owner.");
        if (_pendingDecision?.PlayerSeat != child.EligibleSeats[child.SeatIndex]) throw new InvalidOperationException("The challenge responder changed.");
        var frame = _resolutionStack.OfType<CardDeclarationFrame>().Single(parent => parent.Id == child.ParentFrameId);
        var seat = child.EligibleSeats[child.SeatIndex];
        ClearPendingDecision();
        if (choice.Parameters.GetValueOrDefault("declaration-answer") == "pass")
        { ReplaceRuntimeTop(child with { SeatIndex = child.SeatIndex + 1 }); ContinueCardDeclarationChallenge(child.Id); return; }
        if (choice.Parameters.GetValueOrDefault("declaration-answer") != "challenge" || !CanChallengeDeclaration(seat, frame.OwnerSeat))
            throw new InvalidOperationException("The challenge is no longer legal.");
        PopResolutionFrame(child.Id, ResolutionFrameKind.CardDeclarationChallenge);
        var success = frame.Payment.Cost.CardKind == frame.Payment.DeclaredKind;
        frame = frame with { Payment = frame.Payment with { IsRevealed = true }, IsRevealed = true, ChallengerSeat = seat, Succeeded = success,
            Stage = success ? CardDeclarationStage.Granting : CardDeclarationStage.Cleaning };
        ReplaceRuntimeTop(frame);
        var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == frame.Payment.Cost.CardId);
        AdvanceEventRulesAndQueueFact(new CardDeclarationRevealedEvent(frame.Id, seat, ToSnapshot(card), success));
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        if (success)
            AcquireRuntimeSkills(_players[seat], $"declaration:{frame.Payment.Source.SkillId}:{frame.Payment.Source.SkillInstanceId}",
                [ViewAsRule(frame.Payment.Source)!.DeclarationValidation!.ChallengeGrantSkillId]);
        else MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, new("conversion.declaration.failed"));
        ContinueCardDeclaration(frame.Id);
    }

    private IReadOnlyList<CardDeclarationSnapshot>? GetCardDeclarationSnapshots(int viewer, bool revealAll)
    {
        var values = _resolutionStack.OfType<CardDeclarationFrame>().Select(frame =>
        {
            var cost = _cardZones.CardsAt(_cardZones.GetLocation(frame.Payment.Cost.CardId)).Single(card => card.Id == frame.Payment.Cost.CardId);
            return new CardDeclarationSnapshot(frame.Id, frame.OwnerSeat, frame.Return.ActorSeat, frame.Payment.DeclaredKind,
                Array.AsReadOnly(frame.TargetSeats.ToArray()), frame.IsRevealed, frame.IsRevealed ? ToSnapshot(cost) : null,
                revealAll || viewer == frame.OwnerSeat ? ToSnapshot(cost) : null);
        }).ToArray();
        return values.Length == 0 ? null : Array.AsReadOnly(values);
    }

    private bool AssertCardDeclarationInvariant()
    {
        var declarations = _resolutionStack.OfType<CardDeclarationFrame>().ToArray();
        foreach (var frame in _resolutionStack.Where(frame => frame.AcceptedDeclarationPayment is not null))
        {
            var paid = frame.AcceptedDeclarationPayment!;
            if (paid.OwnerFrameId != frame.Id || paid.Cost.From != CardLocation.Hand(paid.ProviderSeat) ||
                paid.Source.OwnerSeat != paid.ProviderSeat || paid.DeclarationId <= 0 ||
                !paid.Claimed && _cardZones.GetLocation(paid.Cost.CardId) != CardLocation.Processing)
                throw new InvalidOperationException("An accepted declaration payment lost its exact continuation owner or entity.");
        }
        if (declarations.Length == 0) return false;
        foreach (var frame in declarations)
        {
            if (frame.Payment.DeclarationId != frame.Id || frame.Payment.OwnerFrameId != frame.Id ||
                frame.Payment.ProviderSeat != frame.OwnerSeat || frame.Payment.Cost.From != CardLocation.Hand(frame.OwnerSeat) ||
                frame.Payment.Source.OwnerSeat != frame.OwnerSeat || frame.TurnNumber != _turnNumber ||
                _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.Payment.Source.SkillId, DeclarationTurnUsage, SkillUsageScope.Turn) != 1 ||
                frame.Return.ParentFrameId > 0 && !_resolutionStack.Any(parent => parent.Id == frame.Return.ParentFrameId && parent.Kind == frame.Return.ParentKind && parent.Step == frame.Return.ParentStep) ||
                frame.Stage is not (CardDeclarationStage.Paying or CardDeclarationStage.Cleaning or CardDeclarationStage.Returning) && _cardZones.GetLocation(frame.Payment.Cost.CardId) != CardLocation.Processing)
                throw new InvalidOperationException("The declaration lost its frozen paid entity, quota, turn or typed parent stage.");
            if (frame.ActiveChildFrameId is { } childId && !_resolutionStack.OfType<CardsMovedTriggerWindowFrame>()
                .Any(child => child.Id == childId && child.ResumeDeclarationFrameId == frame.Id && child.Batch.ParentFrameId == frame.Id))
                throw new InvalidOperationException("The declaration movement receipt has no exact child.");
        }
        if (_resolutionStack.LastOrDefault() is CardDeclarationChallengeFrame challenge)
        {
            var frame = declarations.Single(parent => parent.Id == challenge.ParentFrameId);
            if (frame.Stage != CardDeclarationStage.Challenging || challenge.SeatIndex < 0 || challenge.SeatIndex >= challenge.EligibleSeats.Count ||
                challenge.EligibleSeats.Contains(frame.OwnerSeat) || challenge.EligibleSeats.Distinct().Count() != challenge.EligibleSeats.Count ||
                _pendingDecision is not { Kind: DecisionKind.RespondDodge } decision ||
                decision.PlayerSeat != challenge.EligibleSeats[challenge.SeatIndex] || decision.ValidCardIds.Count != 0 ||
                decision.Choices.Count != 2 || decision.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
                    choice.Parameters.GetValueOrDefault("declaration-answer") is not ("pass" or "challenge")))
                throw new InvalidOperationException("The declaration challenge lost its one-way private responder prompt.");
        }
        return true;
    }

    private void ReturnCardDeclaration(CardDeclarationFrame frame)
    {
        var parent = frame.Return;
        var parentExists = parent.ParentFrameId == 0 || _resolutionStack.Any(candidate => candidate.Id == parent.ParentFrameId && candidate.Kind == parent.ParentKind);
        var owner = _players[frame.OwnerSeat];
        var card = _cardZones.CardsAt(_cardZones.GetLocation(frame.Payment.Cost.CardId)).Single(card => card.Id == frame.Payment.Cost.CardId);
        var cancelled = !parentExists || !owner.IsAlive || _winner != Winner.None;
        var success = frame.Succeeded == true && !cancelled && _cardZones.GetLocation(card.Id) == CardLocation.Processing;
        if (!success && _cardZones.GetLocation(card.Id) == CardLocation.Processing)
        {
            ReplaceRuntimeTop(frame with { Stage = CardDeclarationStage.Cleaning, Succeeded = false });
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, new("conversion.declaration.cancelled"));
            ContinueCardDeclaration(frame.Id); return;
        }
        if (success && parent.Purpose == CardDeclarationPurpose.Use)
        {
            ReplaceRuntimeTop(frame with { Stage = CardDeclarationStage.Applying });
            ExecuteAction(owner, parent.UseAction!); return;
        }
        PopResolutionFrame(frame.Id, ResolutionFrameKind.CardDeclaration);
        if (cancelled) return;
        if (success)
        {
            var actualParent = _resolutionStack.LastOrDefault(candidate => candidate.Id == parent.ParentFrameId)!;
            if (actualParent is ResponseWindowFrame response)
                actualParent = _resolutionStack.Single(candidate => candidate.Id == response.ParentFrameId);
            ReplaceRuntimeFrame(actualParent.Id, actualParent with { AcceptedDeclarationPayment = frame.Payment with { OwnerFrameId = actualParent.Id } });
            _selectedResponseConversion = frame.Payment.Source; _hasSelectedResponseConversionChoice = true;
        }
        switch (parent.Purpose)
        {
            case CardDeclarationPurpose.Use: break;
            case CardDeclarationPurpose.Dodge:
                if (success) ResolveDodgeResponse(ActiveCardAttack!, owner, card);
                else { var attack = ActiveCardAttack!; if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response && response.ParentFrameId == attack.ResolutionId) PopResponseWindow(attack.ResolutionId); ClearPendingDecision(); if (!ApplyAttackDamage(attack)) CompleteAttack(attack); }
                break;
            case CardDeclarationPurpose.Duel: ResolveDuelResponse(ActiveDuel!, owner, success ? card : null); break;
            case CardDeclarationPurpose.Group: ResolveGroupResponse(ActiveGroupCard!, owner, success ? card : null); break;
            case CardDeclarationPurpose.Counterspell: ResolveNullificationChoice(ActiveNullificationWindow!, owner, success ? card : null, success ? frame.Payment.Source : null); break;
            case CardDeclarationPurpose.Recovery:
                if (success) ResolveRecoveryCard(owner, _players[parent.TargetSeat!.Value], card, CardCatalog.Get(frame.Payment.DeclaredKind).DisplayName,
                    frame.Payment.DeclaredKind, parent.RecoveryAmount, parent.RecoveryPolicies?.Select(policy => (policy.SkillId,policy.PolicyId)).ToArray(),
                    frame.Payment.Source, parent.DyingResponse);
                else if (parent.DyingResponse is { } dying) { CompleteDyingCardResponse(dying with { UsedPeach = false, PeachCardId = null, UsedAlcohol = false, AlcoholCardId = null }); }
                break;
            case CardDeclarationPurpose.FactionDefense:
                if (success) CompleteFactionDefenseResponse(ActiveFactionDefense!, owner, card, false);
                else { ActiveFactionDefense!.CandidateIndex++; AdvanceFactionDefenseCandidate(); }
                break;
            case CardDeclarationPurpose.FactionSlash:
                if (success) CompleteFactionSlashResponse(ActiveFactionCardRequest!, owner, card);
                else { ActiveFactionCardRequest!.CandidateIndex++; AdvanceFactionSlashCandidate(); }
                break;
            case CardDeclarationPurpose.ProvidedSlash:
                if (success) BeginProvidedFactionSlashSlash(ActiveFactionCardRequest!, owner, [card], parent.FinalEffectiveKind ?? frame.Payment.DeclaredKind, parent.UsesZhuqueFan, frame.Payment.Source);
                else { ActiveFactionCardRequest!.CandidateIndex++; AdvanceFactionSlashCandidate(); }
                break;
            case CardDeclarationPurpose.BorrowedSwordProvidedSlash:
                if (success) BeginBorrowedSwordFactionSlashSlash(ActiveFactionCardRequest!, owner, [card], parent.FinalEffectiveKind ?? frame.Payment.DeclaredKind, parent.UsesZhuqueFan, frame.Payment.Source);
                else { ActiveFactionCardRequest!.AwaitingProviders = true; ActiveFactionCardRequest!.CandidateIndex++; AdvanceFactionSlashCandidate(); }
                break;
            case CardDeclarationPurpose.QinglongProvidedSlash:
                if (success) BeginQinglongCrescentBladeFactionSlashSlash(ActiveFactionCardRequest!, owner, [card], parent.FinalEffectiveKind ?? frame.Payment.DeclaredKind, parent.UsesZhuqueFan, frame.Payment.Source);
                else { ActiveFactionCardRequest!.AwaitingProviders = true; ActiveFactionCardRequest!.CandidateIndex++; AdvanceFactionSlashCandidate(); }
                break;
            case CardDeclarationPurpose.BorrowedSword:
                if (success) ResolveBorrowedSwordSlashChoice(ActiveBorrowedSword!, card, parent.FinalEffectiveKind ?? frame.Payment.DeclaredKind);
                else CompleteBorrowedSwordWithoutSlash(ActiveBorrowedSword!, transferWeapon: true);
                break;
        }
    }
}
