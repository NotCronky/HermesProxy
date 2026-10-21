using System.Collections.Frozen;
using System.Collections.Generic;
using HermesProxy.World;
using Xunit;

namespace HermesProxy.Tests.World;

/// <summary>
/// A 3.x or 4.x server sends a visible item's enchants as one dword, the permanent enchant in the low
/// half and the temporary one in the high half (Player::SetVisibleItemSlot). Looked up whole, a shaman's
/// Flametongue imbue named no enchant and the weapon showed no glow.
/// </summary>
public sealed class VisibleItemEnchantVisualTests
{
    private const uint Flametongue = 5;     // ItemVisual 32
    private const uint FieryWeapon = 803;   // ItemVisual 25

    private static uint Visual(uint packedEnchants)
    {
        var shipped = GameData.ItemEnchantVisuals;
        GameData.ItemEnchantVisuals = new Dictionary<uint, uint> { [Flametongue] = 32, [FieryWeapon] = 25 }.ToFrozenDictionary();
        try
        {
            return GameData.GetVisibleItemEnchantVisual(packedEnchants);
        }
        finally
        {
            GameData.ItemEnchantVisuals = shipped;
        }
    }

    [Fact]
    public void TemporaryEnchantAlone_ShowsItsVisual() => Assert.Equal(32u, Visual(Flametongue << 16));

    [Fact]
    public void PermanentEnchantAlone_ShowsItsVisual() => Assert.Equal(25u, Visual(FieryWeapon));

    [Fact]
    public void BothEnchants_TheTemporaryOneShows() => Assert.Equal(32u, Visual(Flametongue << 16 | FieryWeapon));

    [Fact]
    public void NoEnchant_ShowsNothing() => Assert.Equal(0u, Visual(0));
}
