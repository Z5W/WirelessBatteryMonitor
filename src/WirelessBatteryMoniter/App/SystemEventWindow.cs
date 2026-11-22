using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace WirelessBatteryMoniter.App
{
    // Hidden top-level window receives device/power broadcasts even with no main UI.
    internal sealed class SystemEventWindow : NativeWindow, IDisposable
    {
        private readonly Action _deviceChanged;
        private readonly Action<bool> _power;
        private IntPtr _registration;
        [StructLayout(LayoutKind.Sequential)] private struct DeviceInterface
        { public int Size,Type,Reserved; public Guid ClassGuid; public short Name; }
        [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr RegisterDeviceNotification(IntPtr recipient,ref DeviceInterface filter,uint flags);
        [DllImport("user32.dll")] private static extern bool UnregisterDeviceNotification(IntPtr handle);
        public SystemEventWindow(Action deviceChanged, Action<bool> power)
        {
            _deviceChanged=deviceChanged;_power=power;
            CreateHandle(new CreateParams {Caption="WirelessBatteryMoniter.Events",Style=0});
            var filter=new DeviceInterface {Size=Marshal.SizeOf(typeof(DeviceInterface)),Type=5,ClassGuid=new Guid("4D1E55B2-F16F-11CF-88CB-001111000030")};
            _registration=RegisterDeviceNotification(Handle,ref filter,0);
        }
        internal static bool IsDeviceChange(int message, int code)
        {return message==0x219 && (code==7 || code==0x8000 || code==0x8004);}
        internal void ProcessSignal(int message,int code)
        {
            if(IsDeviceChange(message,code))_deviceChanged();
            if(message==0x218 && code==4)_power(true);
            if(message==0x218 && (code==7 || code==18))_power(false);
        }
        protected override void WndProc(ref Message m)
        {ProcessSignal(m.Msg,m.WParam.ToInt32());base.WndProc(ref m);}
        public void Dispose(){if(_registration!=IntPtr.Zero)UnregisterDeviceNotification(_registration);_registration=IntPtr.Zero;DestroyHandle();}
    }
}
