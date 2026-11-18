using LivingPartner.Host;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingPartner.Tests;

public sealed class GomokuTableTests
{
    readonly GomokuTable table;
    readonly string viewer=Guid.NewGuid().ToString();
    double now;
    long seq;
    JObject View=>JObject.FromObject(table.View());
    public GomokuTableTests()
    {
        table=new GomokuTable(()=>now);
        Tick();
        Register();
        Assert.Empty(table.Button(Click(),"start"));
    }
    void Tick()=>table.Update(true,[new("tv","TV",true)],_=>{},_=>{},()=>{});
    void Register()=>Assert.True(table.Register(new(viewer,"tv",true,.125,.125,.75,.75)));
    TableClick Click()=>new(viewer,"tv",(int)View["round"],(int)View["revision"],seq,null,null);
    static double At(int i)=>.125+i*.75/14;
    void Black(int x,int y)
    {
        var click=Click();
        table.Wearer(JObject.FromObject(new{round=click.Round,revision=click.Revision,monitorId="tv",side="wearer",seq=++seq,u=At(x),v=At(y),commit=true}));
    }
    TableClick Frame(int x,int y){
        var click=Click() with{Seq=++seq,U=At(x),V=At(y)};
        table.Camera(JObject.FromObject(new{round=click.Round,revision=click.Revision,monitorId="tv",seq=click.Seq,w=640,h=480,jpeg=new string('a',40)}));
        Assert.Null(table.Latest("another tab",-1));
        Assert.NotNull(table.Latest(viewer,-1));
        return click;
    }
    void White(int x,int y){
        for(int i=0;i<4;i++){now+=.31;Register();Assert.True(table.Guest(Frame(x,y)));}
    }
    [Theory]
    [InlineData(1,0)]
    [InlineData(0,1)]
    [InlineData(1,1)]
    [InlineData(1,-1)]
    public void Five_in_each_direction_ends_the_game(int dx,int dy){
        for(int k=0;k<5;k++){
            Black(6+k*dx,6+k*dy);
            if(k<4)White(k,0);
        }
        Assert.Equal(0,(int)View["turn"]);Assert.Equal("Black wins",(string)View["status"]);
        Assert.Equal(9,(int)View["moves"]);
        var before=Click();
        Assert.Empty(table.Button(before,"restart"));Assert.True((int)View["round"]>before.Round);Assert.Equal(0,(int)View["moves"]);
        Assert.NotEmpty(table.Button(before,"stop"));
    }
    [Fact]
    public void Guest_needs_fresh_delivered_frames_and_an_empty_stable_intersection(){
        Black(3,3);Assert.Equal(2,(int)View["turn"]);
        var first=Frame(4,4);Assert.True(table.Guest(first));Assert.False(table.Guest(first));
        now+=.4;Assert.True(table.Guest(Frame(3,3)));Assert.Equal(-1,(int)View["hover"]);
        now+=.6;Assert.True(table.Guest(Frame(4,4)));Assert.Equal(1,(int)View["moves"]);
        var delayed=Frame(4,4);now+=1.2;Assert.False(table.Guest(delayed));
        Register();Tick();
        White(4,4);Assert.Equal(2,(int)View["moves"]);
        Assert.Null(table.Latest(viewer,-1));
        Assert.False(table.Guest(first));Black(4,4);Assert.Equal(1,(int)View["turn"]);
        table.Disconnect();Assert.False((bool)View["active"]);
    }
    [Fact]
    public void Fullscreen_loss_aborts_capture_and_an_old_stop_does_not_end_a_new_round(){
        Black(6,6);Frame(0,0);var old=Click();
        Assert.True(table.Register(new(viewer,"tv",false,0,0,0,0)));Assert.False((bool)View["active"]);Assert.Null(table.Latest(viewer,-1));
        Register();Assert.Empty(table.Button(Click(),"start"));table.Stop(old.Round,"tv");Assert.True((bool)View["active"]);
        now+=5;Tick();Assert.False((bool)View["active"]);
    }
}
