# 华夏 MMORPG — ET10 服务端

> 复刻《华夏2》(2006, 深圳网域 C++ MFC42) 的 dotnet 服务端
> 协议逆向自 `DataLink.dll` (49KB, x86 PE32)，通过 ET10 跨平台框架实现
> macOS arm64 原生编译运行，同时支持未来 Windows 真实客户端对接

---

## 快速开始

```bash
# 1. 启动 MongoDB (首次自动拉镜像)
docker run -d --name hx-mongodb -p 27017:27017 mongo:7

# 2. 全量编译 + 启动 ET10 StateSync
cd /Users/majun/Developer/ET10
./hx-start.sh

# 3. 跑 MockClient 测 Gate
./hx-start.sh --mock

# 4. 停服
./hx-stop.sh
```

| 场景 | 命令 |
|---|---|
| 首次启动 / 改了代码 | `./hx-start.sh` |
| 代码没变，只启动 | `./hx-start.sh --quick` |
| 自动跑 MockClient 验证 | `./hx-start.sh --mock` |
| 停服 | `./hx-stop.sh` |

---

## 端口一览

| 原始程序 | ET10 Scene | 端口 | 说明 |
|---|---|---|---|
| LoginServer.exe | Realm | 1600 | 登录验证 + 角色持久化 (MongoDB) |
| MmorpgGameServer (赤水) | Gate1 | 1607 | 游戏网关，TCP Adapter 握手 + 加密帧 |
| MmorpgGameServer (雷炎) | Gate2 | 1602 | 第二区网关，架构同 Gate1 |
| CenterServer.exe | 暂未对接 | - | 部落/婚姻/投票中心服 |

---

## ET10 华夏代码结构

```
ET10/
├── hx-start.sh / hx-stop.sh          ← 启动/停服脚本
├── Packages/
│   ├── cn.etetet.huaxia/            ← ★ 华夏专属包 (git track)
│   │   ├── MockClient/
│   │   │   ├── MockClient.csproj     独立 console app, 跨平台
│   │   │   └── Program.cs            TCP + StreamEnc + opcode 全链路
│   │   ├── Scripts/
│   │   │   ├── Model/Server/Huaxia/
│   │   │   │   ├── HX_StreamEnc.cs   DataLink 逆向后的 32-bit 块加密
│   │   │   │   ├── HX_Login.cs       opcode 定义 (CMD_LOGIN=1 等)
│   │   │   │   ├── Account.cs        MongoDB 账户实体
│   │   │   │   ├── Actor.cs          MongoDB 角色实体
│   │   │   │   ├── HX_Gate.cs        Gate 会话状态
│   │   │   │   ├── HX_TcpAdapterComponent.cs
│   │   │   │   └── HuaxiaActorComponent.cs
│   │   │   └── Hotfix/Server/
│   │   │       ├── TcpAdapter/HX_TcpAdapter.cs   ★ 核心 — 握手+帧解析+路由
│   │   │       ├── Gate/HX_C2G_SelectActorHandler.cs
│   │   │       ├── Realm/HX_C2R_*.cs  角色 CRUD Handler
│   │   │       └── Map/ActorDirty_*.cs 位置/数值变化广播
│   │   └── package.json              ← MainPackage 依赖声明
│   │
│   ├── cn.etetet.login/              ← 原生 ET 登录包 (已改 FiberInit)
│   │   └── Scripts/Hotfix/Server/
│   │       ├── Gate/FiberInit_Gate.cs    Gate 启动: TCP Adapter + MongoDB + 端到端验证
│   │       └── Realm/FiberInit_Realm.cs Realm 启动: MongoDB + 角色持久化验证
│   │
│   ├── cn.etetet.statesync/         ← ET10 主场景 (MainPackage)
│   ├── cn.etetet.core/              ← 已改: UnityMathematics NuGet
│   └── ... (其他原生包)
└── MainPackage.txt                   ← pwsh Initialize-Project.ps1 生成
```

### ET10 编译链（极其重要）

```
Model.csproj → Hotfix.csproj → App.csproj
```

- **Hotfix.dll 不会被 App 自动编译！** 必须单独 `dotnet build Hotfix.csproj`
- 每个阶段都靠 `MainPackage.txt` 决定包含哪些包的源码
- 新增包后先跑 `pwsh Scripts/Initialize-Project.ps1 -SceneName StateSync -GenerateMainPackageOnly`

### ET10 SourceGenerator 约束（必须遵守）

| 规则 | 说明 |
|---|---|
| ET0004 | Hotfix 层类必须 `static`，无实例字段 |
| ET0005 | Hotfix 层非 static 类必须 `[EnableClass]` |
| ET0015 | Model 层禁止 static 非 const 字段 |
| Hotfix 命名 | `[EntitySystemOf(typeof(XXXComponent))]` + partial class |

---

## StreamEnc 加密（DataLink.dll 逆向）

### 种子对应（方向规则，极其重要）

