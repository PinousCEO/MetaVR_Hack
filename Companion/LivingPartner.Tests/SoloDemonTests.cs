using LivingPartner.Demon;
using LivingPartner.Shared;
using System.Numerics;
using Xunit;

namespace LivingPartner.Tests;

public class SoloDemonTests
{
    [Fact]
    public void Desktop_controls_freeze_strafe_raise_a_shield_and_start_a_fresh_run()
    {
        var run=new SoloDemonRun(9);
        var controls=new SoloControls(Move:1,Fire:true,Shield:true);
        for(int i=0;i<16;i++)run.Advance(controls,.1f);
        Assert.InRange(run.PlayerX,.35f,.36f);
        float intro=run.Sim.IntroLeft;
        run.Advance(controls with { Pause=true },.1f);
        Assert.Equal(intro,run.Sim.IntroLeft);
        for(int i=0;i<25;i++)run.Advance(controls,.1f);
        int probe=run.Sim.Put(SwarmKind.Hopper,new Vector3(run.PlayerX,SoloDemonRun.CameraY,.16f));
        Assert.True(probe>=0);
        run.Advance(new SoloControls(Shield:true),.02f);
        Assert.False(run.Sim.monsters[probe].alive);
        var replay=new SoloDemonRun(9);
        Assert.Equal(SwarmPhase.Intro,replay.Sim.phase);
        Assert.Equal(0,replay.Sim.kills);
    }

    [Fact]
    public void One_mouse_player_can_choose_charms_and_defeat_the_boss_without_a_companion()
    {
        var run=new SoloDemonRun(42);
        var sim=run.Sim;
        int picks=0;
        var priority=new[]{SwarmAug.Twin,SwarmAug.Rapid,SwarmAug.Big,SwarmAug.Explode,SwarmAug.Chain,SwarmAug.Pierce,SwarmAug.Orbit};
        for(int i=0;i<20000&&sim.phase!=SwarmPhase.Over;i++)
        {
            if(sim.phase==SwarmPhase.Choosing)
            {
                Assert.DoesNotContain(SwarmAug.Buddy,sim.choices);
                Assert.Equal(3,sim.choices.Distinct().Count());
                int rank(int card) {int index=Array.IndexOf(priority,card);return index<0?99:index;}
                int selection=Enumerable.Range(0,3).OrderBy(j=>rank(sim.choices[j])).First();
                run.Advance(new SoloControls(Pick:selection),.02f);picks++;continue;
            }
            var target=sim.monsters.Where(m=>m.alive&&m.state!=SwarmState.Inside)
                .OrderBy(m=>Vector3.DistanceSquared(m.pos,new Vector3(0,1.02f,0))).FirstOrDefault();
            float depth=Math.Max(.05f,target.pos.Z-SoloDemonRun.CameraZ);
            const float aspect=16f/9;
            run.Advance(new SoloControls(X:target.pos.X/(depth*SoloDemonRun.Lens*aspect),
                Y:(target.pos.Y-SoloDemonRun.CameraY)/(depth*SoloDemonRun.Lens),Aspect:aspect,Fire:true,Shield:true),1f/36);
        }
        Assert.True(sim.won,$"time={sim.time}, hp={sim.hearts}, xp={sim.xp}, kills={sim.kills}");
        Assert.True(sim.bossDead);
        Assert.True(picks>=5);
        Assert.True(sim.score>1000);
    }

    [Fact]
    public void A_player_who_does_not_defend_loses()
    {
        var run=new SoloDemonRun(7);
        for(int i=0;i<2200&&run.Sim.phase!=SwarmPhase.Over;i++)run.Advance(new SoloControls(),.1f);
        Assert.Equal(SwarmPhase.Over,run.Sim.phase);Assert.False(run.Sim.won);Assert.Equal(0,run.Sim.hearts);
    }
}

