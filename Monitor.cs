using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Management;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;

namespace CodexLocal {
public static class Quota {
    static Dictionary<string,object> Map(object o) { return o as Dictionary<string,object> ?? new Dictionary<string,object>(); }
    static object Get(object o,string k) { object v;return Map(o).TryGetValue(k,out v)?v:null; }
    public static string Describe(object bucket) {
        var lines=new List<string>();
        foreach(string key in new[]{"primary","secondary"}) {
            var w=Get(bucket,key);if(w==null)continue;
            double used=Convert.ToDouble(Get(w,"usedPercent"));
            double mins=Convert.ToDouble(Get(w,"windowDurationMins"));
            string duration=mins==10080?"每周":mins==300?"5 小时":mins+" 分钟";
            string reset="";var epoch=Get(w,"resetsAt");if(epoch!=null)reset=" · 重置 "+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(epoch)).ToLocalTime().ToString("MM-dd HH:mm");
            lines.Add(duration+"剩余 "+Math.Max(0,Math.Min(100,100-used)).ToString("0.#")+"%"+reset);
        }
        return lines.Count==0?"服务未提供额度窗口":String.Join("\n",lines);
    }
    static string Executable() {
        var dirs=(Environment.GetEnvironmentVariable("PATH")??"").Split(';').Concat(new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin")});
        foreach(string dir in dirs.Where(d=>!String.IsNullOrWhiteSpace(d)&&Path.IsPathRooted(d.Trim('"')))) foreach(string name in new[]{"codex.exe","codex.cmd"})try{string p=Path.Combine(dir.Trim('"'),name);if(File.Exists(p))return p;}catch{}
        throw new IOException("未找到 Codex CLI，请安装后重试");
    }
    public static string Read(string home,string expected) {
        string exe=Executable();var info=new ProcessStartInfo(exe,"app-server"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        if(exe.EndsWith(".cmd",StringComparison.OrdinalIgnoreCase)){info.FileName=Environment.GetEnvironmentVariable("COMSPEC");info.Arguments="/d /s /c \"\""+exe+"\" app-server\"";}
        info.EnvironmentVariables["CODEX_HOME"]=home;
        using(var p=Process.Start(info)) {
            p.ErrorDataReceived+=(s,e)=>{};p.BeginErrorReadLine();var json=new JavaScriptSerializer();
            Func<int,string,object,object> call=(id,method,parameters)=>{
                p.StandardInput.WriteLine(json.Serialize(new {id=id,method=method,@params=parameters}));p.StandardInput.Flush();
                var end=DateTime.UtcNow.AddSeconds(12);
                while(DateTime.UtcNow<end) {
                    var task=p.StandardOutput.ReadLineAsync();if(!task.Wait(Math.Max(1,(int)(end-DateTime.UtcNow).TotalMilliseconds)))throw new IOException("额度查询超时");
                    if(task.Result==null)throw new IOException("Codex 服务已退出");
                    var message=Map(json.DeserializeObject(task.Result));
                    if(Convert.ToString(Get(message,"id"))!=id.ToString())continue;
                    if(Get(message,"error")!=null)throw new IOException("服务无法读取额度，请检查登录状态或 CLI 版本");
                    return Get(message,"result");
                }throw new IOException("额度查询超时");
            };
            try {
                call(1,"initialize",new {clientInfo=new{name="codex_local_settings",version="2.0.0"}});
                p.StandardInput.WriteLine("{\"method\":\"initialized\"}");p.StandardInput.Flush();
                var account=Get(call(2,"account/read",new{refreshToken=false}),"account");
                if(!String.Equals(Convert.ToString(Get(account,"email")),expected,StringComparison.OrdinalIgnoreCase))throw new IOException("服务账户与配置邮箱不匹配，未展示额度");
                var result=call(3,"account/rateLimits/read",new{});
                return Describe(Get(result,"rateLimits"))+"\n更新于 "+DateTime.Now.ToString("HH:mm:ss");
            }finally{try{p.StandardInput.Close();if(!p.WaitForExit(1500)){using(var kill=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),"/PID "+p.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){kill.WaitForExit(2000);}}}catch{}}
        }
    }
    public static string Running() {
        int desktop=0,cli=0,backend=0;
        try {using(var search=new ManagementObjectSearcher("SELECT Name,CommandLine,ExecutablePath FROM Win32_Process WHERE Name='Codex.exe' OR Name='codex.exe'"))using(var results=search.Get())foreach(ManagementObject p in results) {
            string command=Convert.ToString(p["CommandLine"]),path=Convert.ToString(p["ExecutablePath"]);
            if(command.IndexOf("app-server",StringComparison.OrdinalIgnoreCase)>=0){backend++;continue;}
            if(path.IndexOf("WindowsApps",StringComparison.OrdinalIgnoreCase)>=0||command.IndexOf("--type=",StringComparison.OrdinalIgnoreCase)>=0){desktop++;continue;}
            if(command!="")cli++;
        }}catch{return "无法读取进程信息；运行类型未知";}
        return "桌面版："+(desktop>0?"检测到进程":"未检测到界面进程")+"\nCLI："+(cli>0?"检测到 "+cli+" 个进程":"未检测到独立进程")+"\n后台服务："+backend+" 个（不计为 CLI）\n外部 CLI 账户无法从进程可靠读取";
    }
}
public partial class UI {
    Forms.NotifyIcon tray;Window panel;bool exiting,exitPending;
    void InitializeStatus() {
        Button("StatusPanel").Click+=(s,e)=>ShowPanel();
        Window.Closing+=(s,e)=>{if(!busy&&!exiting){e.Cancel=true;Window.Dispatcher.BeginInvoke(new Action(()=>Window.Hide()));}};
        if(Environment.GetCommandLineArgs().Any(a=>a=="--render"||a=="--ui-test"||a=="--resource-test"))return;
        tray=new Forms.NotifyIcon{Text="Codex · 运行状态",Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),Visible=true};
        InitializeTrayGestures(); InitializeQuotaIcon();

    }

