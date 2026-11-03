using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace CodexLocal {
public class Program {
    [STAThread] public static int Main(string[] args) {
        if(args.Length>0&&args[0]=="--end-process"){
            int pid;long identity;if(args.Length!=3||!Int32.TryParse(args[1],out pid)||!Int64.TryParse(args[2],out identity))return 2;
            try{ProcessControl.Stop(pid,identity);return 0;}catch(System.ComponentModel.Win32Exception){return 5;}catch{return 1;}
        }
        try {
            if(args.Contains("--diagnostics")){var check=new Engine(Engine.DefaultRoot,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"occupancy-results.txt"),DateTime.Now.ToString("O")+Environment.NewLine+Occupancy.Report(check.LockRoots()));return 0;}
            if (args.Contains("--self-test")) { SelfTest.Run(); return 0; }
            if(args.Contains("--ui-test")||args.Contains("--resource-test")){
                var testApp=new Application();var testUI=new UI();
                testApp.DispatcherUnhandledException+=(sender,error)=>{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),error.Exception.ToString());error.Handled=true;testApp.Shutdown(1);};
                testUI.Window.Loaded+=(sender,eventArgs)=>{if(args.Contains("--resource-test"))testUI.MeasureResources();else testUI.ExerciseUI();};
                return testApp.Run(testUI.Window);
            }
            using (var mutex = new Mutex(false,"Local\\CodexLocalNativeSettings")) {
                if (!mutex.WaitOne(0)) { try { using(var signal=EventWaitHandle.OpenExisting("Local\\CodexLocalShowSettings"))signal.Set(); }catch(WaitHandleCannotBeOpenedException){MessageBox.Show("请从托盘退出旧版本后再启动新版。", "Codex 设置");} return 0; }
                var app = new Application(); var ui = new UI();
                if (args.Contains("--render")) { ui.Render(); return 0; }
                app.DispatcherUnhandledException += (s,e) => { ui.ShowUnhandled(e.Exception.Message); e.Handled = true; };
                app.Run(ui.Window); mutex.ReleaseMutex();
            }
            return 0;
        } catch (Exception e) {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),e.ToString());
            if (!args.Contains("--self-test") && !args.Contains("--render")) MessageBox.Show(e.Message,"Codex 设置");
            return 1;
        }
    }
}
public partial class UI {
    public Window Window;
    Engine engine = new Engine(Engine.DefaultRoot,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"));
    bool busy, refreshing; string signature = "";
    readonly List<string> stageLines=new List<string>();
    List<Tuple<Process,string>> children = new List<Tuple<Process,string>>();
    T Find<T>(string name) where T:class { return Window.FindName(name) as T; }
    TextBlock Text(string name) { return Find<TextBlock>(name); }
    Button Button(string name) { return Find<Button>(name); }
    public UI() {
        if(Environment.GetCommandLineArgs().Any(a=>a=="--ui-test"||a=="--resource-test")){
            string testRoot=Path.Combine(Path.GetTempPath(),"CodexPanelQA-"+Guid.NewGuid().ToString("N"));
            engine=new Engine(testRoot,Path.Combine(testRoot,"user",".codex"));
            SelfTest.Auth(engine.Active,"desktop@example.invalid");SelfTest.Auth(engine.Profile("work"),"cli@example.invalid");
        }
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Window.xaml")) Window = (Window)XamlReader.Load(stream);
        Window.SourceInitialized += (s,e) => ApplyMaterial();
        Button("NavDesktop").Click += (s,e) => Page(false);
        Button("NavCli").Click += (s,e) => Page(true);
        Button("Refresh").Click += (s,e) => { signature = ""; Refresh(); };
        Button("OpenActive").Click += (s,e) => Guard(delegate { Process.Start(new ProcessStartInfo(engine.Active) { UseShellExecute=true }); });
        Find<ComboBox>("Shell").SelectionChanged += (s,e) => CliChanged();
        Find<TextBox>("WorkingDir").Text = @"D:\";
        Find<TextBox>("WorkingDir").TextChanged+=(s,e)=>CliChanged();
        Button("Launch").Click += (s,e) => Guard(delegate {
            string key = SelectedKey();
            var info = engine.LaunchInfo(key,Find<TextBox>("WorkingDir").Text,Find<ComboBox>("Shell").SelectedIndex==1);
            var child = Process.Start(info); children.Add(Tuple.Create(child,Engine.Read(engine.Profile(key)).Email));
            Text("Status").Text = "已打开终端并提交启动命令；若提示 codex 不存在，请检查 CLI 安装与 PATH。";
            Refresh();
        });
        Button("Browse").Click += (s,e) => Guard(Browse);
        Window.Closing += (s,e) => { if (busy) { e.Cancel = true; Text("Status").Text = "正在复制或提交配置，请等待操作结束再关闭。"; } };
        Button("CancelSwitch").Click+=async(s,e)=>{if(busy){engine.CancelRequested=true;Text("Status").Text="正在安全停止，当前文件校验完成后暂停…";}else await UndoSwitch();};
        Button("ResumeSwitch").Click+=async(s,e)=>await Switch(null);
        engine.Stage=message=>Window.Dispatcher.Invoke(new Action(()=>{stageLines.Add(DateTime.Now.ToString("HH:mm:ss")+"  "+message);Text("StageDetails").Text=String.Join("\n",stageLines.Skip(Math.Max(0,stageLines.Count-40)));}));
        DateTime lastProgress=DateTime.MinValue;
        engine.Progress = (value,message) => {if(value>0&&value<100&&(DateTime.UtcNow-lastProgress).TotalMilliseconds<100)return;lastProgress=DateTime.UtcNow;Window.Dispatcher.Invoke(new Action(delegate {
            Find<ProgressBar>("CopyProgress").Value = value; Text("ProgressText").Text = value+"% · "+message;
        }));};
        if(!Environment.GetCommandLineArgs().Any(a=>a=="--ui-test"||a=="--resource-test"))LoadProfileSettings();else profileSettings.Parent=engine.Profiles; Page(false); Refresh();
        InitializeTheme(); InitializeStatus(); InitializeChrome(); InitializeProfileSettings();
        Window.IsVisibleChanged+=(s,e)=>{if(Window.IsVisible&&!busy)Refresh();else if(!busy)ReleaseIdleResources();};
    }
    [DllImport("psapi.dll")]static extern bool EmptyWorkingSet(IntPtr process);
    void ReleaseIdleResources(){
        if(busy||quotaQueries>0)return;
        if(panel!=null&&!panel.IsVisible){var old=panel;panel=null;old.Close();old.Content=null;}
        stageLines.Clear();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        if(!Window.IsVisible)using(var own=Process.GetCurrentProcess())EmptyWorkingSet(own.Handle);
    }
    public void ShowUnhandled(string message){ShowError(message);}
    void ShowError(string message){var dialog=StyledDialog("操作提示",600,300);var frame=DialogFrame(dialog,"操作提示");var body=new StackPanel{Margin=new Thickness(24)};body.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Foreground=ThemeBrush("Text")});var ok=new Button{Content="知道了",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,24,0,0)};ok.Click+=(s,e)=>dialog.Close();body.Children.Add(ok);frame.Children.Add(body);dialog.ShowDialog();}
    void Guard(Action action) { try { action(); } catch (Exception e) { Text("Status").Text = e.Message; ShowError(e.Message); } }
    void Page(bool cli) {
        Find<StackPanel>("DesktopPage").Visibility = cli ? Visibility.Collapsed : Visibility.Visible;
        Find<StackPanel>("CliPage").Visibility = cli ? Visibility.Visible : Visibility.Collapsed;
        Text("PageTitle").Text = cli ? "CLI 启动" : "Codex 切换";
        Button("OpenActive").Visibility=cli?Visibility.Collapsed:Visibility.Visible;
        Button("NavCli").Tag = cli ? "Selected" : "";
        Button("NavDesktop").Tag = !cli ? "Selected" : "";
        var page=Find<StackPanel>(cli ? "CliPage" : "DesktopPage");if(SystemParameters.ClientAreaAnimation&&!Environment.GetCommandLineArgs().Contains("--render"))page.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(150)));
    }
    string SelectedKey() { return engine.CliKey(); }

    void CliChanged() {
        string key=SelectedKey();Button("Launch").IsEnabled=!busy&&key!="";
        if(key==""){Text("CliAccount").Text="未找到与桌面版不同的另一账户";Text("CliHome").Text="请先确认 C / D 盘的账户配置";Text("Command").Text="账户未就绪";return;}
        string path=engine.Profile(key),cwd=Find<TextBox>("WorkingDir").Text;
        Text("CliAccount").Text=ProfileLabel(key)+" · "+Engine.Read(path).Email;Text("CliHome").Text=path;
        Text("Command").Text=Find<ComboBox>("Shell").SelectedIndex==1 ? "cd /d \""+cwd+"\"\r\nset \"CODEX_HOME="+path+"\"\r\ncodex" : "Set-Location -LiteralPath '"+cwd.Replace("'","''")+"'\r\n$env:CODEX_HOME = '"+path.Replace("'","''")+"'\r\ncodex";
    }
    void Refresh() {
        if (refreshing) return; refreshing = true;
        try {
            var active = Engine.Read(engine.Active); Text("ActiveEmail").Text = active.Email=="" ? active.Note : active.Email; Text("ActivePath").Text = active.Path;
            var identities = Engine.Keys.Select(k=>Engine.Read(engine.Profile(k))).ToArray();
            string next = active.Email + busy + String.Join("|",identities.Select(i=>i.Path+":"+i.Email));
            if (next != signature) {
                signature = next; string selected = SelectedKey();
                var cards = Find<StackPanel>("ProfileCards"); cards.Children.Clear();
                for (int i=0;i<Engine.Keys.Length;i++) {
                    string key = Engine.Keys[i]; var who = identities[i]; bool match = who.Email!=""&&who.Email.Equals(engine.AccountEmails[i],StringComparison.OrdinalIgnoreCase);
                    var card = new Border { Style = (Style)Window.Resources["Card"] };
                    var dock = new DockPanel(); var button = new Button { Content="切换到桌面",Style=(Style)Window.Resources["Primary"],VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(18,0,0,0),IsEnabled=match&&!busy&&!who.Email.Equals(active.Email,StringComparison.OrdinalIgnoreCase) };
                    DockPanel.SetDock(button,Dock.Right); button.Click += async (s,e) => await Switch(key); dock.Children.Add(button);
                    var stack = new StackPanel(); stack.Children.Add(new TextBlock {Text=ProfileLabel(key)+" · "+engine.FolderNames[i],FontWeight=FontWeights.SemiBold});
                    stack.Children.Add(new TextBlock {Text=who.Email==""?"尚未存放可识别的账户配置":who.Email,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,6)});
                    stack.Children.Add(new TextBlock {Text=who.Path,Style=(Style)Window.Resources["Muted"]}); dock.Children.Add(stack); card.Child=dock; cards.Children.Add(card);
                }
            }
            CliChanged();
            var live = new List<string>();
            for (int i=children.Count-1;i>=0;i--) { try { if (children[i].Item1.HasExited) { children[i].Item1.Dispose(); children.RemoveAt(i); } else live.Add(children[i].Item2+" · 终端 PID "+children[i].Item1.Id); } catch { children.RemoveAt(i); } }
            Text("Sessions").Text = live.Count==0 ? "暂无本程序启动的终端" : String.Join("\n",live);
            Button("ResumeSwitch").Visibility=File.Exists(engine.Journal)?Visibility.Visible:Visibility.Collapsed;
            Button("ResumeSwitch").IsEnabled=!busy;
            Button("CancelSwitch").Visibility=(busy||File.Exists(engine.Journal))?Visibility.Visible:Visibility.Collapsed;
            Button("CancelSwitch").Content=busy?"停止本次切换":"撤销并恢复原账户";
            if (File.Exists(engine.Journal)) {
                Text("Status").Text = busy?"正在执行本次切换，可停止后继续或撤销。":"切换已暂停：可以继续同一次操作，或撤销恢复原账户。";
                if(!busy){int saved=engine.PendingProgress;Find<ProgressBar>("CopyProgress").Value=saved;Text("ProgressText").Text=saved+"% · 已保存本次进度，继续时先校验已有副本。";}
            }
        } finally { refreshing = false; }
    }
    async Task Switch(string key) {
        if(busy)return;engine.CancelRequested=false;busy=true;Refresh();stageLines.Clear();int savedProgress=engine.PendingProgress;Find<ProgressBar>("CopyProgress").Value=savedProgress;Text("ProgressText").Text=savedProgress>0?savedProgress+"% · 继续本次切换，正在检查已复制文件…":"正在检查账户与占用…";bool retry=false;Exception failure=null;
        await operationGate.WaitAsync();
        try{await Task.Run(()=>engine.Switch(key));Text("Status").Text="切换完成，请重新打开 Codex 桌面版。";UpdateQuotaIcon();}
        catch(Exception error){failure=error;Text("Status").Text="切换已暂停 · 请查看具体原因，可继续或撤销";Text("ProgressText").Text=error.Message;}
        finally{operationGate.Release();busy=false;Refresh();}
        if(failure!=null&&!engine.CancelRequested){
            string report="";
            if(TransferFailure.IsLock(failure)||TransferFailure.IsPermission(failure))
                report=await Task.Run(()=>{try{return FileLocksmith.FindUI()!=null?"":Occupancy.Report(engine.LockRoots());}catch{return "未能查询到占用详情；请以上方实际错误为准。";}});
            retry=SwitchNotice(failure,report);
        }
        if(retry)await Switch(File.Exists(engine.Journal)?null:key);
    }
    async Task UndoSwitch(){
        if(busy)return;busy=true;Refresh();await operationGate.WaitAsync();
        try{await Task.Run(()=>engine.Undo());Text("Status").Text="已恢复原账户，可以重新切换。";Find<ProgressBar>("CopyProgress").Value=0;}
        catch(Exception error){Text("Status").Text="撤销暂停 · "+error.Message;}
        finally{operationGate.Release();busy=false;Refresh();}
    }
    void Browse() {
        string chosen=PickFolder(Find<TextBox>("WorkingDir").Text,"选择 Codex 项目工作目录",null);
        if(chosen!=null)Find<TextBox>("WorkingDir").Text=Engine.ProjectDirectory(chosen);
    }
    [StructLayout(LayoutKind.Sequential)] struct Margins {public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    void ApplyMaterial() {
        var hwnd=new WindowInteropHelper(Window).Handle;
        int round=2; DwmSetWindowAttribute(hwnd,33,ref round,4);
        if (SystemParameters.HighContrast) {Text("MaterialStatus").Text="高对比度 · 普通背景";return;}
        int dark=DarkTheme ? 1 : 0; DwmSetWindowAttribute(hwnd,20,ref dark,4);
        int mica=2;DwmSetWindowAttribute(hwnd,38,ref mica,4);
        Window.Background=ThemeBrush("Surface");
        Text("MaterialStatus").Text="Fluent · 跟随当前主题";
    }

    public void Render() {
        var root=Find<Grid>("RootGrid");
        root.Resources=Window.Resources;
        root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Window.Foreground);
        Window.Content=null;
        root.Background=ThemeBrush("Surface");
        root.Measure(new Size(1100,820));root.Arrange(new Rect(0,0,1100,820));root.UpdateLayout();
        SaveRender(root,"desktop-preview.png");Page(true);root.Measure(new Size(1100,820));root.Arrange(new Rect(0,0,1100,820));root.UpdateLayout();SaveRender(root,"cli-preview.png");RenderStatusPreview();
        Find<ComboBox>("ThemeChoice").SelectedIndex=1;Page(false);root.Background=ThemeBrush("Surface");root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Window.Foreground);root.Measure(new Size(1100,820));root.Arrange(new Rect(0,0,1100,820));root.UpdateLayout();SaveRender(root,"light-preview.png");
    }
    void SaveRender(Visual visual,string name) {
        var image=new RenderTargetBitmap(1100,820,96,96,PixelFormats.Pbgra32);image.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using (var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name)))encoder.Save(stream);
    }
}
}
