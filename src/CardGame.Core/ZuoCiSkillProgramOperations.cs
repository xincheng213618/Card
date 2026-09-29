namespace CardGame.Core;

/// <summary>
/// Zuo Ci (化身/新生) program operations. The declared-skill exclusion carries the
/// BWIKI boundary ruling (限定技、觉醒技、主公技除外) as an explicit optional node;
/// the official page text carries no such wording, so the node defaults to the
/// exclusion being active and stays content-visible for later revision.
/// </summary>
internal sealed class HuaShenXinShengProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.HuaShenXinSheng;
    public override ISkillProgramEffectHandler Handler { get; } =
        new HuaShenXinShengSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.HuaShenAvatarGain,
        static (effect, context) => context.HuaShenAvatarGain(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class HuaShenChangeAvatarProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    /// <summary>The only tags a content file may exclude from avatar declarations.</summary>
    private static readonly SkillTag[] ExcludableTags =
        [SkillTag.Limited, SkillTag.Awakening, SkillTag.Lord];

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.HuaShenChangeAvatar;
    public override ISkillProgramEffectHandler Handler { get; } =
        new HuaShenChangeAvatarSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.HuaShenAvatarGain,
        static (_, _) => { });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "declaredSkillTags", "condition");
        var declaredSkillTags = r.Has("declaredSkillTags")
            ? r.RequiredEnumArray<SkillTag>("declaredSkillTags")
            : DefaultDeclaredSkillTags;
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            0, r.Condition(), declaredSkillTags: declaredSkillTags);
        RequireAlways(effect, r.Path);
        if (declaredSkillTags.Any(tag => !ExcludableTags.Contains(tag)) ||
            declaredSkillTags.Distinct().Count() != declaredSkillTags.Count)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.declaredSkillTags: " +
                "avatar declarations only exclude limited, awakening or lord skills.");
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];

    internal static IReadOnlyList<SkillTag> DefaultDeclaredSkillTags { get; } = ExcludableTags;
}

public sealed class HuaShenXinShengSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.HuaShenXinSheng;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.DeclareHuaShenXinSheng(frame.Id, targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class HuaShenChangeAvatarSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.HuaShenChangeAvatar;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.ChangeHuaShenAvatar(frame, targetSeat, effect.DeclaredSkillTags);
}
