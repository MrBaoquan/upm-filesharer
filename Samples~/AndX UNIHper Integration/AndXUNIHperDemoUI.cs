using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using Parful.AndX.Integration.UNIHper;
using Parful.AndX.Unity;
using UniRx;
using UnityEngine;
using UnityEngine.UIElements;
using UNIHper;
using UNIHper.UI;

namespace Parful.AndX.Samples
{
    [UIToolkitPage(Asset = "AndXUNIHperDemoUI", Scene = "AndXUNIHperDemo", Type = UIType.Normal, Order = 0)]
    public sealed class AndXUNIHperDemoUI : UIToolkitBase
    {
        private Label configText;
        private Label configRaw;
        private Label configPath;
        private Label businessCode;
        private Label connectionText;
        private Label status;
        private Label resultLog;
        private VisualElement connectionPill;
        private VisualElement diagnosticsPanel;
        private Button diagnosticsButton;
        private TextField exhibitId;
        private TextField paidExhibitId;
        private Label previewHint;
        private Button uploadButton;
        private Button receiveImageButton;
        private Button resolveButton;
        private Button downloadButton;
        private Button payButton;
        private Button orderButton;
        private Button startGameButton;
        private Button aiTextButton;
        private Button aiImageButton;
        private Button reloadButton;
        private Image qrImage;
        private Label qrPlaceholder;
        private Texture2D sourceTexture;
        private string lastTicket;
        private string lastOrderNo;
        private string lastCommandId;
        private bool busy;
        private bool autoDownloadPending;
        private IDisposable currentOperationSubscription;
        private IDisposable currentQrSubscription;
        private int operationVersion;
        private string activeReceiveSessionId;
        private readonly List<Button> actionButtons = new List<Button>();
        private static readonly TimeSpan DemoPlayingDuration = TimeSpan.FromSeconds(3);

        protected override void OnLoaded()
        {
            configText = Q<Label>("config");
            configRaw = Q<Label>("config-raw");
            configPath = Q<Label>("config-path");
            businessCode = Q<Label>("business-code");
            connectionText = Q<Label>("connection-text");
            connectionPill = Q<VisualElement>("connection-pill");
            status = Q<Label>("status");
            diagnosticsButton = Q<Button>("diagnostics-toggle");
            diagnosticsPanel = Q<VisualElement>("diagnostics-panel");
            resultLog = Q<Label>("result-log");
            exhibitId = Q<TextField>("exhibit-id");
            paidExhibitId = Q<TextField>("paid-exhibit-id");
            previewHint = Q<Label>("preview-hint");
            uploadButton = Q<Button>("upload-button");
            receiveImageButton = Q<Button>("receive-image-button");
            resolveButton = Q<Button>("resolve-button");
            downloadButton = Q<Button>("download-button");
            payButton = Q<Button>("pay-button");
            orderButton = Q<Button>("order-button");
            startGameButton = Q<Button>("start-game-button");
            aiTextButton = Q<Button>("ai-text-button");
            aiImageButton = Q<Button>("ai-image-button");
            reloadButton = Q<Button>("reload-button");
            qrImage = Q<Image>("qr-image");
            qrPlaceholder = Q<Label>("qr-placeholder");

            if (diagnosticsPanel != null) diagnosticsPanel.style.display = DisplayStyle.None;
            if (diagnosticsButton != null) diagnosticsButton.text = "显示运行诊断";

            actionButtons.Clear();
            actionButtons.Add(uploadButton);
            actionButtons.Add(receiveImageButton);
            actionButtons.Add(resolveButton);
            actionButtons.Add(downloadButton);
            actionButtons.Add(payButton);
            actionButtons.Add(orderButton);
            actionButtons.Add(startGameButton);
            actionButtons.Add(aiTextButton);
            actionButtons.Add(aiImageButton);
            actionButtons.Add(reloadButton);

            if (qrImage != null) qrImage.scaleMode = ScaleMode.ScaleToFit;
            ApplyReadableFont();
            sourceTexture = CreateSourceTexture();

            var configuredExhibitId = AndXUNIHperBridge.Current == null ? null : AndXUNIHperBridge.Current.ExhibitId;
            if (!string.IsNullOrWhiteSpace(configuredExhibitId) && exhibitId != null) exhibitId.value = configuredExhibitId.Trim();
            if (businessCode != null) businessCode.text = "展项参数可在下方调整；默认值已连接测试展项。";
            RefreshConfig();
        }

