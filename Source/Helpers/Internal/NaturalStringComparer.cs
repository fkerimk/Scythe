internal class NaturalStringComparer : IComparer<string> {

    public int Compare(string? x, string? y) => SortUtil.NaturalCompare(x, y);
}