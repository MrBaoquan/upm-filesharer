using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UniRx;
using System.Threading.Tasks;
using FileSharer;
using UnityEngine.UI;

public class FileSharerExample : MonoBehaviour
{
    [Tooltip("用于显示二维码的RawImage")]
    public RawImage DisplayQRCode;

    [Tooltip("待上传的图片资源")]
    public Texture2D targetTex;
    public string httpServer = "http://162.16.114.114:6699/api/share";

    public async void ShareFile()
    {
        // 分享Texture2D 资源
        var _tex = await FileUploader.ShareTexture2D(httpServer, targetTex);
        DisplayQRCode.texture = _tex;

        // 分享本地文件
        // var _filePath = Application.streamingAssetsPath + "/test.png";
        // var _tex2 = await FileUploader.ShareLocalFile(httpServer, _filePath);
    }

    // Start is called before the first frame update
    void Start() { }

    // Update is called once per frame
    void Update() { }
}
