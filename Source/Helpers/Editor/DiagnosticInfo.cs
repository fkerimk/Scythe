internal struct DiagnosticInfo {

    public string Message;
    public int Severity; // 1: Error, 2: Warning, 3: Info, 4: Hint
    public int Line;
    public int StartChar;
    public int EndChar;
}