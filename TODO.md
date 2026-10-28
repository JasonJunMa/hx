# 华夏 MMORPG — 路线图与 TODO

> 跟代码一起维护，每次 commit 更新状态
> 状态：`[x]` 已完成 | `[~]` 进行中 | `[ ]` 待开始 | `[!]` 被阻塞

---

## 📚 文档索引

所有支撑材料，任务里引用 `→ [文档名]`：

| 文档 | 位置 | 内容 |
|---|---|---|
| **协议规范** | `hxserver/docs/华夏_协议规范.md` | 逆向出来的 opcode 表、帧格式、种子方向 |
| **DataLink 分析** | `hxserver/docs/DataLink_Analysis.md` | DataLink.dll 逆向笔记 + 算法推导 |
| **逆向脚本集** | `hxserver/docs/datalink_*.py` (7个) | xor / stream / vtable / disasm / send 验证脚本 |
| **MSSQL 结构** | `hxserver/MudHxCenter_complete.sql` | 中心服 DB 表结构（tUserInfo_Center 等） |
| **游戏服 DB** | `hxserver/MudHx_complete.sql` | 游戏服 DB 表结构 |
| **ET10 架构** | `ET10/HX_README.md` | 编译链、SourceGenerator 约束、启动 FAQ、已知坑 |
| **本文件** | `ET10/TODO.md` | 路线图 + 任务追踪 |

---

## 🗺️ 总体阶段路线

```
Phase 0 逆向盘点 ──► Phase 1 协议基础 ──► Phase 2 服务端业务
     │  (DataLink)         │ (StreamEnc)       │ (真实 Handler)
     └─────────────────────────────────────────┘
                          │
                          ▼
                 Phase 3 客户端联调
                   │ (Windows 真客户端)
                   ▼
                 Phase 4 生产化
                   │ (压测 / 多区 / 中心服)
```

---

## Phase 0：逆向盘点

### 0.1 DataLink.dll 加密算法 ✅