    readonly SemaphoreSlim operationGate=new SemaphoreSlim(1,1);
    int quotaQueries;
    readonly Dictionary<string,QuotaSnapshot> quotaCache=new Dictionary<string,QuotaSnapshot>();
    readonly Dictionary<string,Task<QuotaSnapshot>> quotaPending=new Dictionary<string,Task<QuotaSnapshot>>();
    async Task<QuotaSnapshot> FetchQuota(string home,string email){
        string key=QuotaSnapshot.Key(home,email);
        QuotaSnapshot cached; if(quotaCache.TryGetValue(key,out cached)&&cached.Fresh(DateTime.UtcNow))return cached;
        Task<QuotaSnapshot> pending;if(quotaPending.TryGetValue(key,out pending))return await pending;
        var task=FetchQuotaCore(home,email,key);quotaPending[key]=task;
        try{return await task;}finally{quotaPending.Remove(key);}
    }
    async Task<QuotaSnapshot> FetchQuotaCore(string home,string email,string key){
        if(busy||exitPending)throw new IOException("当前操作尚未结束");await operationGate.WaitAsync();
        if(busy||exitPending){operationGate.Release();throw new IOException("切换尚未结束");}
        quotaQueries++;UpdateQuotaIcon();
        try{var text=await Task.Run(()=>Quota.Read(home,email));var snapshot=new QuotaSnapshot{Value=text,UpdatedUtc=DateTime.UtcNow};quotaCache[key]=snapshot;quotaCache[QuotaSnapshot.Key(home,email)]=snapshot;return snapshot;}
        finally{quotaQueries--;operationGate.Release();UpdateQuotaIcon();}
    }
    Window BuildPanel(bool preview){
        var popup=new Window{Title="Codex 运行状态",Width=430,Height=680,WindowStyle=WindowStyle.None,AllowsTransparency=false,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Background=ThemeBrush("Surface"),Foreground=ThemeBrush("Text"),FontFamily=Window.FontFamily,FontSize=13};
        System.Windows.Shell.WindowChrome.SetWindowChrome(popup,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
        popup.Resources=Window.Resources;popup.SourceInitialized+=(s,e)=>{int round=2;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(popup).Handle,33,ref round,4);};
        var outer=new Border{CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Padding=new Thickness(0)};outer.SetResourceReference(Border.BackgroundProperty,"Surface");outer.SetResourceReference(Border.BorderBrushProperty,"Stroke");
        var dock=new DockPanel();outer.Child=dock;popup.Content=outer;
        outer.SizeChanged+=(s,e)=>{outer.Clip=new RectangleGeometry(new Rect(0,0,outer.ActualWidth,outer.ActualHeight),8,8);};
        var header=new DockPanel{Margin=new Thickness(22,22,22,18)};DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
        var settings=new Button{Content="设置  ›",Style=(Style)Window.Resources[typeof(Button)],Padding=new Thickness(12,7,12,7),VerticalAlignment=VerticalAlignment.Top};DockPanel.SetDock(settings,Dock.Right);header.Children.Add(settings);settings.Click+=(s,e)=>{ShowSettings();DismissPanel(popup);};
        var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="账户概览",FontSize=23,FontWeight=FontWeights.SemiBold});heading.Children.Add(new TextBlock{Text="3 分钟内复用 · 额度按账户共享",FontSize=10,Foreground=ThemeBrush("MutedText"),Margin=new Thickness(0,7,0,0)});header.Children.Add(heading);
        var body=new StackPanel{Margin=new Thickness(18,0,18,8)};dock.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var updates=new List<Func<Task>>();
        foreach(string home in new[]{engine.Active,engine.Profile("life"),engine.Profile("work")}){
            var who=Engine.Read(home);if(who.Email=="")continue;
            var cardBody=new StackPanel{Margin=new Thickness(20,18,20,16)};
            cardBody.Children.Add(new TextBlock{Text=home==engine.Active?"\uE770":"\uE756",FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=22,Foreground=new SolidColorBrush(Color.FromRgb(72,194,245)),Margin=new Thickness(0,0,0,10)});
            cardBody.Children.Add(new TextBlock{Text=home==engine.Active?"桌面账户":ProfileLabel(Engine.Keys.First(k=>engine.Profile(k)==home)),FontSize=17,FontWeight=FontWeights.SemiBold});
            cardBody.Children.Add(new TextBlock{Text=who.Email,FontSize=12,Foreground=ThemeBrush("MutedText"),Margin=new Thickness(0,7,0,14),TextWrapping=TextWrapping.Wrap});
            var usage=new StackPanel();cardBody.Children.Add(usage);
            var updated=new TextBlock{Text="正在读取最新额度…",Foreground=ThemeBrush("MutedText"),FontSize=10,Margin=new Thickness(0,12,0,0)};cardBody.Children.Add(updated);
            var card=new Border{CornerRadius=new CornerRadius(11),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,10),Child=cardBody};card.SetResourceReference(Border.BackgroundProperty,"CardFill");card.SetResourceReference(Border.BorderBrushProperty,"Stroke");body.Children.Add(card);
            Action<QuotaSnapshot> draw=snapshot=>{
                usage.Children.Clear();
                var lines=snapshot==null?new[]{"额度尚未读取"}:snapshot.Value.Split('\n').Where(x=>!x.StartsWith("更新于")).ToArray();
                foreach(string line in lines){
                    var m=System.Text.RegularExpressions.Regex.Match(line,@"^(.*?)剩余 ([\d.]+)%(?: · 重置 (.*))?$");double percent;
                    if(m.Success&&Double.TryParse(m.Groups[2].Value,out percent)){
                        var label=new DockPanel{Margin=new Thickness(0,0,0,7)};
                        var amount=new TextBlock{Text=m.Groups[2].Value+"%",FontSize=22,FontWeight=FontWeights.SemiBold};DockPanel.SetDock(amount,Dock.Right);label.Children.Add(amount);
                        label.Children.Add(new TextBlock{Text=m.Groups[1].Value+"剩余额度",FontSize=12,Foreground=ThemeBrush("MutedText"),VerticalAlignment=VerticalAlignment.Center});usage.Children.Add(label);
                        if(m.Groups[3].Success)usage.Children.Add(new TextBlock{Text="重置于 "+m.Groups[3].Value,FontSize=10,Foreground=ThemeBrush("MutedText"),Margin=new Thickness(0,0,0,9)});
                        var track=new Border{Height=4,ToolTip="剩余 ≤20%：红色；20%–50%：黄色；>50%：绿色",Background=ThemeBrush("Track"),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,0,16)};
                        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Math.Max(0,percent),GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Math.Max(0,100-percent),GridUnitType.Star)});
                        grid.Children.Add(new Border{Background=QuotaColor(percent),CornerRadius=new CornerRadius(2)});track.Child=grid;usage.Children.Add(track);
                    }else usage.Children.Add(new TextBlock{Text=line,FontSize=12,Foreground=ThemeBrush("MutedText"),Margin=new Thickness(0,0,0,8)});
                }
                updated.Text=snapshot==null?"打开面板后自动更新":snapshot.UpdatedUtc.ToLocalTime().ToString("HH:mm:ss")+" 更新 · 3 分钟内免重复查询";
            };
            QuotaSnapshot cached;quotaCache.TryGetValue(QuotaSnapshot.Key(home,who.Email),out cached);draw(cached);
            if(!preview)updates.Add(async()=>{if(!popup.IsVisible)return;try{var result=await FetchQuota(home,who.Email);draw(result);UpdateLastQuotaIcon(home,result);}catch{updated.Text="更新失败 · 下次打开时重试";}});
        }
        if(!preview)popup.Loaded+=async(s,e)=>{foreach(var update in updates){if(!popup.IsVisible)break;await update();}if(!popup.IsVisible)ReleaseIdleResources();};
        return popup;
    }
    void DismissPanel(Window popup){
        if(popup==null)return;
        popup.Dispatcher.BeginInvoke(new Action(()=>{if(popup.IsVisible)popup.Hide();ReleaseIdleResources();}));
    }
    void ShowPanel(){
        if(panel!=null&&panel.IsVisible){DismissPanel(panel);return;}
        if(panel!=null){var old=panel;panel=null;old.Close();}
        var popup=BuildPanel(UiTestMode);panel=popup;
        var area=SystemParameters.WorkArea;popup.Height=Math.Min(680,area.Height-24);popup.Left=area.Right-popup.Width-12;popup.Top=area.Bottom-popup.Height-12;
        // Dismiss by hiding on the next dispatcher turn. Never call Close from Deactivated:
        // Deactivated is raised again during Close, causing the reentrant-close exception.
        popup.Deactivated+=(s,e)=>DismissPanel(popup);
        popup.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape)DismissPanel(popup);};
        popup.Closed+=(s,e)=>{if(panel==popup)panel=null;};popup.Show();popup.Activate();
    }
    void RenderStatusPreview(){
        var popup=BuildPanel(true);var visual=(FrameworkElement)popup.Content;popup.Content=null;
        visual.Resources=Window.Resources;visual.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,Window.Foreground);
        visual.Measure(new Size(430,680));visual.Arrange(new Rect(0,0,430,680));visual.UpdateLayout();
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(430,680,96,96,PixelFormats.Pbgra32);image.Render(visual);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var f=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"status-preview.png")))encoder.Save(f);
    }
}
public class QuotaSnapshot{
    public string Value;public DateTime UpdatedUtc;
    public bool Fresh(DateTime now){return now>=UpdatedUtc&&(now-UpdatedUtc)<TimeSpan.FromMinutes(3);}
    public static string Key(string home,string email){
        // Credential-file fingerprint avoids reusing a different workspace's snapshot after a login change.
        string fingerprint="";try{using(var hash=System.Security.Cryptography.SHA256.Create())fingerprint=Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(Path.Combine(home,"auth.json"))));}catch{}
        return email.ToLowerInvariant()+"|"+fingerprint;
    }
}
}
