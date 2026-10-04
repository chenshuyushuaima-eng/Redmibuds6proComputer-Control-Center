# 0.1.1 强度调节修正记录

## 依据

- 用户提供的小米耳机 1.38.0 APK，SHA-256 `47b34f62e2e328d17c402702b2efbc643cecd449fd2a1d97877c2370fa9165a6`。仅静态分析，APK 和反编译材料不随公开项目分发。
- `classes10.dex` 的 `NoiseReductionVM.getLevelPos(int)` 按滑块下标读取机型强度表；`setNoiseLevel(int)` 将当前模式和对应强度转为两字节，调用 `IFunctionConfig.noiseLevel`。
- `getReductionMax()` / `getTransMax()` 返回表长度减一；`NoiseReductionView` 将其设为 SeekBar 最大值。`NoiseExtra.levels` 的 JSON 字段名为 `tws_gear`。
- `classes7.dex` 的 `DeviceConfigNoiseLevel` 使用配置 ID `0x0B`，值为 `[ancState, ancLevel]`。
- 按 APK 的 `DeviceService`、`DeviceRequestBean` 和 `RegionUrlSwitcherImpl` 还原中国区产品配置请求。接口为 `https://cn.tws.wear.xiaomiwear.com/twswear/product/get_product_list`，请求数据为 app_platform=0、app_version=1.38.0、last_modify_time=0。读取时间 2026-10-04，响应 code=0。
- 匹配条目：`miwear.headphone.o76cn`，VID `2717`、PID `509d`，与实机设备信息字段 03 的 `27-17-50-9D` 一致。公开项目只保留 [机型配置事实摘要](protocol/redmi-buds6-pro.json)，包含本应用使用的强度范围及空间音频能力，不保留完整接口响应或原始反编译材料。

## 确认的映射

| 调节项 | 官方功能 ID | 机型 tws_gear | 配置 0x0B 的值 |
| --- | --- | --- | --- |
| 手动降噪，轻到深 | 1003 | `[0,1,2,...,19]` | `[1,强度]` |
| 通透 | 1005 | `[0,1,2]` | `[2,强度]` |

`getNoiseTransparentStr` 将 0/1/2 对应为标准通透、人声增强、环境增强。用户截图中的人声增强居中，与初次读取的 `[2,1]` 一致。

界面用连续降噪滑块呈现官方的无级调节体验；通信按官方表取整为 20 个内部强度值。不沿用旧款轻度=1、均衡=0、深度=2 的含义，不将百分比当作设备值。通透保留三个位置，不扩展为任意强度。

## 状态通知

现有运行日志包含：

```text
FE-DC-BA-87-F4-00-06-50-04-00-0B-02-01-EF
FE-DC-BA-87-0E-00-04-4F-02-04-02-EF
```

前者 sequence=50、payload=`04-00-0B-02-01`；后者 sequence=4F、payload=`02-04-02`。两者无 status 字节。旧解码器按位 0x40 判断 status 是否存在，将 sequence 错当作错误状态，丢失通知。

现分开处理：位 0x80 判断是否无 status，位 0x40 判断是否需要请求应答；0x87 通知可更新状态，但不匹配待完成请求，也不发送多余应答。0x04 响应仍读取 status，0xC0 / 0xC4 请求仍保留原认证和请求行为。

## 交互与范围

- 按机型 VID/PID 启用强度调节，其他设备不套用此范围。
- 松手后 400 ms 写入；点击和键盘连续操作合并，拖动期间不发命令。来自耳机的同步不会覆盖正在拖动的位置。
- 自适应降噪开启时禁用手动调节；仅显示当前模式的强度控件。
- 检查写入应答后读取配置 0x0B，检查实际模式和强度，失败时显示错误并恢复读取值。

0.1.1 编译成功。本次没有切换用户正在运行的旧程序，也未对耳机进行新的强度写入或听感测试；新强度范围的实机表现需在新版中确认。APK、官方产品配置和已有通知记录证明编码与映射，不单独证明实际听感。
