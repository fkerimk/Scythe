using System.Diagnostics;

internal static class ExceptionUtil {
    public static string GetMessage(Exception exception) =>
#if !SCYTHE_RUNTIME_BUILD
        exception.Demystify().Message;
#else
        exception.Message;
#endif
}
