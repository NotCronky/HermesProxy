using HermesProxy.Enums;
using HermesProxy.World.Enums;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// "Your quest log is full." comes from the server as SMSG_QUEST_LOG_FULL, an empty packet. The
/// 3.4.3.54261 client reads it at 0x2A87 (a 0-byte struct in the Quest range, between
/// SMSG_QUEST_GIVER_QUEST_FAILED 0x2A86 and SMSG_QUEST_UPDATE_COMPLETE 0x2A89); the 3.4.3 table
/// had it under another name with no value, so the proxy dropped it.
/// </summary>
public sealed class QuestLogFullTests
{
    [Theory]
    [InlineData(ClientVersionBuild.V3_4_3_54261, 0x2A87u)]
    [InlineData(ClientVersionBuild.V3_3_5a_12340, 0x195u)]
    public void QuestLogFull_IsMapped(ClientVersionBuild build, uint expected)
        => Assert.Equal(expected, Opcodes.GetOpcodeValueForVersion(Opcode.SMSG_QUEST_LOG_FULL, build));
}
