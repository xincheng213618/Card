namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<PromptChoice> MouYishiFieldChoices(ProgramSkillFrame? f, int owner)
    {
        foreach (var source in _players.Where(p => p.IsAlive))
        foreach (var zone in new[] { CardZoneKind.Equipment, CardZoneKind.Judgment })
        foreach (var card in _cardZones.CardsAt(new(zone, source.Seat)))
        foreach (var target in _players.Where(p => p.IsAlive && p.Seat != source.Seat))
        {
            if (!(zone == CardZoneKind.Equipment ? !card.IsGeneralWeapon && CanEnterEquipmentSlot(target.Seat, card)
                : CanMoveProgramCardToCorrespondingZone(card, zone, target.Seat))) continue;
            if (f is null) { yield return new(new("mou-dong-zhao.field-probe"), "移动场上牌", [card.Id], [target.Seat], new Dictionary<string, string>()); yield break; }
            yield return MouDongZhaoChoice(f, "field", $"将{source.Name}的【{card.DisplayName}】移给{target.Name}", [card.Id], [target.Seat],
                new Dictionary<string, string> { ["source-zone"] = zone.ToString(), ["card-owner-seat"] = source.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
    }
    private int MouYishiPhaseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouDongZhaoYishiStartedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Phase == phase);
    [ProgramPhaseUsedProjection(33500)]
    private int MouYishiPublicPhaseUses(int owner, string skill, int previous) => skill == MouDongZhaoComposition.Yishi &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == owner ? MouYishiPhaseUses(owner, phase) : previous;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 33500, SkillProgramEffectOp.ResolveMouDongZhaoYishi)]
    private bool CanStartMouYishi(ProgramActivationLegalityContext c) => c.SkillId == MouDongZhaoComposition.Yishi &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat && MouYishiPhaseUses(c.OwnerSeat, phase) < 1 &&
        MouYishiFieldChoices(null, c.OwnerSeat).Any();
    private SkillProgramStepOutcome BeginMouYishi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouDongZhao is not null) { ContinueMouDongZhao(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouDongZhaoComposition.Yishi || f.ActivationId != "keep-suits-and-move" || f.TriggerId is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || MouYishiPhaseUses(f.OwnerSeat, phase) != 0 ||
            !MouYishiFieldChoices(null, f.OwnerSeat).Any()) throw new InvalidOperationException("Yishi needs one actual Play and a currently legal native field move.");
        var hand = GetHand(_players[f.OwnerSeat]).Select(c => new MouDongZhaoHandEntity(c.Id, c.Kind, GetProgramEffectiveSuit(_players[f.OwnerSeat], c))).ToArray();
        var receipt = new MouDongZhaoReceipt(MouDongZhaoSource(f), f.GameplayHash, SkillProgramEffectOp.ResolveMouDongZhaoYishi,
            MouDongZhaoStage.ChoosingKept, f.Id, _roundNumber, f.OwnerSeat, phase) { Hand = hand };
        ReplaceRuntimeTop(f = f with { MouDongZhao = receipt });
        AdvanceEventRulesAndQueueFact(new MouDongZhaoYishiStartedEvent(f.Id, receipt.Source, receipt.GameplayHash, phase, hand.Length));
        PublishMouDongZhao(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouYishiChoices(ProgramSkillFrame f)
    {
        var r = f.MouDongZhao!;
        if (r.Stage == MouDongZhaoStage.ChoosingMove) return Array.AsReadOnly(MouYishiFieldChoices(f, f.OwnerSeat).ToArray());
        if (r.Stage != MouDongZhaoStage.ChoosingKept) return [];
        var choices = new List<PromptChoice>();
        foreach (var card in r.Hand.Where(c => c.Suit != Suit.None && _cardZones.GetLocation(c.CardId) == CardLocation.Hand(f.OwnerSeat)))
        {
            if (!r.Selected.Contains(card.CardId) && r.Selected.Any(id => r.Hand.Single(c => c.CardId == id).Suit == card.Suit)) continue;
            choices.Add(MouDongZhaoChoice(f, "keep-toggle", (r.Selected.Contains(card.CardId) ? "取消保留" : "保留") + GetAdvancedCard(card.CardId).DisplayName, [card.CardId]));
        }
        if (r.Selected.Count == r.Hand.Where(c => c.Suit != Suit.None).Select(c => c.Suit).Distinct().Count() && r.Hand.Where(c => c.Suit != Suit.None).Select(c => c.Suit).Distinct().All(suit =>
            r.Selected.Count(id => r.Hand.Single(c => c.CardId == id).Suit == suit) == 1))
            choices.Add(MouDongZhaoChoice(f, "keep-confirm", "展示保留牌，将其余手牌置入弃牌堆"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void AnswerMouYishi(ProgramSkillFrame f, PromptChoice selected)
    {
        var r = f.MouDongZhao!; var verb = selected.Parameters["verb"];
        if (r.Stage == MouDongZhaoStage.ChoosingKept)
        {
            if (verb == "keep-toggle")
            {
                var id = selected.Cards.Single();
                ReplaceRuntimeTop(f = f with { MouDongZhao = r with { Selected = r.Selected.Contains(id) ? r.Selected.Where(c => c != id).ToArray() : r.Selected.Append(id).ToArray() } });
                PublishMouDongZhao(f); return;
            }
            if (verb != "keep-confirm" || r.Hand.Any(c => _cardZones.GetLocation(c.CardId) != CardLocation.Hand(f.OwnerSeat)))
                throw new InvalidOperationException("Yishi retains exactly one current owned Hand entity of every frozen suit.");
            if (r.Selected.Count > 0) AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id,
                Array.AsReadOnly(r.Selected.Select(id => ToSnapshot(GetAdvancedCard(id))).ToArray())));
if(TryBeginPublicHandRevealWindow(f.Id,"yishi-kept-hand")){return;}ContinueYishiAfterPublicReveal(GetActiveProgramFrame(f.Id));return;

        }
        if (r.Stage != MouDongZhaoStage.ChoosingMove || verb != "field") throw new InvalidOperationException("Yishi lost its field-move choice.");
        var moving = GetAdvancedCard(selected.Cards.Single()); var from = _cardZones.GetLocation(moving.Id);
        var destination = new CardLocation(from.Zone, selected.Targets.Single());
        var suit = GetProgramEffectiveSuit(_players[from.OwnerSeat!.Value], moving);
        ReplaceRuntimeTop(f = f with { MouDongZhao = r with { MovedCardId = moving.Id, MovedSuit = suit } });
        MutateMouDongZhao(f, MouDongZhaoStage.MoveChildren, () => MoveCard(moving, from, destination,
            new("skill-program.mou-ol:yishi.field"), movement =>
            {
                var parent = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(parent with { MouDongZhao = parent.MouDongZhao! with { FieldMoved = movement.To == destination && movement.From != movement.To } });
                AdvanceEventRulesAndQueueFact(new MouDongZhaoFieldMovedEvent(f.Id, moving.Id, suit, movement.From, movement.To, movement.Sequence));
            }, disclosure: CardMovementDisclosure.FaceUp));
    }
    private PromptChoice AiMouYishi(PendingDecision prompt, ProgramSkillFrame f)
    {
        if (f.MouDongZhao!.Stage == MouDongZhaoStage.ChoosingMove) return prompt.Choices.First();
        return prompt.Choices.FirstOrDefault(c => c.Parameters["verb"] == "keep-confirm") ?? prompt.Choices.First(c =>
            c.Parameters["verb"] == "keep-toggle" && !f.MouDongZhao.Selected.Contains(c.Cards.Single()));
    }
    private void GrantMouYishi(ProgramSkillFrame f)
    {
        var r = f.MouDongZhao!;
        if (!r.LostHand && r.FieldMoved && r.MovedSuit is { } suit && _players[f.OwnerSeat].IsAlive)
            AdvanceEventRulesAndQueueFact(new MouDongZhaoYishiGrantedEvent(f.Id, r.Source, r.GameplayHash, suit,
                ProgramEventHistory<TurnStartedEvent>().Count(e => e.ActorSeat == f.OwnerSeat), _movementSequence));
    }
 [PublicHandRevealReturn("yishi-kept-hand")]
 private void ContinueYishiAfterPublicReveal(ProgramSkillFrame f)
 {
        var r=f.MouDongZhao??throw new InvalidOperationException("Yishi lost its original Hand selection.");
            var remainder = r.Hand.Where(c => !r.Selected.Contains(c.CardId) && _cardZones.GetLocation(c.CardId)==CardLocation.Hand(f.OwnerSeat)).Select(c => GetAdvancedCard(c.CardId)).ToArray();
            ReplaceRuntimeTop(f = f with { MouDongZhao = r with { LostHand = remainder.Length > 0 } });
            MutateMouDongZhao(f, MouDongZhaoStage.DiscardChildren, () =>
            {
                if (remainder.Length > 0) MoveCards(remainder, CardLocation.Hand(f.OwnerSeat), CardLocation.DiscardPile,
                    new("skill-program.mou-ol:yishi.place-rest"), disclosure: CardMovementDisclosure.FaceUp);
            });
            return;
 }

}
