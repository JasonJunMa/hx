#!/usr/bin/env bash
# ============================================================================
# 华夏 MMORPG 服务端停服脚本
# 用法: ./hx-stop.sh
# ============================================================================

R='\033[0;31m'; G='\033[0;32m'; C='\033[0;36m'; N='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PID_FILE="$SCRIPT_DIR/.hxserver.pid"

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  华夏 MMORPG 服务端停服                                   ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

if [ ! -f "$PID_FILE" ]; then
  echo -e "  ${C}→${N} 无 PID 文件, 尝试按端口查找..."
  PIDS=$(lsof -tiTCP:1600 -tiTCP:1607 -tiTCP:1602 -sTCP:LISTEN 2>/dev/null | sort -u)
  if [ -z "$PIDS" ]; then
    echo -e "  ${G}✓${N} 服务端已停止"
    exit 0
  fi
else
  PIDS=$(cat "$PID_FILE")
  rm -f "$PID_FILE"
fi

for PID in $PIDS; do
  if kill -0 "$PID" 2>/dev/null; then
    echo -e "  ${C}→${N} 优雅停止 PID=$PID..."
    kill "$PID" 2>/dev/null || true
  fi
done

sleep 2

for PID in $PIDS; do
  if kill -0 "$PID" 2>/dev/null; then
    echo -e "  ${R}✗${N} 强制结束 PID=$PID..."
    kill -9 "$PID" 2>/dev/null || true
  fi
done

sleep 1

# 验证端口
for port in 1600 1607 1602; do
  if lsof -iTCP:"$port" -sTCP:LISTEN -P 2>/dev/null | grep -q LISTEN; then
    echo -e "  ${R}⚠${N} 端口 $port 仍在监听"
  else
    echo -e "  ${G}✓${N} 端口 $port 已释放"
  fi
done

echo ""
echo -e "  ${G}✅ 华夏服务端已停止${N}"
echo ""
