using System.Numerics;
using LivingPartner.Shared;

namespace LivingPartner.Mac;

sealed partial class Stage
{
    readonly TennisReferee Match = new();
    SwarmGlass swarm;
    string afterTransfer = "", tennisSound = "";
    double pendingAt, swarmAt, ackAt, tennisSoundAt;
    int tennisSoundId;

    void TennisChanged()
    {
        var s = Match.State;
        snap.tennis = s;
        if (s.phase == "rally" || s.phase == "point")
        {
            tennisSound = s.phase == "point" ? s.lastPoint == "user" ? "win" : "drop" : "hit";
            tennisSoundId++; tennisSoundAt = Now;
        }
        Send(headset, Msg.GameState, s);
        if (s.phase == "ready" || !s.active) Snapshot();
    }

    string LaunchNative(string id)
    {
        if (headset == null) return "Quest를 먼저 연결해 주세요.";
        if (machine.IsActive) return "밤비가 화면을 통과하면 시작할 수 있어요.";
        if (screen == null || snap.calibrations.Count == 0) return "TV를 전체 화면으로 선택하고 헤드셋에서 TV 위치를 확인해 주세요.";
        Table.Stop();
        StopNativeGames(Match.State.active || swarm!=null);
        StopGlassGames();
        needReturn = false;
        if (id != "game-swarm" && T.owner == Env.Mr)
        {
            string error = Command("enter");
            if (error.Length == 0) { afterTransfer = id; pendingAt = Now; }
            return error;
        }
        AskNative(id);
        return "";
    }

    void AskNative(string id)
    {
        Send(headset, Msg.IntentResult, new { kind="playGame", text = id=="game-swarm" ? SwarmRules.PackId : id=="game-keeper" ? "keeper" : "tennis" });
    }
    void StopNativeGames(bool tellHeadset)
    {
        afterTransfer=""; swarm=null; tennisSound="";
        Match.Stop();
        if (tellHeadset) Send(headset,Msg.IntentResult,new {kind="stopTennis",text=""});
    }
    void TickNativeGames()
    {
        if (afterTransfer.Length>0)
        {
            if (Now-pendingAt>45 || headset==null) afterTransfer="";
            else if (!machine.IsActive && T.owner==Env.Pc)
            {
                var id=afterTransfer; afterTransfer=""; Snapshot(); AskNative(id);
            }
        }
        if (swarm!=null && Now-swarmAt>2) swarm=null;
        if (Match.State.active && (headset==null || (Match.State.court.room ? T.owner!=Env.Mr : screen==null || T.owner!=Env.Pc))) Match.Stop();
        Match.Tick(Now);
    }
    public void SwarmVisible()
    {
        if (swarm==null || Now-swarmAt>.6 || Now<ackAt) return;
        ackAt=Now+.40;
        Send(headset,Msg.SwarmState,new SwarmGlass {on=true,monitorId=swarm.monitorId,count=-1});
    }
    bool NativeGameMessage(Envelope e)
    {
        if (e.type==Msg.SwarmState)
        {
            var s=e.Body<SwarmGlass>();
            if (s==null || !s.on) { swarm=null; return true; }
            if (screen==null || s.monitorId!=screen.id || s.count<0 || s.count>SwarmGlass.Max ||
                s.id==null || s.kind==null || s.u==null || s.v==null || s.press==null ||
                s.id.Length<s.count || s.kind.Length<s.count || s.u.Length<s.count || s.v.Length<s.count || s.press.Length<s.count ||
                !float.IsFinite(s.halfH) || !float.IsFinite(s.halfW) || s.halfH<=0 || s.halfW<=0) return true;
            if (swarm==null) { Match.Stop(); EndScreenGame(); }
            swarm=s; swarmAt=Now; return true;
        }
        if (e.type!=Msg.GameCommand) return false;
        var cmd=e.Body<GameCommand>();
        if(cmd==null) return true;
        if (cmd.cmd=="stop") { Match.Stop(); return true; }
        if (cmd.cmd=="hit") { Match.Hit(cmd,Now); return true; }
        if (cmd.cmd!="start" || Match.State.active || machine.IsActive || (cmd.mode!="" && cmd.mode!="keeper")) return true;
        TennisCourt court;
        if (cmd.room && T.owner==Env.Mr)
        {
            float height=Math.Clamp(cmd.height,.20f,.80f);
            court=new TennisCourt {room=true,width=Math.Clamp(cmd.width,.25f,.70f),height=height,depth=Math.Clamp(cmd.depth,.20f,.60f),deskHeight=-height*.5f,baseline=.20f};
        }
        else if (!cmd.room && T.owner==Env.Pc && screen!=null && screen.id==cmd.monitorId)
        {
            var calibration=snap.calibrations.Find(c=>c.monitorId==screen.id);
            if (calibration==null) return true;
            ScreenMath.PhysicalSize(screen,calibration,out float w,out float h,out _);
            court=new TennisCourt {monitorId=screen.id,width=w,height=h,depth=Math.Clamp(w*.85f,.4f,.6f),up=cmd.up,deskHeight=Math.Min(cmd.deskHeight,-h*.5f)};
        }
        else return true;
        swarm=null; tennisSound=""; EndScreenGame(); ++serial;
        Match.Start(court,cmd.mode,cmd.eye,Now);
        return true;
    }

    object NativeGameFrame()
    {
        if (swarm!=null && Now-swarmAt<=2)
        {
            var s=swarm;
            return new {native="swarm",s.phase,s.bursts,s.halfW,s.halfH,s.level,s.hearts,s.maxHearts,s.score,s.xp,s.need,s.elapsed,s.won,
                choices=s.phase==SwarmPhase.Choosing ? (s.choices??[]).Take(3).Where(i=>i>=0 && i<SwarmAug.Count).ToArray() : Array.Empty<int>(),
                actors=Enumerable.Range(0,s.count)
                .Where(i=>s.kind[i]>=0 && s.kind[i]<SwarmKind.Count && float.IsFinite(s.u[i]) && float.IsFinite(s.v[i]) && float.IsFinite(s.press[i]))
                .Select(i=>new {id=s.id[i],kind=s.kind[i],u=s.u[i],v=s.v[i],press=Math.Clamp(s.press[i],0,1),radius=SwarmKind.Radius[s.kind[i]]}).ToArray()};
        }
        Match.Tick(Now);
        var t=Match.State;
        if (!t.active || t.court.room) return null;
        var c=t.court;
        Vector3? ball=null;
        if(t.shot!=null) { var b=Tennis.Evaluate(c,t.shot,Now); if (!b.dead && b.pos.Z>=-c.radius) ball=b.pos; }
        return new {native=t.mode=="keeper"?"keeper":"tennis",runId=serial,quiet=snap.settings.quietMode,now=Now,
            t.phase,t.rally,t.scoreUser,t.scorePartner,t.round,t.rounds,t.lastPoint,t.partnerX,t.partnerZ,t.partnerHitAt,
            c.width,c.height,c.depth,c.radius, ball=ball is Vector3 bpos ? new {x=bpos.X,y=bpos.Y,z=bpos.Z} : null,
            sounds=tennisSound!="" && Now-tennisSoundAt<.65 && !snap.settings.quietMode
                ? new[]{new GameSound(tennisSoundId,tennisSound,tennisSoundAt)} : Array.Empty<GameSound>() };
    }
}