        protected override void OnShowing()
        {
            BindButton(uploadButton, OnUploadClicked);
            BindButton(receiveImageButton, OnReceiveImageClicked);
            BindButton(resolveButton, OnResolveClicked);
            BindButton(downloadButton, OnDownloadClicked);
            BindButton(payButton, OnPayClicked);
            BindButton(orderButton, OnOrderClicked);
            BindButton(startGameButton, OnStartGameClicked);
            BindButton(aiTextButton, OnAiTextClicked);
            BindButton(aiImageButton, OnAiImageClicked);
            BindButton(reloadButton, OnReloadClicked);
            BindButton(diagnosticsButton, ToggleDiagnostics);
            LifeCycleDisposables.Add(Disposable.Create(() =>
            {
                UnbindButton(uploadButton, OnUploadClicked);
                UnbindButton(receiveImageButton, OnReceiveImageClicked);
                UnbindButton(resolveButton, OnResolveClicked);
                UnbindButton(downloadButton, OnDownloadClicked);
                UnbindButton(payButton, OnPayClicked);
                UnbindButton(orderButton, OnOrderClicked);
                UnbindButton(startGameButton, OnStartGameClicked);
                UnbindButton(aiTextButton, OnAiTextClicked);
                UnbindButton(aiImageButton, OnAiImageClicked);
                UnbindButton(reloadButton, OnReloadClicked);
                UnbindButton(diagnosticsButton, ToggleDiagnostics);
                CancelCurrentOperation();
            }));
            RefreshConfig();
            if (!busy && (qrImage == null || qrImage.image == null))
            {
                SetSelectedAction(null);
                SetStatus(global::AndX.Config.IsConfigured ? "请选择要体验的功能" : "正在连接现场服务…");
                if (previewHint != null) previewHint.text = "点击「扫码收图」生成二维码";
            }
        }

        private void OnUploadClicked()
        {
            SetSelectedAction(uploadButton);
            RunUpload();
        }

        private void OnReceiveImageClicked()
        {
            SetSelectedAction(receiveImageButton);
            RunObservable(
                global::AndX.Share.ReceiveImageAsObservable(new ReceiveUploadOptions
                {
                    ExhibitId = CurrentExhibitId(),
                    Title = "上传一张照片",
                    // 例如要求成品为 3:4 竖版，最短边不低于 1080。
                    ImageSpec = new ReceiveUploadImageSpec
                    {
                        Aspect = 0.75f,
                        AspectLocked = true,
                        MinEdgePx = 1080,
                    },
                }),
                "扫码收图",
                state =>
                {
                    var waiting = string.Equals(state.Status, "WAITING", StringComparison.OrdinalIgnoreCase);
                    if (waiting)
                    {
                        SetStatus("等待观众扫码\n请让观众用微信扫描右侧二维码", "status-waiting");
                    }
                    else if (state.ImageTexture == null)
                    {
                        SetStatus(state.StatusText, "status-waiting");
                    }

                    if (state.QrTexture != null)
                    {
                        activeReceiveSessionId = state.Session == null ? null : state.Session.SessionId;
                        if (qrImage != null) qrImage.image = state.QrTexture;
                        if (qrPlaceholder != null) qrPlaceholder.style.display = DisplayStyle.None;
                        if (previewHint != null) previewHint.text = "让观众用微信扫一扫，选择一张照片";
                        if (waiting) AppendLog("扫码收图：二维码已生成，等待观众上传");
                    }
                    if (state.ImageTexture != null)
                    {
                        activeReceiveSessionId = null;
                        if (qrImage != null) qrImage.image = state.ImageTexture;
                        if (qrPlaceholder != null) qrPlaceholder.style.display = DisplayStyle.None;
                        if (previewHint != null) previewHint.text = "照片已显示在现场屏幕上";
                        SetStatus("照片已收到\n上传完成，现场屏幕已更新", "status-success");
                        AppendLog("扫码收图完成：mediaId=" + (state.Media == null ? "-" : state.Media.Id));
                        Debug.Log("[AndX][AndXUNIHperDemoUI] 扫码收图完成 mediaId="
                            + (state.Media == null ? "-" : state.Media.Id));
                    }
                });
        }
        private void OnResolveClicked()
        {
            SetSelectedAction(resolveButton);
            if (string.IsNullOrEmpty(lastTicket)) { CancelCurrentOperation(); SetStatus("请先生成分享二维码，再查看扫码结果。", "status-waiting"); return; }
            RunObservable(global::AndX.Share.ResolveAsObservable(lastTicket), "查看扫码结果", result =>
            {
                SetStatus("二维码内容已读取", "status-success");
                AppendLog("扫码：purpose=" + (result.Purpose ?? "-") + " exhibit=" + (result.Exhibit?.Code ?? "-") + " media=" + (result.Media?.Type ?? "-") + " title=" + (result.Media?.Title ?? "-") + " amount=" + (result.Media?.Amount ?? 0) + " entitled=" + result.Entitled);
            });
        }

