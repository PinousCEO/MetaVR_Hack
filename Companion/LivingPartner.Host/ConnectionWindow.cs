using System.Drawing;
using System.Windows.Forms;
using LivingPartner.Shared;

namespace LivingPartner.Host;

sealed class ConnectionWindow : Form
{
    readonly Label device = new() { AutoSize = true };
    readonly Label code = new() { AutoSize = true };
    readonly Label help = new() { AutoSize = true, MaximumSize = new Size(355, 0) };
    readonly Button scan = new() { Text = "화면 위치 다시 확인", AutoSize = true };
    readonly Core core;

    public ConnectionWindow(Core core)
    {
        this.core = core;
        Text = "밤비 Companion"; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = new Font("Malgun Gothic",10); AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var panel = new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown, WrapContents=false, Padding=new Padding(17),
            AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, MinimumSize=new Size(330,0) };
        device.Font = new Font(Font.FontFamily,14,FontStyle.Bold);
        code.Font = new Font(Font.FontFamily,20,FontStyle.Bold);
        help.Margin = new Padding(3,6,3,12);
        panel.Controls.AddRange(new Control[]{device,code,help,scan});
        foreach (var (title,path) in new[]{("악마의 문 · 혼자 플레이","/demon-gate/"),("오목 · Quest와 두 사람","/table/")})
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
        device.Text = connected ? "Quest가 연결됐어요" : "Quest 연결 대기 중";
        code.Visible = !connected; scan.Visible = connected;
        code.Text = connected ? "" : core.Auth.PairCode;
        help.Text = connected ? "헤드셋을 쓰고 모니터를 바라보세요. TV도 Windows의 디스플레이로 연결하면 사용할 수 있어요." :
            "Quest와 PC를 같은 Wi-Fi에 연결하고 헤드셋에서 위 번호를 입력하세요. USB로 연결했다면 헤드셋에 뜨는 디버깅 요청을 허용하세요.";
    }
}
