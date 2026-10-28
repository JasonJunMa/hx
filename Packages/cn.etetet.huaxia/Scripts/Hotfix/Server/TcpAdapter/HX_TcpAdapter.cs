using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ET.Server
{
    using ET;

    // ============================================================
    // 生命周期系统
    // ============================================================
    [EntitySystemOf(typeof(HX_TcpAdapterComponent))]
    public static partial class HX_TcpAdapterComponentSystem
    {
        [EntitySystem]
        private static void Awake(this HX_TcpAdapterComponent self, int port)
        {
            self.Port = port;
            self.Cts = new CancellationTokenSource();
            self.Listener = new TcpListener(IPAddress.Any, port);
            self.Listener.Start();
            Log.Info($"[华夏] TCP Adapter 监听 0.0.0.0:{port} ✅");
            self.RunAcceptLoop().Coroutine();
        }

        [EntitySystem]
        private static void Destroy(this HX_TcpAdapterComponent self)
        {
            self.Cts?.Cancel();
            try { self.Listener?.Stop(); } catch { }
            self.Sessions.Clear();
            Log.Info("[华夏] TCP Adapter 已停止");
        }

        private static async ETTask RunAcceptLoop(this HX_TcpAdapterComponent self)
        {
            var ct = self.Cts.Token;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    TcpClient rawClient = await self.Listener.AcceptTcpClientAsync();
                    string ep = rawClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    Log.Info($"[华夏] 新 TCP 连接: {ep}");

                    self.Sessions[ep] = 1;
                    HX_TcpSession.Handle(self, rawClient, ep, ct).Coroutine();
                }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    if (!ct.IsCancellationRequested)
                        Log.Error($"[华夏] TCP Accept 异常: {e.Message}");
                }
            }
        }
    }

    // ============================================================
    // Opcode 主命令号 (逆向自 MmorpgGameServer.exe FUN_004181e9)
    // ============================================================
    public static class HX_Opcode
    {
        public const ushort CMD_LOGIN       = 1;
        public const ushort CMD_USER        = 3;
        public const ushort CMD_CHAT        = 4;
        public const ushort CMD_MANAGER     = 5;
        public const ushort CMD_THING       = 0x10;
        public const ushort CMD_SERVER      = 0x1000;
        public const ushort CMD_C2C         = 0x2000;
        public const ushort CMD_GAME_ENGING = 0x4000;
        public const ushort CMD_GAME        = 0x8000;
        public const ushort CMD_SWITCH      = 0x8100;

        public static string Name(ushort op) => op switch
        {
            CMD_LOGIN => "CMD_LOGIN",
            CMD_USER => "CMD_USER",
            CMD_CHAT => "CMD_CHAT",
            CMD_MANAGER => "CMD_MANAGER",
            CMD_THING => "CMD_THING",
            CMD_SERVER => "CMD_SERVER",
            CMD_C2C => "CMD_C2C",
            CMD_GAME_ENGING => "CMD_GAME_ENGING",
            CMD_GAME => "CMD_GAME",
            CMD_SWITCH => "CMD_SWITCH",
            _ => $"0x{op:X4}"
        };
    }

    // ============================================================
    // 协议帧工具 (逆向自 DataLink.dll RecvData RVA=0x1d36)
    //
    // [2B LE剩余长度] [4B LE原始包总长度] [2B LE enable加密] [StreamEnc body]
    // body = [2B opcode] [payload...]
    // ============================================================
    public static class HX_Frame
    {
        /// <summary>包头固定 8 字节</summary>
        public const int HeaderSize = 8;

        public static byte[] BuildHeader(int bodyLen, int origLen, bool enableEncrypt)
        {
            byte[] hdr = new byte[HeaderSize];
            BitConverter.GetBytes((ushort)bodyLen).CopyTo(hdr, 0);
            BitConverter.GetBytes((uint)origLen).CopyTo(hdr, 2);
            BitConverter.GetBytes((ushort)(enableEncrypt ? 1 : 0)).CopyTo(hdr, 6);
            return hdr;
        }

        /// <summary>解析包头, out remainingBodyLen / out origLen / out enableEncrypt</summary>
        public static bool TryParseHeader(byte[] data, out int bodyLen, out int origLen, out bool enableEncrypt)
        {
            bodyLen = 0; origLen = 0; enableEncrypt = false;
            if (data.Length < HeaderSize) return false;
            bodyLen = BitConverter.ToUInt16(data, 0);
            origLen = (int)BitConverter.ToUInt32(data, 2);
            enableEncrypt = BitConverter.ToUInt16(data, 6) == 1;
            return true;
        }
    }

    // ============================================================
    // 单连接处理 — 纯静态 (Hotfix ET0004 约束)
    // ============================================================
    public static class HX_TcpSession
    {
        private enum HS { Connecting, Authenticating, Phase2Binary }

        public static async ETTask Handle(HX_TcpAdapterComponent parent, TcpClient tcp, string ep, CancellationToken parentCt)
        {
            try
            {
                HS state = HS.Connecting;
                tcp.NoDelay = true;
                NetworkStream stream = tcp.GetStream();
                byte[] recvBuf = new byte[4096]; // Phase 2 持续复用

                // 握手完成后创建加密器
                HX_StreamEnc sendEnc = null;
                HX_StreamEnc recvEnc = null;
                long accountId = 0;

                while (!parentCt.IsCancellationRequested)
                {
                    if (state == HS.Phase2Binary)
                    {
                        await HandlePhase2(parent, stream, recvBuf, recvEnc, sendEnc, ep, parentCt);
                        break;
                    }

                    // ===== Phase 1: 文本握手 =====
                    string line = await ReadLineAsync(stream, parentCt);
                    if (line == null) return;
                    line = line.Trim();

                    Log.Info($"[华夏→] [{ep}] C→S: {line}");

                    var (reply, nextState, aid) = await HandleHandshakeAsync(parent, state, line);
                    state = nextState;
                    accountId = aid;

                    if (reply != null)
                    {
                        Log.Info($"[华夏←] [{ep}] S→C: {reply}");
                        byte[] replyBytes = Encoding.ASCII.GetBytes(reply + "\r\n");
                        await stream.WriteAsync(replyBytes, 0, replyBytes.Length, parentCt);
                    }

                    if (state == HS.Phase2Binary)
                    {
                        // 握手成功, 创建默认加密器 (Phase 2 开始)
                        // 逆向自 DataLink.dll: Recv seed 8字节, Send seed 4字节
                        sendEnc = new HX_StreamEnc(HX_StreamEnc.DefaultKey,
                            new byte[] { 0x13, 0x51, 0x02, 0x19 });
                        recvEnc = new HX_StreamEnc(HX_StreamEnc.DefaultKey,
                            new byte[] { 0x17, 0x51, 0x71, 0x00, 0x68, 0x31, 0x01, 0x00 });
                        Log.Info($"[华夏] 客户端握手完成: {ep}, AccountId={accountId}, 进入加密二进制阶段");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception e)
            {
                Log.Error($"[华夏] TcpSession[{ep}] 异常: {e.Message}");
            }
            finally
            {
                try { tcp.Close(); } catch { }
                parent.Sessions.TryRemove(ep, out _);
                Log.Info($"[华夏] 客户端断开: {ep}, 会话数={parent.Sessions.Count}");
            }
        }

        // ===== 文本握手状态机 (async 因为要查 DB) =====
        private static async ETTask<(string reply, HS nextState, long accountId)> HandleHandshakeAsync(
            HX_TcpAdapterComponent parent, HS state, string line)
        {
            HS nextState = state;
            long accountId = 0;
            string reply = null;

            if (state == HS.Connecting && line.StartsWith("Reqest connect", StringComparison.OrdinalIgnoreCase))
            {
                nextState = HS.Authenticating;
                reply = "Server reply Reqest connect : ";
            }
            else if (state == HS.Authenticating && line.StartsWith("Request authority", StringComparison.OrdinalIgnoreCase))
            {
                string user = ExtractBetween(line, "User(", ")");
                string pass = ExtractBetween(line, "Password(", ")");
                Log.Info($"[华夏] 客户端验证: User={user}, Password={pass}");

                // ===== 真实 DB 验证 =====
                var (ok, aid, roles) = await VerifyAccountAsync(parent, user, pass);
                if (ok)
                {
                    accountId = aid;
                    nextState = HS.Phase2Binary;

                    string roleList = roles.Count > 0
                        ? string.Join(", ", roles.ConvertAll(r => $"{r.ActorName}(Lv.{r.Level})"))
                        : "无角色";

                    reply = $"Server reply Reqest authority : OK\r\n" +
                            $"Server reply Reqest methods : [CMD_LOGIN(1), CMD_USER(3), CMD_GAME(0x8000), CMD_CHAT(4)]\r\n" +
                            $"Server reply Reqest connect, other data : AccountId={aid}|Roles={roleList}";
                }
                else
                {
                    reply = "Server reply Reqest authority : FAIL";
                }
            }
            else
            {
                Log.Warning($"[华夏] 未知握手行 (state={state}): {line}");
            }

            return (reply, nextState, accountId);
        }

        // ===== 账号验证 + 角色查询 (真实 MongoDB) =====
        private static async ETTask<(bool ok, long accountId, List<Actor> roles)> VerifyAccountAsync(
            HX_TcpAdapterComponent parent, string user, string pass)
        {
            var roles = new List<Actor>();
            try
            {
                DBManagerComponent dbManager = parent.Root().GetComponent<DBManagerComponent>();
                if (dbManager == null)
                {
                    Log.Error("[华夏] DBManagerComponent 未初始化");
                    return (false, 0, roles);
                }
                DBComponent db = dbManager.GetZoneDB(0);

                // 查 Account
                var accounts = await db.Query<Account>(a => a.UserName == user);
                Account account = accounts.Count > 0 ? accounts[0] : null;

                if (account == null)
                {
                    // 测试阶段: 自动创建
                    account = parent.Root().AddChild<Account>();
                    account.UserName = user;
                    account.PasswordHash = pass;
                    account.OpenAccountNum = 1;
                    await db.Save(account);
                    Log.Info($"[华夏] 自动创建测试账号: User={user}, Id={account.Id}");
                }
                else if (account.PasswordHash != pass)
                {
                    Log.Warning($"[华夏] 密码错误: User={user}");
                    account.Dispose();
                    return (false, 0, roles);
                }

                long accountId = account.Id;

                // 查角色 (Actor.UserId 对应 Account.Id)
                var actors = await db.Query<Actor>(a => a.UserId == accountId);
                foreach (var a in actors) roles.Add(a);

                Log.Info($"[华夏] ✅ 账号验证通过: User={user}, AccountId={accountId}, 角色数={roles.Count}");
                return (true, accountId, roles);
            }
            catch (Exception e)
            {
                Log.Error($"[华夏] 账号验证异常: {e.Message}");
                return (false, 0, roles);
            }
        }

        // ============================================================
        // Phase 2: 加密二进制帧解析 (先记录 opcode, 实际路由后续加)
        // ============================================================
        private static async ETTask HandlePhase2(
            HX_TcpAdapterComponent parent, NetworkStream stream, byte[] buf,
            HX_StreamEnc recvEnc, HX_StreamEnc sendEnc,
            string ep, CancellationToken ct)
        {
            // 循环读包头 + body + StreamEnc 解密
            // 当前: 只记日志, 后续 opcode → Proto 路由加在这
            var pending = new MemoryStream(); // 粘包缓冲
            byte[] headerBuf = new byte[HX_Frame.HeaderSize];

            while (!ct.IsCancellationRequested)
            {
                // 1. 先收够 8 字节包头
                int headerNeed = HX_Frame.HeaderSize - (int)pending.Length;
                if (pending.Length < HX_Frame.HeaderSize)
                {
                    int headerRead = await stream.ReadAsync(headerBuf, 0, headerNeed, ct);
                    if (headerRead == 0) return;
                    pending.Write(headerBuf, 0, headerRead);
                }

                // 2. 解析包头
                byte[] header = pending.ToArray();
                if (!HX_Frame.TryParseHeader(header, out int bodyLen, out int origLen, out bool enableEnc))
                    return;

                int totalFrame = HX_Frame.HeaderSize + bodyLen;
                int remaining = totalFrame - (int)pending.Length;

                // 3. 收够完整帧
                if (remaining > 0)
                {
                    byte[] bodyBuf = new byte[remaining];
                    int bodyRead = await stream.ReadAsync(bodyBuf, 0, remaining, ct);
                    if (bodyRead == 0) return;
                    pending.Write(bodyBuf, 0, bodyRead);
                }

                // 4. 拆 body
                byte[] fullFrame = pending.ToArray();
                byte[] body = new byte[bodyLen];
                Array.Copy(fullFrame, HX_Frame.HeaderSize, body, 0, bodyLen);

                // 5. StreamEnc 解密
                byte[] decrypted;
                if (enableEnc && recvEnc != null)
                {
                    recvEnc.Reset();
                    decrypted = recvEnc.Decrypt(body);
                }
                else
                {
                    decrypted = body;
                }

                // 6. 拆 opcode
                if (decrypted.Length >= 2)
                {
                    ushort opcode = BitConverter.ToUInt16(decrypted, 0);
                    string opcodeName = HX_Opcode.Name(opcode);
                    Log.Info($"[华夏→] [{ep}] Phase2: opcode={opcodeName} (0x{opcode:X4}), body={bodyLen}B, origLen={origLen}B, enc={enableEnc}");

                    // ===== 7. opcode → 路由 + sendEnc 回包 =====
                    byte[] replyPayload = null;
                    switch (opcode)
                    {
                        case HX_Opcode.CMD_LOGIN:
                            replyPayload = Encoding.UTF8.GetBytes($"ACK_LOGIN OK");
                            break;
                        case HX_Opcode.CMD_USER:
                            replyPayload = Encoding.UTF8.GetBytes($"ACK_USER OK");
                            break;
                        case HX_Opcode.CMD_CHAT:
                            replyPayload = Encoding.UTF8.GetBytes($"ACK_CHAT OK");
                            break;
                        case HX_Opcode.CMD_GAME:
                            replyPayload = Encoding.UTF8.GetBytes($"ACK_GAME OK");
                            break;
                        case HX_Opcode.CMD_THING:
                            replyPayload = Encoding.UTF8.GetBytes($"ACK_THING OK");
                            break;
                        default:
                            replyPayload = Encoding.UTF8.GetBytes($"ACK_{opcode:X4} UNKNOWN");
                            break;
                    }

                    // 构建回包明文 = opcode(2B) + payload
                    byte[] plainReply = new byte[2 + replyPayload.Length];
                    BitConverter.GetBytes(opcode).CopyTo(plainReply, 0);
                    replyPayload.CopyTo(plainReply, 2);

                    // sendEnc 加密 (每个包独立 Reset)
                    sendEnc.Reset();
                    byte[] encReply = sendEnc.Encrypt(plainReply);

                    // 包头
                    byte[] hdr = HX_Frame.BuildHeader(encReply.Length, plainReply.Length, true);

                    // 发回包
                    byte[] fullReply = new byte[HX_Frame.HeaderSize + encReply.Length];
                    hdr.CopyTo(fullReply, 0);
                    encReply.CopyTo(fullReply, HX_Frame.HeaderSize);
                    await stream.WriteAsync(fullReply, 0, fullReply.Length, ct);

                    Log.Info($"[华夏←] [{ep}] Phase2: 回包 {opcodeName} ({fullReply.Length}B)");
                }
                else
                {
                    Log.Warning($"[华夏→] [{ep}] Phase2: body too short ({decrypted.Length}B)");
                }

                // 8. 清空粘包缓冲 (移除已处理帧)
                pending.SetLength(0);
            }
        }

        // ===== 工具 =====
        private static string ExtractBetween(string s, string a, string b)
        {
            int ai = s.IndexOf(a, StringComparison.OrdinalIgnoreCase);
            if (ai < 0) return "";
            int bi = s.IndexOf(b, ai + a.Length, StringComparison.OrdinalIgnoreCase);
            if (bi < 0) return "";
            return s.Substring(ai + a.Length, bi - ai - a.Length);
        }

        private static async ETTask<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            byte[] buf = new byte[1];
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buf, 0, 1, ct);
                if (n <= 0) return null;
                byte b = buf[0];
                if (b == '\n') break;
                if (b == '\r') continue;
                sb.Append((char)b);
                if (sb.Length > 4096) break;
            }
            return sb.ToString();
        }
    }
}
