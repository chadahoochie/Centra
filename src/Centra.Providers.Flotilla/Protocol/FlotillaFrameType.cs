namespace Centra.Providers.Flotilla.Protocol;

/// <summary>
/// Frame message type discriminants matching Flotilla protocol specification.
/// </summary>
public enum FlotillaFrameType : ushort
{
    Unknown = 0,
    RequestVoteArgs = 1,
    RequestVoteReply = 2,
    AppendEntriesArgs = 3,
    AppendEntriesReply = 4,
    HeartbeatArgs = 5,
    HeartbeatReply = 6,
    ClientProposal = 7,
    ClientProposalReply = 8,
}
