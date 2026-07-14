sealed class TransferStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<string>> _transfers = [];

    public string Add(List<string> sourcePaths)
    {
        var transferId = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _transfers[transferId] = sourcePaths;
        }

        return transferId;
    }

    public string ResolvePath(string transferId, int index)
    {
        lock (_gate)
        {
            if (!_transfers.TryGetValue(transferId, out var paths))
            {
                throw new KlipException("Transfer is no longer available.");
            }

            if (index < 0 || index >= paths.Count)
            {
                throw new KlipException("Transfer requested an invalid file index.");
            }

            return paths[index];
        }
    }
}
