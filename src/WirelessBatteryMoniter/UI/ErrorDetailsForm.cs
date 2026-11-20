using System;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
namespace WirelessBatteryMoniter.UI
{
    internal sealed class ErrorDetailsForm : Form
    {
        public ErrorDetailsForm(string details)
        {
            SuspendLayout();
            Text="读取详情";AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(560,420);MinimumSize=new Size(440,300);StartPosition=FormStartPosition.CenterParent;
            BackColor=Theme.Background;ForeColor=Theme.Text;Font=Theme.Font(10.5f);ShowInTaskbar=false;MinimizeBox=false;
            var header=new Panel { Width=560,Dock=DockStyle.Top,Height=80 };
            MainForm.AddLabel(header,"读取详情",24,16,440,28,15,Theme.Text,FontStyle.Bold);
            MainForm.AddLabel(header,"已保留可用结果；可复制以下信息用于排查。",24,48,500,24,10.5f,Theme.TextMuted,FontStyle.Regular);
            var footer=new Panel { Width=560,Dock=DockStyle.Bottom,Height=72,BackColor=Theme.Surface };
            var copy=MainForm.Button(footer,"复制详情",24,16,120,40);
            copy.Click+=(s,e)=> { try { Clipboard.SetText(details ?? "没有错误详情。");copy.Text="已复制"; } catch(System.Runtime.InteropServices.ExternalException) { copy.Text="请重试复制"; } };
            var close=MainForm.Button(footer,"关闭",416,16,120,40);close.Anchor=AnchorStyles.Top|AnchorStyles.Right;close.Click+=(s,e)=>Close();
            var body=new Panel { Dock=DockStyle.Fill,Padding=new Padding(24,8,24,16) };
            var text=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Text=details ?? "没有错误详情。",BackColor=Theme.Surface,ForeColor=Theme.Text,BorderStyle=BorderStyle.None,AccessibleName="错误详情",Font=Theme.Font(10.5f) };
            body.Controls.Add(text);Controls.Add(body);Controls.Add(header);Controls.Add(footer);
            KeyPreview=true;KeyDown+=(s,e)=> { if(e.KeyCode==Keys.Escape) Close(); };
            AutoScaleDimensions = new SizeF(96,96); ResumeLayout(true);
            Shown+=(s,e)=>NativeMethods.ApplyDarkWindow(Handle);
        }
    }
}
