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
            Check("main maximize disabled",!form.MaximizeBox);
            Check("main uses application icon", form.Icon != null && form.Icon.Size == new Size(32,32));
            var mainLogo = Field<Panel>(form,"_header").Controls.OfType<PictureBox>().Single();
            Check("main logo uses high resolution artwork", mainLogo.Image != null && mainLogo.Image.Width == 1024);
            Check("embedded font family active",form.Font.FontFamily.Name=="WBM Mixed Sans");
            Check("embedded regular and bold faces",form.Font.FontFamily.IsStyleAvailable(FontStyle.Regular)&&form.Font.FontFamily.IsStyleAvailable(FontStyle.Bold));
            Check("initial scan is not an empty error",Field<Control>(form,"_empty").Visible && Field<Control>(form,"_empty").Text.Contains("正在查找"));
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
            Check("pending connection excluded from confirmed count",Field<Control>(form,"_count").Text.StartsWith("1 台") && Field<Control>(form,"_pending").Text=="1 台待确认");
            var texts=Descendants(form).OfType<Label>().Select(l=>l.Text).ToList();
            Check("stale reading retains success time",texts.Any(t=>t.Contains("上次读数 70%")&&t.Contains("10:12:34")));
            Check("uncertain connection explicit",texts.Any(t=>t.Contains("连接待确认")));
            Check("unknown level is dash",texts.Contains("—")&&texts.Any(t=>t.Contains("暂未获取电量")));
            Check("long name wraps",Descendants(form).OfType<Label>().Any(l=>l.Text==c.Name&&l.Height>28));
            Call(form,"UpdateSnapshot",new BatterySnapshot(new[]{stale,c},DateTime.Now,"蓝牙读取超时\r\n已保留有效结果。",false),settings);
            Check("partial error banner visible",Field<Panel>(form,"_notice").Visible);
            Capture(form,"main-partial-error");
            var failed=new BatterySnapshot(new[]{a},DateTime.Now,null,false).WithFailure("模拟意外刷新失败");
            Call(form,"UpdateSnapshot",failed,settings);Call(form,"SetBusy",false);
            Check("unexpected failure visible with retry",Field<Panel>(form,"_notice").Visible && Field<Control>(form,"_refreshButton").Enabled && Field<Control>(form,"_scan").Text.Contains("异常"));
            Check("unexpected failure retains cached device row",list.Controls.Count==1 && Field<Control>(form,"_pending").Text=="1 台待确认");
            settings.ShowDisconnected=false;
            var unknown=a.AsUnconfirmedCache();
            Update(form,settings,unknown,Reading("offline","Offline",15,false,true));
            Check("unknown row visible while disconnected hidden",list.Controls.Count==1 && Descendants(form).Any(l=>l.Text.Contains("连接状态未知")));
            Check("unknown row excluded from connected count",Field<Control>(form,"_count").Text.StartsWith("0 台") && Field<Control>(form,"_pending").Text=="1 台待确认");
            Capture(form,"main-unknown-link");
            settings.ShowDisconnected=true;
            Update(form,settings,a);
            Check("successful retry clears failure",!Field<Panel>(form,"_notice").Visible && !Field<Control>(form,"_pending").Visible && Field<Control>(form,"_scan").Text.Contains("完成"));

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
            float actualScale=(float)app.GetType("WirelessBatteryMoniter.UI.Theme").GetField("DpiScale").GetValue(null);
            form.ClientSize=new Size((int)(840*actualScale),(int)(720*actualScale));Update(form,settings,a,b,c);
            Check("wide view uses two columns",list.Controls[0].Top==list.Controls[1].Top && list.Controls[1].Left>list.Controls[0].Left);
            Capture(form,"main-two-columns");
            form.ClientSize=new Size((int)(420*actualScale),(int)(560*actualScale));Update(form,settings,a,b,c);
            Check("minimum view has no horizontal scroll",!list.HorizontalScroll.Visible);
            Check("minimum view uses one column",list.Controls[0].Left==list.Controls[1].Left && list.Controls[1].Top>list.Controls[0].Top);
            Check("name capped at two lines",Field<Label>(list.Controls[2],"_name").Height<=48*actualScale);
            Capture(form,"main-minimum");
            Update(form,settings,Reading("zero","零电量鼠标",0,true,false),Reading("full","满电鼠标",100,true,false));
            Check("zero and full values remain exact",Field<Label>(list.Controls[0],"_level").Text=="0" && Field<Label>(list.Controls[1],"_level").Text=="100");
            Capture(form,"main-zero-full");
            form.Close();Check("close hides instead of exiting",!form.IsDisposed&&!form.Visible);
        }
        using(var form=Create("AboutForm")) {
            form.Show();
            Check("about uses application icon", form.Icon != null && form.Icon.Size == new Size(32,32));
            Capture(form,"about-main-icon");
        }
        using(var form=Create("SettingsForm",settings,new Action<AppSettings>(s=>{}),new Action(()=>{}))) {
            Check("settings uses application icon", form.Icon != null && form.Icon.Size == new Size(32,32) && form.Icon.Handle != SystemIcons.Application.Handle);
            form.Show();Check("settings inputs remain inside scroller", new[]{"_pollSeconds","_threshold","_notifications","_sound","_startup","_startMinimized"}.All(n=> { var input=Field<Control>(form,n);return input.Left>=0 && input.Right<=input.Parent.ClientSize.Width; }));var notifications=Field<Control>(form,"_notifications");
            notifications.GetType().GetProperty("Checked").SetValue(notifications,false,null);
            Check("notifications off disables dependent controls",!Field<Control>(form,"_threshold").Enabled&&!Field<Control>(form,"_sound").Enabled&&!Field<Control>(form,"_testButton").Enabled);
            Check("disabled toggle exposed as unavailable",(Field<Control>(form,"_sound").AccessibilityObject.State & AccessibleStates.Unavailable)!=0);
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
            form.Show();Check("error maximize disabled",!form.MaximizeBox);var text=Descendants(form).OfType<TextBox>().Single();
            Check("error details selectable and read-only",text.ReadOnly&&text.Multiline);
            Check("error details includes copy action",Descendants(form).Any(c=>c.Text=="复制详情"));
            Capture(form,"error-details");
        }
        using(var form=Create("DeviceDetailsForm",Reading("details","完整设备名称 Wireless Mouse with a very long name",70,true,true))) {
            Check("device details uses application icon", form.Icon != null && form.Icon.Size == new Size(32,32) && form.Icon.Handle != SystemIcons.Application.Handle);
            form.Show();form.Opacity=1;Check("device maximize disabled",!form.MaximizeBox);
            var text=Descendants(form).OfType<TextBox>().Single();
            Check("device details retains full name and cached time",text.Text.Contains("完整设备名称 Wireless Mouse with a very long name")&&text.Text.Contains("10:12:34")&&text.Text.Contains("上次读数"));
            Check("device details takes content focus",text.Focused);
            Capture(form,"device-details");
        }
        var deviceSettings=new AppSettings();AppSettings savedDevices=null;bool failDeviceSave=true;
        deviceSettings.Devices.Add(new DevicePreference {Id="second",Hidden=true,Alias="隐藏鼠标",LowBatteryThreshold=10});
        var deviceList=new BatterySnapshot(new[]{Reading("first","原始鼠标",25,true,false),Reading("second","同名鼠标",20,true,false)},DateTime.Now,null,false);
        using(var form=Create("DevicePreferencesForm",deviceSettings,deviceList,new Action<AppSettings>(value=>{if(failDeviceSave)throw new IOException("模拟保存失败");savedDevices=value.Clone();}))) {
            Check("device preferences uses application icon", form.Icon != null && form.Icon.Size == new Size(32,32) && form.Icon.Handle != SystemIcons.Application.Handle);
            form.Show();form.Opacity=1;
            var selector=Field<ComboBox>(form,"_devices");
            int firstIndex=Enumerable.Range(0,selector.Items.Count).Single(i=>selector.Items[i].ToString().Contains("原始鼠标"));
            int secondIndex=1-firstIndex;selector.SelectedIndex=firstIndex;
            Field<TextBox>(form,"_alias").Text="办公鼠标";Field<CheckBox>(form,"_global").Checked=false;Field<NumericUpDown>(form,"_threshold").Value=35;
            selector.SelectedIndex=secondIndex;Field<CheckBox>(form,"_hidden").Checked=false;
            selector.SelectedIndex=firstIndex;
            Check("device editor retains independent edits on selection change",Field<TextBox>(form,"_alias").Text=="办公鼠标" && Field<NumericUpDown>(form,"_threshold").Value==35);
            Call(form,"SaveChanges");
            Check("device save failure remains visible with inputs",form.Visible && Field<Label>(form,"_error").Text.Contains("保存失败") && Field<TextBox>(form,"_alias").Text=="办公鼠标");
            Check("device edits do not mutate original before save",deviceSettings.PreferenceFor("first")==null && deviceSettings.IsHidden("second"));
            Capture(form,"device-preferences");
            failDeviceSave=false;Call(form,"SaveChanges");
            Check("device save retry stores alias threshold and unhides",savedDevices!=null && savedDevices.PreferenceFor("first").Alias=="办公鼠标" && savedDevices.ThresholdFor("first")==35 && !savedDevices.IsHidden("second"));
        }
        using(var form=Create("MainForm",new Action(()=>{}),new Func<Task>(()=>Task.FromResult(0)))) {
            form.Show();form.Opacity=1;
            savedDevices.PreferenceFor("second").Hidden=true;
            Update(form,savedDevices,deviceList.Devices.ToArray());
            Check("hidden preference removes card and summary",Field<FlowLayoutPanel>(form,"_list").Controls.Count==1 && Field<Control>(form,"_count").Text.StartsWith("1 台"));
            Check("device alias and custom threshold render",Descendants(form).Any(c=>c.Text=="办公鼠标") && Descendants(form).Any(c=>c.Text=="电量偏低"));
            savedDevices.PreferenceFor("second").Hidden=false;Update(form,savedDevices,deviceList.Devices.ToArray());
            Check("unhidden preference restores card",Field<FlowLayoutPanel>(form,"_list").Controls.Count==2);
        }
        int deviceSignals=0,suspends=0,resumes=0;
        var eventType=app.GetType("WirelessBatteryMoniter.App.SystemEventWindow");
        using(var events=(IDisposable)Activator.CreateInstance(eventType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{new Action(()=>deviceSignals++),new Action<bool>(suspended=>{if(suspended)suspends++;else resumes++;})},null)) {
            var process=eventType.GetMethod("ProcessSignal",BindingFlags.Instance|BindingFlags.NonPublic);
            process.Invoke(events,new object[]{0x219,7});process.Invoke(events,new object[]{0x219,0x8000});process.Invoke(events,new object[]{0x219,0x8004});
            process.Invoke(events,new object[]{0x219,99});process.Invoke(events,new object[]{0x218,4});process.Invoke(events,new object[]{0x218,7});process.Invoke(events,new object[]{0x218,18});
            Check("device and resume signals routed without unrelated messages",deviceSignals==3 && suspends==1 && resumes==2);
        }
        var charging = new DeviceBatteryInfo("charging", "充电中的鼠标", "2.4G", "", 10, true, false, new DateTime(2026,1,1,10,12,34), BatteryReadingSource.DeviceReported, BatteryChargeState.Charging);
        using(var form=Create("MainForm",new Action(()=>{}),new Func<Task>(()=>Task.FromResult(0)))) {
            form.Show();form.Opacity=1;Update(form,settings,charging);
            Check("charging card shows charge state before low battery",Descendants(form).Any(c=>c.Text=="正在充电") && !Descendants(form).Any(c=>c.Text=="电量很低"));
            Capture(form,"main-charging");
            Update(form,settings,charging.AsUnconfirmedCache());
            Check("stale charging card shows unknown connection",!Descendants(form).Any(c=>c.Text=="正在充电") && Descendants(form).Any(c=>c.Text.Contains("连接状态未知")));
        }
        using(var form=Create("DeviceDetailsForm",charging.AsUnconfirmedCache())) {
            form.Show();form.Opacity=1;
            Check("cached charging details label state as historical",Descendants(form).OfType<TextBox>().Single().Text.Contains("上次充电状态：正在充电"));
            Capture(form,"device-cached-charging");
        }
        var estimate = new DeviceBatteryInfo("estimate","估算电量鼠标", "2.4G", "", 100, true, false, new DateTime(2026,1,1,10,12,34), BatteryReadingSource.VoltageEstimate);
        using(var form=Create("DeviceDetailsForm",estimate.AsUnconfirmedCache())) {
            form.Show();form.Opacity=1;
            var text=Descendants(form).OfType<TextBox>().Single().Text;
            Check("estimated details expose source precision and cached value",text.Contains("约 100%") && text.Contains("电压曲线换算") && text.Contains("估算值") && text.Contains("10:12:34"));
            Capture(form,"device-estimated-details");
        }
        using(var form=Create("MainForm",new Action(()=>{}),new Func<Task>(()=>Task.FromResult(0)))) {
            form.Show();form.Opacity=1;
            Update(form,settings,estimate);
            var card=Field<FlowLayoutPanel>(form,"_list").Controls[0];
            var level=Field<Label>(card,"_level");
            Check("estimated card shows qualifier without clipping",level.Text=="约 100" && level.Width>=TextRenderer.MeasureText(level.Text,level.Font).Width);
            Check("estimated card accessibility keeps qualifier",card.AccessibleName.Contains("约 100%"));
            Capture(form,"main-estimated-reading");
        }
        using(var host=new Form()) {
            var card=(Control)Activator.CreateInstance(app.GetType("WirelessBatteryMoniter.UI.DeviceCard"),true);
            host.Controls.Add(card);card.Width=440;
            Call(card,"UpdateDevice",Reading("card","Keyboard accessible mouse",70,true,false),20);
            int invoked=0;card.GetType().GetEvent("OpenDetails").AddEventHandler(card,new EventHandler((s,e)=>invoked++));host.Show();
            Call(card,"OnKeyDown",new KeyEventArgs(Keys.Enter));Check("device card opens with Enter",invoked==1);
            card.AccessibilityObject.DoDefaultAction();Check("device card accessible default action",invoked==2);
            Call(card,"OnMouseClick",new MouseEventArgs(MouseButtons.Right,1,10,10,0));Check("right click does not open device details",invoked==2);
        }
        // Real disk-write failure, followed by retry, without touching user settings.
        string saveFolder=Path.Combine(output,"settings-save-failure");Directory.CreateDirectory(saveFolder);
        string settingsPath=Path.Combine(saveFolder,"settings.json");
        if(File.Exists(settingsPath))File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath); // A directory cannot be replaced with a settings file.
        var service=(WirelessBatteryMoniter.Services.SettingsService)Activator.CreateInstance(typeof(WirelessBatteryMoniter.Services.SettingsService),Hidden,null,new object[]{saveFolder},null);
        using(var form=Create("SettingsForm",settings,new Action<AppSettings>(value=>service.Save(value)),new Action(()=>{}))) {
            form.Show();var input=Field<Control>(form,"_pollSeconds");input.GetType().GetProperty("Value").SetValue(input,25,null);
            bool errorShown=false;
            using(var timer=new System.Windows.Forms.Timer {Interval=100}) {
                timer.Tick+=(sender,e)=> { foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(dialog.GetType().Name=="ErrorDetailsForm") {errorShown=Descendants(dialog).OfType<TextBox>().Any(t=>t.Text.Contains("保存设置失败")&&t.Text.Contains("settings.json"));dialog.Close();} };
                timer.Start();Call(form,"SaveAndClose");timer.Stop();
            }
            Check("disk save failure shown and settings stays open",errorShown && !form.IsDisposed && form.Visible && form.DialogResult!=DialogResult.OK);
            Check("failed save retains edited value",(int)input.GetType().GetProperty("Value").GetValue(input,null)==25);
            Directory.Delete(settingsPath);Call(form,"SaveAndClose");
            Check("save retry closes and persists value",form.DialogResult==DialogResult.OK && service.Load().PollSeconds==25);
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





