using System.Drawing;
using System.Windows.Forms;
using LivingPartner.Shared;

namespace LivingPartner.Host;

sealed class ConnectionWindow : Form
{
    readonly Label device = new() { AutoSize = true };
    readonly Label code = new() { AutoSize = true };
    readonly Label help = new() { AutoSize = true, MaximumSize = new Size(355, 0) };
    readonly Button scan = new() { Text = "Locate screen again", AutoSize = true };
    readonly Core core;

    public ConnectionWindow(Core core)
    {
        this.core = core;
        Text = "Yonder"; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = new Font("Segoe UI",10); AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var panel = new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new Padding(17),
            AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, MinimumSize=new Size(330,0) };
        device.Font = new Font(Font.FontFamily,14,FontStyle.Bold);
        code.Font = new Font(Font.FontFamily,20,FontStyle.Bold);
        help.Margin = new Padding(3,6,3,12);
        panel.Controls.AddRange(new Control[]{device,code,help,scan});
        foreach (var (title,path) in new[]{("Demon Gate · Solo","/demon-gate/"),("Gomoku · Two players","/table/")})
        {
            var game = new Button { Text = title, AutoSize = true };
            game.Click += (_,_) => Program.OpenGamePage(core.Port,path);
            panel.Controls.Add(game);
        }
        Controls.Add(panel);
        scan.Click += (_,_) => core.Post(() => core.Broadcast(Msg.IntentResult, new IntentResultMsg { kind="calibrate" },core.IsMr));
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        bool connected = core.HasMr;
        device.Text = connected ? "Quest connected" : "Waiting for Quest";
        code.Visible = !connected; scan.Visible = connected;
        code.Text = connected ? "" : core.Auth.PairCode;
        help.Text = connected ? "Look at the monitor through your headset. You can also use a TV connected as a display." :
            "On Wi-Fi, enter this code in your headset. On USB, allow debugging in your headset.";
    }
}
