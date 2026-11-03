using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexLocal {
public partial class UI {
    bool UiTestMode { get { return Environment.GetCommandLineArgs().Contains("--ui-test"); } }
    void InitializeChrome(){
        Window.SourceInitialized+=(s,e)=>HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle).AddHook(WorkAreaHook);
        Button("CaptionMin").Click+=(s,e)=>SystemCommands.MinimizeWindow(Window);
        Button("CaptionMax").Click+=(s,e)=>{if(Window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(Window);else SystemCommands.MaximizeWindow(Window);};
        Button("CaptionClose").Click+=(s,e)=>Window.Close();
        Window.StateChanged+=(s,e)=>{Find<Grid>("RootGrid").Margin=new Thickness(0);Button("CaptionMax").Content=Window.WindowState==WindowState.Maximized?"\uE923":"\uE922";};
    }
    static void Require(bool value,string message){if(!value)throw new Exception("UI regression: "+message);}
    static T Descendant<T>(DependencyObject parent) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)return (T)child;var result=Descendant<T>(child);if(result!=null)return result;}return null;
    }
    void CaptureVisual(FrameworkElement visual,string name){
        visual.UpdateLayout();int width=(int)Math.Ceiling(visual.ActualWidth),height=(int)Math.Ceiling(visual.ActualHeight);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name)))encoder.Save(f);
    }
    public async void MeasureResources(){
        await Task.Delay(2000);Window.Hide();await Task.Delay(2000);
        using(var process=System.Diagnostics.Process.GetCurrentProcess()){
            var cpu=process.TotalProcessorTime;await Task.Delay(3000);process.Refresh();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resource-results.txt"),"Hidden window; no quota query: working set MB="+(process.WorkingSet64/1048576.0).ToString("F1")+"; private MB="+(process.PrivateMemorySize64/1048576.0).ToString("F1")+"; CPU ms over 3s="+(process.TotalProcessorTime-cpu).TotalMilliseconds);
        }Application.Current.Shutdown();
    }
    public async void ExerciseUI(){
        try{
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"RUNNING v6 UI regression");
            await Task.Delay(250);
            Require(Window.WindowStyle==WindowStyle.None,"integrated title bar");
            Require(Math.Abs(Find<Border>("StatusFooter").ActualWidth-Find<Grid>("PageContent").ActualWidth)<0.6,"footer excludes scrollbar and matches cards");
            var cardButton=Descendant<Button>(Find<StackPanel>("ProfileCards"));
            if(cardButton!=null){var root=Find<Grid>("RootGrid");Func<Button,double> right=b=>b.TransformToAncestor(root).Transform(new Point(b.ActualWidth,0)).X;
                Require(Math.Abs(right(cardButton)-right(Button("ManageProfiles")))<0.6,"manage and switch buttons right edges align");
                Require(Math.Abs(right(cardButton)-right(Button("Refresh")))<0.6,"header and switch buttons right edges align");}

            Require(Button("CaptionClose").ActualWidth>=46,"caption hit target");
            for(int theme=2;theme>=0;theme--){Find<ComboBox>("ThemeChoice").SelectedIndex=theme;await Task.Delay(80);}
            var combo=Find<ComboBox>("ThemeChoice");combo.SelectedIndex=2;await Task.Delay(120);combo.IsDropDownOpen=true;await Task.Delay(200);
            var popup=combo.Template.FindName("PART_Popup",combo) as Popup;
            Require(popup!=null&&popup.IsOpen,"theme popup opens");
            Require(combo.Items.Count==3,"theme has exactly three options");
            var surface=combo.Template.FindName("FlyoutSurface",combo) as Border;
            Require(surface!=null&&Math.Abs(surface.ActualWidth-combo.ActualWidth)<0.6,"dropdown surface matches closed control width");
            Require(Math.Abs(surface.PointToScreen(new Point(0,0)).X-combo.PointToScreen(new Point(0,0)).X)<1.1,"dropdown left edge matches closed control");
            Require(Window.FontFamily.Source=="Microsoft YaHei","UI uses Microsoft YaHei");
            var menuItem=(ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0);var itemSurface=menuItem.Template.FindName("ItemSurface",menuItem) as Border;
            var itemLeft=itemSurface.PointToScreen(new Point(0,0)).X-surface.PointToScreen(new Point(0,0)).X;
            var itemRight=surface.PointToScreen(new Point(surface.ActualWidth,0)).X-itemSurface.PointToScreen(new Point(itemSurface.ActualWidth,0)).X;
            Require(Math.Abs(itemLeft-itemRight)<1.1,"dropdown item has equal left and right insets");
            Require(popup.Placement==PlacementMode.Relative&&popup.VerticalOffset<0,"popup overlays field instead of adding fourth row");
            CaptureVisual((FrameworkElement)popup.Child,"dropdown-live.png");combo.IsDropDownOpen=false;await Task.Delay(100);
            CaptureVisual(Find<Grid>("RootGrid"),"desktop-live.png");
            Button("CaptionMax").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(250);
            Require(Window.WindowState==WindowState.Maximized,"maximize enters maximized state");
            var handle=new WindowInteropHelper(Window).Handle;var monitor=new MonitorInfo{Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonitorInfo))};GetMonitorInfo(MonitorFromWindow(handle,2),ref monitor);
            NativeRect bounds;GetWindowRect(handle,out bounds);
            Require(bounds.Left>=monitor.Work.Left-2&&bounds.Top>=monitor.Work.Top-2&&bounds.Right<=monitor.Work.Right+2&&bounds.Bottom<=monitor.Work.Bottom+2,"maximized window stays inside monitor work area");
            var caption=Button("CaptionClose");var captionPoint=caption.PointToScreen(new Point(caption.ActualWidth/2,caption.ActualHeight/2));
            Require(captionPoint.X>=monitor.Work.Left&&captionPoint.X<monitor.Work.Right&&captionPoint.Y>=monitor.Work.Top&&captionPoint.Y<monitor.Work.Bottom,"maximized caption buttons remain on screen");
            CaptureVisual(Find<Grid>("RootGrid"),"maximized-live.png");Button("CaptionMax").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(150);
            Window.Hide();QueueTraySingle();await Task.Delay(60);OpenFromTrayDouble();await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime+100);
            Require(Window.IsVisible&&(panel==null||!panel.IsVisible),"double click opens settings and cancels pending single click");
            suppressTrayClick=DateTime.MinValue;QueueTraySingle();await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime+140);Require(panel!=null&&panel.IsVisible,"single click opens flyout");DismissPanel(panel);await Task.Delay(120);
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"\nOpening native picker");
            string cancelled=NativeFolderPicker.Pick(Window,@"D:\","选择 Codex 项目工作目录 · 测试",true);Require(cancelled==null,"native Windows folder picker opens and cancels");
            Require(Window.FindName("Account")==null,"CLI account has no dropdown");Require(SelectedKey()==engine.CliKey(),"CLI automatically chooses other account");
            Require(Find<Expander>("CopyDetails").IsExpanded==false,"migration stage details start collapsed");
            var input=Find<TextBox>("WorkingDir");Require(input.Text==@"D:\","project defaults to D root");
            input.ApplyTemplate();var rounded=input.Template.FindName("InputSurface",input) as Border;Require(rounded!=null&&rounded.CornerRadius.TopLeft>=6,"rounded project input");
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"\nRendering icon");
            File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cli-ring-4k.png"),RingIcon.Png(RingIcon.Draw(4096,null,0)));
            var iconBytes=RingIcon.Ico(null,0);File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ring-app.ico"),iconBytes);
            using(var stream=new MemoryStream(iconBytes))using(var icon=new System.Drawing.Icon(stream,new System.Drawing.Size(32,32)))Require(icon.Width==32,"multi-size transparent icon loads");
            var profileDialog=CreateProfileSettingsDialog();profileDialog.Show();await Task.Delay(150);CaptureVisual((FrameworkElement)profileDialog.Content,"profile-settings-live.png");profileDialog.Close();
            var trayMenu=CreateTrayMenu();trayMenu.Placement=PlacementMode.AbsolutePoint;var pointer=System.Windows.Forms.Cursor.Position;
            var pointerDip=PresentationSource.FromVisual(Window).CompositionTarget.TransformFromDevice.Transform(new Point(pointer.X,pointer.Y));
            trayMenu.HorizontalOffset=pointerDip.X<SystemParameters.WorkArea.Left+350?SystemParameters.WorkArea.Right-270:SystemParameters.WorkArea.Left+10;trayMenu.VerticalOffset=SystemParameters.WorkArea.Top+70;
            trayMenu.IsOpen=true;await Task.Delay(150);CaptureVisual(trayMenu,"tray-menu-live.png");await Task.Delay(3500);Require(!trayMenu.IsOpen,"context menu dismisses after three seconds outside");

            
            // All scrollbar instances resolve to the Fluent template, not the OS classic template.
            var styled=Window.Resources[typeof(ScrollBar)] as Style;Require(styled!=null,"shared slim scrollbar style");
            var pageScroll=Descendant<ScrollViewer>(Find<Grid>("RootGrid"));
            pageScroll.ScrollToEnd();await Task.Delay(100);
            Require(pageScroll.ScrollableHeight==0||pageScroll.VerticalOffset>0,"custom scrollbar scrolls content");
            pageScroll.ScrollToHome();await Task.Delay(100);
            var thumb=Descendant<Thumb>(pageScroll);
            Require(thumb!=null,"custom scrollbar has draggable thumb");
            var thumbFill=thumb.Template.FindName("ThumbFill",thumb) as Border;
            Require(thumbFill!=null&&thumbFill.Width<=6,"scrollbar thumb remains slim");
            for(int i=0;i<5;i++){
                ShowPanel();await Task.Delay(100);var flyout=panel;Require(flyout!=null&&flyout.IsVisible,"panel opens");
                var panelChrome=System.Windows.Shell.WindowChrome.GetWindowChrome(flyout);
                Require(panelChrome!=null&&panelChrome.GlassFrameThickness==new Thickness(0),"panel has no native frame inset");
                var panelSurface=flyout.Content as Border;Require(panelSurface!=null&&panelSurface.CornerRadius.TopLeft==8&&panelSurface.Clip is RectangleGeometry,"panel border and clipping share system corner radius");
                if(i==0){CaptureVisual((FrameworkElement)flyout.Content,"status-live.png");}
                Require(Descendant<Button>((DependencyObject)flyout.Content).Content.ToString().Contains("设置"),"flyout contains top settings action, no close or info action");DismissPanel(flyout);
                await Task.Delay(100);Require(!flyout.IsVisible,"close button and dismiss hide safely");
            }
            ShowPanel();await Task.Delay(100);Window.Activate();await Task.Delay(150);Require(panel==null||!panel.IsVisible,"outside activation dismisses panel");
            Page(true);await Task.Delay(200);CaptureVisual(Find<Grid>("RootGrid"),"cli-live.png");
            var shell=Find<ComboBox>("Shell");shell.IsDropDownOpen=true;await Task.Delay(100);Require(shell.Items.Count==2,"terminal popup has two options");shell.SelectedIndex=1;shell.IsDropDownOpen=false;
            combo.SelectedIndex=1;await Task.Delay(150);Page(false);await Task.Delay(200);CaptureVisual(Find<Grid>("RootGrid"),"light-live.png");
            Button("CaptionClose").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(150);Require(!Window.IsVisible,"main close hides to tray");
            Window.Show();Window.Activate();await Task.Delay(100);Require(Window.IsVisible,"main can reopen after close");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),"PASS: integrated titlebar; 46px caption targets; all themes; three-item overlay menu; shared slim scrollbars; five panel open/dismiss/reopen cycles; outside activation dismiss; main close-to-tray/reopen; terminal selection; monitor work-area maximize and caption visibility; single/double tray gestures; native Windows folder picker; automatic distinct CLI account; default D project; collapsed migration stages; transparent multi-resolution ring icon and 4K source render; profile settings dialog; context menu automatic dismissal.\r\nNo account switched, no terminal launched, no network quota query.");
            exiting=true;Application.Current.Shutdown(0);
        }catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-results.txt"),e.ToString());exiting=true;Application.Current.Shutdown(1);}
    }
}
}
