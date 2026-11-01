using System.Drawing;
using System.Windows.Forms;

namespace CfTunnelManager.Services;

internal sealed class TrayIcon : IDisposable
{
    private const int MaxTextLength = 127;
    private const int BalloonMilliseconds = 5000;

    private readonly NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(Action show, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => show());
        menu.Items.Add("退出", null, (_, _) => exit());

        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        _image = new Icon(resource.Stream, SystemInformation.SmallIconSize);

        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = "Tunnel Manager",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                show();
            }
        };
    }

    public void SetText(string text)
    {
        _icon.Text = text.Length <= MaxTextLength ? text : text[..MaxTextLength];
    }

    public void Notify(string message)
    {
        _icon.ShowBalloonTip(BalloonMilliseconds, "Tunnel Manager", message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }
}
