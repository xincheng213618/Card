namespace CardGame.Core;

public sealed partial class GameEngine
{
    private PromptChoice SelectAiAdvancedLifecycleChoice(PendingDecision decision, ProgramSkillFrame frame)
    {
        var draft = frame.AdvancedSelection ?? throw new InvalidOperationException("The advanced AI choice lost its draft.");
        var owner = _players[frame.OwnerSeat];
        var finish = decision.Choices.SingleOrDefault(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "finish");
        var candidates = decision.Choices.Where(choice => choice != finish).ToArray();
        if (candidates.Length == 0)
            return finish ?? throw new InvalidOperationException("The advanced AI choice has no legal continuation.");

        string Value(PromptChoice choice) => choice.Parameters["advanced-value"];
        Card CardFor(PromptChoice choice) => GetAdvancedCard(int.Parse(Value(choice)));
        PromptChoice CheapestCard() => candidates.OrderBy(choice => CardCatalog.Get(CardFor(choice).Kind).HandKeepValue +
                (_cardZones.GetLocation(CardFor(choice).Id).Zone == CardZoneKind.Equipment ? 8 : 0))
            .ThenBy(choice => CardFor(choice).Id).First();
        switch (draft.Operation)
        {
            case SkillProgramEffectOp.EquipSampledGenerals:
                var emptySlots = owner.EquipmentSlotCapacity(EquipmentSlot.Weapon) -
                    GetEquipment(owner).Count(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon);
                if (draft.Selected.Count >= Math.Max(0, emptySlots) && finish is not null) return finish;
                return candidates.OrderByDescending(choice =>
                        _contentRegistry.Generals[Value(choice)].BaseHp +
                        2 * GetGeneralWeaponSkillIds(_contentRegistry.Generals[Value(choice)]).Length)
                    .ThenBy(Value, StringComparer.Ordinal).First();
            case SkillProgramEffectOp.SampleFactionSkills:
                if (draft.Selected.Count >= 4 && finish is not null) return finish;
                return CheapestCard();
            case SkillProgramEffectOp.ReplaceSkillsOnAwakening:
                var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
                    .GetPausedInstruction(frame.InstructionIndex).Effect;
                if (draft.Selected.Count >= effect.SkillIds.Count && finish is not null) return finish;
                return candidates.OrderBy(choice => owner.SkillGrants.Grants.Any(grant =>
                        grant.SkillId == Value(choice) && grant.SourceId.StartsWith("acquired:", StringComparison.Ordinal)) ? 0 : 1)
                    .ThenBy(Value, StringComparer.Ordinal).First();
            case SkillProgramEffectOp.ObtainDeckRankSum:
                if (finish is not null) return finish;
                // The published candidates already preserve exact-sum feasibility at each selection.
                return candidates.OrderBy(choice => CardFor(choice).Rank).ThenBy(choice => CardFor(choice).Id).First();
            case SkillProgramEffectOp.BalanceHandAttackTricks:
                if (finish is not null) return finish;
                return CheapestCard();
            case SkillProgramEffectOp.DamageAfterDeckShuffle:
            case SkillProgramEffectOp.DamageFarthestCharacter:
                var snapshot = CreateSnapshot(owner.Seat);
                var damageHint = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
                var best = candidates.Select(choice => (Choice: choice,
                        Score: _aiBrains[owner.Seat].ScoreProgramTarget(snapshot, int.Parse(Value(choice)), damageHint)))
                    .OrderByDescending(item => item.Score).ThenBy(item => Value(item.Choice), StringComparer.Ordinal).First();
                if (finish is not null && (draft.Selected.Count >= 3 || best.Score <= 0)) return finish;
                return best.Choice;
            case SkillProgramEffectOp.InheritWeapon:
                return candidates.OrderByDescending(choice => CardFor(choice).PrintedAttackRange ?? EquipmentCatalog.Get(CardFor(choice).Kind).WeaponAttackRange ?? 1)
                    .ThenByDescending(choice => GetWeaponAbilityKinds(CardFor(choice)).Count())
                    .ThenBy(choice => CardFor(choice).Id).First();
            default:
                throw new InvalidOperationException($"Unsupported advanced AI selection '{draft.Operation}'.");
        }
    }
}
