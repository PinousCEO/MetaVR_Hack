using System;
using System.Numerics;

namespace LivingPartner.Shared
{
    /// <summary>A tennis or keeper match on the host clock. Both renderers follow each stroke using Tennis.Evaluate.</summary>
    public sealed class TennisReferee
    {
        public TennisState State { get; private set; } = new TennisState();
        public event Action Changed;
        readonly Random random = new Random();
        Vector3 eye;
        float partnerX;
        bool planned;
        int seq;
        double last;

        static float Clamp(float x, float lo, float hi) => Math.Max(lo, Math.Min(hi, x));
        void Publish() { State.revision++; Changed?.Invoke(); }
        public void Start(TennisCourt court, string mode, Vector3 eye, double now)
        {
            this.eye = eye; last = now; partnerX = 0; planned = false;
            bool keeper = mode == "keeper";
            court.radius = Tennis.BallRadius(keeper ? "keeper" : "");
            State = new TennisState { revision = State.revision, court = court, active = true, mode = keeper ? "keeper" : "",
                rounds = keeper ? 5 : 0, phase = "ready", phaseAt = now, serveAt = now + 2.4, partnerHitAt = now + 2.4, partnerZ = Tennis.Baseline(court) };
            Publish();
        }
        public void Stop()
        {
            if (!State.active) return;
            State.active = false; State.phase = "off"; State.note = ""; State.shot = null; Publish();
        }
        public bool Hit(GameCommand g, double now)
        {
            var s = State;
            if (g == null || !s.active || s.phase != "rally" || s.shot == null || s.shot.by != "partner" || g.seq != s.shot.seq ||
                double.IsNaN(g.t) || Math.Abs(g.t - now) > .6) return false;
            var ball = Tennis.Evaluate(s.court, s.shot, g.t);
            if (ball.dead || !ball.Front) return false;
            eye = g.eye;
            s.shot = Tennis.UserShot(s.court, ++seq, ball.pos, g.swing, g.normal, g.t);
            s.rally++; s.bestRally = Math.Max(s.rally,s.bestRally);
            planned = false; s.partnerHitAt = 0;
            if (s.mode != "keeper")
            {
                planned = Tennis.PlanReturn(s.court, s.shot, partnerX, now, out double at, out Vector3 p)
                    && random.NextDouble() >= Math.Min(.30,.08 + .02*s.rally);
                s.partnerX = planned ? p.X : Clamp(Tennis.Evaluate(s.court,s.shot,g.t+1.4).pos.X*.6f,-s.court.width*.5f+.06f,s.court.width*.5f-.06f);
                s.partnerZ = planned ? Clamp(p.Z+.02f,.12f,s.court.depth-.07f) : Tennis.Baseline(s.court);
                s.partnerHitAt = planned ? at : 0;
            }
            Publish(); return true;
        }
        void Point(bool user, string why, double now)
        {
            var s = State;
            if (user) s.scoreUser++; else s.scorePartner++;
            s.round++; s.lastPoint = user ? "user" : "partner"; s.note = why;
            s.shot = null; s.partnerHitAt = 0; s.partnerX = 0; s.partnerZ = Tennis.Baseline(s.court);
            bool over = s.mode == "keeper" ? s.round >= s.rounds : Math.Max(s.scoreUser,s.scorePartner) >= Tennis.PointsToWin;
            s.phase = over ? "over" : "point"; s.phaseAt = now; Publish();
        }
        public void Tick(double now)
        {
            var s = State;
            if (!s.active) return;
            float dt = Clamp((float)(now-last),0,.25f); last=now;
            partnerX += Clamp(s.partnerX-partnerX,-Tennis.PartnerSpeed*dt,Tennis.PartnerSpeed*dt);
            if (s.phase=="ready" && now>=s.serveAt)
            {
                float spread = (float)(random.NextDouble()*1.2-.6)*(s.mode=="keeper"?.4f:1f);
                s.shot = Tennis.PartnerShot(s.court,++seq,Tennis.ServePoint(s.court,partnerX),eye,spread,now);
                s.phase="rally"; s.phaseAt=now; s.note="serve"; s.rally=0; planned=false; Publish();
            }
            else if (s.phase=="rally" && s.shot!=null)
            {
                var b=Tennis.Evaluate(s.court,s.shot,now);
                if (s.mode=="keeper" && s.shot.by=="user")
                {
                    if (b.crossed && b.pos.Z>=Math.Min(.13f,s.court.depth*.45f) || b.dead || now>s.shot.t0+4) Point(true,"saved",now);
                }
                else if (planned && s.shot.by=="user" && now>=s.partnerHitAt)
                {
                    float spread = Math.Min(1f,.5f+.08f*s.rally);
                    s.shot=Tennis.PartnerShot(s.court,++seq,b.pos,eye,(float)(random.NextDouble()*2-1)*spread,now);
                    planned=false; s.rally++; s.bestRally=Math.Max(s.rally,s.bestRally); Publish();
                }
                else if (b.dead || now>s.shot.t0+8)
                    Point(s.shot.by=="user" && b.crossed && b.why!="out",b.why,now);
            }
            else if (s.phase=="point" && now-s.phaseAt>1.9)
            {
                s.phase="ready"; s.note=""; s.phaseAt=now; s.serveAt=now+1.3; s.partnerHitAt=s.serveAt; Publish();
            }
            else if (s.phase=="over" && now-s.phaseAt>4.5) Stop();
        }
    }
}
