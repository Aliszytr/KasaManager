
using System;
using System.Globalization;

public class Program
{
    public static void Main()
    {
        string[] inputs = { "100.50", "100,50", "1.000,00", "1,000.00", "0", "0.00", "abc", "100 TL", " 50 " };
        foreach (var input in inputs)
        {
            if (TryParseDecimal(input, out var val))
            {
                Console.WriteLine($"'{input}' -> {val} (OK)");
            }
            else
            {
                Console.WriteLine($"'{input}' -> Fail");
            }
        }
    }

    private static bool TryParseDecimal(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        // TR/EN parse denemesi
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value)) return true;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;

        // Para birimi vb. temizle
        s = s.Replace("TL", "", StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value)
               || decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
