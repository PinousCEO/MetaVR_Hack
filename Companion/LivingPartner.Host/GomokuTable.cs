using System.Diagnostics;
using Newtonsoft.Json.Linq;
using LivingPartner.Shared;

namespace LivingPartner.Host;

public record TableDisplay(string Id,string Name,bool Calibrated);
public record TableLayout(string Viewer,string MonitorId,bool Full,double Left,double Top,double Width,double Height);
public record TableClick(string Viewer,string MonitorId,int Round,int Revision,long Seq,double? U,double? V);
public record TableWire(bool active,int turn,int round,int revision,string monitorId,string status);

/// <summary>The local host referees both players. Camera pixels live in memory only for the latest white-turn frame.</summary>
public sealed class GomokuTable
{
    public const int Size=15;
    readonly object gate=new();
    readonly Func<double> time;
    readonly int[] stones=new int[Size*Size];
    TableDisplay[] displays=[];
    TableLayout layout;
    string monitor="", status="", guestNotice="";
    int round,revision,turn,moves,last=-1,hover=-1;
    bool waiting,connected,notify,ask,open;
    double seen,hoverAt,lastPoint,frameAt,requestAt,noticeAt;
    long wearerSeq=-1,cameraSeq=-1,pointSeq=-1;
    readonly Dictionary<long,double> delivered=new();
    JObject frame;
    public GomokuTable(Func<double> clock=null) {time=clock??(()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);}
    public string Wanted {get{lock(gate)return waiting||turn!=0||moves>0?monitor:"";}}
    bool IsLive => turn!=0||moves>0;
    bool Full => layout?.Full==true && time()-seen<4 && layout.MonitorId==monitor;
    TableWire Wire()=>new(IsLive,turn,round,revision,monitor,status);
    void CursorOff(){hover=-1;hoverAt=0;lastPoint=0;}
    void ClearFrame(){frame=null;delivered.Clear();cameraSeq=pointSeq=-1;guestNotice="";}
    void Begin()
    {
        Array.Clear(stones);round++;revision=1;turn=1;moves=0;last=-1;wearerSeq=-1;waiting=false;
        ClearFrame();CursorOff();notify=true;status="흑 차례 · 헤드셋에서 교차점을 가리키고 핀치하세요.";
    }
    void End(string reason)
    {
        if(!waiting&&!IsLive)return;
        Array.Clear(stones);moves=turn=0;waiting=ask=open=false;last=-1;revision++;CursorOff();ClearFrame();status=reason;notify=true;
    }
    public void Disconnect(){lock(gate){connected=false;End("헤드셋 연결이 끊겼어요.");}}
    public void Stop(){lock(gate)End("대국을 마쳤어요.");}
    public void Stop(int r,string id){lock(gate)if(r==round&&id==monitor)End("대국을 마쳤어요.");}
    public void Update(bool paired,TableDisplay[] available,Action<TableWire> state,Action<string> start,Action show)
    {
        TableWire send=null;string launch=null;bool showPage;
        lock(gate)
        {
            displays=available;connected=paired;
            if(!paired)End("먼저 Quest를 연결해 주세요.");
            else if((waiting||IsLive)&&!displays.Any(d=>d.Id==monitor&&d.Calibrated))End("TV 위치를 다시 확인해 주세요.");
            if(IsLive&&!Full)End("TV 전체 화면이 닫혔어요.");
            if(waiting&&Full)Begin();
            if(waiting&&time()-requestAt>90)End("TV에서 다시 대국을 시작하세요.");
            if(time()-lastPoint>.8)CursorOff();
            if(frame!=null && time()-frameAt>1.1)frame=null;
            if(guestNotice.Length>0 && time()-noticeAt>4)guestNotice="";
            if(ask){launch=monitor;ask=false;}
            if(notify){send=Wire();notify=false;}
            showPage=open;open=false;
        }
        if(showPage)show?.Invoke();
        if(launch!=null)start(launch);
        if(send!=null)state(send);
    }
    public string Request(string id,bool fromQuest=false)
    {
        lock(gate)
        {
            if(!connected)return "Quest를 먼저 연결해 주세요.";
            if(string.IsNullOrEmpty(id))id=displays.FirstOrDefault(d=>d.Id==layout?.MonitorId&&d.Calibrated)?.Id??displays.FirstOrDefault(d=>d.Calibrated)?.Id;
            if(!displays.Any(d=>d.Id==id&&d.Calibrated))return "먼저 헤드셋에서 TV 위치를 확인해 주세요.";
            if(waiting||IsLive){if(id!=monitor)return "이미 다른 TV에서 대국 중이에요.";notify=true;return "";}
            monitor=id;waiting=true;requestAt=time();notify=true;status="선택한 TV에서 오목판을 전체 화면으로 여세요.";
            ask=!fromQuest;open=!Full;
            if(Full)Begin();
            return "";
        }
    }
    public bool Register(TableLayout value)
    {
        if(value==null || !Guid.TryParse(value.Viewer,out _) || value.MonitorId==null)return false;
        lock(gate)
        {
            if(!value.Full){if(layout?.Viewer==value.Viewer){layout=null;End("TV 전체 화면이 닫혔어요.");}return true;}
            if(!displays.Any(d=>d.Id==value.MonitorId&&d.Calibrated) || (waiting||IsLive)&&value.MonitorId!=monitor)return false;
            if(layout!=null && layout.Viewer!=value.Viewer && time()-seen<4)return false;
            if(!double.IsFinite(value.Left+value.Top+value.Width+value.Height) || value.Left<0||value.Top<0||value.Width<.08||value.Height<.08||
               value.Left+value.Width>1.001||value.Top+value.Height>1.001)return false;
            layout=value;seen=time();if(waiting&&Full)Begin();return true;
        }
    }
    public object View()
    {
        lock(gate)return new {active=IsLive,waiting,connected,monitorId=monitor,round,revision,turn,status,guestNotice,displays,
            stones=(int[])stones.Clone(),last,hover=time()-lastPoint<.8?hover:-1,dwell=hover<0||turn!=2?0:Math.Clamp((time()-hoverAt)/.9,0,1),moves};
    }
    public string Button(TableClick p,string action)
    {
        lock(gate)
        {
            if(p==null || !Guid.TryParse(p.Viewer,out _) || p.Viewer!=layout?.Viewer||!layout.Full||time()-seen>=4||p.MonitorId!=layout.MonitorId)return "이 TV에서 전체 화면을 먼저 켜세요.";
            if(action=="start")return Request(p.MonitorId);
            if(!IsLive||p.Round!=round||p.Revision!=revision||p.MonitorId!=monitor)return "판이 바뀌었어요.";
            if(action=="stop"){End("대국을 마쳤어요.");return "";}
            if(action=="restart"){Begin();ask=true;return "";}
            return "알 수 없는 명령이에요.";
        }
    }
    bool Matches(int r,int rev,string id,int side)=>Full&&IsLive&&r==round&&rev==revision&&id==monitor&&turn==side;
    int Cell(double? u,double? v)
    {
        if(u==null||v==null||!double.IsFinite(u.Value+v.Value))return -1;
        double x=(u.Value-layout.Left)/layout.Width*(Size-1),y=(v.Value-layout.Top)/layout.Height*(Size-1);
        if(x<-.42||x>14.42||y<-.42||y>14.42)return -1;
        return (int)Math.Round(y)*Size+(int)Math.Round(x);
    }
    public void Wearer(JObject p)
    {
        lock(gate)
        {
            if(p?.Value<string>("side")!="wearer"||!Matches((int?)p["round"]??-1,(int?)p["revision"]??-1,(string)p["monitorId"],1))return;
            long seq=(long?)p["seq"]??-1;if(seq<=wearerSeq)return;wearerSeq=seq;
            int cell=Cell((double?)p["u"],(double?)p["v"]);hover=cell;lastPoint=time();
            if((bool?)p["commit"]==true&&cell>=0&&stones[cell]==0)Place(cell);
        }
    }
    void Place(int cell)
    {
        int side=turn;stones[cell]=side;moves++;last=cell;revision++;CursorOff();ClearFrame();notify=true;
        bool won=false;
        foreach(var (dx,dy) in new[]{(1,0),(0,1),(1,1),(1,-1)}){
            int run=1;
            foreach(int sign in new[]{-1,1})for(int x=cell%Size+dx*sign,y=cell/Size+dy*sign;x>=0&&x<Size&&y>=0&&y<Size&&stones[y*Size+x]==side;x+=dx*sign,y+=dy*sign)run++;
            if(run>=5){won=true;break;}
        }
        turn=won||moves==stones.Length?0:3-side;
        status=won?(side==1?"흑 승리":"백 승리"):turn==0?"무승부":turn==1?"흑 차례 · 헤드셋에서 교차점을 가리키고 핀치하세요.":"착용자는 상대 손과 TV를 함께 보세요. 상대는 원하는 칸을 0.9초 가리켜요.";
    }
    public void Camera(JObject p)
    {
        lock(gate)
        {
            if(p==null||!Matches((int?)p["round"]??-1,(int?)p["revision"]??-1,(string)p["monitorId"],2))return;
            long seq=(long?)p["seq"]??-1;string jpeg=(string)p["jpeg"];
            if(seq<=cameraSeq || jpeg==null || jpeg.Length is < 16 or > 700000 || (int?)p["w"] is not (>=32 and <=1280) || (int?)p["h"] is not (>=32 and <=1280))return;
            cameraSeq=seq;frameAt=time();frame=p;
        }
    }
    public object Latest(string viewer,long after)
    {
        lock(gate)
        {
            if(!Full||turn!=2||layout.Viewer!=viewer||frame==null||time()-frameAt>1.1||after>=cameraSeq)return null;
            foreach(var item in delivered.Where(x=>time()-x.Value>1.1).ToArray())delivered.Remove(item.Key);
            delivered[cameraSeq]=frameAt;
            return frame.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
    public bool Guest(TableClick p)
    {
        lock(gate)
        {
            if(p==null||layout?.Viewer!=p.Viewer||!Matches(p.Round,p.Revision,p.MonitorId,2)||p.Seq<=pointSeq||
               !delivered.Remove(p.Seq,out var captured)||time()-captured>1.1)return false;
            pointSeq=p.Seq;int cell=Cell(p.U,p.V);
            if(cell<0||stones[cell]!=0){CursorOff();return true;}
            if(cell!=hover||time()-lastPoint>.8){hover=cell;hoverAt=captured;}
            lastPoint=time();
            if(captured-hoverAt>=.9)Place(cell);
            return true;
        }
    }
    public void Notice(int r,string id,string reason)
    {
        lock(gate)if(IsLive&&r==round&&id==monitor){guestNotice=(reason??"")[..Math.Min(120,(reason??"").Length)];noticeAt=time();}
    }
    public void Packet(Envelope e)
    {
        var p=e.Body<JObject>();if(p==null)return;
        string id=(string)p["monitorId"];int r=(int?)p["round"]??-1;
        switch(e.type){
            case "gomoku.start":Request(id,true);break;
            case "gomoku.stop":Stop(r,id);break;
            case "gomoku.input":Wearer(p);break;
            case "gomoku.frame":Camera(p);break;
            case "gomoku.camera":
                lock(gate)if(!Matches(r,(int?)p["revision"]??-1,id,2))return;
                Notice(r,id,(string)p["status"] switch{
                    "consent"=>"착용자가 오른손을 핀치해 상대의 손을 볼 수 있게 해 주세요.",
                    "disabled"=>"Quest 설정에서 카메라를 켜세요.","permission"=>"Quest에서 헤드셋 카메라 권한을 허용하세요.",
                    "tracking"=>"상대의 손과 TV가 보이도록 고개를 돌려 주세요.","warming"=>"헤드셋 카메라를 준비하는 중이에요.",_=>""});
                break;
        }
    }
}
