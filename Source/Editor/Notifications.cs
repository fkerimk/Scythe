using System.Numerics;
using Raylib_cs;

internal static class Notifications {

    private const int X = 25;
    private const int Y = 25;
    private const int Spacing = 12;
    private const int Size = 18;
    private const int TaskIconSize = 16;
    private const float Fadeout = 0.3f;
    private const float EntryTime = 0.6f;

    private static readonly List<Notification> PendingNotifications = [];

    public static void Show(string text, float duration = 2.5f) => PendingNotifications.Add(new Notification(text, duration));

    public static void ShowTask(BackgroundTask task) => PendingNotifications.Add(new Notification(task));

    public static void Draw() {

        if (PendingNotifications.Count == 0) return;

        var dt = Raylib.GetFrameTime();

        for (var i = PendingNotifications.Count - 1; i >= 0; i--) {

            var n = PendingNotifications[i];
            n.Timer += dt;

            if (n.Task != null) {

                n.Text = string.IsNullOrWhiteSpace(n.Task.Status) ? n.Task.Name : $"{n.Task.Name}: {n.Task.Status}";
                n.UpdateWidth();

                if (n.Task.IsDone && n.Duration < 0f) {

                    n.Duration = 2.5f;
                    n.Timer = 0f;
                }
            }

            // Stacking
            float targetY = Y + i * (n.Height + Spacing);
            n.DrawPosY = Raymath.Lerp(n.DrawPosY, targetY, dt * 10.0f);

            // Entry Animation
            var alpha = 1.0f;

            if (n.Timer < EntryTime) {

                var progress = n.Timer / EntryTime;
                n.DrawPosX = Raymath.Lerp(-n.Width, X, Ease.OutBack(progress));
            } else
                n.DrawPosX = Raymath.Lerp(n.DrawPosX, X, dt * 10.0f);

            // Exit Animation
            var shouldExit = n.Duration >= 0f && n.Timer > n.Duration;

            if (shouldExit) {

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
        var rect = new Rectangle(n.DrawPosX, n.DrawPosY, n.Width, n.Height);
        var iconX = rect.X + 18;
        var iconY = rect.Y + rect.Height / 2;

        // Shadow
        Raylib.DrawRectangleRounded(new Rectangle(rect.X + 3, rect.Y + 3, rect.Width, rect.Height), 0.25f, 8, new Color((byte)0, (byte)0, (byte)0, (byte)(120 * alpha)));

        // Main Body
        Raylib.DrawRectangleRounded(rect, 0.25f, 8, bg);
        Raylib.DrawRectangleRoundedLines(rect, 0.25f, 8, border);

        // Accent Bar
        Raylib.DrawRectangleRounded(new Rectangle(rect.X, rect.Y + 6, 3, rect.Height - 12), 1.0f, 4, accent);

        // Subtle Glow
        Raylib.DrawCircleV(new Vector2(rect.X + 2, rect.Y + rect.Height / 2), 6, Raylib.Fade(accent, 0.2f * alpha));

        if (n.Task is { IsDone: false })
            DrawTaskSpinner(new Vector2(iconX, iconY), alpha);
        else if (n.Task?.IsDone == true)
            Raylib.DrawCircleV(new Vector2(iconX, iconY), 5f, Raylib.Fade(accent, 0.9f * alpha));

        var textY = rect.Y + 10;
        Raylib.DrawTextEx(Fonts.RlMontserratRegular, n.Text, new Vector2(rect.X + 34, textY), Size, 1.0f, textCol);

        if (n.Task is { IsDone: false } task && task.Progress > 0f) {

            var barX = rect.X + 34;
            var barY = rect.Y + rect.Height - 12;
            var barWidth = rect.Width - 50;
            var progress = Math.Clamp(task.Progress, 0f, 1f);

            Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barWidth, 4), 1f, 4, new Color((byte)60, (byte)60, (byte)75, (byte)(200 * alpha)));
            Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barWidth * progress, 4), 1f, 4, accent);
        }
    }

    private static void DrawTaskSpinner(Vector2 center, float alpha) {

        var t = (float)Raylib.GetTime();
        var baseColor = new Color((byte)230, (byte)230, (byte)245, (byte)(220 * alpha));

        for (var i = 0; i < 8; i++) {

            var angle = t * 5f + i * MathF.PI * 0.25f;
            var radius = 6f;
            var dot = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var dotAlpha = (byte)(Math.Max(40, 255 - i * 26) * alpha);
            Raylib.DrawCircleV(dot, 1.8f, new Color(baseColor.R, baseColor.G, baseColor.B, dotAlpha));
        }
    }

    private class Notification {

        public int Height => Task == null || Task.IsDone || Task.Progress <= 0f ? 42 : 54;
        public int Width;

        public string Text;
        public float Duration;
        public readonly BackgroundTask? Task;

        public float Timer;
        public float DrawPosX;
        public float DrawPosY;

        public Notification(string text, float duration) {

            Text = text;
            Duration = duration;
            Timer = 0;
            Width = MeasureWidth(text, false);
            DrawPosX = -Width; // Start off-screen
            DrawPosY = Y + PendingNotifications.Count * (Height + Spacing);
        }

        public Notification(BackgroundTask task) {

            Task = task;
            Text = string.IsNullOrWhiteSpace(task.Status) ? task.Name : $"{task.Name}: {task.Status}";
            Duration = task.IsDone ? 2.5f : -1f;
            Timer = 0f;
            Width = MeasureWidth(Text, true);
            DrawPosX = -Width;
            DrawPosY = Y + PendingNotifications.Count * (Height + Spacing);
        }

        public void UpdateWidth() {

            var targetWidth = MeasureWidth(Text, Task != null);
            if (targetWidth > Width) Width = targetWidth;
        }

        private static int MeasureWidth(string text, bool hasTaskIcon) {

            var size = Raylib.MeasureTextEx(Fonts.RlMontserratRegular, text, Size, 1.0f);
            var basePadding = hasTaskIcon ? 64 : 45;
            var minWidth = hasTaskIcon ? 320 : 0;
            return (int)Math.Max(size.X + basePadding, minWidth);
        }
    }
}
