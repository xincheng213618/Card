namespace CardGame.Core;

public sealed record ConfiguredConversionTierChangedEvent(int OwnerSeat, string StateId, int Tier) : IGameEvent;
public sealed record PhysicalCardNameDeclaredEvent(int OwnerSeat, int DeclarerSeat, string StateId,
    int CardId, CardKind OutputKind, int TurnNumber) : IGameEvent;

public sealed partial class GameEngine
{
    private sealed record CardDeclarationDraft(string StateId, string Bind, int CardId, int Tier,
        IReadOnlyList<int> NearestSeats, int? DeclarerSeat = null);
    private readonly Dictionary<(int Seat, string State), int> _configuredConversionTiers = [];
    private readonly Dictionary<(int Seat, string State), (int Turn, int Card, CardKind Kind)> _declaredConversionCards = [];
    private readonly Dictionary<long, CardDeclarationDraft> _cardDeclarationDrafts = [];

    private IReadOnlyDictionary<string, int>? GetConfiguredConversionTierSnapshot(CharacterState owner)
    {
        var result = _configuredConversionTiers.Where(item => item.Key.Seat == owner.Seat).ToDictionary(item => item.Key.State, item => item.Value, StringComparer.Ordinal);
        return result.Count == 0 ? null : result;
    }

