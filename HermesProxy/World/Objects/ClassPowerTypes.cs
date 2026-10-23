using HermesProxy.World.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HermesProxy.World.Objects;

public static class ClassPowerTypes
{
    // ChrClassesXPowerTypes.db2
    public static sbyte GetPowerSlotForClass(Class classId, PowerType power)
    {
        // 4.4.2's table gives a hunter focus where the older clients have mana, and each Cataclysm
        // class power a slot of its own; for the rest it is the older clients' table. Without
        // these the value had no slot and the bar stayed at 0.
        if (ModernVersion.AddedInVersion(4, 4, 0))
        {
            switch (classId, power)
            {
                case (Class.Hunter, PowerType.Focus):
                    return 0;
                case (Class.Paladin, PowerType.HolyPower):
                case (Class.Warlock, PowerType.SoulShards):
                    return 1;
                // Balance, after Lunar Power (4) and Alternate (5); in slot 4 the eclipse showed as
                // Lunar Power, a tenth of it, and the Eclipse bar had nothing.
                case (Class.Druid, PowerType.Eclipse):
                    return 6;
                case (Class.Deathknight, PowerType.RuneBlood):
                    return 2;
                case (Class.Deathknight, PowerType.RuneFrost):
                    return 3;
                case (Class.Deathknight, PowerType.RuneUnholy):
                    return 4;
            }
        }
        // 3.4.3's table has the rune kinds as well, with no Runes row ahead of them. Without these
        // a rune made faster by Improved Unholy Presence had no slot for its rate, and the client
        // counted ten seconds where the server took nine.
        else if (ModernVersion.ExpansionVersion == 3)
        {
            switch (classId, power)
            {
                case (Class.Deathknight, PowerType.RuneBlood):
                    return 1;
                case (Class.Deathknight, PowerType.RuneFrost):
                    return 2;
                case (Class.Deathknight, PowerType.RuneUnholy):
                    return 3;
            }
        }

        switch (classId)
        {
            case Class.Warrior:
            {
                switch (power)
                {
                    case PowerType.Rage:
                        return 0;
                    case PowerType.ComboPoints:
                        return 1;
                }
                break;
            }
            case Class.Paladin:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                }
                break;
            }
            case Class.Hunter:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                }
                break;
            }
            case Class.Rogue:
            {
                switch (power)
                {
                    case PowerType.Energy:
                        return 0;
                    case PowerType.ComboPoints:
                        return 1;
                }
                break;
            }
            case Class.Priest:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                }
                break;
            }
            case Class.Deathknight:
            {
                switch (power)
                {
                    case PowerType.RunicPower:
                        return 0;
                }
                break;
            }
            case Class.Shaman:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                }
                break;
            }
            case Class.Mage:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                }
                break;
            }
            case Class.Warlock:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                }
                break;
            }
            case Class.Druid:
            {
                switch (power)
                {
                    case PowerType.Mana:
                        return 0;
                    case PowerType.Rage:
                        return 1;
                    case PowerType.Energy:
                        return 2;
                    case PowerType.ComboPoints:
                        return 3;
                }
                break;
            }
        }

        return -1;
    }
    // A creature's combo points, whatever its class: a native 3.4.3 or 4.4.2 server creates every
    // creature with MaxPower[2] = 5 next to its display power in slot 0.
    public const sbyte CreatureComboPointsSlot = 2;

    // The power type a packet names, for the modern client. Only a 4.3.4 server's eclipse differs.
    public static PowerType ToModernPowerType(PowerType power) =>
        power == PowerType.Eclipse && ModernVersion.AddedInVersion(4, 4, 0) ? PowerType.Balance : power;

    public static sbyte GetPowerSlotForPet(PowerType power)
    {
        switch (power)
        {
            case PowerType.Focus:
                return 0;
            case PowerType.Happiness:
                return 3;
        }

        return -1;
    }
}
