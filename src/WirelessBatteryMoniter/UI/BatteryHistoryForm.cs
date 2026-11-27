using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class BatteryHistoryForm : Form
    {
        private readonly BatteryHistoryService _history;
        private readonly AppSettings _settings;
        private readonly ComboBox _devices=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=250};
        private readonly ComboBox _range=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=150};
        private readonly Label _status=new Label {Dock=DockStyle.Bottom,Height=54,AutoEllipsis=true};
        private readonly DataGridView _grid=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        private readonly HistoryPlot _plot=new HistoryPlot {Dock=DockStyle.Top,Height=210};
        private List<BatteryHistoryPoint> _all;
        private sealed class Choice { public string Id; public string Name; public override string ToString() { return Name; } }
        public BatteryHistoryForm(BatteryHistoryService history,AppSettings settings)
        {
            _history=history;_settings=settings;
            Text="电量历史";Icon=AppIcon.Load(32);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);ClientSize=new Size(760,560);MinimumSize=new Size(640,480);
            StartPosition=FormStartPosition.CenterParent;Font=Theme.Font(10);BackColor=Theme.Background;
            var top=new FlowLayoutPanel {Dock=DockStyle.Top,Height=50,Padding=new Padding(10)};
            var refresh=new Button {Text="刷新 / 重试",AutoSize=true};
            _range.Items.AddRange(new object[]{"最近 24 小时","最近 7 天"});_range.SelectedIndex=1;
            top.Controls.AddRange(new Control[]{_devices,_range,refresh});
            _grid.Columns.Add("time","读取时间");_grid.Columns.Add("level","电量");_grid.Columns.Add("source","来源");_grid.Columns.Add("charge","充电状态");
            _grid.BackgroundColor=Theme.Surface;_grid.BorderStyle=BorderStyle.None;
            _grid.ColumnHeadersHeight=36;_grid.RowTemplate.Height=30;
            _grid.DefaultCellStyle.BackColor=Theme.Surface;_grid.DefaultCellStyle.ForeColor=Theme.Text;
            _grid.EnableHeadersVisualStyles=false;_grid.ColumnHeadersDefaultCellStyle.BackColor=Theme.SurfaceRaised;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor=Theme.Text;
            Controls.Add(_grid);Controls.Add(_plot);Controls.Add(top);Controls.Add(_status);
            _devices.SelectedIndexChanged+=(s,e)=>Render();_range.SelectedIndexChanged+=(s,e)=>Render();
            refresh.Click+=async (s,e)=> {refresh.Enabled=false;try {await System.Threading.Tasks.Task.Run(()=>_history.RetrySave());if(!IsDisposed)Reload();}finally {if(!IsDisposed)refresh.Enabled=true;}};
            Reload();
        }
        private void Reload()
        {
            var selected=_devices.SelectedItem as Choice;
            _all=_history.Read();_devices.Items.Clear();
            foreach(var group in _all.GroupBy(p=>p.Id))
            {
                var preference=_settings.PreferenceFor(group.Key);
                _devices.Items.Add(new Choice {Id=group.Key,Name=preference!=null && !string.IsNullOrWhiteSpace(preference.Alias) ? preference.Alias : group.Last().Name});
            }
            var previous=_devices.Items.Cast<Choice>().FirstOrDefault(c=>selected!=null && c.Id==selected.Id);
            if(previous!=null)_devices.SelectedItem=previous;else if(_devices.Items.Count>0)_devices.SelectedIndex=0;
            Render();
        }
        private void Render()
        {
            if(_all==null)return;
            var choice=_devices.SelectedItem as Choice;
            DateTime end=DateTime.UtcNow,start=end.AddDays(_range.SelectedIndex==0 ? -1 : -7);
            var points=_all.Where(p=>choice!=null && p.Id==choice.Id && p.Utc>=start && p.Utc<=end).OrderBy(p=>p.Utc).ToList();
            _plot.SetPoints(points,start,end);_grid.Rows.Clear();
            foreach(var p in points.AsEnumerable().Reverse().Take(2000))
            {
                var reading=new DeviceBatteryInfo(p.Id,p.Name,"","",p.Level,true,false,p.Utc,p.Source,p.Charge);
                _grid.Rows.Add(p.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss"),p.LevelText,reading.ReadingSourceText,reading.ChargeStateText);
            }
            _status.Text=_history.Error ?? (points.Count==0 ? "暂无历史记录。连接设备并读取有效电量后开始记录。" : points.Count+" 个记录点；明细最多显示最近 2000 条。约 = 估算值。蓝色 = 充电。断档不补值，不预测续航。");
            _status.ForeColor=_history.Error==null ? Theme.TextMuted : Theme.Red;
        }
        private sealed class HistoryPlot : Control
        {
            private List<BatteryHistoryPoint> _points=new List<BatteryHistoryPoint>();
            private DateTime _start,_end;
            internal HistoryPlot() { DoubleBuffered=true; }
            internal void SetPoints(List<BatteryHistoryPoint> points,DateTime start,DateTime end) { _points=points;_start=start;_end=end;Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);if(_end<=_start)return;
                var area=new RectangleF(44,15,Math.Max(1,Width-64),Math.Max(1,Height-50));
                using(var grid=new Pen(Theme.TextFaint))using(var text=new SolidBrush(Theme.TextMuted))
                {
                    foreach(int level in new[]{0,50,100}) {float y=area.Bottom-area.Height*level/100f;e.Graphics.DrawLine(grid,area.Left,y,area.Right,y);e.Graphics.DrawString(level+"%",Font,text,0,y-8);}
                    e.Graphics.DrawString(_start.ToLocalTime().ToString("MM-dd HH:mm"),Font,text,area.Left,area.Bottom+6);
                    e.Graphics.DrawString(_end.ToLocalTime().ToString("MM-dd HH:mm"),Font,text,Math.Max(area.Left,area.Right-105),area.Bottom+6);
                }
                PointF previous=new PointF();BatteryHistoryPoint last=null;
                foreach(var point in _points)
                {
                    var pos=new PointF(area.Left+(float)((point.Utc-_start).TotalSeconds/(_end-_start).TotalSeconds)*area.Width,area.Bottom-area.Height*point.Level/100f);
                    Color color=point.Charge==BatteryChargeState.Charging ? Theme.Accent : Theme.Green;
                    using(var pen=new Pen(color,2))using(var brush=new SolidBrush(color))
                    {
                        if(last!=null && last.Session==point.Session && last.Source==point.Source && point.Utc-last.Utc<=TimeSpan.FromMinutes(15))e.Graphics.DrawLine(pen,previous,pos);
                        e.Graphics.FillEllipse(brush,pos.X-2,pos.Y-2,4,4);
                    }
                    last=point;previous=pos;
                }
            }
        }
    }
}
