using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;
namespace WirelessBatteryMoniter.UI
{
    internal sealed class DevicePreferencesForm : Form
    {
        private readonly ComboBox _devices = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Dock=DockStyle.Fill };
        private readonly TextBox _alias = new TextBox { MaxLength=64, Dock=DockStyle.Fill };
        private readonly CheckBox _hidden = new CheckBox { Text="隐藏此设备（同时停止提醒）",AutoSize=true };
        private readonly CheckBox _disabled = new CheckBox { Text="关闭此设备的低电量提醒",AutoSize=true };
        private readonly CheckBox _global = new CheckBox { Text="使用全局提醒阈值",AutoSize=true };
        private readonly NumericUpDown _threshold = new NumericUpDown { Minimum=5,Maximum=95,Width=100 };
        private readonly Label _error = new Label { AutoSize=true,ForeColor=Color.Firebrick,MaximumSize=new Size(440,0) };
        private readonly AppSettings _working;
        private readonly Action<AppSettings> _save;
        private string _selected;
        private sealed class Item { public string Id,Name; public override string ToString(){return Name;} }
        public DevicePreferencesForm(AppSettings settings, BatterySnapshot snapshot, Action<AppSettings> save)
        {
            _working=settings.Clone();_save=save;
            _devices.AccessibleName="选择设备";_alias.AccessibleName="设备别名";_threshold.AccessibleName="此设备提醒阈值";
            Text="逐设备设置";Font=Theme.Font(10.5f);BackColor=Theme.Background;ForeColor=Theme.Text;
            AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(500,460);MinimumSize=SizeFromClientSize(new Size(460,420));
            MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
            var panel=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,AutoScroll=true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var note=new Label { Text="按设备分别保存；隐藏设备仍可在这里恢复显示。",AutoSize=true,MaximumSize=new Size(440,0) };
            panel.Controls.Add(note);panel.Controls.Add(_devices);
            panel.Controls.Add(new Label {Text="显示名称（留空恢复原名）",AutoSize=true});panel.Controls.Add(_alias);
            panel.Controls.Add(_hidden);panel.Controls.Add(_disabled);panel.Controls.Add(_global);
            var thresholdRow=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            thresholdRow.Controls.Add(_threshold);thresholdRow.Controls.Add(new Label {Text="%",AutoSize=true});panel.Controls.Add(thresholdRow);
            panel.Controls.Add(_error);
            panel.RowCount=panel.Controls.Count;
            for(int row=0;row<panel.RowCount;row++)panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach(Control control in panel.Controls)control.Margin=new Padding(0,6,0,6);
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=64,Padding=new Padding(16),FlowDirection=FlowDirection.RightToLeft };
            var saveButton=new Button {Text="保存",AutoSize=true};var cancel=new Button {Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel};
            buttons.Controls.Add(saveButton);buttons.Controls.Add(cancel);Controls.Add(panel);Controls.Add(buttons);
            AcceptButton=saveButton;CancelButton=cancel;
            var known=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
            foreach(var d in snapshot.Devices) known[d.Id]=new Item {Id=d.Id,Name=d.OriginalName+" — "+d.Address+" ["+ShortId(d.Id)+"]"};
            foreach(var p in _working.Devices) if(!known.ContainsKey(p.Id)) known[p.Id]=new Item {Id=p.Id,Name=(string.IsNullOrEmpty(p.Alias)?"历史设备":p.Alias)+" ["+ShortId(p.Id)+"]"};
            foreach(var item in known.Values) _devices.Items.Add(item);
            _devices.SelectedIndexChanged+=(s,e)=>SelectDevice();
            _global.CheckedChanged+=(s,e)=>_threshold.Enabled=!_global.Checked;
            saveButton.Click+=(s,e)=>SaveChanges();
            if(_devices.Items.Count>0)_devices.SelectedIndex=0;
            else { note.Text="暂未发现设备，首次读取后可在此设置。";saveButton.Enabled=false;_alias.Enabled=_hidden.Enabled=_disabled.Enabled=_global.Enabled=_threshold.Enabled=false; }
        }
        private static string ShortId(string id){return id.Length<=24?id:id.Substring(id.Length-24);}
        private void StoreCurrent()
        {
            if(_selected==null)return;
            var p=_working.PreferenceFor(_selected);
            if(p==null){p=new DevicePreference {Id=_selected};_working.Devices.Add(p);}
            p.Alias=_alias.Text;p.Hidden=_hidden.Checked;p.DisableNotifications=_disabled.Checked;
            p.LowBatteryThreshold=_global.Checked?(int?)null:(int)_threshold.Value;p.Normalize();
        }
        private void SelectDevice()
        {
            StoreCurrent();var item=_devices.SelectedItem as Item;if(item==null)return;_selected=item.Id;
            var p=_working.PreferenceFor(item.Id) ?? new DevicePreference();
            _alias.Text=p.Alias ?? "";_hidden.Checked=p.Hidden;_disabled.Checked=p.DisableNotifications;
            _global.Checked=!p.LowBatteryThreshold.HasValue;_threshold.Value=p.LowBatteryThreshold ?? _working.LowBatteryThreshold;
            _threshold.Enabled=!_global.Checked;
        }
        private void SaveChanges()
        {
            StoreCurrent();_working.Normalize();
            try {_save(_working);DialogResult=DialogResult.OK;Close();}
            catch(Exception ex){_error.Text="保存失败，请重试："+ex.Message;}
        }
    }
}
