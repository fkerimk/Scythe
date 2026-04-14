using System.Collections.Concurrent;

internal class BackgroundTask {
    public string Name { get; set; } = "Task";
    public string Status { get; set; } = "Pending";
    public float Progress { get; set; } = 0f;
    public bool IsDone { get; set; } = false;
    public DateTime EndTime { get; set; }
}

internal static class Tasks {
    public static readonly List<BackgroundTask> ActiveTasks = [];
    public static readonly ConcurrentQueue<Action> MainThreadQueue = new();

    public static BackgroundTask Run(string name, Action<BackgroundTask> action) {
        var task = new BackgroundTask { Name = name, Status = "Working..." };
        lock (ActiveTasks) ActiveTasks.Add(task);
        
        Task.Run(() => {
            try {
                action(task);
            } catch (Exception e) {
                task.Status = "Error: " + e.Message;
            } finally {
                task.IsDone = true;
                task.EndTime = DateTime.Now;
            }
        });
        
        return task;
    }

    public static void RunOnMainThread(Action action) {
        MainThreadQueue.Enqueue(action);
    }

    public static void Update(int timeBudgetMs = 0) {
        var sw = timeBudgetMs > 0 ? System.Diagnostics.Stopwatch.StartNew() : null;
        while (MainThreadQueue.TryDequeue(out var action)) {
            SafeExec.Try(action);
            if (sw != null && sw.ElapsedMilliseconds > timeBudgetMs) break;
        }

        lock (ActiveTasks) {
            ActiveTasks.RemoveAll(t => t.IsDone && (DateTime.Now - t.EndTime).TotalSeconds > 3);
        }
    }
}
