using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace CodexLocal {
public class SwitchRecord {
    public int Version=2,Phase;
    public bool SlotExisted;
    public string Id,Key,OldKey,Source,Slot;
}
public partial class Engine {
    public Action<string> Stage=delegate{};
    public Action<string> BeforeDelete=delegate{};
    public Action<string> BeforeFileDelete=delegate{};
    public volatile bool CancelRequested;
    void CheckCancel(){if(CancelRequested)throw new OperationCanceledException("已停止，可继续或撤销本次切换");}
    void Say(int percent,string name){CheckCancel();Audit(name);Stage(name);Progress(percent,name);}
    void Audit(string text){string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs");Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"switch-"+DateTime.Today.ToString("yyyy-MM-dd")+".log"),DateTime.Now.ToString("O")+" "+text+Environment.NewLine);}
    static string Full(string p){return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);}
    bool Same(string a,string b){return Full(a).Equals(Full(b),StringComparison.OrdinalIgnoreCase);}
    Dictionary<string,string> Inventory(string root){
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(!Directory.Exists(root))return result;
        Action<string> walk=null;walk=dir=>{NoLink(dir);foreach(string f in Directory.GetFiles(dir)){NoLink(f);result.Add(f.Substring(root.TrimEnd('\\').Length+1),f);}foreach(string child in Directory.GetDirectories(dir).Where(d=>!RuntimeEntry(d)))walk(child);};walk(root);return result;
    }
    void CompareFiles(string a,string b){using(var sha=SHA256.Create())using(var x=File.OpenRead(a))using(var y=File.OpenRead(b))if(!sha.ComputeHash(x).SequenceEqual(sha.ComputeHash(y)))throw new IOException("内容校验失败，未清理源文件："+a);}
    void Verify(string source,string destination,bool subset){
        if(!Directory.Exists(source)||!Directory.Exists(destination))throw new IOException("校验目录不存在，已停止迁移");
        var a=Inventory(source);var b=Inventory(destination);
        if(!subset&&a.Count!=b.Count)throw new IOException("文件数量不一致，已停止清理");
        foreach(var file in a){string other;if(!b.TryGetValue(file.Key,out other))throw new IOException("目标缺少文件："+file.Key);CompareFiles(file.Value,other);}
    }
    static void RemoveRuntime(string path){
        var attrs=File.GetAttributes(path);if((attrs&FileAttributes.ReparsePoint)!=0){if((attrs&FileAttributes.Directory)!=0)Directory.Delete(path,false);else File.Delete(path);return;}
        foreach(var file in Directory.GetFiles(path))try{File.Delete(file);}catch(Exception error){throw new IOException("运行时文件无法清理："+file+"；"+error.Message,error);}
        foreach(var dir in Directory.GetDirectories(path))RemoveRuntime(dir);Directory.Delete(path,false);
    }
    void ClearRuntimeOnly(string path){
        if(!Directory.Exists(path))return;
        if(Inventory(path).Count!=0)throw new IOException("C 盘存在新的配置数据，已保留文件；请选择撤销或检查账户后继续。");
        foreach(var dir in Directory.GetDirectories(path)){if(!RuntimeEntry(dir))throw new IOException("C 盘存在额外目录，已保留："+dir);RemoveRuntime(dir);}
        Directory.Delete(path,false);
    }
    void DeleteVerified(string source,string reference,int start,int end){
        if(!Directory.Exists(source)){Progress(end,"源目录已清理");return;}
        Verify(source,reference,true);BeforeDelete(source);
        var files=Inventory(source);int count=0;
        foreach(var f in files){CheckCancel();NoLink(f.Value);CompareFiles(f.Value,Path.Combine(reference,f.Key));BeforeFileDelete(f.Value);File.Delete(f.Value);count++;Progress(start+(end-start)*count/Math.Max(1,files.Count),"清理源文件 · "+f.Key);}
        // Enumerate without following links, then remove only empty directories.
        Action<string> remove=null;remove=dir=>{NoLink(dir);foreach(var child in Directory.GetDirectories(dir)){if(RuntimeEntry(child))RemoveRuntime(child);else remove(child);}Directory.Delete(dir,false);};remove(source);Progress(end,"源目录清理完成");
    }
    void Save(SwitchRecord record,int phase){
        record.Phase=phase;var bytes=System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(record));string temp=Journal+".new";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
        if(File.Exists(Journal))File.Replace(temp,Journal,null);else File.Move(temp,Journal);
    }
    public string[] LockRoots(){
        var paths=new List<string>{Active,Profile("life"),Profile("work")};
        try{if(File.Exists(Journal)){var r=new JavaScriptSerializer().Deserialize<SwitchRecord>(File.ReadAllText(Journal));if(r!=null&&r.Id!=null&&System.Text.RegularExpressions.Regex.IsMatch(r.Id,"^[a-f0-9]{32}$")){paths.Add(Active+"-retired-"+r.Id);paths.Add(Active+"-incoming-"+r.Id);}}}catch{}
        return paths.ToArray();
    }
    void CheckClear(){if(Occupied())throw new IOException("检测到 Codex 或相关资源正在使用配置。请先退出桌面版或对应 CLI，再重新检查。");}
    bool IsHome(string path,string key){string container=Path.Combine(Profiles,FolderNames[Array.IndexOf(Keys,key)]);return Same(path,container)||Same(path,Path.Combine(container,".codex"));}
    void RestoreCopy(string source,string destination){
        if(Directory.Exists(destination))Verify(destination,source,true);
        Copy(source,destination,0,80);Verify(source,destination,false);
    }
    public void Undo(){
        Audit("撤销请求");CancelRequested=false;CheckClear();if(!File.Exists(Journal))return;
        var r=new JavaScriptSerializer().Deserialize<SwitchRecord>(File.ReadAllText(Journal));
        if(r==null||r.Version!=2||!Keys.Contains(r.Key)||!Keys.Contains(r.OldKey)||r.Id==null||!System.Text.RegularExpressions.Regex.IsMatch(r.Id,"^[a-f0-9]{32}$")||!IsHome(r.Source,r.Key)||!IsHome(r.Slot,r.OldKey))throw new IOException("恢复记录无效，未修改文件");
        string outgoing=Path.Combine(Profiles,".outgoing-"+r.Id),retired=Active+"-retired-"+r.Id,incoming=Active+"-incoming-"+r.Id;
        Stage("正在撤销，校验并恢复点击前的账户");
        if(Read(Active).Email==AccountEmails[Array.IndexOf(Keys,r.Key)]){RestoreCopy(Active,r.Source);DeleteVerified(Active,r.Source,80,85);}
        if(Directory.Exists(retired))RestoreCopy(r.Slot,retired);
        if(!Directory.Exists(Active)){string old=Directory.Exists(retired)?retired:r.Slot;RestoreCopy(old,Active);}
        if(Read(Active).Email!=AccountEmails[Array.IndexOf(Keys,r.OldKey)])throw new IOException("原账户身份不一致，保留副本并停止撤销");
        if(Directory.Exists(retired))DeleteVerified(retired,Active,85,90);
        if(Directory.Exists(outgoing))DeleteVerified(outgoing,Active,90,92);
        if(Directory.Exists(incoming))DeleteVerified(incoming,r.Source,92,94);
        if(!r.SlotExisted&&Directory.Exists(r.Slot))DeleteVerified(r.Slot,Active,94,99);
        string stagingUndo=Path.Combine(Profiles,".transfer-"+r.Id);if(Directory.Exists(stagingUndo))DeleteVerified(stagingUndo,r.Source,99,100);
        Audit("撤销完成");File.Delete(Journal);Progress(0,"已撤销，账户已恢复，可以重新切换");
    }
    public void Switch(string key){
        SwitchRecord r;
        Audit("切换请求 / 继续 "+key);CheckCancel(); CheckClear();
        if(File.Exists(Journal)){
            try{r=new JavaScriptSerializer().Deserialize<SwitchRecord>(File.ReadAllText(Journal));}catch(Exception){throw new IOException("发现旧版或无法识别的恢复记录。请保留配置并按说明手动恢复。");}
            if(r==null||r.Version!=2||!Keys.Contains(r.Key)||!Keys.Contains(r.OldKey)||r.Key==r.OldKey||r.Id==null||!System.Text.RegularExpressions.Regex.IsMatch(r.Id,"^[a-f0-9]{32}$")||!IsHome(r.Source,r.Key)||!IsHome(r.Slot,r.OldKey)||r.Phase<0||r.Phase>7)throw new IOException("恢复记录校验失败，已停止文件操作");
            // Always resume the single durable transaction.
            Say(1,"恢复上次未完成的迁移");
        }else{
            if(AccountEmails.Any(e=>e=="")||AccountEmails[0].Equals(AccountEmails[1],StringComparison.OrdinalIgnoreCase))throw new IOException("需要两个不同的账户，请先选择并检查账户文件夹");
            if(!Keys.Contains(key)||!Matches(key))throw new IOException("目标认证文件与账户不匹配");
            int old=Array.FindIndex(AccountEmails,e=>e.Equals(Read(Active).Email,StringComparison.OrdinalIgnoreCase));
            if(old<0||Keys[old]==key)throw new IOException("当前桌面账户无法识别或已是目标账户");
            r=new SwitchRecord{Id=Guid.NewGuid().ToString("N"),Key=key,OldKey=Keys[old],Source=Profile(key),Slot=Profile(Keys[old])};
            if(Directory.Exists(r.Slot)&&Directory.EnumerateFileSystemEntries(r.Slot).Any()){
                if(!Read(r.Slot).Email.Equals(AccountEmails[old],StringComparison.OrdinalIgnoreCase))throw new IOException("原账户的存储位置非空，不能覆盖");
                Verify(Active,r.Slot,false);r.SlotExisted=true;r.Phase=2;
            }
            Directory.CreateDirectory(Profiles);Save(r,r.Phase);
        }
        // Temporary paths are derived from validated fields, never accepted from the journal.
        string outgoing=Path.Combine(Profiles,".outgoing-"+r.Id),retired=Active+"-retired-"+r.Id,incoming=Active+"-incoming-"+r.Id;
        try{
            if(r.Phase==0){string staging=Path.Combine(Profiles,".transfer-"+r.Id);Say(1,"准备独立中转副本");Copy(r.Source,staging,1,3);Verify(r.Source,staging,false);Say(3,"1 / 6 · 复制当前桌面账户到 D 盘");Copy(Active,outgoing,3,25);Verify(Active,outgoing,false);Save(r,1);}
            if(r.Phase==1){Say(26,"2 / 6 · 校验并提交原账户存储");
                if(Directory.Exists(outgoing)){
                    if(Directory.Exists(r.Slot)){if(Directory.EnumerateFileSystemEntries(r.Slot).Any())throw new IOException("原账户目标目录出现新内容，已暂停");Directory.Delete(r.Slot,false);}
                    Directory.CreateDirectory(Path.GetDirectoryName(r.Slot));Rename(outgoing,r.Slot);
                }
                Verify(Active,r.Slot,false);Save(r,2);
            }
            if(r.Phase==2){Say(30,"3 / 6 · 核验归档，准备清理 C 盘原配置");CheckClear();
                if(Directory.Exists(Active)){Verify(Active,r.Slot,false);Rename(Active,retired);}
                else if(!Directory.Exists(retired))throw new IOException("找不到 C 盘原始配置，已暂停恢复");
                Save(r,3);
            }
            if(r.Phase==3){CheckClear();Say(33,"3 / 6 · 清理 C 盘已迁移的源目录");DeleteVerified(retired,r.Slot,33,45);Save(r,4);}
            if(r.Phase==4&&Read(Active).Email==AccountEmails[Array.IndexOf(Keys,r.Key)]){Verify(r.Source,Active,false);Save(r,6);}
            if(r.Phase==4){CheckClear();Say(46,"4 / 6 · 复制另一账户到 C 盘暂存");
                if(Directory.Exists(Active)&&Read(Active).Email==AccountEmails[Array.IndexOf(Keys,r.OldKey)]){Verify(Active,r.Slot,false);Rename(Active,retired);DeleteVerified(retired,r.Slot,33,45);}
                if(Directory.Exists(Active))ClearRuntimeOnly(Active);
                string staged=Path.Combine(Profiles,".transfer-"+r.Id);Copy(Directory.Exists(staged)?staged:r.Source,incoming,46,73);Verify(r.Source,incoming,false);Save(r,5);
            }
            if(r.Phase==5){CheckClear();Say(74,"5 / 6 · 安装并校验新桌面账户");
                if(Directory.Exists(incoming)){if(Directory.Exists(Active))throw new IOException("C 盘配置位置被占用");Rename(incoming,Active);}
                if(!Read(Active).Email.Equals(AccountEmails[Array.IndexOf(Keys,r.Key)],StringComparison.OrdinalIgnoreCase))throw new IOException("新桌面账户校验失败");
                Verify(r.Source,Active,false);Save(r,6);
            }
            if(r.Phase==6){CheckClear();Say(82,"6 / 6 · 校验 C 盘完整性并清理 D 盘源目录");
                if(!Read(Active).Email.Equals(AccountEmails[Array.IndexOf(Keys,r.Key)],StringComparison.OrdinalIgnoreCase)||!Read(r.Slot).Email.Equals(AccountEmails[Array.IndexOf(Keys,r.OldKey)],StringComparison.OrdinalIgnoreCase))throw new IOException("两处账户身份核验失败，已停止清理");
                DeleteVerified(r.Source,Active,82,97);Save(r,7);
            }
            if(Read(Active).Email.Equals(Read(r.Slot).Email,StringComparison.OrdinalIgnoreCase)||Directory.Exists(r.Source))throw new IOException("迁移后账户分离检查未通过，请恢复本次操作");
            string stagingDone=Path.Combine(Profiles,".transfer-"+r.Id);if(Directory.Exists(stagingDone))DeleteVerified(stagingDone,Active,97,99);
            File.Delete(Journal);Say(100,"切换完成 · C / D 盘分别保留两个不同账户");
        }catch(Exception e){Audit("暂停 "+e.ToString());Stage("操作暂停 · "+e.Message);throw new IOException(e.Message+"\n已保存恢复记录。关闭占用后点击“继续未完成切换”，不会覆盖现有文件。",e);}
    }
}
}
