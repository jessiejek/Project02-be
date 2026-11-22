//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.2: the key two names are compared by. Capitals, accents, spaces and punctuation do not matter ("De la Cruz", "dela cruz" and "Dela-Cruz" are the same).
using System.Globalization;
using System.Text;

namespace ClinicApp.Infrastructure.Sync;

public static class PersonNameKey
{
    public static string Of(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;   // accents
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
//JJQR - END
