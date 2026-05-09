using System.Text.RegularExpressions;
using Raylib_cs;

internal struct Token {

    public string Text;
    public TokenType Type;
}

internal static partial class Extensions {

    private static readonly string[] TokenKeywords = ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"];

    public static Color GetColor(this Token token) => token.Type switch {

        TokenType.Keyword => new Color(255, 120, 180, 255),
        TokenType.String  => new Color(160, 255, 120, 255),
        TokenType.Number  => new Color(120, 220, 255, 255),
        TokenType.Comment => new Color(120, 120, 130, 255),

        _ => Color.LightGray
    };

    public static List<Token> Tokenize(this string line) {

        var tokens = new List<Token>();
        var matches = Regex.Matches(line, """(--.*)|("[^"]*")|('[^']*')|(\b\d+\.?\d*\b)|(\b\w+\b)|(\s+)|(.)""");

        foreach (Match m in matches) {

            var t = m.Value;
            var type = t.StartsWith("--") ? TokenType.Comment : t.StartsWith('\"') || t.StartsWith('\'') ? TokenType.String : char.IsDigit(t[0]) ? TokenType.Number : TokenKeywords.Contains(t) ? TokenType.Keyword : string.IsNullOrWhiteSpace(t) ? TokenType.Whitespace : TokenType.Normal;

            tokens.Add(new Token { Text = t, Type = type });
        }

        return tokens;
    }
}