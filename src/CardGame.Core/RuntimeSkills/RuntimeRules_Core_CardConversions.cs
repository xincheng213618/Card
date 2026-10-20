namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardConversions
{
    internal static IReadOnlyList<IReadOnlyList<Card>> EnumerateCardCombinations(
        IReadOnlyList<Card> cards,
        int count)
    {
        var result = new List<IReadOnlyList<Card>>();
        var selected = new Card[count];
        void Visit(int sourceIndex, int selectedIndex)
        {
            if (selectedIndex == count)
            {
                result.Add(Array.AsReadOnly(selected.ToArray()));
                return;
            }
            for (var index = sourceIndex; index <= cards.Count - (count - selectedIndex); index++)
            {
                selected[selectedIndex] = cards[index];
                Visit(index + 1, selectedIndex + 1);
            }
        }
        Visit(0, 0);
        return result;
    }

    internal static void AddConversionParameters(
        IDictionary<string, string> parameters,
        CardConversionSource source)
    {
        parameters["conversion-skill-id"] = source.SkillId;
        parameters["conversion-binding-id"] = source.BindingId;
        parameters["conversion-owner-seat"] = source.OwnerSeat.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        parameters["conversion-instance-id"] = source.SkillInstanceId;
    }
}
