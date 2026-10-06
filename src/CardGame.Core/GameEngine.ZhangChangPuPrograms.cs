namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record YanjiaoCandidate(int Id, int Rank, CardLocation Location,
        string DisplayName, string RankText);

    // 严教 reveal: the pending ShenJiao bonus widens the public reveal and is
    // consumed afterwards by the explicit changeParticipantMarker instruction.
    private void YanjiaoRevealProgramTopCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var bonus = Math.Min(4, _players[active.OwnerSeat].Markers.GetValueOrDefault(effect.Marker!.Value));
        RevealProgramTopCards(frame.Id, active.OwnerSeat, effect.Amount + bonus, null,
            effect.ResultBind!, effect.Visibility);
    }

    // 严教 split: the selected counterpart first picks the owner group (every
    // feasible option has a disjoint equal-rank-sum partner), then the equal
    // partner group for themselves; ungrouped cards stay in the leftover bind.
    private SkillProgramStepOutcome YanjiaoSplitRevealedCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (frame.CardSetBindings.Any(binding => binding.Name == effect.OwnerBind!))
            throw new InvalidOperationException("The Yanjiao split bound its groups twice.");
        var source = GetProgramCardSet(active, effect.SourceBind!);
        var cards = ReadYanjiaoBoundCards(source);
        var chooserSeat = active.SelectedTargetSeats is [var selected] ? selected :
            throw new InvalidOperationException("The Yanjiao split lost its distributing counterpart.");
        var totalMask = (1 << cards.Count) - 1;
        var feasible = cards.Count == 0 ? [] : Enumerable.Range(1, totalMask - 1)
            .Where(mask => YanjiaoHasEqualSumPartner(cards, mask, totalMask ^ mask))
            .ToArray();
        if (feasible.Length == 0 || !_players[chooserSeat].IsAlive)
        {
            BindYanjiaoSubset(active, effect, source, effect.OwnerBind!, [], chooserSeat);
            BindYanjiaoSubset(active, effect, source, effect.ChooserBind!, [], chooserSeat);
            BindYanjiaoSubset(active, effect, source, effect.LeftoverBind!,
                cards.Select(card => card.Id).ToArray(), chooserSeat);
            YanjiaoApplyLeftoverPenalty(active, cards.Count);
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.Continue;
        }
        PresentYanjiaoGroupPrompt(active, effect, source, cards, chooserSeat, "first", feasible);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveYanjiaoSplitChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Yanjiao split choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not
            { Op: SkillProgramEffectOp.YanjiaoSplitRevealedCards } effect ||
            selected.Parameters.GetValueOrDefault("source-bind") != effect.SourceBind)
            throw new InvalidOperationException("The Yanjiao split choice does not match the suspended instruction.");
        var source = GetProgramCardSet(frame, effect.SourceBind!);
        var cards = ReadYanjiaoBoundCards(source);
        var chooserSeat = frame.SelectedTargetSeats.Single();
        if (selected.Parameters.GetValueOrDefault("stage") == "first")
        {
            var ownerMask = YanjiaoMaskOf(cards, selected.Cards);
            if (ownerMask == 0 || !YanjiaoHasEqualSumPartner(cards, ownerMask, ((1 << cards.Count) - 1) ^ ownerMask))
                throw new InvalidOperationException("The chosen Yanjiao group has no equal-rank-sum partner.");
            ClearPendingDecision();
            BindYanjiaoSubset(frame, effect, source, effect.OwnerBind!, selected.Cards, chooserSeat);
            AdvanceEventRulesAndQueueFact(new ProgramCardSubsetSelectedEvent(frame.Id, frame.SkillId,
                GetProgramBindingId(frame), effect.SourceBind!, effect.OwnerBind!,
                Array.AsReadOnly(selected.Cards.ToArray()), YanjiaoRankSum(cards, ownerMask)));
            var secondMasks = YanjiaoEqualSumSubsets(cards, ownerMask, ((1 << cards.Count) - 1) ^ ownerMask).ToArray();
            if (secondMasks.Length == 0)
                throw new InvalidOperationException("The Yanjiao partner group vanished before its selection.");
            PresentYanjiaoGroupPrompt(frame, effect, source, cards, chooserSeat, "second", secondMasks);
            return;
        }
        var ownerBind = GetProgramCardSet(frame, effect.OwnerBind!);
        var ownerMask2 = YanjiaoMaskOf(cards, ownerBind.CardIds);
        var chooserMask = YanjiaoMaskOf(cards, selected.Cards);
        if (chooserMask == 0 || YanjiaoRankSum(cards, chooserMask) != YanjiaoRankSum(cards, ownerMask2) ||
            (chooserMask & ownerMask2) != 0)
            throw new InvalidOperationException("The Yanjiao partner group no longer matches the owner group.");
        ClearPendingDecision();
        BindYanjiaoSubset(frame, effect, source, effect.ChooserBind!, selected.Cards, chooserSeat);
        var leftoverIds = cards.Where((_, index) => ((chooserMask | ownerMask2) & (1 << index)) == 0)
            .Select(card => card.Id).ToArray();
        BindYanjiaoSubset(frame, effect, source, effect.LeftoverBind!, leftoverIds, chooserSeat);
        YanjiaoApplyLeftoverPenalty(frame, leftoverIds.Length);
        AdvanceEventRulesAndQueueFact(new ProgramCardSubsetSelectedEvent(frame.Id, frame.SkillId,
            GetProgramBindingId(frame), effect.SourceBind!, effect.ChooserBind!,
            Array.AsReadOnly(selected.Cards.ToArray()), YanjiaoRankSum(cards, chooserMask)));
        AdvanceRuntimeProgram(frame.Id);
    }

    // 省身: draw one, or two when the owner's hand is the fewest alive; then
    // arm the next Yanjiao reveal by one, or two when the owner's HP is the
    // fewest alive, capped at four.
    private void ShenShenDrawAndArmBonus(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId)) return;
        var alive = _players.Where(player => player.IsAlive).ToArray();
        var minimumHand = alive.Min(player => GetHand(player).Count);
        DrawCards(owner, GetHand(owner).Count == minimumHand ? 2 : 1, true,
            new($"skill-program.{active.SkillId}.shengshen-draw"));
        var minimumHp = alive.Min(player => Math.Max(0, player.Hp));
        var marker = effect.Marker!.Value;
        var current = owner.Markers.GetValueOrDefault(marker);
        var armed = Math.Min(4, current + (Math.Max(0, owner.Hp) == minimumHp ? 2 : 1));
        if (armed != current)
            MutateParticipantMarker(GetActiveProgramFrame(active.Id), active.OwnerSeat, marker, armed - current);
    }

    private List<YanjiaoCandidate> ReadYanjiaoBoundCards(ProgramSkillCardSetBinding source)
    {
        if (source.CardIds.Count != source.SourceLocations.Count)
            throw new InvalidOperationException("A program card-set binding lost its source locations.");
        return source.CardIds.Select((cardId, index) =>
        {
            var location = source.SourceLocations[index];
            if (_cardZones.GetLocation(cardId) != location)
                throw new InvalidOperationException("A bound card left its frozen source before the Yanjiao split.");
            var card = _cardZones.CardsAt(location).Single(current => current.Id == cardId);
            return new YanjiaoCandidate(card.Id, card.Rank, location, card.DisplayName, card.RankText);
        }).ToList();
    }

    private static int YanjiaoRankSum(List<YanjiaoCandidate> cards, int mask) =>
        Enumerable.Range(0, cards.Count).Where(index => (mask & (1 << index)) != 0)
            .Sum(index => cards[index].Rank);

    private static int YanjiaoMaskOf(List<YanjiaoCandidate> cards, IReadOnlyList<int> cardIds)
    {
        var mask = 0;
        foreach (var cardId in cardIds)
        {
            var index = cards.FindIndex(card => card.Id == cardId);
            if (index < 0 || (mask & (1 << index)) != 0) return 0;
            mask |= 1 << index;
        }
        return mask;
    }

    private static IEnumerable<int> YanjiaoEqualSumSubsets(List<YanjiaoCandidate> cards, int mask, int pool)
    {
        var target = YanjiaoRankSum(cards, mask);
        for (var subset = pool; subset > 0; subset = (subset - 1) & pool)
            if (YanjiaoRankSum(cards, subset) == target) yield return subset;
    }

    private static bool YanjiaoHasEqualSumPartner(List<YanjiaoCandidate> cards, int mask, int pool) =>
        YanjiaoEqualSumSubsets(cards, mask, pool).Any();

    // 严教 tail: more than one ungrouped card lowers this turn's hand limit.
    private void YanjiaoApplyLeftoverPenalty(ProgramSkillFrame frame, int leftoverCount)
    {
        if (leftoverCount <= 1) return;
        GrantProgramTurnRuleModifier(GetActiveProgramFrame(frame.Id), SkillRuleQuery.HandLimit,
            SkillRuleOperation.Add, -1, Array.Empty<CardKind>());
    }

    private void BindYanjiaoSubset(ProgramSkillFrame frame, SkillProgramEffect effect,
        ProgramSkillCardSetBinding source, string bind, IReadOnlyList<int> cardIds, int chooserSeat)
    {
        var indexById = source.CardIds.Select((cardId, index) => (cardId, index))
            .ToDictionary(item => item.cardId, item => item.index);
        var locations = cardIds.Select(cardId =>
            source.SourceLocations[indexById[cardId]]).ToArray();
        SetProgramCardSet(frame.Id, bind, cardIds, SkillProgramCardSetVisibility.Public, locations,
            selectionActorSeat: chooserSeat);
    }

    private void PresentYanjiaoGroupPrompt(ProgramSkillFrame frame, SkillProgramEffect effect,
        ProgramSkillCardSetBinding source, List<YanjiaoCandidate> cards, int chooserSeat,
        string stage, int[] masks)
    {
        var presentation = _contentRegistry!.GetSkill(frame.SkillId);
        var ownerName = _players[frame.OwnerSeat].Name;
        var choices = masks.Select(mask => new PromptChoice(
            new ChoiceId($"yanjiao-{stage}.frame-{frame.Id}.mask-{mask}"),
            stage == "first"
                ? $"将 {string.Join("、", YanjiaoCardTexts(cards, mask))} 分给 {ownerName}（点数和 {YanjiaoRankSum(cards, mask)}）。"
                : $"将 {string.Join("、", YanjiaoCardTexts(cards, mask))} 留给自己（点数和 {YanjiaoRankSum(cards, mask)}）。" +
                    $"剩余未分组的牌将置入弃牌堆。",
            YanjiaoCardIds(cards, mask), [],
            new Dictionary<string, string>
            {
                ["program-action"] = "yanjiao-split",
                ["stage"] = stage,
                ["source-bind"] = effect.SourceBind!,
                ["owner-bind"] = effect.OwnerBind!,
                ["chooser-bind"] = effect.ChooserBind!,
                ["leftover-bind"] = effect.LeftoverBind!,
                ["rank-sum"] = YanjiaoRankSum(cards, mask).ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, chooserSeat,
            $"【{presentation.Name}】请将亮出的牌分成点数之和相等的两组。",
            source.CardIds, [], frame.WindowContext?.SourceSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = chooserSeat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, presentation.Name,
                $"{presentation.Name} · {YanjiaoStageText(cards, stage, masks.Length)}",
                stage == "first"
                    ? $"从亮出的牌中选出非空的一组分给 {ownerName}；只有存在与其点数和相等的另一组时才可选择。"
                    : $"从剩余亮出的牌中选出与上一组点数和相等的一组留给自己；其余牌置入弃牌堆，若超过一张则 {ownerName} 本回合手牌上限-1。"),
            Choices = Array.AsReadOnly(choices)
        };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private static string YanjiaoStageText(List<YanjiaoCandidate> cards, string stage, int optionCount) =>
        stage == "first"
            ? $"选出第一组（{optionCount} 种可行分组）"
            : $"选出点数和相等的另一组（{optionCount} 种）";

    private static IEnumerable<string> YanjiaoCardTexts(List<YanjiaoCandidate> cards, int mask) =>
        Enumerable.Range(0, cards.Count).Where(index => (mask & (1 << index)) != 0)
            .Select(index => $"【{cards[index].DisplayName}】{cards[index].RankText}");

    private static int[] YanjiaoCardIds(List<YanjiaoCandidate> cards, int mask) =>
        Enumerable.Range(0, cards.Count).Where(index => (mask & (1 << index)) != 0)
            .Select(index => cards[index].Id).ToArray();

    private PromptChoice SelectAiYanjiaoSplit(PendingDecision decision, ProgramSkillFrame frame)
    {
        if (decision.Choices[0].Parameters.GetValueOrDefault("stage") == "first")
            return decision.Choices
                .OrderByDescending(choice => choice.Cards.Count)
                .ThenByDescending(choice => int.Parse(
                    choice.Parameters.GetValueOrDefault("rank-sum", "0"),
                    System.Globalization.CultureInfo.InvariantCulture))
                .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
        return decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();
    }

    private sealed partial class ProgramSkillHost : IZhangChangPuProgramHost
    {
        public void YanjiaoRevealTopCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YanjiaoRevealProgramTopCards(frame, effect);
        public SkillProgramStepOutcome YanjiaoSplitRevealedCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YanjiaoSplitRevealedCards(frame, effect);
        public void ShenShenDrawAndArmBonus(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ShenShenDrawAndArmBonus(frame, effect);
    }
}
