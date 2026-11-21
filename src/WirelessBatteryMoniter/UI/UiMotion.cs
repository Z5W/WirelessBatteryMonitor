using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace WirelessBatteryMoniter.UI
{
    internal sealed class UiMotion : IDisposable
    {
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action,uint parameter,out bool value,uint flags);
        private readonly Timer _timer=new Timer {Interval=15};
        private readonly Action _changed;
        private float _target;
        public float Value {get;private set;}
        public static bool Enabled {get {bool enabled;return SystemParametersInfo(0x1042,0,out enabled,0)&&enabled;}}
        public UiMotion(Action changed) {_changed=changed;_timer.Tick+=(s,e)=>{float step=15f/140;Value=_target>Value ? Math.Min(_target,Value+step) : Math.Max(_target,Value-step);_changed();if(Value==_target)_timer.Stop();};}
        public void Set(float target) {_target=target;if(!Enabled){Value=target;_changed();_timer.Stop();}else _timer.Start();}
        public static Color Blend(Color a,Color b,float amount) {return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));}
        public static void FadeIn(Form form) {
            if(!Enabled)return;
            form.Opacity=0.88;var timer=new Timer {Interval=15};int elapsed=0;
            timer.Tick+=(s,e)=>{if(form.IsDisposed){timer.Dispose();return;}elapsed+=15;form.Opacity=Math.Min(1,0.88+0.12*elapsed/180);if(elapsed>=180)timer.Dispose();};
            form.FormClosed+=(s,e)=>timer.Dispose();timer.Start();
        }
        public void Dispose() {_timer.Dispose();}
    }
}
