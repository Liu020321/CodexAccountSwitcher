using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Forms=System.Windows.Forms;

namespace CodexLocal {
public class ProfileSettings {
    public string Parent;
    public string[] Folders={"life","work"};
    public string[] Names={"生活账户","工作账户"};
    public static string DetectParent(string selected){
        selected=Path.GetFullPath(selected);
        if(Path.GetFileName(selected).Equals(".codex",StringComparison.OrdinalIgnoreCase))selected=Directory.GetParent(selected).FullName;
        var children=Directory.GetDirectories(selected).Where(d=>!Path.GetFileName(d).StartsWith("."));
        if(children.Any(d=>Engine.Keys.Contains(Path.GetFileName(d))||File.Exists(Path.Combine(d,"auth.json"))||File.Exists(Path.Combine(d,".codex","auth.json"))))return selected;
        var parent=Directory.GetParent(selected);if(parent==null)throw new IOException("请选择配置父目录或其中一个账户文件夹");return parent.FullName;
    }
    public void Validate(string active){
        if(Folders==null||Names==null||Folders.Length!=2||Names.Length!=2)throw new IOException("最多配置两个账户目录");
        if(!Directory.Exists(Parent))throw new IOException("父目录不存在");
        if(Folders[0].Equals(Folders[1],StringComparison.OrdinalIgnoreCase))throw new IOException("两个账户不能选择同一目录");
        foreach(var name in Names)if(String.IsNullOrWhiteSpace(name)||name.Length>32)throw new IOException("显示名称须为 1–32 个字符");
        foreach(var leaf in Folders){
            if(String.IsNullOrWhiteSpace(leaf)||leaf.StartsWith(".")||leaf!=Path.GetFileName(leaf)||leaf.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new IOException("请选择父目录下的同级账户文件夹，不能嵌套或重复");
            string folder=Path.GetFullPath(Path.Combine(Parent,leaf)).TrimEnd('\\')+"\\";
            string user=Path.GetFullPath(active).TrimEnd('\\')+"\\";
            string program=AppDomain.CurrentDomain.BaseDirectory;
            if(folder.StartsWith(user,StringComparison.OrdinalIgnoreCase)||user.StartsWith(folder,StringComparison.OrdinalIgnoreCase)||program.StartsWith(folder,StringComparison.OrdinalIgnoreCase))throw new IOException("不能把桌面配置或当前程序所在目录设为备用配置目录");
        }
    }
}
public partial class UI {
    ProfileSettings profileSettings=new ProfileSettings();
    DispatcherTimer trayClickTimer;DateTime suppressTrayClick;
    EventWaitHandle settingsSignal;RegisteredWaitHandle settingsWait;
    string SettingsPath {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles.settings.json");}}
    string ProfileLabel(string key){return profileSettings.Names[Array.IndexOf(Engine.Keys,key)];}
    void LoadProfileSettings(){
        profileSettings.Parent=engine.Profiles;
        try{if(File.Exists(SettingsPath)){var loaded=new JavaScriptSerializer().Deserialize<ProfileSettings>(File.ReadAllText(SettingsPath));loaded.Validate(engine.Active);profileSettings=loaded;engine.Profiles=Path.GetFullPath(loaded.Parent);engine.FolderNames=(string[])loaded.Folders.Clone();}}
        catch(Exception){Text("Status").Text="目录设置无效，已使用原始目录；请在管理目录中重新选择。";}
    }
    void InitializeProfileSettings(){Button("ManageProfiles").Click+=(s,e)=>Guard(ManageProfiles);}
    public static Brush QuotaColor(double remaining){return new SolidColorBrush(remaining<=20?Color.FromRgb(240,103,106):remaining<=50?Color.FromRgb(231,174,73):Color.FromRgb(61,189,150));}
    void ShowSettings(){
        if(panel!=null)DismissPanel(panel);
        Window.Show();if(Window.WindowState==WindowState.Minimized)Window.WindowState=WindowState.Normal;Window.Activate();
    }
    void EnsureTrayTimer(){
        if(trayClickTimer!=null)return;
        trayClickTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime+30)};
        trayClickTimer.Tick+=(s,e)=>{trayClickTimer.Stop();ShowPanel();};
    }
    void QueueTraySingle(){EnsureTrayTimer();if(DateTime.UtcNow<suppressTrayClick)return;trayClickTimer.Stop();trayClickTimer.Start();}
    void OpenFromTrayDouble(){EnsureTrayTimer();trayClickTimer.Stop();suppressTrayClick=DateTime.UtcNow.AddMilliseconds(Forms.SystemInformation.DoubleClickTime);ShowSettings();}
    void InitializeTrayGestures(){
        EnsureTrayTimer();
        tray.MouseClick+=(s,e)=>Window.Dispatcher.BeginInvoke(new Action(()=>{
            if(e.Button==Forms.MouseButtons.Right){trayClickTimer.Stop();ShowTrayMenu();return;}
            if(e.Button==Forms.MouseButtons.Left&&e.Clicks<2&&DateTime.UtcNow>=suppressTrayClick){QueueTraySingle();}
        }));
        tray.MouseDoubleClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)Window.Dispatcher.BeginInvoke(new Action(()=>{OpenFromTrayDouble();}));};
        bool created;settingsSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexLocalShowSettings",out created);
        settingsWait=ThreadPool.RegisterWaitForSingleObject(settingsSignal,(s,t)=>Window.Dispatcher.BeginInvoke(new Action(ShowSettings)),null,Timeout.Infinite,false);
        Window.Closed+=(s,e)=>{trayClickTimer.Stop();settingsWait.Unregister(null);settingsSignal.Dispose();tray.Dispose();if(lastIcon!=null)lastIcon.Dispose();};
    }
    ContextMenu CreateTrayMenu(){
        var menu=new ContextMenu{Resources=Window.Resources,PlacementTarget=Window,Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint};
        var quick=new MenuItem{Header="快速访问",InputGestureText="左键单击"};quick.Click+=(s,e)=>ShowPanel();
        var settings=new MenuItem{Header="设置",InputGestureText="双击"};settings.Click+=(s,e)=>ShowSettings();
        var help=new MenuItem{Header="使用说明"};help.Click+=(s,e)=>Guard(()=>Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.md")){UseShellExecute=true}));
        var quit=new MenuItem{Header="退出"};quit.Click+=async(s,e)=>{if(busy){Text("Status").Text="请等待当前切换结束";return;}exitPending=true;Text("Status").Text="正在结束后台额度查询…";await operationGate.WaitAsync();operationGate.Release();exiting=true;if(panel!=null)panel.Close();Window.Close();};
        AttachMenuDismiss(menu);menu.Items.Add(quick);menu.Items.Add(settings);menu.Items.Add(new Separator{Style=(Style)Window.Resources[typeof(Separator)]});menu.Items.Add(help);menu.Items.Add(new Separator{Style=(Style)Window.Resources[typeof(Separator)]});menu.Items.Add(quit);return menu;
    }
    void ShowTrayMenu(){var menu=CreateTrayMenu();menu.IsOpen=true;}
    Window StyledDialog(string title,double width,double height){
        var dialog=new Window{Title=title,Width=width,Height=height,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=ThemeBrush("Surface"),Foreground=ThemeBrush("Text"),FontFamily=Window.FontFamily,FontSize=14,Resources=Window.Resources};
        System.Windows.Shell.WindowChrome.SetWindowChrome(dialog,new System.Windows.Shell.WindowChrome{CaptionHeight=46,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        dialog.SourceInitialized+=(s,e)=>{var h=new WindowInteropHelper(dialog).Handle;int r=2;DwmSetWindowAttribute(h,33,ref r,4);};return dialog;
    }
    DockPanel DialogFrame(Window dialog,string title){
        var dock=new DockPanel{Background=ThemeBrush("Surface")};var bar=new DockPanel{Height=46,Margin=new Thickness(18,0,8,0)};DockPanel.SetDock(bar,Dock.Top);dock.Children.Add(bar);
        var close=new Button{Content="\uE711",Style=(Style)Window.Resources["RoundIconButton"],Width=34,Height=34};System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close,true);DockPanel.SetDock(close,Dock.Right);bar.Children.Add(close);close.Click+=(s,e)=>dialog.Close();bar.Children.Add(new TextBlock{Text=title,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold});dialog.Content=dock;return dock;
    }
    string PickFolder(string initial,string title,string boundary){return NativeFolderPicker.Pick(Window,initial,title,false);}
    void ManageProfiles(){CreateProfileSettingsDialog().ShowDialog();}
    Window CreateProfileSettingsDialog(){
        if(!UiTestMode&&busy)throw new IOException("请先完成当前切换或恢复未完成记录");
        var dialog=StyledDialog("管理账户目录",720,560);dialog.SizeToContent=SizeToContent.Height;var frame=DialogFrame(dialog,"管理账户目录");var stack=new StackPanel{Margin=new Thickness(28,14,28,24)};frame.Children.Add(stack);
        stack.Children.Add(new TextBlock{Text="两个账户，一个父目录",FontSize=24,FontWeight=FontWeights.SemiBold});stack.Children.Add(new TextBlock{Text="选择其中一个账户文件夹，自动读取同级目录。修改显示名称不会重命名磁盘文件夹。",Style=(Style)Window.Resources["Muted"],Margin=new Thickness(0,8,0,22)});
        var parent=new TextBox{Text=engine.Profiles,IsReadOnly=true};var pick=new Button{Content="选择账户文件夹",Margin=new Thickness(10,0,0,0)};var line=new DockPanel();DockPanel.SetDock(pick,Dock.Right);line.Children.Add(pick);line.Children.Add(parent);stack.Children.Add(line);
        var folders=new ComboBox[2];var names=new TextBox[2];
        for(int i=0;i<2;i++){stack.Children.Add(new TextBlock{Text=engine.AccountEmails[i],Foreground=ThemeBrush("MutedText"),FontSize=12,Margin=new Thickness(0,20,0,8)});var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});row.ColumnDefinitions.Add(new ColumnDefinition());folders[i]=new ComboBox();names[i]=new TextBox{Text=profileSettings.Names[i],MaxLength=32,ToolTip="显示名称"};Grid.SetColumn(names[i],2);row.Children.Add(folders[i]);row.Children.Add(names[i]);stack.Children.Add(row);}
        var message=new TextBlock{Style=(Style)Window.Resources["Muted"],Margin=new Thickness(0,16,0,16)};stack.Children.Add(message);
        Action populate=()=>{var dirs=(Directory.Exists(parent.Text)?Directory.GetDirectories(parent.Text):new string[0]).Where(d=>!Path.GetFileName(d).StartsWith(".")&&!AppDomain.CurrentDomain.BaseDirectory.StartsWith(d.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Concat(engine.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();for(int i=0;i<2;i++){folders[i].ItemsSource=dirs;folders[i].SelectedItem=dirs.Contains(engine.FolderNames[i])?engine.FolderNames[i]:dirs.ElementAtOrDefault(i);}};populate();
        pick.Click+=(s,e)=>{
            var chosen=PickFolder(parent.Text,"选择配置父目录或任意一个账户文件夹",null);if(chosen==null)return;
            parent.Text=ProfileSettings.DetectParent(chosen);populate();
            foreach(var child in Directory.GetDirectories(parent.Text).Where(d=>!Path.GetFileName(d).StartsWith("."))){
                string authHome=File.Exists(Path.Combine(child,"auth.json"))?child:Path.Combine(child,".codex");
                string email=Engine.Read(authHome).Email;int index=Array.FindIndex(engine.AccountEmails,x=>x.Equals(email,StringComparison.OrdinalIgnoreCase));
                if(index>=0)folders[index].SelectedItem=Path.GetFileName(child);
            }
            if(!chosen.Equals(parent.Text,StringComparison.OrdinalIgnoreCase)){
                if(Path.GetFileName(chosen).Equals(".codex",StringComparison.OrdinalIgnoreCase))chosen=Directory.GetParent(chosen).FullName;
                string email=Engine.Read(File.Exists(Path.Combine(chosen,"auth.json"))?chosen:Path.Combine(chosen,".codex")).Email;
                int index=Array.FindIndex(engine.AccountEmails,x=>x.Equals(email,StringComparison.OrdinalIgnoreCase));folders[index<0?0:index].SelectedItem=Path.GetFileName(chosen);
            }
        };

        var save=new Button{Content="保存目录设置",Style=(Style)Window.Resources["Primary"],HorizontalAlignment=HorizontalAlignment.Right};stack.Children.Add(save);save.Click+=(s,e)=>{try{var settings=new ProfileSettings{Parent=Path.GetFullPath(parent.Text),Folders=folders.Select(c=>Convert.ToString(c.SelectedItem)).ToArray(),Names=names.Select(n=>n.Text.Trim()).ToArray()};settings.Validate(engine.Active);if(busy)throw new IOException("请先停止当前操作");if(File.Exists(engine.Journal)&&!Path.GetFullPath(settings.Parent).Equals(Path.GetFullPath(engine.Profiles),StringComparison.OrdinalIgnoreCase))throw new IOException("原目录有未完成记录，请连同记录和中转目录一起移动后选择新父目录，避免丢失恢复依据。");File.WriteAllText(SettingsPath,new JavaScriptSerializer().Serialize(settings));profileSettings=settings;engine.Profiles=settings.Parent;engine.FolderNames=(string[])settings.Folders.Clone();engine.ResetIdentities();signature="";Refresh();dialog.Close();}catch(Exception error){message.Text=error.Message;}};
        return dialog;
    }
    [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public int Flags;}
    [StructLayout(LayoutKind.Sequential)]struct MinMaxInfo{public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);
    [DllImport("user32.dll")]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    IntPtr WorkAreaHook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled){
        if(msg==0x24){var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(GetMonitorInfo(monitor,ref info)){var limits=(MinMaxInfo)Marshal.PtrToStructure(lParam,typeof(MinMaxInfo));limits.MaxPosition.X=info.Work.Left-info.Monitor.Left;limits.MaxPosition.Y=info.Work.Top-info.Monitor.Top;limits.MaxSize.X=info.Work.Right-info.Work.Left;limits.MaxSize.Y=info.Work.Bottom-info.Work.Top;Marshal.StructureToPtr(limits,lParam,false);handled=true;}}
        return IntPtr.Zero;
    }
}
}
