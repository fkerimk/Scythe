internal static class SortUtil {

    public static int NaturalCompare(string? x, string? y) {

        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var xIndex = 0;
        var yIndex = 0;

        while (xIndex < x.Length && yIndex < y.Length) {

            var xChar = x[xIndex];
            var yChar = y[yIndex];
            var xDigit = char.IsDigit(xChar);
            var yDigit = char.IsDigit(yChar);

            if (xDigit && yDigit) {

                var xStart = xIndex;
                var yStart = yIndex;

                while (xIndex < x.Length && char.IsDigit(x[xIndex])) xIndex++;
                while (yIndex < y.Length && char.IsDigit(y[yIndex])) yIndex++;

                var xNumber = x.AsSpan(xStart, xIndex - xStart).TrimStart('0');
                var yNumber = y.AsSpan(yStart, yIndex - yStart).TrimStart('0');

                if (xNumber.Length != yNumber.Length)
                    return xNumber.Length.CompareTo(yNumber.Length);

                var numericCompare = xNumber.SequenceCompareTo(yNumber);
                if (numericCompare != 0) return numericCompare;

                var rawLengthCompare = (xIndex - xStart).CompareTo(yIndex - yStart);
                if (rawLengthCompare != 0) return rawLengthCompare;

                continue;
            }

            var charCompare = char.ToUpperInvariant(xChar).CompareTo(char.ToUpperInvariant(yChar));
            if (charCompare != 0) return charCompare;

            xIndex++;
            yIndex++;
        }

        return x.Length.CompareTo(y.Length);
    }
}
