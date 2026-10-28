#!/usr/bin/env bash
# ============================================================================
# 华夏 ET10 抓包脚本 — 给 Windows 真实客户端联调用
#
# 用法:
#   sudo ./hx-capture.sh                 # 抓 Realm+Gate1+Gate2 存 /tmp/hx-*.pcap
#   sudo ./hx-capture.sh --gate1         # 只抓 Gate1 (1607)
#   sudo ./hx-capture.sh --gate2         # 只抓 Gate2 (1602)
#   sudo ./hx-capture.sh --realm         # 只抓 Realm (1600)
#   sudo ./hx-capture.sh --tcpdump-args "-A -X"   # 透传 tcpdump 参数 (实时看内容)
#   sudo ./hx-capture.sh --iface en1     # 指定网卡 (默认自动检测)
#
# 自动做的事:
#   1. 打印 Mac 局域网 IP (告诉 Windows 填这个)
#   2. 打印 ET10 服务端端口状态 (没启动会提醒)
#   3. 选对网卡 (en0/en1/bridge0 有 IP 的那个)
#   4. Ctrl+C 停抓后显示抓了多少包 + 文件大小 + Wireshark 打开命令
# ============================================================================

set -e

# ---------- 颜色 ----------
R='\033[0;31m'; G='\033[0;32m'; Y='\033[1;33m'; C='\033[0;36m'; N='\033[0m'
ok()  { echo -e "  ${G}✓${N} $*"; }
info(){ echo -e "  ${C}→${N} $*"; }
warn(){ echo -e "  ${Y}!${N} $*"; }
err() { echo -e "  ${R}✗${N} $*"; exit 1; }

# ---------- 参数 ----------
PORTS="1600 or port 1607 or port 1602"
EXTRA_ARGS=""
FORCE_IFACE=""
for arg in "$@"; do
  case $arg in
    --gate1)        PORTS="1607" ;;
    --gate2)        PORTS="1602" ;;
    --realm)        PORTS="1600" ;;
    --all)          PORTS="1600 or port 1607 or port 1602" ;;
    --iface)        FORCE_IFACE="${2:-}"; shift ;;
    --tcpdump-args) EXTRA_ARGS="${2:-}"; shift ;;
    -h|--help)
      head -20 "$0"; exit 0 ;;
  esac
done

# ---------- 0. sudo 检查 ----------
if [ "$EUID" -ne 0 ]; then
  echo ""
  warn "tcpdump 需要 root 权限"
  echo -e "  ${C}→${N} 重新用 sudo 跑:  ${G}sudo $0 $*${N}"
  exit 1
fi

# ---------- 1. 自动检测网卡 ----------
iface_candidates=()
for iface in en0 en1 en2 bridge0; do
  if ip=$(ipconfig getifaddr $iface 2>/dev/null); then
    iface_candidates+=("$iface:$ip")
  fi
done

if [ -z "$FORCE_IFACE" ]; then
  if [ ${#iface_candidates[@]} -eq 0 ]; then
    err "找不到有 IP 的网卡"
  elif [ ${#iface_candidates[@]} -eq 1 ]; then
    IFACE=$(echo "${iface_candidates[0]}" | cut -d: -f1)
    MAC_IP=$(echo "${iface_candidates[0]}" | cut -d: -f2)
  else
    echo ""
    echo "  发现多个网卡, 请选一个:"
    for i in "${!iface_candidates[@]}"; do
      label="${iface_candidates[$i]}"
      echo -e "    ${C}$((i+1))${N}) $label"
    done
    echo -n "  输入编号 [1]: "; read choice
    choice="${choice:-1}"
    chosen="${iface_candidates[$((choice-1))]}"
    IFACE=$(echo "$chosen" | cut -d: -f1)
    MAC_IP=$(echo "$chosen" | cut -d: -f2)
  fi
else
  IFACE="$FORCE_IFACE"
  MAC_IP=$(ipconfig getifaddr $IFACE 2>/dev/null || echo "")
  [ -n "$MAC_IP" ] || err "$FORCE_IFACE 没有 IP"
fi

# ---------- 2. 检查服务端 ----------
echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  华夏 ET10 抓包脚本                                       ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

SERVER_UP=true
for port in 1600 1607 1602; do
  if lsof -iTCP:$port -sTCP:LISTEN -P 2>/dev/null | grep -q LISTEN; then
    ok "端口 $port 监听中"
  else
    warn "端口 $port 未监听 (服务端可能没起)"
    SERVER_UP=false
  fi
done
[ "$SERVER_UP" = true ] || warn "建议先 ./hx-start.sh --quick 起服务端, 抓不到连接的"

echo ""
echo -e "  ${C}📡 网卡:${N} $IFACE"
echo -e "  ${C}🖥️  Mac IP:${N} ${G}${MAC_IP}${N}"
echo -e "  ${C}🚪  端口:${N} $PORTS"
echo ""
info "告诉 Windows 客户端服务器地址填: ${G}${MAC_IP}:1607${N}"

# ---------- 3. 生成输出文件名 ----------
TS=$(date +%Y%m%d-%H%M%S)
CAPTURE_DIR="/tmp/hx-captures"
mkdir -p "$CAPTURE_DIR"

if echo "$PORTS" | grep -q "1600"; then SCENE="all"
elif echo "$PORTS" | grep -q "1607"; then SCENE="gate1"
elif echo "$PORTS" | grep -q "1602"; then SCENE="gate2"
else SCENE="realm"; fi

PCAP_FILE="$CAPTURE_DIR/hx-${SCENE}-${TS}.pcap"

echo -e "  ${C}📁 输出:${N} $PCAP_FILE"
echo ""
echo "  ──────────────────────────────────────────────────────────"
info "开始抓包... 请在 Windows 上启动客户端 / 登录 / 操作"
info "操作完后按 ${Y}Ctrl+C${N} 停抓包"
echo "  ──────────────────────────────────────────────────────────"
echo ""

# ---------- 4. 启动 tcpdump ----------
PKT_COUNT=0
on_interrupt() {
  echo ""
  echo ""
  info "停止抓包..."
  if [ -f "$PCAP_FILE" ]; then
    SIZE=$(du -h "$PCAP_FILE" | cut -f1)
    PKT_COUNT=$(tcpdump -r "$PCAP_FILE" 2>/dev/null | wc -l | tr -d ' ')
    echo ""
    echo "╔══════════════════════════════════════════════════════════╗"
    echo "║  ✅ 抓包完成                                              ║"
    echo "╠══════════════════════════════════════════════════════════╣"
    echo -e "║  文件:   ${PCAP_FILE}"
    echo -e "║  大小:   ${SIZE}"
    echo -e "║  包数:   ${PKT_COUNT}"
    echo -e "║  网卡:   ${IFACE} (${MAC_IP})"
    echo "╚══════════════════════════════════════════════════════════╝"
    echo ""

    # Wireshark
    if command -v wireshark >/dev/null 2>&1; then
      echo -e "  ${G}直接打开 Wireshark:${N}"
      echo -e "    open -a Wireshark $PCAP_FILE"
    else
      echo -e "  ${Y}建议装 Wireshark:${N} brew install --cask wireshark"
    fi
    echo ""
    # 简单统计
    info "快速一眼 (Phase1 文本握手过滤):"
    tcpdump -r "$PCAP_FILE" -A 2>/dev/null | grep -E "Reqest|Server reply" | head -10 || true
    echo ""
  fi
  exit 0
}
trap on_interrupt SIGINT SIGTERM

tcpdump -i "$IFACE" -w "$PCAP_FILE" $EXTRA_ARGS "(tcp port $PORTS)" 2>&1 || true
