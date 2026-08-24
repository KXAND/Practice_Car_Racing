# RaceClient

一个用于展示输入帧同步、客户端预测与回滚的 Windows x64 赛车技术 Demo。

## 构建

在 Unity 中选择 `File > Build Settings`，目标平台选择 **Windows x64**，输出到：

`Builds/RaceClient/RaceClient.exe`

`Project Settings > Player` 中的 `Product Name` 应保持为 `RaceClient`。

## 手动启动三进程

先在项目根目录创建 `Logs` 目录。使用三个 PowerShell 窗口，按以下顺序启动：服务器、键盘客户端、脚本客户端。

```powershell
.\Builds\RaceClient\RaceClient.exe -batchmode -nographics -server -logFile Logs\server.log
```

```powershell
.\Builds\RaceClient\RaceClient.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile Logs\keyboard-client.log -upstreamDelayTicks 0 -dropEveryNthInput 0
```

```powershell
.\Builds\RaceClient\RaceClient.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -scriptedInput -logFile Logs\scripted-client.log -upstreamDelayTicks 0 -dropEveryNthInput 0
```

第一个客户端固定获得 Player 0，可用 `WASD` 或方向键驾驶；第二个客户端固定获得 Player 1，由固定逻辑帧输入序列驾驶并完成一圈。日志位于 `Logs/`，该目录不会提交。

## 命令行参数

- `-server`：启动无玩家的专用服务器。
- `-scriptedInput`：为该客户端选择固定脚本输入；不读取世界状态，也不执行寻路或决策。
- `--dt <N>`（或 `-upstreamDelayTicks <N>`）：覆盖上行输入延迟。仅 `0` 或 `1` 合法；其他值回退为 `0` 并记录警告。
- `--di <N>`（或 `-dropEveryNthInput <N>`）：每第 `N` 条上行输入确定性丢弃；`0` 表示关闭，负数回退为 `0` 并记录警告。

弱网验收使用两名客户端都附加：

```text
--dt 1 --di 3
```

也可使用本地启动脚本：

```powershell
.\Builds\Start-RaceDemo.ps1 -dt 1 -di 3
```

交替转向并保持油门，可在 HUD 与日志中观察 `InputDropped`、`Rollback` 和后续 `HashCheck ... Match=True`。

## 核心架构

服务器与客户端只交换带玩家编号和目标逻辑帧号的量化输入。服务器汇总双方输入为确认帧；双方执行同一份 30 Hz 整数模拟。客户端先预测未确认帧，确认输入不同则恢复历史快照并重演。

项目不使用 `NetworkTransform` 同步赛车位置：Transform 是由本地确定性状态渲染得到的结果，不是网络权威状态。每 30 个确认帧比较同帧世界哈希；不同步仅报警，不自动下发状态快照。

## 面试演示顺序

1. 启动三进程，说明网络只发送输入，第二客户端使用固定帧序列。
2. 启动时加入弱网参数，展示 HUD 的预测深度、回滚次数和日志中的 `Rollback`。
3. 展示后续 `HashCheck` 的 `Local` 与 `Server` 相等且 `Match=True`，说明回滚后重新一致。
