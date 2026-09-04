using System.Threading.Channels;

namespace FileAnalysis.Infrastructure.Analysis;

/// <summary>Unbounded queue of file IDs pending AI analysis, decoupling scanning from analysis throughput.</summary>
public class AnalysisQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;
}
