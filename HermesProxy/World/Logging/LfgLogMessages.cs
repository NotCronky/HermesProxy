using Microsoft.Extensions.Logging;

namespace HermesProxy.World.Logging;

/// <summary>
/// Source-generated logging for the dungeon finder.
/// </summary>
/// <remarks>
/// EventId 1630-1639 is reserved for this file.
/// </remarks>
internal static partial class LfgLogMessages
{
    // The 4.4.2 client shows nothing of a vote once it is over (GetLFGBootProposal() returns
    // false alone), so the count at its end shows only here and in the packet log.
    [LoggerMessage(EventId = 1630, Level = LogLevel.Debug,
        Message = "LFG vote to remove: inProgress={InProgress} passed={Passed} voted={MyVoteCompleted} for={MyVote} cast={TotalVotes} agree={BootVotes} needed={VotesNeeded}")]
    public static partial void BootVote(ILogger logger, bool InProgress, bool Passed, bool MyVoteCompleted, bool MyVote, uint TotalVotes, uint BootVotes, uint VotesNeeded);
}
