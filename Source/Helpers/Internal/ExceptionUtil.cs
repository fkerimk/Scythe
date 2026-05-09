using System.Diagnostics;

internal static class ExceptionUtil {
    public static string GetMessage(Exception exception) =>
        exception.Demystify().Message;
}
