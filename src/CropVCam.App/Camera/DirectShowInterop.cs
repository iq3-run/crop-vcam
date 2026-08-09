using System.Runtime.InteropServices;

namespace CropVCam.App.Camera;

[ComImport]
[Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICreateDevEnum
{
    [PreserveSig]
    int CreateClassEnumerator(ref Guid deviceClass, out System.Runtime.InteropServices.ComTypes.IEnumMoniker? enumMoniker, int flags);
}

[ComImport]
[Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyBag
{
    [PreserveSig]
    int Read(
        [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
        [In, Out, MarshalAs(UnmanagedType.Struct)] ref object value,
        IntPtr errorLog);

    [PreserveSig]
    int Write(
        [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
        [In, MarshalAs(UnmanagedType.Struct)] ref object value);
}

// CLSID_SystemDeviceEnum: the coclass DirectShow uses to enumerate
// hardware/device categories (video capture devices among them).
[ComImport]
[Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")]
internal class SystemDeviceEnum
{
}

internal static class DirectShowCategories
{
    // CLSID_VideoInputDeviceCategory
    public static readonly Guid VideoInputDevice = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
}
