using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>The complete base roster of S5 main battle/group 2674, in source order.</summary>
public sealed class StandardShanheS5MoushiPackage : IGameContentPackage
{
    public const string XunchenId = "shanhe:s5-xun-chen-2674";
    public const string GuotuId = "shanhe:s5-guo-tu-2674";
    public const string ShenpeiId = "shanhe:s5-shen-pei-2674";
    public const string FenglveId = "ol:fenglve";
    public const string MoushiId = "guandu:moushi";
    public const string QushiId = "ol:qushi";
    public const string WeijieId = "ol:weijie";
    public const string GangzhiId = "ol:gangzhi";
    public const string JiemingId = "wushuang:skill-3083";
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public PackageManifest Manifest { get; } = new("shanhe-s5-moushi-boss", CurrentVersion,
        [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

    public void Register(IContentRegistryBuilder builder)
    {
        // Import only the two exact existing skill definitions, never their activity rosters.
        // Ordinary OL Moushi's older mark approximation is not the source's next actual Play
        // contract. Original Jieming refills to min(MaxHp, 5), unlike uncapped standard Jieming.
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("guandu-activity", MoushiId));
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("wushuang-skills-a", JiemingId));

        // S5 诸侯伐董 / chapter 11 河内 / boss 5118 / battle/group 2674 谋士争衡.
        // Source order: 460 荀谌 (1189/1190), 624 郭图 (3476/3477), 459 审配 (1185/93).
        // Each is raw 3/3, Draw2/Hand4/Slash1/Armor0, no base fixed cards or tactics.
        // Hard fixed cards 20721, 20722 and 20723 are excluded. Qun/Male and portraits
        // reuse the ordinary local general mappings. This Shenpei has Jieming, not Beizhan.
        Add(XunchenId, "荀谌", "ol-xun-chen", "character:xun-chen", FenglveId, MoushiId);
        Add(GuotuId, "郭图", "ol-guo-tu", "character:guo-tu", QushiId, WeijieId);
        Add(ShenpeiId, "审配", "ol-shen-pei", "character:shen-pei", GangzhiId, JiemingId);

        void Add(string id, string name, string portrait, string character, string primary, string additional) =>
            builder.AddGeneral(new(id, name, portrait, primary, "qun", 3, [additional])
            {
                InitialHp = 3, CharacterId = character, VariantId = "shanhe-s5-config-2674",
                RulesetId = "shanhe-s5-current-config", Gender = GeneralGender.Male
            });
    }
}
