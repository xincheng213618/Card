namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardKind[] SlashKinds =
        [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash];

    private bool HasCardDistanceExemption(CharacterState actor, CharacterState target, CardKind kind, long? cardUseFrameId = null, int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null) =>
        DistanceRules.HasCardUseExemption(new(actor, target, kind, cardUseFrameId, excludedEquipmentId, excludedEquipmentSlot));

    private bool HasCardArmorBypass(CharacterState actor, CharacterState target, CardKind kind) =>
        IsSlashCard(kind) && HasMouHuaXiongWei(actor.Seat) ||
        IsSlashCard(kind) && HasCardPolicy(actor, SkillProgramCardPolicyKind.HellSlashIgnoreArmor, kind) ||
        HasArmorBypass(actor) ||
        IsArmorIneffectiveForTurn(target) ||
        HasDirectedTurnCardPolicy(actor.Seat, target.Seat, kind, DirectedTurnCardPolicyEffect.IgnoreArmor);

    private bool HasYuanchouCardPolicy => _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.BlackSlashIgnoresArmorBothDirections);

    private bool HasDirectedCardArmorBypass(long resolutionId, int targetSeat)
    {
        var action = _resolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.Id == resolutionId)?.Action;
        // Damage sources can change (for example in a duel). The policy belongs
        // to the original card actor and its actual declared targets.
        return HasYuanchouArmorBypass(resolutionId, targetSeat) || HasIssuedOriginalTargetArmorBypass(resolutionId, targetSeat) || action is not null && action.TargetSeats.Contains(targetSeat) &&
            HasDirectedTurnCardPolicy(action.ActorSeat, targetSeat, action.EffectiveKind,
                DirectedTurnCardPolicyEffect.IgnoreArmor);
    }

    private bool HasSlashAllowanceForAnyTarget(CharacterState actor, IEnumerable<CardKind>? kinds = null) =>
        _players.Any(target => target.IsAlive && target.Seat != actor.Seat &&
            (kinds ?? SlashKinds).Any(kind =>
                !IsCardUseForbidden(actor.Seat, kind, CardActionType.Use) &&
                !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) &&
                (CanSpendSlashUse(actor, target, ignoresCount: false, kind) ||
                 GetHand(actor).Any(card => BypassesSlashLimitBySuit(actor, card, kind) ||
                     HasJudgedRankSlashQuota(actor.Seat, kind, SpecificSlashRank(actor, card, kind))))));

    private bool CanUseGlobalCard(CharacterState actor, CardKind kind, bool excludeOwner = false) =>
        GetDeclaredGlobalCardTargets(actor, kind).Where(target => !excludeOwner || target != actor.Seat).All(target =>
            !IsDirectedCardTargetProhibited(actor.Seat, target, kind));

    // These are declared targets, before immunity and replacement remove effects.
    // Borrowed Sword's second seat is the target of a later Slash, not of this trick.
    private IEnumerable<int> GetDeclaredGlobalCardTargets(CharacterState actor, CardKind kind) =>
        _players.Where(player => player.IsAlive &&
            !TargetRules.IsDeclaredGlobalProhibited(new(actor.Seat, player.Seat, kind)) &&
            (kind is CardKind.PeachGarden or CardKind.FiveGrains or CardKind.AllianceFeast || player.Seat != actor.Seat))
            .Select(player => player.Seat);

    private IEnumerable<int> GetDeclaredCardTargets(
        CharacterState actor, LegalActionKind actionKind, IReadOnlyList<int> targets) =>
        actionKind switch
        {
            LegalActionKind.BarbarianAssault => GetDeclaredGlobalCardTargets(actor, CardKind.BarbarianAssault),
            LegalActionKind.ArrowBarrage => GetDeclaredGlobalCardTargets(actor, CardKind.ArrowBarrage),
            LegalActionKind.PeachGarden => GetDeclaredGlobalCardTargets(actor, CardKind.PeachGarden),
            LegalActionKind.FiveGrains => GetDeclaredGlobalCardTargets(actor, CardKind.FiveGrains),
            LegalActionKind.BorrowedSword => targets.Take(1),
            _ => targets
        };

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 200)]
    private bool DistancePhysicalSlash200HasCardDistanceExemption(ProgramSlashDistanceQuery q) =>
        HasCardDistanceExemption(q.Actor, q.Target, q.Kind, q.ExistingUseFrameId);


    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 100)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 80)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 160)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 100)]
    private bool DistanceVirtualSlash100HasCardDistanceExemption(ProgramSlashDistanceQuery q) =>
        HasCardDistanceExemption(q.Actor, q.Target, q.Kind);







}
