namespace CardGame.Core;

// Query ports contain immutable/scalar facts; native mutation remains with the host.
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MoCaoCaoFuzaiRuntime(IMoCaoCaoFacts facts, IMoCaoCaoOperations operations,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events) : IMoCaoCaoFuzaiHost
{
    internal static CardKind[] Weapons => [CardKind.Crossbow, CardKind.CixiongDoubleSwords, CardKind.QinggangSword,
        CardKind.IceSword, CardKind.GudingBlade, CardKind.QinglongCrescentBlade, CardKind.ZhangbaSerpentSpear,
        CardKind.StoneAxe, CardKind.FangtianHalberd, CardKind.ZhuqueFan, CardKind.QilinBow, CardKind.QiBaoDao, CardKind.BaiBiDao];
    internal static CardKind[] Armors => [CardKind.Tengjia, CardKind.BaguaFormation, CardKind.RenwangShield, CardKind.SilverLion];

    public SkillProgramStepOutcome Initialize(ProgramSkillFrame frame)
    {
        if (!facts.IsExactStart(frame, SkillProgramEffectOp.InitializeMoCaoCaoFuzai) || facts.FuzaiBinding(frame.OwnerSeat) is not { } source)
            throw new InvalidOperationException("Fuzai initialization requires its native game-start owner binding.");
        if (!facts.History<MoCaoCaoEquipmentIssuedEvent>().Any(e => e.Source.OwnerSeat == frame.OwnerSeat))
            Issue(source, facts.Participant(frame.OwnerSeat).Hp, frame.Id, true);
        return SkillProgramStepOutcome.Continue;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureHealthChange, 33800)]
    internal void ObserveHp(HpChangeContext change)
    {
        if (!facts.Enabled || change.HpBefore == change.HpAfter || facts.FuzaiBinding(change.TargetSeat) is not { } source ||
            facts.History<MoCaoCaoEquipmentIssuedEvent>().Any(e => !e.Initial && e.CauseId == change.Id && e.Source.OwnerSeat == change.TargetSeat)) return;
        Issue(source, change.HpAfter, change.Id, false);
    }
    private void Issue(MoCaoCaoBinding binding, int hp, long cause, bool initial)
    {
        var weapons = Weapons.Where(k => EquipmentCatalog.Get(k).WeaponAttackRange == hp).ToArray();
        var armors = Armors.Where(k => EquipmentCatalog.Get(k).DisplayName.Length == hp).ToArray();
        if (weapons.Length == 0) weapons = Weapons;
        if (armors.Length == 0) armors = Armors;
        var weapon = weapons[operations.NextRandom(weapons.Length)];
        var armor = armors[operations.NextRandom(armors.Length)];
        events.AdvanceFact(new MoCaoCaoEquipmentIssuedEvent(frames.NextId(), binding.Source, binding.GameplayHash, hp, weapon, armor, cause, initial));
        operations.LogEquipment(binding.Source.OwnerSeat, weapon, armor);
    }
    internal MoCaoCaoEquipmentIssuedEvent? LastIssue(int ownerSeat) => !facts.Enabled ? null :
        facts.History<MoCaoCaoEquipmentIssuedEvent>().LastOrDefault(e => e.Source.OwnerSeat == ownerSeat &&
            facts.Program(e.Source.SkillId)?.GameplayHash == e.GameplayHash && Weapons.Contains(e.Weapon) && Armors.Contains(e.Armor));
    internal CardKind? VirtualKind(int ownerSeat, EquipmentSlot slot)
    {
        if (!facts.Enabled || facts.FuzaiBinding(ownerSeat) is null || LastIssue(ownerSeat) is not { } issue) return null;
        var actor = facts.Participant(ownerSeat);
        if (!actor.Alive || actor.EquipmentCount != 0) return null;
        return slot == EquipmentSlot.Weapon && actor.WeaponSlot ? issue.Weapon : slot == EquipmentSlot.Armor && actor.ArmorSlot ? issue.Armor : null;
    }
    internal bool HasVirtual(int ownerSeat, CardKind kind) => VirtualKind(ownerSeat, EquipmentSlot.Weapon) == kind || VirtualKind(ownerSeat, EquipmentSlot.Armor) == kind;
    internal bool IsRestrictedPhysicalEquipment(int ownerSeat, int cardId) => facts.Enabled && facts.FuzaiBinding(ownerSeat) is not null &&
        cardId > 0 && EquipmentCatalog.IsEquipment(facts.Kind(cardId));
    internal bool HasHandPrivilege(int actorSeat, IReadOnlyList<int>? physicalIds, long? useId = null)
    {
        if (!facts.Enabled || facts.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != actorSeat ||
            physicalIds is not { Count: > 0 } || physicalIds.Any(id => id <= 0)) return false;
        var ids = physicalIds;
        if (useId is { } id && frames.Find(id) is CardUseFrame { Action: { } action })
        {
            if (action.ActorSeat != actorSeat || action.ProviderSeat != actorSeat || action.PhysicalCards.Count == 0 ||
                action.PhysicalCards.Any(c => c.From != CardLocation.Hand(actorSeat))) return false;
            ids = action.PhysicalCards.Select(c => c.CardId).ToArray();
        }
        else if (ids.Any(card => facts.Location(card) != CardLocation.Hand(actorSeat))) return false;
        var issued = facts.History<MoCaoCaoHandPrivilegedEvent>().Where(e => e.Source.OwnerSeat == actorSeat && e.Phase == phase &&
            facts.Program(e.Source.SkillId)?.GameplayHash == e.GameplayHash).Select(e => e.CardId).ToHashSet();
        return ids.All(issued.Contains);
    }
    internal bool HasUsePrivilege(long useId, int actorSeat) => frames.Find(useId) is CardUseFrame { Action: { } a } &&
        HasHandPrivilege(actorSeat, a.PhysicalCards.Select(c => c.CardId).ToArray(), useId);
    [ProgramPipelineHook(ProgramPipelinePhase.HealthChangeSubject, 33800)]
    internal bool HasLiveSubject(int ownerSeat) => facts.FuzaiBinding(ownerSeat) is not null;
}
