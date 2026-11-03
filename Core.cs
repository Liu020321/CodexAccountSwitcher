using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace CodexLocal {
public class Identity {
    public string Email = "";
    public string Path;
    public string Note = "未找到可识别的文件认证";
    public Identity(string path) { Path = path; }
}
public partial class Engine {
    public string Root, Active, Profiles;
    public string[] FolderNames = { "life", "work" };
    public static readonly string[] Keys = { "life", "work" };
    public static readonly string[] Emails = { "first@example.invalid", "second@example.invalid" };
    string[] knownEmails={"",""};
    public string[] AccountEmails { get {
        for(int i=0;i<Keys.Length;i++)if(knownEmails[i]==""){string email=Read(Profile(Keys[i])).Email;if(email!="")knownEmails[i]=email;}
        string active=Read(Active).Email;
        if(active!=""&&knownEmails.Count(x=>x!="")==1&&!knownEmails.Any(x=>x.Equals(active,StringComparison.OrdinalIgnoreCase)))knownEmails[Array.FindIndex(knownEmails,x=>x=="")]=active;
        return (string[])knownEmails.Clone();
    }}
    public void ResetIdentities(){knownEmails=new[]{"",""};}
    public static string DefaultRoot { get {
#if PORTABLE
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accounts");
#else
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"accounts");
#endif
    }}
    public Action<int,string> Progress = delegate {};
    public Func<bool> Occupied;
    public Action<string,string> Rename = Directory.Move;
    public string Journal { get { return System.IO.Path.Combine(Profiles, ".native-switch-journal.json"); } }
    static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }; }
    public Engine(string root, string active) {
        Root = System.IO.Path.GetFullPath(root); Active = active;
        Profiles = Directory.Exists(System.IO.Path.Combine(root,"life"))&&Directory.Exists(System.IO.Path.Combine(root,"work"))?root:System.IO.Path.Combine(root, "codex_profiles");
        Occupied = delegate { return false; };
    }
    static Dictionary<string,object> Map(object o) { return o as Dictionary<string,object> ?? new Dictionary<string,object>(); }
    static object Get(Dictionary<string,object> d, string key) { object v; return d.TryGetValue(key, out v) ? v : null; }
    public static Identity Read(string path) {
        var result = new Identity(path);
        try {
            var auth = Map(Json().DeserializeObject(File.ReadAllText(System.IO.Path.Combine(path,"auth.json"))));
            var tokens = Map(Get(auth,"tokens"));
            foreach (string key in new [] { "id_token", "access_token" }) {
                var token = Get(tokens,key) as string;
                if (String.IsNullOrEmpty(token) || token.Split('.').Length != 3) continue;
                try {
                    string part = token.Split('.')[1].Replace('-','+').Replace('_','/');
                    part = part.PadRight((part.Length + 3) / 4 * 4, '=');
                    var claims = Map(Json().DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(part))));
                    string email = Get(claims,"email") as string ?? Get(Map(Get(claims,"https://api.openai.com/profile")),"email") as string;
                    if (!String.IsNullOrWhiteSpace(email)) { result.Email = email; result.Note = "从本地认证文件识别"; break; }
                } catch (FormatException) {} catch (ArgumentException) {}
            }
        } catch (IOException) {} catch (UnauthorizedAccessException) {} catch (ArgumentException) {} catch (InvalidOperationException) {}
        return result;
    }
    public string Profile(string key) {
        if (!Keys.Contains(key)) throw new InvalidOperationException("未知账户");
        string leaf=FolderNames[Array.IndexOf(Keys,key)];
        if(String.IsNullOrWhiteSpace(leaf)||leaf=="."||leaf==".."||leaf!=System.IO.Path.GetFileName(leaf))throw new IOException("配置必须是同级文件夹");
        string folder = System.IO.Path.Combine(Profiles,leaf);
        return File.Exists(System.IO.Path.Combine(folder,"auth.json")) ? folder : System.IO.Path.Combine(folder,".codex");
    }
    public bool Matches(string key) { return Read(Profile(key)).Email!="" && String.Equals(Read(Profile(key)).Email, AccountEmails[Array.IndexOf(Keys,key)], StringComparison.OrdinalIgnoreCase); }
    static readonly string[] RuntimeNames={"app-server-control","app-server-daemon","tmp",".tmp","project-metadata-locks","thread-writer-locks","tui-thread-reference-capabilities"};
    static bool RuntimeEntry(string path){return RuntimeNames.Contains(System.IO.Path.GetFileName(path),StringComparer.OrdinalIgnoreCase);}
    static void NoLink(string path) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("请先处理配置中的目录链接或联接：" + path); }
    public string Inside(string value) {
        string path = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(value) ? value : System.IO.Path.Combine(Root,value));
        string prefix = Root.TrimEnd('\\') + "\\";
        if (!path.Equals(Root,StringComparison.OrdinalIgnoreCase) && !path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择 Codex_cli 内的目录");
        if (!Directory.Exists(path)) throw new IOException("工作目录不存在");
        for (var d = new DirectoryInfo(path); d != null; d = d.Parent) {
            NoLink(d.FullName); if (d.FullName.Equals(Root,StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }
    void Collect(string source, string target, List<Tuple<string,string>> files) {
        NoLink(source); Directory.CreateDirectory(target);
        foreach (string f in Directory.GetFiles(source)) { NoLink(f); files.Add(Tuple.Create(f,System.IO.Path.Combine(target,System.IO.Path.GetFileName(f)))); }
        foreach (string d in Directory.GetDirectories(source).Where(d=>!RuntimeEntry(d))) Collect(d,System.IO.Path.Combine(target,System.IO.Path.GetFileName(d)),files);
    }
    void Copy(string source, string target, int start, int end) {
        var files = new List<Tuple<string,string>>(); Collect(source,target,files);
        for (int i=0;i<files.Count;i++) {
            CheckCancel(); var item = files[i]; File.Copy(item.Item1,item.Item2,true);
            using (var hash = SHA256.Create()) using (var a = File.OpenRead(item.Item1)) using (var b = File.OpenRead(item.Item2)) {
                if (!hash.ComputeHash(a).SequenceEqual(hash.ComputeHash(b))) throw new IOException("校验失败：" + item.Item1);
            }
            Progress(start + (end-start)*(i+1)/Math.Max(1,files.Count),"复制并校验 · " + System.IO.Path.GetFileName(item.Item1));
        }
    }

    public string CliKey(){
        var email=Read(Active).Email;int activeIndex=Array.FindIndex(AccountEmails,e=>e.Equals(email,StringComparison.OrdinalIgnoreCase));
        if(email==""||activeIndex<0||AccountEmails[0].Equals(AccountEmails[1],StringComparison.OrdinalIgnoreCase))return "";
        string key=Keys[1-activeIndex];return Matches(key)?key:"";
    }
    public static string ProjectDirectory(string cwd){
        string path=System.IO.Path.GetFullPath(cwd);if(!Directory.Exists(path))throw new IOException("项目工作目录不存在");return path;
    }
    public ProcessStartInfo LaunchInfo(string key, string cwd, bool cmd) {
        if(key==""||key!=CliKey())throw new IOException("CLI 必须使用与桌面版不同的另一账户，请先检查两处认证配置");
        string directory=ProjectDirectory(cwd),home=Profile(key);
        string executable = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),cmd ? "cmd.exe" : "WindowsPowerShell\\v1.0\\powershell.exe");
        string command=cmd ? "/d /v:off /k \"cd /d \"%CODEX_LAUNCH_DIR%\" && set \"CODEX_HOME=%CODEX_LAUNCH_HOME%\" && set \"CODEX_LAUNCH_DIR=\" && set \"CODEX_LAUNCH_HOME=\" && codex\"" : "-NoProfile -NoExit -Command \"Set-Location -LiteralPath $env:CODEX_LAUNCH_DIR -ErrorAction Stop; $env:CODEX_HOME=$env:CODEX_LAUNCH_HOME; Remove-Item Env:CODEX_LAUNCH_DIR,Env:CODEX_LAUNCH_HOME; codex\"";
        var info = new ProcessStartInfo(executable,command);
        info.UseShellExecute = false; info.CreateNoWindow = false;info.WorkingDirectory=directory;
        info.EnvironmentVariables["CODEX_HOME"]=home;info.EnvironmentVariables["CODEX_LAUNCH_DIR"]=directory;info.EnvironmentVariables["CODEX_LAUNCH_HOME"]=home;
        return info;
    }
}
}
