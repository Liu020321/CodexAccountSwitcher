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
    bool SwitchNotice(Exception failure,string report){
        string message=failure.Message;bool locked=TransferFailure.IsLock(failure),permission=TransferFailure.IsPermission(failure);
        bool retry=false;var dialog=StyledDialog("切换暂未完成",650,500);var frame=DialogFrame(dialog,"账户迁移");var body=new DockPanel{Margin=new Thickness(26,12,26,26)};frame.Children.Add(body);
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        var close=new Button{Content="稍后处理",Margin=new Thickness(0,0,8,0)};var check=new Button{Content="重新检查并继续",Style=(Style)Window.Resources["Primary"]};footer.Children.Add(close);footer.Children.Add(check);close.Click+=(s,e)=>dialog.Close();check.Click+=(s,e)=>{retry=true;dialog.Close();};
        var stack=new StackPanel();body.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});stack.Children.Add(new TextBlock{Text=report.Contains("Codex 桌面版")?"请先退出 Codex 桌面版":locked?"文件被其他程序锁定":permission?"无法访问配置文件":"本次切换已暂停",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});stack.Children.Add(new TextBlock{Text=message,Style=(Style)Window.Resources["Muted"],Margin=new Thickness(0,0,0,18)});
        bool official=(locked||permission)&&FileLocksmith.FindUI()!=null;
        if(official){
            var locksmith=new Button{Content="使用 PowerToys 检查占用",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};stack.Children.Add(locksmith);
            var feedback=new TextBlock{Text="在官方 File Locksmith 窗口查看占用、结束任务，或以管理员身份重新扫描。处理后回到这里点击“重新检查并继续”。访问被拒绝也可能由只读属性或权限引起。",TextWrapping=TextWrapping.Wrap,Foreground=ThemeBrush("Text"),FontSize=12};stack.Children.Add(feedback);
            locksmith.Click+=(sender,args)=>{try{using(var opened=FileLocksmith.Open(FileLocksmith.Targets(message,engine.LockRoots()))){}feedback.Text="已打开 PowerToys File Locksmith。结束相关进程后，请回到这里重新检查。";}catch(Exception error){feedback.Text="打开失败："+error.Message;}};
        }
        if(!locked&&!permission)stack.Children.Add(new TextBlock{
            Text="本次是校验或目录状态问题，并未据此认定有进程占用。重试会更新尚未清理源文件的中转副本；若已开始清理，则继续严格校验并保留恢复副本。",
            TextWrapping=TextWrapping.Wrap,Foreground=ThemeBrush("Text"),FontSize=12});
        foreach(string row in (official?new string[0]:report.Split('\n'))){
            var line=new DockPanel{Margin=new Thickness(0,4,0,4)};var match=System.Text.RegularExpressions.Regex.Match(row,@"PID (\d+)");
            if(match.Success){int pid=Int32.Parse(match.Groups[1].Value);long identity=0;string identityError="";
                try{using(var proc=System.Diagnostics.Process.GetProcessById(pid))identity=proc.StartTime.ToUniversalTime().Ticks;}catch(Exception error){identityError=error.Message;}
                var end=new Button{Content="结束任务",Padding=new Thickness(10,5,10,5),Margin=new Thickness(8,0,0,0),ToolTip="结束此进程，未保存的工作可能丢失"};DockPanel.SetDock(end,Dock.Right);line.Children.Add(end);
                var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,0),Foreground=ThemeBrush("Text")};
                DockPanel.SetDock(status,Dock.Bottom);line.Children.Add(status);
                bool elevate=false;
                if(identity==0){end.IsEnabled=false;end.Content="请重新检查";status.Text="无法确认此进程："+identityError;}
                end.Click+=async(sender,args)=>{
                    end.IsEnabled=false;end.Content="正在结束…";status.Text="正在等待进程退出…";
                    try{
                        bool useAdmin=elevate;await System.Threading.Tasks.Task.Run(()=>{if(useAdmin)ProcessControl.StopElevated(pid,identity);else ProcessControl.Stop(pid,identity);});
                        end.Content="已结束";status.Text="原进程已退出。请点击“重新检查并继续”重新验证迁移条件。";
                    }catch(System.ComponentModel.Win32Exception error){
                        end.IsEnabled=true;
                        if(error.NativeErrorCode==5){elevate=true;end.Content="以管理员权限结束";status.Text="权限不足。点击右侧按钮，确认 Windows 授权后再结束此进程。";}
                        else{end.Content=elevate?"以管理员权限重试":"重试结束";status.Text=(error.NativeErrorCode==1223?"已取消管理员授权，进程未结束。": "结束失败："+error.Message+"（"+error.NativeErrorCode+"）");}
                    }catch(Exception error){end.IsEnabled=true;end.Content="重试结束";status.Text="结束失败："+error.Message;}
                };
            }
            line.Children.Add(new TextBlock{Text=row,TextWrapping=TextWrapping.Wrap,FontSize=12,VerticalAlignment=VerticalAlignment.Center});stack.Children.Add(new Border{Style=(Style)Window.Resources["Card"],Child=line});
        }dialog.ShowDialog();return retry;
    }
}
}
