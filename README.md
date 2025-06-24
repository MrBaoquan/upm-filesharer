## File Sharer
将本地文件通过二维码的形式进行分享，用户可以进行扫码预览及下载操作
```csharp
  // 分享Texture2D 资源
    var _tex = await FileUploader.ShareTexture2D(httpServer, targetTex);
    DisplayQRCode.texture = _tex;

    // 分享本地文件
    // var _filePath = Application.streamingAssetsPath + "/test.png";
    // var _tex2= await FileUploader.ShareLocalFile(httpServer, _filePath);
```

### v1.0
- 对接File Sharer服务