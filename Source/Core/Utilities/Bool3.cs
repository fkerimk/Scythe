internal struct Bool3(bool x, bool y, bool z) {

    public bool X = x;
    public bool Y = y;
    public bool Z = z;

    public override string ToString() => $"{X}, {Y}, {Z}";
}