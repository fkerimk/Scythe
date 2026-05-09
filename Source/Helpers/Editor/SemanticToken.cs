using Raylib_cs;

internal struct SemanticToken {

    public int Line;
    public int StartChar;
    public int Length;
    public int Type;
}

internal static partial class Extensions {

    public static Color GetLspColor(this SemanticToken token) => token.Type switch {

        0 or 8 or 9 or 20       => new Color(130, 200, 255, 255),
        1 or 2 or 5 or 12 or 13 => new Color(255, 210, 100, 255),
        15                      => new Color(255, 120, 180, 255),
        17                      => new Color(130, 130, 140, 255),
        18                      => new Color(160, 255, 120, 255),
        19                      => new Color(210, 170, 255, 255),
        7                       => new Color(180, 255, 240, 255),
        _                       => Color.LightGray
    };
}