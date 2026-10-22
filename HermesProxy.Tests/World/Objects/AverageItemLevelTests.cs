using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using Xunit;

namespace HermesProxy.Tests.World.Objects;

/// <summary>
/// <see cref="AverageItemLevel"/> against TrinityCore 4.3.4's Player::GetAverageItemLevel, worked
/// by hand: fifteen slots besides the hands (ten single ones, two rings, two trinkets, the ranged
/// one), then the hands over 17, or a two-hander over 16.
/// </summary>
public sealed class AverageItemLevelTests
{
    // Head to hands, the cloak, the rings, the trinkets: 14 of the 15 slots besides the hands.
    private static readonly (InventoryType Type, uint Level)[] Armour =
    [
        (InventoryType.Head, 359), (InventoryType.Neck, 359), (InventoryType.Shoulders, 359),
        (InventoryType.Robe, 359), (InventoryType.Waist, 359), (InventoryType.Legs, 359),
        (InventoryType.Feet, 359), (InventoryType.Wrists, 359), (InventoryType.Hands, 359),
        (InventoryType.Cloak, 359), (InventoryType.Finger, 359), (InventoryType.Finger, 359),
        (InventoryType.Trinket, 359), (InventoryType.Trinket, 359),
    ];

    private static float Of(Class playerClass, params (InventoryType Type, uint Level)[] more)
        => AverageItemLevel.OfEquipped(playerClass, [.. Armour, .. more]);

    [Fact]
    public void NothingWorn_IsZero()
        => Assert.Equal(0f, AverageItemLevel.OfEquipped(Class.Priest, []));

    [Fact]
    public void ATwoHander_CountsOverSixteen()
    {
        float average = Of(Class.Warrior,
            (InventoryType.TwoHandWeapon, 359), (InventoryType.Thrown, 359),
            (InventoryType.Body, 1), (InventoryType.Tabard, 1));

        Assert.Equal((359f + 15 * 359f) / 16f, average);
    }

    [Fact]
    public void AMainHandAndAHeldItem_CountOverSeventeen()
    {
        float average = Of(Class.Priest,
            (InventoryType.Weapon, 378), (InventoryType.Holdable, 359), (InventoryType.RangedRight, 359));

        Assert.Equal((378f + 15 * 359f + 359f) / 17f, average);
    }

    [Fact]
    public void TwoOneHanders_CountBothForAClassThatWieldsTwo()
    {
        float average = Of(Class.Rogue,
            (InventoryType.Weapon, 378), (InventoryType.Weapon, 359), (InventoryType.Thrown, 359));

        Assert.Equal((378f + 15 * 359f + 359f) / 17f, average);
    }

    [Fact]
    public void TwoOneHanders_CountOnlyTheBetterForAClassThatWieldsOne()
    {
        float average = Of(Class.Priest,
            (InventoryType.Weapon, 378), (InventoryType.Weapon, 359), (InventoryType.RangedRight, 359));

        Assert.Equal((378f + 15 * 359f) / 17f, average);
    }

    [Fact]
    public void OnlyTheBestRangedItemCounts()
    {
        float average = Of(Class.Hunter,
            (InventoryType.TwoHandWeapon, 359), (InventoryType.Ranged, 372), (InventoryType.Relic, 300));

        Assert.Equal((359f + 14 * 359f + 372f) / 16f, average);
    }
}
