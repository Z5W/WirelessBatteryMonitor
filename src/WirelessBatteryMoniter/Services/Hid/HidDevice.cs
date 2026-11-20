using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WirelessBatteryMoniter.Services.Hid
{
    internal sealed class HidDeviceInfo
    {
        public string Path;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputReportLength;
        public int OutputReportLength;
        public int FeatureReportLength;
        public string ProductName;
        public string Manufacturer;

        public string PhysicalKey
        {
            get
            {
                string path = Path ?? string.Empty;
                // Preserve VID/PID and receiver instance. Only collapse the HID
                // collection component; different receivers must never share a key.
                string[] parts = path.Split('#');
                if (parts.Length < 3) return path;
                if (Regex.IsMatch(parts[1], @"&col[0-9a-f]{2}", RegexOptions.IgnoreCase))
                {
                    parts[1] = Regex.Replace(parts[1], @"&col[0-9a-f]{2}", "", RegexOptions.IgnoreCase);
                    parts[2] = Regex.Replace(parts[2], @"&[0-9a-f]{4}$", "", RegexOptions.IgnoreCase);
                }
                return string.Join("#", parts, 0, 3);
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ProductName)) return ProductName.Trim();
                return string.Format("HID 2.4G 设备 ({0:X4}:{1:X4})", VendorId, ProductId);
            }
        }
    }

    internal static class HidEnumerator
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid guid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr handle, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetProductString(IntPtr handle, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(IntPtr handle, StringBuilder buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_Interface_List_SizeW(
            out uint length, ref Guid interfaceClassGuid, IntPtr deviceInstance, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_Interface_ListW(
            ref Guid interfaceClassGuid, IntPtr deviceInstance, [Out] char[] buffer, uint bufferLength, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;

        public static List<HidDeviceInfo> Enumerate()
        {
            List<HidDeviceInfo> devices = new List<HidDeviceInfo>();
            Guid guid;
            HidD_GetHidGuid(out guid);

            uint length;
            int result = CM_Get_Device_Interface_List_SizeW(out length, ref guid, IntPtr.Zero, 0);
            if (result != 0 || length == 0) return devices;

            char[] buffer = new char[length];
            result = CM_Get_Device_Interface_ListW(ref guid, IntPtr.Zero, buffer, length, 0);
            if (result != 0) return devices;

            List<string> paths = new List<string>();
            int start = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '\0') continue;
                if (i > start) paths.Add(new string(buffer, start, i - start));
                start = i + 1;
            }

            foreach (string path in paths)
            {
                HidDeviceInfo info = ReadInfo(path);
                if (info != null) devices.Add(info);
            }
            return devices;
        }

        private static HidDeviceInfo ReadInfo(string path)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == new IntPtr(-1)) return null;

            try
            {
                HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                attributes.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (!HidD_GetAttributes(handle, ref attributes)) return null;

                StringBuilder product = new StringBuilder(256);
                HidD_GetProductString(handle, product, product.Capacity * 2);
                StringBuilder manufacturer = new StringBuilder(256);
                HidD_GetManufacturerString(handle, manufacturer, manufacturer.Capacity * 2);

                IntPtr preparsed;
                if (!HidD_GetPreparsedData(handle, out preparsed)) return null;
                HIDP_CAPS caps;
                try
                {
                    if (HidP_GetCaps(preparsed, out caps) < 0) return null;
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }

                return new HidDeviceInfo
                {
                    Path = path,
                    VendorId = attributes.VendorID,
                    ProductId = attributes.ProductID,
                    UsagePage = caps.UsagePage,
                    Usage = caps.Usage,
                    InputReportLength = caps.InputReportByteLength,
                    OutputReportLength = caps.OutputReportByteLength,
                    FeatureReportLength = caps.FeatureReportByteLength,
                    ProductName = product.ToString(),
                    Manufacturer = manufacturer.ToString()
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>Overlapped HID read/write wrapper with per-operation timeouts.</summary>
    internal sealed class HidDevice : IDisposable
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint ERROR_IO_PENDING = 997;
        private const uint WAIT_OBJECT_0 = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            IntPtr handle, IntPtr buffer, int numberOfBytesToWrite, out int numberOfBytesWritten,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr handle, IntPtr buffer, int numberOfBytesToRead, out int numberOfBytesRead,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(
            IntPtr handle, IntPtr overlapped, out int numberOfBytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ResetEvent(IntPtr handle);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr handle, byte[] buffer, int bufferLength);

        private IntPtr _handle;
        private IntPtr _event;
        private bool _disposed;
        private readonly CancellationToken _cancellationToken;

        public HidDevice(string path, CancellationToken cancellationToken = default(CancellationToken))
        {
            _cancellationToken = cancellationToken;
            _handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (_handle == new IntPtr(-1))
            {
                throw new IOException("无法打开 HID 设备：" + path + "，错误码 " + Marshal.GetLastWin32Error());
            }
            _event = CreateEvent(IntPtr.Zero, true, false, null);
            if (_event == IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = new IntPtr(-1);
                throw new IOException("无法创建 HID 事件。");
            }
        }

        public bool TryWrite(byte[] data, int timeoutMs)
        {
            try
            {
                Write(data, timeoutMs);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return false;
            }
        }

        // Both the buffer and OVERLAPPED remain at fixed addresses until the
        // kernel has completed the request, including cancellation completion.
        private int Transfer(byte[] buffer, int timeoutMs, bool writing)
        {
            if (_disposed) throw new ObjectDisposedException("HidDevice");
            _cancellationToken.ThrowIfCancellationRequested();
            IntPtr operation = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(OVERLAPPED)));
            GCHandle pinned = default(GCHandle);
            bool pending = false;
            try
            {
                pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                Marshal.StructureToPtr(new OVERLAPPED { hEvent = _event }, operation, false);
                if (!ResetEvent(_event)) throw new IOException("HID event reset failed.");
                int transferred;
                bool ok = writing
                    ? WriteFile(_handle, pinned.AddrOfPinnedObject(), buffer.Length, out transferred, operation)
                    : ReadFile(_handle, pinned.AddrOfPinnedObject(), buffer.Length, out transferred, operation);
                if (!ok)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ERROR_IO_PENDING) throw new IOException("HID I/O failed: " + error);
                    pending = true;
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    while (true)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        int remaining = timeoutMs - (int)elapsed.ElapsedMilliseconds;
                        if (remaining <= 0) return -1;
                        uint wait = WaitForSingleObject(_event, (uint)Math.Min(remaining, 50));
                        if (wait == WAIT_OBJECT_0) break;
                        if (wait != 258) throw new IOException("HID wait failed: " + Marshal.GetLastWin32Error());
                    }
                    ok = GetOverlappedResult(_handle, operation, out transferred, false);
                    int resultError = Marshal.GetLastWin32Error();
                    if (!ok && resultError == 996) throw new IOException("HID operation still pending.");
                    pending = false;
                    if (!ok) throw new IOException("HID completion failed: " + resultError);
                }
                return transferred;
            }
            finally
            {
                if (pending)
                {
                    CancelIoEx(_handle, operation);
                    int ignored;
                    // Cancellation is a request, not completion. Never release
                    // memory while the driver may still access it.
                    GetOverlappedResult(_handle, operation, out ignored, true);
                }
                if (pinned.IsAllocated) pinned.Free();
                Marshal.FreeHGlobal(operation);
            }
        }

        public void Write(byte[] data, int timeoutMs)
        {
            int written = Transfer(data, timeoutMs, true);
            if (written != data.Length) throw new IOException("HID write timed out or was incomplete.");
        }

        public byte[] Read(int length, int timeoutMs)
        {
            byte[] buffer = new byte[Math.Max(1, length)];
            int read;
            try { read = Transfer(buffer, timeoutMs, false); }
            catch (IOException) { return null; }
            if (read <= 0) return null;
            if (read == buffer.Length) return buffer;
            byte[] result = new byte[read];
            Array.Copy(buffer, result, read);
            return result;
        }

        public byte[] GetFeature(int length)
        {
            if (_disposed) return null;
            byte[] buffer = new byte[Math.Max(1, length)];
            if (!HidD_GetFeature(_handle, buffer, buffer.Length)) return null;
            return buffer;
        }

        public bool SetFeature(byte[] data)
        {
            if (_disposed) return false;
            return HidD_SetFeature(_handle, data, data.Length);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1)) CloseHandle(_handle);
            if (_event != IntPtr.Zero) CloseHandle(_event);
            _handle = IntPtr.Zero;
            _event = IntPtr.Zero;
        }
    }
}
