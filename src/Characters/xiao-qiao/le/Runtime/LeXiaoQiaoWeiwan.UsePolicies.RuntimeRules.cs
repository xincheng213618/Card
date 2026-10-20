namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_xiao_qiao_le_LeXiaoQiaoWeiwan_UsePolicies
{
    internal static bool WeiwanDeclarationIsTrueUse(CardDeclarationParentReturn parent) => parent.Purpose is
        CardDeclarationPurpose.Use or CardDeclarationPurpose.Dodge or CardDeclarationPurpose.Counterspell or
        CardDeclarationPurpose.Recovery or CardDeclarationPurpose.BorrowedSword or CardDeclarationPurpose.ProvidedSlash or
        CardDeclarationPurpose.BorrowedSwordProvidedSlash or CardDeclarationPurpose.QinglongProvidedSlash;
}