| # | 任务 | 状态 | 验收标准 | 支撑文档 |
|---|---|---|---|---|
| 1 | StreamEnc 32-bit 块加密 | ✅ | `Encrypt = ROR32(xor, rot)` / `Decrypt = ROL32(xor, rot)` 数学等价 | [DataLink_Analysis.md](file:///Users/majun/Developer/hxserver/docs/DataLink_Analysis.md) |
| 2 | KEY 常量提取 | ✅ | `0x00013168` 来自 Ghidra 反汇编 | ghidra_export.py |
| 3 | 种子方向规则 | ✅ | RECV_SEED(8B) 客户端发包 / SEND_SEED(4B) 服务端发包 | [华夏_协议规范.md §4](file:///Users/majun/Developer/hxserver/docs/华夏_协议规范.md) |
| 4 | 帧格式确认 | ✅ | `[bodyLen(2B)][origLen(4B)][enableEnc(2B)][payload]` | [协议规范 §5](file:///Users/majun/Developer/hxserver/docs/华夏_协议规范.md) |

### 0.2 客户端协议枚举

| # | 任务 | 状态 | 验收标准 | 支撑文档 |
|---|---|---|---|---|
| 5 | Phase 1 文本握手 | ✅ | 3 步 Reqest connect → Reqest authority 完整还原 | [协议规范 §2](file:///Users/majun/Developer/hxserver/docs/华夏_协议规范.md) |
| 6 | opcode 表 (主要) | ✅ | CMD_LOGIN=1 / CMD_USER=3 / CMD_CHAT=4 / CMD_THING=0x10 / CMD_SERVER=0x1000 / CMD_C2C=0x2000 / CMD_GAME_ENGING=0x4000 / CMD_GAME=0x8000 / CMD_SWITCH=0x8100 | [协议规范 §3](file:///Users/majun/Developer/hxserver/docs/华夏_协议规范.md) |
| 7 | opcode 子命令细分 | [!] | 逆向 CMD_GAME=0x8000 下的具体子 opcode（Move/Attack/UseSkill...） | DataLink_Analysis.md §子命令 |
| 8 | 全部 opcode 完整枚举 | [!] | 客户端 IL 反编译 或 DataLink vtable 遍历所有 handler | datalink_vtable.py |
| 9 | 各 opcode payload 字段定义 | [!] | 逆向每个 opcode 的参数结构（Login 账号密码长度 / Move 坐标类型...） | 需 Windows 客户端 + IDA |

### 0.3 MSSQL 数据库

| # | 任务 | 状态 | 验收标准 | 支撑文档 |
|---|---|---|---|---|
| 10 | tUserInfo_Center 表 | ✅ | 账号表：szUserName / szPassword / iOpenAccountNum / bBanned | MudHxCenter_complete.sql |
| 11 | 角色表 | [!] | 找出角色 ID / 名字 / 职业 / 等级 / 出生地图 | MudHx_complete.sql 待分析 |
| 12 | 物品表 / 背包结构 | [!] | ItemClassFactory.dll 逆向物品结构 | hxserver/雷炎/ 物品 scp |
| 13 | 技能配置 | [!] | SkillFactory.dll + scp 配置文件 | hxserver/雷炎/Scp/skill.scp |

### Phase 0 完成定义

```
✅ 加密算法数学等价
✅ 种子方向清晰
✅ 主要 opcode 已知
⚠️ 子命令细分缺 (0x8000 下有多少子 opcode?)
⚠️ payload 字段定义缺 (Login 发几个字节?)
⚠️ 完整 opcode 表缺 (客户端到底注册了哪些 handler?)
⚠️ DB 角色表结构缺 (MongoDB 对应 Entity 还得改)
```

---

## Phase 1：协议基础 ✅

**全部完成** — 见各任务状态。

| # | 任务 | 状态 | 验收标准 |
|---|---|---|---|
| 1 | HX_StreamEnc Encrypt/Decrypt | ✅ | 与逆向算法数学一致，C# rot &= 31/7 处理 |
| 2 | HX_TcpAdapter TCP Listener | ✅ | 监听 int port，Accept + 读循环 |
| 3 | Phase 1 文本握手状态机 | ✅ | 3 步 Reqest connect → authority，AccountId 下发 |
| 4 | Phase 2 加密帧解析 | ✅ | bodyLen + enableEnc + StreamEnc Decrypt |
| 5 | enableEnc 明文/加密双模式 | ✅ | enableEnc=0x0000 明文直通 |
| 6 | opcode 路由 switch-case | ✅ | 5 opcode 全回复 ACK_OK |
| 7 | sendEnc 服务端回包 | ✅ | 用 SEND_SEED 加密 |
| 8 | MockClient 跨平台验证 | ✅ | macOS 原生 dotnet 跑，Phase1+2 全链路通过 |
| 9 | Mongo 账户持久化 | ✅ | Account Entity + FiberInit 端到端创建/查询 |
| 10 | Mongo 角色持久化 | ✅ | Actor Entity + CRUD 验证 |
| 11 | Gate 端口按 root.Name 区分 | ✅ | "2"→1602，否则 1607 |
| 12 | 编译链 + MainPackage 依赖 | ✅ | Initialize-Project.ps1 生成，NuGet UnityMathematics 替代 Unity Library |
| 13 | 启动/停服脚本 | ✅ | hx-start.sh / hx-stop.sh 自动 MongoDB + 编译 + 端口验证 |
| 14 | Console 干净，Info 写文件 | ✅ | NLog.config Console Info→Warn，Info 全写文件 |

### Phase 1 完成定义

```
✅ ET10 StateSync 启动: Realm:1600 / Gate1:1607 / Gate2:1602
✅ MockClient → Gate1607: Phase1 握手 + Phase2 加密帧 + 5 opcode 全 ACK
✅ MongoDB: 自动建账号、建角色、CRUD 全链路验证
✅ 编译 Model→Hotfix→App 零错误
✅ macOS arm64 原生，跨平台客户端 MockClient
```

---

## Phase 2：服务端真实业务路由 🟡

当前状态：switch-case ACK_OK → 要换真实 Handler

| # | 任务 | 状态 | 验收标准 | 优先级 | 依赖 |
|---|---|---|---|---|---|
| 1 | **CMD_LOGIN(1) → Realm 正式登录** | [ ] | MockClient 发 LOGIN → Gate 转发 Realm → 查 Mongo Account → 校验密码 → 回 AccountId | P0 | Phase 0.2 #9 payload 字段定义 |
| 2 | **CMD_USER(3) → 角色列表** | [ ] | Gate 收到 AccountId → Mongo 查 Actor[] → 回角色列表 (Name/Class/Level) | P0 | #1 |
| 3 | **SelectActor → Map.Enter** | [ ] | 选角色 → Gate 发 G2Map_EnterMap → Map 创建 Unit → 回出生点 | P0 | #2 + ET10 Transfer 流程 |
| 4 | **CMD_GAME(0x8000) → Unit.Move** | [ ] | 解析坐标 payload → Unit.Move(位置) → 周围 Unit 广播位置更新 | P1 | Phase 0.2 #7 子 opcode |
| 5 | **CMD_CHAT(4) → 聊天广播** | [ ] | Gate 收聊天 → Map 层广播到同 Map 所有 Player | P1 | Map Scene 结构理解 |
| 6 | **CMD_THING(0x10) → Inventory** | [ ] | 物品请求 → Item Handler → 背包增删改查 | P2 | Phase 0.3 #12 物品结构 |
| 7 | **CMD_SERVER(0x1000) 系命** | [ ] | 心跳 / ping / 时间同步 | P1 | - |
| 8 | **GM 命令通道** | [ ] | CMD_MANAGER(5) 或 CMD_SWITCH(0x8100) | P2 | - |

### Phase 2 完成定义（MVP）

```
✅ MockClient: LOGIN → USER → SelectActor → EnterMap 完整跑通
✅ Map 层 Unit.Move 广播到周围 Player
✅ 聊天消息跨 Player 广播
✅ 50 并发压测不掉线
```

---

## Phase 3：真实 Windows 客户端联调 🔵

| # | 任务 | 状态 | 验收标准 | 优先级 | 依赖 |
|---|---|---|---|---|---|
| 1 | **Windows 运行华夏客户端** | [ ] | xx华夏登录器.exe 能启动 → 填 127.0.0.1 服务器地址 → 连接成功 | P0 | 需要一台 Windows 机器 |
| 2 | **tcpdump 抓真实握手包** | [ ] | Wireshark 抓 1607 端口流量，对照 Phase1 文本握手格式 | P0 | #1 |
| 3 | **StreamEnc 种子方向校准** | [ ] | 真实客户端加密发包能否被 ET10 正确 Decrypt | P0 | #2 |
| 4 | **opcode 号与逆向对照** | [ ] | 抓到的 opcode 号与 Phase 0.2 逆向是否一致（客户端可能用了偏移表） | P0 | #2 |
| 5 | **payload 字节级对照** | [ ] | 逆向出来的 payload 字段 vs 真实报文 | P1 | #3+#4 |
| 6 | **完整登录流程端到端** | [ ] | 真实客户端 → ET10 Realm → Gate → Map → 看到角色列表 | P1 | #5 |

### Phase 3 完成定义

```
✅ Wireshark 抓到的真实报文 与 逆向协议规范 逐字节一致
✅ 真实 Windows 客户端登录看到角色列表
✅ 移动/聊天能在 Map 上广播
```

---

## Phase 4：生产化 ⚪

| # | 任务 | 状态 | 验收标准 |
|---|---|---|---|
| 1 | 压测脚本自动化 | [ ] | MockClient 50/100/500 并发压测脚本 + GitHub Actions 每天跑 |
| 2 | 多区部署 (赤水/雷炎) | [ ] | Gate1/Gate2 独立 DB / 独立 Map / ServiceDiscovery 区分 Zone |
| 3 | CenterServer 对接 | [ ] | 部落 / 婚姻 / 投票中心服协议逆向 + Scene 实现 |
| 4 | Redis 缓存层 | [ ] | Account 缓存 / Actor 快照缓存 |
| 5 | 生产级日志 + 监控 | [ ] | NLog ELK / Prometheus + Grafana |
| 6 | Docker Compose 一键部署 | [ ] | mongo + ET10 StateSync 全部容器化 |

---

## 🎯 当前优先（下次回来直接干）

**Phase 0.2 #9 — payload 字段定义**

为什么？Phase 2 所有真实 Handler 都依赖"客户端到底发了几个字节、顺序是什么"。MockClient 现在发的 payload 是手工凑的，不是逆向出来的。

**两种获取路径：**

| 路径 | 条件 | 产出 |
|---|---|---|
| A. 继续逆向 DataLink.dll / ClientSocket.dll | 纯静态 | 推导出 payload struct |
| B. Windows 抓真实报文 | 有 Windows 机器 + 登录器 | 抓到完整字节流，对照 opcode 解析 |

路径 A 可以现在继续（macOS），路径 B 需要一台 Windows。

---

## 📝 任务更新规则

1. **状态标记**：改完代码一起改 TODO.md，commit message 里带上任务编号（如 `feat(#P2-1): CMD_LOGIN Realm 正式登录`）
2. **验收标准**：每条任务的"验收标准"是 Done 的唯一依据，不满足不能勾 ✅
3. **阻塞标记 `[!]`**：被硬件/外部依赖卡住的任务标 `[!]`，写清楚卡在哪
4. **新任务追加**：逆向过程中发现新 opcode / 新表结构，直接在对应 Phase 加行
