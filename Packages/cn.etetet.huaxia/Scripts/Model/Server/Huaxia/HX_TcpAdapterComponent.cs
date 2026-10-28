using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace ET
{
    /// <summary>
    /// 华夏原始客户端 TCP 适配器组件
    /// 挂在 Realm Scene 上, 监听原始 LoginServer 端口 (1600)
    ///
    /// Model 层只放 Entity 类 + 属性, Hotfix 层放 System 方法
    /// </summary>
    [ComponentOf(typeof(Scene))]
    public class HX_TcpAdapterComponent : Entity, IAwake<int>, IDestroy
    {
        public int Port { get; set; }
        public TcpListener Listener { get; set; }
        public CancellationTokenSource Cts { get; set; }
        /// <summary>活跃会话 ep → 标记 (Phase 2 可扩展为 HX_TcpSession 实例)</summary>
        public readonly ConcurrentDictionary<string, int> Sessions = new();
    }
}
