using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Candyroom;

static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new BrowserWindow()); }
}

static class Theme
{
    public static readonly Color Background=Color.FromArgb(19,19,22), Panel=Color.FromArgb(28,28,32), Input=Color.FromArgb(44,44,49), Accent=Color.FromArgb(137,95,207), Hover=Color.FromArgb(59,56,65), Text=Color.FromArgb(245,243,250), Muted=Color.FromArgb(174,169,184);
    public static GraphicsPath Round(Rectangle r,int radius)
    {
        var p=new GraphicsPath(); int d=Math.Max(2,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
}
sealed class RoundButton : Control
{
    bool hovered, active;
    public bool WindowButton {get;set;}
    public bool DangerButton {get;set;}
    public bool Active {get=>active;set{active=value;Invalidate();}}
    public RoundButton(string label,string accessible)
    {
        Text=label; AccessibleName=accessible; AccessibleRole=AccessibleRole.PushButton;
        Font=new Font("Segoe UI",11,FontStyle.Regular); ForeColor=Theme.Text;
        BackColor=Theme.Panel; Cursor=Cursors.Hand; Margin=new Padding(3); TabStop=true; Dock=DockStyle.Fill;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        MouseEnter+=(_,_)=>{hovered=true;Invalidate();};MouseLeave+=(_,_)=>{hovered=false;Invalidate();};EnabledChanged+=(_,_)=>Invalidate();
    }
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new Rectangle(1,1,Width-2,Height-2);if(r.Width<3||r.Height<3)return;
        if(Active||hovered){using var p=Theme.Round(r,10);using var b=new SolidBrush(Active?Theme.Accent:(DangerButton?Color.FromArgb(190,49,62):Theme.Hover));e.Graphics.FillPath(b,p);}
        if(WindowButton)
        {
            using var pen=new Pen(Theme.Text,1.5f);float cx=Width/2f,cy=Height/2f;
            if(AccessibleName=="Свернуть") e.Graphics.DrawLine(pen,cx-6,cy+3,cx+6,cy+3);
            else if(AccessibleName=="Развернуть")e.Graphics.DrawRectangle(pen,cx-5.5f,cy-5.5f,11,11);
            else {e.Graphics.DrawLine(pen,cx-5,cy-5,cx+5,cy+5);e.Graphics.DrawLine(pen,cx+5,cy-5,cx-5,cy+5);}
        }
        else TextRenderer.DrawText(e.Graphics,Text,Font,r,Enabled?Theme.Text:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        if(Focused){using var p=Theme.Round(r,10);using var pen=new Pen(Theme.Muted,1);e.Graphics.DrawPath(pen,p);}
    }
}
sealed class RoundedInput : Panel
{
    public TextBox Box {get;}=new(){BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",11),BackColor=Theme.Input,ForeColor=Theme.Text};
    public RoundedInput()
    {
        BackColor=Theme.Panel; Padding=new Padding(14,11,14,8);Height=40; Box.Dock=DockStyle.Fill;Controls.Add(Box);DoubleBuffered=true;ResizeRedraw=true;
    }
    protected override void OnPaint(PaintEventArgs e) {base.OnPaint(e);if(Width<3||Height<3)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var p=Theme.Round(new Rectangle(0,0,Width-1,Height-1),16);using var b=new SolidBrush(Theme.Input);e.Graphics.FillPath(b,p);}
}
sealed class Settings
{
    public string SearchEngine {get;set;}="DuckDuckGo";
    public string HomeUrl {get;set;}="";
    public int ZoomPercent {get;set;}=100;
}
sealed class BrowserTab
{
    public WebView2 View {get;}=new(){Dock=DockStyle.Fill,Visible=false};
    public Panel Header {get;}=new(){Width=215,Height=38,Margin=new Padding(3,2,3,2),BackColor=Theme.Panel};
    public RoundButton Title {get;}=new("Новая вкладка","Выбрать вкладку");
    public RoundButton Close {get;}=new("×","Закрыть вкладку");
    public bool Disposed {get;set;}
}
sealed class BrowserWindow : Form
{
    readonly FlowLayoutPanel tabStrip=new(){Dock=DockStyle.Fill,WrapContents=false,AutoScroll=false,Padding=new Padding(7,4,7,0),BackColor=Theme.Panel};
    readonly Panel content=new(){Dock=DockStyle.Fill,BackColor=Theme.Background,Margin=Padding.Empty};
    readonly RoundedInput address=new(){Dock=DockStyle.Fill,Margin=new Padding(6,0,6,0)};
    readonly RoundButton back=new("‹","Назад"),forward=new("›","Вперёд"),refresh=new("⟳","Обновить"),home=new("⌂","Домой"),go=new("→","Перейти"),settingsButton=new("⚙","Настройки"),newTabButton=new("+","Новая вкладка");
    readonly RoundButton minimize=new("−","Свернуть"),maximize=new("□","Развернуть"),closeWindow=new("×","Закрыть окно");
    readonly List<BrowserTab> tabs=new(); BrowserTab? selected;
    CoreWebView2Environment? environment; Settings settings=new();
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Candyroom","settings.json");
    bool closing;
    string HomeHtml()
    {
        string action=settings.SearchEngine=="Google"?"https://www.google.com/search":settings.SearchEngine=="Яндекс"?"https://yandex.ru/search/":"https://duckduckgo.com/";
        string field=settings.SearchEngine=="Яндекс"?"text":"q";
        return """
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Новая вкладка</title><style>
*{box-sizing:border-box}html,body{height:100%}body{margin:0;background:#131316;color:#f6f3fb;font-family:Segoe UI,Arial,sans-serif;display:grid;place-items:center}main{width:min(660px,88vw);text-align:center;transform:translateY(-28px)}h1{margin:0 0 34px;font-size:clamp(44px,6vw,70px);letter-spacing:-3px;line-height:1}h1 span{color:#ac81e9}form{display:flex;align-items:center;background:#29292f;border:1px solid #46414e;border-radius:14px;padding:5px 6px 5px 20px;box-shadow:0 10px 40px #0005}form:focus-within{border-color:#a879e9;box-shadow:0 0 0 2px #a879e944}input{width:100%;height:46px;outline:0;border:0;background:transparent;color:#f9f7fc;font-size:17px}input::placeholder{color:#9d98a4}button{height:42px;border:0;border-radius:10px;padding:0 20px;background:#9364d0;color:white;font-size:15px;font-weight:600;cursor:pointer}button:hover{background:#a77ae2}button:focus-visible{outline:2px solid #e6d2ff;outline-offset:2px}</style></head><body><main><h1>candy<span>room</span></h1><form action="__ACTION__" method="get"><input name="__FIELD__" aria-label="Поиск в интернете" placeholder="Поиск в интернете" autocomplete="off"><button type="submit">Найти</button></form></main></body></html>
""".Replace("__ACTION__",action).Replace("__FIELD__",field);
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,IntPtr lParam);
    void DragTitle(object? sender,MouseEventArgs e)
    {
        if(e.Button!=MouseButtons.Left)return;
        if(e.Clicks==2){ToggleMaximize();return;}
        ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);
    }
    void ToggleMaximize(){MaximizedBounds=Screen.FromControl(this).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;maximize.Text=WindowState==FormWindowState.Maximized?"❐":"□";maximize.Invalidate();}
    protected override void WndProc(ref Message m)
    {
        const int NCHITTEST=0x84, CLIENT=1, LEFT=10, RIGHT=11, TOP=12, TOPLEFT=13, TOPRIGHT=14, BOTTOM=15, BOTTOMLEFT=16, BOTTOMRIGHT=17;
        if(m.Msg==NCHITTEST&&WindowState==FormWindowState.Normal){
            base.WndProc(ref m);if(m.Result==(IntPtr)CLIENT){long lp=m.LParam.ToInt64();var pt=PointToClient(new Point(unchecked((short)(lp&0xffff)),unchecked((short)((lp>>16)&0xffff))));int edge=7;
                bool l=pt.X<edge,r=pt.X>=ClientSize.Width-edge,t=pt.Y<edge,b=pt.Y>=ClientSize.Height-edge;
                int hit=t&&l?TOPLEFT:t&&r?TOPRIGHT:b&&l?BOTTOMLEFT:b&&r?BOTTOMRIGHT:l?LEFT:r?RIGHT:t?TOP:b?BOTTOM:CLIENT;
                m.Result=(IntPtr)hit;}return;
        }base.WndProc(ref m);
    }
    public BrowserWindow()
    {
        Text="Candyroom Browser";
        try { Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* Browser remains usable if icon loading fails. */ }
        FormBorderStyle=FormBorderStyle.None;Width=1280;Height=820;MinimumSize=new Size(720,480);StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Background;Font=new Font("Segoe UI",10);KeyPreview=true;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=Theme.Background};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var nav=new TableLayoutPanel{Dock=DockStyle.Fill,Height=60,Padding=new Padding(8,8,8,8),ColumnCount=7,RowCount=1,BackColor=Theme.Panel,Margin=Padding.Empty};
        nav.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int i=0;i<4;i++)nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,46));nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,46));
        nav.Controls.Add(back,0,0);nav.Controls.Add(forward,1,0);nav.Controls.Add(refresh,2,0);nav.Controls.Add(home,3,0);nav.Controls.Add(address,4,0);nav.Controls.Add(go,5,0);nav.Controls.Add(settingsButton,6,0);
        var top=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,BackColor=Theme.Panel,Margin=Padding.Empty};
        top.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        minimize.WindowButton=maximize.WindowButton=closeWindow.WindowButton=true;closeWindow.DangerButton=true;
        minimize.Dock=maximize.Dock=closeWindow.Dock=DockStyle.Fill;minimize.Margin=maximize.Margin=closeWindow.Margin=new Padding(1,3,1,3);
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<3;i++)top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,46));
        top.Controls.Add(tabStrip,0,0);top.Controls.Add(minimize,1,0);top.Controls.Add(maximize,2,0);top.Controls.Add(closeWindow,3,0);
        root.Controls.Add(top,0,0);root.Controls.Add(nav,0,1);root.Controls.Add(content,0,2);Controls.Add(root);
        tabStrip.MouseDown+=DragTitle;top.MouseDown+=DragTitle;
        minimize.Click+=(_,_)=>WindowState=FormWindowState.Minimized;maximize.Click+=(_,_)=>ToggleMaximize();closeWindow.Click+=(_,_)=>Close();
        newTabButton.Dock=DockStyle.None;newTabButton.Size=new Size(40,38);tabStrip.Controls.Add(newTabButton);
        tabStrip.SizeChanged+=(_,_)=>ResizeTabHeaders();
        back.Click+=(_,_)=>{var c=selected?.View.CoreWebView2;if(c?.CanGoBack==true)c.GoBack();};
        forward.Click+=(_,_)=>{var c=selected?.View.CoreWebView2;if(c?.CanGoForward==true)c.GoForward();};
        refresh.Click+=(_,_)=>selected?.View.CoreWebView2?.Reload();home.Click+=(_,_)=>GoHome();go.Click+=(_,_)=>NavigateInput();
        newTabButton.Click+=async (_,_)=>await CreateTab();settingsButton.Click+=async (_,_)=>await OpenSettings();
        address.Box.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){NavigateInput();e.SuppressKeyPress=true;}};
        Shown+=async (_,_)=>await Start();FormClosing+=(_,_)=>closing=true;
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys data)
    {
        if(data==(Keys.Control|Keys.Oemcomma)){_=OpenSettings();return true;}
        if(data==(Keys.Control|Keys.T)){_=CreateTab();return true;}
        if(data==(Keys.Control|Keys.W)){if(selected!=null)CloseTab(selected);return true;}
        if(data==(Keys.Control|Keys.L)){address.Box.Focus();address.Box.SelectAll();return true;}
        if(data==Keys.F5){selected?.View.CoreWebView2?.Reload();return true;}
        if(data==(Keys.Alt|Keys.Left)){if(selected?.View.CoreWebView2?.CanGoBack==true)selected.View.CoreWebView2.GoBack();return true;}
        if(data==(Keys.Alt|Keys.Right)){if(selected?.View.CoreWebView2?.CanGoForward==true)selected.View.CoreWebView2.GoForward();return true;}
        return base.ProcessCmdKey(ref msg,data);
    }
    async Task Start()
    {
        try {
            if(File.Exists(settingsPath)) {try {settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath))??new Settings();}catch{settings=new Settings();}}
            if(settings.SearchEngine!="Google"&&settings.SearchEngine!="Яндекс")settings.SearchEngine="DuckDuckGo";
            settings.ZoomPercent=Math.Clamp(settings.ZoomPercent,50,200);
            var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Candyroom","BrowserProfile");
            environment=await CoreWebView2Environment.CreateAsync(null,dir);await CreateTab();
        } catch(Exception ex){MessageBox.Show("Не удалось запустить браузер. Установи WebView2 Runtime и попробуй снова.\n\n"+ex.Message,"Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    async Task CreateTab(string? url=null)
    {
        if(environment==null||closing)return;
        var tab=new BrowserTab();tab.Title.Dock=DockStyle.Fill;tab.Close.Dock=DockStyle.Right;tab.Close.Width=32;tab.Close.Margin=Padding.Empty;
        tab.Header.Controls.Add(tab.Title);tab.Header.Controls.Add(tab.Close);tab.Title.Click+=(_,_)=>SelectTab(tab);tab.Close.Click+=(_,_)=>CloseTab(tab);
        tabs.Add(tab);tabStrip.Controls.Add(tab.Header);tabStrip.Controls.SetChildIndex(newTabButton,tabStrip.Controls.Count-1);ResizeTabHeaders();content.Controls.Add(tab.View);SelectTab(tab);
        try {
            await tab.View.EnsureCoreWebView2Async(environment);if(tab.Disposed||closing)return;
            var core=tab.View.CoreWebView2;tab.View.ZoomFactor=settings.ZoomPercent/100.0;
            core.DocumentTitleChanged+=(_,_)=>{if(tab.Disposed)return;string title=core.DocumentTitle;tab.Title.Text=string.IsNullOrWhiteSpace(title)?"Новая вкладка":title;UpdateChrome();};
            core.SourceChanged+=(_,_)=>{if(tab==selected)UpdateChrome(true);};
            core.NavigationCompleted+=(_,_)=>{if(tab==selected)UpdateChrome(true);};
            core.HistoryChanged+=(_,_)=>{if(tab==selected)UpdateChrome();};
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var uri)&&(uri.Scheme=="http"||uri.Scheme=="https"))_=CreateTab(uri.AbsoluteUri);};
            if(url==null)tab.View.NavigateToString(HomeHtml());else core.Navigate(url);
            UpdateChrome(true);
        }catch(Exception ex){if(!tab.Disposed&&!closing){MessageBox.Show("Не удалось открыть вкладку: "+ex.Message,"Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Error);CloseTab(tab);}}
    }
    void ResizeTabHeaders()
    {
        if(tabs.Count==0)return;
        int available=Math.Max(0,tabStrip.ClientSize.Width-tabStrip.Padding.Horizontal-newTabButton.Width-newTabButton.Margin.Horizontal-4);
        int width=Math.Clamp(available/tabs.Count-6,40,215);
        foreach(var tab in tabs) {tab.Header.Width=width;tab.Close.Width=Math.Min(32,Math.Max(18,width/3));}
    }
    void SelectTab(BrowserTab tab)
    {
        if(tab.Disposed)return; selected=tab;foreach(var t in tabs){t.View.Visible=t==tab;t.Title.Active=t==tab;t.Title.Invalidate();}tab.View.BringToFront();UpdateChrome(true);
    }
    void CloseTab(BrowserTab tab)
    {
        if(tab.Disposed)return;int i=tabs.IndexOf(tab);bool wasSelected=selected==tab;tab.Disposed=true;tabs.Remove(tab);tabStrip.Controls.Remove(tab.Header);ResizeTabHeaders();content.Controls.Remove(tab.View);tab.View.Dispose();tab.Header.Dispose();
        if(wasSelected){selected=null;if(tabs.Count>0)SelectTab(tabs[Math.Clamp(i-1,0,tabs.Count-1)]);else _=CreateTab();}
    }
    void UpdateChrome(bool updateAddress=false)
    {
        var core=selected?.View.CoreWebView2;back.Enabled=core?.CanGoBack==true;forward.Enabled=core?.CanGoForward==true;
        if(updateAddress)address.Box.Text=core==null||core.Source=="about:blank"?"":core.Source;
        Text=(core==null||string.IsNullOrWhiteSpace(core.DocumentTitle)?"Candyroom":core.DocumentTitle)+" | Candyroom";
    }
    string SearchUrl(string query)=>settings.SearchEngine switch {"Google"=>"https://www.google.com/search?q="+Uri.EscapeDataString(query),"Яндекс"=>"https://yandex.ru/search/?text="+Uri.EscapeDataString(query),_=>"https://duckduckgo.com/?q="+Uri.EscapeDataString(query)};
    static bool WebAddress(string input,out string url)
    {
        url="";if(Uri.TryCreate(input,UriKind.Absolute,out var absolute)&&(absolute.Scheme==Uri.UriSchemeHttp||absolute.Scheme==Uri.UriSchemeHttps)){url=absolute.AbsoluteUri;return true;}
        if(!input.Contains(' ')&&!input.Contains("://")&&(input.Contains('.')||input.Equals("localhost",StringComparison.OrdinalIgnoreCase)||input.StartsWith("localhost:"))&&Uri.TryCreate("https://"+input,UriKind.Absolute,out var candidate)&&!string.IsNullOrEmpty(candidate.Host)){url=candidate.AbsoluteUri;return true;}return false;
    }
    void NavigateInput()
    {
        var core=selected?.View.CoreWebView2;if(core==null)return;string input=address.Box.Text.Trim();if(input.Length==0){GoHome();return;}
        try{core.Navigate(WebAddress(input,out string url)?url:SearchUrl(input));selected!.View.Focus();}catch(Exception ex){MessageBox.Show("Не удалось открыть адрес: "+ex.Message,"Candyroom");}
    }
    void GoHome()
    {
        if(selected?.View.CoreWebView2==null)return;
        if(!string.IsNullOrWhiteSpace(settings.HomeUrl)&&WebAddress(settings.HomeUrl,out var url))selected.View.CoreWebView2.Navigate(url);else selected.View.NavigateToString(HomeHtml());
        address.Box.Text="";
    }
    async Task OpenSettings()
    {
        if(environment==null){MessageBox.Show("Браузер ещё запускается. Попробуй открыть настройки снова.","Candyroom");return;}
        using var dialog=new SettingsDialog(settings);
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        if(dialog.ClearDataRequested)
        {
            try {var profile=selected?.View.CoreWebView2?.Profile;if(profile==null)throw new InvalidOperationException("Открой вкладку и попробуй ещё раз.");await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);}
            catch(Exception ex){MessageBox.Show("Не удалось очистить данные: "+ex.Message,"Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Error);return;}
        }
        settings=dialog.Value;
        try {Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));}
        catch(Exception ex){MessageBox.Show("Настройки изменены, но не сохранены: "+ex.Message,"Candyroom");}
        foreach(var t in tabs)if(!t.Disposed&&t.View.CoreWebView2!=null){t.View.ZoomFactor=settings.ZoomPercent/100.0;if(t.View.CoreWebView2.Source=="about:blank")t.View.NavigateToString(HomeHtml());}
        if(dialog.ClearDataRequested)MessageBox.Show("Данные сайтов очищены. Из некоторых аккаунтов может потребоваться войти заново.","Candyroom");
    }
}
sealed class SettingsDialog : Form
{
    readonly ComboBox search=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    readonly TextBox homepage=new(){Dock=DockStyle.Fill};
    readonly NumericUpDown zoom=new(){Minimum=50,Maximum=200,Increment=10,Dock=DockStyle.Left,Width=110};
    readonly CheckBox clear=new(){Text="Очистить данные сайтов и историю при сохранении",AutoSize=true,ForeColor=Theme.Text};
    public bool ClearDataRequested=>clear.Checked;
    public Settings Value => new(){SearchEngine=search.SelectedItem?.ToString()??"DuckDuckGo",HomeUrl=homepage.Text.Trim(),ZoomPercent=(int)zoom.Value};
    public SettingsDialog(Settings current)
    {
        Text="Настройки Candyroom";ClientSize=new Size(590,420);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;BackColor=Theme.Background;ForeColor=Theme.Text;Font=new Font("Segoe UI",10);
        search.BackColor=homepage.BackColor=zoom.BackColor=Theme.Input;search.ForeColor=homepage.ForeColor=zoom.ForeColor=Theme.Text;
        search.Items.AddRange(new object[]{"DuckDuckGo","Google","Яндекс"});search.SelectedItem=current.SearchEngine;if(search.SelectedIndex<0)search.SelectedIndex=0;
        homepage.Text=current.HomeUrl;zoom.Value=Math.Clamp(current.ZoomPercent,50,200);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(26,20,26,18)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        Label Label(string s)=>new(){Text=s,AutoSize=true,ForeColor=Theme.Text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
        layout.Controls.Add(Label("Поисковая система"),0,0);layout.Controls.Add(search,0,1);layout.Controls.Add(Label("Домашняя страница (пусто = Candyroom)"),0,2);layout.Controls.Add(homepage,0,3);
        layout.Controls.Add(Label("Масштаб страниц (%)"),0,4);layout.Controls.Add(zoom,0,5);layout.Controls.Add(clear,0,6);
        var warning=Label("Очистка удалит cookies и историю: возможен выход из аккаунтов.");warning.ForeColor=Theme.Muted;layout.Controls.Add(warning,0,7);
        var buttons=new FlowLayoutPanel{FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Fill,WrapContents=false};
        var save=new RoundButton("Сохранить","Сохранить настройки"){Width=135,Height=40,Active=true,Dock=DockStyle.None};var cancel=new RoundButton("Отмена","Отменить"){Width=110,Height=40,Dock=DockStyle.None};
        save.Click+=(_,_)=>{
            string input=homepage.Text.Trim();if(input.Length>0&&!ValidHomepage(input)){MessageBox.Show("Укажи адрес сайта, например https://example.com, или оставь поле пустым.","Candyroom");return;}
            if(clear.Checked&&MessageBox.Show("Удалить данные сайтов, cookies и историю? Это может выполнить выход из аккаунтов.","Очистка данных",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            DialogResult=DialogResult.OK;Close();};cancel.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};buttons.Controls.Add(save);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,9);Controls.Add(layout);
    }
    static bool ValidHomepage(string text)
    {
        if(!text.Contains("://"))text="https://"+text;
        return Uri.TryCreate(text,UriKind.Absolute,out var uri)&&(uri.Scheme=="http"||uri.Scheme=="https")&&!string.IsNullOrWhiteSpace(uri.Host);
    }
}
