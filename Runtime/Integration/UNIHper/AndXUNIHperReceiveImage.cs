using System;
using System.Threading;
using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using Parful.AndX.Unity;
using UniRx;
using UnityEngine;

namespace Parful.AndX.Integration.UNIHper
{
    /// <summary>反向上传的业务状态；二维码和图片纹理都在主线程准备好。</summary>
    public sealed class ReceiveImageState
    {
        public string Status { get; set; }
        public string StatusText { get; set; }
        public int Version { get; set; }
        public string ErrorCode { get; set; }
        public ReceiveUploadSession Session { get; set; }
        public ReceiveUploadMedia Media { get; set; }
        public Texture2D QrTexture { get; set; }
        public Texture2D ImageTexture { get; set; }

        public bool IsCompleted
        {
            get { return Session != null && Session.IsCompleted; }
        }
    }

    /// <summary>
    /// UNIHper 业务便捷接口：创建普通链接上传二维码 → 等待用户经小程序/H5 上传 →
    /// 下载图片并切回主线程交给 UI。
    /// </summary>
    public static class AndXUNIHperReceiveImageExtensions
    {
        public static IObservable<ReceiveImageState> ReceiveImageAsObservable(
            this Share share,
            ReceiveUploadOptions options = null,
            CancellationToken cancellationToken = default)
        {
            options = options ?? new ReceiveUploadOptions();
            if (options.PollInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "PollInterval 必须大于 0");
            }
            if (options.Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Timeout 必须大于 0");
            }
            // 等待预算必须覆盖服务端的编辑窗口，否则会先于服务端判超时。
            var waitTimeout = options.ResolveTimeout();

            return Observable.Create<ReceiveImageState>(observer =>
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var scheduled = Scheduler.MainThread.Schedule(() =>
                    RunAsync(share, options, waitTimeout, observer, cts.Token));
                return Disposable.Create(() =>
                {
                    scheduled.Dispose();
                    try { cts.Cancel(); } catch (ObjectDisposedException) { }
                    cts.Dispose();
                });
            }).ObserveOnMainThread();
        }

        private static async void RunAsync(
            Share share,
            ReceiveUploadOptions options,
            TimeSpan waitTimeout,
            IObserver<ReceiveImageState> observer,
            CancellationToken cancellationToken)
        {
            try
            {
                var session = await share.CreateReceiveUploadSessionAsync(options, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var qrTexture = await session.LoadQrTextureAsync();
                cancellationToken.ThrowIfCancellationRequested();
                Emit(observer, session, qrTexture, null, "等待用户扫码上传");

                var startedAt = DateTime.UtcNow;
                var lastVersion = session.Version;
                while (true)
                {
                    if (session.IsCompleted)
                    {
                        var imageTexture = await session.Media.LoadTextureAsync();
                        cancellationToken.ThrowIfCancellationRequested();
                        if (imageTexture == null)
                        {
                            throw new AndXException(
                                AndXContract.ErrorCodes.ReceiveUploadFailed,
                                "图片已上传，但下载或解码失败");
                        }
                        Emit(observer, session, qrTexture, imageTexture, "上传完成");
                        observer.OnCompleted();
                        return;
                    }

                    if (session.IsTerminal)
                    {
                        throw new AndXException(
                            string.IsNullOrEmpty(session.ErrorCode) ? session.Status : session.ErrorCode,
                            StatusText(session.Status));
                    }

                    if (DateTime.UtcNow - startedAt > waitTimeout)
                    {
                        throw new AndXException(
                            AndXContract.SdkErrorCodes.Timeout,
                            "等待用户上传图片超时");
                    }

                    await Task.Delay(options.PollInterval, cancellationToken);
                    session = await share.GetReceiveUploadSessionAsync(session.SessionId, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (session.Version != lastVersion)
                    {
                        lastVersion = session.Version;
                        Emit(observer, session, qrTexture, null, StatusText(session.Status));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose 会主动取消；不再向已释放的订阅派发错误。
            }
            catch (Exception error)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    observer.OnError(error);
                }
            }
        }

        private static void Emit(
            IObserver<ReceiveImageState> observer,
            ReceiveUploadSession session,
            Texture2D qrTexture,
            Texture2D imageTexture,
            string statusText)
        {
            observer.OnNext(new ReceiveImageState
            {
                Status = session.Status,
                StatusText = statusText,
                Version = session.Version,
                ErrorCode = session.ErrorCode,
                Session = session,
                Media = session.Media,
                QrTexture = qrTexture,
                ImageTexture = imageTexture,
            });
        }

        private static string StatusText(string status)
        {
            if (string.Equals(status, AndXContract.ReceiveUploadStatuses.Uploading, StringComparison.OrdinalIgnoreCase))
                return "用户正在上传图片";
            if (string.Equals(status, AndXContract.ReceiveUploadStatuses.Processing, StringComparison.OrdinalIgnoreCase))
                return "正在处理图片";
            if (string.Equals(status, AndXContract.ReceiveUploadStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                return "上传完成";
            if (string.Equals(status, AndXContract.ReceiveUploadStatuses.Expired, StringComparison.OrdinalIgnoreCase))
                return "上传二维码已过期";
            if (string.Equals(status, AndXContract.ReceiveUploadStatuses.Canceled, StringComparison.OrdinalIgnoreCase))
                return "上传会话已取消";
            if (string.Equals(status, AndXContract.ReceiveUploadStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                return "图片处理失败";
            return "等待用户扫码上传";
        }
    }
}
