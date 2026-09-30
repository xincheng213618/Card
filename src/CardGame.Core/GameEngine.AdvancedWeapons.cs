namespace CardGame.Core;
public sealed partial class GameEngine
{
    private readonly HashSet<int> _permanentlyRemovedWeapons = [];
    private readonly HashSet<int> _generatedPhysicalCardIds = [];
    private readonly HashSet<string> _usedGeneralWeaponSources = new(StringComparer.Ordinal);
    private bool MoveProcessingCardUnlessDestroyed(Card card, CardLocation destination, CardMoveReason reason)
    {
        if (card.IsGeneralWeapon && _cardZones.GetLocation(card.Id) == CardLocation.OutsideGame)
            return false;
        MoveCard(card, CardLocation.Processing, destination, reason);
        return true;
    }
    private void ResolveDynamicEquipmentSkillGrants(Card card, CardLocation from, CardLocation to)
    {
        var source = $"equipment:dynamic:{card.Id}";
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } oldSeat })
            foreach (var grant in _players[oldSeat].SkillGrants.Grants.Where(grant => grant.SourceId == source).ToArray())
                _players[oldSeat].SkillGrants.RemoveGrant(grant.GrantId);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } newSeat })
            foreach (var id in card.GrantedSkillIds.Concat(_copiedWeaponSkills.GetValueOrDefault(card.Id) ?? []).Distinct(StringComparer.Ordinal))
            {
                var grant = $"{source}:{id}";
                _players[newSeat].SkillGrants.Grant(new(grant, id, grant, source));
            }
    }
    private string[] GetGeneralWeaponSkillIds(ContentGeneralDefinition general) =>
        general.SkillIds.Select(_contentRegistry.GetSkill).Where(skill =>
            skill.Description.Contains("【杀】", StringComparison.Ordinal) &&
            (skill.Tags & (SkillTag.Awakening | SkillTag.Limited | SkillTag.Conversion | SkillTag.Lord)) == SkillTag.None)
            .Select(skill => skill.Id).ToArray();

    private string DescribeGeneralWeaponChoice(string generalId)
    {
        var general = _contentRegistry.Generals[generalId];
        var names = GetGeneralWeaponSkillIds(general).Select(id => _contentRegistry.GetSkill(id).Name).ToArray();
        return $"{general.Name} · 攻击范围{general.BaseHp} · " +
            (names.Length == 0 ? "无可复制技能" : string.Join(" / ", names));
    }

    private void EquipGeneralWeapon(ProgramSkillFrame frame, ContentGeneralDefinition general)
    {
        _usedGeneralWeaponSources.Add(general.Id);
        var skills = GetGeneralWeaponSkillIds(general);
        var id = CreateCardZoneDiagnostics().Select(item => item.CardId).DefaultIfEmpty(0).Max() + 1;
        var card = new Card(id, CardKind.GeneralWeapon, Suit.None, 0)
        {
            PrintedName = general.Name, PrintedAttackRange = general.BaseHp,
            GrantedSkillIds = skills, IsGeneralWeapon = true
        };
        _cardZones.AddGeneratedCard(card);
        _generatedPhysicalCardIds.Add(card.Id);
        PlaceAdvancedEquipment(card, frame.OwnerSeat);
    }
    private void PlaceAdvancedWeapon(ProgramSkillFrame frame, int targetSeat, CardKind kind)
    {
        if (!_players[targetSeat].IsAlive) return;
        var found = CreateCardZoneDiagnostics().Where(item => item.CardKind == kind && !_permanentlyRemovedWeapons.Contains(item.CardId)).ToArray();
        var card = found.Length > 0 ? GetAdvancedCard(found[0].CardId) : null;
        if (card is null)
        {
            if (_permanentlyRemovedWeapons.Any(id => GetAdvancedCard(id).Kind == kind)) return;
            card = new(CreateCardZoneDiagnostics().Select(item => item.CardId).DefaultIfEmpty(0).Max() + 1, kind, Suit.Heart, 13);
            _cardZones.AddGeneratedCard(card);
            _generatedPhysicalCardIds.Add(card.Id);
        }
        PlaceAdvancedEquipment(card, targetSeat);
    }
    private void PlaceAdvancedEquipment(Card card, int targetSeat)
    {
        var target = _players[targetSeat];
        var slot = EquipmentCatalog.Get(card.Kind).Slot;
        if (target.EquipmentSlotCapacity(slot) == 0) return;
        var from = _cardZones.GetLocation(card.Id);
        if (from == CardLocation.Equipment(targetSeat)) return;
        var equipped = GetEquipment(target).Where(item => EquipmentCatalog.Get(item.Kind).Slot == slot).ToArray();
        if (equipped.Length >= target.EquipmentSlotCapacity(slot))
        {
            var replaced = equipped[0];
            CopyFirstReplacedWeapon(card, replaced);
            MoveCard(replaced, CardLocation.Equipment(targetSeat), CardLocation.DiscardPile, CardMoveReasons.EquipmentReplace);
        }
        MoveCard(card, from, CardLocation.Equipment(targetSeat), CardMoveReasons.EquipmentEnter);
    }
    private void CopyFirstReplacedWeapon(Card card, Card replaced)
    {
        if (card.Kind != CardKind.RedBloodBlade || EquipmentCatalog.Get(replaced.Kind).Slot != EquipmentSlot.Weapon || !_weaponCopyConsumed.Add(card.Id)) return;
        _copiedWeaponAbilities[card.Id] = GetWeaponAbilityKinds(replaced).ToArray();
        var skills = replaced.GrantedSkillIds.Concat(replaced.Kind == CardKind.XingtianAxe ? new[] { "special:xingtian-axe-effect" } : []).ToArray();
        _copiedWeaponSkills[card.Id] = skills;
    }
    private readonly Dictionary<int, IReadOnlyList<string>> _copiedWeaponSkills = [];
    private IEnumerable<CardKind> GetWeaponAbilityKinds(Card card) =>
        new[] { card.Kind }.Concat(_copiedWeaponAbilities.GetValueOrDefault(card.Id) ?? []);
    private bool HasWeaponAbility(CharacterState player, CardKind kind) =>
        GetEquipment(player).Any(card => !card.IsGeneralWeapon && GetWeaponAbilityKinds(card).Contains(kind)) ||
        (_inheritedWeaponAbilities.GetValueOrDefault(player.Seat) ?? []).Contains(kind);
    private IReadOnlyList<Card> GetEquipmentRuleCards(CharacterState player)
    {
        var physical = GetEquipment(player);
        var kinds = physical.Where(card => !card.IsGeneralWeapon).SelectMany(GetWeaponAbilityKinds)
            .Concat(_inheritedWeaponAbilities.GetValueOrDefault(player.Seat) ?? []).Distinct().ToArray();
        return physical.Where(card => !card.IsGeneralWeapon).Concat(kinds.Where(kind => !physical.Any(card => card.Kind == kind))
            .Select((kind, index) => new Card(-500 - index, kind, Suit.None, 0))).ToArray();
    }
    private void CommitWeaponInheritance(ProgramSkillFrame frame, int cardId)
    {
        var card = GetAdvancedCard(cardId);
        var location = _cardZones.GetLocation(cardId);
        if (EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.Weapon || location.Zone != CardZoneKind.Equipment && location != CardLocation.Hand(frame.OwnerSeat))
            throw new InvalidOperationException("Only an equipped weapon or owner's hand weapon can be inherited.");
        var range = location.OwnerSeat is { } seat ? GetWeaponAttackRange(_players[seat], card) : EquipmentCatalog.Get(card.Kind).WeaponAttackRange ?? 1;
        _inheritedWeaponAbilities[frame.OwnerSeat] = (_inheritedWeaponAbilities.GetValueOrDefault(frame.OwnerSeat) ?? [])
            .Concat(GetWeaponAbilityKinds(card)).Distinct().ToArray();
        var skills = card.GrantedSkillIds.Concat(_copiedWeaponSkills.GetValueOrDefault(card.Id) ?? [])
            .Concat(card.Kind == CardKind.XingtianAxe ? new[] { "special:xingtian-axe-effect" } : []).ToArray();
        AcquireRuntimeSkills(_players[frame.OwnerSeat], frame.SkillId, skills);
        _permanentlyRemovedWeapons.Add(card.Id);
        MoveCard(card, location, CardLocation.OutsideGame, new("equipment.permanent-removal"));
        DrawCards(_players[frame.OwnerSeat], range, true);
    }
    private SkillProgramStepOutcome BeginWeaponReclaim(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var turns = EventsSinceLastBoundary(item => item is TurnStartedEvent).ToArray();
        var damaged = turns.OfType<DamageAppliedEvent>().Any(item => !item.SourceLess && item.SourceSeat == frame.OwnerSeat && item.Amount > 0);
        var uses = turns.OfType<CardUseDeclaredEvent>().Where(item => item.SourceSeat == frame.OwnerSeat)
            .Count(item => item.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage);
        if (!damaged && uses < 2) return SkillProgramStepOutcome.Continue;
        var blade = _players.Where(player => player.IsAlive && player.Seat != frame.OwnerSeat).SelectMany(GetEquipment).FirstOrDefault(card => card.Kind == effect.OutputKind);
        if (blade is null) return SkillProgramStepOutcome.Continue;
        var former = _cardZones.GetLocation(blade.Id).OwnerSeat!.Value;
        var affected = AliveSeatsBetweenInclusive(former, frame.OwnerSeat);
        PlaceAdvancedEquipment(blade, frame.OwnerSeat);
        if (_cardZones.GetLocation(blade.Id) != CardLocation.Equipment(frame.OwnerSeat)) return SkillProgramStepOutcome.Continue;
        foreach (var seat in affected)
        {
            var candidates = GetHand(_players[seat]).Concat(GetEquipment(seat)).Concat(GetJudgment(_players[seat])).ToArray();
            if (candidates.Length == 0) continue;
            var card = candidates[_random.Next(candidates.Length)];
            MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.Hand(frame.OwnerSeat), new("skill.weapon-reclaim.obtain"));
        }
        return SkillProgramStepOutcome.Continue;
    }
    private IReadOnlyList<int> AliveSeatsBetweenInclusive(int start, int end)
    {
        var clockwise = new List<int>(); var counterclockwise = new List<int>();
        for (var seat = start; seat != end; seat = (seat + 1) % _playerCount) if (_players[seat].IsAlive) clockwise.Add(seat);
        for (var seat = start; seat != end; seat = (seat + _playerCount - 1) % _playerCount) if (_players[seat].IsAlive) counterclockwise.Add(seat);
        return clockwise.Count <= counterclockwise.Count ? clockwise : counterclockwise;
    }
}
