using System;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A mirror image's look from a 3.3.5a server. AzerothCore sends the caster's guild as its id, four
/// bytes, where TrinityCore 4.3.4 sends a GUID, so a player's look is 53 bytes; read as the 4.3.4
/// one it came out too short, and the images were drawn without the caster's face, hair or gear.
/// </summary>
public class MirrorImageDataTests
{
    /// <summary>
    /// The answer AzerothCore gave for one of a gnome male mage's images: model 1563, gnome, male,
    /// mage; skin 3, face 1, hair style 0, hair colour 1, facial hair 7; guild 22; the shirt, chest,
    /// legs, feet, hands and back displays.
    /// </summary>
    private static readonly byte[] CapturedAnswer = Convert.FromHexString(
        "6F7702F0790030F1" + "1B060000" + "070008" + "0301000107" + "16000000"
        + "00000000" + "00000000" + "73080000" + "8A300000" + "00000000" + "C4260000"
        + "13410000" + "00000000" + "11410000" + "0E5A0000" + "00000000");

    [Fact]
    public void PlayerLook_FromA335Server_KeepsTheCustomizationsAndItems()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_MIRROR_IMAGE_COMPONENTED_DATA, p => p.WriteBytes(CapturedAnswer));

        harness.Deliver(Opcode.SMSG_MIRROR_IMAGE_COMPONENTED_DATA, wire, harness.Client.HandleMirrorImageData);

        var image = Assert.IsType<MirrorImageComponentedData>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(31216u, image.UnitGUID.GetEntry());
        Assert.Equal(1563, image.DisplayID);
        Assert.Equal((byte)Race.Gnome, image.RaceID);
        Assert.Equal((byte)Gender.Male, image.Gender);
        Assert.Equal((byte)Class.Mage, image.ClassID);
        Assert.Equal(
            [(80u, 17737u), (81u, 17742u), (82u, 17748u), (83u, 17756u), (84u, 17771u)],
            image.Customizations.ConvertAll(c => (c.ChrCustomizationOptionID, c.ChrCustomizationChoiceID)));
        Assert.Equal([0, 0, 2163, 12426, 0, 9924, 16659, 0, 16657, 23054, 0], image.ItemDisplayIDs);
    }
}
