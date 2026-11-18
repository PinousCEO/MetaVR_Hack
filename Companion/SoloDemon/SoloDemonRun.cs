using System.Numerics;
using LivingPartner.Shared;

namespace LivingPartner.Demon;

public sealed record SoloControls(float X = 0, float Y = 0, float Aspect = 1.77f, int Move = 0, bool Fire = false, bool Pause = false, int? Pick = null, bool Restart = false);

/// <summary>One desktop player facing the gate. Independent sessions share only the Quest game's geometry and simulation rules.</summary>
public sealed class SoloDemonRun
{
    public const float CameraY = 1.06f, CameraZ = -.39f, Lens = .40f;
    public readonly SwarmSim Sim = new();
    public float PlayerX { get; private set; }
    readonly List<float[]> effects = new();

    public SoloDemonRun(uint seed)
    {
        Sim.Start(seed, new SwarmArena {
            center = new Vector3(0, 1.06f, .87f), right = Vector3.UnitX, up = Vector3.UnitY, outward = -Vector3.UnitZ,
            halfW = .46f, halfH = .19f, deskY = .90f, pace = .90f
        });
    }

    public void Advance(SoloControls input, float elapsed)
    {
        effects.Clear();
        if (input.Pause) return;
        if (input.Pick is int card) Sim.Choose(card);
        float aspect = Math.Clamp(input.Aspect, .4f, 5f);
        Vector3 direction = Vector3.Normalize(new Vector3(Math.Clamp(input.X,-1,1)*aspect*Lens, Math.Clamp(input.Y,-1,1)*Lens, 1));
        for (float remaining = Math.Clamp(elapsed, 0, .15f); remaining > .00001f;)
        {
            float dt = Math.Min(remaining, 1f/72f);
            remaining -= dt;
            if (Sim.phase == SwarmPhase.Choosing || Sim.phase == SwarmPhase.Over) break;
            PlayerX = Math.Clamp(PlayerX + Math.Clamp(input.Move,-1,1)*dt*.46f,-.36f,.36f);
            var camera = new Vector3(PlayerX,CameraY,CameraZ);
            var hand = camera + direction * (.55f / direction.Z);
            Sim.Step(dt, new SwarmInput {
                aimOrigin = camera, aimDir = direction, aimValid = input.Fire,
                target = new Vector3(PlayerX,1.02f,0), handValid = true, hand = hand
            });
            for (int i=0;i<Sim.eventCount;i++) {
                var e=Sim.events[i];
                effects.Add(new[]{(float)e.type,e.pos.X,e.pos.Y,e.pos.Z,e.pos2.X,e.pos2.Y,e.pos2.Z});
            }
        }
    }

    public object Frame()
    {
        var monsters = new List<float[]>(Sim.alive);
        foreach (var m in Sim.monsters) if (m.alive)
            monsters.Add(new[]{m.id,(float)m.kind,m.pos.X,m.pos.Y,m.pos.Z,m.radius,m.phase,m.state,m.grow,m.flash,m.hp/m.maxHp});
        var bolts = new List<float[]>();
        foreach (var b in Sim.bolts) if (b.alive)
            bolts.Add(new[]{b.pos.X,b.pos.Y,b.pos.Z,b.prev.X,b.prev.Y,b.prev.Z,b.radius});
        var gems = new List<float[]>();
        foreach (var g in Sim.gems) if (g.alive) gems.Add(new[]{g.pos.X,g.pos.Y,g.pos.Z});
        var orbit = new List<float[]>();
        for (int i=0;i<Sim.orbitCount;i++) { var p=Sim.orbit[i]; orbit.Add(new[]{p.X,p.Y,p.Z}); }
        return new {
            phase=Sim.phase, time=Sim.time, intro=Sim.IntroLeft, hp=Sim.hearts, maxHp=Sim.maxHearts, level=Sim.level, xp=Sim.xp, need=Sim.XpNeed,
            score=Sim.score, kills=Sim.kills, won=Sim.won, boss=Sim.bossSpawned, bossAt=SwarmSim.BossAt, playerX=PlayerX,
            choices=Sim.phase==SwarmPhase.Choosing?Sim.choices:Array.Empty<int>(), upgrades=Sim.aug,
            monsters,bolts,gems,orbit,effects
        };
    }
}

