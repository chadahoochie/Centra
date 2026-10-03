using System.Diagnostics.Metrics;
using System.Text;

namespace Centra.Sample.FlotillaSimulation.Server.Consensus;

/// <summary>
/// Telemetry metrics for Flotilla consensus server engine.
/// </summary>
public sealed class FlotillaConsensusMetrics
{
    public const string MeterName = "Centra.Flotilla.Server";
    private readonly Meter _meter;
    public Counter<long> ProposalsTotal { get; }
    public Counter<long> CommitsTotal { get; }
    public Histogram<double> ProposalDurationMs { get; }
    public Counter<long> TcpProposalsTotal { get; }
    public Counter<long> UdpProposalsTotal { get; }
    public Counter<long> GrpcProposalsTotal { get; }
    public Counter<long> ProposalsFailedTotal { get; }

    private long _proposalsTotalCount;
    private long _commitsTotalCount;
    private long _tcpProposalsCount;
    private long _udpProposalsCount;
    private long _grpcProposalsCount;
    private long _proposalsFailedCount;

    public long TotalProposals => Volatile.Read(ref _proposalsTotalCount);
    public long CommittedEntries => Volatile.Read(ref _commitsTotalCount);
    public long TcpProposals => Volatile.Read(ref _tcpProposalsCount);
    public long UdpProposals => Volatile.Read(ref _udpProposalsCount);
    public long GrpcProposals => Volatile.Read(ref _grpcProposalsCount);
    public long FailedProposals => Volatile.Read(ref _proposalsFailedCount);

    public FlotillaConsensusMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        ProposalsTotal = _meter.CreateCounter<long>("flotilla.server.proposals_total", description: "Total proposals received");
        CommitsTotal = _meter.CreateCounter<long>("flotilla.server.commits_total", description: "Total commits appended");
        ProposalDurationMs = _meter.CreateHistogram<double>("flotilla.server.proposal_duration_ms", unit: "ms", description: "Proposal commit duration");
        TcpProposalsTotal = _meter.CreateCounter<long>("flotilla.server.tcp_proposals_total", description: "Total TCP proposals received");
        UdpProposalsTotal = _meter.CreateCounter<long>("flotilla.server.udp_proposals_total", description: "Total UDP proposals received");
        GrpcProposalsTotal = _meter.CreateCounter<long>("flotilla.server.grpc_proposals_total", description: "Total gRPC proposals received");
        ProposalsFailedTotal = _meter.CreateCounter<long>("flotilla.server.proposals_failed_total", description: "Total failed proposals");
    }

    public void RecordProposal(string transport, bool success, double durationMs)
    {
        Interlocked.Increment(ref _proposalsTotalCount);
        ProposalsTotal.Add(1);

        if (string.Equals(transport, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _tcpProposalsCount);
            TcpProposalsTotal.Add(1);
        }
        else if (string.Equals(transport, "udp", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _udpProposalsCount);
            UdpProposalsTotal.Add(1);
        }
        else if (string.Equals(transport, "grpc", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _grpcProposalsCount);
            GrpcProposalsTotal.Add(1);
        }

        ProposalDurationMs.Record(durationMs);

        if (success)
        {
            Interlocked.Increment(ref _commitsTotalCount);
            CommitsTotal.Add(1);
        }
        else
        {
            Interlocked.Increment(ref _proposalsFailedCount);
            ProposalsFailedTotal.Add(1);
        }
    }

    public string ToPrometheusText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP flotilla_server_proposals_total Total proposals received");
        sb.AppendLine("# TYPE flotilla_server_proposals_total counter");
        sb.AppendLine($"flotilla_server_proposals_total {Volatile.Read(ref _proposalsTotalCount)}");

        sb.AppendLine("# HELP flotilla_server_commits_total Total commits appended");
        sb.AppendLine("# TYPE flotilla_server_commits_total counter");
        sb.AppendLine($"flotilla_server_commits_total {Volatile.Read(ref _commitsTotalCount)}");

        sb.AppendLine("# HELP flotilla_server_tcp_proposals_total Total TCP proposals received");
        sb.AppendLine("# TYPE flotilla_server_tcp_proposals_total counter");
        sb.AppendLine($"flotilla_server_tcp_proposals_total {Volatile.Read(ref _tcpProposalsCount)}");

        sb.AppendLine("# HELP flotilla_server_udp_proposals_total Total UDP proposals received");
        sb.AppendLine("# TYPE flotilla_server_udp_proposals_total counter");
        sb.AppendLine($"flotilla_server_udp_proposals_total {Volatile.Read(ref _udpProposalsCount)}");

        sb.AppendLine("# HELP flotilla_server_grpc_proposals_total Total gRPC proposals received");
        sb.AppendLine("# TYPE flotilla_server_grpc_proposals_total counter");
        sb.AppendLine($"flotilla_server_grpc_proposals_total {Volatile.Read(ref _grpcProposalsCount)}");

        sb.AppendLine("# HELP flotilla_server_proposals_failed_total Total failed proposals");
        sb.AppendLine("# TYPE flotilla_server_proposals_failed_total counter");
        sb.AppendLine($"flotilla_server_proposals_failed_total {Volatile.Read(ref _proposalsFailedCount)}");

        return sb.ToString();
    }
}
