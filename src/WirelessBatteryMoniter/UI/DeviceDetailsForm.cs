using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;
namespace WirelessBatteryMoniter.UI
{
    internal sealed class DeviceDetailsForm : Form
    {
        public DeviceDetailsForm(DeviceBatteryInfo device) {
            MaximizeBox=false;
            SuspendLayout();Text="设备详情";AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(480,420);MinimumSize=SizeFromClientSize(new Size(420,320));StartPosition=FormStartPosition.CenterParent;
            ShowInTaskbar=false;MinimizeBox=false;BackColor=Theme.Background;Font=Theme.Font(10.5f);ForeColor=Theme.Text;
            var header=new Panel { Width=480,Height=72,Dock=DockStyle.Top };
            MainForm.AddLabel(header,"设备详情",20,20,400,32,15,Theme.Text,FontStyle.Bold);
            var footer=new Panel { Width=480,Height=72,Dock=DockStyle.Bottom };
            var close=MainForm.Button(footer,"关闭",360,16,100,40);close.Accent=true;close.Anchor=AnchorStyles.Top|AnchorStyles.Right;close.Click+=(s,e)=>Close();
            var body=new RoundedPanel { Dock=DockStyle.Fill,Padding=new Padding(20) };
            var content=new StringBuilder();content.AppendLine(device.Name).AppendLine();
            content.AppendLine("设备类型："+TrayTextFormatter.DeviceType(device.OriginalName));content.AppendLine("连接方式："+device.Kind);
            content.AppendLine("原始名称："+device.OriginalName);
            content.AppendLine("活动状态："+device.ActivityStateText);
            content.AppendLine("状态："+device.StateText);
            content.AppendLine(device.HasLevel ? (device.IsStale||!device.IsConnected ? "上次读数：" : "电量：")+device.LevelText : "暂未获取电量");
            content.AppendLine((device.IsStale || !device.IsConnected ? "上次充电状态：" : "充电状态：")+device.ChargeStateText);
            content.AppendLine("读数来源："+device.ReadingSourceText);
            content.AppendLine("读数精度："+device.ReadingPrecisionText);
            if(device.HasLevel)content.AppendLine("最近成功读取："+device.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"));
            var text=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=Theme.Surface,ForeColor=Theme.Text,Font=Theme.Font(10.5f),Text=content.ToString(),AccessibleName="设备完整信息" };
            body.Controls.Add(text);Controls.Add(body);Controls.Add(header);Controls.Add(footer);
            KeyPreview=true;KeyDown+=(s,e)=> {if(e.KeyCode==Keys.Escape)Close();};
            AutoScaleDimensions=new SizeF(96,96);ResumeLayout(true);
            Shown+=(s,e)=> {text.Focus();text.Select(0,0);UiMotion.FadeIn(this);};
        }
    }
}


