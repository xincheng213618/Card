using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_LateFixedTargetDamageCapScopes
{
    internal static string FixedDamageCapChainHash(IReadOnlyList<int> targets) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(",", targets.Select(seat => seat.ToString(CultureInfo.InvariantCulture)))))).ToLowerInvariant();
}