        private void OnDownloadClicked()
        {
            SetSelectedAction(downloadButton);
            if (string.IsNullOrEmpty(lastTicket)) { CancelCurrentOperation(); SetStatus("请先生成分享二维码，再保存测试文件。", "status-waiting"); return; }
            RunObservable(global::AndX.Share.GetDownloadAsObservable(lastTicket), "保存测试文件", result =>
            {
                SetStatus("文件可以下载了", "status-success");
                AppendLog("下载：type=" + (result.Type ?? "-"));
                Debug.Log("[AndX][AndXUNIHperDemoUI] 下载授权成功 type=" + (result.Type ?? "-"));
            });
        }

        private void OnPayClicked()
        {
            SetSelectedAction(payButton);
            RunObservable(
                global::AndX.Pay.CreateTicketAsObservable(
                    new PayTicketOptions { ExhibitId = PaidExhibitId() }),
                "生成支付码",
                ticket =>
                {
                    lastOrderNo = ticket.OrderNo;
                    SetStatus("支付码已生成\n请扫码完成支付，然后点击「开始付费体验」", "status-waiting");
                    AppendLog("支付码：amount=" + ticket.Amount + " orderNo=" + (ticket.OrderNo ?? "(待下单)"));
                    ShowQr(ticket.LoadQrTextureAsync(), "扫码支付后，点击「开始付费体验」");
                });
        }

        private void OnOrderClicked()
        {
            SetSelectedAction(orderButton);
            if (string.IsNullOrEmpty(lastOrderNo)) { CancelCurrentOperation(); SetStatus("还没有订单号，请先生成支付码并完成支付。", "status-waiting"); return; }
            RunObservable(global::AndX.Pay.QueryOrderAsObservable(lastOrderNo), "查询订单", order =>
            {
                SetStatus("订单状态：" + order.Status, "status-success");
                AppendLog("订单：" + order.OrderNo + " status=" + order.Status + " amount=" + order.Amount);
            });
        }

