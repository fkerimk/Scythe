using System.Numerics;
using Raylib_cs;

internal static class Notifications {

    private const int X = 25;
    private const int Y = 25;
    private const int Spacing = 12;
    private const int Size = 18;
    private const float Fadeout = 0.3f;
    private const float EntryTime = 0.6f;

    private static readonly List<Notification> PendingNotifications = [];

    public static void Show(string text, float duration = 2.5f) => PendingNotifications.Add(new Notification(text, duration));

    public static void Draw() {

        if (PendingNotifications.Count == 0) return;

        var dt = Raylib.GetFrameTime();

        for (var i = PendingNotifications.Count - 1; i >= 0; i--) {

            var n = PendingNotifications[i];
            n.Timer += dt;

            // Stacking
            float targetY = Y + i * (Notification.Height + Spacing);
            n.DrawPosY = Raymath.Lerp(n.DrawPosY, targetY, dt * 10.0f);

            // Entry Animation
            var alpha = 1.0f;

            if (n.Timer < EntryTime) {

                var progress = n.Timer / EntryTime;
                n.DrawPosX = Raymath.Lerp(-n.Width, X, Ease.OutBack(progress));
            } else
                n.DrawPosX = Raymath.Lerp(n.DrawPosX, X, dt * 10.0f);

            // Exit Animation
            if (n.Timer > n.Duration) {

                var exitProgress = (n.Timer - n.Duration) / Fadeout;
                alpha = Math.Max(0, 1.0f - Ease.InCubic(exitProgress));
                n.DrawPosX += exitProgress * 200 * dt; // Slide away

                if (exitProgress >= 1.0f) {

                    PendingNotifications.RemoveAt(i);

                    continue;
                }
            }

            DrawNotification(n, alpha);
            PendingNotifications[i] = n;
        }
    }

    private static void DrawNotification(Notification n, float alpha) {

        var bg = new Color((byte)22, (byte)22, (byte)32, (byte)(248 * alpha));
        var border = new Color((byte)50, (byte)50, (byte)70, (byte)(150 * alpha));
        var accent = Colors.Primary;
        accent.A = (byte)(255 * alpha);
        var textCol = new Color((byte)230, (byte)230, (byte)245, (byte)(255 * alpha));

        var rect = new Rectangle(n.DrawPosX, n.DrawPosY, n.Width, Notification.Height);

        // Shadow
        Raylib.DrawRectangleRounded(new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), 0.25f, 8, new Color((byte)0, (byte)0, (byte)0, (byte)(120 * alpha)));

        // Main Body
        Raylib.DrawRectangleRounded(rect, 0.25f, 8, bg);
        Raylib.DrawRectangleRoundedLines(rect, 0.25f, 8, border);

        // Accent Bar
        Raylib.DrawRectangleRounded(new Rectangle(rect.X, rect.Y + 6, 3, rect.Height - 12), 1.0f, 4, accent);

        // Subtle Glow
        Raylib.DrawCircleV(new Vector2(rect.X + 2, rect.Y + rect.Height / 2), 6, Raylib.Fade(accent, 0.2f * alpha));

        Raylib.DrawTextEx(Fonts.RlMontserratRegular, n.Text, new Vector2(rect.X + 18, rect.Y + (rect.Height - Size) / 2 + 1), Size, 1.0f, textCol);
    }

    private class Notification {

        public const int Height = 42;
        public readonly int Width;

        public readonly string Text;
        public readonly float Duration;

        public float Timer;
        public float DrawPosX;
        public float DrawPosY;

        public Notification(string text, float duration) {

            Text = text;
            Duration = duration;
            Timer = 0;
            var size = Raylib.MeasureTextEx(Fonts.RlMontserratRegular, text, Size, 1.0f);
            Width = (int)size.X + 45;
            DrawPosX = -Width; // Start off-screen
            DrawPosY = Y + PendingNotifications.Count * (Height + Spacing);
        }
    }
}