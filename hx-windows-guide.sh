#!/usr/bin/env bash
# ============================================================================
# 给 Windows 客户端的连接指南 — 一键打印 Mac IP + 端口状态 + 排查 checklist
#
# 用法: ./hx-windows-guide.sh
# ============================================================================

C=$'\033[0;36m'; G=$'\033[0;32m'; Y=$'\033[1;33m'; R=$'\033[0;31m'; N=$'\033[0m'

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  华夏 ET10 — Windows 客户端连接指南                       ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# 自动 IP
for iface in en0 en1 bridge0; do
  ip=$(ipconfig getifaddr $iface 2>/dev/null)
  if [ -n "$ip" ]; then
    MAC_IP="$ip"; MAC_IFACE="$iface"; break
  fi
done

echo -e "  ${C}🖥️  Mac 局域网 IP:${N} ${G}${MAC_IP:-未检测到}${N}  (网卡: ${MAC_IFACE:-?})"
echo ""

# 端口状态
echo -e "  ${C}🚪  ET10 端口状态:${N}"
PORT_OK=true
for port in 1600 1607 1602; do
  if lsof -iTCP:$port -sTCP:LISTEN -P 2>/dev/null | grep -q LISTEN; then
    echo -e "    ${G}✓${N} :$port 监听中"
  else
    echo -e "    ${R}✗${N} :$port 未监听"
    PORT_OK=false
  fi
done
echo ""

if [ "$PORT_OK" = false ]; then
  echo -e "  ${Y}⚠  请先在 Mac 上启动服务端:${N}"
  echo -e "    cd /Users/majun/Developer/ET10"
  echo -e "    ./hx-start.sh --quick"
  echo ""
fi

# Windows 端操作步骤
echo -e "  ${C}─────────────── Windows 端操作步骤 ───────────────${N}"
echo ""
echo -e "  ${G}①${N} 找到华夏客户端的服务器地址配置文件"
echo "     常见位置:"
echo "       - 登录器 exe 同目录 .ini / .cfg"
echo "       - 客户端安装目录 server.ini"
echo "       - 登录器 UI 里直接有输入框"
echo ""
echo -e "  ${G}②${N} 把服务器地址改成 Mac IP:"
echo -e "     ${G}${MAC_IP:-<Mac_IP>}${N}:1607"
echo ""
echo -e "  ${G}③${N} PowerShell 连通性测试:"
echo "     Test-NetConnection -ComputerName ${MAC_IP:-<Mac_IP>} -Port 1607"
echo "     (期望 TcpTestSucceeded : True)"
echo ""
echo -e "  ${G}④${N} 启动登录器 → 填账号 → 登录 → 操作"
echo ""

# 排查 checklist
echo -e "  ${C}─────────────── 连不上排查 Checklist ───────────────${N}"
echo ""
echo -e "  ${Y}□${N} Windows PowerShell: ping ${MAC_IP:-<Mac_IP>} → 通不通?"
echo -e "  ${Y}□${N} Mac 系统设置 → 网络 → 防火墙 → 关掉试试"
echo -e "  ${Y}□${N} Mac: lsof -iTCP:1607 -sTCP:LISTEN → 是不是 *:1607?"
echo -e "  ${Y}□${N} 两边同一个 WiFi? 有些路由器开启了 AP 隔离 → 换有线"
echo -e "  ${Y}□${N} 试试 Mac 防火墙命令: sudo /usr/libexec/ApplicationFirewall/socketfilterfw --setglobalstate off"
echo ""
