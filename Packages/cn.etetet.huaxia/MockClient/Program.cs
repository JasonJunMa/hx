using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 华夏原始客户端 Mock — 模拟 DataLink.dll 完整协议
///   Phase 1: 文本握手 3 步 (Reqest connect → Request authority)
///   Phase 2: StreamEnc 加密二进制帧 (包头 + Encrypt(opcode+payload))
///
/// 逆向来源: DataLink.dll + ClientSocket.dll (华夏2 客户端 深圳网域 2006)
/// </summary>
class MockClient
{
    // ===== StreamEnc 算法 (和服务端完全一致, Seed 必须和对方对应方向的解密器相同!) =====
    // 服务端 recvEnc (收包解密) 用 RECV_SEED (8字节) — 所以客户端发包加密也必须用 RECV_SEED
    // 服务端 sendEnc (发包加密) 用 SEND_SEED (4字节) — 所以客户端收包解密必须用 SEND_SEED
    private const uint KEY = 0x00013168;
    private static readonly byte[] RECV_SEED = { 0x17, 0x51, 0x71, 0x00, 0x68, 0x31, 0x01, 0x00 }; // 服务端收包解密用
    private static readonly byte[] SEND_SEED = { 0x13, 0x51, 0x02, 0x19 };                          // 服务端发包加密用

    // MockClient 发包 → 服务端收, 种子必须和服务端 recvEnc 相同 → RECV_SEED
    private static byte[] EncryptForServer(byte[] data) => Encrypt(data, RECV_SEED);
    // 服务端发 → MockClient 收, 种子必须和服务端 sendEnc 相同 → SEND_SEED
    private static byte[] DecryptFromServer(byte[] data) => Decrypt(data, SEND_SEED);

    private static int _blockIdx;
    private static uint Ror32(uint v, byte r) { r &= 31; return (v >> r) | (v << (32 - r)); }
    private static uint Rol32(uint v, byte r) { r &= 31; return (v << r) | (v >> (32 - r)); }
    private static byte Ror8(byte v, byte r) { r &= 7;  return (byte)((v >> r) | (v << (8 - r))); }
    private static byte Rol8(byte v, byte r) { r &= 7;  return (byte)((v << r) | (v >> (8 - r))); }

    private static byte[] Encrypt(byte[] data, byte[] seed)
    {
        byte[] outp = new byte[data.Length];
        _blockIdx = 0;
        int i = 0;
        while (i <= data.Length - 4)
        {
            uint blk = BitConverter.ToUInt32(data, i);
            byte rot = seed[_blockIdx % 4];
            BitConverter.GetBytes(Ror32(blk, rot) ^ KEY).CopyTo(outp, i);
            _blockIdx = (_blockIdx + 1) & 0x7FFFFFFF;
            i += 4;
        }
        while (i < data.Length)
        {
            byte rot = seed[_blockIdx % 4];
            outp[i] = (byte)(Ror8(data[i], rot) ^ rot);
            i++;
        }
        return outp;
    }

    private static byte[] Decrypt(byte[] data, byte[] seed)
    {
        byte[] outp = new byte[data.Length];
        _blockIdx = 0;
        int i = 0;
        while (i <= data.Length - 4)
        {
            uint blk = BitConverter.ToUInt32(data, i);
            byte rot = seed[_blockIdx % 4];
            BitConverter.GetBytes(Rol32(blk ^ KEY, rot)).CopyTo(outp, i);
            _blockIdx = (_blockIdx + 1) & 0x7FFFFFFF;
            i += 4;
        }
        while (i < data.Length)
        {
            byte rot = seed[_blockIdx % 4];
            outp[i] = Rol8((byte)(data[i] ^ rot), rot);
            i++;
        }
        return outp;
    }

    // ===== Opcode (逆向自 MmorpgGameServer.exe FUN_004181e9) =====
    private const ushort CMD_LOGIN       = 1;
    private const ushort CMD_USER        = 3;
    private const ushort CMD_CHAT        = 4;
    private const ushort CMD_MANAGER     = 5;
    private const ushort CMD_THING       = 0x10;
    private const ushort CMD_SERVER      = 0x1000;
    private const ushort CMD_C2C         = 0x2000;
    private const ushort CMD_GAME_ENGING = 0x4000;
    private const ushort CMD_GAME        = 0x8000;
    private const ushort CMD_SWITCH      = 0x8100;

    // ===== 协议帧 =====
    // [2B LE bodyLen][4B LE origLen][2B LE enableEnc][Encrypt(opcode+payload)]
    private const int HEADER_SIZE = 8;

