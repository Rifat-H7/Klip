using System.Runtime.InteropServices;

[ComVisible(true)]
[Guid("3D8B0590-F691-11D2-8EA9-006097DF5BD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellAsyncOperation
{
    void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool doOperationAsync);

    void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool isOperationAsync);

    void StartOperation(IntPtr bindContext);

    void InOperation([MarshalAs(UnmanagedType.Bool)] out bool inAsyncOperation);

    void EndOperation(int result, IntPtr bindContext, uint effects);
}
