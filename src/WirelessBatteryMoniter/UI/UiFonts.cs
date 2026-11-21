using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
namespace WirelessBatteryMoniter.UI
{
    // App-private OFL derivative: Source Han Sans non-Latin + Noto Sans CJK Latin.
    // Both GDI (native controls/TextRenderer) and GDI+ (tray drawing) need registration.
    internal static class UiFonts
    {
        private static readonly PrivateFontCollection Collection=new PrivateFontCollection();
        private static readonly List<IntPtr> Buffers=new List<IntPtr>();
        private static readonly List<IntPtr> Handles=new List<IntPtr>();
        public static readonly FontFamily Family;
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr data,uint size,IntPtr reserved,ref uint count);
        [DllImport("gdi32.dll")] private static extern bool RemoveFontMemResourceEx(IntPtr handle);
        static UiFonts() {
            foreach(string style in new[]{"Regular","Bold"}) {
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("WirelessBatteryMoniter.Fonts.WBMMixedSans-"+style+".ttf")) {
                    if(stream==null)throw new InvalidOperationException("Missing embedded UI font: "+style);
                    byte[] bytes=new byte[stream.Length];int offset=0,read;
                    while(offset<bytes.Length && (read=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=read;
                    if(offset!=bytes.Length)throw new EndOfStreamException("Incomplete embedded font");
                    IntPtr buffer=Marshal.AllocHGlobal(bytes.Length);Marshal.Copy(bytes,0,buffer,bytes.Length);Buffers.Add(buffer);
                    uint count=0;IntPtr handle=AddFontMemResourceEx(buffer,(uint)bytes.Length,IntPtr.Zero,ref count);
                    if(handle==IntPtr.Zero)throw new InvalidOperationException("Unable to load UI font");
                    Handles.Add(handle);Collection.AddMemoryFont(buffer,bytes.Length);
                }
            }
            Family=Collection.Families[0];
            AppDomain.CurrentDomain.ProcessExit+=(s,e)=>{Collection.Dispose();foreach(var handle in Handles)RemoveFontMemResourceEx(handle);foreach(var buffer in Buffers)Marshal.FreeHGlobal(buffer);};
        }
    }
}
