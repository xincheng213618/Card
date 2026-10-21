using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private PromptChoice GuanduChoice(ProgramSkillFrame f, string option, string text, IReadOnlyList<int>? cards = null,
        IReadOnlyList<int>? targets = null) => new(new($"guandu-general.{f.Id}.{f.GuanduGeneral!.Stage}.{option}.cards-{string.Join("-", cards ?? [])}.targets-{string.Join("-", targets ?? [])}"), text,
        cards ?? [], targets ?? [], new Dictionary<string, string>
        {
            ["program-action"] = "guandu-general", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option
        });
    private void PublishGuanduGeneralChoice(ProgramSkillFrame f, int chooser, IReadOnlyList<PromptChoice> choices)
    {
        if (choices.Count == 0) { FinishGuanduGeneral(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"【{skill.Name}】请选择。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description), Choices = choices
        };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private IReadOnlyList<PromptChoice> GuanduOwnCardChoices(ProgramSkillFrame f, int seat, string option, bool discard = false,
        bool allowSkip = true, bool nonEquipment = false, bool handOnly = false)
    {
        var cards = discard ? GuanduDiscardable(seat, seat) : handOnly ? GetHand(_players[seat]).ToArray() : GuanduOwnedCards(seat);
        var choices = cards.Where(c => !nonEquipment || !EquipmentCatalog.IsEquipment(c.Kind))
            .Select(c => GuanduChoice(f, option, $"{(discard ? "弃置" : "交出")}【{c.DisplayName}】", [c.Id], [seat])).ToList();
        if (allowSkip) choices.Add(GuanduChoice(f, "skip", "不执行"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private IReadOnlyList<PromptChoice> GuanduForeignGainChoices(ProgramSkillFrame f, int seat)
    {
        var choices = new List<PromptChoice>();
        if (GetHand(_players[seat]).Count != 0)
            choices.Add(GuanduChoice(f, "take-hand", $"随机获得 {_players[seat].Name} 的一张手牌", targets: [seat]));
        choices.AddRange(GuanduOwnedCards(seat, judgment: true).Where(c => _cardZones.GetLocation(c.Id).Zone != CardZoneKind.Hand)
            .Select(c => GuanduChoice(f, "take-public", $"获得 {_players[seat].Name} 的【{c.DisplayName}】", [c.Id], [seat])));
        return Array.AsReadOnly(choices.ToArray());
    }
    [ProgramAnswer("guandu-general")]
    private void ResolveGuanduGeneralChoice(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanduGeneral: { } r } f ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Activity choice lost its exact published prompt.");
        AssertGuanduGeneral(f);
        var option = choice.Parameters["option"];
        ClearPendingDecision();
        if (r.Stage == "shicai-cost")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            f = GuanduStage(f, "shicai-obtain"); GuanduDiscardOne(f, choice.Cards.Single(), f.OwnerSeat); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "jieliang-cost")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            f = GuanduStage(f, "jieliang-paid"); GuanduDiscardOne(f, choice.Cards.Single(), f.OwnerSeat); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "jieliang-claim")
        {
            if (option != "skip")
            {
                var id = choice.Cards.Single();
                if (!GuanduClaimableDiscards(r.TargetSeat).Any(e => e.CardId == id)) throw new InvalidOperationException("The original discard entity is no longer claimable.");
                f = GuanduStage(f, "finish"); MoveCard(GetAdvancedCard(id), CardLocation.DiscardPile, CardLocation.Hand(f.OwnerSeat), new("activity.guandu.jieliang.claim"));
            }
            else f = GuanduStage(f, "finish");
            RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "zezhu-take")
        {
            var seat = r.Seats![r.Index];
            var card = option == "take-hand" ? GetHand(_players[seat])[_random.Next(GetHand(_players[seat]).Count)] : GetAdvancedCard(choice.Cards.Single());
            if (_cardZones.GetLocation(card.Id).OwnerSeat != seat) throw new InvalidOperationException("The commander's selected card changed owner.");
            f = GuanduUpdate(f, r with { Stage = "zezhu-take-next", Index = r.Index + 1 });
            MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.Hand(f.OwnerSeat), new("activity.guandu.zezhu.take")); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "zezhu-give")
        {
            f = GuanduUpdate(f, r with { Stage = "zezhu-give-next", Index = r.Index + 1 });
            GuanduGiveOne(f, choice.Cards.Single(), f.OwnerSeat, r.Seats![r.Index]); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "sushou-give")
        {
            if (option == "skip") { GuanduStage(f, "finish"); RunGuanduGeneral(f.Id); return; }
            var target = choice.Targets.Single();
            if (!r.Seats!.Contains(target) || target == f.OwnerSeat || !GuanduAllies(f.OwnerSeat, target)) throw new InvalidOperationException("Sushou changed its frozen allied recipient.");
            f = GuanduUpdate(f, r with { Seats = FreezeGuanduSeats(r.Seats!.Where(s => s != target)) });
            GuanduGiveOne(f, choice.Cards.Single(), f.OwnerSeat, target); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "xiying-cost")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            f = GuanduUpdate(f, r with { Stage = "xiying-target", Seats = FreezeGuanduSeats(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => p.Seat)), Index = 0 });
            GuanduDiscardOne(f, choice.Cards.Single(), f.OwnerSeat); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "xiying-answer")
        {
            var target = r.Seats![r.Index];
            f = GuanduUpdate(f, r with { Stage = "xiying-target", Index = r.Index + 1 });
            if (option == "ban") IssueGuanduAllCardBan(f, target);
            else GuanduDiscardOne(f, choice.Cards.Single(), target);
            RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage is "beizhan-target" or "zhongjie-target")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            var target = choice.Targets.Single();
            f = GuanduUpdate(f, r with { TargetSeat = target, Stage = r.Stage == "beizhan-target" ? "beizhan-draw" : "zhongjie-grow" });
            RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage is "yuanlve-give" or "moushi-give")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            var target = choice.Targets.Single(); var card = choice.Cards.Single(); var before = _movementSequence;
            f = GuanduUpdate(f, r with { TargetSeat = target, CardId = card, MovementBefore = before,
                Stage = r.Stage == "yuanlve-give" ? "yuanlve-offer" : "moushi-arm" });
            GuanduGiveOne(f, card, f.OwnerSeat, target); f = GetActiveProgramFrame(f.Id);
            GuanduUpdate(f, f.GuanduGeneral! with { MovementAfter = _movementSequence }); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "choulue-target")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            f = GuanduUpdate(f, r with { TargetSeat = choice.Targets.Single(), Stage = "choulue-gift" }); RunGuanduGeneral(f.Id); return;
        }
        if (r.Stage == "choulue-gift")
        {
            if (option == "skip") { FinishGuanduGeneral(f, false); return; }
            var before = _movementSequence; var id = choice.Cards.Single();
            f = GuanduUpdate(f, r with { CardId = id, MovementBefore = before, Stage = "choulue-arm" });
            GuanduGiveOne(f, id, r.TargetSeat, f.OwnerSeat); f = GetActiveProgramFrame(f.Id);
            GuanduUpdate(f, f.GuanduGeneral! with { MovementAfter = _movementSequence }); RunGuanduGeneral(f.Id); return;
        }
        throw new InvalidOperationException("Activity choice does not belong to the current receipt stage.");
    }
    private void GuanduDiscardOne(ProgramSkillFrame f, int id, int owner)
    {
        if (!GuanduDiscardable(owner, owner).Any(c => c.Id == id)) throw new InvalidOperationException("A paid discard is no longer owned or legal.");
        var card = GetAdvancedCard(id); var location = _cardZones.GetLocation(id); var reason = new CardMoveReason("activity.guandu." + f.SkillId + ".discard");
        var invoice = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, owner, [id], reason, f.Id);
        MoveCard(card, location, CardLocation.DiscardPile, reason, disclosure: CardMovementDisclosure.FaceUp);
        CompleteNativeLogicalDiscardInvocation(invoice);
    }
    private void GuanduGiveOne(ProgramSkillFrame f, int id, int owner, int target)
    {
        if (!_players[owner].IsAlive || !_players[target].IsAlive || !GuanduOwnedCards(owner).Any(c => c.Id == id))
            throw new InvalidOperationException("A gift lost its exact living participants or physical card.");
        var from = _cardZones.GetLocation(id);
        if (from == CardLocation.Hand(target)) return;
        MoveCard(GetAdvancedCard(id), from, CardLocation.Hand(target), new("activity.guandu." + f.SkillId + ".gift"), NativeGiveCompletion(f.Id,owner,target,[id],_movementSequence,null), disclosure: from.Zone == CardZoneKind.Equipment ? CardMovementDisclosure.FaceUp : CardMovementDisclosure.FaceDown);
    }
}
