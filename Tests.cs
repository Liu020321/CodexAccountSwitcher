using System;
using System.IO;
using System.Text;
using System.Linq;

namespace CodexLocal {
public static class SelfTest {
    static void Check(bool test,string name) {if(!test)throw new Exception("FAIL: "+name);}
    internal static void Auth(string path,string email) {
        Directory.CreateDirectory(path);
        string payload=Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"email\":\""+email+"\"}")).TrimEnd('=').Replace('+','-').Replace('/','_');
        File.WriteAllText(Path.Combine(path,"auth.json"),"{\"tokens\":{\"id_token\":\"x."+payload+".x\"}}");File.WriteAllText(Path.Combine(path,"example.txt"),email);
    }
    public static void Run() {
        Check(((System.Windows.Media.SolidColorBrush)UI.QuotaColor(20)).Color!=((System.Windows.Media.SolidColorBrush)UI.QuotaColor(21)).Color,"red/amber threshold");
        Check(((System.Windows.Media.SolidColorBrush)UI.QuotaColor(50)).Color!=((System.Windows.Media.SolidColorBrush)UI.QuotaColor(51)).Color,"amber/green threshold");
        var now=DateTime.UtcNow;var snapshot=new QuotaSnapshot{UpdatedUtc=now};
        Check(snapshot.Fresh(now.AddSeconds(179)),"quota cache within three minutes");
        Check(!snapshot.Fresh(now.AddMinutes(3)),"quota cache expires at three minutes");
        Check(!snapshot.Fresh(now.AddSeconds(-1)),"clock rollback does not extend cache");
        var json=new System.Web.Script.Serialization.JavaScriptSerializer();
        var quota=json.DeserializeObject("{\"primary\":{\"usedPercent\":31,\"windowDurationMins\":10080},\"secondary\":null}");
        Check(Quota.Describe(quota).Contains("69%"),"quota percentage");
        Check(Quota.Describe(quota).Contains("每周"),"quota window inferred from duration");
        Check(Quota.Describe(json.DeserializeObject("{}")).Contains("未提供"),"missing quota is unknown");
        string temp=Path.Combine(Path.GetTempPath(),"CodexNativeTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try {
            var engine=new Engine(temp,Path.Combine(temp,"user",".codex"));engine.Occupied=()=>false;
            var settings=new ProfileSettings{Parent=temp,Folders=new[]{"private","office"},Names=new[]{"个人","公司"}};settings.Validate(engine.Active);
            bool invalid=false;settings.Folders=new[]{"same","same"};try{settings.Validate(engine.Active);}catch(IOException){invalid=true;}Check(invalid,"duplicate sibling profiles rejected");
            invalid=false;settings.Folders=new[]{"one",".."};try{settings.Validate(engine.Active);}catch(IOException){invalid=true;}Check(invalid,"nested/traversal profile folders rejected");
            var custom=new Engine(Path.Combine(temp,"custom"),Path.Combine(temp,"custom-user",".codex")){FolderNames=new[]{"private","office"},Occupied=()=>false};
            Auth(custom.Active,Engine.Emails[0]);Auth(custom.Profile("work"),Engine.Emails[1]);custom.Switch("work");custom.Switch("life");Check(Engine.Read(custom.Active).Email==Engine.Emails[0],"custom sibling folder roundtrip");
            Check(ProfileSettings.DetectParent(custom.Profiles)==custom.Profiles,"select one parent discovers sibling profiles");
            Check(ProfileSettings.DetectParent(Path.GetDirectoryName(custom.Profile("work")))==custom.Profiles,"select one account container discovers parent");
            Auth(engine.Active,Engine.Emails[0]);Auth(engine.Profile("work"),Engine.Emails[1]);
            engine.Switch("work");Check(Engine.Read(engine.Active).Email==Engine.Emails[1],"switch work");
            Check(!Directory.Exists(engine.Profile("work")),"new desktop source cleaned from D drive");
            engine.Switch("life");Check(Engine.Read(engine.Active).Email==Engine.Emails[0],"round trip");
            engine.Occupied=()=>true;bool failed=false;try{engine.Switch("work");}catch(IOException){failed=true;}Check(failed&&Engine.Read(engine.Active).Email==Engine.Emails[0],"occupied preserves source");engine.Occupied=()=>false;
            failed=false;try{engine.Inside("..");}catch(IOException){failed=true;}Check(failed,"directory boundary");
            foreach(bool cmd in new[]{false,true}) {var info=engine.LaunchInfo("work",temp,cmd);Check(info.EnvironmentVariables["CODEX_HOME"]==engine.Profile("work"),"isolated environment");Check(info.UseShellExecute==false&&info.Arguments.Contains("codex"),"terminal arguments");}
            for(int fault=1;fault<=3;fault++){
                var interrupted=new Engine(Path.Combine(temp,"fault-"+fault),Path.Combine(temp,"user-"+fault,".codex")){Occupied=()=>false};
                Auth(interrupted.Active,Engine.Emails[0]);Auth(interrupted.Profile("work"),Engine.Emails[1]);
                int calls=0;int failAt=fault;interrupted.Rename=(from,to)=>{if(++calls==failAt)throw new IOException("simulated rename interruption");Directory.Move(from,to);};
                bool stopped=false;try{interrupted.Switch("work");}catch(IOException){stopped=true;}Check(stopped&&File.Exists(interrupted.Journal),"interruption leaves durable recovery journal");
                interrupted.Rename=Directory.Move;interrupted.Switch(null);
                Check(Engine.Read(interrupted.Active).Email==Engine.Emails[1]&&interrupted.Matches("life")&&!Directory.Exists(interrupted.Profile("work"))&&!File.Exists(interrupted.Journal),"recovery finishes with distinct homes and source removed");
            }
            foreach(int stop in new[]{2,4}){
                var partial=new Engine(Path.Combine(temp,"delete-fault-"+stop),Path.Combine(temp,"delete-user-"+stop,".codex")){Occupied=()=>false};
                Auth(partial.Active,Engine.Emails[0]);Auth(partial.Profile("work"),Engine.Emails[1]);int deletes=0;
                partial.BeforeFileDelete=file=>{if(++deletes==stop)throw new IOException("simulated source cleanup interruption");};
                bool paused=false;try{partial.Switch("work");}catch(IOException){paused=true;}Check(paused&&partial.Matches("life")&&(partial.Matches("work")||Engine.Read(partial.Active).Email==Engine.Emails[1]),"cleanup failure retains verified account copies");
                partial.BeforeFileDelete=file=>{};partial.Switch(null);Check(Engine.Read(partial.Active).Email==Engine.Emails[1]&&!Directory.Exists(partial.Profile("work")),"partial deletion resumes safely on both drives");
            }
            for(int phase=0;phase<=6;phase++){
                var undo=new Engine(Path.Combine(temp,"undo-"+phase),Path.Combine(temp,"undo-user-"+phase,".codex")){Occupied=()=>false};Auth(undo.Active,Engine.Emails[0]);Auth(undo.Profile("work"),Engine.Emails[1]);int wanted=phase;
                undo.Stage=message=>{if((wanted==0&&message.Contains("准备独立"))||(wanted>0&&message.StartsWith(wanted+" /")))undo.CancelRequested=true;};
                try{undo.Switch("work");}catch{}
                undo.Stage=message=>{};undo.CancelRequested=false;undo.Undo();
                Check(Engine.Read(undo.Active).Email==Engine.Emails[0]&&undo.Matches("work")&&!File.Exists(undo.Journal),"undo phase "+phase);
            }
            var readOnlyTest=new Engine(Path.Combine(temp,"readonly"),Path.Combine(temp,"readonly-user",".codex")){Occupied=()=>false};Auth(readOnlyTest.Active,Engine.Emails[0]);Auth(readOnlyTest.Profile("work"),Engine.Emails[1]);
            string readOnlySource=Path.Combine(readOnlyTest.Profile("work"),"AGENTS.md");File.WriteAllText(readOnlySource,"test read-only configuration");File.SetAttributes(readOnlySource,FileAttributes.ReadOnly);
            readOnlyTest.Stage=message=>{if(message.StartsWith("1 /"))readOnlyTest.CancelRequested=true;};try{readOnlyTest.Switch("work");}catch(IOException){}
            Check(File.Exists(readOnlySource),"cancel preserves read-only source");readOnlyTest.Stage=delegate{};readOnlyTest.CancelRequested=false;readOnlyTest.Switch(null);
            Check(Engine.Read(readOnlyTest.Active).Email==Engine.Emails[1]&&File.ReadAllText(Path.Combine(readOnlyTest.Active,"AGENTS.md"))=="test read-only configuration"&&!File.Exists(readOnlyTest.Journal),"read-only checkpoint resumes and verified cleanup completes");
            File.SetAttributes(Path.Combine(readOnlyTest.Active,"AGENTS.md"),FileAttributes.Normal);
            using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60"){UseShellExecute=false,CreateNoWindow=true})){
                try{long identity=child.StartTime.ToUniversalTime().Ticks;bool rejected=false;try{ProcessControl.Stop(child.Id,identity+1);}catch(IOException){rejected=true;}Check(rejected&&!child.HasExited,"process identity mismatch cannot kill process");ProcessControl.Stop(child.Id,identity);Check(child.WaitForExit(1000),"end task verifies child process termination");ProcessControl.Stop(child.Id,identity);}
                finally{if(!child.HasExited){child.Kill();child.WaitForExit();}}
            }
            var runtime=new Engine(Path.Combine(temp,"runtime"),Path.Combine(temp,"runtime-user",".codex")){Occupied=()=>false};Auth(runtime.Active,Engine.Emails[0]);Auth(runtime.Profile("work"),Engine.Emails[1]);
            Directory.CreateDirectory(Path.Combine(runtime.Profile("work"),"app-server-control"));File.WriteAllText(Path.Combine(runtime.Profile("work"),"app-server-control","app-server-control.sock"),"runtime only");
            runtime.Stage=message=>{if(message.StartsWith("4 /"))Directory.CreateDirectory(Path.Combine(runtime.Active,"app-server-daemon"));};runtime.Switch("work");
            Check(!Directory.Exists(Path.Combine(runtime.Active,"app-server-control"))&&Engine.Read(runtime.Active).Email==Engine.Emails[1],"runtime endpoints excluded; recreated empty active runtime reconciled");
            var locked=new Engine(Path.Combine(temp,"locked"),Path.Combine(temp,"locked-user",".codex")){Occupied=()=>false};Auth(locked.Active,Engine.Emails[0]);Auth(locked.Profile("work"),Engine.Emails[1]);
            string lockedFile=Path.Combine(locked.Active,"example.txt");using(var handle=new FileStream(lockedFile,FileMode.Open,FileAccess.Read,FileShare.None)){bool blocked=false;try{locked.Switch("work");}catch(IOException){blocked=true;}Check(blocked&&File.Exists(lockedFile),"real exclusive file lock preserves original");}locked.Switch(null);Check(Engine.Read(locked.Active).Email==Engine.Emails[1],"release actual lock resumes same transaction");
            Check(TransferFailure.IsLock(new IOException("wrapper",new IOException("locked",unchecked((int)0x80070020)))),"inner sharing violation classified as lock");
            Check(!TransferFailure.IsLock(new IOException("内容校验失败")),"checksum error is not a process lock");
            Check(TransferFailure.IsPermission(new IOException("wrapper",new UnauthorizedAccessException("denied"))),"permission classified independently");
            foreach(int checkpoint in new[]{1,2,3,4,5}){
                var changed=new Engine(Path.Combine(temp,"changed-"+checkpoint),Path.Combine(temp,"changed-user-"+checkpoint,".codex")){Occupied=()=>false};
                Auth(changed.Active,Engine.Emails[0]);Auth(changed.Profile("work"),Engine.Emails[1]);
                File.WriteAllText(Path.Combine(changed.Active,".codex-global-state.json"),"old desktop state");
                File.WriteAllText(Path.Combine(changed.Profile("work"),"state.json"),"old target state");
                File.WriteAllText(Path.Combine(changed.Profile("work"),"obsolete.txt"),"obsolete checkpoint");
                int at=checkpoint;
                changed.Stage=message=>{if(message.StartsWith(at+" /"))changed.CancelRequested=true;};
                try{changed.Switch("work");}catch(IOException){}
                Check(File.Exists(changed.Journal),"changed source has one resumable transaction");
                // Before the first source deletion, the live original remains authoritative.
                if(checkpoint<=3)File.WriteAllText(Path.Combine(changed.Active,".codex-global-state.json"),"latest desktop state");
                File.WriteAllText(Path.Combine(changed.Profile("work"),"state.json"),"latest target state");
                File.Delete(Path.Combine(changed.Profile("work"),"obsolete.txt"));
                File.WriteAllText(Path.Combine(changed.Profile("work"),"added.txt"),"new target file");
                changed.Stage=delegate{};changed.CancelRequested=false;changed.Switch(null);
                Check(File.ReadAllText(Path.Combine(changed.Active,"state.json"))=="latest target state"
                    &&File.Exists(Path.Combine(changed.Active,"added.txt"))&&!File.Exists(Path.Combine(changed.Active,"obsolete.txt")),
                    "retry refreshes changed, added and removed target files at checkpoint "+checkpoint);
                if(checkpoint<=3)Check(File.ReadAllText(Path.Combine(changed.Profile("life"),".codex-global-state.json"))=="latest desktop state","retry refreshes desktop snapshot at checkpoint "+checkpoint);
                Check(!File.Exists(changed.Journal)&&!Directory.Exists(changed.Profile("work")),"retry completes same transaction "+checkpoint);
            }
            var progressRoot=Path.Combine(temp,"progress");var progressActive=Path.Combine(temp,"progress-user",".codex");
            var progress=new Engine(progressRoot,progressActive){Occupied=()=>false};Auth(progress.Active,Engine.Emails[0]);Auth(progress.Profile("work"),Engine.Emails[1]);
            for(int i=0;i<100;i++)File.WriteAllText(Path.Combine(progress.Active,"sample-"+i.ToString("D3")+".txt"),"unchanged sample "+i);
            progress.Progress=(percent,message)=>{if(percent>=4)progress.CancelRequested=true;};
            try{progress.Switch("work");}catch(IOException){}
            Check(progress.PendingProgress==4,"interruption persists four percent");
            var progressRecord=json.Deserialize<SwitchRecord>(File.ReadAllText(progress.Journal));
            string checkpointFile=Directory.GetFiles(Path.Combine(progress.Profiles,".outgoing-"+progressRecord.Id)).First();
            var checkpointTime=File.GetLastWriteTimeUtc(checkpointFile);
            var resumed=new Engine(progressRoot,progressActive){Occupied=()=>false};
            Check(resumed.PendingProgress==4,"restart restores persisted progress");int previousPercent=4;bool reusedMessage=false;
            resumed.Progress=(percent,message)=>{Check(percent>=previousPercent,"retry progress never moves backwards");previousPercent=percent;if(message.Contains("校验已复制文件"))reusedMessage=true;
                if(percent==4&&File.Exists(checkpointFile))Check(File.GetLastWriteTimeUtc(checkpointFile)==checkpointTime,"verified checkpoint not rewritten");};
            resumed.Switch(null);Check(previousPercent==100&&reusedMessage&&!File.Exists(resumed.Journal)&&resumed.PendingProgress==0,"resume reuses checkpoint and completes then resets");
            previousPercent=-1;resumed.Progress=(percent,message)=>{if(previousPercent<0)Check(percent==0,"new switch starts fresh");previousPercent=percent;};resumed.Switch("life");
            File.WriteAllText(resumed.Journal,"{\"Version\":2,\"Phase\":5}");Check(resumed.PendingProgress==73,"legacy journal falls back to saved phase");File.Delete(resumed.Journal);
            var mismatch=new Engine(Path.Combine(temp,"verify-fault"),Path.Combine(temp,"verify-user",".codex")){Occupied=()=>false};Auth(mismatch.Active,Engine.Emails[0]);Auth(mismatch.Profile("work"),Engine.Emails[1]);
            mismatch.BeforeDelete=source=>File.WriteAllText(Path.Combine(mismatch.Profile("life"),"example.txt"),"simulated corruption");
            bool protectedSource=false;try{mismatch.Switch("work");}catch(IOException){protectedSource=true;}
            string retired=Directory.GetDirectories(Path.GetDirectoryName(mismatch.Active),".codex-retired-*").Single();
            Check(protectedSource&&File.ReadAllText(Path.Combine(retired,"example.txt"))==Engine.Emails[0],"checksum mismatch preserves original file");
            File.Copy(Path.Combine(retired,"example.txt"),Path.Combine(mismatch.Profile("life"),"example.txt"),true);mismatch.BeforeDelete=source=>{};mismatch.Switch(null);
            bool sameAccountRejected=false;try{engine.LaunchInfo("life",temp,false);}catch(IOException){sameAccountRejected=true;}Check(sameAccountRejected,"CLI cannot launch desktop account");
            var external=Path.Combine(temp,"external-project");Directory.CreateDirectory(external);Check(engine.LaunchInfo("work",external,false).WorkingDirectory==external,"project directory independent of profile root");
            var duplicate=new Engine(Path.Combine(temp,"duplicate"),Path.Combine(temp,"duplicate-user",".codex"));Auth(duplicate.Active,"same@example.invalid");Auth(duplicate.Profile("life"),"same@example.invalid");Auth(duplicate.Profile("work"),"same@example.invalid");
            Check(duplicate.CliKey()=="","duplicate identities cannot launch CLI");bool duplicateBlocked=false;try{duplicate.Switch("work");}catch(IOException){duplicateBlocked=true;}Check(duplicateBlocked&&!File.Exists(duplicate.Journal),"duplicate identities rejected before mutation");
            File.WriteAllText(engine.Journal,"[]");failed=false;try{engine.Switch("work");}catch(IOException){failed=true;}Check(failed,"incomplete journal blocks switch");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),"PASS: v7.2.7 persistent monotonic resume progress; restart restores percentage; unchanged checkpoint reused; completion reset; legacy phase fallback; stale checkpoint refresh at five interruption points; changed/added/removed files; lock/permission/checksum classification; verified child termination; PID identity guard; read-only checkpoint resume and verified cleanup; round-trip switch; occupied process; directory boundary; PowerShell/CMD environment and arguments; recoverable ordered migration; verified source cleanup; partial-deletion recovery; distinct accounts; arbitrary project directory; interrupted transaction protection.\r\nAll tests used temporary fake credentials; real profiles unchanged.");
        } finally { if(Path.GetFullPath(temp).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(temp,true); }
    }
}
}
