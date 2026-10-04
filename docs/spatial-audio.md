# 0.1.2 空间音频实现依据

## 分析材料

使用用户提供的小米耳机 `com.mi.earphone` 1.38.0 APK，哈希见 [强度调节记录](noise-strength.md)。APK 仅用于静态分析，没有安装、修改或随软件分发。

对应机型 `miwear.headphone.o76cn`，VID/PID `2717/509d`，固件目标 1.1.9.9。此前取得的官方机型配置包含功能 2002（空间音频）、2004（头部追踪）、2009（场景）；2009 的 `sound_render.scenes` 为 `[1,2,3,4,5]`。相关事实保存在 [机型配置摘要](protocol/redmi-buds6-pro.json)，公开项目不包含完整接口响应。

## 主开关与追踪

`classes7.dex` 中 `DeviceConfigSpatialAudio` 的写入构造函数使用配置 ID `0x1D`。`paramsToValue` 编码为单字节：

| 位 | 含义 |
| --- | --- |
| 0 | 空间音频开关 |
| 1–2 | 旧版偏好字段，官方当前 UI 写入 1 |
| 3 | 头部追踪开关 |
| 4 | 虚拟环绕开关 |

`classes10.dex` 的 `SpatialAudioVM.setConfig$default` 将偏好参数设为 1。`SpatialAudioActivity` 修改主开关时保持已有追踪和环绕值；修改追踪时保持已有主开关和环绕值。

状态读取使用配置 **`0x1E`**，不能直接查询写入编号 `0x1D`。证据：`SpatialAudioActivity.initView` 将 `0x1E` 加入查询列表，`SpatialAudioActivity$setListener$1` 读取该类型并以 `DeviceConfigSpatialAudio` 解析。位解析由该类 `valueToParams` 确认。

桌面程序每次修改前先读取 0x1E，用实际的三个开关值构造 0x1D 写入；成功应答后再次读取 0x1E，核对目标开关。关闭主开关时保留追踪偏好，UI 禁用追踪操作。

## 场景渲染

`DeviceConfigSceneRendering` 配置 ID 为 **`0x36`**。`paramsToValue` 开启时编码 `[1,scene]`，关闭时编码 `[0]`；`valueToParams` 读取开关和可选场景字节。

`SpatialAudioActivity.showSceneRenderingGroupIfNeed` 按机型场景列表展示选项，`getSceneMode` / `getSceneRenderingView` 确认对应关系：

| 值 | UI 场景 |
| --- | --- |
| 1 | 经典 |
| 2 | 音乐 |
| 3 | 视频 |
| 4 | 游戏 |
| 5 | 有声书 |

官方 UI 选择场景时传入当前空间音频主开关。桌面程序仅允许主开关开启后选择，写入 `[1,scene]`，随后查询 0x36 并核对两字节，再同步空间音频状态。

## 适配与状态同步

- 新查询限定于 VID/PID 匹配的国行 REDMI Buds 6 Pro，其他机型不套用该协议配置。
- 初次连接、手动刷新、周期刷新查询 0x1E 与 0x36。
- F4 配置通知沿用修正后的 0x87 解码器，更新状态并刷新 UI。
- 没有读到实际状态时禁用开关。场景关闭时可在空间音频开启后选择一个场景，不要求之前已有选中项。
- 该机型功能列表不包含 2018（个性化空间音频校准）或 2020（另一个算法选择）；不加入其他机型特有的入口。
- 这是耳机端设置控制，不改变 Windows 的空间音效选项，也不实现 Windows 音频驱动。

## 完成与待确认

0.1.2 的协议、查询、回读和 UI 已实现并编译成功。没有终止用户运行中的 0.1.1，也没有对实机执行本次新增写入。空间音频开启效果、追踪听感及五种场景的实际表现仍需在新版使用中确认。
