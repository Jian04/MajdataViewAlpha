# 联机与跨平台评估

核对日期：2026-10-06。本轮发布仍为 Windows 版本；下面把已实现的修复和后续迁移分开记录。

## 本轮已修复并验证

- 公网大厅的应答原来依赖 WPF 同步上下文，编辑器忙时会拖后目录应答。现在独立后台运行，创建房间时先保存普通字符串，后台不读取 WPF 控件，避免界面阻塞和跨线程访问。
- 目录连接增加超时重试、保活，并完整接收分片的 WebSocket 文本消息。使用独立主机连接和搜索连接实际发现了测试房间；本地联机回归通过。这个测试验证当前机器到服务的连通性，不能代表所有城市、运营商或无代理网络。
- 房间服务器、WebSocket 桥接、客户端请求及素材下载均在后台执行，回归使用不分派回调的模拟界面线程验证不会依赖 UI 完成通信。新版素材使用带 HMAC 的 64 KB 原始二进制块，旧版 JSON 传输仍兼容；进度按 0.1 MB 更新，断点与最终文件校验保留。
- 公网连接同时尝试两条线路，首条实际能接入的线路胜出，停止未使用的辅助进程，避免串行等待下载/超时后才回退。房间目录搜索也移到后台。
- 已内置 WebRTC DataChannel：通过现有签名房间协议交换 SDP，优先用可靠有序直连传素材；10 秒内不能协商则自动继续用公网隧道。直连断开后按已接收偏移重试隧道。用户无需另装软件或登录网络账户。接收队列和发送积压有上限，PV 仍使用独立连接。
- 录制使用标准输入 `pipe:0` 传帧，取消 Windows 命名管道依赖；3 个缓冲区的后台 FIFO 保持顺序，编码器慢时等待，不主动丢帧。Windows 编码/解码检查：两秒 60 fps 为 120 帧，120 fps 为 240 帧。仍使用同步 GPU 读回，超高分辨率的速度需要另行评测。
- 编码器可以按 `StreamingAssets/Encoders/windows-x64/ffmpeg.exe`、`macos-arm64/ffmpeg`、`macos-x64/ffmpeg`、`linux-x64/ffmpeg` 分发，兼容原 `StreamingAssets/ffmpeg.exe`。当前只打包、验证了 Windows 二进制；这不意味着程序已经能在其他平台运行。

## 联机下一步

