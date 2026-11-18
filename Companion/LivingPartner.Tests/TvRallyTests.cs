using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

public sealed class TvRallyTests
{
    [Theory]
    [InlineData("아내랑 TV 게임 하자", "start")]
    [InlineData("와이프랑 같이 TV 랠리 시작해 줘", "start")]
    [InlineData("TV 랠리", "start")]
    [InlineData("TV 랠리를 끝내 줘", "stop")]
    [InlineData("stop TV Rally", "stop")]
    [InlineData("아내랑 TV 게임을 만들어 줘", "")]
    [InlineData("TV 랠리가 뭐야?", "")]
    public void Addressed_orders_choose_the_family_game(string spoken, string command) => Assert.Equal(command, TvRallyWords.Command(spoken));
    static void Wait(TvRally game, float duration)
    {
        for (int i = 0; i < (int)(duration * 100f); i++) game.Step(0.01f, 0, false);
    }

    [Fact]
    public void Six_returns_require_both_players_in_order()
    {
        var game = new TvRally();
        Assert.Equal(TvRallyPhase.Join, game.phase);
        game.Join(); game.Aligned();
        for (int rally = 1; rally <= TvRally.Goal; rally++)
        {
            Assert.Equal(TvRallyPhase.Serve, game.phase);
            Wait(game, 1.1f);
            game.Step(.02f, -game.side, true);
            Assert.Equal(TvRallyPhase.Reach, game.phase);
            game.Step(.01f, game.side, false);
            game.Step(.01f, 0, true);
            Assert.Equal(TvRallyPhase.Volley, game.phase);
            Wait(game, .35f);
            game.Step(.02f, 0, true);
            Assert.Equal(TvRallyPhase.Return, game.phase);
            Assert.Equal(rally, game.score);
            Wait(game, .96f);
        }
        Assert.Equal(TvRallyPhase.Won, game.phase);
        game.Join();
        Assert.Equal(TvRallyPhase.Align, game.phase);
        Assert.Equal(0, game.score);
    }

    [Fact]
    public void Both_a_missed_reach_and_a_missed_volley_get_another_serve()
    {
        var game = new TvRally();
        game.Join(); game.Aligned(); Wait(game, 6.15f);
        Assert.Equal(TvRallyPhase.Miss, game.phase);
        Wait(game, 2.42f);
        Assert.Equal(TvRallyPhase.Reach, game.phase);
        game.Step(.01f, game.side, false); Wait(game, 2.8f);
        Assert.Equal(TvRallyPhase.Miss, game.phase);
        Assert.Equal(0, game.score);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void New_body_motion_in_the_target_region_needs_two_frames(int side)
    {
        var motion = new TvMotion();
        var samples = Enumerable.Repeat((byte)80, TvMotion.Count * 2).ToArray();
        motion.Calibrate(samples);
        Array.Fill(samples, (byte)110);
        Assert.Equal(0, motion.Read(samples, side));
        Assert.True(motion.IsNeutral);
        int offset = side < 0 ? 0 : TvMotion.Count;
        for (int y = 2; y <= 6; y++)
            for (int x = 2; x <= 6; x++) samples[offset + y * TvMotion.Grid + x] = 175;
        Assert.Equal(0, motion.Read(samples, -side));
        Assert.Equal(0, motion.Read(samples, side));
        Assert.Equal(side, motion.Read(samples, side));
        Assert.False(motion.IsNeutral);
        Array.Fill(samples, (byte)110);
        Assert.Equal(0, motion.Read(samples, side));
        Assert.True(motion.IsNeutral);
        motion.Clear();
        Assert.Equal(0, motion.Read(samples, side));
    }
}
