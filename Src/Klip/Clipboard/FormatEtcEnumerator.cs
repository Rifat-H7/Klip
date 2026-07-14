sealed class FormatEtcEnumerator : System.Runtime.InteropServices.ComTypes.IEnumFORMATETC
{
    private readonly System.Runtime.InteropServices.ComTypes.FORMATETC[] _formats;
    private int _index;

    public FormatEtcEnumerator(System.Runtime.InteropServices.ComTypes.FORMATETC[] formats)
    {
        _formats = formats;
    }

    public int Next(int celt, System.Runtime.InteropServices.ComTypes.FORMATETC[] rgelt, int[]? pceltFetched)
    {
        var fetched = 0;
        while (fetched < celt && _index < _formats.Length && fetched < rgelt.Length)
        {
            rgelt[fetched++] = _formats[_index++];
        }

        if (pceltFetched is { Length: > 0 })
        {
            pceltFetched[0] = fetched;
        }

        return fetched == celt ? 0 : 1;
    }

    public int Skip(int celt)
    {
        _index = Math.Min(_index + celt, _formats.Length);
        return _index < _formats.Length ? 0 : 1;
    }

    public int Reset()
    {
        _index = 0;
        return 0;
    }

    public void Clone(out System.Runtime.InteropServices.ComTypes.IEnumFORMATETC newEnum)
    {
        newEnum = new FormatEtcEnumerator(_formats) { _index = _index };
    }
}