    private static byte[] BuildFrame(ushort opcode, byte[] payload, bool encrypt, byte[] seed)
    {
        // body 明文 = opcode(2B) + payload
        byte[] plainBody = new byte[2 + payload.Length];
        BitConverter.GetBytes(opcode).CopyTo(plainBody, 0);
        payload.CopyTo(plainBody, 2);

        // 加密 body
        byte[] body;
        if (encrypt)
            body = Encrypt(plainBody, seed);
        else
            body = plainBody;

        // 包头: bodyLen(2B) + origLen(4B) + enableEnc(2B)
        byte[] hdr = new byte[HEADER_SIZE];
        BitConverter.GetBytes((ushort)body.Length).CopyTo(hdr, 0);
        BitConverter.GetBytes((uint)plainBody.Length).CopyTo(hdr, 2);
        BitConverter.GetBytes((ushort)(encrypt ? 1 : 0)).CopyTo(hdr, 6);

        byte[] frame = new byte[HEADER_SIZE + body.Length];
        hdr.CopyTo(frame, 0);
        body.CopyTo(frame, HEADER_SIZE);
        return frame;
    }

    static async Task<int> Main(string[] args)
    {
        string host = "127.0.0.1";
        int port = 1600;
        string user = "mock_client";
        string pass = "mock_pass";
        int clients = 1;
        int warmupMs = 100;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host": host = args[++i]; break;
                case "--port": port = int.Parse(args[++i]); break;
                case "--user": user = args[++i]; break;
                case "--pass": pass = args[++i]; break;
                case "--clients": clients = int.Parse(args[++i]); break;
            }
        }

        Console.WriteLine($"╔══ MockClient ════════════════════════════════════╗");
        Console.WriteLine($"║  Target:        {host}:{port,-38}");
        Console.WriteLine($"║  Account:        {user} / {pass,-28}");
        Console.WriteLine($"║  Concurrent:     {clients} clients                    ║");
        Console.WriteLine($"║  Encrypt:        StreamEnc (ROR/XOR, key=0x{KEY:X8})  ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════╝\n");

        if (clients <= 1)
        {
            int ok = await RunOneClient(host, port, user, pass, 1);
            return ok == 1 ? 0 : 1;
        }

        // ===== 并发多客户端 =====
        var tasks = new List<Task<int>>();
        for (int i = 1; i <= clients; i++)
        {
            string u = $"{user}_{i}";
            string p = $"{pass}_{i}";
            tasks.Add(RunOneClient(host, port, u, p, i));
            await Task.Delay(warmupMs);
        }
        var results = await Task.WhenAll(tasks);
        int success = results.Count(r => r == 1);
        Console.WriteLine($"\n🎯 并发结果: {success}/{clients} 成功");
        return success == clients ? 0 : 1;
    }

    private static async Task<int> RunOneClient(string host, int port, string user, string pass, int id)
    {
        string tag = $"[Client#{id} {user}]";
        try
        {
            // ===== Phase 0: TCP 连接 =====
            using var tcp = new TcpClient(host, port);
            tcp.NoDelay = true;
            NetworkStream stream = tcp.GetStream();
            Console.WriteLine($"{tag} ✅ TCP 连接 {host}:{port}");

            // ===== Phase 1: 文本握手 =====
            string reply;

            // Step 1: Reqest connect
            await SendLine(stream, "Reqest connect : ");
            reply = await ReadLine(stream);
            if (!reply.Contains("Server reply Reqest connect"))
            {
                Console.WriteLine($"{tag} ❌ Step1 失败: {reply}");
                return 0;
            }
            Console.WriteLine($"{tag} ✅ Step1 回复 connect OK");

            // Step 2: Request authority
            await SendLine(stream, $"Request authority : User({user}) and Password({pass})");
            // 读回三行
            reply = await ReadLine(stream);
            if (!reply.Contains("Server reply Reqest authority : OK"))
            {
                Console.WriteLine($"{tag} ❌ Step2 失败: {reply}");
                return 0;
            }
            Console.WriteLine($"{tag} ✅ Step2 账号验证通过");

            // 读后续 methods + other data (可选, 用于返回角色列表)
            string methodsLine = await ReadLine(stream);
            string otherDataLine = await ReadLine(stream);
            Console.WriteLine($"{tag}   methods:    {methodsLine.Substring(0, Math.Min(methodsLine.Length, 60))}");
            Console.WriteLine($"{tag}   other data: {otherDataLine.Substring(0, Math.Min(otherDataLine.Length, 80))}");

            // ===== Phase 2: StreamEnc 加密二进制帧 =====
            Console.WriteLine($"{tag} 📦 发送加密二进制帧...");

            // 构造多帧测试包
            var frames = new List<(ushort op, string desc, byte[] payload)>
            {
                (CMD_LOGIN,  "登录请求",        Encoding.ASCII.GetBytes($"User={user},Pass={pass}")),
                (CMD_USER,   "角色请求",        Encoding.ASCII.GetBytes("GetCharacterList")),
                (CMD_CHAT,   "聊天消息",        Encoding.ASCII.GetBytes("Hello from MockClient!")),
                (CMD_GAME,   "移动操作",        new byte[] { 0x64, 0x00, 0x65, 0x00, 0x01 }), // X=100,Y=101,Dir=1
                (CMD_THING,  "物品请求",        Encoding.ASCII.GetBytes("Inventory")),
            };

            foreach (var (op, desc, payload) in frames)
            {
                // 发包给服务端 → 必须用 RECV_SEED (服务端 recvEnc 用这个解密)
                byte[] frame = BuildFrame(op, payload, true, RECV_SEED);
                await stream.WriteAsync(frame);

                // 转 hex 打印 (包头 + 加密 body)
                string hex = BitConverter.ToString(frame).Replace("-", "");
                Console.WriteLine($"{tag}   0x{op:X4} {desc,-10} → {frame.Length}B [{hex.Substring(0, Math.Min(hex.Length, 40))}...]");
                _blockIdx = 0; // 每帧独立种子偏移
            }

            // 等 300ms 让服务端把所有回包发过来 (5帧客户端 → 5帧服务端回包)
            await Task.Delay(300);

            // ===== Phase 2 完整回包解析 =====
            int replyCount = 0;
            var pending = new byte[4096];
            int pendingLen = 0;

            // 循环读回包, 200ms 没新数据就退出
            while (true)
            {
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(200);
                try
                {
                    int n = await stream.ReadAsync(pending, pendingLen, pending.Length - pendingLen, cts.Token);
                    if (n <= 0) break;
                    pendingLen += n;
                }
                catch (OperationCanceledException)
                {
                    // 200ms 没数据了, 继续处理已收到的数据, 读完退出
                    break;
                }

                // 处理所有完整帧
                while (pendingLen >= 8)
                {
                    int bodyLen = BitConverter.ToUInt16(pending, 0);
                    int origLen = (int)BitConverter.ToUInt32(pending, 2);
                    bool enableEnc = BitConverter.ToUInt16(pending, 6) == 1;

                    int totalFrame = 8 + bodyLen;
                    if (pendingLen < totalFrame) break; // 不够一帧, 等更多数据

                    // 拆 body
                    byte[] body = new byte[bodyLen];
                    Array.Copy(pending, 8, body, 0, bodyLen);

                    // Decrypt (用 SEND_SEED — 服务端 sendEnc 加密, 客户端用同 seed 解密)
                    byte[] decrypted = enableEnc ? DecryptFromServer(body) : body;

                    if (decrypted.Length >= 2)
                    {
                        ushort rOpcode = BitConverter.ToUInt16(decrypted, 0);
                        string rPayload = Encoding.ASCII.GetString(decrypted, 2, decrypted.Length - 2);
                        replyCount++;
                        Console.WriteLine($"{tag} ← 0x{rOpcode:X4} 回包 #{replyCount}: [{rPayload}]");
                    }

                    // 移除已处理帧
                    Array.Copy(pending, totalFrame, pending, 0, pendingLen - totalFrame);
                    pendingLen -= totalFrame;
                }
            }

            if (replyCount > 0)
                Console.WriteLine($"{tag} 📥 收到 {replyCount} 个服务端回包");
            else
                Console.WriteLine($"{tag}   (服务端暂无回包)");

            Console.WriteLine($"{tag} ✅ Phase 1+2 全链路通过");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{tag} ❌ 异常: {ex.Message}");
            return 0;
        }
    }

    private static async Task SendLine(NetworkStream s, string line)
    {
        byte[] buf = Encoding.ASCII.GetBytes(line + "\r\n");
        await s.WriteAsync(buf);
    }

    private static async Task<string> ReadLine(NetworkStream s)
    {
        var sb = new StringBuilder();
        byte[] b = new byte[1];
        while (true)
        {
            int n = await s.ReadAsync(b);
            if (n <= 0) break;
            if (b[0] == '\n') break;
            if (b[0] == '\r') continue;
            sb.Append((char)b[0]);
        }
        return sb.ToString();
    }
}
