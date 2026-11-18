using Xunit;

namespace LivingPartner.Tests;

public class NiloWakeTests
{
    [Theory]
    [InlineData("Hey, Nilo!", true, true)]
    [InlineData("Hey Nilo, copy this cup.", true, false)]
    [InlineData("Hey, Nee-lo! Take a sip.", true, false)]
    [InlineData("Before we start, hi Niro!", true, true)]
    [InlineData("Okay then. Hey Miro, open the chart.", true, false)]
    [InlineData("Hi, Nilo", true, true)]
    [InlineData("high nitrogen levels", false, false)]
    [InlineData("Nilo", false, false)]
    [InlineData("She said hey Nilo", false, false)]
    [InlineData("Hey, Bambi", false, false)]
    public void Open_microphone_requires_the_wake_phrase(string text, bool wakes, bool onlyName)
    {
        Assert.Equal(wakes, LivingPartner.Host.NameRules.Wake(text, out var only));
        Assert.Equal(onlyName, only);
        if (wakes && !onlyName)
            Assert.DoesNotContain("Nilo", LivingPartner.Host.NameRules.Strip(text), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Earlier_conversation_does_not_enter_the_command()
    {
        Assert.Equal("play keeper.", LivingPartner.Host.NameRules.Strip("Stop the video? Wait. Hey Niro, play keeper."));
    }
}
