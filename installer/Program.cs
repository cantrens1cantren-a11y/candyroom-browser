using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CandyroomSetup;

static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new SetupWindow()); }
}

sealed class SetupWindow : Form
{
    readonly string installDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Candyroom","Application");
    readonly Button install=new(){Text="Установить",AutoSize=false,Width=170,Height=42,BackColor=Color.FromArgb(143,96,205),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
    readonly Button cancel=new(){Text="Отмена",AutoSize=false,Width=120,Height=42,BackColor=Color.FromArgb(48,46,54),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
    readonly CheckBox desktop=new(){Text="Создать ярлык на рабочем столе",Checked=true,AutoSize=true,ForeColor=Color.FromArgb(238,233,245)};
    readonly CheckBox launch=new(){Text="Запустить Candyroom после установки",Checked=true,AutoSize=true,ForeColor=Color.FromArgb(238,233,245)};
    readonly Label status=new(){AutoSize=true,ForeColor=Color.FromArgb(184,172,195),MaximumSize=new Size(510,0)};
    public SetupWindow()
    {
        Text="Установка Candyroom";ClientSize=new Size(560,370);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
        BackColor=Color.FromArgb(23,22,27);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        var title=new Label{Text="candyroom",Font=new Font("Segoe UI",25,FontStyle.Bold),ForeColor=Color.FromArgb(179,139,238),AutoSize=true,Location=new Point(30,24)};
        var subtitle=new Label{Text="Установка браузера",Font=new Font("Segoe UI",12),AutoSize=true,Location=new Point(32,82)};
        var location=new Label{Text="Папка установки:\n"+installDir,AutoSize=true,MaximumSize=new Size(500,0),ForeColor=Color.FromArgb(184,172,195),Location=new Point(32,121)};
        desktop.Location=new Point(35,180);launch.Location=new Point(35,211);status.Location=new Point(35,244);
        install.Location=new Point(228,306);cancel.Location=new Point(408,306); install.FlatAppearance.BorderSize=0;cancel.FlatAppearance.BorderSize=0;
        Controls.AddRange(new Control[]{title,subtitle,location,desktop,launch,status,install,cancel});
        cancel.Click+=(_,_)=>Close();install.Click+=(_,_)=>Install();
    }
    void Install()
    {
        string exe=Path.Combine(installDir,"Candyroom.exe");
        foreach(var p in Process.GetProcessesByName("Candyroom"))
        {
            try { if(string.Equals(p.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase)) {MessageBox.Show("Закрой Candyroom перед установкой или обновлением, затем попробуй снова.","Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Information);return;} }
            catch { /* If process inspection is denied, extraction reports any locked files. */ }
            finally { p.Dispose(); }
        }
        install.Enabled=false;cancel.Enabled=false;status.Text="Устанавливаю...";status.Refresh();
        try
        {
            Directory.CreateDirectory(installDir);
            using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Candyroom.Payload.zip") ?? throw new InvalidOperationException("В установщике нет файлов браузера.");
            using var archive=new ZipArchive(stream,ZipArchiveMode.Read);
            string root=Path.GetFullPath(installDir)+Path.DirectorySeparatorChar;
            foreach(var entry in archive.Entries)
            {
                string dest=Path.GetFullPath(Path.Combine(installDir,entry.FullName));
                if(!dest.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Некорректный путь в архиве установки.");
                if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(dest);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);entry.ExtractToFile(dest,true);
            }
            if(!File.Exists(exe))throw new FileNotFoundException("Candyroom.exe отсутствует в архиве установки.");
            var warnings=new System.Collections.Generic.List<string>();
            try { string programs=Environment.GetFolderPath(Environment.SpecialFolder.Programs);string folder=Path.Combine(programs,"Candyroom");Directory.CreateDirectory(folder);Shortcut(Path.Combine(folder,"Candyroom.lnk"),exe); }
            catch(Exception ex){warnings.Add("Не удалось создать ярлык в меню Пуск: "+ex.Message);}
            if(desktop.Checked)try{Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Candyroom.lnk"),exe);}
            catch(Exception ex){warnings.Add("Не удалось создать ярлык на рабочем столе: "+ex.Message);}
            status.Text="Установка завершена.";
            if(warnings.Count>0)MessageBox.Show(string.Join("\n",warnings)+"\n\nБраузер установлен: "+exe,"Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            else MessageBox.Show("Candyroom установлен!","Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Information);
            if(launch.Checked)try{Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=installDir});}catch(Exception ex){MessageBox.Show("Браузер установлен, но не запустился: "+ex.Message,"Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            Close();
        }
        catch(Exception ex)
        {
            status.Text="Ошибка установки. Попробуй ещё раз.";
            MessageBox.Show("Не удалось установить Candyroom: "+ex.Message+"\n\nЕсли браузер открыт, закрой его и повтори установку.","Candyroom",MessageBoxButtons.OK,MessageBoxIcon.Error);
            install.Enabled=true;cancel.Enabled=true;
        }
    }
    static void Shortcut(string shortcutPath,string exe)
    {
        Type type=Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host недоступен.");
        object shell=Activator.CreateInstance(type) ?? throw new InvalidOperationException("Не удалось создать ярлык.");
        object? shortcut=null;
        try
        {
            shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcutPath});
            if(shortcut==null)throw new InvalidOperationException("Не удалось создать ярлык.");
            var t=shortcut.GetType();
            t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{exe});
            t.InvokeMember("IconLocation",BindingFlags.SetProperty,null,shortcut,new object[]{exe+",0"});
            t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Path.GetDirectoryName(exe)!});
            t.InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"Candyroom Browser"});
            t.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
        }
        finally {if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);if(Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
    }
}
