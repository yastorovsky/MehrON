namespace ServiceLib.Aether;

/// <summary>
/// PattN (PattNG core/SocksAnswers.kt): whether the SOCKS5 server on a loopback port answers its greeting, asked over at
/// most one connection per port at a time.
/// <para>
/// An Aether core binds its listener as it starts and serves it only once its tunnel is up. Until then the system queues
/// every connection to it, and nothing answers. A probe that hung up after its wait and connected anew at its next try
/// left a dead connection in that queue each time: two a second over a long Tor or Psiphon warm-up, ahead of the clients
/// the tunnel serves first. Here a greeting that got no answer yet is kept waiting, and the next question about that port
/// reads on, on the same connection, while it stays unanswered; questions asked at the same time share one answer. Once
/// the kept connection is answered or ended, a new one tells whether the port is served now: an answer that waited there
/// can be older than the server's end.
/// </para>
/// <para>At most one connection per port stays open while nobody asks.</para>
/// </summary>
public sealed class AetherSocksAnswers
{
    /// <summary>The SOCKS5 greeting, no authentication offered.</summary>
    private static readonly byte[] Greeting = [5, 1, 0];

    private const byte SocksVersion = 5;

    /// <summary>The SOCKS5 method "no authentication", the only one the greeting offers.</summary>
    private const byte NoAuthentication = 0;

    /// <summary>The SOCKS5 answer "no acceptable method": a SOCKS server all the same.</summary>
    private const byte NoAcceptableMethod = 0xFF;

    private readonly TimeSpan _wait;
    private readonly TimeSpan _connectTimeout;
    private readonly object _gate = new();

    /// <summary>The questions under way, by port; whoever asks about that port meanwhile takes its answer. Under <see cref="_gate" />.</summary>
    private readonly Dictionary<int, Task<bool>> _asking = [];

    /// <summary>The connections whose greeting got no answer yet, by port. Under <see cref="_gate" />.</summary>
    private readonly Dictionary<int, TcpClient> _waiting = [];

    /// <param name="wait">How long a question waits for the answer on its connection.</param>
    /// <param name="connectTimeout">How long a new connection may take to be made; <paramref name="wait" /> unless given.</param>
    public AetherSocksAnswers(TimeSpan wait, TimeSpan? connectTimeout = null)
    {
        _wait = wait;
        _connectTimeout = connectTimeout ?? wait;
    }

    private enum Reply
    {
        Answered,
        Waiting,
        Ended,
    }

    /// <summary>
    /// True when the SOCKS5 server on the loopback <paramref name="port" /> answers the greeting with the version and a
    /// method the greeting allows within the wait, on the connection an earlier question left waiting where there is one
    /// (<see cref="AetherCoreManager.AnswersSocksAsync" /> tells why the whole answer counts). A cancellation of
    /// <paramref name="ct" /> throws <see cref="OperationCanceledException" /> at once; the question goes on for those
    /// who share it, and its connection is kept or closed as it ends.
    /// </summary>
    public Task<bool> AnswersAsync(int port, CancellationToken ct = default)
    {
        TaskCompletionSource<bool>? mine = null;
        Task<bool>? question;
        lock (_gate)
        {
            if (!_asking.TryGetValue(port, out question))
            {
                mine = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                question = mine.Task;
                _asking[port] = question;
            }
        }
        if (mine != null)
        {
            _ = AnswerAsync(port, mine);
        }
        return question.WaitAsync(ct);
    }

