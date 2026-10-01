namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void PromptAdvancedSelection(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        var draft = frame.AdvancedSelection ?? throw new InvalidOperationException("Advanced choice lost its draft.");
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var choices = new List<PromptChoice>();
        var selectedCards = draft.Operation is SkillProgramEffectOp.SampleFactionSkills or SkillProgramEffectOp.ObtainDeckRankSum or SkillProgramEffectOp.InheritWeapon or SkillProgramEffectOp.BalanceHandAttackTricks
            ? draft.Selected.Select(value => GetAdvancedCard(int.Parse(value))).ToArray() : [];
        foreach (var value in draft.Candidates.Except(draft.Selected, StringComparer.Ordinal))
        {
            var eligible = draft.Operation switch
            {
                SkillProgramEffectOp.SampleFactionSkills => selectedCards.All(card => EffectiveSuit(_players[frame.OwnerSeat], card) != EffectiveSuit(_players[frame.OwnerSeat], GetAdvancedCard(int.Parse(value)))) && selectedCards.Length < 4,
                SkillProgramEffectOp.ObtainDeckRankSum => GetAdvancedCard(int.Parse(value)).Rank <= draft.RequiredRankSum - selectedCards.Sum(card => card.Rank) &&
                    RankSubsetSearch.CanComplete(draft.Candidates.Except(draft.Selected.Append(value)).Select(id => GetAdvancedCard(int.Parse(id)).Rank),
                        draft.RequiredRankSum - selectedCards.Sum(card => card.Rank) - GetAdvancedCard(int.Parse(value)).Rank),
                SkillProgramEffectOp.DamageAfterDeckShuffle => draft.Selected.Count < 3,
                SkillProgramEffectOp.BalanceHandAttackTricks => draft.Selected.Count < draft.RequiredRankSum,
                _ => true
            };
            if (!eligible) continue;
            var label = draft.Operation switch
            {
                SkillProgramEffectOp.ReplaceSkillsOnAwakening => _contentRegistry.GetSkill(value).Name,
                SkillProgramEffectOp.EquipSampledGenerals => DescribeGeneralWeaponChoice(value),
                SkillProgramEffectOp.DamageAfterDeckShuffle or SkillProgramEffectOp.DamageFarthestCharacter => _players[int.Parse(value)].Name,
                _ => $"{GetAdvancedCard(int.Parse(value)).DisplayName} {GetAdvancedCard(int.Parse(value)).Suit} {GetAdvancedCard(int.Parse(value)).RankText}"
            };
            var cards = draft.Operation is SkillProgramEffectOp.SampleFactionSkills or SkillProgramEffectOp.ObtainDeckRankSum or SkillProgramEffectOp.InheritWeapon
                ? new[] { int.Parse(value) } : Array.Empty<int>();
            var targets = draft.Operation == SkillProgramEffectOp.DamageFarthestCharacter ? new[] { int.Parse(value) } : Array.Empty<int>();
            choices.Add(new(new ChoiceId($"advanced.{frameId}.select.{value}"), label, cards, targets,
                new Dictionary<string, string> { ["program-action"] = "advanced-lifecycle", ["advanced-value"] = value, ["frame-id"] = frameId.ToString() }));
        }
        if (draft.Operation is not (SkillProgramEffectOp.DamageFarthestCharacter or SkillProgramEffectOp.InheritWeapon) && (draft.Operation == SkillProgramEffectOp.BalanceHandAttackTricks ? draft.Selected.Count == draft.RequiredRankSum :
            draft.Operation != SkillProgramEffectOp.ObtainDeckRankSum || selectedCards.Sum(card => card.Rank) == draft.RequiredRankSum))
            choices.Add(new(new ChoiceId($"advanced.{frameId}.finish"), "完成选择", [], [],
                new Dictionary<string, string> { ["program-action"] = "advanced-lifecycle", ["advanced-value"] = "finish", ["frame-id"] = frameId.ToString() }));
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, $"【{skill.Name}】请选择。", [], [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()),
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 选择", skill.Description)
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveAdvancedLifecycleChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Advanced choice lost its frame.");
        var draft = frame.AdvancedSelection ?? throw new InvalidOperationException("Advanced choice lost its draft.");
        if (choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString()) throw new InvalidOperationException("Advanced choice belongs to another frame.");
        var value = choice.Parameters.GetValueOrDefault("advanced-value") ?? throw new InvalidOperationException("Advanced choice has no value.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        ClearPendingDecision();
        if (value != "finish")
        {
            if (!draft.Candidates.Contains(value) || draft.Selected.Contains(value)) throw new InvalidOperationException("Advanced selection is unavailable.");
            draft = draft with { Selected = draft.Selected.Append(value).ToArray() };
            ReplaceRuntimeTop(frame = frame with { AdvancedSelection = draft });
            if (draft.Operation == SkillProgramEffectOp.DamageFarthestCharacter)
            {
                var target = int.Parse(value);
                if (!IsFarthestInRange(_players[frame.OwnerSeat], _players[target]) ||
                    !_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat, frame.SkillId, $"target:{target}", SkillUsageScope.Turn, 1))
                    throw new InvalidOperationException("The chosen farthest target is no longer available.");
                ReplaceRuntimeTop(frame = frame with { AdvancedSelection = null, SelectedTargetSeats = [target] });
                BeginProgramSkillDamage(frame, target, 1);
                return;
            }
            if (draft.Operation == SkillProgramEffectOp.InheritWeapon)
            {
                CommitWeaponInheritance(frame, int.Parse(value));
                ReplaceRuntimeTop(frame with { AdvancedSelection = null });
                AdvanceRuntimeProgram(frame.Id);
                return;
            }
            PromptAdvancedSelection(frame.Id);
            return;
        }
        ReplaceRuntimeTop(frame with { AdvancedSelection = null });
        switch (draft.Operation)
        {
            case SkillProgramEffectOp.BalanceHandAttackTricks:
                if (draft.Selected.Count != draft.RequiredRankSum) throw new InvalidOperationException("Hand category balancing requires the entire difference.");
                foreach (var id in draft.Selected.Select(int.Parse))
                    MoveCard(GetAdvancedCard(id), CardLocation.Hand(frame.OwnerSeat), CardLocation.DiscardPile, new("skill.hand-category-balance.discard"));
                DrawCards(_players[frame.OwnerSeat], 1, true);
                break;
            case SkillProgramEffectOp.SampleFactionSkills:
                foreach (var id in draft.Selected.Select(int.Parse))
                {
                    var card = GetAdvancedCard(id); var from = _cardZones.GetLocation(id);
                    if (from.OwnerSeat != frame.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
                        throw new InvalidOperationException("Sampled skill payment changed owner.");
                    MoveCard(card, from, CardLocation.DiscardPile, new("skill.faction-sampling.discard"));
                }
                var pool = _contentRegistry.Generals.Values.Where(general => general.FactionId == effect.ProviderFactionId)
                    .SelectMany(general => general.SkillIds).Distinct(StringComparer.Ordinal)
                    .Where(id => !EnabledContentSkillIds(_players[frame.OwnerSeat]).Contains(id, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
                _random.Shuffle(pool);
                var source = $"acquired:{frame.SkillId}:sample:{_turnNumber}";
                foreach (var id in pool.Take(draft.Selected.Count))
                {
                    var grantId = $"{source}:{id}";
                    _players[frame.OwnerSeat].SkillGrants.Grant(new(grantId, id, grantId, source));
                    RegisterTaggedConversionSkill(_players[frame.OwnerSeat], id);
                }
                break;
            case SkillProgramEffectOp.ReplaceSkillsOnAwakening:
                foreach (var id in draft.Selected)
                    foreach (var grant in _players[frame.OwnerSeat].SkillGrants.Grants.Where(grant => grant.SkillId == id).ToArray())
                        _players[frame.OwnerSeat].SkillGrants.RemoveGrant(grant.GrantId);
                AcquireRuntimeSkills(_players[frame.OwnerSeat], frame.SkillId, effect.SkillIds.Take(draft.Selected.Count).ToArray());
                break;
            case SkillProgramEffectOp.ObtainDeckRankSum:
                var cards = draft.Selected.Select(id => GetAdvancedCard(int.Parse(id))).ToArray();
                if (cards.Sum(card => card.Rank) != draft.RequiredRankSum || cards.Any(card => _cardZones.GetLocation(card.Id) != CardLocation.DrawPile))
                    throw new InvalidOperationException("Exact rank-sum search no longer matches its frozen deck cards.");
                ObtainAdvancedCards(frame, cards);
                break;
            case SkillProgramEffectOp.DamageAfterDeckShuffle:
                ReplaceRuntimeTop(frame with { AdvancedSelection = draft with { RequiredRankSum = -1 } });
                break;
            case SkillProgramEffectOp.EquipSampledGenerals:
                foreach (var id in draft.Selected) EquipGeneralWeapon(frame, _contentRegistry.Generals[id]);
                break;
            case SkillProgramEffectOp.InheritWeapon: break;
        }
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool TryContinueAdvancedDamage(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.AdvancedSelection is not { Operation: SkillProgramEffectOp.DamageAfterDeckShuffle, RequiredRankSum: -1 } draft)
            return false;
        var selected = draft.Selected.Select(int.Parse).Where(seat => _players[seat].IsAlive).ToArray();
        if (selected.Length == 0) { ReplaceRuntimeTop(frame with { AdvancedSelection = null }); return false; }
        var seat = selected[0];
        ReplaceRuntimeTop(frame = frame with { AdvancedSelection = draft with { Selected = selected.Skip(1).Select(value => value.ToString()).ToArray() } });
        var amount = Math.Max(1, GetHand(_players[seat]).Count(card => AdvancedEffectiveHandKind(_players[seat], card) == CardKind.Dodge));
        BeginProgramSkillDamage(frame, seat, amount, nature: DamageNature.Thunder);
        return true;
    }
}