        private void OnStartGameClicked()
        {
            SetSelectedAction(startGameButton);
            RunObservable(
                global::AndX.Node.HeartbeatAsObservable(PaidExhibitId())
                    .SelectMany(heartbeat =>
                    {
                        var command = heartbeat.Command;
                        if (command == null || !string.Equals(command.Action, "start", StringComparison.OrdinalIgnoreCase))
                        {
                            return Observable.Throw<NodeStateResult>(
                                new AndXException("PAYMENT_REQUIRED", "还没有收到支付成功通知，请完成支付后再试"));
                        }
                        lastOrderNo = command.OrderNo;
                        lastCommandId = command.CommandId;
                        var exhibit = PaidExhibitId();
                        return global::AndX.Node.ReportStateAsObservable(
                                "STARTING", command.OrderNo, command.CommandId, exhibit)
                            .SelectMany(_ => Observable.Timer(TimeSpan.FromMilliseconds(500))
                                .SelectMany(__ => global::AndX.Node.ReportStateAsObservable(
                                    "PLAYING", command.OrderNo, command.CommandId, exhibit))
                                .Do(playing =>
                                {
                                    SetStatus("体验已开始\n3 秒后会自动回到待机状态", "status-success");
                                    AppendLog("游戏运行：orderNo=" + (lastOrderNo ?? "-") + " state=" + (playing.State ?? "-"));
                                })
                                .SelectMany(__ => Observable.Timer(DemoPlayingDuration)
                                    .SelectMany(___ => global::AndX.Node.ReportStateAsObservable(
                                        "IDLE", command.OrderNo, command.CommandId, exhibit))));
                    }),
                "开始付费体验",
                state =>
                {
                    SetStatus("体验已结束\n展项已复位，可以再次体验", "status-success");
                    AppendLog("演示结束并复位：orderNo=" + (lastOrderNo ?? "-") + " commandId=" + (lastCommandId ?? "-") + " state=" + (state.State ?? "-"));
                    Debug.Log("[AndX][AndXUNIHperDemoUI] 付费演示完成并复位 orderNo=" + (lastOrderNo ?? "-") + " state=" + (state.State ?? "-"));
                });
        }

        private void OnAiTextClicked()
        {
            SetSelectedAction(aiTextButton);
            var parameters = new Dictionary<string, object> { ["head"] = "安达创展", ["topic"] = "科技展馆" };
            RunObservable(global::AndX.AI.GenerateTextAsObservable(AndXContract.AITextScenarios.Acrostic, parameters, exhibitId: CurrentExhibitId()), "生成文字", result =>
            {
                SetStatus("文字已生成", "status-success");
                AppendLog("AI 文字：" + AIText.ComposeDisplayText(result));
            });
        }

        private void OnAiImageClicked()
        {
            SetSelectedAction(aiImageButton);
            RunObservable(global::AndX.AI.GenerateImageAsObservable(new AIImageRequest
            {
                Prompt = "科技馆展项宣传图，蓝色未来感，清晰的二维码展示位",
                Size = "1024x1024",
                ExhibitId = CurrentExhibitId(),
            }), "生成图片", job =>
            {
                SetStatus("图片已生成", "status-success");
                AppendLog("AI 生图：status=" + job.Status + " jobNo=" + (job.JobNo ?? "-") + " mediaId=" + (job.MediaId ?? "-"));
            });
        }

        private void OnReloadClicked()
        {
            SetSelectedAction(reloadButton);
            CancelCurrentOperation();
            AndXUNIHperBridge.ReloadAndApply();
            RefreshConfig();
            SetStatus(global::AndX.Config.IsConfigured ? "配置已重新加载" : "配置仍未连接，请检查运行诊断", global::AndX.Config.IsConfigured ? "status-success" : "status-error");
        }

        private void RunUpload()
        {
            if (!global::AndX.Config.IsConfigured) { SetStatus("现场服务尚未连接，请展开运行诊断查看原因", "status-error"); return; }
            if (sourceTexture == null) { SetStatus("测试图片创建失败，请重新进入页面", "status-error"); return; }
            var progress = new Progress<UploadProgress>(value => SetStatus("正在上传测试图片 " + value.Percent.ToString("P0"), "status-waiting"));
            RunObservable(
                global::AndX.Share.UploadAsObservable(
                    new TexturePayload(sourceTexture),
                    new UploadOptions { Title = "UNIHper + AndX 示例", ExhibitId = CurrentExhibitId() },
                    progress),
                "上传测试图片",
                result =>
                {
                    // 票据先落地，二维码渲染独立执行；图片加载异常不能阻塞下载授权等后续操作。
                    lastTicket = result.Token;
                    autoDownloadPending = true;
                    SetStatus("分享二维码已生成\n观众扫码即可查看或保存图片", "status-success");
                    AppendLog("分享二维码已生成");
                    Debug.Log("[AndX][AndXUNIHperDemoUI] 分享二维码已生成");
                    LoadQrAfterUpload(result);
                });
        }

