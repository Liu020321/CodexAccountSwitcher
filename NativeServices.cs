using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CodexLocal {
public static class NativeFolderPicker {
    delegate void TimerProc(IntPtr hwnd,uint message,UIntPtr id,uint time);
    delegate bool WindowEnum(IntPtr hwnd,IntPtr param);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowEnum callback,IntPtr param);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]static extern UIntPtr SetTimer(IntPtr hwnd,UIntPtr id,uint interval,TimerProc callback);
    [DllImport("user32.dll")]static extern bool KillTimer(IntPtr hwnd,UIntPtr id);
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IShellItem {
        void BindToHandler(IntPtr pbc,ref Guid bhid,ref Guid iid,out IntPtr value);void GetParent(out IShellItem parent);void GetDisplayName(uint kind,out IntPtr name);void GetAttributes(uint mask,out uint attributes);void Compare(IShellItem other,uint hint,out int order);
    }
    [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IFileDialog {
        [PreserveSig]int Show(IntPtr owner);void SetFileTypes(uint count,IntPtr types);void SetFileTypeIndex(uint index);void GetFileTypeIndex(out uint index);void Advise(IntPtr events,out uint cookie);void Unadvise(uint cookie);void SetOptions(uint options);void GetOptions(out uint options);void SetDefaultFolder(IShellItem item);void SetFolder(IShellItem item);void GetFolder(out IShellItem item);void GetCurrentSelection(out IShellItem item);void SetFileName([MarshalAs(UnmanagedType.LPWStr)]string name);void GetFileName(out IntPtr name);void SetTitle([MarshalAs(UnmanagedType.LPWStr)]string title);void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)]string text);void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)]string text);void GetResult(out IShellItem item);void AddPlace(IShellItem item,int location);void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)]string extension);void Close(int error);void SetClientGuid(ref Guid guid);void ClearClientData();void SetFilter(IntPtr filter);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]static extern void SHCreateItemFromParsingName(string path,IntPtr binding,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);
    public static string Pick(Window owner,string initial,string title,bool testCancel){
        var dialog=(IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));UIntPtr timer=UIntPtr.Zero;TimerProc cancel=null;
        try{
            if(testCancel)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"\nNative COM constructed");
            uint options;dialog.GetOptions(out options);dialog.SetOptions(options|0x20|0x40|0x800|0x10000000|0x02000000);dialog.SetTitle(title);dialog.SetOkButtonLabel("选择此文件夹");
            if(testCancel)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"\nNative options set");
            if(Directory.Exists(initial)){IShellItem folder;var iid=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");SHCreateItemFromParsingName(initial,IntPtr.Zero,ref iid,out folder);try{dialog.SetFolder(folder);}finally{Marshal.FinalReleaseComObject(folder);}}
            if(testCancel)File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"\nNative folder set");
            if(testCancel){cancel=(h,m,id,time)=>{
                uint own=(uint)Process.GetCurrentProcess().Id;
                EnumWindows((window,param)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(pid!=own)return true;var text=new StringBuilder(512);GetWindowText(window,text,text.Capacity);if(text.ToString()!=title)return true;
                    PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);KillTimer(IntPtr.Zero,id);return false;
                },IntPtr.Zero);
            };timer=SetTimer(IntPtr.Zero,UIntPtr.Zero,700,cancel);}
            int result=dialog.Show(new WindowInteropHelper(owner).Handle);if(result==unchecked((int)0x800704C7))return null;if(result<0)Marshal.ThrowExceptionForHR(result);
            IShellItem item;dialog.GetResult(out item);try{IntPtr name;item.GetDisplayName(0x80058000,out name);try{return Marshal.PtrToStringUni(name);}finally{Marshal.FreeCoTaskMem(name);}}finally{Marshal.FinalReleaseComObject(item);}
        }finally{if(timer!=UIntPtr.Zero)KillTimer(IntPtr.Zero,timer);GC.KeepAlive(cancel);Marshal.FinalReleaseComObject(dialog);}
    }
}
// Integration adapter: launches the separately installed Microsoft PowerToys UI.
// Protocol reference: microsoft/PowerToys, FileLocksmithLib/IPC.cpp and Constants.h.
public static class FileLocksmith {
    public static string FindUI(){
        var candidates=new List<string>();
        foreach(string entry in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){
            try{string folder=entry.Trim().Trim('"');if(folder.Length==0)continue;
                if(File.Exists(Path.Combine(folder,"PowerToys.FileLocksmith.CLI.exe"))){var parent=Directory.GetParent(folder.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));if(parent!=null)candidates.Add(Path.Combine(parent.FullName,"WinUI3Apps","PowerToys.FileLocksmithUI.exe"));}
            }catch{}
        }
        foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)})
            candidates.Add(Path.Combine(root,"PowerToys","WinUI3Apps","PowerToys.FileLocksmithUI.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
    public static string[] Targets(string message,string[] fallback){
        var paths=new List<string>();
        foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(message,"[“\"]([A-Za-z]:[^”\"\r\n]+)[”\"]")){
            string path=match.Groups[1].Value;if(File.Exists(path)||Directory.Exists(path))paths.Add(Path.GetFullPath(path));
        }
        return (paths.Count>0?paths:fallback.Where(p=>File.Exists(p)||Directory.Exists(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static Process Open(string[] paths){
        string executable=FindUI();if(executable==null)throw new IOException("未找到已安装的 PowerToys File Locksmith，请安装或启用此工具后重试。");
        if(paths.Length==0)throw new IOException("待检查的文件路径已不存在，请重新检查本次切换。");
        if(paths.Any(p=>p.IndexOfAny(new[]{'\r','\n'})>=0))throw new IOException("路径包含不支持的换行符。");
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","PowerToys","File Locksmith");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"last-run.log"),String.Join("\n",paths)+"\n\n",new UnicodeEncoding(false,false));
        return Process.Start(new ProcessStartInfo(executable){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(executable)});
    }
}
public static class ProcessControl {
    public static void Stop(int pid,long identity) {
        if(pid<=0||pid==Process.GetCurrentProcess().Id||identity<=0)throw new IOException("无法确认进程身份，请重新检查进程列表。");
        Process process;try{process=Process.GetProcessById(pid);}catch(ArgumentException){return;}
        using(process){
            if(process.HasExited)return;
            if(process.StartTime.ToUniversalTime().Ticks!=identity)throw new IOException("该 PID 已被其他进程使用，已取消结束操作，请重新检查。");
            process.Kill();
            if(!process.WaitForExit(5000))throw new IOException("进程尚未退出，请重新检查；本次迁移仍保持暂停。");
        }
    }
    public static void StopElevated(int pid,long identity) {
        using(var helper=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--end-process "+pid+" "+identity){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})){
            if(helper==null)throw new IOException("未能启动管理员操作。");
            helper.WaitForExit();
            if(helper.ExitCode!=0)throw new IOException("管理员结束操作失败（代码 "+helper.ExitCode+"）。请重新检查权限及进程状态。");
        }
        try{using(var process=Process.GetProcessById(pid))if(!process.HasExited&&process.StartTime.ToUniversalTime().Ticks==identity)throw new IOException("进程仍在运行，未完成结束操作。");}catch(ArgumentException){}
    }
}
public static class Occupancy {
    [StructLayout(LayoutKind.Sequential)]struct UniqueProcess{public uint Id;public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ProcessInfo {
        public UniqueProcess Process;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Service;public uint Type,Status,Session;[MarshalAs(UnmanagedType.Bool)]public bool Restartable;
    }
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]static extern int RmStartSession(out uint session,int flags,StringBuilder key);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]static extern int RmRegisterResources(uint session,uint files,[In,MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr)]string[] names,uint apps,IntPtr processes,uint services,string[] serviceNames);
    [DllImport("rstrtmgr.dll")]static extern int RmGetList(uint session,out uint needed,ref uint count,[In,Out]ProcessInfo[] infos,ref uint reasons);
    [DllImport("rstrtmgr.dll")]static extern int RmEndSession(uint session);
    public static string Report(params string[] roots){
        var rows=new List<string>();
        var files=new List<string>();Action<string> walk=null;walk=dir=>{try{if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)return;foreach(var file in Directory.GetFiles(dir))files.Add(file);foreach(var child in Directory.GetDirectories(dir))walk(child);}catch{}};foreach(var root in roots)if(Directory.Exists(root))walk(root);
        uint session;if(files.Count>0&&RmStartSession(out session,0,new StringBuilder(33))==0){try{
            string[] names=files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(RmRegisterResources(session,(uint)names.Length,names,0,IntPtr.Zero,0,null)==0){uint needed,count=0,reasons=0;int result=RmGetList(session,out needed,ref count,null,ref reasons);if(result==234&&needed>0){var infos=new ProcessInfo[needed];count=needed;if(RmGetList(session,out needed,ref count,infos,ref reasons)==0)foreach(var info in infos.Take((int)count))rows.Add("文件关联进程（不一定阻止迁移） · "+info.Name+" · PID "+info.Process.Id);}}
        }finally{RmEndSession(session);}}
        return rows.Count==0?"未检测到文件关联进程。请以上方具体错误为准；目录状态或权限问题不等于文件被占用。":String.Join("\n",rows.Distinct());
    }
}
}