现有链路为「免费 WebSocket 目录发现房间 → 房主临时公网隧道 → 房主保存与合并谱面」。目录只广播消息，不存储房间；Cloudflare Quick Tunnel 是临时开发服务，没有可用性保证。因此增加重试能改善瞬时断线，但无法给免费第三方服务承诺长期稳定。来源：[目录协议](https://router.metapage.io/docs/)、[Quick Tunnel 限制](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/)。

| 方案 | 用户操作 | 好处 | 边界 |
| --- | --- | --- | --- |
| 继续优化当前公网隧道 | 软件自动连接 | 改动小，已有实时编辑和续传 | 目录与隧道是外部依赖，中国不同网络表现须实测 |
| WebRTC DataChannel 优先直连，隧道兜底 | 软件内自动协商 | 直连成功时歌曲素材不绕中继 | 仍需信令；部分 NAT 需要 TURN，中继流量不是无限免费的 |
| 自有目录与中继 | 软件自动连接 | 房间目录、限流与故障恢复可控 | 要维护服务并承担费用，暂不符合当前要求 |

当前自动尝试直连，签名的房间连接承担协商；大厅目录不广播候选地址。使用 SIPSorcery 10.0.17 的纯托管实现及公开 STUN，不包含 TURN 账户或收费密钥。STUN 不承载歌曲数据，也无法保证穿透所有 NAT；对称 NAT、UDP 被封等情形会回退隧道。Cloudflare 明确说明公开 STUN 免费且不限额；TURN 需要另行配置，不能把 STUN 当作免费中继。来源：[WebRTC 连接](https://webrtc.org/getting-started/peer-connections/)、[SIPSorcery](https://www.nuget.org/packages/SIPSorcery/10.0.17)、[STUN/TURN 说明](https://developers.cloudflare.com/realtime/turn/faq/)。

先补房间目录应答健康状态、搜索耗时、连接路径、修改延迟与素材吞吐的诊断日志，再比较多个免费服务的国内可达性。日志留在诊断页或文件，不把内部信息堆到联机页面。后续目录连接应做多端点配置与故障切换，避免每次换服务都要重新发布。

## 跨平台迁移路径

### 国内联网的具体选择

「软件内自动 UDP/ICE 打洞 → 失败后切换中继」已加入本轮版本。房间目录和兜底隧道仍是海外服务，国内节点与跨运营商可达性是下一步需验证的部分。

| 候选 | 适用部分 | 需要解决的问题 |
| --- | --- | --- |
| frp XTCP + STCP 兜底 | 开源打洞与中继，可内置客户端 | 需要可用的 frps；官方明确打洞成功率受 NAT 影响，可配置自动转 STCP。不是仅装 frpc 就不需要服务器 |
| LoCyanFrp / SakuraFrp | 国内服务商的免费穿透或中继候选 | 需要账户、令牌与节点配置；国内节点条件、第三方接入许可、限速和额度须确认。不能把一个维护者的长期账户令牌直接发给所有用户 |
| 公共 EMQX MQTT/WSS | 房间目录与小体积信令的原型、备用端点 | 是公开测试服务，消息可被其他订阅者读取；不发送密码、谱面或素材，不把它当作有服务保证的大文件中继 |
| EOS Lobby + P2P | 成熟游戏联机 SDK 的另一候选，官方列出免费大厅和 P2P | 需要开发者项目配置、SDK 集成和许可核对；仍要实际测中国移动/联通/电信连通性，不能因服务免费就承诺国内稳定 |

建议优先用开源 WebRTC/ICE 实现直连，目录加入多端点故障切换；中继接口保留可更换的国内 FRP 服务适配器。在拿到可用节点和第三方接入许可前，不把某个免费提供商写死为默认。客户端内置后可自动启动和管理连接，用户不必另装 Tailscale 或 FRP GUI。

本轮实测：房间 6.40 秒可连接；同一份随机 512 KB 素材走免费隧道需 18.62 秒，通过该公网房间协商直连需 3.10 秒，直连传输需 3.36 秒，字节一致。另一个 6.7 MB 完整直连测试需 36.08 秒，传完后房间协议仍可复用。两端进程在同一台机器，因此验证了公网协商和直连链路，不代表跨城速度。四路隧道并行测试发生超时，已取消该实现，没有设为默认。国内跨运营商测试尚未完成，回退隧道仍可能较慢。

原始资料：[frp XTCP 与兜底](https://gofrp.org/en/docs/features/xtcp/)、[LoCyanFrp 官网及开放 API 说明](https://www.locyanfrp.cn/)、[LoCyanFrp 客户端令牌配置](https://docs.locyanfrp.cn/zh-cn/tutorials/daemon/docker)、[SakuraFrp 免费额度](https://www.natfrp.com/)、[SakuraFrp 节点访问条件](https://doc.natfrp.com/faq/site-inaccessible.html)、[EMQX 公共测试 Broker](https://www.emqx.com/en/mqtt/public-mqtt5-broker)、[EOS 多人服务](https://onlineservices.epicgames.com/multiplayer?lang=en-US)。

### 2026-10-06：连接与歌曲安装恢复

公网地址现在必须收到实际房间的挑战消息才会发布；大厅连接完成后才提示可从大厅加入。系统代理无法升级 WebSocket 时尝试直连 TLS。房主的隧道辅助进程退出后会重新建立地址，并撤下旧大厅入口；仍使用旧中继地址的客机需要刷新大厅重新加入。

WebRTC 数据通道需完成一次房间认证才替换中继。下载后读取最新谱面遇到失效连接会自动重连一次，编辑请求不重复提交。同步过程中房主换歌会被检测，不把新谱面套到旧歌曲素材上。

歌曲音频和波形在后台预加载，成功后才替换编辑器当前歌曲。联机加载 48 kHz 音频不再弹出采样率确认框。解码线程显式选择已初始化的 BASS 设备；输出设备故障与文件解码失败分开处理。坏音频保留当前谱面并显示实际 BASS 错误和文件位置。安装阶段显示“素材已校验 / 读取最新谱面 / 加载音频和波形”，诊断记录在 Edit 目录的 `collaboration-network.log` 和 `collaboration-error.log`。

公网完整测试使用本地合成的 48 kHz MP3：约 6.7 MB 文件通过房间同步、校验、BASS 解码并选中房主难度，WPF 心跳持续运行；有效传输但无效音频的测试保留了本地谱面。一次较早测试因回退慢中继，两分钟只收到约 2.3 MB，因此仍不能承诺免费中继速度或跨运营商成功率。这些测试两端位于同一台电脑，不等同于跨城实测。

## 跨平台实现

### 2026-10-06：谱面文档与播放分离

联机同步只更新编辑器中的 TXT 文档及未保存状态，不暂停、停止或重载正在播放的 View，也不替换当前打击音时间表与结束时间。下一次用户主动播放才使用修改后的谱面。

双方支持新版协议时仅发送修改的文字片段；直连长轮询及时通知客机，并保留旧协议兼容。素材安装前按大小与 SHA-256 校验本地谱面列表和房间缓存，相同的歌曲音频、封面和 PV 可直接复用。不会为了日常文字修改重新传素材。

本机两端通过真实公网房间完成协商后，文字变化通知实测为 58 ms；这验证通知链路，不代表跨城或国内不同运营商延迟。实际 Edit 回归测试验证收到文字及偏移修改后没有发送任何 View 控制命令，当前播放状态、声音时间表与结束时间均保持不变。

WPF 只运行在 Windows；不能靠改 Unity Build Target 迁移 Edit/Launcher。[Microsoft 说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)。

| 模块 | 现状 | 建议 |
| --- | --- | --- |
| Simai/Alpha 解析、时序、噪域几何 | `Assets/MajdataCore` 已共享源文件 | 拆成无 WPF/Unity 依赖的公共库，保持两端使用同一解析器 |
| Edit 界面、文本编辑、侧边栏 | WPF、AvalonEdit，逻辑大量位于 MainWindow | 先拆 ViewModel 与谱面文档服务，再迁移 Avalonia；文本编辑控件需适配，不是直接复制 XAML |
| Launcher 与桌宠 | WPF 与 Windows 窗口/拖拽接口 | 桌面壳按平台实现；移动端桌宠放应用内部，不依赖浮动桌面窗口 |
| Unity View/Play | Windows 构建与本机窗口接口 | 平台条件编译，替换窗口位置、发光、进程通信；保留谱面显示与判定核心 |
| 音频、封面 | BASS 原生库、GDI+ 等 Windows 实现 | 抽出音频解码/混音与绘图接口；核对每平台原生库与许可，封面改 Skia 或 Unity 离屏绘制 |
| 桌面导出 | FFmpeg 可执行程序 | 每平台/架构独立包；macOS 需执行权限与签名，不能把 Windows exe 改名使用 |
| 移动端导出 | 目前没有实现 | Android 用 MediaCodec/MediaMuxer，iOS 用 AVAssetWriter；直接使用 GPU Surface/像素缓冲和统一时间戳 |
| 文件、素材与 PV | 路径与桌面文件系统 | 改存储服务，处理沙盒、授权选择文件及 URI；H.264/AAC 优先，实际核对每平台编解码支持 |

Avalonia 支持 Windows/macOS/Linux/Android/iOS，但各系统的版本和支持等级不同；移动端还需重新设计触屏布局，不能把桌面窗口等比缩小。[官方平台表](https://docs.avaloniaui.net/docs/supported-platforms)。

顺序建议：公共文档与时序库 → macOS 桌面 Edit/Launcher → macOS View/Play → Android 单应用中的编辑/预览 → iOS。移动端采用一个应用内的编辑与预览页面，共享同一文档和时钟，不照搬「Edit + View 多进程、多窗口」。桌面仍可保留多窗口录制。

GPU 优化应在当前顺序传帧测试基础上实现 RenderTexture + AsyncGPUReadback 的有界队列，同时保存每帧序号和时间戳；排队等待不能推进谱面时钟或跳过帧。Unity 明确指出异步读回会增加数帧延迟，所以不能仅替换 ReadPixels 就结束。[Unity API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html)。

移动端编码入口参考：[Android MediaCodec](https://developer.android.com/reference/android/media/MediaCodec)、[Apple AVAssetWriter](https://developer.apple.com/documentation/avfoundation/avassetwriter)。FFmpeg 打包还要核对所选构建的 LGPL/GPL 选项并附上对应许可与来源，尤其当前 libx264 方案。[FFmpeg 许可说明](https://www.ffmpeg.org/legal.html)。

验收需覆盖：同一谱面解析/物量/时长一致，播放/暂停/拖动时序一致，录制帧数与音频时间戳一致；跨网络房间能发现和重连、修改延迟可记录，素材传输不中断文字编辑。macOS/移动端尚未进行真机验证，不能当作已发布功能。
