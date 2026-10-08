## AndX
AndX 展项能力平台 Unity 包。将本地文件（图片/视频）通过二维码的形式进行分享，用户扫码可预览、下载，并按配置支持扫码付费。
```csharp
  // 分享Texture2D 资源
    var _tex = await FileUploader.ShareTexture2D(httpServer, targetTex);
    DisplayQRCode.texture = _tex;

    // 分享本地文件
    // var _filePath = Application.streamingAssetsPath + "/test.png";
    // var _tex2= await FileUploader.ShareLocalFile(httpServer, _filePath);
```

### v1.0
- 对接 AndX 服务