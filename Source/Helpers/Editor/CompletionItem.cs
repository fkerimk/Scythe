internal struct CompletionItem {

    public string Label;
    public string InsertText;
    public int InsertTextFormat; // 1: Plain, 2: Snippet
    public int Kind;
}