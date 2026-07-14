[SupportedOSPlatform("windows")]
sealed class StaClipboardWorker
{
    private readonly BlockingCollection<ClipboardWorkItem> _queue = [];

    public StaClipboardWorker()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Klip Clipboard STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Invoke(Action action)
    {
        using var done = new ManualResetEventSlim();
        var item = new ClipboardWorkItem(action, done);
        _queue.Add(item);
        done.Wait();

        if (item.Failure is not null)
        {
            throw item.Failure;
        }
    }

    private void Run()
    {
        var initialized = OleInitialize(IntPtr.Zero);
        if (initialized < 0)
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                item.Failure = new KlipException($"OleInitialize failed with HRESULT 0x{initialized:X8}.");
                item.Done.Set();
            }

            return;
        }

        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    item.Action();
                }
                catch (Exception ex)
                {
                    item.Failure = ex;
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }
        finally
        {
            OleUninitialize();
        }
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();
}

sealed class ClipboardWorkItem
{
    public ClipboardWorkItem(Action action, ManualResetEventSlim done)
    {
        Action = action;
        Done = done;
    }

    public Action Action { get; }

    public ManualResetEventSlim Done { get; }

    public Exception? Failure { get; set; }
}
