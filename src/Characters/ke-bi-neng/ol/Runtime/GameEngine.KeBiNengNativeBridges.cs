namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<LegalAction> BuildKeBiNengPingduanBasicActions(ProgramSkillFrame frame)
    {
        if (frame.KeBiNengPingduan is not { } receipt || !IsValidPlayerSeat(receipt.ActorSeat)) return [];
        var actor = _players[receipt.ActorSeat];
        if (!IsKeBiNengPingduanBasicQuery(actor)) return [];
        var result = new List<LegalAction>();
        foreach (var action in BuildLegalActions(actor, includeProgramActions: false))
        {
            if (action.Kind is LegalActionKind.Slash or LegalActionKind.Peach or LegalActionKind.Alcohol)
                result.Add(action);
        }
        foreach (var cards in GetZhangbaSlashPairs(actor))
        foreach (var kind in new[] { CardKind.Slash })
        foreach (var target in _players.Where(target => CanUseVirtualSlashTarget(actor, target, kind,
                     PhysicalGroupSuit(actor, cards), PhysicalGroupColor(actor, cards), ZhangbaSpecificSlashRank(actor, cards), cards.Select(c => c.Id).ToArray()) &&
                     !IsCardTargetProhibited(target, kind, PhysicalGroupSuit(actor, cards), PhysicalGroupColor(actor, cards)) &&
                     !HasBeneficiarySuitShield(actor.Seat, target.Seat, PhysicalGroupSuit(actor, cards))))
            result.Add(new LegalAction(LegalActionKind.UseEquipmentEffect, null, target.Seat,
                $"发动【丈八蛇矛】对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】", kind,
                TargetSeats: [target.Seat], MinCardCount: 2, MaxCardCount: 2, MinTargetCount: 1, MaxTargetCount: 1,
                EquipmentKind: CardKind.ZhangbaSerpentSpear)
            { SelectableCardIds = cards.Select(c => c.Id).ToArray(), SelectableTargetSeats = [target.Seat] });

        var selections = new List<ProgramMultiCardViewAsSelection>();
        foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Peach, CardKind.Alcohol })
            selections.AddRange(GetProgramMultiCardViewAsSelections(actor, kind, false));
        foreach (var instance in GetSkillBindingShard(actor).ProgramInstances)
        foreach (var rule in instance.Program.ViewAs.Where(rule => rule.VariableInputCount && rule.AllHandCardsOfSelectedSuit && rule.ForPlay &&
                     (IsSlashCard(rule.OutputKind) || rule.OutputKind is CardKind.Peach or CardKind.Alcohol)))
        {
            var source = new CardConversionSource(instance.SkillId, rule.Id, actor.Seat, instance.SkillInstanceId);
            var cards = rule.SourceZones.SelectMany(zone => _cardZones.CardsAt(new CardLocation(zone, actor.Seat)))
                .DistinctBy(c => c.Id).ToArray();
            // A whole-suit request is an independent equipment conversion.
            // Latent viewAs rules owned by a Play-only activation must retain
            // that activation's timing, costs and remaining program effects.
            IEnumerable<IReadOnlyList<Card>> combinations = source.SkillId == "equipment:xuanjian" &&
                HasRuntimeSkill(actor, "friend:gongli-xu-shu") && HasFriendTrioAlly(actor, "zhuge-liang")
                    ? cards.Select(card => (IReadOnlyList<Card>)new[] { card })
                    : cards.GroupBy(card => EffectiveSuit(actor, card)).Select(group => (IReadOnlyList<Card>)group.ToArray());
            foreach (var combination in combinations)
                if (FindProgramMultiCardViewAsSelection(actor, combination.Select(c => c.Id).ToArray(), rule.OutputKind, false, source) is { } selected)
                    selections.Add(selected);
        }
        foreach (var selection in selections.DistinctBy(s => (s.Source, s.OutputKind, Cards: string.Join(',', s.Cards.Select(c => c.Id).Order()))))
        {
            var kind = selection.OutputKind;
            var rule = ViewAsRule(selection.Source)!;
            var cards = selection.Cards;
            IReadOnlyList<int> targets = IsSlashCard(kind)
                ? _players.Where(target => CanSpendSlashUse(actor, target, IgnoresProgramSlashLimit(actor, selection.Source), kind,
                        physicalCardIds: cards.Select(c => c.Id).ToArray()) &&
                    CanUseVirtualSlashTarget(actor, target, kind, PhysicalGroupSuit(actor, cards), PhysicalGroupColor(actor, cards),
                        physicalCardIds: cards.Select(c => c.Id).ToArray(), ignoreDistance: rule.DistanceUnlimited || FriendXuanjianIgnoresDistance(actor, selection.Source)) &&
                    !IsCardTargetProhibited(target, kind, PhysicalGroupSuit(actor, cards), PhysicalGroupColor(actor, cards)) &&
                    !HasBeneficiarySuitShield(actor.Seat, target.Seat, PhysicalGroupSuit(actor, cards)))
                    .Select(t => t.Seat).ToArray()
                : kind == CardKind.Peach && IsRuleWounded(actor) && !HasSelfCardTargetProhibition(actor.Seat) &&
                    !IsCardTargetProhibited(actor, kind, PhysicalGroupSuit(actor, cards), PhysicalGroupColor(actor, cards)) &&
                    !HasBeneficiarySuitShield(actor.Seat, actor.Seat, PhysicalGroupSuit(actor, cards)) ? [actor.Seat]
                : kind == CardKind.Alcohol && CanUseRoundDistinctBasicAlcohol(actor, cards) ? [actor.Seat] : [];
            foreach (var target in targets)
                result.Add(new LegalAction(IsSlashCard(kind) ? LegalActionKind.Slash : kind == CardKind.Peach ? LegalActionKind.Peach : LegalActionKind.Alcohol,
                    cards[0].Id, kind == CardKind.Alcohol ? null : target, DescribeConversion(selection.Source,
                        $"将所选牌当【{CardCatalog.Get(kind).DisplayName}】使用"), kind,
                    TargetSeats: kind == CardKind.Alcohol ? [] : [target], MinCardCount: cards.Count, MaxCardCount: cards.Count)
                { ConversionSource = selection.Source, SelectableCardIds = cards.Select(c => c.Id).ToArray() });
        }
        return Array.AsReadOnly(result.Select(KeBiNengPingduanReceipt.FreezeAction).ToArray());
    }

    private void BeginKeBiNengPingduanBasicUse(ProgramSkillFrame frame, LegalAction selected)
    {
        if (frame.KeBiNengPingduan is not { Stage: KeBiNengPingduanStage.BasicIssuing } receipt ||
            !HasAcceptedKenshangKeAction(frame, selected) && !HasAcceptedWeijieKeAction(frame, selected) && !(HasAcceptedZonghuKeAction(frame, selected) || HasAcceptedHuanpeiKeAction(frame, selected)) && !BuildKeBiNengPingduanBasicActions(frame).Any(a => SameKeBiNengAction(a, selected)))
            throw new InvalidOperationException("Pingduan lost its exact currently legal basic-card selection.");
        var actor = _players[receipt.ActorSeat];
        var original = _pendingDecision;
        if (original is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } || original.PlayerSeat != actor.Seat ||
            original.SkillPrompt?.SkillId != frame.SkillId)
            throw new InvalidOperationException("Pingduan lost the exact recipient's original accepted private basic request.");
        ClearPendingDecision();
        if (selected.DrawFundedDistinctBasicUse is not null)
        {
            BeginDrawFundedDistinctBasic(actor, selected.ConversionSource!, selected.PlayedCardKind!.Value, selected.TargetSeats, original,
                new(DrawFundedDistinctBasicIntent.Play, frame.Id, frame.Id, null, frame.InstructionIndex, selected.TargetSeat));
            return;
        }
        if (selected.ChainedStateBasicUse is not null)
        {
            BeginChainedStateBasic(actor, selected.ConversionSource!, selected.PlayedCardKind!.Value, selected.TargetSeats, original,
                new(ChainedStateBasicIntent.Play, frame.Id, frame.Id, null, frame.InstructionIndex, selected.TargetSeat));
            return;
        }
        if (selected.Kind == LegalActionKind.UseEquipmentEffect && selected.EquipmentKind == CardKind.ZhangbaSerpentSpear)
        {
            var cards = selected.SelectableCardIds.Select(GetAttackCard).ToArray();
            ResolveSlashCore(actor, _players[selected.TargetSeats.Single()], cards[0], selected.PlayedCardKind!.Value, actor.Seat,
                physicalCards: cards);
            return;
        }
        if (selected.SelectableCardIds.Count > 0 && selected.ConversionSource is { } source &&
            (ViewAsRule(source)?.InputCount > 1 || ViewAsRule(source)?.VariableInputCount == true))
        {
            var kind = selected.PlayedCardKind!.Value;
            var selection = FindProgramMultiCardViewAsSelection(actor, selected.SelectableCardIds, kind, false, source) ??
                throw new InvalidOperationException("Pingduan multi-card conversion lost its exact native material set.");
            if (IsSlashCard(kind))
                ResolveSlashCore(actor, _players[selected.TargetSeats.Single()], selection.Cards[0], kind, actor.Seat,
                    physicalCards: selection.Cards, conversionSource: source);
            else if (kind == CardKind.Peach)
                ResolveRecoveryCard(actor, actor, selection.Cards[0], "桃", kind, 1 + (ViewAsRule(source)?.RecoveryBonus ?? 0),
                    conversionSource: source, physicalCards: selection.Cards);
            else
            {
                var id = BeginCardUse(selection.Cards[0], actor.Seat, [], kind,
                    physicalCardIds: selection.Cards.Select(c => c.Id).ToArray(), conversionSource: source);
                foreach (var card in selection.Cards) MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use);
                if (!HasIssuedRoundGainedBasicBonus(id, actor.Seat)) actor.UsedPlayPhaseAlcoholThisTurn = true;
                BeginSimpleCardUse(id, new(selection.Cards[0].Id, SimpleCardUseEffect.Alcohol));
            }
            return;
        }
        // Youlong owns a second, real slot-payment program and must retain
        // the accepted request's prompt identity while that program is issued.
        if (selected.TieredRoundZeroUse is not null && selected.ConversionSource is { } tieredSource && IsEquipmentSlotNamedSkill(tieredSource.SkillId))
        {
            _pendingDecision = original;
            // The payment frame owns this future use. Do not publish a global
            // selected conversion while its movement and dying children run.
            if (TryBeginEquipmentSlotNamedPlay(actor, selected)) return;
            ClearPendingDecision();
        }
        ExecuteAction(actor, selected);
    }

    private bool IsKeBiNengPingduanPaymentFrame(DrawFundedDistinctBasicFrame paid) =>
        paid.Payment.Intent == DrawFundedDistinctBasicIntent.Play &&
        IsKeBiNengPingduanPaymentParent(paid.Id, paid.Payment.ParentFrameId, paid.Payment.RequestFrameId,
            paid.Payment.Cursor, paid.Payment.Source, paid.Payment.EffectiveKind, paid.TargetSeats);
    private bool IsKeBiNengPingduanPaymentFrame(ChainedStateBasicFrame paid) =>
        paid.Payment.Intent == ChainedStateBasicIntent.Play &&
        IsKeBiNengPingduanPaymentParent(paid.Id, paid.Payment.ParentFrameId, paid.Payment.RequestFrameId,
            paid.Payment.Cursor, paid.Payment.Source, paid.Payment.EffectiveKind, paid.TargetSeats);
    private bool IsKeBiNengPingduanPaymentParent(long paymentId, long? parentId, long? requestId, int cursor,
        CardConversionSource source, CardKind kind, IReadOnlyList<int> targets)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == paymentId);
        return index > 0 && parentId == requestId && _resolutionStack[index - 1] is ProgramSkillFrame parent && parent.Id == parentId &&
            parent.InstructionIndex == cursor && parent.KeBiNengPingduan is { Stage: KeBiNengPingduanStage.BasicIssuing, SelectedBasicAction: { } action } receipt &&
            receipt.InstructionIndex == cursor && receipt.ActorSeat == source.OwnerSeat && receipt.ActualTurn == _turnNumber &&
            receipt.PhaseInstance == _cardUseDebitPhaseInstanceId && action.ConversionSource == source && action.PlayedCardKind == kind &&
            action.TargetSeats.SequenceEqual(targets) &&
            CompleteProgramEventHistory().OfType<KeBiNengPingduanStartedEvent>().Count(e => e.FrameId == parent.Id &&
                e.Source == receipt.Source && e.GameplayHash == receipt.GameplayHash && e.ActorSeat == receipt.ActorSeat) == 1;
    }
}
