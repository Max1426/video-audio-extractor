# 视频音轨提取软件

<p align="center">
  <img src="src/VideoAudioExtractor/Assets/app-icon.png" width="180" alt="视频音轨提取软件图标">
</p>

一款简洁的 Windows 桌面工具，可将一个或多个视频中的完整音轨批量转换为 MP3。所有处理均在本机完成，视频和音频不会上传到网络。

## 下载

普通用户请前往 [Releases](https://github.com/Max1426/video-audio-extractor/releases/latest) 下载最新的 Windows x64 安装程序。

## 功能

- 拖放或选择多个视频，按队列逐个处理
- 支持 MP4、MOV、MKV、AVI、WebM 等常见格式
- 128 / 192 / 320 kbps 三档 MP3 音质
- FFmpeg 预检、实时进度、取消处理和中文错误提示
- 自动避免覆盖同名文件，失败或取消不留下伪成品
- 记住上次使用的保存目录和音质
- 自包含 Windows x64 安装包，无需用户另装 .NET 或 FFmpeg

## 从源码构建

### 环境

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（只在生成安装包时需要）

在 PowerShell 中运行：

```powershell
.\scripts\build.ps1
```

脚本会自动下载 FFmpeg Windows essentials build，然后编译、发布并生成安装程序：

- 自包含程序：`artifacts/publish`
- 安装程序：`artifacts/installer`

只准备 FFmpeg 依赖：

```powershell
.\scripts\setup-dependencies.ps1
```

## 测试

```powershell
dotnet run --project .\tests\VideoAudioExtractor.SmokeTests\VideoAudioExtractor.SmokeTests.csproj -c Release
```

测试会生成临时媒体文件，并验证中文路径、三档码率、音轨识别、损坏文件、同名避让与取消流程。

## 第三方组件

应用通过独立进程调用 FFmpeg/ffprobe。安装包内随附相应许可证和第三方声明，详情见 [`THIRD-PARTY-NOTICES.txt`](src/VideoAudioExtractor/THIRD-PARTY-NOTICES.txt)。

## 许可证

本项目源码采用 [MIT License](LICENSE)。FFmpeg 及其构建产物遵循各自许可证。