    /// <summary>Asks about <paramref name="port" />, and gives the answer to <paramref name="question" /> and to whoever shares it.</summary>
    private async Task AnswerAsync(int port, TaskCompletionSource<bool> question)
    {
        var answered = false;
        try
        {
            answered = await AskAsync(port).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"AetherCore: the readiness probe of port {port} failed", ex);
        }
        finally
        {
            lock (_gate)
            {
                _asking.Remove(port);
            }
            question.TrySetResult(answered);
        }
    }

    /// <summary>
    /// Reads on over the connection left waiting for <paramref name="port" /> while it stays unanswered, so that no new one
    /// queues behind it; asks over a new connection where there is none, or once the kept one was answered or ended. An
    /// answer that waited on the kept one can be older than the end of the server that sent it, so only a new connection
    /// tells that the port is served now.
    /// </summary>
    private async Task<bool> AskAsync(int port)
    {
        TcpClient? kept;
        lock (_gate)
        {
            _waiting.Remove(port, out kept);
        }
        if (kept != null && await ReadAsync(kept).ConfigureAwait(false) == Reply.Waiting)
        {
            return Keep(port, kept);
        }
        if (await GreetAsync(port).ConfigureAwait(false) is not { } client)
        {
            return false;
        }
        return await ReadAsync(client).ConfigureAwait(false) switch
        {
            Reply.Answered => true,
            Reply.Waiting => Keep(port, client),
            _ => false,
        };
    }

    /// <summary>
    /// A connection to <paramref name="port" /> with the greeting sent, null when none could be made. A connection to itself
    /// (<see cref="AetherCoreLoad.IsSelfConnect" />, D6) is none: the greeting it sends would come back as its answer.
    /// </summary>
    private async Task<TcpClient?> GreetAsync(int port)
    {
        var client = new TcpClient();
        try
        {
            using (var connecting = new CancellationTokenSource(_connectTimeout))
            {
                await client.ConnectAsync(Global.Loopback, port, connecting.Token).ConfigureAwait(false);
            }
            if (AetherCoreLoad.IsSelfConnect(client.Client.LocalEndPoint, client.Client.RemoteEndPoint))
            {
                client.Dispose();
                return null;
            }
            using var writing = new CancellationTokenSource(_wait);
            await client.GetStream().WriteAsync(Greeting, writing.Token).ConfigureAwait(false);
            return client;
        }
        catch (Exception ex)
        {
            client.Dispose();
            if (ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
            {
                return null;
            }
            throw;
        }
    }

    /// <summary>
    /// Waits at most the wait for the answer on <paramref name="client" />, which stays open only while no answer has begun.
    /// The whole answer counts, the version and a method the greeting allows: another client's greeting, read on a
    /// connection two probes made to each other, begins with the version as well. Once the version came, the byte after it
    /// decides, within a wait of its own, and the connection is done with either way: a connection is kept for an answer
    /// that has not begun, never for the rest of one.
    /// </summary>
    private async Task<Reply> ReadAsync(TcpClient client)
    {
        var reply = new byte[2];
        try
        {
            var stream = client.GetStream();
            using (var waiting = new CancellationTokenSource(_wait))
            {
                try
                {
                    if (await stream.ReadAsync(reply.AsMemory(0, 1), waiting.Token).ConfigureAwait(false) == 0)
                    {
                        client.Dispose();
                        return Reply.Ended;
                    }
                }
                catch (OperationCanceledException) when (waiting.IsCancellationRequested)
                {
                    return Reply.Waiting;
                }
            }
            var answered = false;
            if (reply[0] == SocksVersion)
            {
                using var rest = new CancellationTokenSource(_wait);
                answered = await stream.ReadAsync(reply.AsMemory(1, 1), rest.Token).ConfigureAwait(false) == 1
                           && reply[1] is NoAuthentication or NoAcceptableMethod;
            }
            client.Dispose();
            return answered ? Reply.Answered : Reply.Ended;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException
                                       or InvalidOperationException)
        {
            client.Dispose();
            return Reply.Ended;
        }
    }

    /// <summary>Keeps <paramref name="client" /> waiting for its answer, in place of any other for <paramref name="port" />; false, as no answer came yet.</summary>
    private bool Keep(int port, TcpClient client)
    {
        TcpClient? replaced;
        lock (_gate)
        {
            _waiting.Remove(port, out replaced);
            _waiting[port] = client;
        }
        replaced?.Dispose();
        return false;
    }
}
