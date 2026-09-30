namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IPublicPileCollectionProgramHost
    {
        public SkillProgramStepOutcome ExecutePublicPileCollection(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.ExecutePublicPileCollection(effect, frame);
    }

    private IReadOnlyList<PromptChoice> PublicPileCollectionChoices(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var slots = _players.Where(player => player.IsAlive)
            .SelectMany(player => BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, player.Seat, effect.Zones))
            .ToArray();
        var choices = new List<PromptChoice>();
        void Add(IReadOnlyList<PromptChoice> selection)
        {
            var parameters = new Dictionary<string, string>
            {
                ["program-action"] = "public-pile-collection",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["slot-count"] = selection.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            for (var index = 0; index < selection.Count; index++)
            {
                foreach (var key in new[] { "card-owner-seat", "source-zone", "slot-index" })
                    parameters[$"{index}.{key}"] = selection[index].Parameters[key];
            }
            choices.Add(new(new ChoiceId("public-pile." + string.Join("+", selection.Select(item => item.Parameters["card-owner-seat"] + "." + item.Id.Value))),
                string.Join("；", selection.Select(item => _players[int.Parse(item.Parameters["card-owner-seat"])].Name + "：" + item.Description)),
                selection.SelectMany(item => item.Cards).ToArray(),
                selection.Select(item => int.Parse(item.Parameters["card-owner-seat"])).ToArray(), parameters));
        }
        for (var first = 0; first < slots.Length; first++)
        {
            Add([slots[first]]);
            if (effect.Amount < 2) continue;
            for (var second = first + 1; second < slots.Length; second++)
                if (slots[first].Parameters["card-owner-seat"] != slots[second].Parameters["card-owner-seat"])
                    Add([slots[first], slots[second]]);
        }
        return choices.AsReadOnly();
    }

    private SkillProgramStepOutcome ExecutePublicPileCollection(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        var choices = PublicPileCollectionChoices(frame, effect);
        if (choices.Count == 0) return SkillProgramStepOutcome.Continue;
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            "请选择至多两名角色的各一张手牌或装备牌，明置于自己的武将牌上。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 选择公开牌堆", skill.Description), Choices = choices
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolvePublicPileCollectionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Public pile collection lost its frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.CollectPublicPile || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            throw new InvalidOperationException("Public pile collection lost its enabled source.");
        selected = PublicPileCollectionChoices(frame, effect).SingleOrDefault(choice => choice.Id == selected.Id)
            ?? throw new InvalidOperationException("Public pile collection no longer matches published slots.");
        var count = int.Parse(selected.Parameters["slot-count"]);
        var destination = new CardLocation(effect.DestinationZone!.Value, frame.OwnerSeat);
        var moves = Enumerable.Range(0, count).Select(index =>
        {
            var seat = int.Parse(selected.Parameters[$"{index}.card-owner-seat"]);
            var zone = Enum.Parse<CardZoneKind>(selected.Parameters[$"{index}.source-zone"]);
            var from = new CardLocation(zone, seat);
            var card = _cardZones.CardsAt(from)[int.Parse(selected.Parameters[$"{index}.slot-index"])];
            return (Card: card, From: from, To: card.IsGeneralWeapon && zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : destination);
        }).ToArray();
        ClearPendingDecision();
        _resolutionStack[^1] = frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) };
        var batch = BeginCardMovementBatch(moves.Select(move => move.From), moves.Select(move => move.To));
        var records = new List<CardMovementRecord>();
        var committed = false;
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.public-pile-collection");
        try
        {
            foreach (var move in moves) _cardZones.Move(move.Card.Id, move.From, move.To);
            foreach (var move in moves)
            {
                records.Add(RecordMovement(move.Card, move.From, move.To, reason));
                ResolveEquipmentSkillGrant(move.Card, move.From, move.To);
                ClearJudgmentEffectiveKindAfterMove(move.Card, move.From, move.To);
                ResolveSilverLionRemoval(move.Card, move.From, reason);
                ResolveWoodenOxMove(move.Card, move.From, move.To);
            }
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, records, committed); }
        if (!TryBeginCardsMovedProgramWindow()) CompleteAwaitedProgramMovement(frame.Id);
    }
}
