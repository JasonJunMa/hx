#!/usr/bin/env bash
# ============================================================================
# 华夏 ET10 pcap 快速分析脚本
#
# 用法:
#   ./hx-analyze.sh /tmp/hx-captures/hx-gate1-xxx.pcap
#   ./hx-analyze.sh                  # 自动分析最新的 pcap
# ============================================================================

C=$'\033[0;36m'; G=$'\033[0;32m'; Y=$'\033[1;33m'; R=$'\033[0;31m'; N=$'\033[0m'

PCAP="${1:-}"
if [ -z "$PCAP" ]; then
  PCAP=$(ls -t /tmp/hx-captures/*.pcap 2>/dev/null | head -1)
  [ -n "$PCAP" ] || { echo "没找到 pcap 文件"; exit 1; }
fi
[ -f "$PCAP" ] || { echo "文件不存在: $PCAP"; exit 1; }

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  pcap 快速分析: $PCAP"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# 1. 基本统计
PKT=$(tcpdump -r "$PCAP" 2>/dev/null | wc -l | tr -d ' ')
SIZE=$(du -h "$PCAP" | cut -f1)
SRC_IPS=$(tcpdump -r "$PCAP" -n 2>/dev/null | awk '{print $3}' | cut -d. -f1-4 | sort -u)
DST_IPS=$(tcpdump -r "$PCAP" -n 2>/dev/null | awk '{print $5}' | cut -d. -f1-4 | sort -u)

echo -e "  ${C}📦 总包数:${N} $PKT"
echo -e "  ${C}📏 大小:${N}   $SIZE"
echo -e "  ${C}🌐 源 IP:${N}  $SRC_IPS"
echo -e "  ${C}🎯 目标 IP:${N} $DST_IPS"
echo ""

# 2. TCP 连接数
echo -e "  ${C}🔗 TCP 连接:${N}"
tcpdump -r "$PCAP" -n 2>/dev/null | grep -E "Flags \[S" | head -5 | sed 's/^/    /'
echo ""

# 3. Phase 1 文本握手
echo -e "  ${C}👋 Phase 1 文本握手 (Reqest connect / authority):${N}"
PHASE1=$(tcpdump -r "$PCAP" -A -s 200 2>/dev/null | grep -E "Reqest|Server reply" || true)
if [ -n "$PHASE1" ]; then
  echo "$PHASE1" | head -20 | sed 's/^/    /'
else
  echo "    (没找到 — 可能客户端直接发加密帧了?)"
fi
echo ""

# 4. 尝试提取 opcode（Phase 2 加密帧解不开，只能看 enableEnc 标志）
echo -e "  ${C}🔐 Phase 2 帧头 (enableEnc 标志):${N}"
tcpdump -r "$PCAP" -X 2>/dev/null | grep -A 3 "enableEnc" | head -10 || true
echo "    (加密帧内部 opcode 需 StreamEnc Decrypt 才能看, 建议用 Wireshark)"
echo ""

# 5. 实时预览小工具
echo -e "  ${C}💡 后续操作:${N}"
echo "    open -a Wireshark $PCAP"
echo ""
