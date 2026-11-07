using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ShopSphere.Tests.Common;

public static class TestResponseBodyFeatureExtensions
{
    public static IServiceCollection AddTestResponseBodyFeatureWorkaround(this IServiceCollection services)
    {
        services.AddTransient<IStartupFilter, ResponseBodyPipeWriterStartupFilter>();
        return services;
    }

    private class ResponseBodyPipeWriterStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                builder.Use(async (context, nextMiddleware) =>
                {
                    var bodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
                    if (bodyFeature != null && bodyFeature is not FixedHttpResponseBodyFeature)
                    {
                        context.Features.Set<IHttpResponseBodyFeature>(new FixedHttpResponseBodyFeature(bodyFeature));
                    }

                    await nextMiddleware();
                });

                next(builder);
            };
        }
    }

    private class FixedHttpResponseBodyFeature : IHttpResponseBodyFeature
    {
        private readonly IHttpResponseBodyFeature _inner;
        private PipeWriter? _writer;

        public FixedHttpResponseBodyFeature(IHttpResponseBodyFeature inner)
        {
            _inner = inner;
        }

        public Stream Stream => _inner.Stream;

        public PipeWriter Writer => _writer ??= new ResponseBodyPipeWriterWrapper(_inner.Writer);

        public Task CompleteAsync() => _inner.CompleteAsync();

        public void DisableBuffering() => _inner.DisableBuffering();

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            => _inner.SendFileAsync(path, offset, count, cancellationToken);

        public Task StartAsync(CancellationToken cancellationToken = default)
            => _inner.StartAsync(cancellationToken);
    }

    private class ResponseBodyPipeWriterWrapper : PipeWriter
    {
        private readonly PipeWriter _inner;
        private long _unflushedBytes;

        public ResponseBodyPipeWriterWrapper(PipeWriter inner)
        {
            _inner = inner;
        }

        public override bool CanGetUnflushedBytes => true;

        public override long UnflushedBytes => _unflushedBytes;

        public override void Advance(int bytes)
        {
            _unflushedBytes += bytes;
            _inner.Advance(bytes);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);

        public override Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);

        public override void CancelPendingFlush() => _inner.CancelPendingFlush();

        public override void Complete(Exception? exception = null) => _inner.Complete(exception);

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            _unflushedBytes = 0;
            return await _inner.FlushAsync(cancellationToken);
        }

        public override async ValueTask CompleteAsync(Exception? exception = null)
        {
            _unflushedBytes = 0;
            await _inner.CompleteAsync(exception);
        }
    }
}

