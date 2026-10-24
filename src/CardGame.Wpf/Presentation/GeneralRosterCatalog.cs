namespace CardGame.Wpf.Presentation;

// Broad browse categories for the current prompt, independent of gameplay eligibility.
public static class GeneralRosterCatalog
{
    public static IReadOnlyList<GeneralRosterCategory> Categories { get; } = Array.AsReadOnly<GeneralRosterCategory>(
    [
        new("all", "全部"), new("boundary", "界"), new("mou", "谋"),
        new("magic", "魔"), new("god", "神"), new("le", "乐"),
        new("wumiao", "武庙"), new("friend", "友"), new("shi", "势"), new("sp", "SP"),
        new("standard", "标准"), new("myth", "神话再临"), new("fame", "一将成名"),
        new("ol", "OL扩展"), new("mobile", "移动版"), new("national", "国战"), new("other", "其他")
    ]);

    public static string Classify(string generalId)
    {
        var prefix = generalId.Split(':', 2)[0];
        if (prefix is "standard" or "boundary" or "mou" or "magic" or "le" or "wumiao" or "friend" or "national")
            return prefix;
        var series = GeneralGalleryCatalog.Classify(generalId).SeriesId;
        return series switch
        {
            "boundary-fame" => "boundary",
            "other" => prefix is "ol" or "mobile" ? prefix : "other",
            _ => series
        };
    }
}

public sealed record GeneralRosterCategory(string Id, string Name);
