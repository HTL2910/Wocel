using System.Drawing;
using System.Windows.Forms;

namespace Wocel.Capture.Windows.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayService(Action show, Action capture, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Chụp vùng", null, (_, _) => capture());
        menu.Items.Add("Mở Wocel Capture", null, (_, _) => show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => exit());
        _icon = new NotifyIcon
        {
            Text = "Wocel Capture",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
