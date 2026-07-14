sealed record TransferMetadata(string FileName, long Length, string Sha256)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new KlipException("File name is required.");
        }

        if (Length < 0)
        {
            throw new KlipException("File length cannot be negative.");
        }

        if (Sha256.Length != 64 || Sha256.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new KlipException("Metadata contains an invalid SHA-256 hash.");
        }
    }
}
