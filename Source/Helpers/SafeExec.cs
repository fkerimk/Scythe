
public static class SafeExec {

    public static void Try(Action action) {

        try {
            action.Invoke();
        } catch (Exception) {

            //Console.WriteLine(e);
            //throw;
        }
    }
}