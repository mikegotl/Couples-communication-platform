using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Couples.Application.Sms;

/// <summary>Reads a phone number as typed into E.164 form. Deliberately simple: no carrier lookup.</summary>
public static class PhoneNumber
{
    /// <summary>
    /// "+44 7700 900123" → "+447700900123"; "(555) 123-4567" with calling code 1 → "+15551234567".
    /// Spaces, dots, dashes and brackets are ignored; anything else fails.
    /// </summary>
    public static bool TryNormalize(string? input, string defaultCallingCode, [NotNullWhen(true)] out string? e164)
    {
        e164 = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
        {
            return false;
        }

        var text = input.Trim();
        var international = text.StartsWith('+');
        if (!international && text.StartsWith("00", StringComparison.Ordinal))
        {
            international = true;
            text = text[2..];
        }

        var digits = new StringBuilder();
        foreach (var c in international && text.StartsWith('+') ? text[1..] : text)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return false;
            }
        }

        var number = digits.ToString();
        if (!international)
        {
            // National format: drop a single trunk 0 (as in 07700 900123), then add the default code.
            if (number.StartsWith('0'))
            {
                number = number[1..];
            }

            number = defaultCallingCode + number;
        }

        if (number.Length is < 8 or > 15 || number[0] == '0')
        {
            return false;
        }

        e164 = "+" + number;
        return true;
    }

    /// <summary>"+15551234567" → "•••• 4567". For screens; logs never get the number at all.</summary>
    public static string Mask(string e164) => e164.Length <= 4 ? "••••" : "•••• " + e164[^4..];
}
