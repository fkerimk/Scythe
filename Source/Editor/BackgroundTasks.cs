using System.Numerics;
using ImGuiNET;

internal class BackgroundTasks : Viewport {
    public BackgroundTasks() : base("Background Tasks") {}

    protected override void OnDraw() {
        lock (Tasks.ActiveTasks) {
            if (Tasks.ActiveTasks.Count == 0) {
                ImGui.TextDisabled("No active tasks.");
                return;
            }

            foreach (var task in Tasks.ActiveTasks) {
                ImGui.Text(task.Name);
                ImGui.SameLine();
                if (task.IsDone)
                    ImGui.TextDisabled(task.Status);
                else
                    ImGui.TextDisabled(task.Status); // can animate ...

                if (task.Progress > 0f && !task.IsDone) {
                    ImGui.ProgressBar(task.Progress, new Vector2(-1, 0));
                }
            }
        }
    }
}
