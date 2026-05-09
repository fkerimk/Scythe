using System.Text.RegularExpressions;
#if !SCYTHE_RUNTIME_BUILD
using Humanizer;
#endif

// ReSharper disable PossibleMultipleEnumeration
internal static class Generators {

    public static string AvailableName(string input, IEnumerable<string?> names) {

        var output = input = Regex.Replace(input, @"\s\d+$", "");

        var i = 0;

        while (names.Contains(output)) {

            i++;
            output = $"{input} {i}";
        }

        return output;
    }

    public static string SplitCamelCase(string input) {
        if (string.IsNullOrEmpty(input)) return input;
#if !SCYTHE_RUNTIME_BUILD
        return input.Replace("_", " ").Humanize(LetterCasing.Title);
#else
        return input.Replace("_", " ");
#endif
    }
}