        private void LoadQrAfterUpload(ShareResult result)
        {
            ShowQr(result.LoadQrTextureAsync(), "让观众扫码查看或保存这张图片");
        }

        private void ShowQr(Task<Texture2D> load, string hint)
        {
            currentQrSubscription?.Dispose();
            currentQrSubscription = null;
            if (qrImage != null) qrImage.image = null;
            if (qrPlaceholder != null)
            {
                qrPlaceholder.text = "正在生成二维码…";
                qrPlaceholder.style.display = DisplayStyle.Flex;
            }

            currentQrSubscription = load.ToObservable()
                .Timeout(TimeSpan.FromSeconds(10))
                .ObserveOnMainThread()
                .Subscribe(
                    texture =>
                    {
                        if (qrImage == null || texture == null) return;
                        qrImage.image = texture;
                        if (qrPlaceholder != null) qrPlaceholder.style.display = DisplayStyle.None;
                        if (previewHint != null) previewHint.text = hint;
                    },
                    error =>
                    {
                        var message = "二维码渲染失败：" + DescribeError(error);
                        if (qrPlaceholder != null) qrPlaceholder.text = "二维码加载失败";
                        if (previewHint != null) previewHint.text = "二维码暂时没有加载出来，请展开运行诊断查看";
                        AppendLog(message);
                        Debug.LogWarning("[AndX][AndXUNIHperDemoUI] " + message);
                    });
        }

        private void RunObservable<T>(IObservable<T> stream, string action, Action<T> onSuccess)
        {
            CancelCurrentOperation();
            var version = operationVersion;
            busy = true;
            SetStatus(action + "…", "status-waiting");
            var subscription = stream.ObserveOnMainThread().Subscribe(
                value =>
                {
                    if (version == operationVersion) onSuccess(value);
                },
                error =>
                {
                    if (version != operationVersion) return;
                    busy = false;
                    currentOperationSubscription = null;
                    SetStatus(action + "失败\n" + DescribeError(error), "status-error");
                    AppendLog(action + "失败：" + DescribeError(error));
                    Debug.LogError("[AndX][AndXUNIHperDemoUI] " + error);
                },
                () =>
                {
                    if (version != operationVersion) return;
                    busy = false;
                    currentOperationSubscription = null;
                    if (autoDownloadPending && action == "上传测试图片")
                    {
                        autoDownloadPending = false;
                        OnDownloadClicked();
                    }
                });
            if (version == operationVersion) currentOperationSubscription = subscription;
            else subscription.Dispose();
        }

        private void CancelCurrentOperation()
        {
            operationVersion++;
            busy = false;
            autoDownloadPending = false;
            var receiveSessionId = activeReceiveSessionId;
            activeReceiveSessionId = null;
            if (!string.IsNullOrEmpty(receiveSessionId))
            {
                if (qrImage != null) qrImage.image = null;
                if (qrPlaceholder != null)
                {
                    qrPlaceholder.text = "等待生成二维码";
                    qrPlaceholder.style.display = DisplayStyle.Flex;
                }
                _ = CancelReceiveSessionQuietly(receiveSessionId);
            }
            var subscription = currentOperationSubscription;
            currentOperationSubscription = null;
            subscription?.Dispose();
            var qrSubscription = currentQrSubscription;
            currentQrSubscription = null;
            qrSubscription?.Dispose();
        }
        private static async Task CancelReceiveSessionQuietly(string sessionId)
        {
            try { await global::AndX.Share.CancelReceiveUploadSessionAsync(sessionId); }
            catch (Exception) { }
        }
        private void SetSelectedAction(Button selected)
        {
            foreach (var button in actionButtons)
            {
                if (button == null) continue;
                button.RemoveFromClassList("action-selected");
            }
            selected?.AddToClassList("action-selected");
        }

