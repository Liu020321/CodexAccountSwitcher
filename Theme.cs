using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
namespace CodexLocal {
public partial class UI {
    bool DarkTheme=true;
    string ThemeFile {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"theme.settings");}}
    Brush ThemeBrush(string key){return (Brush)Window.Resources[key];}
    void InitializeTheme(){
        var choice=Find<ComboBox>("ThemeChoice");int saved=0;
        try{Int32.TryParse(File.ReadAllText(ThemeFile),out saved);}catch(IOException){}catch(UnauthorizedAccessException){}
        choice.SelectedIndex=Math.Max(0,Math.Min(2,saved));
        choice.SelectionChanged+=(s,e)=>{ApplyTheme();if(Environment.GetCommandLineArgs().Any(a=>a=="--render"||a=="--ui-test"))return;try{File.WriteAllText(ThemeFile,choice.SelectedIndex.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}};
        SystemEvents.UserPreferenceChanged+=SystemThemeChanged;
        Window.Closed+=(s,e)=>SystemEvents.UserPreferenceChanged-=SystemThemeChanged;
        ApplyTheme();
    }
    void SystemThemeChanged(object sender,UserPreferenceChangedEventArgs e){Window.Dispatcher.BeginInvoke(new Action(ApplyTheme));}
    void ApplyTheme(){
        int selected=Find<ComboBox>("ThemeChoice").SelectedIndex;bool light=false;
        try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))light=key==null||Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))!=0;}catch{}
        DarkTheme=selected==2||(selected==0&&!light);
        string[] names={"Surface","Sidebar","CardFill","Text","MutedText","ControlFill","Stroke","Track","HoverFill","FlyoutFill","Accent","AccentText"};
        string[] dark={"#1D1F25","#1D1F25","#282B33","#F3F4F8","#A6ADBC","#343841","#383D47","#454C59","#34373E","#2C2F36","#4CC2FF","#102A40"};
        string[] bright={"#F4F5F8","#F4F5F8","#FFFFFF","#202733","#687689","#E8EDF4","#D6DDE7","#DCE3ED","#E6E9F0","#FBFCFE","#0067C0","#FFFFFF"};
        for(int i=0;i<names.Length;i++)Window.Resources[names[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString((DarkTheme?dark:bright)[i]));
        Window.Foreground=ThemeBrush("Text");Window.Background=ThemeBrush("Surface");
        if(panel!=null){panel.Foreground=ThemeBrush("Text");}
        var handle=new WindowInteropHelper(Window).Handle;if(handle!=IntPtr.Zero){int darkFlag=DarkTheme?1:0;DwmSetWindowAttribute(handle,20,ref darkFlag,4);}
        Page(Find<StackPanel>("CliPage").Visibility==Visibility.Visible);UpdateQuotaIcon();
    }
}
}
