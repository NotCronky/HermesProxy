using System;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Objects;

/// <summary>
/// The average level of the items a player wears, worked out the way TrinityCore 4.3.4 does it
/// (Player::GetAverageItemLevel): the number its dungeon finder holds against a dungeon's required
/// item level.
/// </summary>
/// <remarks>
/// The best item of each kind counts once; rings, trinkets and one-handed weapons have two slots,
/// so the best two of each count. Of bows, guns, crossbows, wands, thrown weapons and relics only
/// the best counts. A class that can wield two weapons (warrior, rogue, shaman, hunter, death
/// knight) gets the best two of its hand items, a warrior's two-handers among them for Titan's
/// Grip; any other class gets its best main hand and its best shield or held item. The result is
/// the better of one hand and an off hand over 17 slots and a two-hander over 16.
/// </remarks>
internal static class AverageItemLevel
{
    public static float OfEquipped(Class playerClass, ReadOnlySpan<(InventoryType Type, uint Level)> items)
    {
        Span<uint> best = stackalloc uint[(int)InventoryType.Max];
        best.Clear();
        uint secondFinger = 0;
        uint secondTrinket = 0;
        uint secondWeapon = 0;
        uint secondTwoHand = 0;

        foreach (var (type, level) in items)
        {
            if (type >= InventoryType.Max)
                continue;

            ref uint top = ref best[(int)type];
            switch (type)
            {
                case InventoryType.Finger: KeepTwoBest(level, ref top, ref secondFinger); break;
                case InventoryType.Trinket: KeepTwoBest(level, ref top, ref secondTrinket); break;
                case InventoryType.Weapon: KeepTwoBest(level, ref top, ref secondWeapon); break;
                case InventoryType.TwoHandWeapon: KeepTwoBest(level, ref top, ref secondTwoHand); break;
                default: top = Math.Max(top, level); break;
            }
        }

        uint ranged = Math.Max(
            Math.Max(best[(int)InventoryType.Ranged], best[(int)InventoryType.RangedRight]),
            Math.Max(best[(int)InventoryType.Relic], best[(int)InventoryType.Thrown]));

        uint mainHand;
        uint offHand;
        if (playerClass is Class.Warrior or Class.Rogue or Class.Shaman or Class.Hunter or Class.Deathknight)
        {
            Span<uint> hands = stackalloc uint[6];
            hands[0] = best[(int)InventoryType.WeaponMainHand];
            hands[1] = Math.Max(best[(int)InventoryType.Shield],
                Math.Max(best[(int)InventoryType.WeaponOffHand], best[(int)InventoryType.Holdable]));
            hands[2] = best[(int)InventoryType.Weapon];
            hands[3] = secondWeapon;
            hands[4] = playerClass == Class.Warrior ? best[(int)InventoryType.TwoHandWeapon] : 0;
            hands[5] = playerClass == Class.Warrior ? secondTwoHand : 0;
            hands.Sort();
            mainHand = hands[5];
            offHand = hands[4];
        }
        else
        {
            mainHand = Math.Max(best[(int)InventoryType.Weapon], best[(int)InventoryType.WeaponMainHand]);
            offHand = Math.Max(best[(int)InventoryType.Shield], best[(int)InventoryType.Holdable]);
        }

        uint twoHand = best[(int)InventoryType.TwoHandWeapon];
        best[(int)InventoryType.Chest] = Math.Max(best[(int)InventoryType.Chest], best[(int)InventoryType.Robe]);
        foreach (InventoryType notCounted in (ReadOnlySpan<InventoryType>)[
            InventoryType.NonEquip, InventoryType.Body, InventoryType.Ranged, InventoryType.Thrown,
            InventoryType.Ammo, InventoryType.RangedRight, InventoryType.Relic, InventoryType.TwoHandWeapon,
            InventoryType.Weapon, InventoryType.Shield, InventoryType.WeaponMainHand, InventoryType.WeaponOffHand,
            InventoryType.Holdable, InventoryType.Bag, InventoryType.Tabard, InventoryType.Robe])
            best[(int)notCounted] = 0;

        uint others = 0;
        foreach (uint level in best)
            others += level;
        others += secondFinger + secondTrinket + ranged;

        float oneHanded = (mainHand + others + offHand) / 17.0f;
        float twoHanded = (twoHand + others) / 16.0f;
        return oneHanded <= twoHanded ? twoHanded : oneHanded;
    }

    private static void KeepTwoBest(uint level, ref uint top, ref uint second)
    {
        if (level <= top)
        {
            if (level >= second)
                second = level;
        }
        else
        {
            second = top;
            top = level;
        }
    }
}
