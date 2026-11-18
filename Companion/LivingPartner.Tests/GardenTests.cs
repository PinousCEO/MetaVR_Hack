using LivingPartner.Shared;
using Xunit;

namespace LivingPartner.Tests;

public class GardenTests
{
    [Fact]
    public void Only_watered_seeds_grow_and_harvest_survives_reopening()
    {
        var bed = new RadishGarden();
        Assert.True(bed.Tend(2, 100));
        Assert.Equal(RadishStage.Seed, bed.Stage(2, 10000));
        Assert.True(bed.Tend(2, 10000));
        Assert.False(bed.Tend(2, 10001));
        Assert.Equal(RadishStage.Sprout, bed.Stage(2, 9900));
        Assert.Equal(RadishStage.Leaves, bed.Stage(2, 10044));
        bed = Proto.FromJson<RadishGarden>(Proto.ToJson(bed));
        Assert.Equal(RadishStage.Ripe, bed.Stage(2, 10045));
        Assert.True(bed.Tend(2, 10045));
        Assert.Equal(1, bed.harvested);
        Assert.All(bed.beds, v => Assert.Equal(0L, v));
        Assert.False(bed.Tend(3, 10050));
    }

    [Theory]
    [InlineData("농사 하자.", "gardenOpen")]
    [InlineData("텃밭 닫아 줘", "gardenClose")]
    [InlineData("텃밭 그림을 만들어 줘", "")]
    public void Only_explicit_garden_requests_open_the_planter(string words, string intent) =>
        Assert.Equal(intent, RadishGarden.Request(words));

    [Theory]
    [InlineData("Hey, Nilo!", true, true)]
    [InlineData("Hey Nilo, open the star garden.", true, false)]
    [InlineData("Hey, Nee-lo! Close the garden.", true, false)]
    [InlineData("Nilo", false, false)]
    [InlineData("She said hey Nilo", false, false)]
    [InlineData("Hey, Bambi", false, false)]
    public void Open_microphone_requires_the_wake_phrase(string text, bool wakes, bool onlyName)
    {
        Assert.Equal(wakes, LivingPartner.Host.NameRules.Wake(text, out var only));
        Assert.Equal(onlyName, only);
        if (wakes && !onlyName)
            Assert.NotEmpty(RadishGarden.Request(LivingPartner.Host.NameRules.Strip(text)));
    }

    [Fact]
    public void Harvest_pays_for_persistent_upgrades_that_change_the_next_crop()
    {
        var garden = new RadishGarden();
        Assert.False(garden.Buy(0));
        for (int plot = 0; plot < 3; plot++)
        {
            Assert.True(garden.Tend(plot, 100));
            Assert.True(garden.Tend(plot, 101));
            Assert.True(garden.Tend(plot, 146));
        }
        Assert.Equal(9, garden.stars);
        Assert.True(garden.Buy(0));
        Assert.False(garden.Buy(0));
        Assert.True(garden.Buy(1));
        garden = Proto.FromJson<RadishGarden>(Proto.ToJson(garden));
        Assert.Equal(0, garden.stars);
        for (int plot = 0; plot < 3; plot++)
        {
            Assert.True(garden.Tend(plot, 200));
            Assert.Equal(RadishStage.Sprout, garden.Stage(plot, 200));
            Assert.False(garden.Tend(plot, 224));
            Assert.True(garden.Tend(plot, 225));
        }
        Assert.False(garden.Buy(-1));
        Assert.False(garden.Buy(3));
        Assert.True(garden.Buy(2));
        Assert.True(garden.Tend(1, 300));
        Assert.True(garden.Tend(1, 325));
        Assert.Equal(6, garden.stars);
    }
}
