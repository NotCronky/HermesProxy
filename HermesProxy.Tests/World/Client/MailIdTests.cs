using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The mail list and the take result name a mail and its attachment the same way, past 2^31 too.
/// </summary>
/// <remarks>
/// The list read the attachment's item guid signed, the take result unsigned; with guid 0xFDA0141D
/// (from a ChromieCraft capture) the client got two different ids, could not match the result to
/// its pending take, and Open All stopped after the first item.
/// </remarks>
public class MailIdTests
{
    private const uint MailId = 0x80000001;
    private const uint AttachId = 0xFDA0141D;

    private static byte[] MailList() => LegacyPacketBuilder.Build(Opcode.SMSG_MAIL_LIST_RESULT, p =>
    {
        p.WriteInt32(1);      // total
        p.WriteUInt8(1);      // mails
        p.WriteUInt16(0);     // message size
        p.WriteUInt32(MailId);
        p.WriteUInt8((byte)MailType.Normal);
        p.WriteUInt64(0);     // sender
        p.WriteUInt32(0);     // COD
        p.WriteUInt32(0);     // package
        p.WriteInt32(41);     // stationery
        p.WriteUInt32(0);     // money
        p.WriteUInt32(0);     // flags
        p.WriteFloat(29f);    // days left
        p.WriteInt32(0);      // template
        p.WriteCString("Auction successful");
        p.WriteCString("");
        p.WriteUInt8(1);      // items
        p.WriteUInt8(0);      // position
        p.WriteUInt32(AttachId);
        p.WriteUInt32(2589);  // Linen Cloth
        for (int slot = 0; slot < 7; slot++)
        {
            p.WriteUInt32(0);
            p.WriteUInt32(0);
            p.WriteUInt32(0);
        }
        p.WriteUInt32(0);     // random property
        p.WriteUInt32(0);     // suffix factor
        p.WriteUInt32(1);     // count
        p.WriteInt32(0);      // charges
        p.WriteUInt32(0);     // max durability
        p.WriteUInt32(0);     // durability
        p.WriteBool(false);   // unlocked
    });

    private static byte[] ItemTaken() => LegacyPacketBuilder.Build(Opcode.SMSG_MAIL_COMMAND_RESULT, p =>
    {
        p.WriteUInt32(MailId);
        p.WriteUInt32((uint)MailActionType.AttachmentExpired); // 2: item taken
        p.WriteUInt32((uint)MailErrorType.Ok);
        p.WriteUInt32(AttachId);
        p.WriteUInt32(1);     // quantity in inventory
    });

    [Fact]
    public void ListAndTakeResult_NameTheSameMailAndAttachment()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_MAIL_LIST_RESULT, MailList(), harness.Client.HandleMailListResult);
        harness.Deliver(Opcode.SMSG_MAIL_COMMAND_RESULT, ItemTaken(), harness.Client.HandleMailCommandResult);

        var sent = harness.ClientWire.Sent.Select(s => s.Packet).ToList();
        var mail = Assert.Single(Assert.IsType<MailListResult>(sent[0]).Mails);
        var result = Assert.IsType<MailCommandResult>(sent[1]);

        Assert.Equal((long)MailId, mail.MailID);
        Assert.Equal((long)AttachId, Assert.Single(mail.Attachments).AttachID);
        Assert.Equal(mail.MailID, result.MailID);
        Assert.Equal(mail.Attachments[0].AttachID, result.AttachID);
    }
}