    private bool IsConfiguredConversionAvailable(CharacterState owner, Card card,
        IndexedSkillProgramInstance instance, SkillProgramViewAs rule, bool dyingUse)
    {
        if (rule.ExcludeOwnerEffects &&
            (dyingUse ? ActiveDying?.VictimSeat == owner.Seat :
             rule.OutputKind is CardKind.Peach or CardKind.Alcohol)) return false;
        if (rule.ActivationUsageGroup is { } group && rule.UsesPerPhase is { } limit &&
            _programPhaseUses.GetValueOrDefault((owner.Seat, instance.SkillId, group)) >= limit) return false;
        if (rule.ConversionStateId is not { } state) return true;
        var tier = _configuredConversionTiers.GetValueOrDefault((owner.Seat, state));
        if (!rule.DeclaredEntity) return tier >= rule.MinimumTier && tier <= rule.MaximumTier;
        return _declaredConversionCards.TryGetValue((owner.Seat, state), out var declared) &&
            declared.Turn == _turnNumber && declared.Card == card.Id && declared.Kind == rule.OutputKind &&
            _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner.Seat);
    }

    private void UpgradeConfiguredConversionTier(ProgramSkillFrame frame, string state)
    {
        var key = (frame.OwnerSeat, state);
        var tier = _configuredConversionTiers.GetValueOrDefault(key);
        if (tier >= 2) throw new InvalidOperationException("The conversion tier is already maximal.");
        _configuredConversionTiers[key] = tier + 1;
        AdvanceEventRulesAndQueueFact(new ConfiguredConversionTierChangedEvent(frame.OwnerSeat, state, tier + 1));
        AddLog("SkillTriggered", $"{_players[frame.OwnerSeat].Name} 的转换能力修改至第 {tier + 1} 层。", frame.OwnerSeat);
    }

    private SkillProgramStepOutcome BeginConfiguredCardDeclaration(ProgramSkillFrame frame, string bind, string state)
    {
        var binding = GetProgramCardSet(frame, bind);
        if (binding.CardIds.Count != 1 || binding.Visibility != SkillProgramCardSetVisibility.Public ||
            binding.SourceLocations.Single() != CardLocation.Hand(frame.OwnerSeat) ||
            _cardZones.GetLocation(binding.CardIds[0]) != CardLocation.Hand(frame.OwnerSeat))
            throw new InvalidOperationException("Declaring requires the publicly revealed real owner hand card.");
        var others = _players.Where(player => player.IsAlive && player.Seat != frame.OwnerSeat).ToArray();
        if (others.Length == 0) return SkillProgramStepOutcome.Continue;
        var distance = others.Min(player => GetCombatDistance(frame.OwnerSeat, player.Seat));
        var nearest = others.Where(player => GetCombatDistance(frame.OwnerSeat, player.Seat) == distance)
            .Select(player => player.Seat).Order().ToArray();
        var tier = _configuredConversionTiers.GetValueOrDefault((frame.OwnerSeat, state));
        if (tier >= 2) throw new InvalidOperationException("The direct tier does not reveal or declare a card.");
        _cardDeclarationDrafts[frame.Id] = new(state, bind, binding.CardIds[0], tier, nearest);
        PublishConfiguredCardDeclaration(frame);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<CardKind> DeclarationNames(ProgramSkillFrame frame, CardDeclarationDraft draft) =>
        _contentRegistry.GetSkill(frame.SkillId).Program!.ViewAs
            .Where(rule => rule.ConversionStateId == draft.StateId && rule.DeclaredEntity &&
                rule.MinimumTier <= draft.Tier && rule.MaximumTier >= draft.Tier)
            .Select(rule => rule.OutputKind).Distinct().Order().ToArray();

    private void PublishConfiguredCardDeclaration(ProgramSkillFrame frame)
    {
        var draft = _cardDeclarationDrafts[frame.Id];
        var seat = draft.DeclarerSeat ?? frame.OwnerSeat;
        PromptChoice Choice(string key, string label, IReadOnlyList<int> targets) =>
            new(new ChoiceId($"declaration.{frame.Id}.{seat}.{key}"), label, [], targets,
                new Dictionary<string, string> { ["program-action"] = "configured-card-declaration",
                    ["frame-id"] = frame.Id.ToString(), ["choice"] = key });
        var choices = draft.DeclarerSeat is null
            ? draft.NearestSeats.Select(target => Choice($"seat-{target}", $"请 {_players[target].Name} 声明牌名", [target])).ToArray()
            : DeclarationNames(frame, draft).Select(kind => Choice(kind.ToString(), $"声明【{CardCatalog.Get(kind).DisplayName}】", [])).ToArray();
        if (choices.Length == 0) throw new InvalidOperationException("A declaration has no configured legal names.");
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, seat,
            draft.DeclarerSeat is null ? "选择距离最近的其他角色。" : "为公开展示的手牌声明牌名。",
            [], draft.DeclarerSeat is null ? draft.NearestSeats : [], SourceSeat: frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 声明", skill.Description), Choices = choices };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveConfiguredCardDeclaration(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("A declaration lost its frame.");
        var draft = _cardDeclarationDrafts.GetValueOrDefault(frame.Id) ??
            throw new InvalidOperationException("A declaration lost its draft.");
        if (choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString() ||
            _cardZones.GetLocation(draft.CardId) != CardLocation.Hand(frame.OwnerSeat))
            throw new InvalidOperationException("A declaration is stale.");
        if (draft.DeclarerSeat is null)
        {
            var target = choice.Targets.Single();
            if (!draft.NearestSeats.Contains(target) || !_players[target].IsAlive)
                throw new InvalidOperationException("The declarer is not a frozen nearest other character.");
            ClearPendingDecision();
            _cardDeclarationDrafts[frame.Id] = draft with { DeclarerSeat = target };
            PublishConfiguredCardDeclaration(frame);
            return;
        }
        if (!Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("choice"), out var kind) ||
            !DeclarationNames(frame, draft).Contains(kind))
            throw new InvalidOperationException("The declared name is not available at the frozen tier.");
        ClearPendingDecision();
        _declaredConversionCards[(frame.OwnerSeat, draft.StateId)] = (_turnNumber, draft.CardId, kind);
        AdvanceEventRulesAndQueueFact(new PhysicalCardNameDeclaredEvent(frame.OwnerSeat, draft.DeclarerSeat.Value,
            draft.StateId, draft.CardId, kind, _turnNumber));
        AddLog("SkillTriggered", $"{_players[draft.DeclarerSeat.Value].Name} 为 {_players[frame.OwnerSeat].Name} 展示的牌声明【{CardCatalog.Get(kind).DisplayName}】。", draft.DeclarerSeat.Value, frame.OwnerSeat);
        _cardDeclarationDrafts.Remove(frame.Id);
        AdvanceRuntimeProgram(frame.Id);
    }

    private void AssertConfiguredCardDeclaration(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (!_cardDeclarationDrafts.TryGetValue(frame.Id, out var draft)) return;
        if (paused.Op != SkillProgramEffectOp.DeclareBoundCardNameUntilTurnEnd ||
            paused.StateId != draft.StateId || paused.SourceBind != draft.Bind ||
            draft.NearestSeats.Count == 0 || draft.NearestSeats.Contains(frame.OwnerSeat) ||
            draft.DeclarerSeat is { } declaredSeat && !draft.NearestSeats.Contains(declaredSeat) ||
            _cardZones.GetLocation(draft.CardId) != CardLocation.Hand(frame.OwnerSeat) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != (draft.DeclarerSeat ?? frame.OwnerSeat) || prompt.Choices.Count == 0 ||
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") != "configured-card-declaration" ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString()))
            throw new InvalidOperationException("A card declaration lost its validated owner, entity or prompt.");
    }
}