        private void ToggleDiagnostics()
        {
            if (diagnosticsPanel == null) return;
            var visible = diagnosticsPanel.style.display == DisplayStyle.Flex;
            diagnosticsPanel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            if (diagnosticsButton != null)
            {
                diagnosticsButton.text = visible ? "显示运行诊断" : "收起运行诊断";
            }
        }

        private void RefreshConfig()
        {
            var configured = global::AndX.Config.IsConfigured;
            if (configText != null)
            {
                configText.text = configured
                    ? "服务已连接 · 当前展项 " + (CurrentExhibitId() ?? "由节点自动识别")
                    : "正在连接现场服务…";
            }
            if (configRaw != null)
            {
                configRaw.text = configured ? global::AndX.Config.Describe() : "AndX 尚未配置，等待 UNIHper 加载 AndX.xml。";
            }
            if (configPath != null)
            {
                configPath.text = "配置文件：" + (AndXUNIHperBridge.ConfigFilePath ?? "尚未加载");
            }
            if (connectionText != null)
            {
                connectionText.text = configured ? "服务已连接" : "连接中";
            }
            if (connectionPill != null)
            {
                if (configured) connectionPill.RemoveFromClassList("connection-error");
                else connectionPill.AddToClassList("connection-error");
            }
        }

        private string CurrentExhibitId() { return string.IsNullOrWhiteSpace(exhibitId?.value) ? null : exhibitId.value.Trim(); }

        private string PaidExhibitId()
        {
            return string.IsNullOrWhiteSpace(paidExhibitId?.value)
                ? CurrentExhibitId()
                : paidExhibitId.value.Trim();
        }
        private void SetStatus(string text, string toneClass = null)
        {
            if (status == null) return;
            status.text = text;
            status.RemoveFromClassList("status-waiting");
            status.RemoveFromClassList("status-success");
            status.RemoveFromClassList("status-error");
            if (!string.IsNullOrEmpty(toneClass)) status.AddToClassList(toneClass);
        }

        private void AppendLog(string text)
        {
            if (resultLog == null) return;
            var next = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + "\n" + resultLog.text;
            var lines = next.Split('\n');
            if (lines.Length > 14)
            {
                var kept = new string[14];
                Array.Copy(lines, kept, 14);
                next = string.Join("\n", kept);
            }
            resultLog.text = next;
        }

        private static void BindButton(Button button, Action action) { if (button != null) button.clicked += action; }
        private static void UnbindButton(Button button, Action action) { if (button != null) button.clicked -= action; }

        private void ApplyReadableFont()
        {
            if (Root == null) return;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) Root.style.unityFont = font;
        }

        private void OnDestroy()
        {
            CancelCurrentOperation();
            if (sourceTexture != null) { Destroy(sourceTexture); sourceTexture = null; }
        }

        private static string DescribeError(Exception error)
        {
            var andxError = error as AndXException;
            return andxError == null ? error.GetType().Name + ": " + error.Message : "[" + andxError.Code + "] " + andxError.Message;
        }

        private static Texture2D CreateSourceTexture()
        {
            const int width = 640;
            const int height = 360;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var left = new Color(0.08f, 0.34f, 0.84f);
            var right = new Color(0.9f, 0.22f, 0.52f);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var t = (float)x / (width - 1);
                    var color = Color.Lerp(left, right, t);
                    if (x % 80 < 2 || y % 80 < 2) color = Color.Lerp(color, Color.white, 0.35f);
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            return texture;
        }

        private sealed class UploadView
        {
            public UploadView(ShareResult result, Texture2D texture) { Result = result; Texture = texture; }
            public ShareResult Result { get; }
            public Texture2D Texture { get; }
        }
    }
}