| 方向 | 加密方种子 | 解密方种子 |
|---|---|---|
| **客户端发 → 服务端收** | `RECV_SEED` (8B) `{0x17,0x51,0x71,0x00,0x68,0x31,0x01,0x00}` | 服务端 `recvEnc` 用同 RECV_SEED |
| **服务端发 → 客户端收** | `SEND_SEED` (4B) `{0x13,0x51,0x02,0x19}` | 客户端 `Decrypt` 用同 SEND_SEED |

- **KEY** = `0x00013168`
- 算法：`Encrypt = ROR32(block, rot) XOR key`；`Decrypt = ROL32(cipher XOR key, rot)`
- x86 ror/rol 自动 mod 32/8，C# 必须手动 `rot &= 31/7`
- 每帧独立 `Reset()`，**不是流密码**

### 二进制帧格式

```
[2B LE bodyLen][4B LE origLen][2B LE enableEnc][StreamEnc Encrypt(opcode + payload)]
enableEnc = 0x0001 启用加密, 0x0000 明文
```

---

## 连接生命周期

```
原始客户端               HX_TcpAdapter (Gate)         Realm (1600)
    │  TCP connect :1607
    │ ──────────────────►│
    │  "Reqest connect : "
    │ ──────────────────►│
    │  "Server reply Reqest connect, other data : AccountId=xxx|Roles=???"
    │ ◄──────────────────│
    │  "Reqest authority : szUserName=xxx&szPassword=xxx"
    │ ──────────────────►│──── TCP inner → Realm 验证
    │  "Server reply Reqest authority : [CMD_LOGIN, CMD_USER, ...]"
    │ ◄──────────────────│
    │                     │ ✅ Phase 1 完成, 进入 Phase 2
    │  [加密帧][opcode=0x0001][payload]
    │ ──────────────────►│──── StreamEnc Decrypt → opcode 路由
    │  [加密帧][opcode=0x8000][payload]
    │ ◄──────────────────│──── StreamEnc Encrypt → sendEnc 回包
    │                     │
```

---

## 数据库

```bash
docker run -d --name hx-mongodb -p 27017:27017 mongo:7
# DB 名: ET0_zone_0000 (不是 ET0!)
# Collections: Account, Actor
```

服务端首次启动时 `FiberInit_Realm` 会自动创建测试账号和角色并验证 CRUD。

---

## 路线图

### ✅ Phase 1 — 协议基础（已完成）
- [x] StreamEnc 加解密算法逆向
- [x] Phase 1 文本握手状态机
- [x] Phase 2 加密帧解析 + opcode 路由
- [x] 跨平台 MockClient（macOS 原生）
- [x] 5 opcode 双向通信跑通
- [x] MongoDB 账户 + 角色持久化
- [x] 编译链 + 启动脚本落地

### 🟡 Phase 2 — 真实业务路由（下一步）
- [ ] `CMD_LOGIN(1)` → Realm 正式登录流程，AccountId 传到 Gate
- [ ] `CMD_USER(3)` → MongoDB 查角色列表返回
- [ ] `CMD_GAME(0x8000)` → Map 层 Unit.Move（坐标解析 + 广播）
- [ ] `CMD_CHAT(4)` → Map 层聊天广播
- [ ] `CMD_THING(0x10)` → Inventory Handler

### 🔵 Phase 3 — 真实客户端联调
- [ ] Windows 上运行华夏原始客户端
- [ ] tcpdump/Wireshark 抓真实报文
- [ ] 对照 MockClient 校准 seed / opcode / 帧格式
- [ ] 确认原始客户端加密后 opcode 是否与逆向一致

### ⚪ Phase 4 — 中心服对接
- [ ] CenterServer 协议逆向（部落/婚姻/投票）
- [ ] ET10 Scene 组织 + ServiceDiscovery 注册

---

## 已知坑

| 现象 | 根因 | 解法 |
|---|---|---|
| `ET.Server` 命名空间找不到 | App.csproj 不会自动编译 Hotfix | **必须单独** `dotnet build Hotfix.csproj` |
| 新增包后 Model 编译缺代码 | MainPackage.txt 还是旧的 | 跑 `pwsh Scripts/Initialize-Project.ps1 -SceneName StateSync -GenerateMainPackageOnly` |
| Unity.Mathematics.dll 找不到 | 从 Unity Library 导出的 HintPath | 改用 NuGet `UnityMathematics 1.3.2` |
| MockClient opcode 全是垃圾 (0x0017 0x0036) | 客户端发包用了 SEND_SEED | 改 `Program.cs` 用 `RECV_SEED` 发包 |
| Gate1/Gate2 都绑 1607 | 两个 Gate 端口冲突 | FiberInit 里 `root.Name.Contains("2")` → 1602，否则 1607 |
| 服务端启动后自动退出 | shell 会话被杀后台进程 | 用 `hx-start.sh` 或 `run_in_background` |
| MongoDB 连接慢导致 ServiceDiscovery 超时 | FiberInit 还没等 MongoDB 就注册了 ServiceDiscovery | FiberInit 里加 `WaitAsync(3000~5000)` |
