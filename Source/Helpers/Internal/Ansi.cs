internal static class Ansi {

    public static string ErrorMessage(string msg) => $"\e[31m\e[1m{msg}\e[0m";
}