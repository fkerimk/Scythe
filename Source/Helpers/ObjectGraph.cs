using Force.DeepCloner;
using KellermanSoftware.CompareNetObjects;

internal static class ObjectGraph {
    private static readonly CompareLogic Comparer = new(new ComparisonConfig {
        MaxDifferences = 1,
        IgnoreCollectionOrder = false
    });

    public static bool AreEqual(object? left, object? right) =>
        Comparer.Compare(left, right).AreEqual;

    public static T DeepClone<T>(T value) =>
        value.DeepClone();
}
