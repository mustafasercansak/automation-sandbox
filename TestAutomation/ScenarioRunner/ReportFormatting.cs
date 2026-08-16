using System.Globalization;

namespace ScenarioRunner
{
    // Number formatting for anything that ends up in a report - a JSON artifact, a
    // `$GITHUB_STEP_SUMMARY` table, console output.
    //
    // Standard numeric format strings follow the *ambient* culture, which makes report text
    // depend on the machine that produced it. `{fraction:P1}` renders three different ways:
    //
    //   en-US            "40.0%"
    //   InvariantCulture "40.0 %"     <- note the space
    //   tr-TR            "%40,0"      <- symbol first, comma decimal separator
    //
    // That is not cosmetic here. The Windows survey (#64) compares reports produced on two
    // different runner images, so a locale difference between them would surface as a diff that
    // is really a formatting artifact. It also broke a test that passed locally on en-US and
    // failed on the Linux CI leg, which runs invariant.
    //
    // These helpers pin the culture explicitly and avoid the percent format specifier entirely,
    // so the symbol position, spacing and decimal separator are all fixed. `HealingReportHtmlRenderer`
    // already formats its numbers this way; this is the same rule, shared.
    internal static class ReportFormatting
    {
        // 0.4 -> "40.0%" on every machine, whatever the ambient culture.
        public static string Percent(double fraction, int decimals = 1) =>
            (fraction * 100.0).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + "%";

        // Same for a value that is already a percentage rather than a fraction.
        public static string PercentOfTotal(int part, int total, int decimals = 0) =>
            total <= 0 ? "-" : Percent((double)part / total, decimals);

        public static string Number(double value, int decimals = 2) =>
            value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        // Provider error bodies can be several hundred characters of JSON. One long message must not
        // push the rest of a summary table off the screen.
        public static string Truncate(string value, int maxLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength) + "…";
    }
}
