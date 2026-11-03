using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace CodexLocal {
public static class RingIcon {
    public static BitmapSource Draw(int size,double? percentage,double rotation){
        var visual=new DrawingVisual();double scale=size/64.0;
        using(var context=visual.RenderOpen()){
            context.PushTransform(new TranslateTransform(-size*0.04,-size*0.04));context.PushTransform(new ScaleTransform(scale*1.08,scale*1.08));
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(220,22,28,40)),null,new Point(32,32),24,24);
            context.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(100,152,169,192)),5),new Point(32,32),26,26);
            var color=percentage.HasValue?UI.QuotaColor(percentage.Value):new SolidColorBrush(Color.FromRgb(61,189,150));
            double sweep=percentage.HasValue?Math.Min(359.99,Math.Max(0,percentage.Value)*3.6):359.99;
            double start=-90+rotation,end=start+sweep;
            if(sweep>0){var path=new StreamGeometry();using(var figure=path.Open()){
                var a=new Point(32+26*Math.Cos(start*Math.PI/180),32+26*Math.Sin(start*Math.PI/180));var b=new Point(32+26*Math.Cos(end*Math.PI/180),32+26*Math.Sin(end*Math.PI/180));
                figure.BeginFigure(a,false,false);figure.ArcTo(b,new Size(26,26),0,sweep>180,SweepDirection.Clockwise,true,false);
            }context.DrawGeometry(null,new Pen(color,5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},path);}
            var glyph=new FormattedText(">_",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Cascadia Code, Consolas"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),21,Brushes.White,1.0);
            context.DrawText(glyph,new Point((64-glyph.Width)/2,19));context.Pop();
        }
        var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
    }
    public static byte[] Png(BitmapSource image){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var memory=new MemoryStream()){encoder.Save(memory);return memory.ToArray();}}
    public static byte[] Ico(double? percentage,double rotation){
        int[] sizes={16,20,24,32,40,48,64,128,256};var frames=sizes.Select(n=>Png(Draw(n,percentage,rotation))).ToArray();
        using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+sizes.Length*16;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}
            foreach(var frame in frames)writer.Write(frame);return memory.ToArray();
        }
    }
}
public partial class UI {

    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr kind,IntPtr icon);
    System.Drawing.Icon taskbarIcon;
    void SetTaskbarIcon(double? percent){
        Window.Icon=RingIcon.Draw(48,percent,0);
        using(var memory=new MemoryStream(RingIcon.Ico(percent,0)))using(var source=new System.Drawing.Icon(memory,new System.Drawing.Size(48,48))){var old=taskbarIcon;taskbarIcon=(System.Drawing.Icon)source.Clone();var hwnd=new System.Windows.Interop.WindowInteropHelper(Window).Handle;if(hwnd!=IntPtr.Zero){SendMessage(hwnd,0x80,new IntPtr(1),taskbarIcon.Handle);SendMessage(hwnd,0x80,IntPtr.Zero,taskbarIcon.Handle);}if(old!=null)old.Dispose();}
    }
    void InitializeQuotaIcon(){Window.SourceInitialized+=(s,e)=>SetTaskbarIcon(null);if(new System.Windows.Interop.WindowInteropHelper(Window).Handle!=IntPtr.Zero)SetTaskbarIcon(null);Window.Closed+=(s,e)=>{if(taskbarIcon!=null)taskbarIcon.Dispose();};}
    System.Drawing.Icon lastIcon;double? lastPercent;
    void UpdateQuotaIcon(){}
    void UpdateLastQuotaIcon(string home,QuotaSnapshot snapshot){
        if(home.Equals(engine.Active,StringComparison.OrdinalIgnoreCase)||tray==null)return;
        var match=System.Text.RegularExpressions.Regex.Match(snapshot.Value,@"5 小时剩余 ([\d.]+)%");double percent;if(!match.Success||!Double.TryParse(match.Groups[1].Value,out percent)||lastPercent==percent)return;
        using(var memory=new MemoryStream(RingIcon.Ico(percent,0)))using(var icon=new System.Drawing.Icon(memory,new System.Drawing.Size(32,32))){var old=lastIcon;lastIcon=(System.Drawing.Icon)icon.Clone();tray.Icon=lastIcon;if(old!=null)old.Dispose();}
        SetTaskbarIcon(percent);lastPercent=percent;
    }
    bool CursorInside(ContextMenu menu){
        try{var cursor=System.Windows.Forms.Cursor.Position;var point=menu.PointFromScreen(new Point(cursor.X,cursor.Y));return point.X>=0&&point.Y>=0&&point.X<menu.ActualWidth&&point.Y<menu.ActualHeight;}catch{return false;}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    void AttachMenuDismiss(ContextMenu menu){
        DateTime? outside=null;bool left=false,right=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
        timer.Tick+=(s,e)=>{bool l=(GetAsyncKeyState(1)&0x8000)!=0,r=(GetAsyncKeyState(2)&0x8000)!=0;if(!CursorInside(menu)&&((l&&!left)||(r&&!right))){menu.IsOpen=false;return;}left=l;right=r;if(CursorInside(menu))outside=null;else{if(!outside.HasValue)outside=DateTime.UtcNow;if((DateTime.UtcNow-outside.Value).TotalSeconds>=1.5)menu.IsOpen=false;}};
        EventHandler deactivate=(s,e)=>{if(menu.IsOpen)menu.IsOpen=false;};
        menu.Opened+=(s,e)=>{outside=DateTime.UtcNow;timer.Start();Window.Deactivated+=deactivate;};
        menu.Closed+=(s,e)=>{timer.Stop();Window.Deactivated-=deactivate;};menu.StaysOpen=false;
    }
    bool SwitchNotice(string message,string report){
        bool retry=false;var dialog=StyledDialog("切换暂未完成",650,500);var frame=DialogFrame(dialog,"账户迁移");var body=new DockPanel{Margin=new Thickness(26,12,26,26)};frame.Children.Add(body);
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        var close=new Button{Content="稍后处理",Margin=new Thickness(0,0,8,0)};var check=new Button{Content="重新检查并继续",Style=(Style)Window.Resources["Primary"]};footer.Children.Add(close);footer.Children.Add(check);close.Click+=(s,e)=>dialog.Close();check.Click+=(s,e)=>{retry=true;dialog.Close();};
        var stack=new StackPanel();body.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});stack.Children.Add(new TextBlock{Text=report.Contains("Codex 桌面版")?"请先退出 Codex 桌面版":message.Contains("共享")?"配置文件正被占用":"本次切换已暂停",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});stack.Children.Add(new TextBlock{Text=message,Style=(Style)Window.Resources["Muted"],Margin=new Thickness(0,0,0,18)});
        foreach(string row in report.Split('\n')){
            var line=new DockPanel{Margin=new Thickness(0,4,0,4)};var match=System.Text.RegularExpressions.Regex.Match(row,@"PID (\d+)");
            if(match.Success){int pid=Int32.Parse(match.Groups[1].Value);DateTime started=DateTime.MinValue;try{using(var proc=System.Diagnostics.Process.GetProcessById(pid))started=proc.StartTime;}catch{}
                var end=new Button{Content="结束任务",Padding=new Thickness(10,5,10,5),Margin=new Thickness(8,0,0,0),ToolTip="结束此进程，未保存的工作可能丢失"};DockPanel.SetDock(end,Dock.Right);line.Children.Add(end);
                bool elevate=false;end.Click+=(sender,args)=>{try{using(var proc=System.Diagnostics.Process.GetProcessById(pid)){if(started==DateTime.MinValue||proc.StartTime!=started)throw new IOException("进程已变化，请重新检查");if(elevate){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),"/PID "+pid+" /F"){UseShellExecute=true,Verb="runas",WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});}else {proc.Kill();if(!proc.WaitForExit(2000))throw new IOException("进程尚未退出，请稍后重新检查");}}end.IsEnabled=false;end.Content="已请求结束";}catch(System.ComponentModel.Win32Exception){elevate=true;end.Content="以管理员权限结束";}catch(Exception error){end.Content="未结束";line.ToolTip=error.Message;}};
            }
            line.Children.Add(new TextBlock{Text=row,TextWrapping=TextWrapping.Wrap,FontSize=12,VerticalAlignment=VerticalAlignment.Center});stack.Children.Add(new Border{Style=(Style)Window.Resources["Card"],Child=line});
        }dialog.ShowDialog();return retry;
    }
}
}
