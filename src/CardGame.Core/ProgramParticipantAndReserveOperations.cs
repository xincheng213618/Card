namespace CardGame.Core;

internal interface IParticipantReserveProgramHost
{
    SkillProgramStepOutcome ExecuteParticipantReserve(SkillProgramEffect effect, ProgramSkillFrame frame);
}

internal abstract class ParticipantReserveDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => SkillProgramEffectCatalog.Default.Resolve(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "secondaryAmount", "targetKind", "nature", "zones", "condition", "marker", "prevent");
        var amount = r.Has("amount") ? r.RequiredInt("amount") : 0;
        if (amount < 0 || amount > 64) throw new InvalidOperationException("Participant amount must be 0..64.");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var marker = r.Has("marker") ? r.RequiredEnum<PlayerMarkerKind>("marker") : (PlayerMarkerKind?)null;
        var targetKind = r.Has("targetKind") ? r.RequiredEnum<SkillProgramTargetKind>("targetKind") : (SkillProgramTargetKind?)null;
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        if (Op == SkillProgramEffectOp.RequestSlashByNearest &&
                (target != SkillProgramEffectTarget.Owner ||
                 targetKind != SkillProgramTargetKind.OtherLiving || amount < 1) ||
            Op is SkillProgramEffectOp.SpendMarkerOrLoseHp && (marker is null || amount < 1) ||
            Op is SkillProgramEffectOp.GrantAttributedNatureEffect && marker is not (PlayerMarkerKind.Gale or PlayerMarkerKind.Mist) ||
            Op is SkillProgramEffectOp.DiscardParticipantCards && zones is not [CardZoneKind.Hand] and not [CardZoneKind.Equipment] ||
            targetKind is not null && targetKind is not (SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.OtherLiving) ||
            Op is SkillProgramEffectOp.InitializePrivatePile or SkillProgramEffectOp.LoseHpUnclamped or SkillProgramEffectOp.SpendMarkerOrLoseHp or SkillProgramEffectOp.ExchangePrivatePile or SkillProgramEffectOp.RecoverAllLiving && target != SkillProgramEffectTarget.Owner ||
            Op is SkillProgramEffectOp.DamageParticipants or SkillProgramEffectOp.LoseHpParticipants or SkillProgramEffectOp.LoseHpUnclamped or SkillProgramEffectOp.RecoverAllLiving && amount < 1)
            throw new InvalidOperationException($"Invalid participant operation parameters at {r.Path}.");
        var secondary = r.Has("secondaryAmount") ? r.RequiredInt("secondaryAmount") : amount;
        if (secondary < 0 || secondary > 64) throw new InvalidOperationException("Secondary participant amount must be 0..64.");
        // The decline branch re-applies this amount after a suspended prompt, so the
        // per-participant value must not depend on the cursor position.
        if (Op == SkillProgramEffectOp.RequestSlashByNearest && secondary != amount)
            throw new InvalidOperationException("A nearest-character slash request uses one fixed amount.");
        return new(Op, target, amount, r.Condition(),
            minimumValue: secondary,
            targetKind: targetKind,
            zones: zones,
            damageNature: r.Has("nature") ? r.RequiredEnum<DamageNature>("nature") : null,
            marker: marker,
            booleanValue: r.Has("prevent") ? r.RequiredBool("prevent") : null);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.TargetKind is null ? WithSelectedTarget(effect) : [];
}
internal sealed class DamageParticipantsDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageParticipants; }
internal sealed class LoseHpParticipantsDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHpParticipants; }
internal sealed class DiscardParticipantCardsDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardParticipantCards; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class InitializePrivatePileDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializePrivatePile; }
internal sealed class ExchangePrivatePileDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangePrivatePile; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; }
internal sealed class GrantAttributedNatureEffectDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantAttributedNatureEffect; }
internal sealed class RecoverAllLivingDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverAllLiving; }
internal sealed class SpendMarkerOrLoseHpDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendMarkerOrLoseHp; public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice; public override ProgramOperationAiPolicy AiPolicy {get;} = new(ProgramOperationAiSemantic.Damage,static(e,c)=>c.PriceAttributedMarkerOrHpPayment(e)); }
internal sealed class LoseHpUnclampedDescriptor : ParticipantReserveDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHpUnclamped; }
internal sealed class RequestSlashByNearestDescriptor : ParticipantReserveDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashByNearest;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
}
public abstract class ParticipantReserveHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((IParticipantReserveProgramHost)host).ExecuteParticipantReserve(effect, frame);
}
public sealed class DamageParticipantsHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DamageParticipants; }
public sealed class LoseHpParticipantsHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHpParticipants; }
public sealed class DiscardParticipantCardsHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardParticipantCards; }
public sealed class InitializePrivatePileHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializePrivatePile; }
public sealed class ExchangePrivatePileHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangePrivatePile; }
public sealed class GrantAttributedNatureEffectHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantAttributedNatureEffect; }
public sealed class RecoverAllLivingHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverAllLiving; }
public sealed class SpendMarkerOrLoseHpHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SpendMarkerOrLoseHp; }
public sealed class LoseHpUnclampedHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHpUnclamped; }
public sealed class RequestSlashByNearestHandler : ParticipantReserveHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashByNearest; }

public sealed record ProgramPrivateReserveDraft(int ChooserSeat, string Mode, int RequiredCount,
    IReadOnlyList<int> SelectedHandIds, IReadOnlyList<int> SelectedReserveIds);
