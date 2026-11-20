// Offline UI regression harness. Never constructs a tray icon or changes user settings.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;
class UiRegression
{
    static Assembly app;
    static string output;
    static int checks;
    const BindingFlags Hidden = BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    [STAThread] static int Main(string[] args)
    {
        try {
            app=typeof(AppSettings).Assembly;output=args[1];Directory.CreateDirectory(output);
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>app;
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Run();Console.WriteLine("PASS "+checks+" UI checks");return 0;
        } catch(Exception ex) { Console.WriteLine(ex);return 1; }
    }
    static Form Create(string name,params object[] args) { return (Form)Activator.CreateInstance(app.GetType("WirelessBatteryMoniter.UI."+name),Hidden,null,args,null); }
    static T Field<T>(object obj,string name) { return (T)obj.GetType().GetField(name,Hidden).GetValue(obj); }
    static void Call(object obj,string method,params object[] args) { obj.GetType().GetMethod(method,Hidden).Invoke(obj,args); }
    static void Check(string name,bool result) { if(!result) throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);checks++; }
    static DeviceBatteryInfo Reading(string id,string name,int? level,bool online,bool stale) { return new DeviceBatteryInfo(id,name,"2.4G","",level,online,stale,new DateTime(2026,11,20,10,12,34)); }
    static void Update(Form f,AppSettings settings,params DeviceBatteryInfo[] devices) { Call(f,"UpdateSnapshot",new BatterySnapshot(devices,DateTime.Now,null,false),settings);Application.DoEvents(); }
    static void Capture(Form form,string file) { Application.DoEvents();using(var b=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(output,file+".png")); } }
    static IEnumerable<Control> Descendants(Control c) { foreach(Control child in c.Controls) { yield return child;foreach(var sub in Descendants(child)) yield return sub; } }
    static void Run()
    {
        var settings=new AppSettings { ShowDisconnected=true };
        using(var form=Create("MainForm",new Action(()=>{}),new Func<Task>(()=>Task.FromResult(0)))) {
            form.Show();
            var a=Reading("a","PRO X2 SUPERSTRIKE",70,true,false);
            var b=Reading("b","VXE NordicMouse 1K Dongle",15,true,false);
            var c=Reading("c","XINMENG 超长设备名称 — Wireless Gaming Mouse with a very long descriptive model name",null,true,false);
            Update(form,settings,a,b,c);
            var list=Field<FlowLayoutPanel>(form,"_list");
            var first=list.Controls[0];
            Check("initial low battery first", ((DeviceBatteryInfo)first.GetType().GetProperty("Device").GetValue(first,null)).Id=="b");
            Capture(form,"main-three");
            Update(form,settings,Reading("a",a.Name,5,true,false),Reading("b",b.Name,90,true,false),c);
            Check("refresh reuses rows and preserves order",ReferenceEquals(first,list.Controls[0]));
            var stale=Reading("a",a.Name,70,true,true);
            Update(form,settings,stale,Reading("b",b.Name,15,false,true),c);
            var texts=Descendants(form).OfType<Label>().Select(l=>l.Text).ToList();
            Check("stale reading retains success time",texts.Any(t=>t.Contains("上次读数 70%")&&t.Contains("10:12:34")));
            Check("uncertain connection explicit",texts.Any(t=>t.Contains("连接待确认")));
            Check("unknown level is dash",texts.Contains("—")&&texts.Any(t=>t.Contains("暂未获取电量")));
            Check("long name wraps",Descendants(form).OfType<Label>().Any(l=>l.Text==c.Name&&l.Height>28));
            Call(form,"UpdateSnapshot",new BatterySnapshot(new[]{stale,c},DateTime.Now,"蓝牙读取超时\r\n已保留有效结果。",false),settings);
            Check("partial error banner visible",Field<Panel>(form,"_notice").Visible);
            Capture(form,"main-partial-error");
            var many=Enumerable.Range(0,12).Select(i=>Reading(i.ToString(),"同名鼠标",20+i,true,false)).ToArray();
            Update(form,settings,many);
            Check("same names with distinct IDs preserved",list.Controls.Count==12);
            Check("many rows scroll vertically",list.VerticalScroll.Visible);
            var footer=Field<Panel>(form,"_footer");var footerBounds=footer.Bounds;
            list.AutoScrollPosition=new Point(0,300);Application.DoEvents();
            Check("footer stays fixed when scrolling",footer.Bounds==footerBounds);
            Capture(form,"main-many");
            Update(form,settings,a);Capture(form,"main-one");
            Call(form,"SetBusy",true);
            Check("busy disables refresh",!Field<Control>(form,"_refreshButton").Enabled);
            Call(form,"SetBusy",false);
            Update(form,settings,new DeviceBatteryInfo[0]);
            Check("empty state offers rescan",Field<Control>(form,"_empty").Visible&&Field<Control>(form,"_refreshButton").Text=="重新扫描");
            Capture(form,"main-empty");
            form.ClientSize=new Size(700,800);Update(form,settings,a,b,c);Capture(form,"main-wide");
            Check("resize keeps rows inside viewport",list.Controls.Cast<Control>().All(r=>r.Width<=list.ClientSize.Width));
            form.Close();Check("close hides instead of exiting",!form.IsDisposed&&!form.Visible);
        }
        using(var form=Create("SettingsForm",settings,new Action<AppSettings>(s=>{}),new Action(()=>{}))) {
            form.Show();Check("settings inputs remain inside scroller", new[]{"_pollSeconds","_threshold","_notifications","_sound","_startup","_startMinimized"}.All(n=> { var input=Field<Control>(form,n);return input.Left>=0 && input.Right<=input.Parent.ClientSize.Width; }));var notifications=Field<Control>(form,"_notifications");
            notifications.GetType().GetProperty("Checked").SetValue(notifications,false,null);
            Check("notifications off disables dependent controls",!Field<Control>(form,"_threshold").Enabled&&!Field<Control>(form,"_sound").Enabled&&!Field<Control>(form,"_testButton").Enabled);
            Capture(form,"settings-disabled");
            notifications.GetType().GetProperty("Checked").SetValue(notifications,true,null);
            var number=Field<Control>(form,"_pollSeconds");int value=(int)number.GetType().GetProperty("Value").GetValue(number,null);
            Call(number,"OnKeyDown",new KeyEventArgs(Keys.Right));
            Check("stepper keyboard arrow increments",(int)number.GetType().GetProperty("Value").GetValue(number,null)==value+1);
            var button=Field<Control>(form,"_testButton");int clicked=0;button.Click+=(s,e)=>clicked++;
            button.Focus();SendMessage(button.Handle,0x201,new IntPtr(1),new IntPtr(0x00100010));SendMessage(button.Handle,0x202,IntPtr.Zero,new IntPtr(0x00100010));
            Check("native left click fires exactly once",clicked==1);
            SendMessage(button.Handle,0x204,new IntPtr(2),new IntPtr(0x00100010));SendMessage(button.Handle,0x205,IntPtr.Zero,new IntPtr(0x00100010));
            Check("right click does not activate button",clicked==1);
            Call(button,"OnKeyDown",new KeyEventArgs(Keys.Space));Check("space activates once",clicked==2);
            Capture(form,"settings-focus");
        }
        using(var form=Create("ErrorDetailsForm","蓝牙读取超时\r\n设备连接待确认。\r\n"+new string('x',1500))) {
            form.Show();var text=Descendants(form).OfType<TextBox>().Single();
            Check("error details selectable and read-only",text.ReadOnly&&text.Multiline);
            Check("error details includes copy action",Descendants(form).Any(c=>c.Text=="复制详情"));
            Capture(form,"error-details");
        }
        // Geometry stress tests only: actual desktop DPI remains unchanged.
        // Scale bounds and fonts together, then let row layout measure the new text.
        var dpiField=app.GetType("WirelessBatteryMoniter.UI.Theme").GetField("DpiScale",BindingFlags.Static|BindingFlags.Public);
        float nativeScale=(float)dpiField.GetValue(null);
        foreach(float target in new[]{1f,1.5f,2f}) {
            foreach(string kind in new[]{"MainForm","SettingsForm"}) {
                using(var form=kind=="MainForm" ? Create(kind,new Action(()=>{}),new Func<Task>(()=>Task.FromResult(0))) : Create(kind,settings,new Action<AppSettings>(s=>{}),new Action(()=>{}))) {
                    form.Show();
                    if(kind=="MainForm") Update(form,settings,Reading("long","超长名称 Wireless Mouse PRO SUPERSTRIKE Edition for testing wrapping",70,true,false),Reading("stale","缓存设备",15,false,true));
                    var controls=Descendants(form).ToList();
                    var fonts=controls.Select(x=>new Font(x.Font.FontFamily,x.Font.Size*target/nativeScale,x.Font.Style)).ToList();
                    form.AutoScaleMode=AutoScaleMode.None;
                    dpiField.SetValue(null,target);
                    form.Scale(new SizeF(target/nativeScale,target/nativeScale));
                    for(int i=0;i<controls.Count;i++) controls[i].Font=fonts[i];
                    foreach(var control in controls) control.PerformLayout();
                    if(kind=="MainForm") Call(form,"ResizeRows");
                    Application.DoEvents();
                    if(kind=="SettingsForm") Check("settings simulated "+target+" scale bounds",new[]{"_pollSeconds","_threshold","_notifications","_sound"}.All(n=> {var input=Field<Control>(form,n);return input.Left>=0&&input.Right<=input.Parent.ClientSize.Width;}));
                    else Check("main simulated "+target+" scale rows",Field<FlowLayoutPanel>(form,"_list").Controls.Cast<Control>().All(row=>row.Controls.Cast<Control>().All(child=>child.Right<=row.Width && child.Bottom<=row.Height)));
                    Capture(form,"simulated-"+(int)(target*100)+"-"+kind);
                    form.Hide();
                    foreach(var font in fonts) font.Dispose();
                }
                dpiField.SetValue(null,nativeScale);
            }
        }    }
}
